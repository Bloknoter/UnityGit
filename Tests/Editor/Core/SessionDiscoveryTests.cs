#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class SessionDiscoveryTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        [Test] public void NewSession_BeforeRefresh_IsUnknown()
            => Assert.That(_sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks()).Snapshot.Availability, Is.EqualTo(RepoAvailability.Unknown));

        [Test] public async Task Refresh_FolderWithoutRepo_IsNotARepository()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.NotARepository));
        }

        [Test] public async Task Refresh_RepoAtProjectRoot_IsReady()
        {
            var repo = await _sb.InitRepoAsync("p");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.Ready));
            Assert.That(s.Snapshot.RepoRoot, Is.SamePath(repo.WorkingDirectory));
        }

        [Test] public async Task Refresh_ProjectInRepoSubfolder_FindsRepoRoot()
        {
            var repo = await _sb.InitRepoAsync("repo");
            var s = _sb.CreateSession(_sb.MakeDir("repo/UnityProject"), new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.RepoRoot, Is.SamePath(repo.WorkingDirectory));
        }

        [Test] public async Task Refresh_BadGitPath_IsGitMissing()
        {
            _sb.Options.GitExecutablePath = Path.Combine(_sb.Root, "missing", "git.exe");
            GitLocator.ClearCache();
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.GitMissing));
            Assert.That(s.Snapshot.Message, Is.EqualTo("Git isn't installed or wasn't found — install Git 2.32 or newer"));
        }

        // A repo git refuses to open (here an unsupported format version; on Windows, often "dubious ownership") is not
        // "no repository": the session shows git's reason and offers no Git init on top of it.
        [Test] public async Task Refresh_UnreadableRepository_ShowsUnavailableNotInit()
        {
            var repo = await _sb.InitRepoAsync("p");
            await repo.Config.SetAsync("core.repositoryformatversion", "99");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());

            await s.RefreshAsync();

            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.Unavailable));
            Assert.That(s.Snapshot.Message, Does.Contain("Expected git repo version <= 1"));
            var init = s.GetAvailability(GitAction.Init);
            Assert.That((init.Enabled, init.Reason), Is.EqualTo((false, "Git can't open this repository")));
        }

        // git status fails (a corrupt index) after git opened the repo. With nothing loaded yet, the failure becomes the
        // snapshot, so the window and toolbar don't stay on "Loading…".
        [Test] public async Task Refresh_FirstLoadFails_IsUnavailableAndWarns()
        {
            var repo = await _sb.InitRepoAsync("p");
            File.WriteAllText(Path.Combine(repo.GitDirectory, "index"), "garbage");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            var warnings = new List<string>();
            s.Warning = warnings.Add;

            await s.RefreshAsync();

            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.Unavailable));
            Assert.That(s.Snapshot.Message, Does.Contain("index file smaller than expected"));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("index file smaller than expected"));
        }

        [Test] public async Task Refresh_LaterLoadFails_KeepsSnapshotAndWarns()
        {
            var repo = await _sb.InitRepoAsync("p");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            var warnings = new List<string>();
            s.Warning = warnings.Add;
            await s.RefreshAsync();
            var loaded = s.Snapshot;
            Assert.That(loaded.Availability, Is.EqualTo(RepoAvailability.Ready), "setup");
            File.WriteAllText(Path.Combine(repo.GitDirectory, "index"), "garbage");

            await s.RefreshAsync();

            Assert.That(s.Snapshot, Is.SameAs(loaded));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("index file smaller than expected"));
        }

        // .git moved away while the editor is open: the cached repo is dropped, so the next refresh finds no repo.
        [Test] public async Task Refresh_RepoMovedAway_RediscoversNotARepository()
        {
            var repo = await _sb.InitRepoAsync("p");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.Ready), "setup");
            Directory.Move(repo.GitDirectory, Path.Combine(repo.WorkingDirectory, ".git-moved"));

            await s.RefreshAsync();
            await s.RefreshAsync();

            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.NotARepository));
        }
    }
}
