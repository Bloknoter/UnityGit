// Vendored from the EditorUI kit (EditorExtension.EditorUI); only namespace, visibility and asset loading changed.
using UnityEngine.UIElements;


namespace UnityGit.UI.Theme
{
    internal class EditorUiCard : VisualElement
    {
        private readonly VisualElement m_header;
        private readonly Label m_title;
        private readonly VisualElement m_content;

        public EditorUiCard() : this(null)
        {
        }

        public EditorUiCard(string title)
        {
            AddToClassList(EditorUiClasses.Card);

            m_header = new VisualElement();
            m_header.AddToClassList(EditorUiClasses.CardHeader);

            m_title = new Label();
            m_title.AddToClassList(EditorUiClasses.CardTitle);
            m_header.Add(m_title);

            m_content = new VisualElement();
            m_content.AddToClassList(EditorUiClasses.CardContent);
            hierarchy.Add(m_content);

            Title = title;
        }

        public string Title
        {
            get => m_title.text;
            set => SetTitle(value);
        }

        public VisualElement Header => m_header;

        public VisualElement Content => m_content;

        public override VisualElement contentContainer => m_content;

        private void SetTitle(string title)
        {
            m_title.text = title;

            if (string.IsNullOrEmpty(title))
            {
                m_header.RemoveFromHierarchy();
                return;
            }

            if (m_header.parent == null)
                hierarchy.Insert(0, m_header);
        }
    }
}
