#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// The change list, under a header with a tick-all checkbox and a "{ticked} of {rows} to commit" summary, and the
    /// commit card. Ticks decide what is committed. The list's selection (click, Ctrl+click, Shift+click) only decides
    /// which rows a checkbox click or Space applies to, which file the preview under the list shows, and which rows the
    /// right-click menu (Revert, Show in Explorer, Show in Project) acts on.
    /// </summary>
    internal sealed class ChangesPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly ListView _list;
        readonly Toggle _tickAll;
        readonly Label _summary;
        readonly UnityEditor.UIElements.ToolbarToggle _showFoldersToggle;
        readonly TextField _message;
        readonly Button _submit;
        // The typed message is kept in SessionState, so it survives domain reloads (a script change, entering Play Mode).
        // The key holds the project path, so sessions on other folders (the tests' sandboxes) keep their own.
        readonly string _messageKey;

        /// <summary>Whether folder rows are shown, saved per user (EditorPrefs). Shown unless the user hid them.</summary>
        internal const string ShowFoldersPref = "UnityGit.ChangesShowFolders";

        // Unticked rather than ticked rows, so rows that appear later default to ticked. File rows only: a folder's box is
        // worked out from the files under it.
        readonly HashSet<string> _unticked = new HashSet<string>(StringComparer.Ordinal);
        // Selected lines by key (file path, or folder path with a trailing '/'), so the selection follows its lines when a
        // refresh adds or removes others.
        readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
        // The list's lines: the file rows, plus folder rows when shown (ChangeTree).
        readonly List<ChangeEntry> _entries = new List<ChangeEntry>();
        Dictionary<string, ChangeEntry> _entryByKey = new Dictionary<string, ChangeEntry>(StringComparer.Ordinal);
        Dictionary<string, ChangeItem> _byPath = new Dictionary<string, ChangeItem>(StringComparer.Ordinal);
        List<string> _rows = new List<string>();
        IReadOnlyList<ChangeItem> _changes = Array.Empty<ChangeItem>();
        bool _showFolders;
        bool _merging;
        bool _syncingSelection; // set while the panel moves the list's selection itself, so that isn't read back

        readonly ChangePreviewView _preview;
        string? _previewPath;           // the file shown or being read; null while a note is shown
        RepoSnapshot? _previewSnapshot; // the snapshot that file was read for: a new one reads it again
        int _previewRequest;            // numbers the reads, so a result for an earlier selection is dropped
        CancellationTokenSource? _previewCts; // cancels the running read when the selection moves on
        ChangePreview? _shownPreview;   // what the preview shows now, so an unchanged re-read leaves it alone

        public ChangesPanel(GitSession session)
        {
            _session = session;
            _messageKey = "UnityGit.CommitMessage." + session.ProjectPath;
            _showFolders = UnityEditor.EditorPrefs.GetBool(ShowFoldersPref, true);
            AddToClassList(PanelClasses.Content);

            // A table header lined up with the rows: the checkbox column, then "File", and the summary on the right.
            var header = new VisualElement { name = UiNames.ChangesHeader };
            header.AddToClassList(EditorUiClasses.TableRow);
            header.AddToClassList(EditorUiClasses.TableHeader);
            header.AddToClassList("unitygit-changes-header");
            _tickAll = new Toggle { name = UiNames.ChangesTickAll };
            _tickAll.RegisterValueChangedCallback(_ => SetAllTicked(AllTicked != true)); // from mixed or none, ticks all
            header.Add(_tickAll);
            header.Add(PanelParts.Text("File", EditorUiClasses.TableHeaderCell, "unitygit-changes-file"));
            _summary = PanelParts.Text(null, EditorUiClasses.TableHeaderCell, "unitygit-changes-summary");
            _summary.name = UiNames.ChangesSummary;
            header.Add(_summary);

            // Shows or hides the folder rows; off gives the plain list of file rows.
            _showFoldersToggle = new UnityEditor.UIElements.ToolbarToggle { name = UiNames.ChangesShowFolders, tooltip = "Show folders" };
            _showFoldersToggle.AddToClassList("unitygit-changes-show-folders");
            var folderIcon = new Image { image = GitIcons.LoadFolder(), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            folderIcon.AddToClassList(GitIcons.FolderIconClass);
            (_showFoldersToggle.Q(className: Toggle.inputUssClassName) ?? _showFoldersToggle).Add(folderIcon);
            _showFoldersToggle.SetValueWithoutNotify(_showFolders);
            _showFoldersToggle.RegisterValueChangedCallback(e => ShowFolders = e.newValue);
            header.Add(_showFoldersToggle);

            _list = new ListView
            {
                name = UiNames.ChangesList,
                fixedItemHeight = 24,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                selectionType = SelectionType.Multiple,
                itemsSource = _entries,
                makeItem = () => new ChangeRow(this),
                bindItem = BindRow,
            };
            _list.AddToClassList(PanelClasses.List);
            _list.selectedIndicesChanged += _ => ReadListSelection();
            _list.RegisterCallback<KeyDownEvent>(OnListKeyDown);
            var listCard = PanelParts.ListCard(null, _list);
            listCard.Insert(0, header);

            // The message box and the Commit button on one row, under the list.
            var commitCard = EditorUiElements.Card();
            commitCard.AddToClassList("unitygit-commit-card");
            var commitRow = new VisualElement();
            commitRow.AddToClassList("unitygit-commit-row");
            _message = new TextField { name = UiNames.CommitMessage, multiline = true };
            _message.SetValueWithoutNotify(UnityEditor.SessionState.GetString(_messageKey, string.Empty));
            _message.AddToClassList("unitygit-commit-message");
            _message.RegisterValueChangedCallback(e =>
            {
                SaveMessage(e.newValue);
                UpdateSubmit();
            });
            commitRow.Add(_message);

            _submit = new Button(() => _ = SubmitCommitAsync()) { name = UiNames.CommitSubmit, text = "Commit" };
            _submit.AddToClassList(EditorUiClasses.ButtonPrimary);
            GitIcons.Apply(_submit, GitIcon.Commit);
            commitRow.Add(_submit);
            commitCard.Add(commitRow);

            // The list and the commit bar on the left, the preview of the selected file on the right, full height, with a
            // divider between them to drag.
            var column = new VisualElement();
            column.AddToClassList("unitygit-changes-column");
            column.Add(listCard);
            column.Add(commitCard);
            _preview = new ChangePreviewView();
            Add(PanelParts.SideBySide(UiNames.ChangesSplit, column, _preview));
            Render();
        }

        /// <summary>The preview of the selected file.</summary>
        internal ChangePreviewView Preview => _preview;

        /// <summary>The latest preview read, for tests to wait on. Completed when nothing is being read.</summary>
        internal Task PreviewLoad { get; private set; } = Task.CompletedTask;

        /// <summary>Paths of the file rows currently listed. Folder rows aren't rows of their own: they tick these.</summary>
        internal IReadOnlyList<string> Rows => _rows;

        /// <summary>
        /// Whether folder rows are listed (the header's folder button). Saved per user. Turning it off keeps the selected files
        /// selected and drops selected folders.
        /// </summary>
        internal bool ShowFolders
        {
            get => _showFolders;
            set
            {
                if (value == _showFolders) return;
                _showFolders = value;
                UnityEditor.EditorPrefs.SetBool(ShowFoldersPref, value);
                _showFoldersToggle.SetValueWithoutNotify(value);
                RebuildEntries();
                RefreshRows();
                UpdatePreview();
            }
        }

        /// <summary>Keys of the list's lines in list order: file paths, and folder paths with a trailing '/' when folders are shown.</summary>
        internal IReadOnlyList<string> Entries => _entries.Select(e => e.Key).ToList();

        /// <summary>Keys of the selected lines (files and folders), in list order.</summary>
        internal IReadOnlyList<string> SelectedRows => _entries.Select(e => e.Key).Where(_selected.Contains).ToList();

        /// <summary>True when every row that can be ticked is ticked, false when none is, null when mixed or none can be.</summary>
        internal bool? AllTicked
        {
            get
            {
                int tickable = 0, ticked = 0;
                foreach (var row in _rows)
                {
                    if (!IsTickable(row)) continue;
                    tickable++;
                    if (IsTicked(row)) ticked++;
                }
                if (tickable == 0 || (ticked > 0 && ticked < tickable)) return null;
                return ticked == tickable;
            }
        }

        internal bool IsTicked(string rowPath)
        {
            if (_byPath.TryGetValue(rowPath, out var row) && row.IsConflicted) return false;
            return _merging || !_unticked.Contains(rowPath);
        }

        /// <summary>
        /// A folder's box, from the files under it that can be ticked: true when all are ticked, false when none are (or none
        /// can be), null when some are. During a merge every row is locked in, so it is true.
        /// </summary>
        internal bool? FolderTick(string folderKey)
        {
            if (_merging) return true;
            int tickable = 0, ticked = 0;
            foreach (var file in _entryByKey[folderKey].Files)
            {
                if (!IsTickable(file)) continue;
                tickable++;
                if (IsTicked(file)) ticked++;
            }
            if (tickable == 0 || ticked == 0) return false;
            return ticked == tickable ? true : (bool?)null;
        }

        /// <summary>
        /// A file row or a folder (every file under it). Ignored for conflicted rows and during a merge, where every row is
        /// committed.
        /// </summary>
        internal void SetTicked(string key, bool ticked) => SetTickedRows(ExpandToFiles(new[] { key }), ticked);

        /// <summary>Ticks or unticks every row, skipping the ones <see cref="SetTicked"/> ignores.</summary>
        internal void SetAllTicked(bool ticked) => SetTickedRows(_rows, ticked);

        /// <summary>
        /// What a row's checkbox calls: on a selected row the value applies to every selected row, on any other row only
        /// to that row. A folder stands for every file under it.
        /// </summary>
        internal void ToggleRow(string key, bool ticked)
            => SetTickedRows(ExpandToFiles(_selected.Contains(key) ? SelectedRows : new[] { key }), ticked);

        /// <summary>
        /// What a folder's checkbox calls: from mixed or empty it ticks, from ticked it unticks, as the header's tick-all box
        /// does, and for the selection when the folder is part of it (<see cref="ToggleRow"/>).
        /// </summary>
        internal void ToggleFolder(string folderKey)
        {
            ToggleRow(folderKey, FolderTick(folderKey) != true);
            RefreshRows(); // the click flipped the box itself; put back what the files say even when nothing changed
        }

        /// <summary>
        /// What Space calls: ticks every selected row if any of them is unticked, otherwise unticks them all. A selected folder
        /// stands for every file under it, each file counted once. Rows that can't be ticked are left out of both the check
        /// and the change.
        /// </summary>
        internal void ToggleSelection()
        {
            var rows = ExpandToFiles(SelectedRows).Where(IsTickable).ToList();
            if (rows.Count == 0) return;
            SetTickedRows(rows, rows.Any(r => !IsTicked(r)));
        }

        /// <summary>Selects these lines (files or folders), in place of the current selection. Keys that aren't listed are ignored.</summary>
        internal void Select(IEnumerable<string> keys)
        {
            _selected.Clear();
            foreach (var key in keys)
                if (_entryByKey.ContainsKey(key)) _selected.Add(key);
            ShowSelection();
            UpdatePreview();
        }

        internal async Task<ActionOutcome> SubmitCommitAsync()
        {
            var ticked = _rows.Where(IsTicked).ToList();
            var outcome = await _session.CommitAsync(_message.value, ticked, _rows);
            if (outcome.Succeeded)
            {
                _unticked.Clear();
                _message.value = string.Empty;
                SaveMessage(string.Empty); // here too: a box outside a panel sends no ChangeEvent
            }
            Render();
            return outcome;
        }

        /// <summary>What the right-click menu on a row offers, and for which rows.</summary>
        internal sealed class RowMenuState
        {
            public RowMenuState(IReadOnlyList<string> targets, string? revertDisabledReason, string? explorerPath, UnityEngine.Object? projectAsset)
            {
                Targets = targets;
                RevertDisabledReason = revertDisabledReason;
                ExplorerPath = explorerPath;
                ProjectAsset = projectAsset;
            }

            /// <summary>The rows the menu acts on: the selection when the clicked row is part of it, otherwise that row only.</summary>
            public IReadOnlyList<string> Targets { get; }

            /// <summary>Why Revert is disabled; null when it is enabled.</summary>
            public string? RevertDisabledReason { get; }

            /// <summary>The first target's full path when it is on disk; null disables Show in Explorer.</summary>
            public string? ExplorerPath { get; }

            /// <summary>The first target's asset when it is one; null disables Show in Project.</summary>
            public UnityEngine.Object? ProjectAsset { get; }
        }

        /// <summary>What the OS file browser item is called, as Unity names it.</summary>
        internal static string ExplorerLabel => Application.platform == RuntimePlatform.OSXEditor ? "Reveal in Finder" : "Show in Explorer";

        /// <summary>
        /// The menu for a right-click on this row. Targets follow the checkbox rule (<see cref="ToggleRow"/>), and the selection
        /// is left as it is. Show in Explorer and Show in Project use the first target.
        /// </summary>
        internal RowMenuState GetRowMenuState(string rowPath)
        {
            IReadOnlyList<string> picked = _selected.Contains(rowPath) ? SelectedRows : new[] { rowPath };
            IReadOnlyList<string> targets = ExpandToFiles(picked);

            var reason = _session.GetAvailability(GitAction.Revert).Reason;
            if (reason == null && targets.All(t => _byPath.TryGetValue(t, out var row) && row.IsConflicted))
                reason = RevertText.AllConflicted;

            // The first picked line: a folder opens or pings the folder itself.
            string? explorerPath = null;
            var root = _session.Snapshot.RepoRoot;
            if (root != null)
            {
                var full = Path.GetFullPath(Path.Combine(root, picked[0].TrimEnd('/')));
                if (File.Exists(full) || Directory.Exists(full)) explorerPath = full;
            }
            var assetPath = explorerPath != null ? AssetPaths.FromFullPath(explorerPath) : null;
            var asset = assetPath != null ? UnityEditor.AssetDatabase.LoadMainAssetAtPath(assetPath) : null;
            return new RowMenuState(targets, reason, explorerPath, asset);
        }

        internal void PopulateRowMenu(DropdownMenu menu, string rowPath)
        {
            var state = GetRowMenuState(rowPath);
            var reason = state.RevertDisabledReason;
            // A context menu has no tooltips, so a disabled Revert says why in its name.
            menu.AppendAction(reason == null ? "Revert…" : $"Revert… ({reason})", action => _ = RevertAsync(state.Targets), MenuStatus(reason == null));
            menu.AppendSeparator();
            menu.AppendAction(ExplorerLabel, _ => UnityEditor.EditorUtility.RevealInFinder(state.ExplorerPath), MenuStatus(state.ExplorerPath != null));
            menu.AppendAction("Show in Project", _ =>
            {
                UnityEditor.EditorUtility.FocusProjectWindow();
                UnityEditor.EditorGUIUtility.PingObject(state.ProjectAsset);
            }, MenuStatus(state.ProjectAsset != null));
        }

        /// <summary>Reverts these rows (it asks first), then shows the list as it is now.</summary>
        internal async Task<ActionOutcome> RevertAsync(IReadOnlyList<string> rows)
        {
            var outcome = await _session.RevertAsync(rows);
            Render();
            return outcome;
        }

        static DropdownMenuAction.Status MenuStatus(bool enabled)
            => enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;

        public void Render()
        {
            var s = _session.Snapshot;
            _merging = s.Operation == RepoOperation.Merging;
            _changes = s.Changes;
            _byPath = new Dictionary<string, ChangeItem>(_changes.Count, StringComparer.Ordinal);
            foreach (var item in _changes) _byPath[item.Path] = item;
            _rows = _changes.Select(i => i.Path).ToList();
            _unticked.RemoveWhere(p => !_byPath.ContainsKey(p)); // a row that returns later should default to ticked
            RebuildEntries();
            RefreshRows();
            UpdateSubmit();
            UpdateHeader();
            UpdatePreview();
        }

        /// <summary>The badge tone for a change kind: additions green, edits blue, removals and conflicts red, moves accent.</summary>
        internal static EditorUiTone KindTone(ChangeKind kind)
        {
            switch (kind)
            {
                case ChangeKind.Added:
                case ChangeKind.Untracked:
                    return EditorUiTone.Success;
                case ChangeKind.Deleted:
                case ChangeKind.Conflicted:
                    return EditorUiTone.Error;
                case ChangeKind.Renamed:
                case ChangeKind.Copied:
                    return EditorUiTone.Accent;
                default: // Modified, TypeChanged
                    return EditorUiTone.Info;
            }
        }

        void SaveMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) UnityEditor.SessionState.EraseString(_messageKey);
            else UnityEditor.SessionState.SetString(_messageKey, message);
        }

        bool IsTickable(string rowPath)
            => !_merging && !(_byPath.TryGetValue(rowPath, out var row) && row.IsConflicted);

        // The list's lines from the current rows, with or without folder rows; selected lines no longer listed are dropped.
        void RebuildEntries()
        {
            _entries.Clear();
            _entries.AddRange(ChangeTree.Build(_changes, _showFolders));
            _entryByKey = new Dictionary<string, ChangeEntry>(_entries.Count, StringComparer.Ordinal);
            foreach (var entry in _entries) _entryByKey[entry.Key] = entry;
            _selected.RemoveWhere(k => !_entryByKey.ContainsKey(k));
        }

        // The file rows these keys stand for: a file row is itself (a folded folder .meta too, though it has no line of its
        // own), a folder is every file under it. In the given order, each file once; unknown keys are ignored.
        List<string> ExpandToFiles(IEnumerable<string> keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var files = new List<string>();
            foreach (var key in keys)
            {
                if (_entryByKey.TryGetValue(key, out var entry))
                {
                    foreach (var file in entry.Files)
                        if (seen.Add(file)) files.Add(file);
                }
                else if (_byPath.ContainsKey(key) && seen.Add(key)) files.Add(key);
            }
            return files;
        }

        // These rows plus the folded .meta of every folder above them. A folded .meta has no box of its own, so ticking a file
        // ticks it: otherwise a folder whose files were all ticked by hand would stay mixed, and a new folder's files could be
        // committed without its .meta, which gives the folder another GUID on the next machine.
        List<string> WithFolderMetas(IEnumerable<string> rowPaths)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var path in rowPaths)
            {
                if (seen.Add(path)) result.Add(path);
                for (var i = path.IndexOf('/'); i >= 0 && i < path.Length - 1; i = path.IndexOf('/', i + 1))
                    if (_entryByKey.TryGetValue(path.Substring(0, i + 1), out var folder) && folder.MetaRow is string meta && seen.Add(meta))
                        result.Add(meta);
            }
            return result;
        }

        // A row's checkbox was clicked: a folder follows its own rule, a file takes the box's new value.
        void OnRowTick(string key, bool ticked)
        {
            if (_entryByKey.TryGetValue(key, out var entry) && entry.IsFolder) ToggleFolder(key);
            else ToggleRow(key, ticked);
        }

        // Applies the tick rules to each row, then refreshes the list once for the whole batch.
        void SetTickedRows(IEnumerable<string> rowPaths, bool ticked)
        {
            if (ticked && _showFolders) rowPaths = WithFolderMetas(rowPaths);
            var changed = false;
            foreach (var path in rowPaths)
            {
                if (!IsTickable(path)) continue;
                changed |= ticked ? _unticked.Remove(path) : _unticked.Add(path);
            }
            if (!changed) return;
            RefreshRows();
            UpdateSubmit();
            UpdateHeader();
        }

        // Rebinds the visible rows, then puts the list's selection back on the selected paths, whose indices may have moved.
        void RefreshRows()
        {
            _syncingSelection = true;
            try
            {
                _list.RefreshItems();
                _list.SetSelectionWithoutNotify(SelectedIndices());
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        void ShowSelection()
        {
            _syncingSelection = true;
            try
            {
                _list.SetSelectionWithoutNotify(SelectedIndices());
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        List<int> SelectedIndices()
        {
            var indices = new List<int>(_selected.Count);
            for (var i = 0; i < _entries.Count; i++)
                if (_selected.Contains(_entries[i].Key)) indices.Add(i);
            return indices;
        }

        // The user changed the list's selection (click, Ctrl+click, Shift+click, arrows, Ctrl+A).
        void ReadListSelection()
        {
            if (_syncingSelection) return;
            _selected.Clear();
            foreach (var i in _list.selectedIndices)
                if (i >= 0 && i < _entries.Count) _selected.Add(_entries[i].Key);
            UpdatePreview();
        }

        // One selected row is read and shown; none, several or a conflicted one get a note. The file is read again when the
        // selection moves to another file or the snapshot is new (the file may have changed); while the same file is read
        // again its old preview stays, so a refresh doesn't flicker.
        void UpdatePreview()
        {
            var selected = SelectedRows;
            string? note = null;
            if (selected.Count == 0) note = PreviewText.SelectFile;
            else if (selected.Count > 1) note = PreviewText.Selected(ExpandToFiles(selected).Count, selected.Count(k => _entryByKey[k].IsFolder));
            else if (_entryByKey[selected[0]].IsFolder) note = PreviewText.Folder(_entryByKey[selected[0]].Files.Count);
            else if (_byPath[selected[0]].IsConflicted) note = PreviewText.Conflicted;
            if (note != null)
            {
                CancelPreviewRead();
                _previewRequest++;
                _previewPath = null;
                _previewSnapshot = null;
                _shownPreview = null;
                _preview.ShowNote(note);
                PreviewLoad = Task.CompletedTask;
                return;
            }

            var row = _byPath[selected[0]];
            var snapshot = _session.Snapshot;
            if (row.Path == _previewPath && ReferenceEquals(snapshot, _previewSnapshot)) return;
            if (row.Path != _previewPath)
            {
                _shownPreview = null;
                _preview.ShowLoading(row.Path);
            }
            _previewPath = row.Path;
            _previewSnapshot = snapshot;
            CancelPreviewRead();
            _previewCts = new CancellationTokenSource();
            PreviewLoad = LoadPreviewAsync(row, ++_previewRequest, _previewCts.Token);
        }

        // Stops the read of a selection that has moved on, so its git processes don't hold up the next read (FlexGit runs
        // one command per repository at a time). Not disposed: a token source without a timer needs no disposing, and the
        // read may still be looking at the token.
        void CancelPreviewRead()
        {
            _previewCts?.Cancel();
            _previewCts = null;
        }

        async Task LoadPreviewAsync(ChangeItem row, int request, CancellationToken cancellationToken)
        {
            try
            {
                var preview = await _session.LoadChangePreviewAsync(row, cancellationToken);
                if (request != _previewRequest) return; // the selection or the snapshot moved on
                if (preview == null)
                {
                    _shownPreview = null;
                    _preview.ShowNote(PreviewText.Failed);
                }
                else if (_shownPreview == null || !_shownPreview.HasSameContent(preview))
                {
                    _shownPreview = preview;
                    _preview.Show(preview);
                }
                // else: a refresh found the file as shown; rebuilding would lose the scroll position and stop a playing clip
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (request == _previewRequest) _preview.ShowNote(PreviewText.Failed);
            }
        }

        void OnListKeyDown(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Space) return;
            ToggleSelection();
            e.StopPropagation();
        }

        void UpdateHeader()
        {
            var anyTickable = _rows.Any(IsTickable);
            var all = AllTicked;
            _tickAll.SetEnabled(anyTickable);
            // During a merge every row is locked in, so the box shows ticked (and disabled).
            _tickAll.SetValueWithoutNotify(all == true || (_merging && _rows.Count > 0));
            _tickAll.showMixedValue = anyTickable && all == null;
            _summary.text = _rows.Count == 0
                ? "No changes"
                : $"{PanelParts.Count(_rows.Count(IsTicked))} of {PanelParts.Count(_rows.Count)} to commit";
        }

        void UpdateSubmit()
        {
            var availability = _session.GetAvailability(GitAction.Commit);
            var hasMessage = !string.IsNullOrWhiteSpace(_message.value);
            _submit.SetEnabled(availability.Enabled && hasMessage && _rows.Any(IsTicked));
            _submit.tooltip = availability.Reason ?? string.Empty;
        }

        // Rows are recycled, between files and folders too, so every part of the row is set here.
        void BindRow(VisualElement element, int index)
        {
            var row = (ChangeRow)element;
            var entry = _entries[index];
            row.userData = entry.Key;
            row.Path.text = entry.Label;
            row.Path.tooltip = entry.Label;
            PanelParts.SetDisplayed(row.FolderIcon, entry.IsFolder);
            PanelParts.SetDisplayed(row.Kind, !entry.IsFolder);

            if (entry.Item is ChangeItem item)
            {
                row.Tick.SetValueWithoutNotify(IsTicked(item.Path));
                row.Tick.showMixedValue = false;
                row.Tick.SetEnabled(IsTickable(item.Path));
                EditorUiElements.SetBadge(row.Kind, BadgeText(item.Kind), KindTone(item.Kind));
                row.Kind.tooltip = item.Kind.ToString();
                PanelParts.SetDisplayed(row.Meta, item.HasMeta);
                return;
            }

            // A folder: its box follows the files under it. Value first, then the mixed mark, as UpdateHeader does.
            var tick = FolderTick(entry.Key);
            row.Tick.SetValueWithoutNotify(tick == true);
            row.Tick.showMixedValue = tick == null;
            row.Tick.SetEnabled(!_merging && entry.Files.Any(IsTickable));
            PanelParts.SetDisplayed(row.Meta, entry.MetaRow != null);
        }

        static string BadgeText(ChangeKind kind)
        {
            switch (kind)
            {
                case ChangeKind.Modified: return "M";
                case ChangeKind.Added: return "A";
                case ChangeKind.Deleted: return "D";
                case ChangeKind.Renamed: return "R";
                case ChangeKind.Copied: return "C";
                case ChangeKind.TypeChanged: return "T";
                case ChangeKind.Untracked: return "U";
                default: return "!";
            }
        }

        /// <summary>
        /// A change row: checkbox, kind badge (a folder icon on a folder row), path and a ".meta" chip when the asset's .meta,
        /// or the folder's, is part of the row.
        /// </summary>
        sealed class ChangeRow : VisualElement
        {
            public readonly Toggle Tick = new Toggle();
            public readonly Image FolderIcon = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            public readonly Label Kind = EditorUiElements.Badge(string.Empty, EditorUiTone.Neutral);
            public readonly Label Path = PanelParts.Text(null, PanelClasses.Ellipsis, PanelClasses.Path, "unitygit-change-path");
            public readonly Label Meta = EditorUiElements.Chip(".meta");

            public ChangeRow(ChangesPanel panel)
            {
                AddToClassList(PanelClasses.Row);
                FolderIcon.image = GitIcons.LoadFolder();
                FolderIcon.AddToClassList(GitIcons.FolderIconClass);
                Kind.AddToClassList("unitygit-kind-badge");
                Meta.AddToClassList("unitygit-change-meta");
                // Not focusable, so Space always reaches the list (ToggleSelection) instead of also toggling a focused box.
                // A click still works: the box's Clickable handles it and stops the pointer events there, so the list's
                // selection is left as it was and ToggleRow sees it.
                Tick.focusable = false;
                Tick.RegisterValueChangedCallback(e =>
                {
                    if (userData is string key) panel.OnRowTick(key, e.newValue);
                });
                // Rows are recycled, so the path is read when the menu opens.
                this.AddManipulator(new ContextualMenuManipulator(e =>
                {
                    if (userData is string path) panel.PopulateRowMenu(e.menu, path);
                }));
                Add(Tick);
                Add(FolderIcon);
                Add(Kind);
                Add(Path);
                Add(Meta);
            }
        }
    }
}
