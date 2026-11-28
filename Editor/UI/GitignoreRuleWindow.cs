#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// The small Add rule window: a utility window, so it stays open while the Browse dialog has focus (a popup would
    /// close). One at a time; it closes on Add or Cancel, and after a script reload, which loses the panel it adds to.
    /// </summary>
    internal sealed class GitignoreRuleWindow : EditorWindow
    {
        static GitignoreRuleWindow? s_open;
        GitignoreRuleForm? _form;

        public static GitignoreRuleWindow Open(GitignoreRuleKind kind, string gitignoreFolder, string projectPath,
            Action<IReadOnlyList<string>> onAdd)
        {
            if (s_open != null) s_open.Close();
            var window = CreateInstance<GitignoreRuleWindow>();
            window.titleContent = new GUIContent(GitignoreText.WindowTitle(GitignoreText.KindName(kind)));
            window.minSize = window.maxSize = new Vector2(440, 240);
            window._form = new GitignoreRuleForm(kind, gitignoreFolder, projectPath, lines =>
            {
                onAdd(lines);
                window.Close();
            }, window.Close);

            var root = window.rootVisualElement;
            root.AddToClassList("unitygit-window");
            root.AddToClassList("unitygit-gitignore-rule-window");
            EditorUiTheme.Apply(root);
            var layout = AssetDatabase.LoadAssetAtPath<StyleSheet>(GitWindowView.LayoutSheetPath); // after the theme
            if (layout != null) root.styleSheets.Add(layout);
            root.Add(window._form);

            window.ShowUtility();
            s_open = window;
            window._form.Value.Focus();
            return window;
        }

        void OnEnable()
        {
            // After a script reload the form, and the panel it adds to, are gone.
            EditorApplication.delayCall += () =>
            {
                if (this != null && _form == null) Close();
            };
        }

        void OnDestroy()
        {
            if (s_open == this) s_open = null;
        }
    }
}
