#nullable enable
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class SnapshotDetailsTests
    {
        const string LfsAttributes = "*.psd filter=lfs diff=lfs merge=lfs -text\n";

        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

        [Test] public async Task Branches_LocalAndRemote()
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.Local.Branches.CreateAsync("feature/login");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            Assert.That(s.Snapshot.LocalBranches.Select(b => b.Name), Is.EquivalentTo(new[] { "feature/login", "main" }));
            Assert.That(s.Snapshot.LocalBranches.Single(b => b.Name == "main").IsCurrent, Is.True);
            Assert.That(s.Snapshot.RemoteBranches.Select(b => b.Name), Is.EquivalentTo(new[] { "origin/main" }));
            Assert.That(s.Snapshot.RemoteBranches.Single().ShortName, Is.EqualTo("main"));
            Assert.That(s.Snapshot.LocalBranches.Single(b => b.Name == "main").ShortName, Is.EqualTo("main"));
        }

        [Test] public async Task Authors_CountsPerIdentity()
        {
            var trio = await _sb.CreateTrioAsync();
            await GitSandbox.CommitFileAsync(trio.Local, "local.txt", "x", "local commit");
            await GitSandbox.CommitFileAsync(trio.Teammate, "mate.txt", "y", "mate commit");
            GitSandbox.EnsurePushed(await trio.Teammate.PushAsync());
            (await trio.Local.RunRawAsync("pull", "--rebase")).EnsureSuccess();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            var authors = s.Snapshot.Authors.Select(a => (a.Name, a.CommitCount)).ToList();
            Assert.That(authors, Does.Contain(("Test User", 2)));
            Assert.That(authors, Does.Contain(("Teammate", 1)));
        }

        [Test] public async Task RecentCommits_NewestFirst_Capped()
        {
            var repo = await _sb.InitRepoAsync("p");
            for (var i = 0; i < 55; i++)
                (await repo.RunRawAsync("commit", "--allow-empty", "-m", "c" + i)).EnsureSuccess();
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            Assert.That(s.Snapshot.RecentCommits.Count, Is.EqualTo(SnapshotLoader.RecentCommitLimit));
            Assert.That(s.Snapshot.RecentCommits[0].Subject, Is.EqualTo("c54"));
        }

        [Test] public async Task UnbornRepo_HasNoHistoryAndLogsNothing()
        {
            var repo = await _sb.InitRepoAsync("p");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            var logged = new System.Collections.Generic.List<string>();
            s.Log = logged.Add;
            await s.RefreshAsync();

            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.Ready));
            Assert.That(s.Snapshot.RecentCommits, Is.Empty);
            Assert.That(s.Snapshot.Authors, Is.Empty);
            Assert.That(logged.Where(l => l.StartsWith("git log") || l.StartsWith("git shortlog")), Is.Empty);
        }

        [Test] public async Task RemotesTagsStashes()
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.Local.Tags.CreateAsync("v1");
            GitSandbox.WriteFile(trio.Local, "README.md", "changed\n");
            var stash = await trio.Local.Stash.PushAsync("wip");
            Assert.That(stash.Succeeded, Is.True, stash.Message);
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            Assert.That(s.Snapshot.Remotes.Single().Name, Is.EqualTo("origin"));
            Assert.That(s.Snapshot.Remotes.Single().FetchUrl, Is.EqualTo(trio.Origin.GitDirectory));
            Assert.That(s.Snapshot.Tags.Single().Name, Is.EqualTo("v1"));
            Assert.That(s.Snapshot.Stashes.Single().Message, Does.Contain("wip"));
        }

        [Test] public async Task Identity_FromConfig()
        {
            var repo = await _sb.InitRepoAsync("p");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.UserName, Is.EqualTo("Test User"));
            Assert.That(s.Snapshot.UserEmail, Is.EqualTo("test@example.com"));
        }

        [Test] public async Task Lfs_UsedWithoutHooks()
        {
            var repo = await _sb.InitRepoAsync("p");
            if (!repo.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            GitSandbox.WriteFile(repo, ".gitattributes", LfsAttributes);
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            Assert.That(s.Snapshot.Lfs.GitLfsAvailable, Is.True);
            Assert.That(s.Snapshot.Lfs.UsedByRepo, Is.True);
            Assert.That(s.Snapshot.Lfs.HooksInstalled, Is.False);
        }

        [Test] public async Task Lfs_AfterEnsureInstalled_HooksInstalled()
        {
            var repo = await _sb.InitRepoAsync("p");
            if (!repo.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            GitSandbox.WriteFile(repo, ".gitattributes", LfsAttributes);
            await repo.Lfs.EnsureInstalledAsync();
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            Assert.That(s.Snapshot.Lfs.HooksInstalled, Is.True);
        }

        [Test] public async Task LfsLocks_NotRequested_AreNull()
        {
            var repo = await _sb.InitRepoAsync("p");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.LfsLocks, Is.Null);
        }

        [Test] public async Task LfsLocks_RequestedOnFileRemote_StayNullWithoutThrowing()
        {
            var trio = await _sb.CreateTrioAsync();
            if (!trio.Local.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            GitSandbox.WriteFile(trio.Local, ".gitattributes", LfsAttributes);
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync(true);

            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.Ready));
            // git-lfs answers "[]" (exit 0) for a file:// remote, so no error occurs; the point is that it never throws.
            Assert.That(s.Snapshot.LfsLocks, Is.Null.Or.Empty);
        }

        // An LFS repo whose lock list git-lfs can load: it answers "[]" (exit 0) for the trio's local remote.
        async Task<GitSession> LfsSessionAsync()
        {
            var trio = await _sb.CreateTrioAsync();
            if (!trio.Local.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            GitSandbox.WriteFile(trio.Local, ".gitattributes", LfsAttributes);
            return _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
        }

        // Locks have their own refresh (spec 5.7): a later refresh without them keeps the list the user loaded.
        [Test] public async Task Refresh_KeepsLoadedLfsLocks()
        {
            var s = await LfsSessionAsync();
            await s.RefreshAsync(true);
            Assert.That(s.Snapshot.LfsLocks, Is.Not.Null, "setup: the lock list loaded");

            await s.RefreshAsync();

            Assert.That(s.Snapshot.LfsLocks, Is.Not.Null);
        }

        // "Refresh locks" clicked while an automatic refresh (without locks) runs still loads the locks.
        [Test] public async Task Refresh_LocksRequestedDuringRefresh_LoadsLocks()
        {
            var s = await LfsSessionAsync();

            var plain = s.RefreshAsync();
            var withLocks = s.RefreshAsync(true);
            await Task.WhenAll(plain, withLocks);

            Assert.That(s.Snapshot.LfsLocks, Is.Not.Null);
        }
    }
}
