#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    internal sealed class BranchesPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly TextList<BranchItem> _local = new TextList<BranchItem>(UiNames.LocalBranches, Text);
        readonly TextList<BranchItem> _remote = new TextList<BranchItem>(UiNames.RemoteBranches, Text);

        public BranchesPanel(GitSession session)
        {
            _session = session;
            AddToClassList("unitygit-panel-content");
            Add(PanelParts.Header("Local branches"));
            Add(_local.View);
            Add(PanelParts.Header("Remote branches"));
            Add(_remote.View);
            _local.OnChosen(b => _ = _session.SwitchBranchAsync(b.Name));
            _remote.OnChosen(b => _ = _session.SwitchBranchAsync(b.ShortName));
            Render();
        }

        public void Render()
        {
            _local.Set(_session.Snapshot.LocalBranches);
            _remote.Set(_session.Snapshot.RemoteBranches);
        }

        static string Text(BranchItem b)
        {
            var parts = new List<string> { b.Name };
            if (b.Upstream != null) parts.Add($"→ {b.Upstream}");
            if (b.Ahead != 0 || b.Behind != 0) parts.Add($"↑{b.Ahead} ↓{b.Behind}");
            if (b.LastCommitSubject.Length > 0) parts.Add(b.LastCommitSubject);
            return string.Join("  ", parts);
        }
    }
}
