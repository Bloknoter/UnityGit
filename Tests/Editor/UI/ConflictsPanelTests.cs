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

        Task MergeAsync(params ConflictFile[] files) => MergeInStyleAsync(diff3: false, files);

        // The same, with merge.conflictStyle=diff3: the markers hold the base too.
        Task Diff3MergeAsync(ConflictFile file) => MergeInStyleAsync(diff3: true, file);

        async Task MergeInStyleAsync(bool diff3, params ConflictFile[] files)
        {
            _repo = await _sb.InitRepoAsync("repo");
            if (diff3) await _repo.Config.SetAsync("merge.conflictStyle", "diff3");
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
        static string[] Numbers(VisualElement root) => root.Query<Label>(className: "unitygit-conflict-line-number").ToList().Select(l => l.text).ToArray();
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
                WindowTab.Authors, WindowTab.More, WindowTab.Updates,
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

        // Conflicts joins the nav ahead of Branches: the selection stays on the Branches row.
        [Test] public async Task Nav_ConflictsAppear_KeepsSelectedTab()
        {
            _repo = await _sb.InitRepoAsync("repo");
            _s = _sb.CreateSession(_repo.WorkingDirectory, _hooks);
            await _s.RefreshAsync();
            var v = new GitWindowView(_s);
            using (var window = UnityGitTestWindow.Open(v)) // a live nav: its rows are made and refreshed as in the window
            {
                v.SelectTab(WindowTab.Branches);
                window.ResolveStylesAndLayout();

                await ConflictScenarios.MergeAsync(_repo, ConflictScenarios.TwoPlaces);
                await _s.RefreshAsync();
                v.Render();
                window.ResolveStylesAndLayout();

                Assert.That(v.SelectedTab, Is.EqualTo(WindowTab.Branches));
                Assert.That(v.VisibleTabs, Has.Member(WindowTab.Conflicts), "setup");
                Assert.That(v.Q<ListView>(UiNames.Nav).selectedIndex, Is.EqualTo(v.VisibleTabs.ToList().IndexOf(WindowTab.Branches)));
            }
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

        [Test] public async Task Layout_FilesBesideFile()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);

            var p = await PanelAsync();

            var split = p.Q<TwoPaneSplitView>(UiNames.ConflictsSplit);
            Assert.That(split, Is.Not.Null);
            Assert.That(split.orientation, Is.EqualTo(TwoPaneSplitViewOrientation.Horizontal), "the file is beside the list");
            Assert.That(split.fixedPaneIndex, Is.EqualTo(0), "the list keeps its width; the file takes the rest");
            var panes = split.Children().ToList();
            Assert.That(panes, Has.Count.EqualTo(2));
            Assert.That(panes[0].Q<ListView>(UiNames.ConflictsFiles), Is.Not.Null);
            Assert.That(panes[1].Q(UiNames.ConflictPlaces), Is.Not.Null);
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

        [Test] public async Task Picks_SurviveRefusal()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            p.Pick(ConflictPick.Theirs);
            _s.IsPlayMode = true;

            var o = await p.ApplyAsync();

            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Refused), o.Message);
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

            Assert.That(ButtonTexts(p.Q(UiNames.ConflictWholeFile)), Is.EqualTo(new[] { "Use main", $"✓ Use {p.File!.TheirsName}" }));
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
            Assert.That(IsDisplayed(LabelNamed(p, UiNames.ConflictFileNote)), Is.False, "no \"No conflict places left\" note: nothing was resolved by hand");
        }

        // Markers removed by hand: Use A/B would drop the hand merge, so only Mark resolved (and Start over) are offered.
        [Test] public async Task HandResolvedFile_OffersNoWholeSide()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            File.WriteAllText(FilePath("f.txt"), "fixed\n");

            var p = await PanelAsync();

            Assert.That(p.File!.Places, Is.Empty, "setup: the markers were removed by hand");
            Assert.That(ButtonTexts(p.Q(UiNames.ConflictWholeFile)), Is.Empty);
            var note = LabelNamed(p, UiNames.ConflictFileNote);
            Assert.That(IsDisplayed(note), Is.True);
            Assert.That(note.text, Is.EqualTo("No conflict places left — mark it resolved if it looks right."));
            Assert.That(ButtonNamed(p, UiNames.ConflictMarkResolved).enabledSelf, Is.True);
            Assert.That(ButtonNamed(p, UiNames.ConflictStartOver).enabledSelf, Is.True);
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
            var oursNumbers = Numbers(place.Q(className: "unitygit-conflict-side--ours")); // under the <<<<<<< on line 1
            Assert.That((oursNumbers.Length, oursNumbers.First(), oursNumbers.Last()), Is.EqualTo((200, "2", "201")));
        }

        // A text-serialized mesh can hold a single line of hundreds of KB: no label gets it whole.
        [Test] public async Task Preview_CapsVeryLongLine()
        {
            await MergeAsync(ConflictScenarios.Text("long.txt", "x\n", new string('a', 200000) + "\n", "y\n"));

            var p = await PanelAsync();

            var texts = Place(p, 0).Query<Label>().ToList().Select(l => l.text ?? string.Empty).ToList();
            Assert.That(texts, Has.Member(new string('a', 1000) + "…"));
            Assert.That(p.Query<Label>().ToList().Max(l => (l.text ?? string.Empty).Length), Is.LessThanOrEqualTo(21000));
        }

        // A side that removed the lines says so instead of showing an empty block.
        [Test] public async Task Place_EmptySide_SaysNoLines()
        {
            await MergeAsync(ConflictScenarios.Text("e.txt", "a\nb\nc\n", "a\nc\n", "a\nB\nc\n"));

            var p = await PanelAsync();

            Assert.That(p.File!.Places[0].Ours, Is.Empty, "setup: main removed the line");
            var place = Place(p, 0);
            Assert.That(place.Q(className: "unitygit-conflict-side--ours").Query<Label>().ToList().Select(l => l.text), Has.Member("(no lines)"));
            Assert.That(place.Q(className: "unitygit-conflict-side--theirs").Query<Label>().ToList().Select(l => l.text), Has.No.Member("(no lines)"));
            Assert.That(Numbers(place.Q(className: "unitygit-conflict-side--ours")), Is.Empty);
        }

        // Rebase labels carry commit subjects, which may hold "<b>": side names show as typed.
        [Test] public async Task SideNames_ShowAsTyped()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();

            p.Pick(ConflictPick.Ours);

            Assert.That(LabelNamed(p, UiNames.ConflictFileType).enableRichText, Is.False);
            var chip = Place(p, 0).Q<Label>(className: EditorUiClasses.Chip);
            Assert.That((chip.text, chip.enableRichText), Is.EqualTo(("Use main", false)));
        }

        [Test] public async Task PickedOption_IsFilledAndTicked()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            p.Pick(ConflictPick.Theirs);
            p.SelectPlace(0);

            var buttons = Place(p, 0).Q(UiNames.ConflictPlaceButtons).Query<Button>().ToList();
            Assert.That(buttons.Select(b => b.text), Is.EqualTo(new[] { "Use main", "✓ Use feature", "Use main then feature", "Use feature then main", "Clear" }));
            Assert.That(buttons.Select(b => b.ClassListContains(ConflictsPanel.PickedClass)), Is.EqualTo(new[] { false, true, false, false, false }));
        }

        [Test] public async Task PickedOption_MarksTheSidesItUses()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            string Mark(string side)
            {
                var block = Place(p, 0).Q(className: "unitygit-conflict-side--" + side);
                return block.ClassListContains("unitygit-conflict-side--used") ? "used" : block.ClassListContains("unitygit-conflict-side--unused") ? "unused" : "-";
            }
            Assert.That((Mark("ours"), Mark("theirs")), Is.EqualTo(("-", "-")));

            p.Pick(ConflictPick.Theirs);
            p.SelectPlace(0);
            Assert.That((Mark("ours"), Mark("theirs")), Is.EqualTo(("unused", "used")));

            p.Pick(ConflictPick.OursThenTheirs);
            p.SelectPlace(0);
            Assert.That((Mark("ours"), Mark("theirs")), Is.EqualTo(("used", "used")));
        }

        // The base is never used, so a pick fades it with the side it leaves out.
        [Test] public async Task PickedOption_FadesTheBase()
        {
            await Diff3MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            (bool Used, bool Unused) Base()
            {
                var block = Place(p, 0).Q(className: "unitygit-conflict-side--base");
                return (block.ClassListContains("unitygit-conflict-side--used"), block.ClassListContains("unitygit-conflict-side--unused"));
            }
            Assert.That(Base(), Is.EqualTo((false, false)));

            p.Pick(ConflictPick.OursThenTheirs);
            p.SelectPlace(0);
            Assert.That(Base(), Is.EqualTo((false, true)));
        }

        [Test] public async Task WholeFileButton_PickedWhenEveryPlaceHasThatPick()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();

            p.PickAll(ConflictPick.Ours);
            Assert.That(ButtonTexts(p.Q(UiNames.ConflictWholeFile)), Is.EqualTo(new[] { "✓ Use main", "Use feature", "Use main then feature", "Use feature then main" }));

            p.SelectPlace(1);
            p.Pick(ConflictPick.Theirs);
            Assert.That(ButtonTexts(p.Q(UiNames.ConflictWholeFile)), Is.EqualTo(Options));
        }

        [Test] public async Task Place_HasNoTitle_KeepsTheChip()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();

            var texts = Place(p, 1).Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(texts, Has.None.StartsWith("Place "));
            Assert.That(Place(p, 1).Q<Label>(className: EditorUiClasses.Chip).text, Is.EqualTo("Not chosen"));
        }

        // f.txt on disk: 1 one, 2 <<<<<<<, 3 TWO main, 4 =======, 5 TWO feature, 6 >>>>>>>, 7–11 three…seven,
        // 12 <<<<<<<, 13 EIGHT main, 14 =======, 15 EIGHT feature, 16 >>>>>>>, 17 nine. Place 0 is selected; place 1 shows no lines.
        [Test] public async Task LineNumbers_FollowTheFileOnDisk()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            Assert.That(Numbers(p.Q(UiNames.ConflictPlaces)), Is.EqualTo(new[] { "1", "3", "5", "7", "8", "9", "10", "11", "17" }));
        }

        [Test] public async Task LineNumbers_PreviewNumbersTheSidesItUses()
        {
            await MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            p.Pick(ConflictPick.TheirsThenOurs);
            Assert.That(Numbers(Place(p, 0)), Is.EqualTo(new[] { "5", "3" }));
            Assert.That(Place(p, 0).Query<Label>(className: "unitygit-conflict-preview").ToList().Select(l => l.text), Is.EqualTo(new[] { "TWO feature", "TWO main" }));
        }

        // A blank line in a side keeps its row a line tall: the number stays in the row's flow, beside an empty text (an
        // absolutely placed number would leave the row 0px tall). Batch mode runs no layout, so the flow is what is checked.
        [Test] public async Task LineNumbers_BlankLineKeepsItsRow()
        {
            await MergeAsync(ConflictScenarios.Text("b.txt", "x\n", "a\n\nb\n", "y\n"));
            var p = await PanelAsync();

            var rows = Place(p, 0).Q(className: "unitygit-conflict-side--ours").Query(className: "unitygit-conflict-row").ToList();
            var numbers = rows.Select(r => r.Q<Label>(className: "unitygit-conflict-line-number")).ToList();
            Assert.That(rows.Select(r => r.Q<Label>(className: "unitygit-conflict-text").text), Is.EqualTo(new[] { "a", "", "b" }));
            Assert.That(numbers.Select(n => n.text), Is.EqualTo(new[] { "2", "3", "4" }));
            Assert.That(numbers.Select(n => n.style.position.value), Is.All.EqualTo(Position.Relative));
        }

        [Test] public async Task LineNumbers_Diff3_NumbersTheBase()
        {
            await Diff3MergeAsync(ConflictScenarios.TwoPlaces);
            var p = await PanelAsync();
            Assert.That(Numbers(Place(p, 0)), Is.EqualTo(new[] { "3", "5", "7" }));
        }

        // c.txt: 1 <<<<<<<, 2 L1 main, 3 =======, 4 L1 feature, 5 >>>>>>>, 6–23 L2…L19, 24 <<<<<<< …
        [Test] public async Task LineNumbers_SkipCollapsedRuns()
        {
            string L(int from, int to) => string.Concat(Enumerable.Range(from, to - from + 1).Select(i => $"L{i}\n"));
            await MergeAsync(ConflictScenarios.Text("c.txt", L(1, 20), "L1 main\n" + L(2, 19) + "L20 main\n", "L1 feature\n" + L(2, 19) + "L20 feature\n"));
            var p = await PanelAsync();
            Assert.That(Numbers(p.Q(UiNames.ConflictPlaces)), Is.EqualTo(new[] { "2", "4", "6", "7", "8", "21", "22", "23" }));
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
