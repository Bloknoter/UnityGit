// Vendored from the EditorUI kit (EditorExtension.EditorUI); only namespace, visibility and asset loading changed.
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;


namespace UnityGit.UI.Theme
{
    internal static class EditorUiTheme
    {
        public const string ThemePath = "Packages/com.bloknoter.unitygit/Editor/UI/Theme/EditorDarkTheme.uss";

        private static StyleSheet m_themeSheet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            m_themeSheet = null;
        }

        public static void Apply(VisualElement root)
        {
            var sheet = ThemeSheet();

            if (sheet != null && !root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);

            root.AddToClassList(EditorUiClasses.Root);
        }

        private static StyleSheet ThemeSheet()
        {
            if (m_themeSheet == null)
            {
                m_themeSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemePath);

                if (m_themeSheet == null)
                    Debug.LogError($"[{nameof(EditorUiTheme)}] Missing {nameof(StyleSheet)} at '{ThemePath}'.");
            }

            return m_themeSheet;
        }
    }
}
