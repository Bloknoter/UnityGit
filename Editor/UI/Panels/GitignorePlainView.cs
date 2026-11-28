#nullable enable
using System;
using System.Text;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// Plain text mode: the file's line numbers beside a multiline text box, in the diff view's look. Lines don't wrap and
    /// one scroll view moves both, so each number stays beside its line.
    /// </summary>
    internal sealed class GitignorePlainView : VisualElement
    {
        readonly Label _gutter;

        public GitignorePlainView()
        {
            name = UiNames.GitignorePlain;
            AddToClassList("unitygit-gitignore-plain");
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.AddToClassList("unitygit-gitignore-scroll");
            var row = new VisualElement();
            row.AddToClassList("unitygit-gitignore-plain-row");

            _gutter = PanelParts.Text("1", EditorUiClasses.Mono, "unitygit-conflict-line-number", "unitygit-diff-line-number", "unitygit-gitignore-gutter");
            _gutter.pickingMode = PickingMode.Ignore;
            ConflictsPanel.Plain(_gutter);

            Field = new TextField { name = UiNames.GitignoreField, multiline = true };
            Field.verticalScrollerVisibility = ScrollerVisibility.Hidden; // the outer scroll view scrolls the numbers too
            Field.AddToClassList(EditorUiClasses.Mono);
            Field.AddToClassList("unitygit-gitignore-field");
            Field.RegisterValueChangedCallback(e =>
            {
                UpdateGutter(e.newValue);
                Edited?.Invoke(e.newValue);
            });

            row.Add(_gutter);
            row.Add(Field);
            scroll.Add(row);
            Add(scroll);
        }

        /// <summary>Raised with the whole text on each edit the user makes.</summary>
        public event Action<string>? Edited;

        internal TextField Field { get; }

        /// <summary>"1\n2\n…": one number per line of the file.</summary>
        internal string GutterText => _gutter.text;

        public void Show(string text, bool readOnly)
        {
            if (Field.value != text) Field.SetValueWithoutNotify(text);
            Field.isReadOnly = readOnly;
            UpdateGutter(text);
        }

        void UpdateGutter(string text)
        {
            var count = Math.Max(1, GitignoreDocument.Parse(text).Lines.Count);
            var sb = new StringBuilder(count * 4);
            for (var i = 1; i <= count; i++)
            {
                if (i > 1) sb.Append('\n');
                sb.Append(PanelParts.Count(i));
            }
            _gutter.text = sb.ToString();
            _gutter.style.width = ConflictsPanel.GutterWidth(count);
        }
    }
}
