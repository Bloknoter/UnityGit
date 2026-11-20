#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>A panel the window re-renders in place when the session changes.</summary>
    internal interface IRenderablePanel
    {
        void Render();
    }

    /// <summary>Layout class names shared by the panels; GitWindow.uss styles them with the kit's variables.</summary>
    internal static class PanelClasses
    {
        public const string Content = "unitygit-panel-content";
        public const string Scroll = "unitygit-scroll";
        public const string Fill = "unitygit-fill";
        public const string ListCard = "unitygit-list-card";
        public const string HeaderOnly = "unitygit-header-only";
        public const string List = "unitygit-list";
        public const string Row = "unitygit-list-row";
        public const string Inline = "unitygit-inline";
        public const string Field = "unitygit-field";
        public const string Caption = "unitygit-caption";
        public const string Value = "unitygit-value";
        public const string Ellipsis = "unitygit-ellipsis";
        public const string Path = "unitygit-path";
        public const string Note = "unitygit-card-note";
    }

    internal static class PanelParts
    {
        public static string Files(int n) => n == 1 ? "1 file" : $"{n} files";

        // Core's OutcomeMessages.Places says the same, but Core's internals are hidden from this assembly (they mention FlexGit).
        public static string Places(int n) => n == 1 ? "1 place" : $"{n} places";

        public static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

        static readonly char[] AuthorityEnd = { '/', '?', '#' };

        /// <summary>
        /// A remote URL without its user info, so no credential reaches the screen: <c>https://user:token@host/repo</c>
        /// shows as <c>https://host/repo</c>. scp-style URLs (<c>git@host:repo</c>) and paths have none and are unchanged.
        /// </summary>
        public static string WithoutCredentials(string url)
        {
            var scheme = url.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0) return url;
            var authority = scheme + 3;
            var authorityEnd = url.IndexOfAny(AuthorityEnd, authority);
            if (authorityEnd < 0) authorityEnd = url.Length;
            var at = url.LastIndexOf('@', authorityEnd - 1, authorityEnd - authority);
            return at < 0 ? url : url.Substring(0, authority) + url.Substring(at + 1);
        }

        public static void SetDisplayed(VisualElement element, bool displayed)
            => element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>A label carrying the given classes (kit text classes such as muted, mono or strong).</summary>
        public static Label Text(string? text, params string[] classes)
        {
            var label = new Label(text);
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }

        /// <summary>A kit card that grows with the panel and that <paramref name="list"/> fills edge to edge.</summary>
        public static EditorUiCard ListCard(string? title, VisualElement list)
        {
            var card = EditorUiElements.Card(title);
            card.AddToClassList(PanelClasses.ListCard);
            card.AddToClassList(PanelClasses.Fill);
            card.Add(list);
            return card;
        }

        /// <summary>A caption-and-value line inside a card: a muted caption, then the values side by side.</summary>
        public static VisualElement Field(string caption, params VisualElement[] values)
        {
            var field = new VisualElement();
            field.AddToClassList(PanelClasses.Field);
            field.Add(Text(caption, EditorUiClasses.Muted, PanelClasses.Caption));
            foreach (var value in values) field.Add(value);
            return field;
        }
    }

    /// <summary>
    /// A virtualized list of rich rows. <see cref="Set"/> rebinds only when a row's key changed, so a refresh that
    /// changes nothing leaves selection and scroll position alone. The key must cover everything the bind shows.
    /// </summary>
    internal sealed class RowList<T>
    {
        readonly Func<T, string> _key;
        readonly List<T> _items = new List<T>();
        readonly List<string> _keys = new List<string>();
        Label? _count;

        public RowList(string name, Func<VisualElement> makeRow, Action<VisualElement, T> bindRow, Func<T, string> key, float itemHeight = 24)
        {
            _key = key;
            View = new ListView
            {
                name = name,
                fixedItemHeight = itemHeight,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                selectionType = SelectionType.Single,
                itemsSource = _items,
                makeItem = makeRow,
                bindItem = (row, i) => bindRow(row, _items[i]),
            };
            View.AddToClassList(PanelClasses.List);
        }

        public ListView View { get; }

        /// <summary>The row-count badge in the card header; null until <see cref="InCard"/> made a titled card.</summary>
        public Label? CountBadge => _count;

        /// <summary>The list inside a <see cref="PanelParts.ListCard"/>; a titled card shows the row count in its header.</summary>
        public EditorUiCard InCard(string? title)
        {
            var card = PanelParts.ListCard(title, View);
            if (title != null)
            {
                _count = EditorUiElements.Badge(PanelParts.Count(_items.Count), EditorUiTone.Neutral);
                card.Header.Add(_count);
            }
            return card;
        }

        public void Set(IReadOnlyList<T> items)
        {
            var keys = items.Select(_key).ToList();
            _items.Clear();
            _items.AddRange(items); // fresh items for OnChosen even when nothing visible changed
            if (_count != null) EditorUiElements.SetBadge(_count, PanelParts.Count(_items.Count), EditorUiTone.Neutral);
            if (keys.SequenceEqual(_keys)) return;
            _keys.Clear();
            _keys.AddRange(keys);
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
