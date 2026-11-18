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
            var trio = await _sb.CreateTrioAsync();
            await trio.PrepareConflictAsync();
            var pull = await trio.Local.RunRawAsync("pull", "--no-rebase");
            Assert.That(pull.ExitCode, Is.Not.EqualTo(0), "setup: the pull stops on the conflict");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Operation, Is.EqualTo(RepoOperation.Merging), "setup: a merge is in progress");

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

        [Test] public async Task List_AllowsMultipleSelection()
        {
            var (_, s) = await SessionWithFilesAsync("a.txt");

            var p = new ChangesPanel(s);

            Assert.That(p.Q<ListView>(UiNames.ChangesList).selectionType, Is.EqualTo(SelectionType.Multiple));
        }
    }
}
