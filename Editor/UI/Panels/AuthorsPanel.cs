#nullable enable
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>Authors laid out like the kit's table: Name, Email, and Commits right-aligned; the rows stay virtualized.</summary>
    internal sealed class AuthorsPanel : VisualElement, IRenderablePanel
    {
        const string NameColumn = "unitygit-col-name";
        const string EmailColumn = "unitygit-col-email";
        const string CountColumn = "unitygit-col-count";
        static readonly string[] Headers = { "Name", "Email", "Commits" };
        static readonly bool[] Numeric = { false, false, true };
        static readonly string[] Columns = { NameColumn, EmailColumn, CountColumn };

        readonly GitSession _session;
        readonly RowList<AuthorItem> _list = new RowList<AuthorItem>(UiNames.AuthorsList, () => new AuthorRow(), AuthorRow.Bind,
            a => $"{a.Name}\n{a.Email}\n{a.CommitCount}", 22);

        public AuthorsPanel(GitSession session)
        {
            _session = session;
            AddToClassList(PanelClasses.Content);

            var table = EditorUiElements.Table(Headers, Numeric);
            table.AddToClassList(PanelClasses.Fill);
            var header = table.Q(className: EditorUiClasses.TableHeader);
            for (var i = 0; i < Columns.Length; i++) header[i].AddToClassList(Columns[i]);
            table.Add(_list.View);
            Add(table);
            Render();
        }

        public void Render() => _list.Set(_session.Snapshot.Authors);

        sealed class AuthorRow : VisualElement
        {
            readonly Label _name = Cell(NameColumn);
            readonly Label _email = Cell(EmailColumn, EditorUiClasses.Muted);
            readonly Label _count = Cell(CountColumn, EditorUiClasses.TableCellNumeric);

            public AuthorRow()
            {
                AddToClassList(EditorUiClasses.TableRow);
                Add(_name);
                Add(_email);
                Add(_count);
            }

            // Rows are recycled, so every part of the row is set here.
            public static void Bind(VisualElement element, AuthorItem a)
            {
                var row = (AuthorRow)element;
                row._name.text = a.Name;
                row._email.text = a.Email;
                row._count.text = PanelParts.Count(a.CommitCount);
            }

            static Label Cell(string column, string? extra = null)
            {
                var cell = PanelParts.Text(null, EditorUiClasses.TableCell, column);
                if (extra != null) cell.AddToClassList(extra);
                return cell;
            }
        }
    }
}
