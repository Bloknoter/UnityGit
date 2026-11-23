#nullable enable
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ChangesMenuTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        // A repo with one commit and the given untracked files, and a session that has seen them.
        async Task<(GitRepository repo, GitSession s)> SessionWithFilesAsync(params string[] files)
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "README.md", "readme\n", "initial");
            foreach (var file in files) GitSandbox.WriteFile(repo, file, file);
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Changes.Select(c => c.Path), Is.EquivalentTo(files), "setup: every file is listed");
            return (repo, s);
        }

        async Task<ChangesPanel> PanelWithFilesAsync() => new ChangesPanel((await SessionWithFilesAsync("a.txt", "b.txt", "c.txt")).s);

        [Test] public async Task MenuState_SelectedRow_TargetsTheSelection()
        {
            var p = await PanelWithFilesAsync();
            p.Select(new[] { "b.txt", "c.txt" });

            Assert.That(p.GetRowMenuState("c.txt").Targets, Is.EqualTo(new[] { "b.txt", "c.txt" }));
        }

        [Test] public async Task MenuState_UnselectedRow_TargetsOnlyIt_SelectionKept()
        {
            var p = await PanelWithFilesAsync();
            p.Select(new[] { "b.txt", "c.txt" });

            Assert.That(p.GetRowMenuState("a.txt").Targets, Is.EqualTo(new[] { "a.txt" }));
            Assert.That(p.SelectedRows, Is.EqualTo(new[] { "b.txt", "c.txt" }));
        }

        [Test] public async Task MenuState_NothingSelected_TargetsTheRow()
        {
            var p = await PanelWithFilesAsync();

            Assert.That(p.GetRowMenuState("b.txt").Targets, Is.EqualTo(new[] { "b.txt" }));
        }

        [Test] public async Task MenuState_Normal_RevertEnabled_ExplorerPathSet()
        {
            var (repo, s) = await SessionWithFilesAsync("a.txt");
            var p = new ChangesPanel(s);

            var st = p.GetRowMenuState("a.txt");

            Assert.That(st.RevertDisabledReason, Is.Null);
            Assert.That(st.ExplorerPath, Is.SamePath(Path.Combine(repo.WorkingDirectory, "a.txt")));
        }

        [Test] public async Task MenuState_PlayMode_RevertDisabledWithReason()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt");
            var p = new ChangesPanel(s);
            s.IsPlayMode = true;

            Assert.That(p.GetRowMenuState("a.txt").RevertDisabledReason, Is.EqualTo("Not available in Play Mode"));
        }

        [Test] public async Task MenuState_DeletedFile_ExplorerAndProjectDisabled()
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "d.txt", "d\n", "initial");
            File.Delete(Path.Combine(repo.WorkingDirectory, "d.txt"));
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            var p = new ChangesPanel(s);

            var st = p.GetRowMenuState("d.txt");

            Assert.That((st.ExplorerPath, st.ProjectAsset), Is.EqualTo(((string?)null, (UnityEngine.Object?)null)));
        }

        [Test] public async Task MenuState_FileOutsideProject_ProjectDisabled()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt");
            var p = new ChangesPanel(s);

            Assert.That(p.GetRowMenuState("a.txt").ProjectAsset, Is.Null);
        }

        [Test] public async Task MenuState_ProjectInRepoSubfolder_UsesRepoRoot()
        {
            var repo = await _sb.InitRepoAsync("r");
            await GitSandbox.CommitFileAsync(repo, "README.md", "readme\n", "initial");
            GitSandbox.WriteFile(repo, "Top.txt", "top\n");
            var game = Path.Combine(repo.WorkingDirectory, "Game");
            Directory.CreateDirectory(game);
            var s = _sb.CreateSession(game, new FakeEditorHooks());
            await s.RefreshAsync();
            var p = new ChangesPanel(s);

            Assert.That(p.GetRowMenuState("Top.txt").ExplorerPath, Is.SamePath(Path.Combine(repo.WorkingDirectory, "Top.txt")));
        }

        [Test] public async Task MenuState_OnlyConflictedTargets_RevertDisabled()
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "f.txt", "base\n", "initial");
            GitSandbox.WriteFile(repo, "f.txt", "stash\n");
            Assert.That((await repo.RunRawAsync("stash")).ExitCode, Is.EqualTo(0), "setup: stash");
            await GitSandbox.CommitFileAsync(repo, "f.txt", "main\n", "main edit");
            Assert.That((await repo.RunRawAsync("stash", "pop")).ExitCode, Is.Not.EqualTo(0), "setup: the pop stops on the conflict");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            var p = new ChangesPanel(s);

            Assert.That(p.GetRowMenuState("f.txt").RevertDisabledReason, Is.EqualTo("Resolve conflicts in the Conflicts tab"));
        }

        [Test] public async Task PopulateRowMenu_ItemNamesAndOrder()
        {
            var p = await PanelWithFilesAsync();
            var menu = new DropdownMenu();

            p.PopulateRowMenu(menu, "a.txt");

            Assert.That(menu.MenuItems().Select(i => i is DropdownMenuAction a ? a.name : "-"),
                Is.EqualTo(new[] { "Revert…", "-", ChangesPanel.ExplorerLabel, "Show in Project" }));
        }

        [Test] public async Task PopulateRowMenu_DisabledRevert_ShowsReasonInName()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt");
            var p = new ChangesPanel(s);
            s.IsPlayMode = true;
            var menu = new DropdownMenu();

            p.PopulateRowMenu(menu, "a.txt");

            Assert.That(((DropdownMenuAction)menu.MenuItems()[0]).name, Is.EqualTo("Revert… (Not available in Play Mode)"));
        }

        [Test] public async Task Revert_FromPanel_RowsLeaveTheList()
        {
            var p = await PanelWithFilesAsync();

            var o = await p.RevertAsync(new[] { "a.txt", "b.txt" });

            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded), o.Message);
            Assert.That(p.Rows, Is.EqualTo(new[] { "c.txt" }));
        }
    }
}
