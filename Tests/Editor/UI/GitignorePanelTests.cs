#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class GitignorePanelTests
    {
        const string Original = "# a\nx\n# b\ny\n";

        GitSandbox _sb = null!;
        FakeEditorHooks _hooks = null!;
        readonly List<string> _projects = new List<string>();
        bool _hadMode, _hadAutoSave;
        int _mode;
        bool _autoSave;

        [SetUp] public async Task SetUp()
        {
            _sb = await GitSandbox.CreateAsync();
            _hooks = new FakeEditorHooks();
            _hadMode = EditorPrefs.HasKey(GitignorePanel.ModePref);
            _mode = EditorPrefs.GetInt(GitignorePanel.ModePref, 0);
            _hadAutoSave = EditorPrefs.HasKey(GitignorePanel.AutoSavePref);
            _autoSave = EditorPrefs.GetBool(GitignorePanel.AutoSavePref, true);
            EditorPrefs.SetInt(GitignorePanel.ModePref, 0);
            EditorPrefs.SetBool(GitignorePanel.AutoSavePref, true);
        }

        [TearDown] public void TearDown()
        {
            if (_hadMode) EditorPrefs.SetInt(GitignorePanel.ModePref, _mode);
            else EditorPrefs.DeleteKey(GitignorePanel.ModePref);
            if (_hadAutoSave) EditorPrefs.SetBool(GitignorePanel.AutoSavePref, _autoSave);
            else EditorPrefs.DeleteKey(GitignorePanel.AutoSavePref);
            foreach (var project in _projects)
                foreach (var key in new[] { GitignorePanel.FileKey, GitignorePanel.DraftPathKey, GitignorePanel.DraftTextKey, GitignorePanel.DraftBaseKey })
                    SessionState.EraseString(key + project);
            _projects.Clear();
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        // A repo with a commit, and .gitignore committed with this text unless null; a session that has read it.
        async Task<(GitRepository repo, GitSession s)> RepoAsync(string? gitignore = Original)
        {
            var repo = await _sb.InitRepoAsync("r");
            await GitSandbox.CommitFileAsync(repo, "base.txt", "base\n", "initial");
            if (gitignore != null) await GitSandbox.CommitFileAsync(repo, ".gitignore", gitignore, "gitignore");
            return (repo, await SessionAsync(repo));
        }

        async Task<GitSession> SessionAsync(GitRepository repo)
        {
            var s = _sb.CreateSession(repo.WorkingDirectory, _hooks);
            await s.RefreshAsync();
            _projects.Add(repo.WorkingDirectory);
            return s;
        }

        static async Task<GitignorePanel> PanelAsync(GitSession s)
        {
            var p = new GitignorePanel(s);
            await p.Loaded;
            return p;
        }

        static string GitignorePath(GitRepository repo) => Path.Combine(repo.WorkingDirectory, ".gitignore");

        static string Disk(GitRepository repo) => File.ReadAllText(GitignorePath(repo));

        // Batch mode runs no layout pass: an element is displayed unless it or an ancestor has display: none.
        static bool IsDisplayed(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e.style.display == DisplayStyle.None) return false;
            return true;
        }

        // ---- the tab ----

        [Test]
        public async Task Tab_SitsAfterAuthors()
        {
            var (_, s) = await RepoAsync();

            var v = new GitWindowView(s);

            Assert.That(v.VisibleTabs, Is.EqualTo(new[]
            {
                WindowTab.Overview, WindowTab.Changes, WindowTab.Branches, WindowTab.History, WindowTab.Authors,
                WindowTab.Gitignore, WindowTab.More, WindowTab.Updates,
            }));
        }

        // ---- loading and modes ----

        [Test]
        public async Task Panel_LoadsDefaultFile()
        {
            var (_, s) = await RepoAsync("# a\nx\ny\n");

            var p = await PanelAsync(s);

            Assert.That(p.FilePath, Is.EqualTo(".gitignore"));
            Assert.That(p.Text, Is.EqualTo("# a\nx\ny\n"));
            Assert.That(p.HasUnsavedChanges, Is.False);
            Assert.That(p.PlainView.GutterText, Is.EqualTo("1\n2\n3"));
        }

        [Test]
        public async Task Panel_Mode_PersistsInEditorPrefs()
        {
            var (_, s) = await RepoAsync();
            var p = await PanelAsync(s);

            p.Mode = GitignoreMode.PlainText;

            Assert.That(EditorPrefs.GetInt(GitignorePanel.ModePref), Is.EqualTo(1));
            Assert.That((await PanelAsync(s)).Mode, Is.EqualTo(GitignoreMode.PlainText));
        }

        // ---- saving ----

        [Test]
        public async Task Panel_AutoSave_WritesAfterFlush()
        {
            var (repo, s) = await RepoAsync();
            var p = await PanelAsync(s);

            p.SetPlainText("x\n");

            Assert.That(Disk(repo), Is.EqualTo(Original), "typing doesn't save at once");
            Assert.That(p.HasUnsavedChanges, Is.True);
            p.FlushPendingSave();
            Assert.That(Disk(repo), Is.EqualTo("x\n"));
            Assert.That(p.HasUnsavedChanges, Is.False);
        }

        [Test]
        public async Task Panel_AutoSaveOff_ShowsSave()
        {
            var (repo, s) = await RepoAsync();
            var p = await PanelAsync(s);
            var save = p.Q<Button>(UiNames.GitignoreSave);
            Assert.That(IsDisplayed(save), Is.False, "nothing to save");

            p.AutoSave = false;
            p.SetPlainText("x\n");
            p.FlushPendingSave();

            Assert.That(Disk(repo), Is.EqualTo(Original), "auto-save is off");
            Assert.That(IsDisplayed(save), Is.True);
            Assert.That(p.Save(), Is.True);
            Assert.That(Disk(repo), Is.EqualTo("x\n"));
            Assert.That(IsDisplayed(save), Is.False);
        }

        [Test]
        public async Task Panel_SaveWhileBusy_Waits()
        {
            var (repo, s) = await RepoAsync();
            var p = await PanelAsync(s);
            p.SetPlainText("x\n");
            GitSandbox.WriteFile(repo, "c.txt", "c\n");
            await s.RefreshAsync();
            bool? savedDuringCommit = null;
            _hooks.OnSaveAssets = () => savedDuringCommit = p.Save(); // Commit saves assets while it's busy

            var commit = await s.CommitAsync("c", new[] { "c.txt" }, s.Snapshot.Changes.Select(c => c.Path).ToList());

            Assert.That(commit.Kind, Is.EqualTo(OutcomeKind.Succeeded), "setup: " + commit.Message);
            Assert.That(savedDuringCommit, Is.False);
            Assert.That(Disk(repo), Is.EqualTo(Original));
            p.Render(); // the window renders when the action ends
            Assert.That(Disk(repo), Is.EqualTo("x\n"));
        }

        [Test]
        public async Task Panel_RemoveBlock_KeepsCrlfAndBom()
        {
            var (repo, _) = await RepoAsync(null);
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            File.WriteAllBytes(GitignorePath(repo), bom.Concat(System.Text.Encoding.UTF8.GetBytes("# a\r\nx\r\n# b\r\ny\r\n")).ToArray());
            var s = await SessionAsync(repo);
            var p = await PanelAsync(s);

            p.ReplaceText(GitignoreDocument.Parse(p.Text).Remove(1).ToText(), true);

            Assert.That(File.ReadAllBytes(GitignorePath(repo)),
                Is.EqualTo(bom.Concat(System.Text.Encoding.UTF8.GetBytes("# a\r\n\r\n\r\n# b\r\ny\r\n")).ToArray()));
        }

        // ---- outside changes ----

        [Test]
        public async Task Panel_DiskChanged_NoEdits_Reloads()
        {
            var (repo, s) = await RepoAsync();
            var p = await PanelAsync(s);

            File.WriteAllText(GitignorePath(repo), "theirs, longer\n");
            p.CheckDisk();

            Assert.That(p.Text, Is.EqualTo("theirs, longer\n"));
            Assert.That(p.HasUnsavedChanges, Is.False);
            Assert.That(p.NoteText, Is.Null);
        }

        [Test]
        public async Task Panel_DiskChangedWithUnsavedEdits_Warns()
        {
            var (repo, s) = await RepoAsync();
            var p = await PanelAsync(s);
            p.AutoSave = false;
            p.SetPlainText("mine\n");

            File.WriteAllText(GitignorePath(repo), "theirs\n");
            p.CheckDisk();

            Assert.That(p.NoteText, Is.EqualTo("This .gitignore changed on disk"));
            Assert.That(p.Text, Is.EqualTo("mine\n"));
            Assert.That(Disk(repo), Is.EqualTo("theirs\n"));

            p.Reload();
            Assert.That(p.Text, Is.EqualTo("theirs\n"));
            Assert.That((p.HasUnsavedChanges, p.NoteText), Is.EqualTo((false, (string?)null)));
        }

        // ---- read-only and empty states ----

        [Test]
        public async Task Panel_Conflicted_IsReadOnly()
        {
            var repo = await _sb.InitRepoAsync("r");
            await ConflictScenarios.MergeAsync(repo, ConflictScenarios.Text(".gitignore", "a\n", "b\n", "c\n"));
            var s = await SessionAsync(repo);

            var p = await PanelAsync(s);

            Assert.That(p.IsReadOnly, Is.True);
            Assert.That(p.NoteText, Is.EqualTo("Resolve this file in the Conflicts tab first"));
        }

        [Test]
        public async Task Panel_NotUtf8_IsReadOnly()
        {
            var (repo, _) = await RepoAsync(null);
            File.WriteAllBytes(GitignorePath(repo), new byte[] { 0x61, 0xFF, 0x0A });
            var s = await SessionAsync(repo);

            var p = await PanelAsync(s);

            Assert.That(p.IsReadOnly, Is.True);
            Assert.That(p.NoteText, Is.EqualTo("This file isn't UTF-8 text, so it can't be edited here"));
        }

        [Test]
        public async Task Panel_NoGitignore_OffersCreate()
        {
            var (repo, s) = await RepoAsync(null);
            var p = await PanelAsync(s);
            Assert.That(p.NoteText, Is.EqualTo("No .gitignore in this repository"));
            Assert.That(p.FilePath, Is.Null);

            await p.CreateAsync(true);

            Assert.That(Disk(repo), Is.EqualTo(GitignoreTemplate.Text));
            Assert.That(p.FilePath, Is.EqualTo(".gitignore"));
            Assert.That(p.Text, Is.EqualTo(GitignoreTemplate.Text));
            Assert.That(p.NoteText, Is.Null);
        }

        [Test]
        public async Task Panel_Create_RefusesToOverwrite()
        {
            var (repo, s) = await RepoAsync(null);
            var p = await PanelAsync(s);
            File.WriteAllText(GitignorePath(repo), "mine\n"); // made elsewhere after the panel looked

            await p.CreateAsync(true);

            Assert.That(Disk(repo), Is.EqualTo("mine\n"));
            Assert.That((p.FilePath, p.Text), Is.EqualTo((".gitignore", "mine\n")));
        }

        // ---- drafts ----

        [Test]
        public async Task Panel_Draft_SurvivesNewPanel()
        {
            var (_, s) = await RepoAsync();
            var p = await PanelAsync(s);
            p.AutoSave = false;
            p.SetPlainText("draft\n");

            var again = await PanelAsync(s);

            Assert.That(again.Text, Is.EqualTo("draft\n"));
            Assert.That(again.HasUnsavedChanges, Is.True);
        }

        // ---- switching file and the window's unsaved state ----

        async Task<(GitRepository repo, GitSession s, GitignorePanel p)> TwoFilesAsync(bool autoSave)
        {
            var (repo, s) = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, "a/.gitignore", "A\n", "nested");
            var p = await PanelAsync(s);
            Assert.That((p.FilePath, p.Files.Count), Is.EqualTo((".gitignore", 2)), "setup");
            p.AutoSave = autoSave;
            p.SetPlainText("edited\n");
            return (repo, s, p);
        }

        [Test]
        public async Task Switch_Unsaved_Save_SavesThenSwitches()
        {
            var (repo, _, p) = await TwoFilesAsync(autoSave: false);
            p.AskSwitch = _ => GitignoreSwitchChoice.Save;

            Assert.That(await p.SelectFileAsync("a/.gitignore"), Is.True);

            Assert.That(Disk(repo), Is.EqualTo("edited\n"));
            Assert.That((p.FilePath, p.Text, p.HasUnsavedChanges), Is.EqualTo(("a/.gitignore", "A\n", false)));
        }

        [Test]
        public async Task Switch_Unsaved_DontSave_Discards()
        {
            var (repo, s, p) = await TwoFilesAsync(autoSave: false);
            p.AskSwitch = _ => GitignoreSwitchChoice.DontSave;

            Assert.That(await p.SelectFileAsync("a/.gitignore"), Is.True);

            Assert.That(Disk(repo), Is.EqualTo(Original));
            Assert.That(p.FilePath, Is.EqualTo("a/.gitignore"));
            Assert.That(GitignorePanel.HasDraft(s.ProjectPath), Is.False);
        }

        [Test]
        public async Task Switch_Unsaved_Cancel_Stays()
        {
            var (repo, _, p) = await TwoFilesAsync(autoSave: false);
            string? asked = null;
            p.AskSwitch = path => { asked = path; return GitignoreSwitchChoice.Cancel; };

            Assert.That(await p.SelectFileAsync("a/.gitignore"), Is.False);

            Assert.That(asked, Is.EqualTo(".gitignore"));
            Assert.That((p.FilePath, p.Text, p.HasUnsavedChanges), Is.EqualTo((".gitignore", "edited\n", true)));
            Assert.That(p.Q<DropdownField>(UiNames.GitignoreFiles).value, Is.EqualTo(".gitignore"));
            Assert.That(Disk(repo), Is.EqualTo(Original));
        }

        [Test]
        public async Task Switch_AutoSave_SavesWithoutAsking()
        {
            var (repo, _, p) = await TwoFilesAsync(autoSave: true);
            p.AskSwitch = _ => throw new AssertionException("auto-save doesn't ask");

            Assert.That(await p.SelectFileAsync("a/.gitignore"), Is.True);

            Assert.That(Disk(repo), Is.EqualTo("edited\n"));
        }

        [Test]
        public async Task View_ReportsUnsaved_OnlyWithAutoSaveOff()
        {
            var (_, s) = await RepoAsync();
            var v = new GitWindowView(s);
            v.SelectTab(WindowTab.Gitignore);
            var p = v.Q<GitignorePanel>();
            await p.Loaded;
            var raised = 0;
            v.UnsavedChanged += () => raised++;

            p.SetPlainText("x\n");
            Assert.That(v.HasUnsavedChanges, Is.False, "auto-save will save it");

            p.AutoSave = false;
            Assert.That(v.HasUnsavedChanges, Is.True);
            Assert.That(raised, Is.GreaterThan(0));
            Assert.That(v.UnsavedMessage, Is.EqualTo(".gitignore has unsaved changes."));
            Assert.That(v.SaveChanges(), Is.True);
            Assert.That(v.HasUnsavedChanges, Is.False);
        }

        [Test]
        public async Task View_DraftBeforeTabOpened_Reported()
        {
            var (repo, s) = await RepoAsync();
            var p = await PanelAsync(s);
            p.AutoSave = false;
            p.SetPlainText("draft\n");

            var v = new GitWindowView(s); // on another tab

            Assert.That(v.SelectedTab, Is.Not.EqualTo(WindowTab.Gitignore), "setup");
            Assert.That(v.HasUnsavedChanges, Is.True);
            v.DiscardChanges();
            Assert.That(v.HasUnsavedChanges, Is.False);
            Assert.That(GitignorePanel.HasDraft(s.ProjectPath), Is.False);
            Assert.That(Disk(repo), Is.EqualTo(Original));
        }
    }
}
