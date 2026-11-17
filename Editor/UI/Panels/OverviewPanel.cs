#nullable enable
using System.Linq;
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    internal sealed class OverviewPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly Label _branch;
        readonly Label _state;
        readonly Label _head;
        readonly Label _user;
        readonly Label _remotes;
        readonly Label _lfs;
        readonly VisualElement _lfsRow;
        readonly Button _lfsSetup;

        public OverviewPanel(GitSession session)
        {
            _session = session;
            AddToClassList("unitygit-panel-content");
            _branch = Row("Branch", UiNames.OverviewBranch);
            _state = Row("State");
            _head = Row("HEAD");
            _user = Row("You", UiNames.OverviewUser);
            _remotes = Row("Remotes");
            _lfs = Row("LFS", UiNames.OverviewLfs, out _lfsRow);
            _lfsSetup = new Button(() => _ = _session.SetUpLfsAsync()) { name = UiNames.LfsSetupButton, text = "Set up LFS" };
            Render();
        }

        public void Render()
        {
            var s = _session.Snapshot;
            _branch.text = ActionLabels.Branch(s)
                + (s.Upstream != null ? $" → {s.Upstream}" : "")
                + (s.Ahead != 0 || s.Behind != 0 ? $" ↑{s.Ahead} ↓{s.Behind}" : "");
            _state.text = StateText(s);
            _head.text = s.RecentCommits.Count > 0 ? $"{s.RecentCommits[0].ShortSha} {s.RecentCommits[0].Subject}" : "No commits yet";
            _user.text = s.UserName != null || s.UserEmail != null ? $"{s.UserName} <{s.UserEmail}>" : "Not configured";
            _remotes.text = string.Join("\n", s.Remotes.Select(r => $"{r.Name} {r.FetchUrl}"));
            _lfs.text = !s.Lfs.UsedByRepo ? "Not used" : s.Lfs.HooksInstalled ? "Used, set up" : "Used — not set up";

            var canSetUp = _session.GetAvailability(GitAction.SetUpLfs).Enabled;
            if (canSetUp && _lfsSetup.parent == null) _lfsRow.Add(_lfsSetup);
            else if (!canSetUp && _lfsSetup.parent != null) _lfsSetup.RemoveFromHierarchy();
        }

        static string StateText(RepoSnapshot s)
        {
            switch (s.Operation)
            {
                case RepoOperation.Merging: return "Merging";
                case RepoOperation.Rebasing: return "Rebasing";
                case RepoOperation.CherryPicking: return "Cherry-picking";
                case RepoOperation.Reverting: return "Reverting";
                case RepoOperation.Bisecting: return "Bisecting";
                default:
                    return s.Changes.Count == 0 ? "Clean" : $"{PanelParts.Files(s.Changes.Count)} changed";
            }
        }

        Label Row(string caption, string? name = null) => Row(caption, name, out _);

        Label Row(string caption, string? name, out VisualElement row)
        {
            row = new VisualElement();
            row.AddToClassList("unitygit-row");
            var captionLabel = new Label(caption);
            captionLabel.AddToClassList("unitygit-caption");
            var value = new Label { name = name };
            value.AddToClassList("unitygit-value");
            value.selection.isSelectable = true;
            row.Add(captionLabel);
            row.Add(value);
            Add(row);
            return value;
        }
    }
}
