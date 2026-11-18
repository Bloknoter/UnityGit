#nullable enable
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ChangesSelectionTests
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

        // A merge stopped on a conflict in README.md, plus the given untracked files: every row is locked in.
        async Task<(GitRepository repo, GitSession s)> MergeConflictWithFilesAsync(params string[] files)
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.PrepareConflictAsync();
            var pull = await trio.Local.RunRawAsync("pull", "--no-rebase");
            Assert.That(pull.ExitCode, Is.Not.EqualTo(0), "setup: the pull stops on the conflict");
            foreach (var file in files) GitSandbox.WriteFile(trio.Local, file, file);
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Operation, Is.EqualTo(RepoOperation.Merging), "setup: a merge is in progress");
            return (trio.Local, s);
        }

        // A cherry-pick stopped on a conflict in README.md, plus the given untracked files. Not a merge, so only the
        // conflicted-row rule applies.
        async Task<(GitRepository repo, GitSession s)> CherryPickConflictWithFilesAsync(params string[] files)
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "README.md", "base\n", "initial");
            Assert.That((await repo.RunRawAsync("checkout", "-b", "other")).ExitCode, Is.EqualTo(0), "setup: branch other");
            await GitSandbox.CommitFileAsync(repo, "README.md", "other line\n", "other edit");
            Assert.That((await repo.RunRawAsync("checkout", "main")).ExitCode, Is.EqualTo(0), "setup: back on main");
            await GitSandbox.CommitFileAsync(repo, "README.md", "main line\n", "main edit");
            var pick = await repo.RunRawAsync("cherry-pick", "other");
            Assert.That(pick.ExitCode, Is.Not.EqualTo(0), "setup: the cherry-pick stops on the conflict");
            foreach (var file in files) GitSandbox.WriteFile(repo, file, file);
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Operation, Is.EqualTo(RepoOperation.CherryPicking), "setup: a cherry-pick is in progress");
            Assert.That(s.Snapshot.Changes.Single(c => c.Path == "README.md").IsConflicted, Is.True, "setup: README.md is conflicted");
            return (repo, s);
        }

        // One press of Space as the editor sends it: a key event, then a character event.
        static void PressSpace(VisualElement target)
        {
            using (var key = KeyDownEvent.GetPooled('\0', KeyCode.Space, EventModifiers.None)) target.SendEvent(key);
            using (var character = KeyDownEvent.GetPooled(' ', KeyCode.None, EventModifiers.None)) target.SendEvent(character);
        }

        static string Summary(ChangesPanel p) => p.Q<Label>(UiNames.ChangesSummary).text;

        static Toggle TickAll(ChangesPanel p) => p.Q<Toggle>(UiNames.ChangesTickAll);

        static string[] Ticked(ChangesPanel p) => p.Rows.Where(p.IsTicked).ToArray();

        [Test] public async Task TickAll_TicksAndUnticksEveryRow()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt");
            var p = new ChangesPanel(s);

            p.SetAllTicked(false);

            Assert.That(p.Rows.All(r => !p.IsTicked(r)), Is.True);
            Assert.That(p.AllTicked, Is.False);
            Assert.That(Summary(p), Is.EqualTo("0 of 3 to commit"));

            p.SetAllTicked(true);

            Assert.That(p.Rows.All(p.IsTicked), Is.True);
            Assert.That(p.AllTicked, Is.True);
            Assert.That(Summary(p), Is.EqualTo("3 of 3 to commit"));
            Assert.That(TickAll(p).value, Is.True);
        }

        [Test] public async Task TickAll_ShowsMixedWhenSomeTicked()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt");
            var p = new ChangesPanel(s);

            p.SetTicked("b.txt", false);

            Assert.That(p.AllTicked, Is.Null);
            Assert.That(TickAll(p).showMixedValue, Is.True);
            Assert.That(Summary(p), Is.EqualTo("2 of 3 to commit"));
        }

        [Test] public async Task Summary_NoChanges()
        {
            var (_, s) = await SessionWithFilesAsync();

            var p = new ChangesPanel(s);

            Assert.That(Summary(p), Is.EqualTo("No changes"));
            Assert.That(TickAll(p).enabledSelf, Is.False);
        }

        [Test] public async Task TickAll_DisabledDuringMerge()
        {
            var (_, s) = await MergeConflictWithFilesAsync();

            var p = new ChangesPanel(s);

            Assert.That(TickAll(p).enabledSelf, Is.False);
        }

        [Test] public async Task ToggleRow_InSelection_AppliesToWholeSelection()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt", "d.txt");
            var p = new ChangesPanel(s);
            p.Select(new[] { "a.txt", "c.txt" });

            p.ToggleRow("a.txt", false);

            Assert.That(Ticked(p), Is.EqualTo(new[] { "b.txt", "d.txt" }));

            p.ToggleRow("c.txt", true);

            Assert.That(Ticked(p), Is.EqualTo(new[] { "a.txt", "b.txt", "c.txt", "d.txt" }));
        }

        [Test] public async Task ToggleRow_OutsideSelection_AffectsOnlyThatRow()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt", "d.txt");
            var p = new ChangesPanel(s);
            p.Select(new[] { "a.txt", "c.txt" });

            p.ToggleRow("b.txt", false);

            Assert.That(Ticked(p), Is.EqualTo(new[] { "a.txt", "c.txt", "d.txt" }));
            Assert.That(p.SelectedRows, Is.EqualTo(new[] { "a.txt", "c.txt" }));
        }

        [Test] public async Task ToggleSelection_TicksAllIfAnyUnticked_ElseUnticksAll()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt", "d.txt");
            var p = new ChangesPanel(s);
            p.Select(new[] { "a.txt", "b.txt" });

            p.ToggleSelection();

            Assert.That(Ticked(p), Is.EqualTo(new[] { "c.txt", "d.txt" }));

            p.SetTicked("a.txt", true);
            p.ToggleSelection();

            Assert.That(Ticked(p), Is.EqualTo(new[] { "a.txt", "b.txt", "c.txt", "d.txt" }));
        }

        [Test] public async Task Selection_SurvivesRender()
        {
            var (repo, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt", "d.txt");
            var p = new ChangesPanel(s);
            p.Select(new[] { "a.txt", "c.txt" });

            GitSandbox.WriteFile(repo, "e.txt", "e");
            await s.RefreshAsync();
            p.Render();

            Assert.That(p.Rows, Does.Contain("e.txt"), "setup: the refresh saw e.txt");
            Assert.That(p.SelectedRows, Is.EqualTo(new[] { "a.txt", "c.txt" }));
        }

        [Test] public async Task Selection_FollowsPathsWhenRowsMove()
        {
            var (repo, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt", "d.txt");
            var p = new ChangesPanel(s);
            var list = p.Q<ListView>(UiNames.ChangesList);
            p.Select(new[] { "a.txt", "c.txt" });
            Assert.That(list.selectedIndices, Is.EqualTo(new[] { 0, 2 }));

            GitSandbox.WriteFile(repo, "0.txt", "0"); // listed first, so every other row moves down one
            await s.RefreshAsync();
            p.Render();

            Assert.That(p.Rows.First(), Is.EqualTo("0.txt"), "setup: the new row is listed first");
            Assert.That(p.SelectedRows, Is.EqualTo(new[] { "a.txt", "c.txt" }));
            Assert.That(list.selectedIndices, Is.EqualTo(new[] { 1, 3 }));
        }

        [Test] public async Task ListSelection_UpdatesSelectedRows()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt", "d.txt");
            var p = new ChangesPanel(s);

            p.Q<ListView>(UiNames.ChangesList).SetSelection(new[] { 1, 3 }); // as a click and a Ctrl+click would

            Assert.That(p.SelectedRows, Is.EqualTo(new[] { "b.txt", "d.txt" }));
        }

        [Test] public async Task MergeLock_BatchPathsChangeNoTick()
        {
            var (repo, s) = await MergeConflictWithFilesAsync("a.txt", "b.txt");
            var p = new ChangesPanel(s);
            var before = Ticked(p);
            Assert.That(before, Is.EquivalentTo(new[] { "a.txt", "b.txt" }), "setup: every row but the conflicted one is ticked");
            Assert.That(TickAll(p).value, Is.True, "the header box shows ticked");
            Assert.That(TickAll(p).enabledSelf, Is.False, "and is locked");
            p.Select(p.Rows);

            p.SetAllTicked(false);
            Assert.That(Ticked(p), Is.EqualTo(before), "SetAllTicked");

            p.ToggleSelection();
            Assert.That(Ticked(p), Is.EqualTo(before), "ToggleSelection");

            p.ToggleRow("a.txt", false);
            Assert.That(Ticked(p), Is.EqualTo(before), "ToggleRow");

            // Nothing was recorded while the rows were locked: once the merge is aborted, both files are still ticked.
            Assert.That((await repo.RunRawAsync("merge", "--abort")).ExitCode, Is.EqualTo(0), "setup: merge aborted");
            await s.RefreshAsync();
            p.Render();
            Assert.That(p.Rows, Is.EquivalentTo(new[] { "a.txt", "b.txt" }), "setup: only the untracked files are left");
            Assert.That(Ticked(p), Is.EquivalentTo(new[] { "a.txt", "b.txt" }), "after the merge");
        }

        [Test] public async Task ConflictedRow_StaysUnticked_OnBatchPaths()
        {
            var (repo, s) = await CherryPickConflictWithFilesAsync("a.txt", "b.txt");
            var p = new ChangesPanel(s);

            p.SetAllTicked(false);
            Assert.That(Ticked(p), Is.Empty, "setup: everything unticked");
            p.SetAllTicked(true);

            Assert.That(Ticked(p), Is.EquivalentTo(new[] { "a.txt", "b.txt" }));
            Assert.That(p.AllTicked, Is.True, "the conflicted row doesn't count");

            p.Select(new[] { "README.md", "a.txt" });
            p.ToggleRow("a.txt", false);
            Assert.That(Ticked(p), Is.EquivalentTo(new[] { "b.txt" }));

            p.ToggleRow("a.txt", true);
            Assert.That(Ticked(p), Is.EquivalentTo(new[] { "a.txt", "b.txt" }), "README.md stays unticked");

            p.ToggleRow("README.md", true);
            Assert.That(p.IsTicked("README.md"), Is.False);

            // Only a counts: it is ticked, so Space unticks it. (Counting README.md would tick all, changing nothing.)
            p.ToggleSelection();
            Assert.That(Ticked(p), Is.EquivalentTo(new[] { "b.txt" }));

            // SetAllTicked skipped README.md while it was conflicted, so once resolved it is ticked like any untouched row.
            p.SetAllTicked(false);
            Assert.That((await repo.RunRawAsync("add", "README.md")).ExitCode, Is.EqualTo(0), "setup: conflict marked resolved");
            await s.RefreshAsync();
            p.Render();
            Assert.That(s.Snapshot.Changes.Single(c => c.Path == "README.md").IsConflicted, Is.False, "setup: README.md resolved");
            Assert.That(Ticked(p), Is.EqualTo(new[] { "README.md" }));
        }

        [Test] public async Task TickAllBox_ValueChange_TicksOrUnticksEveryRow()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt");
            var p = new ChangesPanel(s);
            using (var window = UnityGitTestWindow.Open(p))
            {
                p.SetTicked("b.txt", false);
                Assert.That(TickAll(p).showMixedValue, Is.True, "setup: mixed");

                TickAll(p).value = !TickAll(p).value; // what a click does; the box's ChangeEvent reaches the panel

                Assert.That(Ticked(p), Is.EqualTo(new[] { "a.txt", "b.txt", "c.txt" }));
                Assert.That(TickAll(p).showMixedValue, Is.False);

                TickAll(p).value = !TickAll(p).value;

                Assert.That(Ticked(p), Is.Empty);
                Assert.That(Summary(p), Is.EqualTo("0 of 3 to commit"));
            }
        }

        [Test] public async Task SpaceOnList_TogglesSelection()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt", "d.txt");
            var p = new ChangesPanel(s);
            using (var window = UnityGitTestWindow.Open(p))
            {
                var list = p.Q<ListView>(UiNames.ChangesList);
                p.Select(new[] { "a.txt", "b.txt" });
                list.Focus();
                Assert.That(list.panel.focusController.focusedElement, Is.Not.Null, "setup: the list has keyboard focus");

                PressSpace(list);

                Assert.That(Ticked(p), Is.EqualTo(new[] { "c.txt", "d.txt" }));

                PressSpace(list);

                Assert.That(Ticked(p), Is.EqualTo(new[] { "a.txt", "b.txt", "c.txt", "d.txt" }));
            }
        }

        [Test] public async Task RowCheckbox_ValueChange_FollowsSelection()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt", "b.txt", "c.txt", "d.txt");
            var p = new ChangesPanel(s);
            using (var window = UnityGitTestWindow.Open(p))
            {
                var list = p.Q<ListView>(UiNames.ChangesList);
                // Batch mode lays nothing out, so the list makes no rows: make and bind them as it would, inside the window.
                VisualElement Row(string path)
                {
                    var row = list.makeItem();
                    list.bindItem(row, p.Rows.ToList().IndexOf(path));
                    window.rootVisualElement.Add(row);
                    return row;
                }
                var rowA = Row("a.txt");
                var rowB = Row("b.txt");
                p.Select(new[] { "a.txt", "c.txt" });

                rowA.Q<Toggle>().value = false; // what a click on a's box does

                Assert.That(Ticked(p), Is.EqualTo(new[] { "b.txt", "d.txt" }), "a is selected: a and c untick");
                Assert.That(p.SelectedRows, Is.EqualTo(new[] { "a.txt", "c.txt" }));

                rowB.Q<Toggle>().value = false;

                Assert.That(Ticked(p), Is.EqualTo(new[] { "d.txt" }), "b isn't selected: only b unticks");
                Assert.That(p.SelectedRows, Is.EqualTo(new[] { "a.txt", "c.txt" }));
            }
        }

        [Test] public async Task List_AllowsMultipleSelection()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt");

            var p = new ChangesPanel(s);

            Assert.That(p.Q<ListView>(UiNames.ChangesList).selectionType, Is.EqualTo(SelectionType.Multiple));
        }
    }
}
