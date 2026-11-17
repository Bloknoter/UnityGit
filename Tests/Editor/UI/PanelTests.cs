#nullable enable
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class PanelTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        async Task<(RemoteTrio trio, GitSession s)> TrioSessionAsync()
        {
            var trio = await _sb.CreateTrioAsync();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            return (trio, s);
        }

        [Test] public async Task Overview_ShowsBranchUpstreamAndUser()
        {
            var (_, s) = await TrioSessionAsync();

            var p = new OverviewPanel(s);

            Assert.That(p.Q<Label>(UiNames.OverviewBranch).text, Is.EqualTo("main → origin/main"));
            Assert.That(p.Q<Label>(UiNames.OverviewUser).text, Is.EqualTo("Test User <test@example.com>"));
        }

        [Test] public async Task Overview_LfsNotSetUp_ShowsSetupButton()
        {
            var repo = await _sb.InitRepoAsync("p");
            if (!repo.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            GitSandbox.WriteFile(repo, ".gitattributes", "*.psd filter=lfs diff=lfs merge=lfs -text\n");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            var p = new OverviewPanel(s);

            Assert.That(p.Q<Button>(UiNames.LfsSetupButton), Is.Not.Null);
            Assert.That(p.Q<Label>(UiNames.OverviewLfs).text, Is.EqualTo("Used — not set up"));
        }

        [Test] public async Task Changes_SubmitCommitsOnlyTicked()
        {
            var (trio, s) = await TrioSessionAsync();
            GitSandbox.WriteFile(trio.Local, "a.txt", "a");
            GitSandbox.WriteFile(trio.Local, "b.txt", "b");
            await s.RefreshAsync();
            var p = new ChangesPanel(s);
            p.SetTicked("b.txt", false);
            p.Q<TextField>(UiNames.CommitMessage).value = "msg";

            var o = await p.SubmitCommitAsync();

            Assert.That(o.Succeeded, Is.True, o.Message);
            var head = await trio.Local.RunRawAsync("show", "--name-only", "--format=", "HEAD");
            var files = head.StandardOutput.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
            Assert.That(files, Is.EqualTo(new[] { "a.txt" }));
            p.Render();
            Assert.That(p.Rows, Is.EqualTo(new[] { "b.txt" }));
            Assert.That(p.IsTicked("b.txt"), Is.True);
            Assert.That(p.Q<TextField>(UiNames.CommitMessage).value, Is.EqualTo(""));
        }

        [Test] public async Task Changes_SubmitDisabledWithoutMessage()
        {
            var (trio, s) = await TrioSessionAsync();
            GitSandbox.WriteFile(trio.Local, "a.txt", "a");
            await s.RefreshAsync();

            var p = new ChangesPanel(s);

            Assert.That(p.Rows, Is.Not.Empty);
            Assert.That(p.Q<Button>(UiNames.CommitSubmit).enabledSelf, Is.False);
        }

        [Test] public async Task Changes_ConflictedRowCannotBeTicked()
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.PrepareConflictAsync();
            await trio.Local.RunRawAsync("pull", "--no-rebase"); // Exits non-zero: the conflict is the point.
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            var p = new ChangesPanel(s);
            p.Q<TextField>(UiNames.CommitMessage).value = "merge";
            p.Render();

            p.SetTicked("README.md", true);

            Assert.That(p.IsTicked("README.md"), Is.False);
            Assert.That(p.Q<Button>(UiNames.CommitSubmit).enabledSelf, Is.False);
        }

        [Test] public async Task Changes_NewRowAfterRender_DefaultsTicked()
        {
            var (trio, s) = await TrioSessionAsync();
            GitSandbox.WriteFile(trio.Local, "a.txt", "a");
            await s.RefreshAsync();
            var p = new ChangesPanel(s);
            p.Render();

            GitSandbox.WriteFile(trio.Local, "c.txt", "c");
            await s.RefreshAsync();
            p.Render();

            Assert.That(p.IsTicked("c.txt"), Is.True);
        }

        [Test] public async Task Branches_History_Authors_Populated()
        {
            var (trio, s) = await TrioSessionAsync();
            await trio.Local.RunRawAsync("branch", "feature");
            await s.RefreshAsync();

            var branches = new BranchesPanel(s);
            var history = new HistoryPanel(s);
            var authors = new AuthorsPanel(s);

            Assert.That(branches.Q<ListView>(UiNames.LocalBranches).itemsSource.Count, Is.EqualTo(2));
            Assert.That(history.Q<ListView>(UiNames.HistoryList).itemsSource.Count, Is.EqualTo(s.Snapshot.RecentCommits.Count));
            Assert.That(authors.Q<ListView>(UiNames.AuthorsList).itemsSource.Count, Is.EqualTo(s.Snapshot.Authors.Count));
        }
    }
}
