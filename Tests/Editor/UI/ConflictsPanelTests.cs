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
using UnityGit.UI.Theme;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ConflictsPanelTests
    {
        const string RebaseNote = "During a rebase, HEAD is the branch you're rebasing onto and the other side is your own commit.";
        const string SceneWarning = "Scenes and prefabs can break when places are picked one by one. Check the file opens in Unity before you finish.";
        static readonly string[] Options = { "Use main", "Use feature", "Use main then feature", "Use feature then main" };

        GitSandbox _sb = null!;
        FakeEditorHooks _hooks = null!;
        GitRepository _repo = null!;
        GitSession _s = null!;

        [SetUp] public async Task SetUp() { _sb = await GitSandbox.CreateAsync(); _hooks = new FakeEditorHooks(); }

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        async Task MergeAsync(params ConflictFile[] files)
        {
            _repo = await _sb.InitRepoAsync("repo");
            await ConflictScenarios.MergeAsync(_repo, files);
            _s = _sb.CreateSession(_repo.WorkingDirectory, _hooks);
            await _s.RefreshAsync();
        }

        // A pull through the session that stops on a conflict in README.md: a merge, or a rebase with pull.rebase=true.
        async Task PullConflictAsync(bool rebase)
        {
            var trio = await _sb.CreateTrioAsync();
            _repo = trio.Local;
            if (rebase) await trio.Local.Config.SetAsync("pull.rebase", "true");
            await trio.PrepareConflictAsync();
            _s = _sb.CreateSession(trio.Local.WorkingDirectory, _hooks);
            await _s.RefreshAsync();
            var pull = await _s.PullAsync();
            Assert.That(pull.ConflictedPaths, Is.EqualTo(new[] { "README.md" }), "setup: " + pull.Message);
        }

        // The panel as the window makes it, once the reload its first render started has finished.
        async Task<ConflictsPanel> PanelAsync()
        {
            var p = new ConflictsPanel(_s);
            await p.ReloadAsync();
            return p;
        }

        // Batch mode runs no layout pass, so resolvedStyle is unavailable: an element is displayed unless it or an
        // ancestor has display: none.
        static bool IsDisplayed(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e.style.display == DisplayStyle.None) return false;
            return true;
        }

        static string[] ButtonTexts(VisualElement? root) => root!.Query<Button>().ToList().Select(b => b.text).ToArray();
        static VisualElement Place(VisualElement p, int index) => p.Q(UiNames.ConflictPlacePrefix + index);
        static Label LabelNamed(VisualElement p, string name) => p.Q<Label>(name);
        static Button ButtonNamed(VisualElement p, string name) => p.Q<Button>(name);
        string FilePath(string path) => Path.Combine(_repo.WorkingDirectory, path);

        [Test] public async Task Nav_ShowsConflictsOnlyWhileConflicted()
        {
            _repo = await _sb.InitRepoAsync("repo");
            _s = _sb.CreateSession(_repo.WorkingDirectory, _hooks);
            await _s.RefreshAsync();
            var v = new GitWindowView(_s);
            Assert.That(v.VisibleTabs, Has.No.Member(WindowTab.Conflicts));

            await ConflictScenarios.MergeAsync(_repo, ConflictScenarios.TwoPlaces);
            await _s.RefreshAsync();
            v.Render();

            Assert.That(v.VisibleTabs, Is.EqualTo(new[]
            {
                WindowTab.Overview, WindowTab.Changes, WindowTab.Conflicts, WindowTab.Branches, WindowTab.History,
                WindowTab.Authors, WindowTab.More,
            }));
            Assert.That(v.TabBadge(WindowTab.Conflicts), Is.EqualTo("1"));
            Assert.That(v.TabBadgeTone(WindowTab.Conflicts), Is.EqualTo(EditorUiTone.Error));
            Assert.That(v.TabBadgeTone(WindowTab.Changes), Is.EqualTo(EditorUiTone.Accent));
        }

        [Test] public async Task Nav_ConflictsHidden_SelectsChanges()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var v = new GitWindowView(_s);
            v.SelectTab(WindowTab.Conflicts);
            Assert.That(v.SelectedTab, Is.EqualTo(WindowTab.Conflicts), "setup: the tab is shown while merging");
            await v.Q<ConflictsPanel>()!.ReloadAsync(); // no read of the panel's still running when the merge is aborted

            var abort = await _repo.RunRawAsync("merge", "--abort");
            Assert.That(abort.Succeeded, Is.True, "setup: " + abort.StandardError);
            await _s.RefreshAsync();
            v.Render();

            Assert.That(v.SelectedTab, Is.EqualTo(WindowTab.Changes));
            Assert.That(v.VisibleTabs, Has.No.Member(WindowTab.Conflicts));
        }

        [Test] public async Task Header_Merge()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);

            var p = await PanelAsync();

            Assert.That(LabelNamed(p, UiNames.ConflictsTitle).text, Is.EqualTo("Merging"));
            Assert.That(LabelNamed(p, UiNames.ConflictsSummary).text, Is.EqualTo("1 file left"));
            var finish = ButtonNamed(p, UiNames.ConflictsContinue);
            Assert.That((finish.text, finish.enabledSelf), Is.EqualTo(("Finish merge", false)));
            var abort = ButtonNamed(p, UiNames.ConflictsAbort);
            Assert.That((abort.text, abort.enabledSelf), Is.EqualTo(("Abort merge", true)));
            Assert.That(IsDisplayed(LabelNamed(p, UiNames.ConflictsNote)), Is.False);
        }

        [Test] public async Task Header_Rebase()
        {
            await PullConflictAsync(rebase: true);

            var p = await PanelAsync();

            Assert.That(LabelNamed(p, UiNames.ConflictsTitle).text, Is.EqualTo("Rebasing"));
            Assert.That(ButtonNamed(p, UiNames.ConflictsContinue).text, Is.EqualTo("Continue rebase"));
            Assert.That(ButtonNamed(p, UiNames.ConflictsAbort).text, Is.EqualTo("Abort rebase"));
            var note = LabelNamed(p, UiNames.ConflictsNote);
            Assert.That(note.text, Is.EqualTo(RebaseNote));
            Assert.That(IsDisplayed(note), Is.True);
        }

        [Test] public async Task Header_AllResolved_FinishEnabled()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            var view = (await _s.LoadConflictFileAsync("f.txt"))!;
            var o = await _s.ApplyPicksAsync(view, new Dictionary<int, ConflictPick> { [0] = ConflictPick.Ours, [1] = ConflictPick.Ours });
            Assert.That(o.Succeeded, Is.True, "setup: " + o.Message);

            p.Render();

            Assert.That(LabelNamed(p, UiNames.ConflictsSummary).text, Is.EqualTo("All conflicts resolved"));
            var finish = ButtonNamed(p, UiNames.ConflictsContinue);
            Assert.That(finish.enabledSelf, Is.True, finish.tooltip);
            Assert.That(finish.ClassListContains(EditorUiClasses.ButtonPrimary), Is.True);
            await p.ReloadAsync(); // the reload the new snapshot started
        }

        [Test] public async Task Files_ListedWithChips()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces, ConflictScenarios.Binary, ConflictScenarios.DeletedByThem);

            var p = await PanelAsync();

            Assert.That(p.Files, Is.EqualTo(new[] { "f.txt", "gone.txt", "img.bin" }));
            Assert.That(p.SelectedFile, Is.EqualTo("f.txt"));
        }

        [Test] public void Chip_AndTypeTexts()
        {
            Assert.That(ConflictsPanel.Chip(new ConflictFileInfo { PlaceCount = 2 }), Is.EqualTo("2 places"));
            Assert.That(ConflictsPanel.Chip(new ConflictFileInfo { PlaceCount = 1 }), Is.EqualTo("1 place"));
            Assert.That(ConflictsPanel.Chip(new ConflictFileInfo { IsBinary = true }), Is.EqualTo("binary"));
            Assert.That(ConflictsPanel.Chip(new ConflictFileInfo { Type = ConflictType.DeletedByThem }), Is.EqualTo("no places"));
            Assert.That(ConflictsPanel.TypeBadge(ConflictType.DeletedByThem), Is.EqualTo("deleted by them"));
            Assert.That(ConflictsPanel.TypeText(ConflictType.DeletedByThem, "main", "feature"), Is.EqualTo("Changed by main, deleted by feature"));
        }

        [Test] public async Task SelectFile_ShowsPlacesAndSelectsTheFirst()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();

            await p.SelectFileAsync("f.txt");

            Assert.That(p.File!.Places.Count, Is.EqualTo(2));
            Assert.That(p.SelectedPlace, Is.EqualTo(0));
            Assert.That(Place(p, 0), Is.Not.Null);
            Assert.That(Place(p, 1), Is.Not.Null);
            Assert.That(LabelNamed(p, UiNames.ConflictFileTitle).text, Is.EqualTo("f.txt"));
            Assert.That(LabelNamed(p, UiNames.ConflictFileType).text, Is.EqualTo("Changed on both sides"));
            Assert.That(ButtonTexts(p.Q(UiNames.ConflictWholeFile)), Is.EqualTo(Options));
            Assert.That(ButtonTexts(Place(p, 0).Q(UiNames.ConflictPlaceButtons)), Is.EqualTo(Options));
            Assert.That(Place(p, 1).Q(UiNames.ConflictPlaceButtons), Is.Null);
        }

        [Test] public async Task Pick_PreviewsAndMovesOn()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            var before = File.ReadAllText(FilePath("f.txt"));

            p.Pick(ConflictPick.Theirs);

            Assert.That(p.PickOf(0), Is.EqualTo(ConflictPick.Theirs));
            Assert.That(p.SelectedPlace, Is.EqualTo(1));
            Assert.That(Place(p, 0).Q<Label>(className: "unitygit-conflict-preview")?.text, Is.EqualTo("TWO feature"));
            Assert.That(File.ReadAllText(FilePath("f.txt")), Is.EqualTo(before));
        }

        [Test] public async Task Pick_LastPlace_WrapsToFirstWithoutChoice()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            p.SelectPlace(1);

            p.Pick(ConflictPick.Ours);

            Assert.That(p.PickOf(1), Is.EqualTo(ConflictPick.Ours));
            Assert.That(p.SelectedPlace, Is.EqualTo(0));
        }

        [Test] public async Task PickAll_ThenApply_ResolvesTheFile()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();

            p.PickAll(ConflictPick.OursThenTheirs);

            Assert.That(p.PickOf(0), Is.EqualTo(ConflictPick.OursThenTheirs));
            Assert.That(p.PickOf(1), Is.EqualTo(ConflictPick.OursThenTheirs));
            Assert.That(ButtonNamed(p, UiNames.ConflictApply).enabledSelf, Is.True);

            var o = await p.ApplyAsync();

            Assert.That(o.Succeeded, Is.True, o.Message);
            Assert.That(p.Files, Is.Empty);
            Assert.That(LabelNamed(p, UiNames.ConflictsSummary).text, Is.EqualTo("All conflicts resolved"));
        }

        [Test] public async Task ClearPick_RemovesTheChoice()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            p.Pick(ConflictPick.Ours);
            p.SelectPlace(0);

            p.ClearPick();

            Assert.That(p.PickOf(0), Is.Null);
            Assert.That(ButtonNamed(p, UiNames.ConflictApply).enabledSelf, Is.False);
        }

        [Test] public async Task Apply_Partial_KeepsTheRemainingPlace()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            p.SelectPlace(1);
            p.Pick(ConflictPick.Theirs);

            var o = await p.ApplyAsync();

            Assert.That(o.Succeeded, Is.True, o.Message);
            Assert.That(p.File!.Places.Count, Is.EqualTo(1));
            Assert.That(p.PickOf(0), Is.Null);
        }

        [Test] public async Task Picks_SurviveReloadWhenFileUnchanged()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            p.Pick(ConflictPick.Theirs);

            await p.ReloadAsync();

            Assert.That(p.PickOf(0), Is.EqualTo(ConflictPick.Theirs));
        }

        [Test] public async Task Picks_ClearedWhenFileChanged()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            p.Pick(ConflictPick.Theirs);
            File.WriteAllText(FilePath("f.txt"), "zero\n" + File.ReadAllText(FilePath("f.txt")));

            await p.ReloadAsync();

            Assert.That(p.File!.Places.Count, Is.EqualTo(2), "setup: the markers are still there");
            Assert.That(p.PickOf(0), Is.Null);
        }

        [Test] public async Task BinaryFile_OffersTwoSides()
        {
            await MergeAsync(ConflictScenarios.Binary);
            var p = await PanelAsync();

            await p.SelectFileAsync("img.bin");

            Assert.That(ButtonTexts(p.Q(UiNames.ConflictWholeFile)), Is.EqualTo(new[] { "Use main", $"Use {p.File!.TheirsName}" }));
            Assert.That(p.Q(UiNames.ConflictPlaces).Query(className: "unitygit-conflict-place").ToList(), Is.Empty);

            p.PickSide(false);

            Assert.That(p.SidePick, Is.False);
            var o = await p.ApplyAsync();
            Assert.That(o.Succeeded, Is.True, o.Message);
            Assert.That(Encoding.GetEncoding("ISO-8859-1").GetString(File.ReadAllBytes(FilePath("img.bin"))), Is.EqualTo("B\0feature"));
        }

        [Test] public async Task DeletedFile_LabelsTheRemoval()
        {
            await MergeAsync(ConflictScenarios.DeletedByThem);
            var p = await PanelAsync();

            await p.SelectFileAsync("gone.txt");

            var theirs = p.File!.TheirsName;
            Assert.That(ButtonTexts(p.Q(UiNames.ConflictWholeFile)), Is.EqualTo(new[] { "Use main", $"Use {theirs} (removes the file)" }));
            Assert.That(LabelNamed(p, UiNames.ConflictFileType).text, Is.EqualTo($"Changed by main, deleted by {theirs}"));
        }

        [Test] public async Task SceneFile_ShowsTheWarning()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces, ConflictScenarios.Scene);
            var p = await PanelAsync();

            await p.SelectFileAsync("Assets/Level.unity");

            var warning = LabelNamed(p, UiNames.ConflictSceneWarning);
            Assert.That(IsDisplayed(warning), Is.True);
            Assert.That(warning.text, Is.EqualTo(SceneWarning));

            await p.SelectFileAsync("f.txt");

            Assert.That(IsDisplayed(LabelNamed(p, UiNames.ConflictSceneWarning)), Is.False);
        }

        [Test] public async Task StartOver_OnlyForFilesChangedOnBothSides()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces, ConflictScenarios.DeletedByThem);
            var p = await PanelAsync();

            await p.SelectFileAsync("f.txt");
            Assert.That(ButtonNamed(p, UiNames.ConflictStartOver).enabledSelf, Is.True);

            await p.SelectFileAsync("gone.txt");
            Assert.That(ButtonNamed(p, UiNames.ConflictStartOver).enabledSelf, Is.False);
        }

        // Review focus 4: a <b> in the file shows as typed, and a 251-line side shows its first 200 lines.
        [Test] public async Task Preview_ShowsTagsLiterallyAndCapsLongSides()
        {
            var ours = "<b>bold</b>\n" + string.Concat(Enumerable.Range(1, 250).Select(i => $"line {i}\n"));
            await MergeAsync(ConflictScenarios.Text("tags.txt", "x\n", ours, "y\n"));
            var p = await PanelAsync();

            await p.SelectFileAsync("tags.txt");

            var place = Place(p, 0);
            var oursText = place.Q(className: "unitygit-conflict-side--ours").Q<Label>(className: "unitygit-conflict-text");
            Assert.That(oursText.enableRichText, Is.False);
            Assert.That(oursText.text, Does.StartWith("<b>bold</b>"));
            Assert.That(place.Query<Label>().ToList().Select(l => l.text), Has.Member("⋯ 51 more lines — open the file to see all"));
        }

        [Test] public async Task Outcome_ResolveButton_OpensConflicts()
        {
            await PullConflictAsync(rebase: false);
            var v = new GitWindowView(_s);

            v.Render();

            var resolve = ButtonNamed(v, UiNames.OutcomeResolve);
            Assert.That(IsDisplayed(resolve), Is.True);
            Assert.That(resolve.text, Is.EqualTo("Resolve conflicts"));
            using (UnityGitTestWindow.Open(v))
            {
                resolve.Focus();
                using (var submit = NavigationSubmitEvent.GetPooled()) resolve.SendEvent(submit); // what a click does

                Assert.That(v.SelectedTab, Is.EqualTo(WindowTab.Conflicts));
                await v.Q<ConflictsPanel>()!.ReloadAsync(); // the reload the new panel started
            }
        }
    }
}
