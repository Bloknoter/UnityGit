#nullable enable
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ChangePreviewLoadTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

        async Task<GitRepository> RepoAsync() => await _sb.InitRepoAsync("repo");

        async Task<GitSession> SessionAsync(GitRepository repo)
        {
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            return s;
        }

        static ChangeItem Row(GitSession s, string path) => s.Snapshot.Changes.Single(c => c.Path == path);

        async Task<ChangePreview> LoadAsync(GitRepository repo, string path)
        {
            var s = await SessionAsync(repo);
            var preview = await s.LoadChangePreviewAsync(Row(s, path));
            Assert.That(preview, Is.Not.Null, "the preview loads");
            return preview!;
        }

        static string Lines(int from, int to) => string.Concat(Enumerable.Range(from, to - from + 1).Select(i => "l" + i + "\n"));

        [Test]
        public async Task Load_ModifiedText_RowsAndCounts()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "a.txt", "one\ntwo\nthree\n", "base");
            GitSandbox.WriteFile(repo, "a.txt", "one\nTWO\nthree\nfour\n");

            var f = (await LoadAsync(repo, "a.txt")).File;

            Assert.That(f.Change, Is.EqualTo(DiffChange.Modified));
            Assert.That(f.AddedCount, Is.EqualTo(2));
            Assert.That(f.RemovedCount, Is.EqualTo(1));
            Assert.That(f.HiddenLines, Is.EqualTo(0));
            Assert.That(f.Rows.Select(r => (r.Kind, r.Text, r.OldLine, r.NewLine)), Is.EqualTo(new (DiffRowKind, string, int?, int?)[]
            {
                (DiffRowKind.Unchanged, "one", 1, 1),
                (DiffRowKind.Removed, "two", 2, null),
                (DiffRowKind.Added, "TWO", null, 2),
                (DiffRowKind.Unchanged, "three", 3, 3),
                (DiffRowKind.Added, "four", null, 4),
            }));
            Assert.That(f.FullPath, Is.EqualTo(Path.GetFullPath(Path.Combine(repo.WorkingDirectory, "a.txt"))));
        }

        [Test]
        public async Task Load_TwoHunks_GapBetween()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "a.txt", Lines(1, 30), "base");
            GitSandbox.WriteFile(repo, "a.txt", Lines(1, 30).Replace("l2\n", "L2\n").Replace("l29\n", "L29\n"));

            var f = (await LoadAsync(repo, "a.txt")).File;

            var gaps = f.Rows.Where(r => r.Kind == DiffRowKind.Gap).ToList();
            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.That(gaps[0].GapLines, Is.EqualTo(20)); // hunks cover old lines 1–5 and 26–30
            Assert.That(gaps[0].Text, Is.Empty);
            Assert.That(gaps[0].OldLine, Is.Null);
            Assert.That(gaps[0].NewLine, Is.Null);
            Assert.That(f.Rows.Where(r => r.Kind != DiffRowKind.Gap).All(r => r.GapLines == 0), Is.True);
        }

        [Test]
        public async Task Load_Untracked_EveryLineAdded()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "base.txt", "x\n", "base");
            GitSandbox.WriteFile(repo, "new.txt", "a\nb\nc\n");

            var f = (await LoadAsync(repo, "new.txt")).File;

            Assert.That(f.Change, Is.EqualTo(DiffChange.Added));
            Assert.That(f.IsUntracked, Is.True);
            Assert.That(f.Rows.All(r => r.Kind == DiffRowKind.Added), Is.True);
            Assert.That(f.Rows.Select(r => r.NewLine), Is.EqualTo(new int?[] { 1, 2, 3 }));
            Assert.That(f.Rows.Select(r => r.Text), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(f.AddedCount, Is.EqualTo(3));
        }

        [Test]
        public async Task Load_StagedNewFile_EveryLineAdded()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "base.txt", "x\n", "base");
            GitSandbox.WriteFile(repo, "new.txt", "a\nb\n");
            await repo.Index.StageAsync(new[] { "new.txt" });

            var f = (await LoadAsync(repo, "new.txt")).File;

            Assert.That(f.Change, Is.EqualTo(DiffChange.Added));
            Assert.That(f.IsUntracked, Is.False);
            Assert.That(f.Rows.Select(r => (r.Kind, r.Text)), Is.EqualTo(new[] { (DiffRowKind.Added, "a"), (DiffRowKind.Added, "b") }));
        }

        [Test]
        public async Task Load_Deleted_EveryLineRemoved()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "a.txt", "a\nb\n", "base");
            File.Delete(Path.Combine(repo.WorkingDirectory, "a.txt"));

            var f = (await LoadAsync(repo, "a.txt")).File;

            Assert.That(f.Change, Is.EqualTo(DiffChange.Deleted));
            Assert.That(f.Rows.Select(r => (r.Kind, r.OldLine)), Is.EqualTo(new (DiffRowKind, int?)[] { (DiffRowKind.Removed, 1), (DiffRowKind.Removed, 2) }));
            Assert.That(f.RemovedCount, Is.EqualTo(2));
            Assert.That(f.NewSize, Is.Null);
        }

        [Test]
        public async Task Load_Binary_SizesNoRows()
        {
            var repo = await RepoAsync();
            var path = Path.Combine(repo.WorkingDirectory, "img.bin");
            File.WriteAllBytes(path, new byte[] { 0, 1, 2, 0 });
            await repo.Index.StageAsync(new[] { "img.bin" });
            await repo.CommitAsync("base");
            File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3, 4, 0 });

            var f = (await LoadAsync(repo, "img.bin")).File;

            Assert.That(f.IsBinary, Is.True);
            Assert.That(f.Rows, Is.Empty);
            Assert.That(f.OldSize, Is.EqualTo(4));
            Assert.That(f.NewSize, Is.EqualTo(6));
        }

        [Test]
        public async Task Load_StagedRename_OldPath()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "a.txt", Lines(1, 10), "base");
            Assert.That((await repo.RunRawAsync("mv", "a.txt", "b.txt")).ExitCode, Is.EqualTo(0), "setup: git mv");
            GitSandbox.WriteFile(repo, "b.txt", Lines(1, 10).Replace("l5\n", "L5\n"));
            await repo.Index.StageAsync(new[] { "b.txt" });

            var f = (await LoadAsync(repo, "b.txt")).File;

            Assert.That(f.Change, Is.EqualTo(DiffChange.Renamed));
            Assert.That(f.Path, Is.EqualTo("b.txt"));
            Assert.That(f.OldPath, Is.EqualTo("a.txt"));
            Assert.That(f.AddedCount, Is.EqualTo(1));
            Assert.That(f.RemovedCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Load_WithMeta_LoadsBoth()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "Assets/x.txt", "a\n", "asset");
            await GitSandbox.CommitFileAsync(repo, "Assets/x.txt.meta", "guid: 1\n", "meta");
            GitSandbox.WriteFile(repo, "Assets/x.txt", "b\n");
            GitSandbox.WriteFile(repo, "Assets/x.txt.meta", "guid: 2\n");
            var s = await SessionAsync(repo);
            var row = Row(s, "Assets/x.txt");
            Assert.That(row.HasMeta, Is.True, "setup: the .meta is paired");

            var preview = await s.LoadChangePreviewAsync(row);

            Assert.That(preview!.File.Path, Is.EqualTo("Assets/x.txt"));
            Assert.That(preview.Meta, Is.Not.Null);
            Assert.That(preview.Meta!.Path, Is.EqualTo("Assets/x.txt.meta"));
            Assert.That(preview.Meta.Change, Is.EqualTo(DiffChange.Modified));
            Assert.That(preview.Meta.Rows.Select(r => r.Text), Is.EqualTo(new[] { "guid: 1", "guid: 2" }));
        }

        [Test]
        public async Task Load_NoMeta_MetaNull()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "a.txt", "a\n", "base");
            GitSandbox.WriteFile(repo, "a.txt", "b\n");

            Assert.That((await LoadAsync(repo, "a.txt")).Meta, Is.Null);
        }

        [Test]
        public async Task Load_CapsRows()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "base.txt", "x\n", "base");
            GitSandbox.WriteFile(repo, "big.txt", Lines(1, 1500));

            var f = (await LoadAsync(repo, "big.txt")).File;

            Assert.That(f.Rows, Has.Count.EqualTo(DiffLoader.MaxRows));
            Assert.That(f.HiddenLines, Is.EqualTo(500));
            Assert.That(f.AddedCount, Is.EqualTo(1500));
            Assert.That(f.Rows.Last().Text, Is.EqualTo("l1000"));
        }

        [Test]
        public async Task Load_LargeNewFile_ReadsHead()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "base.txt", "x\n", "base");
            var line = new string('a', 99) + "\n";
            var total = (int)(DiffLoader.LargeNewFileBytes / line.Length) + 1000; // over the limit
            File.WriteAllText(Path.Combine(repo.WorkingDirectory, "huge.txt"), new StringBuilder(line.Length * total).Insert(0, line, total).ToString());

            var f = (await LoadAsync(repo, "huge.txt")).File;

            Assert.That(f.Change, Is.EqualTo(DiffChange.Added));
            Assert.That(f.IsUntracked, Is.True);
            Assert.That(f.IsBinary, Is.False);
            Assert.That(f.Rows, Has.Count.EqualTo(DiffLoader.MaxRows));
            Assert.That(f.Rows.All(r => r.Kind == DiffRowKind.Added && r.Text == new string('a', 99)), Is.True);
            Assert.That(f.Rows.Last().NewLine, Is.EqualTo(DiffLoader.MaxRows));
            Assert.That(f.AddedCount, Is.EqualTo(total));
            Assert.That(f.HiddenLines, Is.EqualTo(total - DiffLoader.MaxRows));
            Assert.That(f.NewSize, Is.EqualTo((long)line.Length * total));
        }

        [Test]
        public async Task Load_LargeNewBinary_IsBinary()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "base.txt", "x\n", "base");
            var bytes = new byte[DiffLoader.LargeNewFileBytes + 1024];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)'b';
            bytes[10] = 0;
            File.WriteAllBytes(Path.Combine(repo.WorkingDirectory, "huge.bin"), bytes);

            var f = (await LoadAsync(repo, "huge.bin")).File;

            Assert.That(f.IsBinary, Is.True);
            Assert.That(f.Rows, Is.Empty);
            Assert.That(f.NewSize, Is.EqualTo(bytes.Length));
        }

        [Test]
        public async Task Load_LargeNewLfsFile_IsLfs()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, ".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n", "lfs");
            var bytes = new byte[DiffLoader.LargeNewFileBytes + 1024]; // zeros: binary
            File.WriteAllBytes(Path.Combine(repo.WorkingDirectory, "model.bin"), bytes);

            var f = (await LoadAsync(repo, "model.bin")).File;

            Assert.That(f.IsLfs, Is.True);
            Assert.That(f.Rows, Is.Empty);
            Assert.That(f.NewSize, Is.EqualTo(bytes.Length));
        }

        [Test]
        public async Task Load_CancelledToken_ReturnsNullWithoutWarning()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "a.txt", "a\n", "base");
            GitSandbox.WriteFile(repo, "a.txt", "b\n");
            var s = await SessionAsync(repo);
            var warnings = new System.Collections.Generic.List<string>();
            s.Warning = w => { lock (warnings) warnings.Add(w); };
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            var preview = await s.LoadChangePreviewAsync(Row(s, "a.txt"), cts.Token);

            Assert.That(preview, Is.Null);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task Load_CrlfLines_NoCarriageReturn()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "base.txt", "x\n", "base");
            GitSandbox.WriteFile(repo, "crlf.txt", "a\r\nb\r\n");

            var f = (await LoadAsync(repo, "crlf.txt")).File;

            Assert.That(f.Rows.Select(r => r.Text), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public async Task Load_NonAsciiPath()
        {
            var repo = await RepoAsync();
            const string path = "Assets/Новая папка/файл.cs";
            await GitSandbox.CommitFileAsync(repo, path, "class A {}\n", "base");
            GitSandbox.WriteFile(repo, path, "class B {}\n");

            var f = (await LoadAsync(repo, path)).File;

            Assert.That(f.Change, Is.EqualTo(DiffChange.Modified));
            Assert.That(f.Rows.Select(r => r.Text), Is.EqualTo(new[] { "class A {}", "class B {}" }));
        }

        [Test]
        public async Task Load_UnbornBranch_EverythingAdded()
        {
            var repo = await RepoAsync();
            GitSandbox.WriteFile(repo, "a.txt", "a\n");
            await repo.Index.StageAsync(new[] { "a.txt" });

            var f = (await LoadAsync(repo, "a.txt")).File;

            Assert.That(f.Change, Is.EqualTo(DiffChange.Added));
            Assert.That(f.Rows.Select(r => (r.Kind, r.Text)), Is.EqualTo(new[] { (DiffRowKind.Added, "a") }));
        }

        [Test]
        public async Task Load_NotARepository_Null()
        {
            var s = _sb.CreateSession(_sb.MakeDir("plain"), new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.Availability, Is.EqualTo(RepoAvailability.NotARepository), "setup: not a repository");

            Assert.That(await s.LoadChangePreviewAsync(new ChangeItem { Path = "a.txt", GitPaths = new[] { "a.txt" } }), Is.Null);
        }
    }
}
