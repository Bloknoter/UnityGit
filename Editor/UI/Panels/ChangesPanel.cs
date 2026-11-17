#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;

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
            AddToClassList("unitygit-panel-content");

            _list = new ListView
            {
                name = UiNames.ChangesList,
                fixedItemHeight = 22,
                showBorder = true,
                selectionType = SelectionType.None,
                itemsSource = _items,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _list.AddToClassList("unitygit-list");
            _list.AddToClassList("unitygit-changes-list");
            Add(_list);

            _message = new TextField { name = UiNames.CommitMessage, multiline = true };
            _message.AddToClassList("unitygit-commit-message");
            _message.RegisterValueChangedCallback(_ => UpdateSubmit());
            Add(_message);

            _submit = new Button(() => _ = SubmitCommitAsync()) { name = UiNames.CommitSubmit, text = "Commit" };
            Add(_submit);
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

        void UpdateSubmit()
        {
            var availability = _session.GetAvailability(GitAction.Commit);
            var hasMessage = !string.IsNullOrWhiteSpace(_message.value);
            _submit.SetEnabled(availability.Enabled && hasMessage && _rows.Any(IsTicked));
            _submit.tooltip = availability.Reason ?? string.Empty;
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("unitygit-change-row");
            var toggle = new Toggle();
            toggle.RegisterValueChangedCallback(e =>
            {
                if (row.userData is string path) SetTicked(path, e.newValue);
            });
            var badge = new Label();
            badge.AddToClassList("unitygit-badge");
            row.Add(toggle);
            row.Add(badge);
            row.Add(new Label());
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            var item = _items[index];
            row.userData = item.Path;
            var toggle = (Toggle)row[0];
            toggle.SetValueWithoutNotify(IsTicked(item.Path));
            toggle.SetEnabled(!item.IsConflicted && !_merging);
            var badge = (Label)row[1];
            badge.text = BadgeText(item.Kind);
            badge.tooltip = item.Kind.ToString();
            foreach (ChangeKind kind in Enum.GetValues(typeof(ChangeKind)))
                badge.EnableInClassList("kind-" + kind.ToString().ToLowerInvariant(), kind == item.Kind);
            ((Label)row[2]).text = item.Path + (item.HasMeta ? " (+ .meta)" : "");
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
    }
}
