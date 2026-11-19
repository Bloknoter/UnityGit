#nullable enable
using System;
using System.Globalization;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    internal sealed class HistoryPanel : VisualElement, IRenderablePanel
    {
        readonly GitSession _session;
        readonly RowList<CommitItem> _list = new RowList<CommitItem>(UiNames.HistoryList, () => new CommitRow(), CommitRow.Bind,
            c => $"{c.Sha}\n{c.Subject}\n{c.AuthorName}\n{c.Date:O}");

        public HistoryPanel(GitSession session)
        {
            _session = session;
            AddToClassList(PanelClasses.Content);
            Add(_list.InCard(null));
            Render();
        }

        public void Render() => _list.Set(_session.Snapshot.RecentCommits);

        /// <summary>A commit date in the viewer's local time, formatted the same in every culture.</summary>
        internal static string FormatDate(DateTimeOffset date)
            => date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>Short SHA in monospace, the subject, then the muted author and faint date.</summary>
        sealed class CommitRow : VisualElement
        {
            readonly Label _sha = PanelParts.Text(null, EditorUiClasses.Mono, EditorUiClasses.Muted, "unitygit-sha");
            readonly Label _subject = PanelParts.Text(null, PanelClasses.Ellipsis, "unitygit-row-subject");
            readonly Label _author = PanelParts.Text(null, EditorUiClasses.Muted, PanelClasses.Ellipsis, "unitygit-commit-author");
            readonly Label _date = PanelParts.Text(null, EditorUiClasses.Faint, EditorUiClasses.Mono, EditorUiClasses.Small, "unitygit-commit-date");

            public CommitRow()
            {
                AddToClassList(PanelClasses.Row);
                Add(_sha);
                Add(_subject);
                Add(_author);
                Add(_date);
            }

            // Rows are recycled, so every part of the row is set here.
            public static void Bind(VisualElement element, CommitItem c)
            {
                var row = (CommitRow)element;
                row._sha.text = c.ShortSha;
                row._subject.text = c.Subject;
                row._author.text = c.AuthorName;
                row._date.text = FormatDate(c.Date);
                row.tooltip = c.Subject;
            }
        }
    }
}
