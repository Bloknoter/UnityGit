#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityGit.UI
{
    internal enum MediaKind { None, Image, Audio }

    /// <summary>
    /// Recognises images and audio for the Changes preview (spec §5.4). Unity decides, not git: a file Unity imported as a
    /// texture is an image and one imported as an audio clip is audio, whatever its format. A file Unity hasn't imported
    /// (outside the project, or not imported yet) is an image only when Unity can decode it itself (PNG, JPG).
    /// </summary>
    internal static class PreviewMedia
    {
        static readonly string[] DecodableImages = { ".png", ".jpg", ".jpeg" };

        public static (MediaKind Kind, string? AssetPath) Detect(string fullPath)
        {
            var assetPath = AssetPathOf(fullPath);
            var type = assetPath != null ? AssetDatabase.GetMainAssetTypeAtPath(assetPath) : null;
            if (type != null)
            {
                if (typeof(Texture).IsAssignableFrom(type)) return (MediaKind.Image, assetPath);
                if (typeof(AudioClip).IsAssignableFrom(type)) return (MediaKind.Audio, assetPath);
                return (MediaKind.None, assetPath);
            }
            return IsDecodableImage(fullPath) ? (MediaKind.Image, (string?)null) : (MediaKind.None, (string?)null);
        }

        /// <summary>
        /// The image to show and its size in pixels (the source file's, not the imported texture's). <paramref name="owned"/>
        /// is true for a texture decoded here, which the caller destroys; an asset's texture belongs to the AssetDatabase.
        /// Null when it can't be loaded.
        /// </summary>
        public static Texture? LoadImage(string fullPath, string? assetPath, out int width, out int height, out bool owned)
        {
            width = height = 0;
            owned = false;
            if (assetPath != null)
            {
                var asset = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
                if (asset != null)
                {
                    width = asset.width;
                    height = asset.height;
                    if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
                        importer.GetSourceTextureWidthAndHeight(out width, out height);
                    return asset;
                }
            }
            if (!IsDecodableImage(fullPath) || !File.Exists(fullPath)) return null;

            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            if (!texture.LoadImage(File.ReadAllBytes(fullPath)))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }
            width = texture.width;
            height = texture.height;
            owned = true;
            return texture;
        }

        public static AudioClip? LoadAudio(string assetPath) => AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);

        // "Assets/..." for a file in the project's Assets, "Packages/<name>/..." for one in a package; null otherwise.
        static string? AssetPathOf(string fullPath)
        {
            var full = Path.GetFullPath(fullPath).Replace('\\', '/');
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            var relative = full.StartsWith(project, StringComparison.OrdinalIgnoreCase) ? full.Substring(project.Length) : null;
            if (relative != null && relative.StartsWith("Assets/", StringComparison.Ordinal)) return relative;

            var logical = FileUtil.GetLogicalPath(full);
            if (logical != null && logical.StartsWith("Packages/", StringComparison.Ordinal)) return logical;
            return relative != null && relative.StartsWith("Packages/", StringComparison.Ordinal) ? relative : null;
        }

        static bool IsDecodableImage(string fullPath)
            => Array.IndexOf(DecodableImages, Path.GetExtension(fullPath).ToLowerInvariant()) >= 0;
    }
}
