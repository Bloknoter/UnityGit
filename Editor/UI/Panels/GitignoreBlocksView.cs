#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// Blocks mode (spec "Blocks"): the file's lines in order, each block in its own element and the blank lines between
    /// blocks as numbered rows. Comment blocks sit on a flat tinted rectangle. Each block has a × at its top right and can
    /// be dragged to another place between blocks; the view only reports those, the panel edits the text.
    /// </summary>
    internal sealed class GitignoreBlocksView : VisualElement
    {
        const string BlockClass = "unitygit-gitignore-block";
        const string DraggingClass = BlockClass + "--dragging";
        const float DragThreshold = 4f;
        const float EdgeZone = 24f;  // near the view's top or bottom edge a drag scrolls
        const float ScrollStep = 8f; // per pointer move

        readonly ScrollView _scroll;
        readonly VisualElement _dropLine;
        readonly Label _note;
        readonly List<VisualElement> _blocks = new List<VisualElement>();
        bool _readOnly;

        // The press on a block, and the drag it became once the pointer moved far enough.
        int _pressBlock = -1;
        int _pointerId = -1;
        Vector2 _pressPosition;
        bool _dragging;
        int _dropPosition;

        public GitignoreBlocksView()
        {
            name = UiNames.GitignoreBlocks;
            AddToClassList("unitygit-gitignore-blocks");
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("unitygit-gitignore-scroll");
            Add(_scroll);
            _note = PanelParts.Text(GitignoreText.EmptyFile, EditorUiClasses.Muted, PanelClasses.Note, "unitygit-gitignore-empty");
            _dropLine = new VisualElement { name = UiNames.GitignoreDropLine, pickingMode = PickingMode.Ignore };
            _dropLine.AddToClassList("unitygit-gitignore-drop-line");
        }

        /// <summary>The × of block i was clicked.</summary>
        public event Action<int>? RemoveRequested;

        /// <summary>Block i was dropped at a position between blocks (0 = above the first, block count = below the last).</summary>
        public event Action<int, int>? MoveRequested;

        internal IReadOnlyList<VisualElement> BlockElements => _blocks;

        /// <summary>The note shown instead of lines (an empty file), or null.</summary>
        internal string? NoteText => _note.parent != null ? _note.text : null;

        public void Show(GitignoreDocument doc, bool readOnly)
        {
            EndPress();
            _readOnly = readOnly;
            var content = _scroll.contentContainer;
            content.Clear();
            _blocks.Clear();
            if (doc.Blocks.Count == 0)
            {
                content.Add(_note);
                return;
            }

            var gutter = ConflictsPanel.GutterWidth(doc.Lines.Count);
            var next = 0;
            for (var b = 0; b < doc.Blocks.Count; b++)
            {
                var block = doc.Blocks[b];
                for (; next < block.StartLine; next++) content.Add(Gap(next, gutter));
                var element = Block(doc, b, gutter);
                _blocks.Add(element);
                content.Add(element);
                next = block.StartLine + block.LineCount;
            }
            for (; next < doc.Lines.Count; next++) content.Add(Gap(next, gutter));
            content.Add(_dropLine);
            PanelParts.SetDisplayed(_dropLine, false);
        }

        /// <summary>The drop position nearest to a y in the content: how many blocks have their middle above it.</summary>
        internal int DropPositionAt(float contentY)
        {
            var position = 0;
            foreach (var block in _blocks)
                if (block.layout.center.y < contentY) position++;
            return position;
        }

        VisualElement Block(GitignoreDocument doc, int index, float gutter)
        {
            var info = doc.Blocks[index];
            var element = new VisualElement { focusable = !_readOnly };
            element.AddToClassList(BlockClass);
            element.AddToClassList(BlockClass + (info.Kind == GitignoreBlockKind.Comment ? "--comment" : "--code"));
            element.EnableInClassList(BlockClass + "--movable", !_readOnly);
            for (var line = info.StartLine; line < info.StartLine + info.LineCount; line++)
                element.Add(Row(line, doc.Lines[line], gutter));

            var remove = new Button(() => RemoveRequested?.Invoke(index)) { text = "×", tooltip = GitignoreText.Remove };
            remove.AddToClassList("unitygit-gitignore-remove");
            PanelParts.SetDisplayed(remove, !_readOnly);
            element.Add(remove);
            if (_readOnly) return element;

            element.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || (e.target is VisualElement target && (target == remove || remove.Contains(target)))) return;
                _pressBlock = index;
                _pointerId = e.pointerId;
                _pressPosition = e.position;
                element.CapturePointer(e.pointerId);
            });
            element.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_pressBlock != index || e.pointerId != _pointerId) return;
                if (!_dragging)
                {
                    if (((Vector2)e.position - _pressPosition).magnitude < DragThreshold) return;
                    _dragging = true;
                    element.AddToClassList(DraggingClass);
                    element.Focus(); // so Escape reaches it
                    PanelParts.SetDisplayed(_dropLine, true);
                }
                Track(e.position);
            });
            element.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_pressBlock != index || e.pointerId != _pointerId) return;
                var (dragging, from, to) = (_dragging, _pressBlock, _dropPosition);
                EndPress();
                if (dragging) MoveRequested?.Invoke(from, to);
            });
            element.RegisterCallback<PointerCaptureOutEvent>(_ => EndPress());
            element.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!_dragging || e.keyCode != KeyCode.Escape) return;
                EndPress();
                e.StopPropagation();
            });
            return element;
        }

        // Moves the drop line to the boundary nearest the pointer, scrolling when it's near the view's top or bottom.
        void Track(Vector2 worldPosition)
        {
            var viewport = _scroll.contentViewport;
            var inView = viewport.WorldToLocal(worldPosition);
            if (inView.y < EdgeZone) _scroll.scrollOffset -= new Vector2(0, ScrollStep);
            else if (inView.y > viewport.layout.height - EdgeZone) _scroll.scrollOffset += new Vector2(0, ScrollStep);
            var content = _scroll.contentContainer;
            _dropPosition = DropPositionAt(content.WorldToLocal(worldPosition).y);
            _dropLine.style.top = BoundaryY(_dropPosition);
        }

        float BoundaryY(int position)
        {
            if (position <= 0) return _blocks[0].layout.y;
            if (position >= _blocks.Count) return _blocks[_blocks.Count - 1].layout.yMax;
            return (_blocks[position - 1].layout.yMax + _blocks[position].layout.y) / 2;
        }

        // Ends a press or drag without dropping: Escape, a lost capture, a release, or new content.
        void EndPress()
        {
            var pressed = _pressBlock >= 0 && _pressBlock < _blocks.Count ? _blocks[_pressBlock] : null;
            var pointer = _pointerId;
            _pressBlock = -1;
            _pointerId = -1;
            _dragging = false;
            PanelParts.SetDisplayed(_dropLine, false);
            if (pressed == null) return;
            pressed.RemoveFromClassList(DraggingClass);
            if (pressed.HasPointerCapture(pointer)) pressed.ReleasePointer(pointer);
        }

        static VisualElement Row(int line, string text, float gutter)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("unitygit-gitignore-row");
            row.Add(Number(line, gutter));
            var label = ConflictsPanel.FileText(ConflictText.CutLine(text), "unitygit-gitignore-text");
            label.pickingMode = PickingMode.Ignore;
            row.Add(label);
            return row;
        }

        static VisualElement Gap(int line, float gutter)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("unitygit-gitignore-row");
            row.AddToClassList("unitygit-gitignore-gap");
            row.Add(Number(line, gutter));
            return row;
        }

        static Label Number(int line, float width)
        {
            var number = PanelParts.Text(PanelParts.Count(line + 1), EditorUiClasses.Mono, "unitygit-conflict-line-number", "unitygit-diff-line-number");
            number.pickingMode = PickingMode.Ignore;
            number.style.width = width;
            return number;
        }
    }
}
