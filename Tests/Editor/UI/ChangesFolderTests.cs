#nullable enable
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
    public class ChangesFolderTests
    {
        const string Assets = "Assets/", A = "Assets/A/", AB = "Assets/A/B/";
        static readonly string[] Files = { "Assets/A/x.txt", "Assets/A/B/y.txt", "Assets/A.meta", "top.txt" };
        static readonly string[] AllRows = { "Assets/A.meta", "Assets/A/B/y.txt", "Assets/A/x.txt", "top.txt" };

        GitSandbox _sb = null!;
        bool _hadPref;
        bool _pref;

        [SetUp] public async Task SetUp()
        {
            _hadPref = EditorPrefs.HasKey(ChangesPanel.ShowFoldersPref);
            _pref = EditorPrefs.GetBool(ChangesPanel.ShowFoldersPref, true);
            _sb = await GitSandbox.CreateAsync();
        }

        [TearDown] public void TearDown()
        {
            if (_hadPref) EditorPrefs.SetBool(ChangesPanel.ShowFoldersPref, _pref);
            else EditorPrefs.DeleteKey(ChangesPanel.ShowFoldersPref);
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

        static async Task<string[]> HeadFiles(GitRepository repo)
        {
            var r = await repo.RunRawAsync("show", "--name-only", "--format=", "HEAD");
            Assert.That(r.Succeeded, Is.True, r.StandardError);
            return r.StandardOutput.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        }

        static ChangesPanel Panel(GitSession s)
        {
            EditorPrefs.SetBool(ChangesPanel.ShowFoldersPref, true);
            return new ChangesPanel(s);
        }

        static string[] Ticked(ChangesPanel p) => p.Rows.Where(p.IsTicked).ToArray();

        static string Summary(ChangesPanel p) => p.Q<Label>(UiNames.ChangesSummary).text;

        // Batch mode lays nothing out, so the list makes no rows: make (or reuse) and bind one as it would, inside the window.
        static VisualElement Bind(UnityGitTestWindow w, ChangesPanel p, string key, VisualElement? row = null)
        {
            var list = p.Q<ListView>(UiNames.ChangesList);
            if (row == null)
            {
                row = list.makeItem();
                w.rootVisualElement.Add(row);
            }
            list.bindItem(row, p.Entries.ToList().IndexOf(key));
            return row;
        }

        [Test] public async Task Entries_FolderRowsAboveTheirFiles_RowsUnchanged()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);

            Assert.That(p.Entries, Is.EqualTo(new[] { Assets, A, AB, "Assets/A/B/y.txt", "Assets/A/x.txt", "top.txt" }));
            Assert.That(p.Rows, Is.EqualTo(AllRows));
        }

        [Test] public async Task UntickFolder_UnticksEverythingUnderIt_TickTicksItBack()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);

            p.SetTicked(A, false);

            Assert.That(Ticked(p), Is.EqualTo(new[] { "top.txt" }));
            Assert.That(new[] { p.FolderTick(Assets), p.FolderTick(A), p.FolderTick(AB) }, Is.EqualTo(new bool?[] { false, false, false }));
            Assert.That(Summary(p), Is.EqualTo("1 of 4 to commit"));

            p.SetTicked(A, true);

            Assert.That(Ticked(p), Is.EqualTo(AllRows));
        }

        [Test] public async Task OneFileUnticked_FolderAndAncestorsMixed()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            using (var w = UnityGitTestWindow.Open(p))
            {
                p.SetTicked("Assets/A/B/y.txt", false);

                Assert.That(new[] { p.FolderTick(Assets), p.FolderTick(A), p.FolderTick(AB) }, Is.EqualTo(new bool?[] { null, null, false }));
                var box = Bind(w, p, A).Q<Toggle>();
                Assert.That((box.showMixedValue, box.value), Is.EqualTo((true, false)));
            }
        }

        // Only the visible files are ticked by hand: the folded A.meta has no box, so ticking a file under A ticks it too.
        [Test] public async Task FolderFollowsTicksMadeOnItsFiles()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            p.SetTicked(A, false);

            foreach (var f in new[] { "Assets/A/B/y.txt", "Assets/A/x.txt" }) p.SetTicked(f, true);

            Assert.That((p.FolderTick(A), p.FolderTick(Assets)), Is.EqualTo(((bool?)true, (bool?)true)));
            Assert.That(p.IsTicked("Assets/A.meta"), Is.True);
        }

        [Test] public async Task TickingAFile_TicksTheFoldedMetaOfEveryFolderAboveIt()
        {
            var p = Panel((await SessionWithFilesAsync("Assets/A/B/y.txt", "Assets/A.meta", "Assets/A/B.meta")).s);
            p.SetTicked(Assets, false);

            p.ToggleRow("Assets/A/B/y.txt", true);

            Assert.That(Ticked(p), Is.EqualTo(new[] { "Assets/A.meta", "Assets/A/B.meta", "Assets/A/B/y.txt" }));
        }

        [Test] public async Task FoldersHidden_TickingAFile_LeavesTheFolderMetaRowAlone()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            p.ShowFolders = false;
            p.SetAllTicked(false);

            p.SetTicked("Assets/A/x.txt", true);

            Assert.That(Ticked(p), Is.EqualTo(new[] { "Assets/A/x.txt" }), "with folders hidden the .meta is a row of its own, as in 1.4.0");
        }

        [Test] public async Task FolderBoxClick_FromMixedTicksAll_ThenUnticksAll()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            using (var w = UnityGitTestWindow.Open(p))
            {
                p.SetTicked("Assets/A/x.txt", false);
                var row = Bind(w, p, A);

                row.Q<Toggle>().value = !row.Q<Toggle>().value; // what a click does

                Assert.That(Ticked(p), Is.EqualTo(AllRows));

                Bind(w, p, A, row);
                row.Q<Toggle>().value = !row.Q<Toggle>().value;

                Assert.That(Ticked(p), Is.EqualTo(new[] { "top.txt" }));
            }
        }

        [Test] public async Task SelectedFolderAndFile_ApplyToExpandedSelection()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            p.Select(new[] { AB, "top.txt" });

            p.ToggleRow(AB, false);

            Assert.That(Ticked(p), Is.EqualTo(new[] { "Assets/A.meta", "Assets/A/x.txt" }));

            p.ToggleSelection();

            Assert.That(Ticked(p), Is.EqualTo(AllRows), "one of them unticked: Space ticks all");

            p.Select(new[] { A, "Assets/A/x.txt" }); // the file is inside the folder: counted once
            p.ToggleSelection();

            Assert.That(Ticked(p), Is.EqualTo(new[] { "top.txt" }));
        }

        [Test] public async Task NewFileUnderUntickedFolder_DefaultsTicked_FolderMixed()
        {
            var (repo, s) = await SessionWithFilesAsync(Files);
            var p = Panel(s);
            p.SetTicked(A, false);

            GitSandbox.WriteFile(repo, "Assets/A/z.txt", "z");
            await s.RefreshAsync();
            p.Render();

            Assert.That(p.IsTicked("Assets/A/z.txt"), Is.True);
            Assert.That(p.FolderTick(A), Is.Null);
        }

        [Test] public async Task FolderOfConflictedFiles_EmptyAndDisabled()
        {
            // A stash pop stopped on a conflict in C/f.txt (no operation in progress), plus an untracked D/g.txt.
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "C/f.txt", "base\n", "initial");
            GitSandbox.WriteFile(repo, "C/f.txt", "stash\n");
            Assert.That((await repo.RunRawAsync("stash")).ExitCode, Is.EqualTo(0), "setup: stash");
            await GitSandbox.CommitFileAsync(repo, "C/f.txt", "main\n", "main edit");
            Assert.That((await repo.RunRawAsync("stash", "pop")).ExitCode, Is.Not.EqualTo(0), "setup: the pop stops on the conflict");
            GitSandbox.WriteFile(repo, "D/g.txt", "g");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            var p = Panel(s);
            using (var w = UnityGitTestWindow.Open(p))
            {
                p.SetTicked("C/", true);

                Assert.That(p.IsTicked("C/f.txt"), Is.False);
                Assert.That(p.FolderTick("C/"), Is.False);
                var box = Bind(w, p, "C/").Q<Toggle>();
                Assert.That((box.value, box.enabledSelf), Is.EqualTo((false, false)));
                Assert.That(p.FolderTick("D/"), Is.True);
            }
        }

        [Test] public async Task MergeLock_FolderBoxTickedAndDisabled()
        {
            var (_, s) = await MergeConflictWithFilesAsync("Assets/A/x.txt");
            var p = Panel(s);
            using (var w = UnityGitTestWindow.Open(p))
            {
                Assert.That(p.FolderTick(A), Is.True);
                var box = Bind(w, p, A).Q<Toggle>();
                Assert.That((box.value, box.enabledSelf, box.showMixedValue), Is.EqualTo((true, false, false)));
            }
        }

        [Test] public async Task RecycledRow_FolderThenFile_ShowsFileParts()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            using (var w = UnityGitTestWindow.Open(p))
            {
                p.SetTicked("Assets/A/x.txt", false); // A is mixed
                var row = Bind(w, p, A);

                Assert.That(row.Q<Image>(className: "unitygit-folder-icon").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(row.Q<Image>(className: "unitygit-folder-icon").image, Is.Not.Null);
                Assert.That(row.Q(className: "unitygit-kind-badge").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(row.Q<Label>(className: "unitygit-change-path").text, Is.EqualTo("Assets/A"));
                Assert.That(row.Q(className: "unitygit-change-meta").style.display.value, Is.EqualTo(DisplayStyle.Flex), "A.meta is folded in");
                Assert.That(row.Q<Toggle>().showMixedValue, Is.True);

                Bind(w, p, "top.txt", row);

                Assert.That(row.Q<Image>(className: "unitygit-folder-icon").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(row.Q(className: "unitygit-kind-badge").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(row.Q<Label>(className: "unitygit-change-path").text, Is.EqualTo("top.txt"));
                Assert.That(row.Q(className: "unitygit-change-meta").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That((row.Q<Toggle>().showMixedValue, row.Q<Toggle>().value), Is.EqualTo((false, true)));
            }
        }

        [Test] public async Task UntickedFolder_CommitLeavesItsFilesOut()
        {
            var (repo, s) = await SessionWithFilesAsync(Files);
            var p = Panel(s);
            p.SetTicked(A, false);
            p.Q<TextField>(UiNames.CommitMessage).value = "msg";

            var o = await p.SubmitCommitAsync();

            Assert.That(o.Succeeded, Is.True, o.Message);
            Assert.That(await HeadFiles(repo), Is.EquivalentTo(new[] { "top.txt" }));
        }

        [Test] public async Task HideFolders_ListIsTodaysList_ChoiceSaved()
        {
            var (_, s) = await SessionWithFilesAsync(Files);
            var p = Panel(s);

            p.ShowFolders = false;

            Assert.That(p.Entries, Is.EqualTo(AllRows));
            Assert.That(EditorPrefs.GetBool(ChangesPanel.ShowFoldersPref, true), Is.False);
            Assert.That(new ChangesPanel(s).Entries, Is.EqualTo(AllRows), "a new panel reads the choice");
        }

        [Test] public async Task HeaderToggle_HidesAndShowsFolders()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            using (UnityGitTestWindow.Open(p))
            {
                var toggle = p.Q<ToolbarToggle>(UiNames.ChangesShowFolders);
                Assert.That((toggle.value, toggle.tooltip), Is.EqualTo((true, "Show folders")));
                Assert.That(toggle.Q<Image>(className: "unitygit-folder-icon").image, Is.Not.Null);

                toggle.value = false;

                Assert.That(p.Entries, Is.EqualTo(AllRows));

                toggle.value = true;

                Assert.That(p.Entries, Does.Contain(A));
            }
        }

        [Test] public async Task HideFolders_KeepsSelectedFiles_DropsFolders()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            p.Select(new[] { A, "top.txt" });

            p.ShowFolders = false;

            Assert.That(p.SelectedRows, Is.EqualTo(new[] { "top.txt" }));
            Assert.That(p.Q<ListView>(UiNames.ChangesList).selectedIndices, Is.EqualTo(new[] { 3 }));
        }

        [Test] public async Task HideFolders_FolderMetaKeepsItsTick()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);
            p.SetTicked(A, false);

            p.ShowFolders = false;

            Assert.That(p.IsTicked("Assets/A.meta"), Is.False);
            Assert.That(Ticked(p), Is.EqualTo(new[] { "top.txt" }));
        }

        [Test] public async Task Preview_OneFolderSelected_CountsItsFiles()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);

            p.Select(new[] { A });
            Assert.That(p.Preview.NoteText, Is.EqualTo("3 files in this folder — select one to preview"));

            p.Select(new[] { AB });
            Assert.That(p.Preview.NoteText, Is.EqualTo("1 file in this folder — select it to preview"));
        }

        // Several lines selected: the files they stand for (each once), and how many of the lines are folders.
        [Test] public async Task Preview_SeveralSelected_CountsFilesAndFolders()
        {
            var p = Panel((await SessionWithFilesAsync(Files)).s);

            p.Select(new[] { A, "top.txt" });
            Assert.That(p.Preview.NoteText, Is.EqualTo("4 files and 1 folder selected"));

            p.Select(new[] { A, AB, "Assets/A/x.txt" }); // AB and x.txt are inside A: their files count once
            Assert.That(p.Preview.NoteText, Is.EqualTo("3 files and 2 folders selected"));

            p.Select(new[] { AB, "Assets/A/B/y.txt" });
            Assert.That(p.Preview.NoteText, Is.EqualTo("1 file and 1 folder selected"));

            p.Select(new[] { "Assets/A/x.txt", "top.txt" });
            Assert.That(p.Preview.NoteText, Is.EqualTo("2 files selected"));
        }

        [Test] public async Task MenuOnFolder_TargetsItsFiles_OpensTheFolder()
        {
            var (repo, s) = await SessionWithFilesAsync(Files);
            var p = Panel(s);

            var st = p.GetRowMenuState(A);

            Assert.That(st.Targets, Is.EqualTo(new[] { "Assets/A.meta", "Assets/A/B/y.txt", "Assets/A/x.txt" }));
            Assert.That(st.RevertDisabledReason, Is.Null);
            Assert.That(st.ExplorerPath, Is.SamePath(Path.Combine(repo.WorkingDirectory, "Assets", "A")));

            p.Select(new[] { A, "Assets/A/x.txt" });

            Assert.That(p.GetRowMenuState(A).Targets, Is.EqualTo(new[] { "Assets/A.meta", "Assets/A/B/y.txt", "Assets/A/x.txt" }), "each file once");
        }

        [Test] public async Task MenuOnDeletedFolder_ExplorerDisabled()
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "Gone/f.txt", "f\n", "initial");
            Directory.Delete(Path.Combine(repo.WorkingDirectory, "Gone"), true);
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            var p = Panel(s);

            Assert.That(p.GetRowMenuState("Gone/").ExplorerPath, Is.Null);
            Assert.That(p.GetRowMenuState("Gone/").Targets, Is.EqualTo(new[] { "Gone/f.txt" }));
        }
    }
}
