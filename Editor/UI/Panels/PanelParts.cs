#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    /// <summary>A panel the window re-renders in place when the session changes.</summary>
    internal interface IRenderablePanel
    {
        void Render();
    }

    internal static class PanelParts
    {
        public static string Files(int n) => n == 1 ? "1 file" : $"{n} files";

        public static Label Header(string text)
        {
            var label = new Label(text);
            label.AddToClassList("unitygit-header");
            return label;
        }

        public static void SetDisplayed(VisualElement element, bool displayed)
            => element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>
    /// A virtualized, one-line-per-item list. <see cref="Set"/> rebinds only when the text changed, so a refresh that
    /// changes nothing leaves selection and scroll position alone.
    /// </summary>
    internal sealed class TextList<T>
    {
        readonly Func<T, string> _text;
        readonly List<string> _texts = new List<string>();
        List<T> _items = new List<T>();

        public TextList(string name, Func<T, string> text)
        {
            _text = text;
            View = new ListView
            {
                name = name,
                fixedItemHeight = 20,
                showBorder = true,
                selectionType = SelectionType.Single,
                itemsSource = _texts,
                makeItem = () => new Label(),
                bindItem = (element, i) => ((Label)element).text = _texts[i],
            };
            View.AddToClassList("unitygit-list");
        }

        public ListView View { get; }

        public void Set(IReadOnlyList<T> items)
        {
            _items = items.ToList();
            var texts = _items.Select(_text).ToList();
            if (texts.SequenceEqual(_texts)) return;
            _texts.Clear();
            _texts.AddRange(texts);
            View.RefreshItems();
        }

        /// <summary>Runs <paramref name="onChosen"/> with the item the user double-clicked or pressed Enter on.</summary>
        public void OnChosen(Action<T> onChosen)
            => View.itemsChosen += _ =>
            {
                var i = View.selectedIndex;
                if (i >= 0 && i < _items.Count) onChosen(_items[i]);
            };
    }
}
