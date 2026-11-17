#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    internal sealed class ChangesPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly ListView _list;
        readonly TextField _message;
        readonly Button _submit;

        // Unticked rather than ticked rows, so rows that appear later default to ticked.
        readonly HashSet<string> _unticked = new HashSet<string>(StringComparer.Ordinal);
        List<ChangeItem> _items = new List<ChangeItem>();
        Dictionary<string, ChangeItem> _byPath = new Dictionary<string, ChangeItem>(StringComparer.Ordinal);
        List<string> _rows = new List<string>();
        bool _merging;

        public ChangesPanel(GitSession session)
        {
            _session = session;
            AddToClassList(PanelClasses.Content);

            _list = new ListView
            {
                name = UiNames.ChangesList,
                fixedItemHeight = 24,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                selectionType = SelectionType.None,
                itemsSource = _items,
                makeItem = () => new ChangeRow(this),
                bindItem = BindRow,
            };
            _list.AddToClassList(PanelClasses.List);
            Add(PanelParts.ListCard(null, _list));

            var commitCard = EditorUiElements.Card();
            commitCard.AddToClassList("unitygit-commit-card");
            _message = new TextField { name = UiNames.CommitMessage, multiline = true };
            _message.AddToClassList("unitygit-commit-message");
            _message.RegisterValueChangedCallback(_ => UpdateSubmit());
            commitCard.Add(_message);

            _submit = new Button(() => _ = SubmitCommitAsync()) { name = UiNames.CommitSubmit, text = "Commit" };
            _submit.AddToClassList(EditorUiClasses.ButtonPrimary);
            var actions = new VisualElement();
            actions.AddToClassList("unitygit-commit-actions");
            actions.Add(_submit);
            commitCard.Add(actions);
            Add(commitCard);
            Render();
        }

        /// <summary>Paths of the rows currently listed.</summary>
        internal IReadOnlyList<string> Rows => _rows;

        internal bool IsTicked(string rowPath)
        {
            if (_byPath.TryGetValue(rowPath, out var row) && row.IsConflicted) return false;
            return _merging || !_unticked.Contains(rowPath);
        }

        /// <summary>Ignored for conflicted rows and during a merge, where every row is committed.</summary>
        internal void SetTicked(string rowPath, bool ticked)
        {
            if (_merging || (_byPath.TryGetValue(rowPath, out var row) && row.IsConflicted)) return;
            var changed = ticked ? _unticked.Remove(rowPath) : _unticked.Add(rowPath);
            if (!changed) return;
            _list.RefreshItems();
            UpdateSubmit();
        }

        internal async Task<ActionOutcome> SubmitCommitAsync()
        {
            var ticked = _rows.Where(IsTicked).ToList();
            var outcome = await _session.CommitAsync(_message.value, ticked, _rows);
            if (outcome.Succeeded)
            {
                _unticked.Clear();
                _message.value = string.Empty;
            }
            Render();
            return outcome;
        }

        public void Render()
        {
            var s = _session.Snapshot;
            _merging = s.Operation == RepoOperation.Merging;
            _items = s.Changes.ToList();
            _byPath = new Dictionary<string, ChangeItem>(_items.Count, StringComparer.Ordinal);
            foreach (var item in _items) _byPath[item.Path] = item;
            _rows = _items.Select(i => i.Path).ToList();
            _unticked.RemoveWhere(p => !_byPath.ContainsKey(p)); // a row that returns later should default to ticked
            _list.itemsSource = _items;
            _list.RefreshItems();
            UpdateSubmit();
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
            row.Tick.SetEnabled(!item.IsConflicted && !_merging);
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
                Tick.RegisterValueChangedCallback(e =>
                {
                    if (userData is string path) panel.SetTicked(path, e.newValue);
                });
                Add(Tick);
                Add(Kind);
                Add(Path);
                Add(Meta);
            }
        }
    }
}
