#nullable enable
using System;
using System.Globalization;
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

        // Spec: nothing secret on screen. A token in a remote URL would show when screen-sharing.
        [Test] public async Task Overview_HidesUrlCredentials()
        {
            var (trio, s) = await TrioSessionAsync();
            var set = await trio.Local.RunRawAsync("remote", "set-url", "origin", "https://user:secret-token@example.com/team/repo.git");
            Assert.That(set.Succeeded, Is.True, "setup: " + set.StandardError);
            await s.RefreshAsync();

            var p = new OverviewPanel(s);

            var texts = p.Query<Label>().ToList().Select(l => l.text ?? string.Empty).ToList();
            Assert.That(texts, Has.None.Contains("secret-token"));
            Assert.That(texts, Has.Member("https://example.com/team/repo.git"));
        }

        [TestCase("https://user:secret@example.com/team/repo.git", "https://example.com/team/repo.git")]
        [TestCase("https://ghp_token@github.com/team/repo.git", "https://github.com/team/repo.git")]
        [TestCase("ssh://git@github.com/team/repo.git", "ssh://github.com/team/repo.git")]
        [TestCase("https://github.com/team/repo.git", "https://github.com/team/repo.git")]
        [TestCase("https://example.com/team/repo@v1.git", "https://example.com/team/repo@v1.git")]
        [TestCase("git@github.com:team/repo.git", "git@github.com:team/repo.git")]
        [TestCase("E:/repos/origin.git", "E:/repos/origin.git")]
        public void RemoteUrl_WithoutCredentials(string url, string shown)
            => Assert.That(PanelParts.WithoutCredentials(url), Is.EqualTo(shown));

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

        // A domain reload rebuilds the window's panels: a half-typed message comes back, and a successful commit clears it.
        [Test] public async Task Changes_MessageSurvivesRebuild_ClearedByCommit()
        {
            var (trio, s) = await TrioSessionAsync();
            GitSandbox.WriteFile(trio.Local, "a.txt", "a");
            await s.RefreshAsync();
            var typed = new ChangesPanel(s);
            using (UnityGitTestWindow.Open(typed))
                typed.Q<TextField>(UiNames.CommitMessage).value = "half typed"; // typing sends a ChangeEvent, which needs a panel

            var rebuilt = new ChangesPanel(s);

            Assert.That(rebuilt.Q<TextField>(UiNames.CommitMessage).value, Is.EqualTo("half typed"));
            var o = await rebuilt.SubmitCommitAsync();
            Assert.That(o.Succeeded, Is.True, o.Message);
            Assert.That(new ChangesPanel(s).Q<TextField>(UiNames.CommitMessage).value, Is.EqualTo(""));
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

        // A double-click (or Enter) on the current branch would run the whole switch flow for a no-op git switch.
        [Test] public async Task Branches_ChoosingCurrentBranch_DoesNothing()
        {
            var trio = await _sb.CreateTrioAsync();
            var hooks = new FakeEditorHooks();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, hooks);
            await s.RefreshAsync();
            var p = new BranchesPanel(s);

            var o = await p.SwitchAsync("main");

            Assert.That(o, Is.Null);
            Assert.That(hooks.Calls, Is.Empty);
            Assert.That(s.LastOutcome, Is.Null);
        }

        // The list can't disable a row the way the toolbar disables a button, so a refused switch is recorded: the
        // outcome bar then says why nothing happened, as it does for a refused toolbar click.
        [Test] public async Task Branches_ChoosingWhileSwitchUnavailable_RecordsTheReason()
        {
            var (trio, s) = await TrioSessionAsync();
            await trio.Local.RunRawAsync("branch", "feature");
            await s.RefreshAsync();
            s.IsPlayMode = true;
            var p = new BranchesPanel(s);

            var o = await p.SwitchAsync("feature");

            Assert.That(o?.Kind, Is.EqualTo(OutcomeKind.Refused));
            Assert.That(s.LastOutcome, Is.SameAs(o));
            Assert.That(s.LastOutcome?.Message, Is.EqualTo("Not available in Play Mode"));
        }

        // Teammates' commits read in order whatever their time zone, and the time separator is always a colon.
        [Test] public void History_DateIsLocalTimeInInvariantFormat()
        {
            var instant = new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero);
            var before = CultureInfo.CurrentCulture;
            var dotted = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            dotted.DateTimeFormat.TimeSeparator = ".";
            CultureInfo.CurrentCulture = dotted;
            try
            {
                var east = HistoryPanel.FormatDate(instant.ToOffset(TimeSpan.FromHours(5)));
                var west = HistoryPanel.FormatDate(instant.ToOffset(TimeSpan.FromHours(-3)));

                Assert.That(east, Is.EqualTo(west), "the same moment, whatever the author's offset");
                Assert.That(east, Is.EqualTo(instant.LocalDateTime.ToString("yyyy-MM-dd HH':'mm", CultureInfo.InvariantCulture)));
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
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
