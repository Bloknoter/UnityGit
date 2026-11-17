#nullable enable
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class SnapshotStateTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

        [Test] public async Task UnbornRepo_HasBranchAndNoHead()
        {
            var repo = await _sb.InitRepoAsync("p");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Branch, Is.EqualTo("main"));
            Assert.That(s.Snapshot.IsUnborn, Is.True);
            Assert.That(s.Snapshot.HeadSha, Is.Null);
            Assert.That(s.Snapshot.Changes, Is.Empty);
        }

        [Test] public async Task AfterCommit_HeadMatchesGit()
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "a.txt", "a", "first");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.HeadSha, Is.EqualTo(await repo.ResolveRevisionAsync("HEAD")));
            Assert.That(s.Snapshot.IsUnborn, Is.False);
        }

        [Test] public async Task DetachedHead_IsReported()
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "a.txt", "a", "first");
            (await repo.RunRawAsync("switch", "--detach", "HEAD")).EnsureSuccess();
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.IsDetached, Is.True);
        }

        [Test] public async Task Upstream_AheadAndBehind()
        {
            var trio = await _sb.CreateTrioAsync();
            await GitSandbox.CommitFileAsync(trio.Local, "local.txt", "x", "local commit");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Ahead, Is.EqualTo(1));
            Assert.That(s.Snapshot.Upstream, Is.EqualTo("origin/main"));

            await GitSandbox.CommitFileAsync(trio.Teammate, "mate.txt", "y", "mate commit");
            GitSandbox.EnsurePushed(await trio.Teammate.PushAsync());
            await trio.Local.FetchAsync();
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Behind, Is.EqualTo(1));
        }

        [Test] public async Task Changes_ArePairedAndKinded()
        {
            var trio = await _sb.CreateTrioAsync();
            GitSandbox.WriteFile(trio.Local, "Assets/A.txt", "a");
            GitSandbox.WriteFile(trio.Local, "Assets/A.txt.meta", "guid: 1");
            GitSandbox.WriteFile(trio.Local, "README.md", "changed\n");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            var a = s.Snapshot.Changes.Single(c => c.Path == "Assets/A.txt");
            Assert.That(a.Kind, Is.EqualTo(ChangeKind.Untracked));
            Assert.That(a.HasMeta, Is.True);
            Assert.That(s.Snapshot.Changes.Single(c => c.Path == "README.md").Kind, Is.EqualTo(ChangeKind.Modified));
        }

        [Test] public async Task MergeConflict_IsMergingWithConflictedRow()
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.PrepareConflictAsync();
            await trio.Local.RunRawAsync("pull", "--no-rebase");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Operation, Is.EqualTo(RepoOperation.Merging));
            Assert.That(s.Snapshot.HasConflicts, Is.True);
            Assert.That(s.Snapshot.Changes.Single(c => c.Path == "README.md").IsConflicted, Is.True);
        }
    }
}
