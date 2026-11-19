#nullable enable
using System.Threading.Tasks;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    internal sealed class BranchesPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly RowList<BranchItem> _local = new RowList<BranchItem>(UiNames.LocalBranches, () => new BranchRow(), BranchRow.Bind, Key);
        readonly RowList<BranchItem> _remote = new RowList<BranchItem>(UiNames.RemoteBranches, () => new BranchRow(), BranchRow.Bind, Key);

        public BranchesPanel(GitSession session)
        {
            _session = session;
            AddToClassList(PanelClasses.Content);
            Add(_local.InCard("Local branches"));
            Add(_remote.InCard("Remote branches"));
            _local.OnChosen(b => _ = SwitchAsync(b.Name));
            _remote.OnChosen(b => _ = SwitchAsync(b.ShortName));
            Render();
        }

        /// <summary>
        /// What a double-click (or Enter) on a branch row does. The current branch is ignored: switching to it would run
        /// the whole switch flow for nothing. A row can't be disabled the way a button is, so a refusal (Play Mode, busy)
        /// is recorded, and the outcome bar says why nothing happened. Returns null when no switch was tried.
        /// </summary>
        internal async Task<ActionOutcome?> SwitchAsync(string branch)
        {
            var s = _session.Snapshot;
            if (!s.IsDetached && s.Branch == branch) return null;
            var outcome = await _session.SwitchBranchAsync(branch);
            MainToolbarButtons.RecordOutcome(_session, outcome); // records only a refusal; the click feedback is the toolbar's
            return outcome;
        }

        public void Render()
        {
            _local.Set(_session.Snapshot.LocalBranches);
            _remote.Set(_session.Snapshot.RemoteBranches);
        }

        static string Key(BranchItem b) => $"{b.Name}\n{b.IsCurrent}\n{b.Upstream}\n{b.Ahead}\n{b.Behind}\n{b.LastCommitSubject}";

        /// <summary>Name, a "current" badge, the upstream, ahead/behind chips and the muted last subject.</summary>
        sealed class BranchRow : VisualElement
        {
            readonly Label _name = PanelParts.Text(null, "unitygit-branch-name");
            readonly Label _current = EditorUiElements.Badge("current", EditorUiTone.Accent);
            readonly Label _upstream = PanelParts.Text(null, EditorUiClasses.Muted, EditorUiClasses.Small, PanelClasses.Ellipsis, "unitygit-branch-upstream");
            readonly Label _ahead = EditorUiElements.Chip(string.Empty, EditorUiTone.Info);
            readonly Label _behind = EditorUiElements.Chip(string.Empty, EditorUiTone.Warning);
            readonly Label _subject = PanelParts.Text(null, EditorUiClasses.Muted, PanelClasses.Ellipsis, "unitygit-row-subject");

            public BranchRow()
            {
                AddToClassList(PanelClasses.Row);
                Add(_name);
                Add(_current);
                Add(_upstream);
                Add(_ahead);
                Add(_behind);
                Add(_subject);
            }

            // Rows are recycled, so every part of the row is set here.
            public static void Bind(VisualElement element, BranchItem b)
            {
                var row = (BranchRow)element;
                row._name.text = b.Name;
                row._name.EnableInClassList(EditorUiClasses.Strong, b.IsCurrent);
                PanelParts.SetDisplayed(row._current, b.IsCurrent);
                row._upstream.text = b.Upstream != null ? $"→ {b.Upstream}" : string.Empty;
                PanelParts.SetDisplayed(row._upstream, b.Upstream != null);
                row._ahead.text = $"↑{b.Ahead}";
                PanelParts.SetDisplayed(row._ahead, b.Ahead != 0);
                row._behind.text = $"↓{b.Behind}";
                PanelParts.SetDisplayed(row._behind, b.Behind != 0);
                row._subject.text = b.LastCommitSubject;
                row.tooltip = b.LastCommitSubject;
            }
        }
    }
}
