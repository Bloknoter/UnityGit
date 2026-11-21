#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// The change list, under a header with a tick-all checkbox and a "{ticked} of {rows} to commit" summary, and the
    /// commit card. Ticks decide what is committed. The list's selection (click, Ctrl+click, Shift+click) only decides
    /// which rows a checkbox click or Space applies to, and which file the preview under the list shows.
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

            // The list on top, the preview of the selected file below, a divider between them to drag (spec §5.1).
            _preview = new ChangePreviewView();
            var split = new TwoPaneSplitView(1, PreviewHeight, TwoPaneSplitViewOrientation.Vertical)
            {
                name = UiNames.ChangesSplit,
                viewDataKey = UiNames.ChangesSplit,
            };
            split.AddToClassList("unitygit-changes-split");
            split.Add(listCard);
            split.Add(_preview);
            Add(split);

            var commitCard = EditorUiElements.Card();
            commitCard.AddToClassList("unitygit-commit-card");
            _message = new TextField { name = UiNames.CommitMessage, multiline = true };
            _message.SetValueWithoutNotify(UnityEditor.SessionState.GetString(_messageKey, string.Empty));
            _message.AddToClassList("unitygit-commit-message");
            _message.RegisterValueChangedCallback(e =>
            {
                SaveMessage(e.newValue);
                UpdateSubmit();
            });
            commitCard.Add(_message);

            _submit = new Button(() => _ = SubmitCommitAsync()) { name = UiNames.CommitSubmit, text = "Commit" };
            _submit.AddToClassList(EditorUiClasses.ButtonPrimary);
            GitIcons.Apply(_submit, GitIcon.Commit);
            var actions = new VisualElement();
            actions.AddToClassList("unitygit-commit-actions");
            actions.Add(_submit);
            commitCard.Add(actions);
            Add(commitCard);
            Render();
        }

        /// <summary>The preview pane's starting height.</summary>
        internal const float PreviewHeight = 220f;

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
                _previewRequest++;
                _previewPath = null;
                _previewSnapshot = null;
                _preview.ShowNote(note);
                PreviewLoad = Task.CompletedTask;
                return;
            }

            var row = _byPath[selected[0]];
            var snapshot = _session.Snapshot;
            if (row.Path == _previewPath && ReferenceEquals(snapshot, _previewSnapshot)) return;
            if (row.Path != _previewPath) _preview.ShowLoading(row.Path);
            _previewPath = row.Path;
            _previewSnapshot = snapshot;
            PreviewLoad = LoadPreviewAsync(row, ++_previewRequest);
        }

        async Task LoadPreviewAsync(ChangeItem row, int request)
        {
            try
            {
                var preview = await _session.LoadChangePreviewAsync(row);
                if (request != _previewRequest) return; // the selection or the snapshot moved on
                if (preview == null) _preview.ShowNote(PreviewText.Failed);
                else _preview.Show(preview);
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
                Add(Tick);
                Add(Kind);
                Add(Path);
                Add(Meta);
            }
        }
    }
}
