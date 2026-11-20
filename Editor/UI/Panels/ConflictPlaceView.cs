#nullable enable
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// One place of the file shown in the <see cref="ConflictsPanel"/>: its header and a chip with the pick. While selected
    /// it also shows both sides (and the base) and the pick buttons; while not selected but picked, the text the pick
    /// gives. Clicking it selects it. The panel keeps the picks and the selection and calls <see cref="Show"/>.
    /// </summary>
    internal sealed class ConflictPlaceView : VisualElement
    {
        const string PlaceClass = "unitygit-conflict-place";

        readonly ConflictsPanel _panel;
        readonly ConflictPlace _place;
        readonly Label _chip = EditorUiElements.Chip(string.Empty);
        readonly List<(ConflictPick Pick, Button Button)> _options = new List<(ConflictPick, Button)>();
        // Built when first needed, so a file with many places doesn't lay out every side up front.
        VisualElement? _detail;
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
            header.Add(PanelParts.Text($"Place {PanelParts.Count(place.Index + 1)} · lines {PanelParts.Count(place.StartLine)}–{PanelParts.Count(place.EndLine)}",
                EditorUiClasses.Strong, "unitygit-conflict-place-title"));
            header.Add(_chip);
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
                foreach (var (option, button) in _options) button.EnableInClassList(ConflictsPanel.PickedClass, option == pick);
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
                    AddCapped(_preview, _place.TextFor(chosen), "unitygit-conflict-preview");
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
            detail.Add(Side(_place.OursName, _place.Ours, "ours"));
            if (_place.Base != null) detail.Add(Side("base", _place.Base, "base"));
            detail.Add(Side(_place.TheirsName, _place.Theirs, "theirs"));

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

        void SetChip(string text, EditorUiTone tone)
        {
            _chip.text = text;
            foreach (var toneClass in EditorUiClasses.ChipTones) _chip.RemoveFromClassList(toneClass);
            _chip.AddToClassList(EditorUiClasses.ChipTone(tone));
        }

        static VisualElement Side(string caption, string text, string side)
        {
            var block = new VisualElement();
            block.AddToClassList("unitygit-conflict-side");
            block.AddToClassList("unitygit-conflict-side--" + side);
            block.Add(ConflictsPanel.Plain(PanelParts.Text(caption, EditorUiClasses.Muted, EditorUiClasses.Small, "unitygit-conflict-side-caption")));
            AddCapped(block, text, "unitygit-conflict-text");
            return block;
        }

        // A side's or a pick's text, capped by ConflictText.Cap (lines, characters per line and per block), then how many
        // more lines the file has.
        static void AddCapped(VisualElement parent, string text, string textClass)
        {
            var (shown, hidden) = ConflictText.Cap(ConflictText.ForDisplay(text));
            parent.Add(ConflictsPanel.FileText(shown, textClass));
            if (hidden > 0) parent.Add(PanelParts.Text(ConflictText.MoreLines(hidden), EditorUiClasses.Muted, EditorUiClasses.Small, "unitygit-conflict-more"));
        }
    }
}
