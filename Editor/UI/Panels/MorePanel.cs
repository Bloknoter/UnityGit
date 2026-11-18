#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>Cards for tags, stashes, LFS locks and settings.</summary>
    internal sealed class MorePanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly RowList<TagItem> _tags = new RowList<TagItem>("unitygit-tags", () => new TagRow(), TagRow.Bind,
            t => $"{t.Name}\n{t.IsAnnotated}");
        readonly RowList<StashItem> _stashes = new RowList<StashItem>("unitygit-stashes", () => new PairRow(stash: true), PairRow.BindStash,
            s => $"{s.Name}\n{s.Message}");
        readonly RowList<LfsLockItem> _locks = new RowList<LfsLockItem>("unitygit-locks", () => new PairRow(stash: false), PairRow.BindLock,
            l => $"{l.Path}\n{l.OwnerName}");
        readonly EditorUiCard _locksCard;
        readonly Label _locksNotLoaded = PanelParts.Text("Not loaded", EditorUiClasses.Muted, PanelClasses.Note);
        readonly Toggle _verbose;

        public MorePanel(GitSession session)
        {
            _session = session;
            AddToClassList(PanelClasses.Content);
            Add(_tags.InCard("Tags"));
            Add(_stashes.InCard("Stashes"));

            _locksCard = _locks.InCard("LFS locks");
            _locksCard.Insert(0, _locksNotLoaded);
            var refreshLocks = new Button(() => _ = _session.RefreshAsync(true)) { name = UiNames.LocksRefresh, text = "Refresh locks" };
            GitIcons.Apply(refreshLocks, GitIcon.Lock);
            _locksCard.Header.Add(refreshLocks);
            Add(_locksCard);

            // SessionProvider reads this preference on the session's next change, so a toggle applies from then on.
            var settings = EditorUiElements.Card("Settings");
            _verbose = new Toggle("Verbose git logging") { name = UiNames.VerboseToggle };
            _verbose.RegisterValueChangedCallback(e => EditorPrefs.SetBool(SessionProvider.VerboseLoggingKey, e.newValue));
            settings.Add(_verbose);
            Add(settings);
            Render();
        }

        public void Render()
        {
            var s = _session.Snapshot;
            _tags.Set(s.Tags);
            _stashes.Set(s.Stashes);
            var locks = s.LfsLocks;
            PanelParts.SetDisplayed(_locksNotLoaded, locks == null);
            PanelParts.SetDisplayed(_locks.View, locks != null);
            _locksCard.EnableInClassList(PanelClasses.Fill, locks != null);
            if (_locks.CountBadge != null) PanelParts.SetDisplayed(_locks.CountBadge, locks != null);
            _locks.Set(locks ?? (IReadOnlyList<LfsLockItem>)Array.Empty<LfsLockItem>());
            _verbose.SetValueWithoutNotify(EditorPrefs.GetBool(SessionProvider.VerboseLoggingKey));
        }

        /// <summary>A tag name with an "annotated" badge for annotated tags.</summary>
        sealed class TagRow : VisualElement
        {
            readonly Label _name = PanelParts.Text(null, PanelClasses.Ellipsis, EditorUiClasses.Grow);
            readonly Label _annotated = EditorUiElements.Badge("annotated", EditorUiTone.Neutral);

            public TagRow()
            {
                AddToClassList(PanelClasses.Row);
                Add(_name);
                Add(_annotated);
            }

            // Rows are recycled, so every part of the row is set here.
            public static void Bind(VisualElement element, TagItem t)
            {
                var row = (TagRow)element;
                row._name.text = t.Name;
                PanelParts.SetDisplayed(row._annotated, t.IsAnnotated);
            }
        }

        /// <summary>
        /// Two values on a line: a stash's monospace name and its message, or a locked path and its muted owner. Each
        /// list makes its own kind of row, so the classes are fixed at construction and the bind only sets the texts.
        /// </summary>
        sealed class PairRow : VisualElement
        {
            readonly Label _first;
            readonly Label _second;

            public PairRow(bool stash)
            {
                AddToClassList(PanelClasses.Row);
                _first = stash
                    ? PanelParts.Text(null, EditorUiClasses.Mono, EditorUiClasses.Muted, "unitygit-pair-key")
                    : PanelParts.Text(null, PanelClasses.Ellipsis, PanelClasses.Path, EditorUiClasses.Grow);
                _second = stash
                    ? PanelParts.Text(null, PanelClasses.Ellipsis, "unitygit-row-subject")
                    : PanelParts.Text(null, EditorUiClasses.Muted, PanelClasses.Ellipsis, "unitygit-pair-owner");
                Add(_first);
                Add(_second);
            }

            // Rows are recycled, so every text of the row is set here.
            public static void BindStash(VisualElement element, StashItem s) => ((PairRow)element).Set(s.Name, s.Message);

            public static void BindLock(VisualElement element, LfsLockItem l) => ((PairRow)element).Set(l.Path, l.OwnerName);

            void Set(string first, string second)
            {
                _first.text = first;
                _second.text = second;
                tooltip = $"{first}  {second}";
            }
        }
    }
}
