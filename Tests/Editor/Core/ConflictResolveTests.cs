#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ConflictResolveTests
    {
        GitSandbox _sb = null!;
        FakeEditorHooks _hooks = null!;
        FlexGit.GitRepository _repo = null!;
        GitSession _s = null!;

        [SetUp] public async Task SetUp() { _sb = await GitSandbox.CreateAsync(); _hooks = new FakeEditorHooks(); }
        [TearDown] public void TearDown() => _sb.Dispose();

        async Task MergeAsync(params ConflictFile[] files)
        {
            _repo = await _sb.InitRepoAsync("repo");
            await ConflictScenarios.MergeAsync(_repo, files);
            _s = _sb.CreateSession(_repo.WorkingDirectory, _hooks);
            await _s.RefreshAsync();
        }

        // MergeAsync with git-lfs tracking *.bin; ignored where git-lfs isn't installed.
        async Task LfsMergeAsync(params ConflictFile[] files)
        {
            _repo = await _sb.InitRepoAsync("repo");
            if (!_repo.Lfs.IsInstalled) Assert.Ignore("git-lfs not installed");
            await ConflictScenarios.LfsMergeAsync(_repo, files);
            _s = _sb.CreateSession(_repo.WorkingDirectory, _hooks);
            await _s.RefreshAsync();
        }

        async Task<ConflictFileView> Load(string path) => (await _s.LoadConflictFileAsync(path))!;
        string FilePath(string path) => Path.Combine(_repo.WorkingDirectory, path);
        string Read(string path) => File.ReadAllText(FilePath(path));
        string ReadBytes(string path) => Encoding.GetEncoding("ISO-8859-1").GetString(File.ReadAllBytes(FilePath(path)));

        static Dictionary<int, ConflictPick> Picks(params (int, ConflictPick)[] picks)
        {
            var d = new Dictionary<int, ConflictPick>();
            foreach (var (i, p) in picks) d[i] = p;
            return d;
        }

        [Test] public async Task ApplyPicks_AllPlaces_ResolvesAndStages()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);

            var o = await _s.ApplyPicksAsync(await Load("f.txt"), Picks((0, ConflictPick.Ours), (1, ConflictPick.TheirsThenOurs)));

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Resolved f.txt")));
            Assert.That(Read("f.txt"), Is.EqualTo("one\nTWO main\nthree\nfour\nfive\nsix\nseven\nEIGHT feature\nEIGHT main\nnine\n"));
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
            Assert.That(_hooks.Calls, Does.Contain("Begin"));
            Assert.That(_hooks.Calls.IndexOf("Begin"), Is.LessThan(_hooks.Calls.IndexOf("End")));
            Assert.That(_hooks.Calls.IndexOf("End"), Is.LessThan(_hooks.Calls.IndexOf("RefreshAssets")));
        }

        [Test] public async Task ApplyPicks_SomePlaces_LeavesTheRest()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);

            var o = await _s.ApplyPicksAsync(await Load("f.txt"), Picks((1, ConflictPick.Theirs)));

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Applied 1 place in f.txt — 1 left")));
            Assert.That(_s.Snapshot.ConflictedPaths, Is.EqualTo(new[] { "f.txt" }));
            var view = await Load("f.txt");
            Assert.That(view.Places.Count, Is.EqualTo(1));
            Assert.That(view.Places[0].StartLine, Is.EqualTo(2));
        }

        [Test] public async Task ApplyPicks_NoPicks_Refused()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);

            var o = await _s.ApplyPicksAsync(await Load("f.txt"), Picks());

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Pick an option for at least one place")));
            Assert.That(_hooks.Calls, Does.Not.Contain("Begin"));
        }

        [Test] public async Task ApplyPicks_FileChangedSinceShown_Refused()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var view = await Load("f.txt");
            File.WriteAllText(FilePath("f.txt"), "hand edit\n");

            var o = await _s.ApplyPicksAsync(view, Picks((0, ConflictPick.Ours)));

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "f.txt changed since it was shown — review it and apply again")));
            Assert.That(Read("f.txt"), Is.EqualTo("hand edit\n"));
            Assert.That(_hooks.Calls, Does.Not.Contain("RefreshAssets"));
        }

        [Test] public async Task ApplyPicks_CrlfFile_KeepsCrlf()
        {
            await MergeAsync(ConflictScenarios.Text("crlf.txt", "a\r\nb\r\nc\r\n", "a\r\nB main\r\nc\r\n", "a\r\nB feature\r\nc\r\n"));
            var view = await Load("crlf.txt");
            Assert.That(view.Places[0].Ours, Is.EqualTo("B main\r\n"));

            var o = await _s.ApplyPicksAsync(view, Picks((0, ConflictPick.Theirs)));

            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded), o.Message);
            Assert.That(Read("crlf.txt"), Is.EqualTo("a\r\nB feature\r\nc\r\n"));
        }

        [Test] public async Task ApplyPicks_PathWithSpacesAndCyrillic_Resolves()
        {
            const string path = "Assets/Мои сцены/Level 1.txt";
            await MergeAsync(ConflictScenarios.Text(path, "x\n", "y\n", "z\n"));
            Assert.That(_s.Snapshot.ConflictedPaths, Does.Contain(path));

            var o = await _s.ApplyPicksAsync(await Load(path), Picks((0, ConflictPick.Ours)));

            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded), o.Message);
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
        }

        [Test] public async Task UseSide_Binary_TakesTheirs()
        {
            await MergeAsync(ConflictScenarios.Binary);
            var f = await Load("img.bin");

            var o = await _s.UseSideAsync(f, false);

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, $"Used {f.TheirsName} for img.bin")));
            Assert.That(Encoding.GetEncoding("ISO-8859-1").GetString(File.ReadAllBytes(FilePath("img.bin"))), Is.EqualTo("B\0feature"));
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
            Assert.That(_hooks.Calls, Does.Contain("RefreshAssets"));
        }

        [Test] public async Task UseSide_Binary_TakesOurs()
        {
            await MergeAsync(ConflictScenarios.Binary);

            var o = await _s.UseSideAsync(await Load("img.bin"), true);

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Used main for img.bin")));
            Assert.That(ReadBytes("img.bin"), Is.EqualTo("B\0main"));
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
        }

        [Test] public async Task UseSide_DeletedByUs_OursRemovesFile()
        {
            await MergeAsync(ConflictScenarios.DeletedByUs);
            var f = await Load("dropped.txt");
            Assert.That(f.Info.Type, Is.EqualTo(ConflictType.DeletedByUs), "setup");

            var o = await _s.UseSideAsync(f, true);

            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded), o.Message);
            Assert.That(File.Exists(FilePath("dropped.txt")), Is.False);
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
        }

        // Taking a whole side would drop the hand merge and the changes git merged cleanly from the other side (spec §3).
        [Test] public async Task UseSide_HandResolvedText_Refused()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            File.WriteAllText(FilePath("f.txt"), "fixed\n");
            var f = await Load("f.txt");

            var o = await _s.UseSideAsync(f, true);

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Mark it resolved, or start over to pick again")));
            Assert.That(Read("f.txt"), Is.EqualTo("fixed\n"));
            Assert.That(_hooks.Calls, Does.Not.Contain("Begin"));
        }

        // FlexGit reads an LFS pointer conflict as text with one place. UnityGit treats it as binary: a whole side goes
        // through git checkout, whose LFS filter puts back the real file instead of pointer text.
        [Test] public async Task UseSide_LfsPointer_RestoresBinary()
        {
            await LfsMergeAsync(ConflictScenarios.Binary);
            Assert.That(Read("img.bin"), Does.StartWith("version https://git-lfs.github.com/spec/v1\n<<<<<<<"), "setup: pointer text with markers");
            var f = await Load("img.bin");

            var o = await _s.UseSideAsync(f, false);

            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded), o.Message);
            Assert.That(ReadBytes("img.bin"), Is.EqualTo("B\0feature"));
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
        }

        // Picking the pointer's one place would leave pointer text in the working tree, so it is refused like any file
        // shown without places.
        [Test] public async Task ApplyPicks_LfsPointer_Refused()
        {
            await LfsMergeAsync(ConflictScenarios.Binary);
            var before = Read("img.bin");

            var o = await _s.ApplyPicksAsync(await Load("img.bin"), Picks((0, ConflictPick.Ours)));

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Pick an option for at least one place")));
            Assert.That(Read("img.bin"), Is.EqualTo(before));
        }

        [Test] public async Task UseSide_DeletedByThem_TheirsDeletesFile()
        {
            await MergeAsync(ConflictScenarios.DeletedByThem);

            var o = await _s.UseSideAsync(await Load("gone.txt"), false);

            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded), o.Message);
            Assert.That(File.Exists(FilePath("gone.txt")), Is.False);
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
        }

        [Test] public async Task UseSide_TextWithPlaces_Refused()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);

            var o = await _s.UseSideAsync(await Load("f.txt"), true);

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Pick options for the places instead")));
            Assert.That(_hooks.Calls, Does.Not.Contain("Begin"));
        }

        [Test] public async Task MarkResolved_WithMarkers_Refused()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);

            var o = await _s.MarkResolvedAsync("f.txt");

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "f.txt still has conflict markers")));
        }

        [Test] public async Task MarkResolved_AfterHandEdit_Resolves()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            File.WriteAllText(FilePath("f.txt"), "fixed\n");

            var o = await _s.MarkResolvedAsync("f.txt");

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Marked f.txt as resolved")));
            Assert.That(_s.Snapshot.ConflictedPaths, Is.Empty);
        }

        [Test] public async Task RestoreConflict_PutsMarkersBack()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            await _s.ApplyPicksAsync(await Load("f.txt"), Picks((0, ConflictPick.Ours)));

            var o = await _s.RestoreConflictAsync("f.txt");

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Put back the conflicts in f.txt")));
            Assert.That((await Load("f.txt")).Places.Count, Is.EqualTo(2));
            Assert.That(_hooks.Calls, Does.Contain("Confirm:Start over?"));
        }

        [Test] public async Task RestoreConflict_Declined_ChangesNothing()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            await _s.ApplyPicksAsync(await Load("f.txt"), Picks((0, ConflictPick.Ours)));
            _hooks.ConfirmResult = false;

            var o = await _s.RestoreConflictAsync("f.txt");

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Cancelled, "Start over cancelled")));
            Assert.That((await Load("f.txt")).Places.Count, Is.EqualTo(1));
        }

        [Test] public async Task RestoreConflict_DeletedByThem_Refused()
        {
            await MergeAsync(ConflictScenarios.DeletedByThem);

            var o = await _s.RestoreConflictAsync("gone.txt");

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Only files changed on both sides can start over")));
            Assert.That(_hooks.Calls, Does.Not.Contain("Confirm:Start over?"));
        }

        [Test] public async Task Resolve_NotConflicted_Refused()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces, ConflictScenarios.Binary);
            await _s.ApplyPicksAsync(await Load("f.txt"), Picks((0, ConflictPick.Ours), (1, ConflictPick.Ours)));

            var o = await _s.MarkResolvedAsync("f.txt");

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "f.txt is no longer conflicted")));
        }

        [Test] public async Task Resolve_InPlayMode_Refused()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var view = await Load("f.txt");
            _s.IsPlayMode = true;

            var o = await _s.ApplyPicksAsync(view, Picks((0, ConflictPick.Ours)));

            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Not available in Play Mode")));
        }
    }
}
