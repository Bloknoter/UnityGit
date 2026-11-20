#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ConflictFinishTests
    {
        GitSandbox _sb = null!;
        FakeEditorHooks _hooks = null!;
        FlexGit.GitRepository _repo = null!;
        GitSession _s = null!;

        [SetUp] public async Task SetUp() { _sb = await GitSandbox.CreateAsync(); _hooks = new FakeEditorHooks(); }
        [TearDown] public void TearDown() => _sb.Dispose();

        async Task MergeAsync(params ConflictFile[] files)
        {
            _repo = await _sb.InitRepoAsync("repo");
            await ConflictScenarios.MergeAsync(_repo, files);
            _s = _sb.CreateSession(_repo.WorkingDirectory, _hooks);
            await _s.RefreshAsync();
        }

        // A rebase pull that stops at the first local commit. Returns the local HEAD from before the pull.
        async Task<string> RebasePullAsync(bool twoSteps = false)
        {
            var trio = await _sb.CreateTrioAsync();
            _repo = trio.Local;
            await trio.Local.Config.SetAsync("pull.rebase", "true");
            if (twoSteps) await trio.PrepareTwoStepRebaseConflictAsync();
            else await trio.PrepareConflictAsync();
            var local = (await trio.Local.ResolveRevisionAsync("HEAD"))!;
            _s = _sb.CreateSession(trio.Local.WorkingDirectory, _hooks);
            await _s.RefreshAsync();
            var pull = await _s.PullAsync();
            Assert.That(pull.Kind, Is.EqualTo(OutcomeKind.Failed), pull.Message);
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.Rebasing));
            return local;
        }

        async Task ResolveAsync(string path, ConflictPick pick)
        {
            var view = (await _s.LoadConflictFileAsync(path))!;
            var o = await _s.ApplyPicksAsync(view, new Dictionary<int, ConflictPick> { [0] = pick });
            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded), o.Message);
        }

        string Read(string path) => File.ReadAllText(Path.Combine(_repo.WorkingDirectory, path));

        [Test] public async Task Continue_Merge_CommitsTheMerge()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var view = (await _s.LoadConflictFileAsync("f.txt"))!;
            await _s.ApplyPicksAsync(view, new Dictionary<int, ConflictPick> { [0] = ConflictPick.Ours, [1] = ConflictPick.Ours });

            var o = await _s.ContinueAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Merge finished")));
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.None));
            var parents = await _repo.RunRawAsync("rev-list", "--parents", "-n", "1", "HEAD");
            Assert.That(parents.StandardOutput.Trim().Split(' ').Length, Is.EqualTo(3));
            Assert.That(_hooks.Calls, Does.Not.Contain("ConfirmScenes"));
        }

        [Test] public async Task Continue_WithConflictsLeft_Refused()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);

            var o = await _s.ContinueAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Resolve all conflicts first")));
        }

        [Test] public async Task Continue_Rebase_Finishes()
        {
            await RebasePullAsync();
            await ResolveAsync("README.md", ConflictPick.Theirs);

            var o = await _s.ContinueAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Rebase finished")));
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.None));
            Assert.That(Read("README.md"), Is.EqualTo("local line\n"));
            Assert.That(_hooks.Count("ConfirmScenes"), Is.EqualTo(2));
            Assert.That(_hooks.Count("RefreshAssets"), Is.GreaterThanOrEqualTo(2));
        }

        [Test] public async Task Continue_Rebase_StopsAgain_ListsConflicts()
        {
            await RebasePullAsync(twoSteps: true);
            await ResolveAsync("README.md", ConflictPick.Theirs);

            var o = await _s.ContinueAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Failed, "Rebase stopped again with conflicts in 1 file — resolve them in Conflicts")));
            Assert.That(_s.Snapshot.ConflictedPaths, Is.EqualTo(new[] { "other.txt" }));
            Assert.That(o.ConflictedPaths, Is.EqualTo(new[] { "other.txt" }));
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.Rebasing));
        }

        [Test] public async Task Continue_Rebase_ScenesDeclined_Cancelled()
        {
            await RebasePullAsync();
            await ResolveAsync("README.md", ConflictPick.Theirs);
            _hooks.ConfirmScenesResult = false;

            var o = await _s.ContinueAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Cancelled, "Continue cancelled — modified scenes weren't saved")));
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.Rebasing));
        }

        [Test] public async Task Abort_Merge_RestoresTheBranch()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var head = await _repo.ResolveRevisionAsync("HEAD");
            _hooks.Calls.Clear();

            var o = await _s.AbortAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Merge aborted")));
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.None));
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
            Assert.That(Read("f.txt"), Is.EqualTo("one\nTWO main\nthree\nfour\nfive\nsix\nseven\nEIGHT main\nnine\n"));
            Assert.That(await _repo.ResolveRevisionAsync("HEAD"), Is.EqualTo(head));
            Assert.That(_hooks.Calls.Take(6), Is.EqualTo(new[]
                { "Confirm:Abort the merge?", "SaveAssets", "ConfirmScenes", "Begin", "End", "RefreshAssets" }));
        }

        [Test] public async Task Abort_Declined_ChangesNothing()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            _hooks.ConfirmResult = false;

            var o = await _s.AbortAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Cancelled, "Abort cancelled")));
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.Merging));
            Assert.That(_hooks.Calls, Does.Not.Contain("SaveAssets"));
        }

        [Test] public async Task Abort_ScenesDeclined_ChangesNothing()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            _hooks.ConfirmScenesResult = false;

            var o = await _s.AbortAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Cancelled, "Abort cancelled — modified scenes weren't saved")));
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.Merging));
        }

        [Test] public async Task Abort_Rebase_RestoresTheBranch()
        {
            var local = await RebasePullAsync();

            var o = await _s.AbortAsync();

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Rebase aborted")));
            Assert.That(_s.Snapshot.Operation, Is.EqualTo(RepoOperation.None));
            Assert.That(Read("README.md"), Is.EqualTo("local line\n"));
            Assert.That(await _repo.ResolveRevisionAsync("HEAD"), Is.EqualTo(local));
            Assert.That(_hooks.Calls, Does.Contain("Confirm:Abort the rebase?"));
        }
    }
}
