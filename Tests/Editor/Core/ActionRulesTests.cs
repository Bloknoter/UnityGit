#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class ActionRulesTests
    {
        static RepoSnapshot Ready(Action<RepoSnapshot>? edit = null)
        {
            var s = new RepoSnapshot { Availability = RepoAvailability.Ready, ProjectPath = "p", Branch = "main", HeadSha = "abc1234",
                Upstream = "origin/main", Remotes = new[] { new RemoteItem { Name = "origin" } },
                Changes = new[] { new ChangeItem { Path = "a.txt", GitPaths = new[] { "a.txt" } } }, Lfs = new LfsInfo() };
            edit?.Invoke(s); return s;
        }
        static string? Why(GitAction a, RepoSnapshot s, bool busy = false, bool play = false) => ActionRules.Evaluate(a, s, busy, play).Reason;

        [Test] public void Busy_DisablesEverything()
            => Assert.That(Enum.GetValues(typeof(GitAction)).Cast<GitAction>().Select(a => Why(a, Ready(), busy: true)), Has.All.EqualTo("Another git action is running"));
        [Test] public void NotARepo_OnlyInitEnabled()
        {
            var s = RepoSnapshot.NotARepository("p");
            Assert.That(ActionRules.Evaluate(GitAction.Init, s, false, false).Enabled, Is.True);
            Assert.That(Why(GitAction.Push, s), Is.EqualTo("This project is not in a git repository"));
        }
        [Test] public void Unavailable_DisablesEverything()
            => Assert.That(Enum.GetValues(typeof(GitAction)).Cast<GitAction>().Select(a => Why(a, RepoSnapshot.Unavailable("p", "fatal: git's own words"))),
                Has.All.EqualTo("Git can't open this repository"));
        [Test] public void GitMissing_UsesSnapshotMessage() => Assert.That(Why(GitAction.Fetch, RepoSnapshot.GitMissing("p", "msg")), Is.EqualTo("msg"));
        [Test] public void Init_WhenReady_Refused() => Assert.That(Why(GitAction.Init, Ready()), Is.EqualTo("Already a git repository"));
        [Test] public void Commit_WithConflicts_Refused()
            => Assert.That(Why(GitAction.Commit, Ready(s => s.Changes = new[] { new ChangeItem { Path = "a", GitPaths = new[] { "a" }, IsConflicted = true } })), Is.EqualTo("Resolve conflicts first"));
        [Test] public void Commit_DuringRebase_Refused() => Assert.That(Why(GitAction.Commit, Ready(s => s.Operation = RepoOperation.Rebasing)), Is.EqualTo("Continue or abort the rebase first"));
        [Test] public void Commit_DuringCherryPick_Refused() => Assert.That(Why(GitAction.Commit, Ready(s => s.Operation = RepoOperation.CherryPicking)), Is.EqualTo("Finish or abort the cherry-pick outside UnityGit"));
        [Test] public void Resolve_NoConflicts_Refused()
            => Assert.That(new[] { Why(GitAction.Resolve, Ready()), Why(GitAction.RestoreConflict, Ready()) }, Has.All.EqualTo("No conflicts to resolve"));
        [Test] public void Resolve_WithConflicts_Allowed()
        {
            var s = Ready(x => x.ConflictedPaths = new[] { "a" });
            Assert.That(new[] { Why(GitAction.Resolve, s), Why(GitAction.RestoreConflict, s) }, Has.All.Null);
        }
        [Test] public void ConflictActions_PlayMode_Refused()
        {
            var s = Ready(x => { x.Operation = RepoOperation.Merging; x.ConflictedPaths = new[] { "a" }; });
            Assert.That(new[] { GitAction.Resolve, GitAction.RestoreConflict, GitAction.Continue, GitAction.Abort }.Select(a => Why(a, s, play: true)),
                Has.All.EqualTo("Not available in Play Mode"));
        }
        [Test] public void Continue_Rules()
        {
            Assert.That(Why(GitAction.Continue, Ready()), Is.EqualTo("Nothing to continue"));
            Assert.That(Why(GitAction.Continue, Ready(s => s.Operation = RepoOperation.CherryPicking)), Is.EqualTo("Finish the cherry-pick outside UnityGit"));
            Assert.That(Why(GitAction.Continue, Ready(s => { s.Operation = RepoOperation.Merging; s.ConflictedPaths = new[] { "a" }; })), Is.EqualTo("Resolve all conflicts first"));
            Assert.That(Why(GitAction.Continue, Ready(s => s.Operation = RepoOperation.Merging)), Is.Null);
            Assert.That(Why(GitAction.Continue, Ready(s => s.Operation = RepoOperation.Rebasing)), Is.Null);
        }
        [Test] public void Abort_Rules()
        {
            Assert.That(Why(GitAction.Abort, Ready()), Is.EqualTo("Nothing to abort"));
            Assert.That(Why(GitAction.Abort, Ready(s => s.Operation = RepoOperation.Reverting)), Is.EqualTo("Abort the revert outside UnityGit"));
            Assert.That(Why(GitAction.Abort, Ready(s => { s.Operation = RepoOperation.Merging; s.ConflictedPaths = new[] { "a" }; })), Is.Null);
            Assert.That(Why(GitAction.Abort, Ready(s => s.Operation = RepoOperation.Rebasing)), Is.Null);
        }
        [Test] public void Commit_DuringMergeWithoutConflicts_Allowed() => Assert.That(Why(GitAction.Commit, Ready(s => s.Operation = RepoOperation.Merging)), Is.Null);
        [Test] public void Commit_NoChanges_Refused() => Assert.That(Why(GitAction.Commit, Ready(s => s.Changes = Array.Empty<ChangeItem>())), Is.EqualTo("Nothing to commit"));
        [Test] public void Push_Detached_Refused() => Assert.That(Why(GitAction.Push, Ready(s => s.IsDetached = true)), Is.EqualTo("Not on a branch"));
        [Test] public void Push_Unborn_Refused() => Assert.That(Why(GitAction.Push, Ready(s => s.IsUnborn = true)), Is.EqualTo("Nothing to push yet"));
        [Test] public void PushFetch_NoRemote_Refused()
        {
            var s = Ready(x => x.Remotes = Array.Empty<RemoteItem>());
            Assert.That(new[] { Why(GitAction.Push, s), Why(GitAction.Fetch, s) }, Has.All.EqualTo("No remote configured"));
        }
        [Test] public void Push_LfsWithoutHooks_Refused()
            => Assert.That(Why(GitAction.Push, Ready(s => s.Lfs = new LfsInfo { UsedByRepo = true, GitLfsAvailable = true })), Is.EqualTo("LFS isn't set up for this repository"));
        [Test] public void PlayMode_BlocksInitPullSwitch_NotCommitPushFetch()
        {
            Assert.That(new[] { Why(GitAction.Pull, Ready(), play: true), Why(GitAction.Switch, Ready(), play: true), Why(GitAction.Init, RepoSnapshot.NotARepository("p"), play: true) },
                Has.All.EqualTo("Not available in Play Mode"));
            Assert.That(new[] { Why(GitAction.Commit, Ready(), play: true), Why(GitAction.Push, Ready(), play: true), Why(GitAction.Fetch, Ready(), play: true) }, Has.All.Null);
        }
        [Test] public void Pull_NoUpstream_Refused() => Assert.That(Why(GitAction.Pull, Ready(s => s.Upstream = null)), Is.EqualTo("This branch has no upstream — push it first"));
        [Test] public void PullSwitch_DuringMerge_Refused()
            => Assert.That(new[] { Why(GitAction.Pull, Ready(s => s.Operation = RepoOperation.Merging)), Why(GitAction.Switch, Ready(s => s.Operation = RepoOperation.Merging)) },
                Has.All.EqualTo("Finish or abort the merge first"));
        [Test] public void SetUpLfs_Rules()
        {
            Assert.That(Why(GitAction.SetUpLfs, Ready()), Is.EqualTo("This repository doesn't use LFS"));
            Assert.That(Why(GitAction.SetUpLfs, Ready(s => s.Lfs = new LfsInfo { UsedByRepo = true })), Is.EqualTo("git-lfs isn't installed — install it from https://git-lfs.com"));
            Assert.That(Why(GitAction.SetUpLfs, Ready(s => s.Lfs = new LfsInfo { UsedByRepo = true, GitLfsAvailable = true, HooksInstalled = true })), Is.EqualTo("LFS is already set up"));
            Assert.That(Why(GitAction.SetUpLfs, Ready(s => s.Lfs = new LfsInfo { UsedByRepo = true, GitLfsAvailable = true })), Is.Null);
        }
    }
}
