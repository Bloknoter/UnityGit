#nullable enable
using System.IO;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class PullModeResolverTests
    {
        [TestCase("true", null, PullMode.Rebase)]
        [TestCase("merges", null, PullMode.Rebase)]
        [TestCase("TRUE", null, PullMode.Rebase)]
        [TestCase("false", "only", PullMode.FastForwardOnly)]
        [TestCase(null, "only", PullMode.FastForwardOnly)]
        [TestCase(null, null, PullMode.Merge)]
        [TestCase("false", null, PullMode.Merge)]
        [TestCase("yes", null, PullMode.Rebase)]
        [TestCase("on", null, PullMode.Rebase)]
        [TestCase("1", null, PullMode.Rebase)]
        [TestCase(" Yes ", null, PullMode.Rebase)]
        [TestCase("no", "only", PullMode.FastForwardOnly)]
        [TestCase("0", null, PullMode.Merge)]
        public void Resolve(string? rebase, string? ff, PullMode expected)
            => Assert.That(PullModeResolver.Resolve(rebase, ff), Is.EqualTo(expected));
    }

    [TestFixture, Category("Integration")]
    public class PullSwitchTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

        async Task<(RemoteTrio trio, GitSession s, FakeEditorHooks hooks)> CreateAsync()
        {
            var trio = await _sb.CreateTrioAsync();
            var hooks = new FakeEditorHooks();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, hooks);
            return (trio, s, hooks);
        }

        static async Task TeammatePushesAsync(RemoteTrio trio, string path, string content)
        {
            await GitSandbox.CommitFileAsync(trio.Teammate, path, content, "teammate work");
            GitSandbox.EnsurePushed(await trio.Teammate.PushAsync());
        }

        static GitResult Ok(GitResult r)
        {
            Assert.That(r.Succeeded, Is.True, $"{r.CommandLine} failed with exit code {r.ExitCode}: {r.StandardError}");
            return r;
        }

        static async Task SwitchCreate(GitRepository repo, string branch)
        {
            var r = await repo.Branches.SwitchAsync(branch, new SwitchOptions { Create = true });
            Assert.That(r.Succeeded, Is.True, r.Message);
        }

        [Test] public async Task Pull_FastForward_RefreshesAssets()
        {
            var (trio, s, hooks) = await CreateAsync();
            await TeammatePushesAsync(trio, "t.txt", "t");
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Succeeded), outcome.Message);
            Assert.That(outcome.Message, Is.EqualTo("Pulled latest changes"));
            Assert.That(File.Exists(Path.Combine(trio.Local.WorkingDirectory, "t.txt")), Is.True);
            Assert.That(s.Snapshot.Behind, Is.EqualTo(0));
            Assert.That(hooks.Calls, Is.EqualTo(new[] { "SaveAssets", "ConfirmScenes", "Begin", "End", "RefreshAssets" }));
        }

        [Test] public async Task Pull_UpToDate_NoAssetRefresh()
        {
            var (_, s, hooks) = await CreateAsync();
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Succeeded), outcome.Message);
            Assert.That(outcome.Message, Is.EqualTo("Already up to date"));
            Assert.That(hooks.Count("RefreshAssets"), Is.EqualTo(0));
        }

        [Test] public async Task Pull_Conflict_ListsFiles()
        {
            var (trio, s, hooks) = await CreateAsync();
            await trio.PrepareConflictAsync();
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Failed));
            Assert.That(outcome.Message, Is.EqualTo("Pull stopped with conflicts in 1 file — resolve them in Conflicts"));
            Assert.That(outcome.ConflictedPaths, Is.EqualTo(new[] { "README.md" }));
            Assert.That(s.Snapshot.Operation, Is.EqualTo(RepoOperation.Merging));
            Assert.That(hooks.Count("RefreshAssets"), Is.EqualTo(1));
        }

        [Test] public async Task Pull_RebaseConfig_ConflictIsRebasing()
        {
            var (trio, s, _) = await CreateAsync();
            await trio.Local.Config.SetAsync("pull.rebase", "true");
            await trio.PrepareConflictAsync();
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Failed), outcome.Message);
            Assert.That(s.Snapshot.Operation, Is.EqualTo(RepoOperation.Rebasing));
            Assert.That(outcome.ConflictedPaths, Is.EqualTo(new[] { "README.md" }));
        }

        [Test] public async Task Pull_FfOnlyDiverged_CannotFastForward()
        {
            var (trio, s, _) = await CreateAsync();
            await trio.Local.Config.SetAsync("pull.ff", "only");
            await TeammatePushesAsync(trio, "a.txt", "a");
            await GitSandbox.CommitFileAsync(trio.Local, "b.txt", "b", "local work");
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Failed));
            Assert.That(outcome.Message, Is.EqualTo("Can't fast-forward — your branch and the remote have diverged"));
        }

        // git names the blocking files in its error output; the outcome's Details carry it.
        [Test] public async Task Pull_LocalChangesBlocked_ListsFilesInDetails()
        {
            var (trio, s, _) = await CreateAsync();
            await TeammatePushesAsync(trio, "README.md", "teammate line");
            GitSandbox.WriteFile(trio.Local, "README.md", "mine");
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Failed));
            Assert.That(outcome.Message, Is.EqualTo("Pull blocked: your local changes would be overwritten — commit or stash them first"));
            Assert.That(outcome.Details ?? "(no details)", Does.Contain("README.md"));
            Assert.That(File.ReadAllText(Path.Combine(trio.Local.WorkingDirectory, "README.md")), Is.EqualTo("mine"));
        }

        [Test] public async Task Pull_ScenesDeclined_NothingPulled()
        {
            var (trio, s, hooks) = await CreateAsync();
            hooks.ConfirmScenesResult = false;
            await TeammatePushesAsync(trio, "t.txt", "t");
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Cancelled));
            Assert.That(outcome.Message, Is.EqualTo("Pull cancelled — modified scenes weren't saved"));
            var fetched = await s.FetchAsync();
            Assert.That(fetched.Kind, Is.EqualTo(OutcomeKind.Succeeded), fetched.Message);
            Assert.That(s.Snapshot.Behind, Is.EqualTo(1));
        }

        [Test] public async Task Pull_CancelledDuringSave_ReturnsCancelled()
        {
            var (_, s, hooks) = await CreateAsync();
            hooks.OnSaveAssets = () => s.Cancel();
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Cancelled));
            Assert.That(outcome.Message, Is.EqualTo("Pull cancelled"));
            Assert.That(hooks.Count("RefreshAssets"), Is.EqualTo(0));
        }

        // The pull starts, then git fails at the unreachable remote: its result doesn't prove the files are untouched.
        [Test] public async Task Pull_GitFailsMidway_StillRefreshesAssets()
        {
            var (trio, s, hooks) = await CreateAsync();
            Ok(await trio.Local.RunRawAsync("remote", "set-url", "origin", Path.Combine(_sb.Root, "nope.git")));
            await s.RefreshAsync();

            var outcome = await s.PullAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Failed), outcome.Message);
            Assert.That(hooks.Calls, Is.EqualTo(new[] { "SaveAssets", "ConfirmScenes", "Begin", "End", "RefreshAssets" }));
        }

        // Switch has no reads before its git call, so a cancel during Begin is thrown by the git call itself.
        [Test] public async Task Switch_CancelledOnceGitCallStarted_StillRefreshesAssets()
        {
            var (trio, s, hooks) = await CreateAsync();
            await trio.Local.Branches.CreateAsync("feature");
            hooks.OnBeginGitOperation = () => s.Cancel();
            await s.RefreshAsync();

            var outcome = await s.SwitchBranchAsync("feature");

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Cancelled));
            Assert.That(outcome.Message, Is.EqualTo("Switch cancelled"));
            Assert.That(hooks.Calls, Is.EqualTo(new[] { "SaveAssets", "ConfirmScenes", "Begin", "End", "RefreshAssets" }));
            Assert.That(s.Snapshot.Branch, Is.EqualTo("main"));
        }

        [Test] public async Task Switch_ToLocalBranch()
        {
            var (trio, s, hooks) = await CreateAsync();
            await trio.Local.Branches.CreateAsync("feature");
            await s.RefreshAsync();

            var outcome = await s.SwitchBranchAsync("feature");

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Succeeded), outcome.Message);
            Assert.That(outcome.Message, Is.EqualTo("Switched to feature"));
            Assert.That(s.Snapshot.Branch, Is.EqualTo("feature"));
            Assert.That(hooks.Count("RefreshAssets"), Is.EqualTo(1));
        }

        [Test] public async Task Switch_ToRemoteOnlyBranch_CreatesTracking()
        {
            var (trio, s, _) = await CreateAsync();
            await SwitchCreate(trio.Teammate, "feature/login");
            await TeammatePushesAsync(trio, "l.txt", "l");
            var fetched = await trio.Local.FetchAsync();
            Assert.That(fetched.Succeeded, Is.True, fetched.Message);
            await s.RefreshAsync();

            var outcome = await s.SwitchBranchAsync("feature/login");

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Succeeded), outcome.Message);
            Assert.That(s.Snapshot.Branch, Is.EqualTo("feature/login"));
            Assert.That(s.Snapshot.Upstream, Is.EqualTo("origin/feature/login"));
        }

        [Test] public async Task Switch_BlockedByLocalChanges_KeepsFile()
        {
            var (trio, s, hooks) = await CreateAsync();
            await SwitchCreate(trio.Local, "feature");
            await GitSandbox.CommitFileAsync(trio.Local, "README.md", "feature line\n", "feature edit");
            var back = await trio.Local.Branches.SwitchAsync("main");
            Assert.That(back.Succeeded, Is.True, back.Message);
            GitSandbox.WriteFile(trio.Local, "README.md", "mine");
            await s.RefreshAsync();

            var outcome = await s.SwitchBranchAsync("feature");

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Failed));
            Assert.That(outcome.Message, Is.EqualTo("Switch blocked: your local changes would be overwritten — commit or stash them first"));
            Assert.That(outcome.Details ?? "(no details)", Does.Contain("README.md"));
            Assert.That(File.ReadAllText(Path.Combine(trio.Local.WorkingDirectory, "README.md")), Is.EqualTo("mine"));
            Assert.That(s.Snapshot.Branch, Is.EqualTo("main"));
            Assert.That(hooks.Count("RefreshAssets"), Is.EqualTo(0));
        }
    }
}
