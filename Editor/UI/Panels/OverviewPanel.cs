#nullable enable
using System.Linq;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>Cards for the branch, the working tree, the identity, the remotes and LFS.</summary>
    internal sealed class OverviewPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly Label _branch;
        readonly Label _ahead;
        readonly Label _behind;
        readonly Label _state;
        readonly Label _headSha;
        readonly Label _headSubject;
        readonly Label _user;
        readonly VisualElement _remotes;
        string? _remotesKey;
        readonly EditorUiCard _lfsCard;
        readonly Label _lfs;
        readonly Button _lfsSetup;

        public OverviewPanel(GitSession session)
        {
            _session = session;
            AddToClassList(PanelClasses.Content);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList(PanelClasses.Scroll);
            Add(scroll);

            var branchCard = EditorUiElements.Card("Branch");
            _branch = Selectable(new Label { name = UiNames.OverviewBranch });
            _branch.AddToClassList(EditorUiClasses.Strong);
            _branch.AddToClassList("unitygit-overview-branch-name");
            _ahead = EditorUiElements.Chip(string.Empty, EditorUiTone.Info);
            _behind = EditorUiElements.Chip(string.Empty, EditorUiTone.Warning);
            var branchLine = new VisualElement();
            branchLine.AddToClassList(PanelClasses.Inline);
            branchLine.Add(_branch);
            branchLine.Add(_ahead);
            branchLine.Add(_behind);
            branchCard.Add(branchLine);
            scroll.Add(branchCard);

            var treeCard = EditorUiElements.Card("Working tree");
            _state = EditorUiElements.Badge(string.Empty, EditorUiTone.Neutral);
            treeCard.Header.Add(_state);
            _headSha = PanelParts.Text(null, EditorUiClasses.Mono, EditorUiClasses.Muted, "unitygit-overview-sha");
            _headSubject = Selectable(PanelParts.Text(null, PanelClasses.Value));
            treeCard.Add(PanelParts.Field("HEAD", _headSha, _headSubject));
            scroll.Add(treeCard);

            var identityCard = EditorUiElements.Card("Identity");
            _user = Selectable(new Label { name = UiNames.OverviewUser });
            _user.AddToClassList(PanelClasses.Value);
            identityCard.Add(_user);
            scroll.Add(identityCard);

            var remotesCard = EditorUiElements.Card("Remotes");
            _remotes = remotesCard.Content;
            scroll.Add(remotesCard);

            _lfsCard = EditorUiElements.Card("LFS");
            _lfs = EditorUiElements.Badge(string.Empty, EditorUiTone.Neutral);
            _lfs.name = UiNames.OverviewLfs;
            _lfsCard.Header.Add(_lfs);
            _lfsSetup = new Button(() => _ = _session.SetUpLfsAsync()) { name = UiNames.LfsSetupButton, text = "Set up LFS" };
            _lfsSetup.AddToClassList(EditorUiClasses.ButtonPrimary);
            GitIcons.Apply(_lfsSetup, GitIcon.Lfs);
            scroll.Add(_lfsCard);
            Render();
        }

        public void Render()
        {
            var s = _session.Snapshot;
            _branch.text = ActionLabels.Branch(s) + (s.Upstream != null ? $" → {s.Upstream}" : "");
            _ahead.text = $"↑{s.Ahead}";
            PanelParts.SetDisplayed(_ahead, s.Ahead != 0);
            _behind.text = $"↓{s.Behind}";
            PanelParts.SetDisplayed(_behind, s.Behind != 0);

            EditorUiElements.SetBadge(_state, StateText(s), StateTone(s));
            var head = s.RecentCommits.Count > 0 ? s.RecentCommits[0] : null;
            _headSha.text = head?.ShortSha ?? string.Empty;
            PanelParts.SetDisplayed(_headSha, head != null);
            _headSubject.text = head?.Subject ?? "No commits yet";
            _headSubject.EnableInClassList(EditorUiClasses.Muted, head == null);

            var configured = s.UserName != null || s.UserEmail != null;
            _user.text = configured ? $"{s.UserName} <{s.UserEmail}>" : "Not configured";
            _user.EnableInClassList(EditorUiClasses.Muted, !configured);

            RenderRemotes(s);

            EditorUiElements.SetBadge(_lfs, LfsText(s.Lfs), LfsTone(s.Lfs));
            var canSetUp = _session.GetAvailability(GitAction.SetUpLfs).Enabled;
            if (canSetUp && _lfsSetup.parent == null) _lfsCard.Add(_lfsSetup);
            else if (!canSetUp && _lfsSetup.parent != null) _lfsSetup.RemoveFromHierarchy();
            PanelParts.SetDisplayed(_lfsCard.Content, canSetUp);
            _lfsCard.EnableInClassList(PanelClasses.HeaderOnly, !canSetUp);
        }

        // Rebuilt only when the remotes change, so a selection in a URL survives refreshes.
        void RenderRemotes(RepoSnapshot s)
        {
            var key = string.Join("\n", s.Remotes.Select(r => $"{r.Name} {r.FetchUrl}"));
            if (key == _remotesKey) return;
            _remotesKey = key;
            _remotes.Clear();
            if (s.Remotes.Count == 0)
            {
                _remotes.Add(PanelParts.Text("—", EditorUiClasses.Faint));
                return;
            }
            foreach (var remote in s.Remotes)
                _remotes.Add(PanelParts.Field(remote.Name,
                    Selectable(PanelParts.Text(PanelParts.WithoutCredentials(remote.FetchUrl), EditorUiClasses.Mono, EditorUiClasses.Small, PanelClasses.Value))));
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

        static EditorUiTone StateTone(RepoSnapshot s)
        {
            if (s.HasConflicts) return EditorUiTone.Error;
            if (s.Operation != RepoOperation.None) return EditorUiTone.Warning;
            return s.Changes.Count == 0 ? EditorUiTone.Success : EditorUiTone.Info;
        }

        static string LfsText(LfsInfo lfs)
            => !lfs.UsedByRepo ? "Not used" : lfs.HooksInstalled ? "Used, set up" : "Used — not set up";

        static EditorUiTone LfsTone(LfsInfo lfs)
            => !lfs.UsedByRepo ? EditorUiTone.Neutral : lfs.HooksInstalled ? EditorUiTone.Success : EditorUiTone.Warning;

        static Label Selectable(Label label)
        {
            label.selection.isSelectable = true;
            return label;
        }
    }
}
