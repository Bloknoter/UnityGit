#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    /// <summary>The UnityGit window: Tools → Git → UnityGit. Its content is a <see cref="GitWindowView"/>.</summary>
    public sealed class GitWindow : EditorWindow
    {
        const string StyleSheetPath = "Packages/com.bloknoter.unitygit/Editor/UI/GitWindow.uss";

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
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styles != null) rootVisualElement.styleSheets.Add(styles);

            _view = new GitWindowView(SessionProvider.Current);
            if (_pendingTab is WindowTab tab) _view.SelectTab(tab);
            _pendingTab = null;
            rootVisualElement.Add(_view);
            SessionProvider.RequestRefresh(); // the window opening is a refresh trigger (spec §5.4)
        }
    }
}
