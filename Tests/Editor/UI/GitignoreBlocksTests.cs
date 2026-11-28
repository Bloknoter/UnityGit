#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI;

namespace UnityGit.Tests
{
    public class GitignoreBlocksTests
    {
        const string NumberClass = "unitygit-diff-line-number";

        static GitignoreBlocksView Shown(string text, bool readOnly = false)
        {
            var view = new GitignoreBlocksView();
            view.Show(GitignoreDocument.Parse(text), readOnly);
            return view;
        }

        static List<Button> RemoveButtons(VisualElement view) => view.Query<Button>(className: "unitygit-gitignore-remove").ToList();

        [Test]
        public void Blocks_RenderTemplate()
        {
            var doc = GitignoreDocument.Parse(GitignoreTemplate.Text);

            var view = Shown(GitignoreTemplate.Text);

            Assert.That(view.BlockElements.Count, Is.EqualTo(doc.Blocks.Count));
            Assert.That(view.BlockElements[0].ClassListContains("unitygit-gitignore-block--comment"), Is.True);
            Assert.That(view.BlockElements[1].ClassListContains("unitygit-gitignore-block--code"), Is.True);
            Assert.That(view.Q<Label>(className: NumberClass).text, Is.EqualTo("1"));
            Assert.That(RemoveButtons(view).Count, Is.EqualTo(doc.Blocks.Count));
            Assert.That(view.NoteText, Is.Null);
        }

        [Test]
        public void Blocks_GapRowsNumbered()
        {
            var view = Shown("# a\n\n\n# b\n");

            var gaps = view.Query(className: "unitygit-gitignore-gap").ToList();

            Assert.That(gaps.Select(g => g.Q<Label>(className: NumberClass).text), Is.EqualTo(new[] { "2", "3" }));
        }

        [Test]
        public void Blocks_CodeShownAsTyped()
        {
            var view = Shown("<b>x</b>\n");
            Assert.That(view.BlockElements[0].Q<Label>(className: "unitygit-gitignore-text").text, Is.EqualTo("<b>x</b>"));
        }

        [Test]
        public void Blocks_Empty_ShowsNote()
        {
            var view = Shown("");
            Assert.That(view.BlockElements, Is.Empty);
            Assert.That(view.NoteText, Is.EqualTo("Empty file — add a rule, or type in Plain text"));
        }

        [Test]
        public void Blocks_ReadOnly_NoRemoveButtons()
            => Assert.That(RemoveButtons(Shown("# a\nx\n", readOnly: true)).Where(b => b.style.display != DisplayStyle.None), Is.Empty);

        [Test]
        public void Blocks_XButton_RaisesRemove()
        {
            var view = Shown("# a\nx\n# b\n");
            using var window = UnityGitTestWindow.Open(view);
            var removed = new List<int>();
            view.RemoveRequested += removed.Add;

            var remove = view.BlockElements[2].Q<Button>(className: "unitygit-gitignore-remove");
            remove.Focus();
            using (var submit = NavigationSubmitEvent.GetPooled()) remove.SendEvent(submit); // what a click does

            Assert.That(removed, Is.EqualTo(new[] { 2 }));
        }

        static void Send(VisualElement target, EventType type, UnityEngine.Vector2 position)
        {
            var e = new UnityEngine.Event { type = type, mousePosition = position, button = 0 };
            if (type == EventType.MouseDown) using (var down = PointerDownEvent.GetPooled(e)) target.SendEvent(down);
            else if (type == EventType.MouseDrag) using (var move = PointerMoveEvent.GetPooled(e)) target.SendEvent(move);
            else using (var up = PointerUpEvent.GetPooled(e)) target.SendEvent(up);
        }

        [Test]
        public void Blocks_Drag_RaisesMove()
        {
            var view = Shown("# a\nx\n# b\n\n\nz\n");
            view.style.flexGrow = 1;
            using var window = UnityGitTestWindow.Open(view);
            window.ResolveStylesAndLayout();
            var moves = new List<(int, int)>();
            view.MoveRequested += (block, position) => moves.Add((block, position));
            var first = view.BlockElements[0];
            var last = view.BlockElements[3];

            Send(first, EventType.MouseDown, first.worldBound.center);
            Send(first, EventType.MouseDrag, first.worldBound.center + new UnityEngine.Vector2(0, 2)); // under the threshold
            Assert.That(first.ClassListContains("unitygit-gitignore-block--dragging"), Is.False, "not a drag yet");
            Send(first, EventType.MouseDrag, new UnityEngine.Vector2(last.worldBound.center.x, last.worldBound.yMax - 1));
            Assert.That(first.ClassListContains("unitygit-gitignore-block--dragging"), Is.True);
            Send(first, EventType.MouseUp, new UnityEngine.Vector2(last.worldBound.center.x, last.worldBound.yMax - 1));

            Assert.That(moves, Is.EqualTo(new[] { (0, 4) }));
            Assert.That(first.ClassListContains("unitygit-gitignore-block--dragging"), Is.False);
        }

        [Test]
        public void Blocks_ClickWithoutDrag_DoesNotMove()
        {
            var view = Shown("# a\nx\n");
            view.style.flexGrow = 1;
            using var window = UnityGitTestWindow.Open(view);
            window.ResolveStylesAndLayout();
            var moves = 0;
            view.MoveRequested += (_, _) => moves++;
            var block = view.BlockElements[1];

            Send(block, EventType.MouseDown, block.worldBound.center);
            Send(block, EventType.MouseUp, block.worldBound.center);

            Assert.That(moves, Is.EqualTo(0));
        }

        [Test]
        public void Blocks_DropPositionAt()
        {
            var view = Shown("# a\nx\ny\n# b\n\n\nz\n");
            view.style.flexGrow = 1;
            using var window = UnityGitTestWindow.Open(view);
            window.ResolveStylesAndLayout();
            var blocks = view.BlockElements;
            Assert.That(blocks.Count, Is.EqualTo(4), "setup");
            Assert.That(blocks[1].layout.height, Is.GreaterThan(0), "setup: laid out");

            Assert.That(view.DropPositionAt(blocks[0].layout.y + 1), Is.EqualTo(0));
            Assert.That(view.DropPositionAt(blocks[1].layout.center.y + 1), Is.EqualTo(2));
            Assert.That(view.DropPositionAt(blocks[3].layout.yMax - 1), Is.EqualTo(4));
        }
    }
}
