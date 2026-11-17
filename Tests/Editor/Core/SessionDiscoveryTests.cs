#nullable enable
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
    }
}
