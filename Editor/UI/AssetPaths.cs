#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityGit.UI
{
    /// <summary>Turns a file's full path into the path the AssetDatabase knows it by.</summary>
    internal static class AssetPaths
    {
        /// <summary>"Assets/..." for a file in the project's Assets, "Packages/&lt;name&gt;/..." for one in a package; null otherwise.</summary>
        public static string? FromFullPath(string fullPath)
        {
            var full = Path.GetFullPath(fullPath).Replace('\\', '/');
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            var relative = full.StartsWith(project, StringComparison.OrdinalIgnoreCase) ? full.Substring(project.Length) : null;
            if (relative != null && relative.StartsWith("Assets/", StringComparison.Ordinal)) return relative;

            var logical = FileUtil.GetLogicalPath(full);
            if (logical != null && logical.StartsWith("Packages/", StringComparison.Ordinal)) return logical;
            return relative != null && relative.StartsWith("Packages/", StringComparison.Ordinal) ? relative : null;
        }
    }
}
