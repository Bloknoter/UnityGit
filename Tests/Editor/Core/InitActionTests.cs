#nullable enable
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class InitActionTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

        [Test] public async Task Init_CreatesRepoAndGitignore()
        {
            var hooks = new FakeEditorHooks(); var dir = _sb.MakeDir("p"); var s = _sb.CreateSession(dir, hooks);
            await s.RefreshAsync();
            var o = await s.InitAsync();
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Repository created with a Unity .gitignore")));
            Assert.That(File.ReadAllText(Path.Combine(dir, ".gitignore")), Is.EqualTo(GitignoreTemplate.Text));
            Assert.That((s.Snapshot.Availability, s.Snapshot.IsUnborn, s.Snapshot.Branch), Is.EqualTo((RepoAvailability.Ready, true, "main")));
            Assert.That((hooks.Count("Begin"), hooks.Count("End")), Is.EqualTo((1, 1)));
            Assert.That(s.LastOutcome, Is.SameAs(o));
        }

        [Test] public async Task Init_KeepsExistingGitignore()
        {
            var dir = _sb.MakeDir("p");
            File.WriteAllText(Path.Combine(dir, ".gitignore"), "custom");
            var s = _sb.CreateSession(dir, new FakeEditorHooks());
            await s.RefreshAsync();
            var o = await s.InitAsync();
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Repository created — existing .gitignore kept")));
            Assert.That(File.ReadAllText(Path.Combine(dir, ".gitignore")), Is.EqualTo("custom"));
        }

        [Test] public async Task Init_WhenAlreadyRepo_Refused()
        {
            var repo = await _sb.InitRepoAsync("p");
            var hooks = new FakeEditorHooks(); var s = _sb.CreateSession(repo.WorkingDirectory, hooks);
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.Ready));
            var o = await s.InitAsync();
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Already a git repository")));
            Assert.That(hooks.Calls, Is.Empty);
        }

        [Test] public async Task SecondActionWhileBusy_Refused()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks()); await s.RefreshAsync();
            var first = s.InitAsync();
            var second = await s.InitAsync();
            Assert.That((second.Kind, second.Message), Is.EqualTo((OutcomeKind.Refused, "Another git action is running")));
            Assert.That((await first).Succeeded, Is.True);
        }

        [Test] public async Task Changed_FiresAroundAction()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks()); await s.RefreshAsync();
            var changed = 0;
            s.Changed += () => changed++;
            await s.InitAsync();
            Assert.That(changed, Is.GreaterThanOrEqualTo(2));
            Assert.That(s.IsBusy, Is.False);
        }
    }
}
