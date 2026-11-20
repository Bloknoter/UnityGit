#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ConflictReadTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

        async Task<(FlexGit.GitRepository repo, GitSession s)> MergedAsync(params ConflictFile[] files)
        {
            var repo = await _sb.InitRepoAsync("repo");
            await ConflictScenarios.MergeAsync(repo, files);
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            return (repo, s);
        }

        [Test]
        public async Task Snapshot_ListsConflictedPathsSeparately()
        {
            var (_, s) = await MergedAsync(ConflictScenarios.TwoPlaces, ConflictScenarios.Scene, ConflictScenarios.SceneMeta);

            Assert.That(s.Snapshot.ConflictedPaths, Is.EqualTo(new[] { "Assets/Level.unity", "Assets/Level.unity.meta", "f.txt" }));
            var row = s.Snapshot.Changes.Single(c => c.Path == "Assets/Level.unity");
            Assert.That(row.HasMeta, Is.True);
            Assert.That(row.IsConflicted, Is.True);
        }

        [Test]
        public async Task Snapshot_NoConflicts_EmptyList()
        {
            var repo = await _sb.InitRepoAsync("repo");
            await GitSandbox.CommitFileAsync(repo, "a.txt", "a\n", "one");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            Assert.That(s.Snapshot.ConflictedPaths, Is.Empty);
        }

        [Test]
        public async Task Overview_DescribesEachFile()
        {
            var (_, s) = await MergedAsync(ConflictScenarios.TwoPlaces, ConflictScenarios.Binary, ConflictScenarios.DeletedByThem);

            var o = await s.LoadConflictOverviewAsync();

            Assert.That(o, Is.Not.Null);
            Assert.That(o!.Operation, Is.EqualTo(RepoOperation.Merging));
            Assert.That(o.Files.Select(f => f.Path), Is.EqualTo(new[] { "f.txt", "gone.txt", "img.bin" }));
            var text = o.Files.Single(f => f.Path == "f.txt");
            Assert.That(text.Type, Is.EqualTo(ConflictType.BothModified));
            Assert.That(text.PlaceCount, Is.EqualTo(2));
            Assert.That(text.IsBinary, Is.False);
            var gone = o.Files.Single(f => f.Path == "gone.txt");
            Assert.That(gone.Type, Is.EqualTo(ConflictType.DeletedByThem));
            Assert.That(gone.PlaceCount, Is.EqualTo(0));
            Assert.That(gone.ExistsInWorkingTree, Is.True);
            var bin = o.Files.Single(f => f.Path == "img.bin");
            Assert.That(bin.IsBinary, Is.True);
            Assert.That(bin.PlaceCount, Is.EqualTo(0));
        }

        [Test]
        public async Task LoadFile_SplitsTextAndPlaces()
        {
            var (repo, s) = await MergedAsync(ConflictScenarios.TwoPlaces);

            var f = (await s.LoadConflictFileAsync("f.txt"))!;

            Assert.That(f.Info.Type, Is.EqualTo(ConflictType.BothModified));
            Assert.That(f.OursName, Is.EqualTo("main"));
            Assert.That(f.TheirsName, Is.EqualTo("feature"));
            Assert.That(f.IsSceneOrPrefab, Is.False);
            Assert.That(f.FullPath, Is.EqualTo(Path.GetFullPath(Path.Combine(repo.WorkingDirectory, "f.txt"))));
            Assert.That(f.Places.Count, Is.EqualTo(2));
            Assert.That(f.Places[0].StartLine, Is.EqualTo(2));
            Assert.That(f.Places[0].EndLine, Is.EqualTo(6));
            Assert.That(f.Places[0].Ours, Is.EqualTo("TWO main\n"));
            Assert.That(f.Places[0].Theirs, Is.EqualTo("TWO feature\n"));
            Assert.That(f.Places[0].Base, Is.Null);
            Assert.That(f.Places[1].StartLine, Is.EqualTo(12));
            Assert.That(f.Places[1].EndLine, Is.EqualTo(16));
            Assert.That(f.Parts.Count, Is.EqualTo(5));
            Assert.That(f.Parts[0].Text, Is.EqualTo("one\n"));
            Assert.That(f.Parts[1].Place, Is.SameAs(f.Places[0]));
            Assert.That(f.Parts[2].Text, Is.EqualTo("three\nfour\nfive\nsix\nseven\n"));
            Assert.That(f.Parts[3].Place, Is.SameAs(f.Places[1]));
            Assert.That(f.Parts[4].Text, Is.EqualTo("nine\n"));
        }

        [Test]
        public async Task Place_LabelsAndTexts()
        {
            var (_, s) = await MergedAsync(ConflictScenarios.TwoPlaces);

            var f = (await s.LoadConflictFileAsync("f.txt"))!;
            var p = f.Places[0];

            Assert.That(p.Label(ConflictPick.Ours), Is.EqualTo("Use main"));
            Assert.That(p.Label(ConflictPick.Theirs), Is.EqualTo("Use feature"));
            Assert.That(p.Label(ConflictPick.OursThenTheirs), Is.EqualTo("Use main then feature"));
            Assert.That(p.Label(ConflictPick.TheirsThenOurs), Is.EqualTo("Use feature then main"));
            Assert.That(p.TextFor(ConflictPick.OursThenTheirs), Is.EqualTo("TWO main\nTWO feature\n"));
            Assert.That(p.TextFor(ConflictPick.TheirsThenOurs), Is.EqualTo("TWO feature\nTWO main\n"));
            Assert.That(f.Label(ConflictPick.Theirs), Is.EqualTo("Use feature"));
        }

        [Test]
        public async Task PreviewText_AppliesPicksWithoutWriting()
        {
            var (repo, s) = await MergedAsync(ConflictScenarios.TwoPlaces);

            var f = (await s.LoadConflictFileAsync("f.txt"))!;
            var preview = f.PreviewText(new Dictionary<int, ConflictPick> { { 0, ConflictPick.Theirs } });

            Assert.That(preview, Is.EqualTo(
                "one\nTWO feature\nthree\nfour\nfive\nsix\nseven\n<<<<<<< HEAD\nEIGHT main\n=======\nEIGHT feature\n>>>>>>> feature\nnine\n"));
            var onDisk = File.ReadAllText(Path.Combine(repo.WorkingDirectory, "f.txt"));
            Assert.That(onDisk, Does.Contain("TWO main"));
            Assert.That(onDisk, Does.Contain("<<<<<<<"));
        }

        [Test]
        public async Task LoadFile_PullMerge_NamesBranchAndUpstream()
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.PrepareConflictAsync();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            await s.PullAsync();

            var f = (await s.LoadConflictFileAsync("README.md"))!;

            Assert.That(f.OursName, Is.EqualTo("main"));
            Assert.That(f.TheirsName, Is.EqualTo("origin/main"));
            Assert.That(f.Places[0].Label(ConflictPick.Theirs), Is.EqualTo("Use origin/main"));
        }

        [Test]
        public async Task LoadFile_RebasePull_UsesGitLabels()
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.PrepareConflictAsync();
            await trio.Local.Config.SetAsync("pull.rebase", "true");
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            await s.PullAsync();

            var f = (await s.LoadConflictFileAsync("README.md"))!;

            Assert.That(f.OursName, Is.EqualTo("HEAD"));
            Assert.That(f.TheirsName, Does.Match(@"^[0-9a-f]{7} \(local edit\)$"));
        }

        [Test]
        public async Task LoadFile_Binary_HasNoPlaces()
        {
            var (_, s) = await MergedAsync(ConflictScenarios.Binary);

            var f = (await s.LoadConflictFileAsync("img.bin"))!;

            Assert.That(f.Info.IsBinary, Is.True);
            Assert.That(f.Places, Is.Empty);
            Assert.That(f.Parts, Is.Empty);
            Assert.That(f.OursName, Is.EqualTo("main"));
            Assert.That(f.TheirsName, Does.Match("^[0-9a-f]{7}$"));
        }

        // FlexGit decides "binary" by a NUL byte, so it reads a conflicted LFS pointer as text with one place.
        [Test]
        public async Task LoadFile_LfsPointerConflict_HasNoPlaces()
        {
            var repo = await _sb.InitRepoAsync("repo");
            if (!repo.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            await ConflictScenarios.LfsMergeAsync(repo, ConflictScenarios.Binary);
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(File.ReadAllText(Path.Combine(repo.WorkingDirectory, "img.bin")),
                Does.StartWith("version https://git-lfs.github.com/spec/v1\n<<<<<<<"), "setup: pointer text with markers");

            var f = (await s.LoadConflictFileAsync("img.bin"))!;
            var o = (await s.LoadConflictOverviewAsync())!;

            Assert.That(f.Info.IsBinary, Is.True);
            Assert.That(f.Info.PlaceCount, Is.EqualTo(0));
            Assert.That(f.Places, Is.Empty);
            Assert.That(f.Parts, Is.Empty);
            var info = o.Files.Single(x => x.Path == "img.bin");
            Assert.That((info.IsBinary, info.PlaceCount), Is.EqualTo((true, 0)));
        }

        [Test]
        public async Task LoadFile_UnreadableFile_WarnsAndReturnsNull()
        {
            var (repo, s) = await MergedAsync(ConflictScenarios.TwoPlaces);
            var warnings = new List<string>();
            s.Warning = warnings.Add;

            ConflictFileView? f;
            using (new FileStream(Path.Combine(repo.WorkingDirectory, "f.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                f = await s.LoadConflictFileAsync("f.txt");

            Assert.That(f, Is.Null);
            Assert.That(warnings, Has.Some.StartsWith("Loading conflicts failed: "));
        }

        // A disposed session cancels its reads: they return null without a warning.
        [Test]
        public async Task Loads_AfterDispose_ReturnNullQuietly()
        {
            var (_, s) = await MergedAsync(ConflictScenarios.TwoPlaces);
            var warnings = new List<string>();
            s.Warning = warnings.Add;

            s.Dispose();

            // Caught here: an async test whose task ends cancelled is reported as passed.
            OperationCanceledException? thrown = null;
            try
            {
                Assert.That(await s.LoadConflictOverviewAsync(), Is.Null);
                Assert.That(await s.LoadConflictFileAsync("f.txt"), Is.Null);
            }
            catch (OperationCanceledException ex)
            {
                thrown = ex;
            }
            Assert.That(thrown, Is.Null);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task LoadFile_Scene_IsFlagged()
        {
            var (_, s) = await MergedAsync(ConflictScenarios.Scene);

            var f = (await s.LoadConflictFileAsync("Assets/Level.unity"))!;

            Assert.That(f.IsSceneOrPrefab, Is.True);
        }

        [Test]
        public async Task LoadFile_ProjectInRepoSubfolder_UsesRepoPaths()
        {
            var repo = await _sb.InitRepoAsync("repo");
            await ConflictScenarios.MergeAsync(repo, ConflictScenarios.Text("Game/Assets/a.txt", "x\n", "y\n", "z\n"));
            var s = _sb.CreateSession(Path.Combine(repo.WorkingDirectory, "Game"), new FakeEditorHooks());
            await s.RefreshAsync();

            Assert.That(s.Snapshot.ConflictedPaths, Is.EqualTo(new[] { "Game/Assets/a.txt" }));
            var f = (await s.LoadConflictFileAsync("Game/Assets/a.txt"))!;
            Assert.That(f.Places.Count, Is.EqualTo(1));
            Assert.That(File.Exists(f.FullPath), Is.True);
        }

        [Test]
        public async Task LoadFile_Diff3_HasBase()
        {
            var repo = await _sb.InitRepoAsync("repo");
            await repo.Config.SetAsync("merge.conflictStyle", "diff3");
            await ConflictScenarios.MergeAsync(repo, ConflictScenarios.TwoPlaces);
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();

            var f = (await s.LoadConflictFileAsync("f.txt"))!;

            Assert.That(f.Places[0].Base, Is.EqualTo("two\n"));
        }

        [Test]
        public async Task Loads_ReturnNullWhenNotARepo()
        {
            var s = _sb.CreateSession(_sb.MakeDir("plain"), new FakeEditorHooks());
            await s.RefreshAsync();

            Assert.That(await s.LoadConflictOverviewAsync(), Is.Null);
            Assert.That(await s.LoadConflictFileAsync("f.txt"), Is.Null);
        }
    }
}
