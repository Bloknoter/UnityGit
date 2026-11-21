#nullable enable
using System;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// The preview card under the Changes list (spec §5.1, §5.2): a header with the path and a summary, a note, and the
    /// file's rows — a diff, or a new file's plain content — then its .meta's rows. <see cref="ChangesPanel"/> decides
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
        bool _noteShown;

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

            _body = new ScrollView(ScrollViewMode.Vertical) { name = UiNames.PreviewBody };
            _body.AddToClassList("unitygit-preview-body");
            _file = new VisualElement { name = UiNames.PreviewFile };
            _file.AddToClassList("unitygit-preview-section");
            _body.Add(_file);
            Add(_body);
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
            SetNote(PreviewText.Note(file));
            if (NoteText == null) AddRows(_file, file);

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

        void SetHeader(string title, string summary)
        {
            Title = title;
            _title.tooltip = title;
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
