#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    internal sealed class MorePanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly TextList<TagItem> _tags = new TextList<TagItem>("unitygit-tags", t => t.Name + (t.IsAnnotated ? " (annotated)" : ""));
        readonly TextList<StashItem> _stashes = new TextList<StashItem>("unitygit-stashes", s => $"{s.Name}: {s.Message}");
        readonly TextList<LfsLockItem> _locks = new TextList<LfsLockItem>("unitygit-locks", l => $"{l.Path} — {l.OwnerName}");
        readonly Label _locksNotLoaded = new Label("Not loaded");
        readonly Toggle _verbose;

        public MorePanel(GitSession session)
        {
            _session = session;
            AddToClassList("unitygit-panel-content");
            Add(PanelParts.Header("Tags"));
            Add(_tags.View);
            Add(PanelParts.Header("Stashes"));
            Add(_stashes.View);
            Add(PanelParts.Header("LFS locks"));
            Add(_locksNotLoaded);
            Add(_locks.View);
            Add(new Button(() => _ = _session.RefreshAsync(true)) { name = UiNames.LocksRefresh, text = "Refresh locks" });

            // SessionProvider reads this preference on the session's next change, so a toggle applies from then on.
            _verbose = new Toggle("Verbose git logging") { name = UiNames.VerboseToggle };
            _verbose.RegisterValueChangedCallback(e => EditorPrefs.SetBool(SessionProvider.VerboseLoggingKey, e.newValue));
            Add(_verbose);
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
            _locks.Set(locks ?? (IReadOnlyList<LfsLockItem>)Array.Empty<LfsLockItem>());
            _verbose.SetValueWithoutNotify(EditorPrefs.GetBool(SessionProvider.VerboseLoggingKey));
        }
    }
}
