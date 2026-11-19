#nullable enable
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class PushFetchLfsTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

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

        async Task<(RemoteTrio trio, GitSession s, FakeEditorHooks hooks)> CreateAsync()
        {
            var trio = await _sb.CreateTrioAsync();
            var hooks = new FakeEditorHooks();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, hooks);
            return (trio, s, hooks);
        }

        [Test] public async Task Push_NewBranch_SetsUpstream()
        {
            var (trio, s, _) = await CreateAsync();
            await SwitchCreate(trio.Local, "feature");
            await GitSandbox.CommitFileAsync(trio.Local, "f.txt", "f", "feature work");
            await s.RefreshAsync();

            var outcome = await s.PushAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Succeeded), outcome.Message);
            Assert.That(outcome.Message, Is.EqualTo("Pushed to origin/feature"));
            Assert.That(s.Snapshot.Upstream, Is.EqualTo("origin/feature"));
            Assert.That(s.Snapshot.Ahead, Is.EqualTo(0));
        }

        // The push works but the refresh after it fails, so the snapshot still has no upstream to name.
        [Test] public async Task Push_RefreshFailsAfterPush_SaysPushed()
        {
            var (trio, s, hooks) = await CreateAsync();
            await SwitchCreate(trio.Local, "feature");
            await GitSandbox.CommitFileAsync(trio.Local, "f.txt", "f", "feature work");
            await s.RefreshAsync();
            hooks.OnEndGitOperation = () => System.IO.File.WriteAllText(System.IO.Path.Combine(trio.Local.GitDirectory, "index"), "garbage");

            var outcome = await s.PushAsync();

            Assert.That((outcome.Kind, outcome.Message), Is.EqualTo((OutcomeKind.Succeeded, "Pushed")));
        }

        [Test] public async Task Push_Normal_ClearsAhead()
        {
            var (trio, s, _) = await CreateAsync();
            await GitSandbox.CommitFileAsync(trio.Local, "a.txt", "a", "local work");
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Ahead, Is.EqualTo(1));

            var outcome = await s.PushAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Succeeded), outcome.Message);
            Assert.That(outcome.Message, Is.EqualTo("Pushed to origin/main"));
            Assert.That(s.Snapshot.Ahead, Is.EqualTo(0));
        }

        [Test] public async Task Push_Rejected_IsOutcomeNotException()
        {
            var (trio, s, _) = await CreateAsync();
            await GitSandbox.CommitFileAsync(trio.Teammate, "t.txt", "t", "teammate work");
            GitSandbox.EnsurePushed(await trio.Teammate.PushAsync());
            await GitSandbox.CommitFileAsync(trio.Local, "a.txt", "a", "local work");
            await s.RefreshAsync();

            var outcome = await s.PushAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Failed));
            Assert.That(outcome.Message, Is.EqualTo("Push rejected: someone pushed first — pull, then push"));
        }

        [Test] public async Task Push_ToMissingRemote_FailsAndPairsHooks()
        {
            var (trio, s, hooks) = await CreateAsync();
            Ok(await trio.Local.RunRawAsync("remote", "set-url", "origin", System.IO.Path.Combine(_sb.Root, "nope.git")));
            await GitSandbox.CommitFileAsync(trio.Local, "a.txt", "a", "local work");
            await s.RefreshAsync();

            var outcome = await s.PushAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Failed), outcome.Message);
            Assert.That(hooks.Count("Begin"), Is.EqualTo(1));
            Assert.That(hooks.Count("End"), Is.EqualTo(1));
        }

        [Test] public async Task Fetch_UpdatesBehindAndRemoteBranches()
        {
            var (trio, s, _) = await CreateAsync();
            await GitSandbox.CommitFileAsync(trio.Teammate, "t.txt", "t", "teammate work");
            GitSandbox.EnsurePushed(await trio.Teammate.PushAsync());
            await SwitchCreate(trio.Teammate, "feature/login");
            await GitSandbox.CommitFileAsync(trio.Teammate, "l.txt", "l", "login work");
            GitSandbox.EnsurePushed(await trio.Teammate.PushAsync());
            await s.RefreshAsync();

            var outcome = await s.FetchAsync();

            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Succeeded), outcome.Message);
            Assert.That(outcome.Message, Is.EqualTo("Fetch complete"));
            Assert.That(s.Snapshot.Behind, Is.EqualTo(1));
            Assert.That(s.Snapshot.RemoteBranches.Select(b => b.Name), Does.Contain("origin/feature/login"));
        }

        [Test] public async Task Push_LfsWithoutHooks_RefusedThenSetUpLfsAllowsPush()
        {
            var (trio, s, _) = await CreateAsync();
            if (!trio.Local.Lfs.IsInstalled) Assert.Ignore("git-lfs isn't installed");
            await GitSandbox.CommitFileAsync(trio.Local, ".gitattributes", "*.psd filter=lfs diff=lfs merge=lfs -text\n", "track psd");
            await s.RefreshAsync();

            var refused = await s.PushAsync();
            Assert.That(refused.Kind, Is.EqualTo(OutcomeKind.Refused));
            Assert.That(refused.Message, Is.EqualTo("LFS isn't set up for this repository"));

            var setUp = await s.SetUpLfsAsync();
            Assert.That(setUp.Kind, Is.EqualTo(OutcomeKind.Succeeded), setUp.Message);
            Assert.That(setUp.Message, Is.EqualTo("LFS set up for this repository"));

            var pushed = await s.PushAsync();
            Assert.That(pushed.Kind, Is.EqualTo(OutcomeKind.Succeeded), pushed.Message);
        }
    }
}
