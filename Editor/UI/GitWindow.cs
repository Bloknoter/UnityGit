#nullable enable
using UnityEditor;
using UnityEngine;

namespace UnityGit.UI
{
    /// <summary>
    /// The UnityGit window: Tools → Git → UnityGit. Its content is a <see cref="GitWindowView"/>, which brings its own
    /// theme and layout style sheets, so the window only hosts it.
    /// </summary>
    public sealed class GitWindow : EditorWindow
    {
        GitWindowView? _view;
        WindowTab? _pendingTab;

        public static GitWindow Open() => GetWindow<GitWindow>("UnityGit");

        // Unity doesn't promise to accept [MenuItem] on a method that returns a value.
        [MenuItem("Tools/Git/UnityGit")]
        static void OpenFromMenu() => Open();

        /// <summary>Opens the window on the Changes panel.</summary>
        public static void ShowChanges() => Open().SelectTab(WindowTab.Changes);

        // CreateGUI can run after GetWindow returns, so a tab picked before then is applied when the view is built.
        void SelectTab(WindowTab tab)
        {
            if (_view != null) _view.SelectTab(tab);
            else _pendingTab = tab;
        }

        void CreateGUI()
        {
            titleContent = new GUIContent("UnityGit");
            _view = new GitWindowView(SessionProvider.Current); // on the tab shown before a domain reload, if any
            if (_pendingTab is WindowTab tab) _view.SelectTab(tab);
            _pendingTab = null;
            rootVisualElement.Add(_view);
            SessionProvider.RequestRefresh(); // the window opening is a refresh trigger (spec §5.4)
        }
    }
}
