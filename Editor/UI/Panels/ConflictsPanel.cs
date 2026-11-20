#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// The Conflicts tab (spec §5.1): a header card with what is in progress and the buttons that finish or abort it, the
    /// conflicted files, and the selected file's card with its places. Picks are a preview, kept in memory until Apply
    /// writes them. The list comes from the snapshot at once; what git says about each file (the overview) and the selected
    /// file are read by <see cref="ReloadAsync"/>, which a render starts whenever the snapshot is new.
    /// </summary>
    internal sealed class ConflictsPanel : VisualElement, IRenderablePanel
    {
        /// <summary>Marks the option button that matches the pending pick.</summary>
        internal const string PickedClass = "unitygit-conflict-option--picked";

        /// <summary>The four options, in the order their buttons appear.</summary>
        internal static readonly ConflictPick[] AllPicks = { ConflictPick.Ours, ConflictPick.Theirs, ConflictPick.OursThenTheirs, ConflictPick.TheirsThenOurs };

        readonly GitSession _session;

        readonly Label _title;
        readonly Label _summary;
        readonly Label _note;
        readonly Button _abort;
        readonly Button _continue;

        readonly RowList<FileRow> _list;
        IReadOnlyList<string> _files = Array.Empty<string>();
        Dictionary<string, ConflictFileInfo> _infos = new Dictionary<string, ConflictFileInfo>(StringComparer.Ordinal);

        readonly EditorUiCard _fileCard;
        readonly Label _fileTitle;
        readonly Label _fileType;
        readonly VisualElement _fileNote;
        readonly VisualElement _sceneWarning;
        readonly VisualElement _wholeFile;
        readonly VisualElement _wholeFileButtons;
        readonly ScrollView _places;
        readonly List<ConflictPlaceView> _placeViews = new List<ConflictPlaceView>();
        Button? _useOurs;
        Button? _useTheirs;
        readonly Button _openEditor;
        readonly Button _markResolved;
        readonly Button _startOver;
        readonly Button _apply;

        // Pending picks by path. In memory only: a domain reload loses them (spec §3 non-goal).
        readonly Dictionary<string, FilePicks> _picks = new Dictionary<string, FilePicks>(StringComparer.Ordinal);

        RepoSnapshot? _loadedSnapshot;
        Task _reload = Task.CompletedTask;
        bool _reloading;
        bool _reloadQueued;

        public ConflictsPanel(GitSession session)
        {
            _session = session;
            AddToClassList(PanelClasses.Content);

            var header = EditorUiElements.Card();
            header.AddToClassList("unitygit-conflicts-header");
            var headline = new VisualElement();
            headline.AddToClassList("unitygit-conflicts-headline");
            _title = Named(PanelParts.Text(null, EditorUiClasses.Strong, "unitygit-conflicts-title"), UiNames.ConflictsTitle);
            _summary = Named(PanelParts.Text(null, EditorUiClasses.Muted, "unitygit-conflicts-summary"), UiNames.ConflictsSummary);
            _abort = new Button(() => _ = Observe(FinishAsync(_session.AbortAsync))) { name = UiNames.ConflictsAbort };
            _continue = new Button(() => _ = Observe(FinishAsync(_session.ContinueAsync))) { name = UiNames.ConflictsContinue };
            headline.Add(_title);
            headline.Add(_summary);
            headline.Add(_abort);
            headline.Add(_continue);
            header.Add(headline);
            _note = Named(PanelParts.Text(null, EditorUiClasses.Muted, "unitygit-conflicts-note"), UiNames.ConflictsNote);
            header.Add(_note);
            Add(header);

            _list = new RowList<FileRow>(UiNames.ConflictsFiles, () => new FileRowView(), FileRowView.Bind, FileRow.Key);
            _list.View.selectedIndicesChanged += _ => OnFileClicked();
            var listCard = _list.InCard(null);
            listCard.AddToClassList("unitygit-conflicts-files-card");
            Add(listCard);

            _fileCard = EditorUiElements.Card();
            _fileCard.AddToClassList(PanelClasses.Fill);
            _fileCard.AddToClassList("unitygit-conflict-file");
            _fileTitle = Named(Plain(PanelParts.Text(null, EditorUiClasses.Strong, PanelClasses.Ellipsis, PanelClasses.Path, "unitygit-conflict-file-title")),
                UiNames.ConflictFileTitle);
            _fileType = Named(PanelParts.Text(null, EditorUiClasses.Muted, "unitygit-conflict-file-type"), UiNames.ConflictFileType);
            _fileNote = EditorUiElements.Message(string.Empty, EditorUiTone.Info);
            Named(MessageLabel(_fileNote), UiNames.ConflictFileNote);
            _sceneWarning = EditorUiElements.Message(
                "Scenes and prefabs can break when places are picked one by one. Check the file opens in Unity before you finish.",
                EditorUiTone.Warning);
            Named(MessageLabel(_sceneWarning), UiNames.ConflictSceneWarning);
            _wholeFile = new VisualElement { name = UiNames.ConflictWholeFile };
            _wholeFile.AddToClassList("unitygit-conflict-whole-file");
            _wholeFile.Add(PanelParts.Text("Whole file:", EditorUiClasses.Muted, "unitygit-conflict-whole-file-caption"));
            _wholeFileButtons = new VisualElement();
            _wholeFileButtons.AddToClassList("unitygit-conflict-options");
            _wholeFile.Add(_wholeFileButtons);
            _places = new ScrollView(ScrollViewMode.Vertical) { name = UiNames.ConflictPlaces, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _places.AddToClassList("unitygit-conflict-places");

            _openEditor = new Button(OpenInEditor) { name = UiNames.ConflictOpenEditor, text = "Open in editor" };
            _markResolved = new Button(() => _ = Observe(MarkResolvedAsync())) { name = UiNames.ConflictMarkResolved, text = "Mark resolved" };
            _startOver = new Button(() => _ = Observe(StartOverAsync())) { name = UiNames.ConflictStartOver, text = "Start over" };
            _apply = new Button(() => _ = Observe(ApplyAsync())) { name = UiNames.ConflictApply, text = "Apply" };
            _apply.AddToClassList(EditorUiClasses.ButtonPrimary);
            var actions = new VisualElement();
            actions.AddToClassList("unitygit-conflict-actions");
            actions.Add(_openEditor);
            actions.Add(_markResolved);
            actions.Add(_startOver);
            var spacer = new VisualElement();
            spacer.AddToClassList(EditorUiClasses.Grow);
            actions.Add(spacer);
            actions.Add(_apply);

            _fileCard.Add(_fileTitle);
            _fileCard.Add(_fileType);
            _fileCard.Add(_fileNote);
            _fileCard.Add(_sceneWarning);
            _fileCard.Add(_wholeFile);
            _fileCard.Add(_places);
            _fileCard.Add(actions);
            Add(_fileCard);
            Render();
        }

        /// <summary>The conflicted paths listed, from the snapshot.</summary>
        internal IReadOnlyList<string> Files => _files;

        internal string? SelectedFile { get; private set; }

        /// <summary>The selected file as last read; null while none is selected or it couldn't be read.</summary>
        internal ConflictFileView? File { get; private set; }

        /// <summary>The selected place's index in <see cref="ConflictFileView.Places"/>; null when the file has none.</summary>
        internal int? SelectedPlace { get; private set; }

        /// <summary>The pending pick for place <paramref name="index"/> of the selected file.</summary>
        internal ConflictPick? PickOf(int index)
            => File != null && _picks.TryGetValue(File.Info.Path, out var picks) && picks.ByPlace.TryGetValue(index, out var pick)
                ? pick
                : (ConflictPick?)null;

        /// <summary>The side picked for a selected file without places: true for ours, false for theirs, null for none.</summary>
        internal bool? SidePick => File != null && _picks.TryGetValue(File.Info.Path, out var picks) ? picks.Side : null;

        public void Render()
        {
            var s = _session.Snapshot;
            RenderHeader(s);
            RenderList(s);
            RenderActions();
            if (!ReferenceEquals(s, _loadedSnapshot)) _ = ReloadAsync(); // the reload logs its own failures
        }

        /// <summary>
        /// Reads the overview (what each listed file is) and the selected file, keeping the selection by path or selecting
        /// the first file. One reload runs at a time: a request made during one queues exactly one more, and the task
        /// returned completes when that one has finished too. A failed reload is logged and leaves the panel as it was.
        /// </summary>
        internal Task ReloadAsync()
        {
            if (_reloading)
            {
                _reloadQueued = true;
                return _reload;
            }
            _reloading = true;
            return _reload = ReloadLoopAsync();
        }

        internal async Task SelectFileAsync(string path)
        {
            SelectedFile = path;
            ShowListSelection();
            var view = await _session.LoadConflictFileAsync(path);
            if (SelectedFile != path) return; // the user picked another file meanwhile
            ShowFile(view, keepPlace: false);
            Render();
        }

        internal void SelectPlace(int index)
        {
            if (File == null || index < 0 || index >= File.Places.Count || SelectedPlace == index) return;
            SelectedPlace = index;
            ShowPicks();
        }

        /// <summary>
        /// Picks for the selected place, then selects the next place without a pick after it, wrapping round. When every
        /// place has a pick the selection stays.
        /// </summary>
        internal void Pick(ConflictPick pick)
        {
            var file = File;
            if (file == null || SelectedPlace is not int place) return;
            PicksFor(file).ByPlace[place] = pick;
            var next = NextWithoutPick(file, place);
            SelectedPlace = next ?? place;
            ShowPicks();
            if (next != null) ScrollToSelectedPlace();
        }

        internal void ClearPick()
        {
            var file = File;
            if (file == null || SelectedPlace is not int place) return;
            if (_picks.TryGetValue(file.Info.Path, out var picks) && picks.ByPlace.Remove(place)) ShowPicks();
        }

        /// <summary>The whole-file buttons of a file with places: the same pick for every place. Still a preview.</summary>
        internal void PickAll(ConflictPick pick)
        {
            var file = File;
            if (file == null || file.Places.Count == 0) return;
            var picks = PicksFor(file);
            foreach (var place in file.Places) picks.ByPlace[place.Index] = pick;
            ShowPicks();
        }

        /// <summary>The whole-file buttons of a file without places: that side's whole file, on Apply.</summary>
        internal void PickSide(bool ours)
        {
            var file = File;
            if (file == null || file.Places.Count > 0 || HandResolved(file.Info)) return;
            PicksFor(file).Side = ours;
            ShowPicks();
        }

        /// <summary>Writes the picks: the picked places of a file with places, or the picked side's whole file.</summary>
        internal async Task<ActionOutcome> ApplyAsync()
        {
            var file = File ?? throw new InvalidOperationException("No conflicted file is selected.");
            _picks.TryGetValue(file.Info.Path, out var picks);
            // A copy: the picks can change while the action runs.
            var outcome = file.Places.Count == 0 && picks?.Side is bool ours
                ? await _session.UseSideAsync(file, ours)
                : await _session.ApplyPicksAsync(file, picks != null ? new Dictionary<int, ConflictPick>(picks.ByPlace) : new Dictionary<int, ConflictPick>());
            return await AfterActionAsync(file.Info.Path, outcome);
        }

        internal async Task<ActionOutcome> MarkResolvedAsync()
        {
            var path = (File ?? throw new InvalidOperationException("No conflicted file is selected.")).Info.Path;
            return await AfterActionAsync(path, await _session.MarkResolvedAsync(path));
        }

        /// <summary>Puts the file's conflicts back. The session asks first.</summary>
        internal async Task<ActionOutcome> StartOverAsync()
        {
            var path = (File ?? throw new InvalidOperationException("No conflicted file is selected.")).Info.Path;
            return await AfterActionAsync(path, await _session.RestoreConflictAsync(path));
        }

        internal static string TypeBadge(ConflictType type)
        {
            switch (type)
            {
                case ConflictType.BothAdded: return "both added";
                case ConflictType.BothDeleted: return "both deleted";
                case ConflictType.AddedByUs: return "added by us";
                case ConflictType.AddedByThem: return "added by them";
                case ConflictType.DeletedByUs: return "deleted by us";
                case ConflictType.DeletedByThem: return "deleted by them";
                default: return "both changed";
            }
        }

        /// <summary>The file list's chip: how many places, "binary", or "no places".</summary>
        internal static string Chip(ConflictFileInfo info)
            => info.PlaceCount > 0 ? PanelParts.Places(info.PlaceCount) : info.IsBinary ? "binary" : "no places";

        internal static string TypeText(ConflictType type, string ours, string theirs)
        {
            switch (type)
            {
                case ConflictType.BothAdded: return "Added on both sides";
                case ConflictType.BothDeleted: return "Deleted on both sides";
                case ConflictType.AddedByUs: return $"Added by {ours}";
                case ConflictType.AddedByThem: return $"Added by {theirs}";
                case ConflictType.DeletedByUs: return $"Deleted by {ours}, changed by {theirs}";
                case ConflictType.DeletedByThem: return $"Changed by {ours}, deleted by {theirs}";
                default: return "Changed on both sides";
            }
        }

        // Every await stays on the main thread (no ConfigureAwait(false)), so the elements are only touched there.
        async Task ReloadLoopAsync()
        {
            try
            {
                do
                {
                    _reloadQueued = false;
                    try
                    {
                        await LoadAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                } while (_reloadQueued);
            }
            finally
            {
                _reloading = false;
            }
        }

        async Task LoadAsync()
        {
            var snapshot = _session.Snapshot;
            _loadedSnapshot = snapshot;
            var overview = await _session.LoadConflictOverviewAsync();
            if (overview != null) _infos = overview.Files.ToDictionary(f => f.Path, StringComparer.Ordinal); // else keep the last

            var listed = snapshot.ConflictedPaths;
            foreach (var gone in _picks.Keys.Where(p => !listed.Contains(p)).ToList()) _picks.Remove(gone);
            var path = SelectedFile != null && listed.Contains(SelectedFile) ? SelectedFile : listed.FirstOrDefault();
            SelectedFile = path;
            var view = path != null ? await _session.LoadConflictFileAsync(path) : null;
            if (SelectedFile == path) ShowFile(view, keepPlace: true); // unless the user picked another file meanwhile
            Render();
        }

        // Apply, Mark resolved and Start over: a success spends the file's picks. Then the panel shows what git left, also
        // after a refusal (a file that changed since it was shown then shows its new content).
        async Task<ActionOutcome> AfterActionAsync(string path, ActionOutcome outcome)
        {
            if (outcome.Succeeded) _picks.Remove(path);
            await ReloadAsync();
            ShowPicks();
            return outcome;
        }

        // Continue and Abort: the session asks and reports; the panel then shows what is left (a rebase may stop again).
        async Task FinishAsync(Func<Task<ActionOutcome>> action)
        {
            await action();
            await ReloadAsync();
        }

        // Shows a newly read view of the selected file. Picks survive only when the file's places are the ones they were
        // made on; the selected place survives a reload of an unchanged file.
        void ShowFile(ConflictFileView? view, bool keepPlace)
        {
            var old = File;
            File = view;
            if (view == null)
            {
                SelectedPlace = null;
                if (old != null) BuildFile();
                return;
            }

            var path = view.Info.Path;
            if (_picks.TryGetValue(path, out var picks))
            {
                if (SamePlaces(picks.Places, view.Places)) picks.Places = view.Places;
                else _picks.Remove(path);
            }
            var unchanged = old != null && old.Info.Path == path && SamePlaces(old.Places, view.Places);
            if (!keepPlace || !unchanged) SelectedPlace = view.Places.Count > 0 ? 0 : (int?)null;
            if (old == null || !SameContent(old, view)) BuildFile(); // a refresh that finds the same file keeps its elements
            ShowPicks();
        }

        static bool SamePlaces(IReadOnlyList<ConflictPlace> a, IReadOnlyList<ConflictPlace> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (a[i].StartLine != b[i].StartLine || a[i].Ours != b[i].Ours || a[i].Theirs != b[i].Theirs) return false;
            return true;
        }

        // Everything BuildFile shows.
        static bool SameContent(ConflictFileView a, ConflictFileView b)
        {
            if (a.Info.Path != b.Info.Path || a.Info.Type != b.Info.Type || a.Info.IsBinary != b.Info.IsBinary
                || a.Info.ExistsInWorkingTree != b.Info.ExistsInWorkingTree || a.Info.HasMalformedMarkers != b.Info.HasMalformedMarkers
                || a.OursName != b.OursName || a.TheirsName != b.TheirsName || a.FullPath != b.FullPath || a.Parts.Count != b.Parts.Count)
                return false;
            for (var i = 0; i < a.Parts.Count; i++)
            {
                var (pa, pb) = (a.Parts[i].Place, b.Parts[i].Place);
                if (pa == null || pb == null)
                {
                    if (pa != pb || a.Parts[i].Text != b.Parts[i].Text) return false;
                }
                else if (pa.StartLine != pb.StartLine || pa.EndLine != pb.EndLine || pa.Ours != pb.Ours || pa.Theirs != pb.Theirs || pa.Base != pb.Base)
                    return false;
            }
            return true;
        }

        FilePicks PicksFor(ConflictFileView file)
        {
            if (!_picks.TryGetValue(file.Info.Path, out var picks)) _picks[file.Info.Path] = picks = new FilePicks(file.Places);
            return picks;
        }

        int? NextWithoutPick(ConflictFileView file, int from)
        {
            var count = file.Places.Count;
            for (var step = 1; step < count; step++)
            {
                var i = (from + step) % count;
                if (PickOf(i) == null) return i;
            }
            return null;
        }

        void RenderHeader(RepoSnapshot s)
        {
            var op = s.Operation;
            _title.text = Title(op);
            var left = s.ConflictedPaths.Count;
            _summary.text = left > 0 ? $"{PanelParts.Files(left)} left" : "All conflicts resolved";
            var note = Note(op);
            _note.text = note ?? string.Empty;
            PanelParts.SetDisplayed(_note, note != null);

            // Only merges and rebases can be finished or aborted here (cherry-pick and revert: spec §2).
            var finishing = op == RepoOperation.Merging || op == RepoOperation.Rebasing;
            PanelParts.SetDisplayed(_continue, finishing);
            PanelParts.SetDisplayed(_abort, finishing);
            if (!finishing) return;
            var merging = op == RepoOperation.Merging;
            _continue.text = merging ? "Finish merge" : "Continue rebase";
            _abort.text = merging ? "Abort merge" : "Abort rebase";
            ApplyAvailability(_continue, GitAction.Continue);
            _continue.EnableInClassList(EditorUiClasses.ButtonPrimary, _continue.enabledSelf);
            ApplyAvailability(_abort, GitAction.Abort);
        }

        static string Title(RepoOperation op)
        {
            switch (op)
            {
                case RepoOperation.Merging: return "Merging";
                case RepoOperation.Rebasing: return "Rebasing";
                case RepoOperation.CherryPicking: return "Cherry-picking";
                case RepoOperation.Reverting: return "Reverting";
                default: return "Conflicts"; // a stash apply or pop
            }
        }

        static string? Note(RepoOperation op)
        {
            switch (op)
            {
                case RepoOperation.Merging: return null;
                case RepoOperation.Rebasing:
                    return "During a rebase, HEAD is the branch you're rebasing onto and the other side is your own commit.";
                case RepoOperation.None:
                    return "These conflicts came from applying a stash. The stash is kept — drop it outside UnityGit when you're done.";
                default:
                    return $"Finish or abort the {ActionRules.OperationNoun(op)} outside UnityGit after resolving.";
            }
        }

        void RenderList(RepoSnapshot s)
        {
            _files = s.ConflictedPaths;
            _list.Set(_files.Select(p => new FileRow(p, _infos.TryGetValue(p, out var info) ? info : null)).ToList());
            ShowListSelection();
        }

        void ShowListSelection()
        {
            var index = -1;
            for (var i = 0; i < _files.Count; i++)
                if (_files[i] == SelectedFile) index = i;
            _list.View.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
        }

        void OnFileClicked()
        {
            var i = _list.View.selectedIndex;
            if (i < 0 || i >= _files.Count || _files[i] == SelectedFile) return;
            _ = Observe(SelectFileAsync(_files[i]));
        }

        // The file card and its action buttons. Cheap, so it runs on every render (busy state, Play Mode).
        void RenderActions()
        {
            var file = File;
            PanelParts.SetDisplayed(_fileCard, file != null && _files.Contains(file.Info.Path));
            if (file == null) return;
            var info = file.Info;

            var resolve = _session.GetAvailability(GitAction.Resolve);
            var hasPlaces = file.Places.Count > 0;
            var picked = hasPlaces ? PicksOf(file)?.ByPlace.Count > 0 : SidePick != null;
            SetEnabled(_apply, resolve.Enabled && picked,
                !resolve.Enabled ? resolve.Reason
                : hasPlaces ? "Pick an option for at least one place"
                : HandResolved(info) ? "Mark it resolved, or start over to pick again"
                : "Pick a side first");
            SetEnabled(_markResolved, resolve.Enabled, resolve.Reason);

            var restore = _session.GetAvailability(GitAction.RestoreConflict);
            SetEnabled(_startOver, restore.Enabled && ConflictTypes.ChangedOnBothSides(info.Type),
                !restore.Enabled ? restore.Reason : "Only files changed on both sides can start over");

            SetEnabled(_openEditor, info.ExistsInWorkingTree && !info.IsBinary,
                !info.ExistsInWorkingTree ? "The file isn't in the working tree" : "Binary files can't be opened in a code editor");
        }

        FilePicks? PicksOf(ConflictFileView file) => _picks.TryGetValue(file.Info.Path, out var picks) ? picks : null;

        // Builds the file card's contents for File: the texts, the whole-file buttons and the places with their context.
        void BuildFile()
        {
            _places.Clear();
            _placeViews.Clear();
            _wholeFileButtons.Clear();
            _useOurs = _useTheirs = null;
            var file = File;
            if (file == null) return;

            var info = file.Info;
            _fileTitle.text = info.Path;
            _fileTitle.tooltip = file.FullPath;
            _fileType.text = TypeText(info.Type, file.OursName, file.TheirsName);
            var note = FileNote(info);
            if (note != null) EditorUiElements.SetMessage(_fileNote, note, info.HasMalformedMarkers ? EditorUiTone.Warning : EditorUiTone.Info);
            PanelParts.SetDisplayed(_fileNote, note != null);
            PanelParts.SetDisplayed(_sceneWarning, file.IsSceneOrPrefab);

            var handResolved = HandResolved(info);
            PanelParts.SetDisplayed(_wholeFile, !handResolved);
            if (handResolved) return; // Mark resolved finishes it; the note says so

            if (file.Places.Count == 0)
            {
                // Binary (an LFS pointer too), deleted on a side, or markers that can't be read: only a whole side can be taken.
                _useOurs = OptionButton(SideText(file.OursName, RemovedBy(info.Type, ours: true)), () => PickSide(true));
                _useTheirs = OptionButton(SideText(file.TheirsName, RemovedBy(info.Type, ours: false)), () => PickSide(false));
                _wholeFileButtons.Add(_useOurs);
                _wholeFileButtons.Add(_useTheirs);
                return;
            }

            foreach (var pick in AllPicks) _wholeFileButtons.Add(OptionButton(file.Label(pick), () => PickAll(pick)));
            var parts = file.Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var place = parts[i].Place;
                if (place != null)
                {
                    var view = new ConflictPlaceView(this, place);
                    _placeViews.Add(view);
                    _places.Add(view);
                    continue;
                }
                var placeBefore = i > 0 && parts[i - 1].IsPlace;
                var placeAfter = i + 1 < parts.Count && parts[i + 1].IsPlace;
                foreach (var piece in ConflictText.Context(parts[i].Text ?? string.Empty, placeBefore, placeAfter))
                    _places.Add(piece.Text != null
                        ? FileText(ConflictText.ForDisplay(piece.Text), "unitygit-conflict-context")
                        : PanelParts.Text(ConflictText.Unchanged(piece.Collapsed), EditorUiClasses.Faint, EditorUiClasses.Small, "unitygit-conflict-collapsed"));
            }
        }

        static string? FileNote(ConflictFileInfo info)
        {
            if (info.HasMalformedMarkers)
                return "The conflict markers in this file can't be read as places — use one side, or fix it in your editor and mark it resolved.";
            // A file deleted on a side has no places to begin with; its type text and the two Use buttons say what to do.
            if (HandResolved(info)) return "No conflict places left — mark it resolved if it looks right.";
            return null;
        }

        // A text file changed on both sides whose markers were removed by hand. Mark resolved finishes it; a whole side isn't
        // offered (the session refuses it too): it would drop the hand merge and the changes git merged cleanly from the
        // other side (spec §3).
        static bool HandResolved(ConflictFileInfo info)
            => ConflictTypes.ChangedOnBothSides(info.Type) && !info.IsBinary && !info.HasMalformedMarkers && info.ExistsInWorkingTree && !info.HasPlaces;

        // Whether taking that side's whole file deletes the file: that side has no file.
        static bool RemovedBy(ConflictType type, bool ours)
            => type == ConflictType.BothDeleted
                || (ours ? type == ConflictType.DeletedByUs || type == ConflictType.AddedByThem
                         : type == ConflictType.DeletedByThem || type == ConflictType.AddedByUs);

        static string SideText(string side, bool removesFile) => removesFile ? $"Use {side} (removes the file)" : $"Use {side}";

        // Shows the picks and the selected place on the places, the side buttons and Apply.
        void ShowPicks()
        {
            foreach (var view in _placeViews) view.Show(view.Index == SelectedPlace, PickOf(view.Index));
            var side = SidePick;
            _useOurs?.EnableInClassList(PickedClass, side == true);
            _useTheirs?.EnableInClassList(PickedClass, side == false);
            RenderActions();
        }

        void ScrollToSelectedPlace()
        {
            var view = _placeViews.FirstOrDefault(v => v.Index == SelectedPlace);
            if (view == null || panel == null) return;
            // After the next layout, which knows the places' new sizes.
            _places.schedule.Execute(() =>
            {
                if (view.parent == _places.contentContainer) _places.ScrollTo(view);
            });
        }

        void OpenInEditor()
        {
            var file = File;
            if (file == null) return;
            var line = SelectedPlace is int place && place < file.Places.Count ? file.Places[place].StartLine : 1;
            if (!InternalEditorUtility.OpenFileAtLineExternal(file.FullPath, line)) EditorUtility.OpenWithDefaultApp(file.FullPath);
        }

        void ApplyAvailability(VisualElement element, GitAction action)
        {
            var availability = _session.GetAvailability(action);
            SetEnabled(element, availability.Enabled, availability.Reason);
        }

        // The tooltip says why a disabled button can't be used.
        static void SetEnabled(VisualElement element, bool enabled, string? reason)
        {
            element.SetEnabled(enabled);
            element.tooltip = enabled ? string.Empty : reason ?? string.Empty;
        }

        // Fire-and-forget work from a click: an exception is logged, never left unobserved.
        static async Task Observe(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        static T Named<T>(T element, string name) where T : VisualElement
        {
            element.name = name;
            return element;
        }

        static Label MessageLabel(VisualElement message) => message.Q<Label>(className: EditorUiClasses.MessageText);

        /// <summary>Text from the repository (paths, branch names) shows as typed, never as rich-text markup.</summary>
        internal static T Plain<T>(T element) where T : TextElement
        {
            element.enableRichText = false;
            return element;
        }

        /// <summary>File content: as typed (a "&lt;b&gt;" in a script stays "&lt;b&gt;"), in the kit's monospace font.</summary>
        internal static Label FileText(string text, string textClass)
            => Plain(PanelParts.Text(text, EditorUiClasses.Mono, "unitygit-conflict-code", textClass));

        internal static Button OptionButton(string text, Action onClick)
        {
            var button = Plain(new Button(onClick) { text = text });
            button.AddToClassList("unitygit-conflict-option");
            return button;
        }

        static EditorUiTone TypeTone(ConflictType type)
        {
            switch (type)
            {
                case ConflictType.AddedByUs:
                case ConflictType.AddedByThem:
                    return EditorUiTone.Info;
                case ConflictType.DeletedByUs:
                case ConflictType.DeletedByThem:
                    return EditorUiTone.Warning;
                default: // changed, added or deleted on both sides
                    return EditorUiTone.Error;
            }
        }

        /// <summary>A file's pending picks, and the places they were made on: a reload that finds other places drops them.</summary>
        sealed class FilePicks
        {
            public FilePicks(IReadOnlyList<ConflictPlace> places) => Places = places;

            public IReadOnlyList<ConflictPlace> Places { get; set; }
            public Dictionary<int, ConflictPick> ByPlace { get; } = new Dictionary<int, ConflictPick>();
            public bool? Side { get; set; }
        }

        /// <summary>A file list row: a conflicted path, and what the overview says about it once it has been read.</summary>
        readonly struct FileRow
        {
            public FileRow(string path, ConflictFileInfo? info)
            {
                Path = path;
                Info = info;
            }

            public string Path { get; }
            public ConflictFileInfo? Info { get; }

            public static string Key(FileRow row) => row.Info == null ? row.Path : $"{row.Path}\n{row.Info.Type}\n{Chip(row.Info)}";
        }

        /// <summary>The path, a badge for what happened to the file, and its places chip.</summary>
        sealed class FileRowView : VisualElement
        {
            readonly Label _path = Plain(PanelParts.Text(null, PanelClasses.Ellipsis, PanelClasses.Path, "unitygit-conflicts-path"));
            readonly Label _type = EditorUiElements.Badge(string.Empty, EditorUiTone.Error);
            readonly Label _chip = EditorUiElements.Chip(string.Empty);

            public FileRowView()
            {
                AddToClassList(PanelClasses.Row);
                Add(_path);
                Add(_type);
                Add(_chip);
            }

            // Rows are recycled, so every part of the row is set here.
            public static void Bind(VisualElement element, FileRow row)
            {
                var view = (FileRowView)element;
                view._path.text = row.Path;
                view._path.tooltip = row.Path;
                var info = row.Info;
                if (info != null)
                {
                    EditorUiElements.SetBadge(view._type, TypeBadge(info.Type), TypeTone(info.Type));
                    view._chip.text = Chip(info);
                }
                PanelParts.SetDisplayed(view._type, info != null);
                PanelParts.SetDisplayed(view._chip, info != null);
            }
        }
    }
}
