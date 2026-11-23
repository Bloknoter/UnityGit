#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class RevertTests
    {
        GitSandbox _sb = null!;
        FakeEditorHooks _hooks = null!;
        GitRepository _repo = null!;

        [SetUp] public async Task SetUp() { _sb = await GitSandbox.CreateAsync(); _hooks = new FakeEditorHooks(); }

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        // A repo with these files committed (one commit each), and a session that has refreshed. With no files there is no
        // commit: the repo is unborn.
        async Task<GitSession> RepoAsync(params (string Path, string Content)[] committed)
        {
            _repo = await _sb.InitRepoAsync("repo");
            foreach (var (path, content) in committed) await GitSandbox.CommitFileAsync(_repo, path, content, "add " + path);
            return _sb.CreateSession(_repo.WorkingDirectory, _hooks);
        }

        // Writes the files, then refreshes so the session lists them.
        async Task WriteAndRefreshAsync(GitSession s, params (string Path, string Content)[] files)
        {
            foreach (var (path, content) in files) GitSandbox.WriteFile(_repo, path, content);
            await s.RefreshAsync();
        }

        // A stash pop stopped on a conflict in f.txt: conflicted, with no merge or rebase in progress.
        async Task<GitSession> StashPopConflictAsync()
        {
            var s = await RepoAsync(("f.txt", "base\n"));
            GitSandbox.WriteFile(_repo, "f.txt", "stash\n");
            Assert.That((await _repo.RunRawAsync("stash")).ExitCode, Is.EqualTo(0), "setup: stash");
            await GitSandbox.CommitFileAsync(_repo, "f.txt", "main\n", "main edit");
            Assert.That((await _repo.RunRawAsync("stash", "pop")).ExitCode, Is.Not.EqualTo(0), "setup: the pop stops on the conflict");
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Operation, Is.EqualTo(RepoOperation.None), "setup: no operation in progress");
            Assert.That(s.Snapshot.ConflictedPaths, Is.EqualTo(new[] { "f.txt" }), "setup: f.txt is conflicted");
            return s;
        }

        string FullPath(string path) => Path.Combine(_repo.WorkingDirectory, path);
        string Read(string path) => File.ReadAllText(FullPath(path));
        bool Exists(string path) => File.Exists(FullPath(path));
        static IEnumerable<string> Rows(GitSession s) => s.Snapshot.Changes.Select(c => c.Path);

        static void AssertOutcome(ActionOutcome o, OutcomeKind kind, string message)
            => Assert.That((o.Kind, o.Message), Is.EqualTo((kind, message)), o.Details);

        [Test] public async Task Revert_ModifiedFile_RestoresHead()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"));

            var o = await s.RevertAsync(new[] { "a.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That(Read("a.txt"), Is.EqualTo("a\n"));
            Assert.That(Rows(s), Is.Empty);
        }

        [Test] public async Task Revert_StagedAndUnstagedEdits_RestoresHead()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            GitSandbox.WriteFile(_repo, "a.txt", "staged\n");
            await _repo.Index.StageAsync(new[] { "a.txt" });
            await WriteAndRefreshAsync(s, ("a.txt", "more\n"));

            var o = await s.RevertAsync(new[] { "a.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That(Read("a.txt"), Is.EqualTo("a\n"));
            Assert.That(Rows(s), Is.Empty);
        }

        [Test] public async Task Revert_StagedNewFile_Deleted()
        {
            var s = await RepoAsync(("README.md", "readme\n"));
            GitSandbox.WriteFile(_repo, "n.txt", "new\n");
            await _repo.Index.StageAsync(new[] { "n.txt" });
            await s.RefreshAsync();

            var o = await s.RevertAsync(new[] { "n.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That(Exists("n.txt"), Is.False);
            Assert.That(Rows(s), Is.Empty);
        }

        [Test] public async Task Revert_UntrackedFile_Deleted()
        {
            var s = await RepoAsync(("README.md", "readme\n"));
            await WriteAndRefreshAsync(s, ("u.txt", "new\n"));

            var o = await s.RevertAsync(new[] { "u.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That(Exists("u.txt"), Is.False);
            Assert.That(Rows(s), Is.Empty);
        }

        [Test] public async Task Revert_DeletedFile_ComesBack()
        {
            var s = await RepoAsync(("d.txt", "d\n"));
            File.Delete(FullPath("d.txt"));
            await s.RefreshAsync();

            var o = await s.RevertAsync(new[] { "d.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That(Read("d.txt"), Is.EqualTo("d\n"));
        }

        [Test] public async Task Revert_AssetWithMeta_RevertsBoth()
        {
            var s = await RepoAsync(("Assets/A.asset", "1\n"), ("Assets/A.asset.meta", "m1\n"));
            await WriteAndRefreshAsync(s, ("Assets/A.asset", "2\n"), ("Assets/A.asset.meta", "m2\n"));
            Assert.That(Rows(s), Is.EqualTo(new[] { "Assets/A.asset" }), "setup: one paired row");

            var o = await s.RevertAsync(new[] { "Assets/A.asset" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That((Read("Assets/A.asset"), Read("Assets/A.asset.meta")), Is.EqualTo(("1\n", "m1\n")));
        }

        [Test] public async Task Revert_StagedRename_RestoresOldPath()
        {
            var s = await RepoAsync(("old.txt", "old\n"));
            Assert.That((await _repo.RunRawAsync("mv", "old.txt", "new.txt")).ExitCode, Is.EqualTo(0), "setup: git mv");
            await s.RefreshAsync();
            Assert.That(Rows(s), Is.EqualTo(new[] { "new.txt" }), "setup: one rename row");

            var o = await s.RevertAsync(new[] { "new.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That((Exists("old.txt"), Exists("new.txt")), Is.EqualTo((true, false)));
            Assert.That(Rows(s), Is.Empty);
        }

        [Test] public async Task Revert_UnbornRepo_DeletesNewFiles()
        {
            var s = await RepoAsync();
            GitSandbox.WriteFile(_repo, "n.txt", "new\n");
            await _repo.Index.StageAsync(new[] { "n.txt" });
            await s.RefreshAsync();
            Assert.That(s.Snapshot.IsUnborn, Is.True, "setup: no commit yet");

            var o = await s.RevertAsync(new[] { "n.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That(Exists("n.txt"), Is.False);
        }

        [Test] public async Task Revert_PathWithSpacesAndCyrillic_Reverts()
        {
            const string committed = "Assets/Мои сцены/Level 1.txt", added = "Assets/Мои сцены/Новый файл.txt";
            var s = await RepoAsync((committed, "a\n"));
            await WriteAndRefreshAsync(s, (committed, "changed\n"), (added, "new\n"));
            Assert.That(Rows(s), Is.EquivalentTo(new[] { committed, added }), "setup: both listed");

            var o = await s.RevertAsync(new[] { committed, added });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 2 files");
            Assert.That(Read(committed), Is.EqualTo("a\n"));
            Assert.That(Exists(added), Is.False);
        }

        [Test] public async Task Revert_ManyFiles_RevertsAll()
        {
            var s = await RepoAsync();
            var tracked = Enumerable.Range(0, 150).Select(i => $"m{i:000}.txt").ToList();
            foreach (var path in tracked) GitSandbox.WriteFile(_repo, path, "base\n");
            await _repo.Index.StageAsync(tracked);
            Assert.That((await _repo.CommitAsync("many")).Succeeded, Is.True, "setup: one commit");
            foreach (var path in tracked) GitSandbox.WriteFile(_repo, path, "changed\n");
            for (var i = 0; i < 150; i++) GitSandbox.WriteFile(_repo, $"u{i:000}.txt", "new\n");
            await s.RefreshAsync();
            Assert.That(Rows(s).Count(), Is.EqualTo(300), "setup: every file listed");

            var o = await s.RevertAsync(Rows(s).ToList());

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 300 files");
            Assert.That(Rows(s), Is.Empty);
        }

        [Test] public async Task Revert_ConfirmDeclined_NothingChanges()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"));
            _hooks.ConfirmResult = false;

            var o = await s.RevertAsync(new[] { "a.txt" });

            AssertOutcome(o, OutcomeKind.Cancelled, "Revert cancelled");
            Assert.That(Read("a.txt"), Is.EqualTo("changed\n"));
            Assert.That(_hooks.Calls, Does.Not.Contain("Begin"));
        }

        [Test] public async Task Revert_ScenesDeclined_NothingChanges()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"));
            _hooks.ConfirmScenesResult = false;

            var o = await s.RevertAsync(new[] { "a.txt" });

            AssertOutcome(o, OutcomeKind.Cancelled, "Revert cancelled — modified scenes weren't saved");
            Assert.That(Read("a.txt"), Is.EqualTo("changed\n"));
            Assert.That(_hooks.Calls, Does.Not.Contain("Begin"));
        }

        [Test] public async Task Revert_HookOrder()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"));

            await s.RevertAsync(new[] { "a.txt" });

            Assert.That(_hooks.Calls, Is.EqualTo(new[] { "Confirm:Revert a.txt?", "SaveAssets", "ConfirmScenes", "Begin", "End", "RefreshAssets" }));
        }

        [Test] public async Task Revert_InPlayMode_RefusedBeforeConfirm()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"));
            s.IsPlayMode = true;

            var o = await s.RevertAsync(new[] { "a.txt" });

            AssertOutcome(o, OutcomeKind.Refused, "Not available in Play Mode");
            Assert.That(_hooks.Calls, Is.Empty);
        }

        [Test] public async Task Revert_NoRows_Refused()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"));

            var o = await s.RevertAsync(Array.Empty<string>());

            AssertOutcome(o, OutcomeKind.Refused, "Select a file to revert");
            Assert.That(_hooks.Calls, Is.Empty);
        }

        [Test] public async Task Revert_RowNoLongerChanged_Ignored()
        {
            var s = await RepoAsync(("a.txt", "a\n"), ("b.txt", "b\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"), ("b.txt", "changed\n"));
            GitSandbox.WriteFile(_repo, "b.txt", "b\n"); // put back by another tool after the list was shown

            var o = await s.RevertAsync(new[] { "a.txt", "b.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That(_hooks.Calls[0], Is.EqualTo("Confirm:Revert a.txt?"));
        }

        [Test] public async Task Revert_AllRowsGone_RefusedAndRefreshed()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"));
            GitSandbox.WriteFile(_repo, "a.txt", "a\n");

            var o = await s.RevertAsync(new[] { "a.txt" });

            AssertOutcome(o, OutcomeKind.Refused, "Nothing to revert");
            Assert.That(Rows(s), Is.Empty, "the list caught up");
            Assert.That(_hooks.Calls, Is.Empty);
        }

        [Test] public async Task Revert_ConflictedRow_Skipped()
        {
            var s = await StashPopConflictAsync();
            await WriteAndRefreshAsync(s, ("u.txt", "new\n"));

            var o = await s.RevertAsync(new[] { "f.txt", "u.txt" });

            AssertOutcome(o, OutcomeKind.Succeeded, "Reverted 1 file");
            Assert.That(Exists("u.txt"), Is.False);
            Assert.That(s.Snapshot.ConflictedPaths, Is.EqualTo(new[] { "f.txt" }));
            Assert.That(_hooks.Calls[0], Is.EqualTo("Confirm:Revert u.txt?"));
        }

        [Test] public async Task Revert_OnlyConflictedRows_Refused()
        {
            var s = await StashPopConflictAsync();

            var o = await s.RevertAsync(new[] { "f.txt" });

            AssertOutcome(o, OutcomeKind.Refused, "Conflicted files can't be reverted — resolve them in Conflicts");
            Assert.That(_hooks.Calls, Is.Empty);
        }

        [Test] public async Task Revert_IndexLocked_Fails()
        {
            var s = await RepoAsync(("a.txt", "a\n"));
            await WriteAndRefreshAsync(s, ("a.txt", "changed\n"));
            File.WriteAllText(Path.Combine(_repo.WorkingDirectory, ".git", "index.lock"), string.Empty);

            var o = await s.RevertAsync(new[] { "a.txt" });

            AssertOutcome(o, OutcomeKind.Failed, "Another git process is running");
            Assert.That(s.IsBusy, Is.False);
            Assert.That(_hooks.Calls, Does.Contain("End"));
            Assert.That(Read("a.txt"), Is.EqualTo("changed\n"));
        }
    }
}
