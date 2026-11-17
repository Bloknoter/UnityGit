#nullable enable
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    internal sealed class HistoryPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly TextList<CommitItem> _list = new TextList<CommitItem>(UiNames.HistoryList,
            c => $"{c.ShortSha}  {c.Subject} — {c.AuthorName}, {c.Date:yyyy-MM-dd HH:mm}");

        public HistoryPanel(GitSession session)
        {
            _session = session;
            AddToClassList("unitygit-panel-content");
            Add(_list.View);
            Render();
        }

        public void Render() => _list.Set(_session.Snapshot.RecentCommits);
    }
}
