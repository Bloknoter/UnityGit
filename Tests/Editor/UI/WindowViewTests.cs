#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class WindowViewTests
    {
        GitSandbox _sb = null!;
        readonly List<string> _tabProjects = new List<string>(); // projects whose selected tab a test stored

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            foreach (var project in _tabProjects) SessionState.EraseString(GitWindowView.SelectedTabKey(project));
            _tabProjects.Clear();
            SessionProvider.ClearOverride();
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        // Batch mode runs no layout pass, so resolvedStyle is unavailable: an element is displayed unless it or an
        // ancestor has display: none.
        static bool IsDisplayed(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e.style.display == DisplayStyle.None) return false;
            return true;
        }

        async Task<(GitSession s, FakeEditorHooks hooks)> TrioWithUnpushedCommitAsync()
        {
            var trio = await _sb.CreateTrioAsync();
            await GitSandbox.CommitFileAsync(trio.Local, "a.txt", "a", "local work");
            var hooks = new FakeEditorHooks();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, hooks);
            await s.RefreshAsync();
            return (s, hooks);
        }

        [Test] public async Task NotARepo_ShowsOnlyInitButton()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());
            await s.RefreshAsync();

            var v = new GitWindowView(s);

            Assert.That(v.Q<Button>(UiNames.InitButton), Is.Not.Null);
            Assert.That(v.Q<Label>(UiNames.NotRepo).text, Is.EqualTo("This project is not in a git repository"));
            Assert.That(v.Q(UiNames.Nav), Is.Null);
        }

        [Test] public async Task GitMissing_ShowsMessage()
        {
            _sb.Options.GitExecutablePath = Path.Combine(_sb.Root, "missing", "git.exe");
            GitLocator.ClearCache();
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());
            await s.RefreshAsync();

            var v = new GitWindowView(s);

            Assert.That(v.Q<Label>(UiNames.GitMissing).text, Is.EqualTo("Git isn't installed or wasn't found — install Git 2.32 or newer"));
        }

        // git refuses the repo: the window shows git's reason (with its own fix) and offers neither Git init nor Download Git.
        [Test] public async Task Unavailable_ShowsGitsMessage_WithoutInit()
        {
            var repo = await _sb.InitRepoAsync("p");
            await repo.Config.SetAsync("core.repositoryformatversion", "99");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            var v = new GitWindowView(s);

            Assert.That(v.Q<Label>(UiNames.Unavailable)?.text, Is.EqualTo("Git can't open this repository"));
            Assert.That(v.Q<Label>(UiNames.UnavailableMessage)?.text, Does.Contain("Expected git repo version <= 1"));
            Assert.That(v.Q<Button>(UiNames.InitButton), Is.Null);
            Assert.That(v.Query<Button>().Where(b => b.text == "Download Git").ToList(), Is.Empty);
            Assert.That(v.Q<ToolbarMenu>(UiNames.BranchMenu).text, Is.EqualTo("No repository"));
        }

        // Spec 5.4: the toolbar's Refresh stays local. Only "Refresh locks" asks git-lfs for the lock list (a server call).
        [Test] public async Task RefreshButton_DoesNotLoadLfsLocks()
        {
            var trio = await _sb.CreateTrioAsync();
            if (!trio.Local.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            GitSandbox.WriteFile(trio.Local, ".gitattributes", "*.psd filter=lfs diff=lfs merge=lfs -text");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            var v = new GitWindowView(s);
            using (UnityGitTestWindow.Open(v))
            {
                var refresh = v.Q<Button>(UiNames.RefreshButton);
                refresh.Focus();
                using (var submit = NavigationSubmitEvent.GetPooled()) refresh.SendEvent(submit); // what a click does

                await s.RefreshAsync(); // joins the refresh the click started
            }

            Assert.That(s.Snapshot.LfsLocks, Is.Null);
        }

        [Test] public async Task Ready_ShowsBranchAndCounts()
        {
            var (s, _) = await TrioWithUnpushedCommitAsync();

            var v = new GitWindowView(s);

            Assert.That(v.Q<ToolbarMenu>(UiNames.BranchMenu).text, Is.EqualTo("main"));
            Assert.That(v.Q<Button>(UiNames.PushButton).text, Is.EqualTo("Push ↑1"));
            Assert.That(v.Q<Button>(UiNames.PullButton).text, Is.EqualTo("Pull"));
        }

        // A script reload builds a new view (GitWindow.CreateGUI): it opens on the tab the last one showed.
        [Test] public async Task SelectedTab_RestoredByTheNextView()
        {
            var (s, _) = await TrioWithUnpushedCommitAsync();
            _tabProjects.Add(s.ProjectPath);
            var v = new GitWindowView(s);

            v.SelectTab(WindowTab.Branches);

            Assert.That(SessionState.GetString(GitWindowView.SelectedTabKey(s.ProjectPath), string.Empty), Is.EqualTo("Branches"));
            Assert.That(new GitWindowView(s).SelectedTab, Is.EqualTo(WindowTab.Branches));
        }

        // Applying the last conflicted script recompiles: the new session starts loading, and Conflicts must survive that.
        [Test] public async Task SelectedTab_ConflictsKeptWhileTheNewSessionLoads()
        {
            var repo = await _sb.InitRepoAsync("repo");
            await ConflictScenarios.MergeAsync(repo, ConflictScenarios.TwoPlaces);
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            _tabProjects.Add(s.ProjectPath);
            await s.RefreshAsync();
            var v = new GitWindowView(s);
            v.SelectTab(WindowTab.Conflicts);
            await v.Q<ConflictsPanel>()!.ReloadAsync();

            var reloaded = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            var view = new GitWindowView(reloaded);
            Assert.That(view.SelectedTab, Is.EqualTo(WindowTab.Conflicts), "while the new session loads");
            await reloaded.RefreshAsync();
            view.Render();

            Assert.That(view.SelectedTab, Is.EqualTo(WindowTab.Conflicts));
            await view.Q<ConflictsPanel>()!.ReloadAsync(); // the reload the new panel started
        }

        // A stored Conflicts tab with nothing left to resolve falls back to Changes, as when the tab goes away.
        [Test] public async Task SelectedTab_StoredConflictsWithoutConflicts_SelectsChanges()
        {
            var (s, _) = await TrioWithUnpushedCommitAsync();
            _tabProjects.Add(s.ProjectPath);
            SessionState.SetString(GitWindowView.SelectedTabKey(s.ProjectPath), "Conflicts");

            var v = new GitWindowView(s);

            Assert.That(v.SelectedTab, Is.EqualTo(WindowTab.Changes));
        }

        [Test] public async Task ButtonsDisabledWhileBusy()
        {
            var (s, hooks) = await TrioWithUnpushedCommitAsync();
            var v = new GitWindowView(s);
            bool? captured = null;
            hooks.OnSaveAssets = () => { v.Render(); captured = v.Q<Button>(UiNames.PushButton).enabledSelf; };

            await s.PullAsync();

            Assert.That(captured, Is.False);
            v.Render();
            Assert.That(v.Q<Button>(UiNames.PushButton).enabledSelf, Is.True);
        }

        [Test] public async Task Outcome_ShownAndDismissed()
        {
            var (s, _) = await TrioWithUnpushedCommitAsync();
            var v = new GitWindowView(s);

            await s.PushAsync();
            v.Render();

            var outcome = v.Q<Label>(UiNames.Outcome);
            Assert.That(outcome.text, Is.EqualTo(s.LastOutcome!.Message));
            Assert.That(IsDisplayed(outcome), Is.True);

            s.LastOutcome = null;
            v.Render();

            Assert.That(IsDisplayed(v.Q<Label>(UiNames.Outcome)), Is.False);
        }

        [Test] public void Outcome_ListsConflictedFiles()
        {
            var s = _sb.CreateSession(_sb.MakeDir("p"), new FakeEditorHooks());
            var v = new GitWindowView(s);

            s.LastOutcome = new ActionOutcome(GitAction.Pull, OutcomeKind.Failed, "m", null, new[] { "README.md", "Assets/A.unity" });
            v.Render();

            var conflicts = v.Q<Foldout>(UiNames.OutcomeConflicts);
            Assert.That(conflicts, Is.Not.Null);
            Assert.That(IsDisplayed(conflicts), Is.True);
            Assert.That(conflicts.value, Is.True);
            Assert.That(conflicts.contentContainer.Children().Select(c => ((Label)c).text), Is.EqualTo(new[] { "README.md", "Assets/A.unity" }));
        }

        [Test] public void OutcomeStore_KeepsConflictedPaths()
        {
            var before = OutcomeStore.Load();
            try
            {
                OutcomeStore.Save(new ActionOutcome(GitAction.Pull, OutcomeKind.Failed, "m", null, new[] { "README.md", "Assets/A.unity" }));

                Assert.That(OutcomeStore.Load()!.ConflictedPaths, Is.EqualTo(new[] { "README.md", "Assets/A.unity" }));
            }
            finally
            {
                OutcomeStore.Save(before);
            }
        }

        [Test] public void OutcomeStore_RoundTrips()
        {
            var before = OutcomeStore.Load();
            try
            {
                OutcomeStore.Save(new ActionOutcome(GitAction.Pull, OutcomeKind.Failed, "m", "d"));
                var loaded = OutcomeStore.Load();

                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.Action, Is.EqualTo(GitAction.Pull));
                Assert.That(loaded.Kind, Is.EqualTo(OutcomeKind.Failed));
                Assert.That(loaded.Message, Is.EqualTo("m"));
                Assert.That(loaded.Details, Is.EqualTo("d"));

                OutcomeStore.Save(null);
                Assert.That(OutcomeStore.Load(), Is.Null);
            }
            finally
            {
                OutcomeStore.Save(before); // Leaves the editor's persisted outcome as it was.
            }
        }

        // A refresh that keeps failing logs its warning once, not on every focus or asset change.
        [Test] public void WarningFilter_PassesEachWarningOnceUntilItChanges()
        {
            var filter = new WarningFilter();

            var passed = new[] { "a", "a", "b", "a", "a" }.Select(filter.ShouldLog).ToArray();

            Assert.That(passed, Is.EqualTo(new[] { true, false, true, true, false }));
        }

        [Test] public void BranchMenuItems_LocalThenRemoteOnly()
        {
            var s = new RepoSnapshot
            {
                Availability = RepoAvailability.Ready,
                LocalBranches = new[]
                {
                    new BranchItem { Name = "main", ShortName = "main" },
                    new BranchItem { Name = "feature", ShortName = "feature" },
                },
                RemoteBranches = new[]
                {
                    new BranchItem { Name = "origin/main", ShortName = "main", IsRemote = true, RemoteName = "origin" },
                    new BranchItem { Name = "origin/fix", ShortName = "fix", IsRemote = true, RemoteName = "origin" },
                },
            };

            Assert.That(ActionLabels.BranchMenuItems(s), Is.EqualTo(new[] { "feature", "main", "fix" }));
        }
    }
}
