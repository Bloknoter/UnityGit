#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// The preview card under the Changes list (spec §5.1, §5.2): a header with the path and a summary, a note, and the
    /// file's rows — a diff, or a new file's plain content — or its picture or audio player, then its .meta's rows. <see cref="ChangesPanel"/> decides
    /// what to show and calls <see cref="ShowNote"/>, <see cref="ShowLoading"/> or <see cref="Show"/>.
    /// </summary>
    internal sealed class ChangePreviewView : EditorUiCard
    {
        const string RowClass = "unitygit-diff-row";
        const float MarkerWidth = 14f; // mirrors .unitygit-diff-marker's width

        readonly Label _title;
        readonly Label _summary;
        readonly Label _note;
        readonly ScrollView _body;
        readonly VisualElement _file;
        readonly VisualElement _media;
        bool _noteShown;

        // The image or audio shown, if any. A texture decoded here (not an asset) is destroyed when it's replaced.
        Texture? _ownedTexture;
        Image? _image;
        float _imageAspect; // the file's width over height, not the imported texture's
        AudioClip? _clip;
        Button? _play;
        bool _playing;
        IVisualElementScheduledItem? _poll;

        public ChangePreviewView() : base(PreviewText.Heading)
        {
            name = UiNames.ChangesPreview;
            AddToClassList("unitygit-preview-card");

            _title = Header.Q<Label>(className: EditorUiClasses.CardTitle);
            _title.name = UiNames.PreviewTitle;
            _title.AddToClassList("unitygit-preview-title");
            ConflictsPanel.Plain(_title);
            _summary = PanelParts.Text(null, EditorUiClasses.Muted, "unitygit-preview-summary");
            _summary.name = UiNames.PreviewSummary;
            Header.Add(_summary);

            _note = PanelParts.Text(null, EditorUiClasses.Muted, PanelClasses.Note, "unitygit-preview-note");
            _note.name = UiNames.PreviewNote;
            Add(_note);

            _media = new VisualElement();
            _media.AddToClassList("unitygit-preview-media");
            PanelParts.SetDisplayed(_media, false);
            _media.RegisterCallback<GeometryChangedEvent>(_ => FitImage());
            Add(_media);

            _body = new ScrollView(ScrollViewMode.Vertical) { name = UiNames.PreviewBody };
            _body.AddToClassList("unitygit-preview-body");
            _file = new VisualElement { name = UiNames.PreviewFile };
            _file.AddToClassList("unitygit-preview-section");
            _body.Add(_file);
            Add(_body);
            // Closing the window or switching tabs stops the sound; the picture stays for when the tab comes back.
            RegisterCallback<DetachFromPanelEvent>(_ => StopAudio());
            ShowNote(PreviewText.SelectFile);
        }

        /// <summary>The note's text while one is shown, otherwise null.</summary>
        public string? NoteText => _noteShown ? _note.text : null;

        /// <summary>Only a note: nothing selected, several selected, a conflicted row, a failed load.</summary>
        public void ShowNote(string text)
        {
            SetHeader(PreviewText.Heading, string.Empty);
            ClearBody();
            SetNote(text);
        }

        /// <summary>A new file was selected and is being read.</summary>
        public void ShowLoading(string path)
        {
            SetHeader(path, string.Empty);
            ClearBody();
            SetNote(PreviewText.Loading);
        }

        public void Show(ChangePreview preview)
        {
            var file = preview.File;
            SetHeader(PreviewText.Title(file), PreviewText.Summary(file));
            ClearBody();
            if (file.Change != DiffChange.Deleted && ShowMedia(file)) SetNote(null);
            else
            {
                SetNote(PreviewText.Note(file));
                if (NoteText == null) AddRows(_file, file);
            }

            if (preview.Meta is FileDiffView meta)
            {
                var section = new VisualElement { name = UiNames.PreviewMeta };
                section.AddToClassList("unitygit-preview-section");
                section.AddToClassList("unitygit-preview-meta");
                var caption = new VisualElement();
                caption.AddToClassList("unitygit-preview-meta-caption");
                caption.Add(ConflictsPanel.Plain(PanelParts.Text(".meta", EditorUiClasses.Strong, EditorUiClasses.Small)));
                caption.Add(ConflictsPanel.Plain(PanelParts.Text(PreviewText.Summary(meta), EditorUiClasses.Muted, EditorUiClasses.Small)));
                section.Add(caption);
                var note = PreviewText.Note(meta);
                if (note != null) section.Add(PanelParts.Text(note, EditorUiClasses.Muted, EditorUiClasses.Small, "unitygit-preview-meta-note"));
                else AddRows(section, meta);
                _body.Add(section);
            }
        }

        // An image or audio clip Unity recognises replaces the rows (spec §5.2): the current file, no diff.
        bool ShowMedia(FileDiffView file)
        {
            var (kind, assetPath) = PreviewMedia.Detect(file.FullPath);
            if (kind == MediaKind.Image)
            {
                var texture = PreviewMedia.LoadImage(file.FullPath, assetPath, out var width, out var height, out var owned);
                if (texture == null) return false;
                if (owned) _ownedTexture = texture;
                // Unity may import the file at other proportions (a non-power-of-two image scaled to 256×128), so the element
                // takes the file's own proportions and the texture is stretched to fill it.
                _image = new Image { name = UiNames.PreviewImage, image = texture, scaleMode = ScaleMode.StretchToFill };
                _image.AddToClassList("unitygit-preview-image");
                _imageAspect = width > 0 && height > 0 ? (float)width / height : (float)texture.width / Math.Max(1, texture.height);
                _media.Add(_image);
                _media.AddToClassList("unitygit-preview-media--image");
                FitImage(); // when the area is already laid out; otherwise its first layout calls it
                SetSummary(PreviewText.ImageSummary(width, height, file.NewSize));
            }
            else if (kind == MediaKind.Audio && assetPath != null && PreviewMedia.LoadAudio(assetPath) is AudioClip clip)
            {
                _clip = clip;
                var player = new VisualElement();
                player.AddToClassList("unitygit-preview-audio");
                _play = new Button(TogglePlay) { name = UiNames.PreviewAudio, text = "Play" };
                _play.SetEnabled(AudioPreview.IsAvailable);
                _play.tooltip = AudioPreview.IsAvailable ? string.Empty : AudioPreview.Unavailable;
                player.Add(_play);
                player.Add(PanelParts.Text(PreviewText.Length(clip.length), EditorUiClasses.Mono, "unitygit-preview-audio-length"));
                _media.Add(player);
                SetSummary(PreviewText.Length(clip.length) + (file.NewSize is long size ? " · " + PreviewText.Size(size) : string.Empty));
            }
            else return false;

            PanelParts.SetDisplayed(_media, true);
            _body.AddToClassList("unitygit-preview-body--under-media");
            return true;
        }

        // The largest size with the file's proportions that fits the area; the area centres it.
        void FitImage()
        {
            if (_image == null || _imageAspect <= 0) return;
            var area = _media.contentRect;
            if (float.IsNaN(area.width) || float.IsNaN(area.height) || area.width <= 0 || area.height <= 0) return;
            var width = Math.Min(area.width, area.height * _imageAspect);
            _image.style.width = width;
            _image.style.height = width / _imageAspect;
        }

        void TogglePlay()
        {
            if (_playing)
            {
                StopAudio();
                return;
            }
            if (_clip == null || _play == null) return;
            AudioPreview.Play(_clip);
            _playing = true;
            _play.text = "Stop";
            _poll ??= schedule.Execute(() =>
            {
                if (_playing && !AudioPreview.IsPlaying) SetStopped(); // the clip ended
            }).Every(100);
            _poll.Resume();
        }

        void StopAudio()
        {
            if (_playing) AudioPreview.Stop();
            SetStopped();
        }

        void SetStopped()
        {
            _playing = false;
            if (_play != null) _play.text = "Play";
            _poll?.Pause();
        }

        void ReleaseMedia()
        {
            StopAudio();
            _clip = null;
            _play = null;
            if (_ownedTexture != null) UnityEngine.Object.DestroyImmediate(_ownedTexture);
            _ownedTexture = null;
            _image = null;
            _media.RemoveFromClassList("unitygit-preview-media--image");
            _media.Clear();
            PanelParts.SetDisplayed(_media, false);
            _body.RemoveFromClassList("unitygit-preview-body--under-media");
        }

        void SetHeader(string title, string summary)
        {
            Title = title;
            _title.tooltip = title;
            SetSummary(summary);
        }

        void SetSummary(string summary)
        {
            _summary.text = summary;
            PanelParts.SetDisplayed(_summary, summary.Length > 0);
        }

        void SetNote(string? text)
        {
            _noteShown = text != null;
            _note.text = text ?? string.Empty;
            PanelParts.SetDisplayed(_note, _noteShown);
        }

        void ClearBody()
        {
            ReleaseMedia();
            _file.Clear();
            _body.Q(UiNames.PreviewMeta)?.RemoveFromHierarchy();
            _body.scrollOffset = UnityEngine.Vector2.zero;
        }

        // The rows of one file: old and new numbers, a marker and the text for a diff; one number and the text for a new
        // file's content (no diff, spec §5.2). Then how many lines the cap left out.
        static void AddRows(VisualElement parent, FileDiffView v)
        {
            var content = v.Change == DiffChange.Added;
            var largest = 1;
            foreach (var row in v.Rows) largest = Math.Max(largest, Math.Max(row.OldLine ?? 0, row.NewLine ?? 0));
            var gutter = ConflictsPanel.GutterWidth(largest);
            var textStart = content ? gutter : 2 * gutter + MarkerWidth;

            foreach (var row in v.Rows)
            {
                if (row.Kind == DiffRowKind.Gap)
                {
                    var gap = PanelParts.Text(ConflictText.Unchanged(row.GapLines), EditorUiClasses.Faint, EditorUiClasses.Small, "unitygit-diff-gap");
                    gap.style.marginLeft = textStart; // in line with the text, not the numbers
                    parent.Add(gap);
                    continue;
                }
                var element = new VisualElement();
                element.AddToClassList(RowClass);
                element.AddToClassList(RowClass + "--" + (content ? "content" : KindName(row.Kind)));
                if (content) element.Add(Number(row.NewLine, gutter));
                else
                {
                    element.Add(Number(row.OldLine, gutter));
                    element.Add(Number(row.NewLine, gutter));
                    var marker = ConflictsPanel.Plain(PanelParts.Text(Marker(row.Kind), EditorUiClasses.Mono, "unitygit-diff-marker"));
                    marker.pickingMode = PickingMode.Ignore;
                    element.Add(marker);
                }
                element.Add(ConflictsPanel.FileText(ConflictText.CutLine(row.Text), "unitygit-diff-text"));
                parent.Add(element);
            }
            if (v.HiddenLines > 0)
            {
                var more = PanelParts.Text(ConflictText.MoreLines(v.HiddenLines), EditorUiClasses.Muted, EditorUiClasses.Small, "unitygit-diff-more");
                more.style.marginLeft = textStart;
                parent.Add(more);
            }
        }

        static Label Number(int? line, float width)
        {
            var number = PanelParts.Text(line is int n ? PanelParts.Count(n) : string.Empty, EditorUiClasses.Mono,
                "unitygit-conflict-line-number", "unitygit-diff-line-number");
            number.pickingMode = PickingMode.Ignore;
            number.style.width = width;
            return number;
        }

        static string KindName(DiffRowKind kind)
            => kind == DiffRowKind.Added ? "added" : kind == DiffRowKind.Removed ? "removed" : "unchanged";

        static string Marker(DiffRowKind kind)
            => kind == DiffRowKind.Added ? "+" : kind == DiffRowKind.Removed ? "-" : " ";
    }
}
