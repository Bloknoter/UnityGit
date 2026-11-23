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
        readonly TextField _message;
        readonly Button _submit;
        // The typed message is kept in SessionState, so it survives domain reloads (a script change, entering Play Mode).
        // The key holds the project path, so sessions on other folders (the tests' sandboxes) keep their own.
        readonly string _messageKey;

        // Unticked rather than ticked rows, so rows that appear later default to ticked.
        readonly HashSet<string> _unticked = new HashSet<string>(StringComparer.Ordinal);
        // Selected rows by path, so the selection follows its rows when a refresh adds or removes others.
        readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
        readonly List<ChangeItem> _items = new List<ChangeItem>();
        Dictionary<string, ChangeItem> _byPath = new Dictionary<string, ChangeItem>(StringComparer.Ordinal);
        List<string> _rows = new List<string>();
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

            _list = new ListView
            {
                name = UiNames.ChangesList,
                fixedItemHeight = 24,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                selectionType = SelectionType.Multiple,
                itemsSource = _items,
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

        /// <summary>Paths of the rows currently listed.</summary>
        internal IReadOnlyList<string> Rows => _rows;

        /// <summary>Paths of the selected rows, in list order.</summary>
        internal IReadOnlyList<string> SelectedRows => _rows.Where(_selected.Contains).ToList();

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

        /// <summary>Ignored for conflicted rows and during a merge, where every row is committed.</summary>
        internal void SetTicked(string rowPath, bool ticked) => SetTickedRows(new[] { rowPath }, ticked);

        /// <summary>Ticks or unticks every row, skipping the ones <see cref="SetTicked"/> ignores.</summary>
        internal void SetAllTicked(bool ticked) => SetTickedRows(_rows, ticked);

        /// <summary>
        /// What a row's checkbox calls: on a selected row the value applies to every selected row, on any other row only
        /// to that row.
        /// </summary>
        internal void ToggleRow(string rowPath, bool ticked)
            => SetTickedRows(_selected.Contains(rowPath) ? SelectedRows : new[] { rowPath }, ticked);

        /// <summary>
        /// What Space calls: ticks every selected row if any of them is unticked, otherwise unticks them all. Rows that
        /// can't be ticked are left out of both the check and the change.
        /// </summary>
        internal void ToggleSelection()
        {
            var rows = SelectedRows.Where(IsTickable).ToList();
            if (rows.Count == 0) return;
            SetTickedRows(rows, rows.Any(r => !IsTicked(r)));
        }

        /// <summary>Selects these rows, in place of the current selection. Paths that aren't listed are ignored.</summary>
        internal void Select(IEnumerable<string> rowPaths)
        {
            _selected.Clear();
            foreach (var path in rowPaths)
                if (_byPath.ContainsKey(path)) _selected.Add(path);
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
            IReadOnlyList<string> targets = _selected.Contains(rowPath) ? SelectedRows : new[] { rowPath };

            var reason = _session.GetAvailability(GitAction.Revert).Reason;
            if (reason == null && targets.All(t => _byPath.TryGetValue(t, out var row) && row.IsConflicted))
                reason = RevertText.AllConflicted;

            string? explorerPath = null;
            var root = _session.Snapshot.RepoRoot;
            if (root != null)
            {
                var full = Path.GetFullPath(Path.Combine(root, targets[0]));
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
            _items.Clear();
            _items.AddRange(s.Changes);
            _byPath = new Dictionary<string, ChangeItem>(_items.Count, StringComparer.Ordinal);
            foreach (var item in _items) _byPath[item.Path] = item;
            _rows = _items.Select(i => i.Path).ToList();
            _unticked.RemoveWhere(p => !_byPath.ContainsKey(p)); // a row that returns later should default to ticked
            _selected.RemoveWhere(p => !_byPath.ContainsKey(p));
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

        // Applies the tick rules to each row, then refreshes the list once for the whole batch.
        void SetTickedRows(IEnumerable<string> rowPaths, bool ticked)
        {
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
            for (var i = 0; i < _rows.Count; i++)
                if (_selected.Contains(_rows[i])) indices.Add(i);
            return indices;
        }

        // The user changed the list's selection (click, Ctrl+click, Shift+click, arrows, Ctrl+A).
        void ReadListSelection()
        {
            if (_syncingSelection) return;
            _selected.Clear();
            foreach (var i in _list.selectedIndices)
                if (i >= 0 && i < _rows.Count) _selected.Add(_rows[i]);
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
            else if (selected.Count > 1) note = PreviewText.Selected(selected.Count);
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

        // Rows are recycled, so every part of the row is set here.
        void BindRow(VisualElement element, int index)
        {
            var row = (ChangeRow)element;
            var item = _items[index];
            row.userData = item.Path;
            row.Tick.SetValueWithoutNotify(IsTicked(item.Path));
            row.Tick.SetEnabled(IsTickable(item.Path));
            EditorUiElements.SetBadge(row.Kind, BadgeText(item.Kind), KindTone(item.Kind));
            row.Kind.tooltip = item.Kind.ToString();
            row.Path.text = item.Path;
            row.Path.tooltip = item.Path;
            PanelParts.SetDisplayed(row.Meta, item.HasMeta);
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

        /// <summary>A change row: checkbox, kind badge, path and a ".meta" chip when the asset's .meta is paired with it.</summary>
        sealed class ChangeRow : VisualElement
        {
            public readonly Toggle Tick = new Toggle();
            public readonly Label Kind = EditorUiElements.Badge(string.Empty, EditorUiTone.Neutral);
            public readonly Label Path = PanelParts.Text(null, PanelClasses.Ellipsis, PanelClasses.Path, "unitygit-change-path");
            public readonly Label Meta = EditorUiElements.Chip(".meta");

            public ChangeRow(ChangesPanel panel)
            {
                AddToClassList(PanelClasses.Row);
                Kind.AddToClassList("unitygit-kind-badge");
                // Not focusable, so Space always reaches the list (ToggleSelection) instead of also toggling a focused box.
                // A click still works: the box's Clickable handles it and stops the pointer events there, so the list's
                // selection is left as it was and ToggleRow sees it.
                Tick.focusable = false;
                Tick.RegisterValueChangedCallback(e =>
                {
                    if (userData is string path) panel.ToggleRow(path, e.newValue);
                });
                // Rows are recycled, so the path is read when the menu opens.
                this.AddManipulator(new ContextualMenuManipulator(e =>
                {
                    if (userData is string path) panel.PopulateRowMenu(e.menu, path);
                }));
                Add(Tick);
                Add(Kind);
                Add(Path);
                Add(Meta);
            }
        }
    }
}
