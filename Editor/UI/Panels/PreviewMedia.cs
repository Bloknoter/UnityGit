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
            var assetPath = AssetPaths.FromFullPath(fullPath);
            var type = assetPath != null ? AssetDatabase.GetMainAssetTypeAtPath(assetPath) : null;
            if (type != null)
            {
                // An image file, imported by Unity's texture importers. Render textures, cubemap and 3D texture assets are
                // textures too, but text-serialized settings: they get their diff.
                if (typeof(Texture).IsAssignableFrom(type) && IsImageImporter(AssetImporter.GetAtPath(assetPath)))
                    return (MediaKind.Image, assetPath);
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

        static bool IsImageImporter(AssetImporter? importer) => importer is TextureImporter || importer is IHVImageFormatImporter;

        static bool IsDecodableImage(string fullPath)
            => Array.IndexOf(DecodableImages, Path.GetExtension(fullPath).ToLowerInvariant()) >= 0;
    }
}
