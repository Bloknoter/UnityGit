#nullable enable
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEditor.Toolbars;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class MainToolbarTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        static MainToolbarElement[] AllElements(GitSession s) => new[]
        {
            MainToolbarButtons.CreateBranch(s), MainToolbarButtons.CreateCommit(s), MainToolbarButtons.CreateFetch(s),
            MainToolbarButtons.CreatePull(s), MainToolbarButtons.CreatePush(s),
        };

        async Task<GitSession> NotARepoSessionAsync()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());
            await s.RefreshAsync();
            return s;
        }

        [Test] public async Task Push_ShowsAheadCount_AndEnabled()
        {
            var trio = await _sb.CreateTrioAsync();
            await GitSandbox.CommitFileAsync(trio.Local, "a.txt", "a", "first");
            await GitSandbox.CommitFileAsync(trio.Local, "b.txt", "b", "second");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            var push = MainToolbarButtons.CreatePush(s);

            Assert.That(push.content.text, Is.EqualTo("Push ↑2"));
            Assert.That(push.enabled, Is.True);
            Assert.That(push.content.tooltip, Is.EqualTo("Push"));
        }

        [Test] public async Task Pull_ShowsBehindCount()
        {
            var trio = await _sb.CreateTrioAsync();
            await GitSandbox.CommitFileAsync(trio.Teammate, "t.txt", "t", "teammate work");
            GitSandbox.EnsurePushed(await trio.Teammate.PushAsync());
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            await s.FetchAsync();

            Assert.That(MainToolbarButtons.CreatePull(s).content.text, Is.EqualTo("Pull ↓1"));
        }

        [Test] public async Task NotARepo_AllDisabled_BranchSaysNoRepository()
        {
            var s = await NotARepoSessionAsync();

            Assert.That(AllElements(s).Select(e => e.enabled), Is.All.False);
            Assert.That(MainToolbarButtons.CreateBranch(s).content.text, Is.EqualTo("No repository"));
            Assert.That(MainToolbarButtons.CreatePush(s).content.tooltip, Is.EqualTo("This project is not in a git repository"));
        }

        [Test] public async Task Busy_DisablesElements()
        {
            var trio = await _sb.CreateTrioAsync();
            var hooks = new FakeEditorHooks();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, hooks);
            await s.RefreshAsync();
            bool? captured = null;
            hooks.OnSaveAssets = () => captured = MainToolbarButtons.CreateFetch(s).enabled;

            await s.PullAsync();

            Assert.That(captured, Is.False);
            Assert.That(MainToolbarButtons.CreateFetch(s).enabled, Is.True);
        }

        [Test] public void Paths_AreUnique()
        {
            var paths = new[]
            {
                MainToolbarButtons.BranchPath, MainToolbarButtons.CommitPath, MainToolbarButtons.FetchPath,
                MainToolbarButtons.PullPath, MainToolbarButtons.PushPath,
            };

            Assert.That(paths, Is.Unique);
            Assert.That(paths, Has.All.StartsWith("UnityGit/"));
        }

        [Test] public async Task Elements_ShowTheirIcons()
        {
            var trio = await _sb.CreateTrioAsync();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            foreach (var element in AllElements(s))
                Assert.That(element.content.image, Is.Not.Null, element.content.text);
        }

        // The runner records only outcomes of actions that ran, so a toolbar click records an availability refusal itself;
        // the window it opens then shows the reason in its outcome bar.
        [Test] public async Task RefusedClick_RecordsTheReason_AndOpensTheWindow()
        {
            var s = await NotARepoSessionAsync();
            var outcome = await s.FetchAsync();
            Assert.That(outcome.Kind, Is.EqualTo(OutcomeKind.Refused), "setup");
            Assert.That(s.LastOutcome, Is.Null, "setup: the runner didn't record the refusal");

            var feedback = MainToolbarButtons.RecordOutcome(s, outcome);

            Assert.That(feedback, Is.EqualTo(ClickFeedback.OpenWindow));
            Assert.That(s.LastOutcome, Is.SameAs(outcome));
        }

        [Test] public void OtherOutcomes_OpenWindowOnFailure_NotifyOnSuccess()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());

            Assert.That(MainToolbarButtons.RecordOutcome(s, new ActionOutcome(GitAction.Push, OutcomeKind.Failed, "m")), Is.EqualTo(ClickFeedback.OpenWindow));
            Assert.That(MainToolbarButtons.RecordOutcome(s, new ActionOutcome(GitAction.Push, OutcomeKind.Succeeded, "m")), Is.EqualTo(ClickFeedback.Notification));
            Assert.That(MainToolbarButtons.RecordOutcome(s, new ActionOutcome(GitAction.Push, OutcomeKind.Cancelled, "m")), Is.EqualTo(ClickFeedback.None));
            Assert.That(s.LastOutcome, Is.Null, "the runner records these itself");
        }
    }
}
