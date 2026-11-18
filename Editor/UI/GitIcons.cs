#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    /// <summary>The package's action icons. Each has a white-on-transparent PNG in <see cref="GitIcons.Folder"/>.</summary>
    internal enum GitIcon { Branch, Commit, Fetch, Pull, Push, Refresh, Cancel, Init, Lfs, Lock }

    /// <summary>
    /// Loads the icons and puts them on controls, before the text. The PNGs are drawn by Tools~/make-icons.ps1 and are
    /// white so that GitWindow.uss can tint them like the control's text.
    /// </summary>
    internal static class GitIcons
    {
        /// <summary>The icon files, named by the lower-case <see cref="GitIcon"/> name.</summary>
        public const string Folder = "Packages/com.bloknoter.unitygit/Editor/UI/Icons";

        /// <summary>The class on an icon <see cref="Apply(ToolbarMenu, GitIcon)"/> adds to a menu.</summary>
        public const string IconClass = "unitygit-icon";

        public static string PathOf(GitIcon icon) => $"{Folder}/{icon.ToString().ToLowerInvariant()}.png";

        /// <summary>The icon's texture, or null when its file is missing.</summary>
        public static Texture2D? Load(GitIcon icon) => AssetDatabase.LoadAssetAtPath<Texture2D>(PathOf(icon));

        /// <summary>Sets the button's icon, which Unity shows before the text. Does nothing when the file is missing.</summary>
        public static void Apply(Button button, GitIcon icon)
        {
            var texture = Load(icon);
            if (texture != null) button.iconImage = Background.FromTexture2D(texture);
        }

        /// <summary>Inserts the icon before the menu's text. Does nothing when the file is missing.</summary>
        public static void Apply(ToolbarMenu menu, GitIcon icon)
        {
            var texture = Load(icon);
            if (texture == null) return;
            var image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList(IconClass);
            var text = menu.Q<TextElement>(className: ToolbarMenu.textUssClassName);
            menu.Insert(text != null ? menu.IndexOf(text) : 0, image);
        }
    }
}
