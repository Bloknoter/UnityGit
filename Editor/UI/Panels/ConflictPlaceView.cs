#nullable enable
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// One place of the file shown in the <see cref="ConflictsPanel"/>: a chip with the pick. While selected it also shows
    /// both sides (and the base) and the pick buttons, marking the sides the pick uses; while not selected but picked, the
    /// text the pick gives. Clicking it selects it. The panel keeps the picks and the selection and calls <see cref="Show"/>.
    /// </summary>
    internal sealed class ConflictPlaceView : VisualElement
    {
        const string PlaceClass = "unitygit-conflict-place";
        const string UsedSideClass = "unitygit-conflict-side--used";
        const string UnusedSideClass = "unitygit-conflict-side--unused";

        readonly ConflictsPanel _panel;
        readonly ConflictPlace _place;
        readonly Label _chip = ConflictsPanel.Plain(EditorUiElements.Chip(string.Empty)); // the pick's label holds side names
        readonly List<(ConflictPick Pick, Button Button)> _options = new List<(ConflictPick, Button)>();
        // Built when first needed, so a file with many places doesn't lay out every side up front.
        VisualElement? _detail;
        VisualElement? _ours;
        VisualElement? _base;
        VisualElement? _theirs;
        VisualElement? _buttons;
        Button? _clear;
        VisualElement? _preview;
        ConflictPick? _previewPick;

        public ConflictPlaceView(ConflictsPanel panel, ConflictPlace place)
        {
            _panel = panel;
            _place = place;
            name = UiNames.ConflictPlacePrefix + place.Index.ToString(CultureInfo.InvariantCulture);
            AddToClassList(PlaceClass);
            var header = new VisualElement();
            header.AddToClassList("unitygit-conflict-place-header");
            header.Add(_chip); // no title: the line numbers say where the place is
            Add(header);
            // A pick button's Clickable stops the pointer event, so picking doesn't select this place again.
            RegisterCallback<PointerDownEvent>(_ => _panel.SelectPlace(_place.Index));
        }

        public int Index => _place.Index;

        public void Show(bool selected, ConflictPick? pick)
        {
            EnableInClassList(PlaceClass + "--selected", selected);
            EnableInClassList(PlaceClass + "--chosen", pick != null);
            if (pick is ConflictPick picked) SetChip(_place.Label(picked), EditorUiTone.Success);
            else SetChip("Not chosen", EditorUiTone.Warning);

            if (selected)
            {
                var detail = _detail ??= BuildDetail();
                if (detail.parent == null) Add(detail);
                foreach (var (option, button) in _options) ConflictsPanel.SetPicked(button, option == pick);
                // Use A uses A, Use B uses B, either order both; the base never. No pick: the sides look as made.
                MarkSide(_ours!, pick == null ? (bool?)null : pick != ConflictPick.Theirs);
                MarkSide(_theirs!, pick == null ? (bool?)null : pick != ConflictPick.Ours);
                if (_base != null) MarkSide(_base, pick == null ? (bool?)null : false);
                if (pick == null) _clear!.RemoveFromHierarchy();
                else if (_clear!.parent == null) _buttons!.Add(_clear);
            }
            else _detail?.RemoveFromHierarchy();

            if (!selected && pick is ConflictPick chosen)
            {
                if (_preview == null || _previewPick != chosen)
                {
                    _preview?.RemoveFromHierarchy();
                    _preview = new VisualElement();
                    _preview.AddToClassList("unitygit-conflict-preview-box");
                    AddCapped(_preview, PreviewSegments(chosen), "unitygit-conflict-preview");
                    _previewPick = chosen;
                }
                if (_preview.parent == null) Add(_preview);
            }
            else _preview?.RemoveFromHierarchy();
        }

        VisualElement BuildDetail()
        {
            var detail = new VisualElement();
            detail.AddToClassList("unitygit-conflict-place-detail");
            var starts = ConflictText.SideStarts(_place);
            detail.Add(_ours = Side(_place.OursName, _place.Ours, starts.Ours, "ours"));
            if (_place.Base != null) detail.Add(_base = Side("base", _place.Base, starts.Base, "base"));
            detail.Add(_theirs = Side(_place.TheirsName, _place.Theirs, starts.Theirs, "theirs"));

            _buttons = new VisualElement { name = UiNames.ConflictPlaceButtons };
            _buttons.AddToClassList("unitygit-conflict-options");
            foreach (var pick in ConflictsPanel.AllPicks)
            {
                var button = ConflictsPanel.OptionButton(_place.Label(pick), () => _panel.Pick(pick));
                _options.Add((pick, button));
                _buttons.Add(button);
            }
            _clear = new Button(_panel.ClearPick) { text = "Clear" };
            _clear.AddToClassList("unitygit-conflict-clear");
            detail.Add(_buttons);
            return detail;
        }

        static void MarkSide(VisualElement block, bool? used)
        {
            block.EnableInClassList(UsedSideClass, used == true);
            block.EnableInClassList(UnusedSideClass, used == false);
        }

        void SetChip(string text, EditorUiTone tone)
        {
            _chip.text = text;
            foreach (var toneClass in EditorUiClasses.ChipTones) _chip.RemoveFromClassList(toneClass);
            _chip.AddToClassList(EditorUiClasses.ChipTone(tone));
        }

        // A side's lines, numbered from the line it starts on in the file.
        VisualElement Side(string caption, string text, int firstLine, string side)
        {
            var block = new VisualElement();
            block.AddToClassList("unitygit-conflict-side");
            block.AddToClassList("unitygit-conflict-side--" + side);
            block.Add(ConflictsPanel.Plain(PanelParts.Text(caption, EditorUiClasses.Muted, EditorUiClasses.Small, "unitygit-conflict-side-caption")));
            AddCapped(block, new[] { (text, firstLine) }, "unitygit-conflict-text");
            return block;
        }

        // The sides a pick takes, in its order, each numbered from where it starts in the file.
        IReadOnlyList<(string Text, int FirstLine)> PreviewSegments(ConflictPick pick)
        {
            var starts = ConflictText.SideStarts(_place);
            var ours = (_place.Ours, starts.Ours);
            var theirs = (_place.Theirs, starts.Theirs);
            switch (pick)
            {
                case ConflictPick.Ours: return new[] { ours };
                case ConflictPick.Theirs: return new[] { theirs };
                case ConflictPick.OursThenTheirs: return new[] { ours, theirs };
                default: return new[] { theirs, ours };
            }
        }

        // Numbered rows of the text, capped by ConflictText.Rows (lines, characters per line and per block), then how many
        // more lines the file has. Text without lines says so: that side removed them.
        void AddCapped(VisualElement parent, IReadOnlyList<(string Text, int FirstLine)> segments, string textClass)
        {
            var (rows, hidden) = ConflictText.Rows(segments);
            if (rows.Count == 0 && hidden == 0) parent.Add(PanelParts.Text("(no lines)", EditorUiClasses.Muted, EditorUiClasses.Small, "unitygit-conflict-no-lines"));
            _panel.AddRows(parent, rows, textClass);
            if (hidden > 0) parent.Add(PanelParts.Text(ConflictText.MoreLines(hidden), EditorUiClasses.Muted, EditorUiClasses.Small, "unitygit-conflict-more"));
        }
    }
}
