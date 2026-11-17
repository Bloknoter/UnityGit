#nullable enable
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    internal sealed class AuthorsPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly TextList<AuthorItem> _list = new TextList<AuthorItem>(UiNames.AuthorsList,
            a => $"{a.Name} <{a.Email}> — {a.CommitCount} commits");

        public AuthorsPanel(GitSession session)
        {
            _session = session;
            AddToClassList("unitygit-panel-content");
            Add(_list.View);
            Render();
        }

        public void Render() => _list.Set(_session.Snapshot.Authors);
    }
}
