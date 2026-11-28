#nullable enable
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    public class GitignoreRuleFormTests
    {
        static readonly string F = Path.Combine(Path.GetTempPath(), "UnityGitRuleFormTests");

        IReadOnlyList<string>? _added;
        int _cancelled;

        GitignoreRuleForm Form(GitignoreRuleKind kind) => new GitignoreRuleForm(kind, F, F, lines => _added = lines, () => _cancelled++);

        static bool IsDisplayed(VisualElement element) => element.style.display != DisplayStyle.None;

        // A key press as the editor sends it: to the element with keyboard focus.
        static void Key(VisualElement target, KeyCode key, EventModifiers modifiers = EventModifiers.None)
        {
            target.Focus();
            var focused = target.panel.focusController.focusedElement as VisualElement;
            Assert.That(focused, Is.Not.Null, "setup: the field has keyboard focus");
            using (var down = KeyDownEvent.GetPooled('\0', key, modifiers)) focused!.SendEvent(down);
        }

        [SetUp] public void SetUp()
        {
            _added = null;
            _cancelled = 0;
        }

        [Test]
        public void Form_Extension_AddsLines()
        {
            var form = Form(GitignoreRuleKind.Extension);
            form.Comment.value = "PSDs";
            form.Value.value = ".psd";

            form.Submit();

            Assert.That(_added, Is.EqualTo(new[] { "# PSDs", "*.psd", "*.psd.meta" }));
        }

        [Test]
        public void Form_Negate_ShowsHint()
        {
            var form = Form(GitignoreRuleKind.File);
            using var window = UnityGitTestWindow.Open(form);
            Assert.That(IsDisplayed(form.NegateHint), Is.False);

            form.Negate!.value = true;

            Assert.That(IsDisplayed(form.NegateHint), Is.True);
            Assert.That(form.NegateHint.text, Is.EqualTo("Git can't re-include a file whose parent folder is ignored."));
        }

        [Test]
        public void Form_Folder_MetaToggleOnlyInsideAssets()
        {
            var form = Form(GitignoreRuleKind.Folder);
            using var window = UnityGitTestWindow.Open(form);

            form.Value.value = "Assets/Foo";
            Assert.That(IsDisplayed(form.IncludeMeta!), Is.True);
            Assert.That(form.IncludeMeta!.value, Is.True, "on by default");

            form.Value.value = "Builds";
            Assert.That(IsDisplayed(form.IncludeMeta!), Is.False);
        }

        [Test]
        public void Form_Invalid_AddDisabled()
        {
            var form = Form(GitignoreRuleKind.Extension);
            using var window = UnityGitTestWindow.Open(form);
            Assert.That(form.AddButton.enabledSelf, Is.False);
            Assert.That(IsDisplayed(form.Error), Is.False, "an untouched form isn't red");

            form.Value.value = "a/b";

            Assert.That(form.AddButton.enabledSelf, Is.False);
            Assert.That(IsDisplayed(form.Error), Is.True);
            Assert.That(form.Error.text, Is.EqualTo("An extension can't contain /"));

            form.Value.value = "psd";
            Assert.That(form.AddButton.enabledSelf, Is.True);
            Assert.That(IsDisplayed(form.Error), Is.False);
        }

        [Test]
        public void Form_Custom_NoNegateNoMeta()
        {
            var form = Form(GitignoreRuleKind.Custom);
            Assert.That(form.Negate, Is.Null);
            Assert.That(form.IncludeMeta, Is.Null);
            Assert.That(form.Value.multiline, Is.True);
        }

        [Test]
        public void Form_EnterSubmits_EscapeCancels()
        {
            var form = Form(GitignoreRuleKind.Extension);
            using var window = UnityGitTestWindow.Open(form);
            form.Value.value = "psd";

            Key(form.Value, KeyCode.Return);
            Assert.That(_added, Is.EqualTo(new[] { "*.psd", "*.psd.meta" }));

            Key(form.Value, KeyCode.Escape);
            Assert.That(_cancelled, Is.EqualTo(1));
        }

        [Test]
        public void Form_Custom_EnterNeedsActionKey()
        {
            var form = Form(GitignoreRuleKind.Custom);
            using var window = UnityGitTestWindow.Open(form);
            form.Value.value = "x";

            Key(form.Value, KeyCode.Return);
            Assert.That(_added, Is.Null, "Enter makes a new line in a custom rule");

            Key(form.Value, KeyCode.Return, Application.platform == RuntimePlatform.OSXEditor ? EventModifiers.Command : EventModifiers.Control);
            Assert.That(_added, Is.EqualTo(new[] { "x" }));
        }

        [Test]
        public void WindowTitle_NamesTheItem()
            => Assert.That(GitignoreText.WindowTitle(GitignoreText.KindName(GitignoreRuleKind.Folder)), Is.EqualTo("Add rule — All in folder"));
    }
}
