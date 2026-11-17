#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using UnityGit.UI;
using UnityGit.UI.Theme;

namespace UnityGit.Tests
{
    // Internal because the test cases take EditorUiTone, which is internal to UnityGit.UI.
    [TestFixture, Category("Integration")]
    internal class ThemeTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        async Task<(RemoteTrio trio, GitSession s)> ReadySessionAsync()
        {
            var trio = await _sb.CreateTrioAsync();
            var s = _sb.CreateSession(trio.Local.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            return (trio, s);
        }

        static VisualElement? MessageOf(VisualElement element)
        {
            for (var e = element.parent; e != null; e = e.parent)
                if (e.ClassListContains("ed-message")) return e;
            return null;
        }

        [Test] public async Task Window_AppliesKitTheme()
        {
            var (_, s) = await ReadySessionAsync();

            var v = new GitWindowView(s);

            Assert.That(v.ClassListContains("ed-root"), Is.True);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(EditorUiTheme.ThemePath);
            Assert.That(sheet, Is.Not.Null);
            Assert.That(v.styleSheets.Contains(sheet), Is.True);
        }

        [Test] public void ThemeTypes_AreInternal()
        {
            var publicTypes = typeof(EditorUiTheme).Assembly.GetTypes()
                .Where(t => t.Namespace == "UnityGit.UI.Theme" && t.IsPublic)
                .Select(t => t.FullName);

            Assert.That(publicTypes, Is.Empty);
        }

        [TestCase(OutcomeKind.Succeeded, EditorUiTone.Success)]
        [TestCase(OutcomeKind.Failed, EditorUiTone.Error)]
        [TestCase(OutcomeKind.Cancelled, EditorUiTone.Warning)]
        [TestCase(OutcomeKind.Refused, EditorUiTone.Warning)]
        public void OutcomeTone_MapsKind(OutcomeKind kind, EditorUiTone tone)
        {
            Assert.That(GitWindowView.OutcomeTone(kind), Is.EqualTo(tone));
        }

        [Test] public async Task Outcome_UsesKitMessage()
        {
            var (_, s) = await ReadySessionAsync();
            var v = new GitWindowView(s);

            s.LastOutcome = new ActionOutcome(GitAction.Push, OutcomeKind.Failed, "m", null, Array.Empty<string>());
            v.Render();

            var label = v.Q<Label>(UiNames.Outcome);
            var bar = MessageOf(label);
            Assert.That(bar, Is.Not.Null, "the outcome label sits inside a kit message");
            Assert.That(bar!.ClassListContains("ed-message"), Is.True);
            Assert.That(bar.ClassListContains("ed-message--error"), Is.True);
            Assert.That(label.ClassListContains("ed-message__text"), Is.True);

            s.LastOutcome = new ActionOutcome(GitAction.Push, OutcomeKind.Succeeded, "done");
            v.Render();

            Assert.That(bar.ClassListContains("ed-message--success"), Is.True);
            Assert.That(bar.ClassListContains("ed-message--error"), Is.False);
        }

        [Test] public async Task TabBadge_ShowsChangesCountOnly()
        {
            var (trio, s) = await ReadySessionAsync();
            GitSandbox.WriteFile(trio.Local, "a.txt", "a");
            GitSandbox.WriteFile(trio.Local, "b.txt", "b");
            await s.RefreshAsync();
            var v = new GitWindowView(s);

            Assert.That(v.TabBadge(WindowTab.Changes), Is.EqualTo("2"));
            Assert.That(v.TabBadge(WindowTab.Overview), Is.Null);

            File.Delete(Path.Combine(trio.Local.WorkingDirectory, "a.txt"));
            File.Delete(Path.Combine(trio.Local.WorkingDirectory, "b.txt"));
            await s.RefreshAsync();

            Assert.That(s.Snapshot.Changes, Is.Empty);
            Assert.That(v.TabBadge(WindowTab.Changes), Is.Null);
        }

        [TestCase(ChangeKind.Added, EditorUiTone.Success)]
        [TestCase(ChangeKind.Untracked, EditorUiTone.Success)]
        [TestCase(ChangeKind.Modified, EditorUiTone.Info)]
        [TestCase(ChangeKind.Deleted, EditorUiTone.Error)]
        [TestCase(ChangeKind.Conflicted, EditorUiTone.Error)]
        [TestCase(ChangeKind.Renamed, EditorUiTone.Accent)]
        public void KindTone_MapsKind(ChangeKind kind, EditorUiTone tone)
        {
            Assert.That(ChangesPanel.KindTone(kind), Is.EqualTo(tone));
        }
    }
}
