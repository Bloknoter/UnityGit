#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ChangePreviewTests
    {
        const string RowClass = "unitygit-diff-row";

        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        // A repo with one commit of base.txt, then whatever the test changes, and a session that has seen it.
        async Task<(GitRepository repo, GitSession s)> RepoAsync(params (string Path, string Content)[] committed)
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "base.txt", "base\n", "initial");
            foreach (var (path, content) in committed) await GitSandbox.CommitFileAsync(repo, path, content, "add " + path);
            return (repo, _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks()));
        }

        static async Task<ChangesPanel> PanelAsync(GitSession s, params string[] selected)
        {
            await s.RefreshAsync();
            var p = new ChangesPanel(s);
            p.Select(selected);
            await p.PreviewLoad;
            return p;
        }

        static string Title(ChangesPanel p) => p.Q<Label>(UiNames.PreviewTitle).text;
        static string Summary(ChangesPanel p) => p.Q<Label>(UiNames.PreviewSummary).text;
        static List<VisualElement> FileRows(ChangesPanel p) => p.Q(UiNames.PreviewFile).Query(className: RowClass).ToList();
        static string RowText(VisualElement row) => row.Q<Label>(className: "unitygit-diff-text").text;
        static string Lines(int from, int to) => string.Concat(Enumerable.Range(from, to - from + 1).Select(i => "l" + i + "\n"));

        [Test]
        public void Texts()
        {
            Assert.That(PreviewText.Selected(3), Is.EqualTo("3 files selected"));
            Assert.That(PreviewText.Size(512), Is.EqualTo("512 B"));
            Assert.That(PreviewText.Size(12_595), Is.EqualTo("12.3 KB"));
            Assert.That(PreviewText.Size(1_258_291), Is.EqualTo("1.2 MB"));
            Assert.That(PreviewText.Sizes(1_258_291, 1_468_006), Is.EqualTo("1.2 MB → 1.4 MB"));
            Assert.That(PreviewText.Sizes(null, 512), Is.EqualTo("512 B"));
            Assert.That(PreviewText.Summary(new FileDiffView { Change = DiffChange.Modified, AddedCount = 12, RemovedCount = 3 }), Is.EqualTo("+12 −3"));
            Assert.That(PreviewText.Summary(new FileDiffView { Change = DiffChange.Added, AddedCount = 1 }), Is.EqualTo("1 line"));
            Assert.That(PreviewText.Title(new FileDiffView { Path = "b.txt", OldPath = "a.txt", Change = DiffChange.Renamed }), Is.EqualTo("a.txt → b.txt"));
        }

        [Test]
        public async Task Preview_NothingSelected_AsksToSelect()
        {
            var (repo, s) = await RepoAsync();
            GitSandbox.WriteFile(repo, "a.txt", "a\n");

            var p = await PanelAsync(s);

            Assert.That(p.Preview.NoteText, Is.EqualTo("Select a file to preview"));
        }

        [Test]
        public async Task Preview_TwoSelected_CountsThem()
        {
            var (repo, s) = await RepoAsync();
            GitSandbox.WriteFile(repo, "a.txt", "a\n");
            GitSandbox.WriteFile(repo, "b.txt", "b\n");

            var p = await PanelAsync(s, "a.txt", "b.txt");

            Assert.That(p.Preview.NoteText, Is.EqualTo("2 files selected"));
        }

        [Test]
        public async Task Preview_Modified_ShowsDiffRows()
        {
            var (repo, s) = await RepoAsync(("a.txt", "one\ntwo\n"));
            GitSandbox.WriteFile(repo, "a.txt", "one\nTWO\n");

            var p = await PanelAsync(s, "a.txt");

            Assert.That(p.Preview.NoteText, Is.Null);
            Assert.That(Title(p), Is.EqualTo("a.txt"));
            Assert.That(Summary(p), Is.EqualTo("+1 −1"));
            var rows = FileRows(p);
            Assert.That(rows.Select(r => r.ClassListContains(RowClass + "--unchanged")), Is.EqualTo(new[] { true, false, false }));
            Assert.That(rows[1].ClassListContains(RowClass + "--removed"), Is.True);
            Assert.That(rows[2].ClassListContains(RowClass + "--added"), Is.True);
            Assert.That(rows.Select(r => r.Q<Label>(className: "unitygit-diff-marker").text), Is.EqualTo(new[] { " ", "-", "+" }));
            Assert.That(rows.Select(RowText), Is.EqualTo(new[] { "one", "two", "TWO" }));
        }

        [Test]
        public async Task Preview_NewFile_ShowsPlainContent()
        {
            var (repo, s) = await RepoAsync();
            GitSandbox.WriteFile(repo, "a.txt", "a\nb\n");

            var p = await PanelAsync(s, "a.txt");

            var rows = FileRows(p);
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows.All(r => r.ClassListContains(RowClass + "--content")), Is.True);
            Assert.That(rows.All(r => r.Q<Label>(className: "unitygit-diff-marker") == null), Is.True);
            Assert.That(rows.Select(RowText), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(Summary(p), Is.EqualTo("2 lines"));
        }

        [Test]
        public async Task Preview_Deleted_AllRemoved()
        {
            var (repo, s) = await RepoAsync(("a.txt", "a\nb\n"));
            File.Delete(Path.Combine(repo.WorkingDirectory, "a.txt"));

            var p = await PanelAsync(s, "a.txt");

            var rows = FileRows(p);
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows.All(r => r.ClassListContains(RowClass + "--removed")), Is.True);
        }

        [Test]
        public async Task Preview_Binary_ShowsSizes()
        {
            var (repo, s) = await RepoAsync();
            var path = Path.Combine(repo.WorkingDirectory, "data.bin");
            File.WriteAllBytes(path, new byte[] { 0, 1, 2, 0 });
            await repo.Index.StageAsync(new[] { "data.bin" });
            await repo.CommitAsync("bin");
            File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3, 0 });

            var p = await PanelAsync(s, "data.bin");

            Assert.That(p.Preview.NoteText, Is.EqualTo("Binary file · 4 B → 5 B"));
            Assert.That(FileRows(p), Is.Empty);
        }

        [Test]
        public async Task Preview_WithMeta_ShowsMetaSection()
        {
            var (repo, s) = await RepoAsync(("Assets/x.txt", "a\n"), ("Assets/x.txt.meta", "guid: 1\n"));
            GitSandbox.WriteFile(repo, "Assets/x.txt", "b\n");
            GitSandbox.WriteFile(repo, "Assets/x.txt.meta", "guid: 2\n");

            var p = await PanelAsync(s, "Assets/x.txt");

            var meta = p.Q(UiNames.PreviewMeta);
            Assert.That(meta, Is.Not.Null);
            Assert.That(meta.Query(className: RowClass).ToList().Select(RowText), Is.EqualTo(new[] { "guid: 1", "guid: 2" }));
            Assert.That(FileRows(p).Select(RowText), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public async Task Preview_Conflicted_SaysResolve()
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.PrepareConflictAsync();
            Assert.That((await trio.Local.RunRawAsync("pull", "--no-rebase")).ExitCode, Is.Not.EqualTo(0), "setup: the pull stops on the conflict");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());

            var p = await PanelAsync(s, "README.md");

            Assert.That(p.Preview.NoteText, Is.EqualTo("Conflicted — resolve it in the Conflicts tab"));
        }

        [Test]
        public async Task Preview_CappedFile_SaysMoreLines()
        {
            var (repo, s) = await RepoAsync();
            GitSandbox.WriteFile(repo, "big.txt", Lines(1, 1500));

            var p = await PanelAsync(s, "big.txt");

            Assert.That(FileRows(p), Has.Count.EqualTo(1000));
            Assert.That(p.Q<Label>(className: "unitygit-diff-more")?.text, Is.EqualTo(ConflictText.MoreLines(500)));
        }

        [Test]
        public async Task Preview_LongLine_IsCut()
        {
            var (repo, s) = await RepoAsync();
            GitSandbox.WriteFile(repo, "mesh.asset", new string('x', 300_000) + "\n");

            var p = await PanelAsync(s, "mesh.asset");

            var text = RowText(FileRows(p).Single());
            Assert.That(text.Length, Is.EqualTo(ConflictText.MaxLineChars + 1));
            Assert.That(text, Does.EndWith("…"));
        }

        [Test]
        public async Task Preview_LastSelectionWins()
        {
            // The first file goes through git (several processes); the second is a big new file read directly, so it
            // finishes first. The first file's result arrives last and must be dropped.
            var (repo, s) = await RepoAsync(("a.txt", "a\n"));
            GitSandbox.WriteFile(repo, "a.txt", "A\n");
            var line = new string('h', 99) + "\n";
            var count = (int)(DiffLoader.LargeNewFileBytes / line.Length) + 10;
            File.WriteAllText(Path.Combine(repo.WorkingDirectory, "huge.txt"), new StringBuilder(line.Length * count).Insert(0, line, count).ToString());
            await s.RefreshAsync();
            var p = new ChangesPanel(s);

            p.Select(new[] { "a.txt" });
            var first = p.PreviewLoad;
            p.Select(new[] { "huge.txt" });
            var second = p.PreviewLoad;
            await Task.WhenAll(first, second);

            Assert.That(Title(p), Is.EqualTo("huge.txt"));
            Assert.That(RowText(FileRows(p)[0]), Is.EqualTo(new string('h', 99)));
        }

        [Test]
        public async Task Preview_AfterCommit_AsksToSelect()
        {
            var (repo, s) = await RepoAsync();
            GitSandbox.WriteFile(repo, "a.txt", "a\n");
            var p = await PanelAsync(s, "a.txt");
            Assert.That(FileRows(p), Has.Count.EqualTo(1), "setup: the file is previewed");
            p.Q<TextField>(UiNames.CommitMessage).value = "add a";

            var outcome = await p.SubmitCommitAsync();
            await p.PreviewLoad;

            Assert.That(outcome.Succeeded, Is.True, "setup: the commit succeeds");
            Assert.That(p.Preview.NoteText, Is.EqualTo("Select a file to preview"));
        }

        [Test]
        public async Task Preview_RefreshSameFile_KeepsContent()
        {
            var (repo, s) = await RepoAsync(("a.txt", "a\n"));
            GitSandbox.WriteFile(repo, "a.txt", "b\n");
            var p = await PanelAsync(s, "a.txt");
            GitSandbox.WriteFile(repo, "a.txt", "c\n");
            await s.RefreshAsync();

            p.Render();

            Assert.That(p.Preview.NoteText, Is.Null, "the old content stays while the file reloads");
            Assert.That(FileRows(p).Select(RowText), Is.EqualTo(new[] { "a", "b" }));
            await p.PreviewLoad;
            Assert.That(FileRows(p).Select(RowText), Is.EqualTo(new[] { "a", "c" }));
        }

        [Test]
        public async Task Layout_SplitHoldsListAndPreview()
        {
            var (_, s) = await RepoAsync();

            var p = await PanelAsync(s);

            var split = p.Q<TwoPaneSplitView>(UiNames.ChangesSplit);
            Assert.That(split, Is.Not.Null);
            var panes = split.Children().ToList();
            Assert.That(panes, Has.Count.EqualTo(2));
            Assert.That(panes[0].Q<ListView>(UiNames.ChangesList), Is.Not.Null);
            Assert.That(panes[1], Is.SameAs(p.Preview));
        }
    }
}
