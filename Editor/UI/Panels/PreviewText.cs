#nullable enable
using System;
using System.Globalization;

namespace UnityGit.UI
{
    /// <summary>The texts of the Changes preview (spec §5.2).</summary>
    internal static class PreviewText
    {
        public const string Heading = "Preview";
        public const string SelectFile = "Select a file to preview";
        public const string Conflicted = "Conflicted — resolve it in the Conflicts tab";
        public const string NoChanges = "No changes";
        public const string Loading = "Loading…";
        public const string Failed = "Couldn't load the preview";
        public const string EmptyFile = "Empty file";
        public const string NoContentChanges = "No changes in the content";

        /// <summary>
        /// Several lines selected: "3 files selected", or with folders among them "4 files and 1 folder selected". Files are the
        /// file rows the lines stand for (a folder's counted once each); folders are the selected folder lines.
        /// </summary>
        public static string Selected(int files, int folders)
        {
            var fileText = files == 1 ? "1 file" : $"{PanelParts.Count(files)} files";
            if (folders == 0) return fileText + " selected";
            var folderText = folders == 1 ? "1 folder" : $"{PanelParts.Count(folders)} folders";
            return $"{fileText} and {folderText} selected";
        }

        /// <summary>One folder row selected: how many file rows it ticks.</summary>
        public static string Folder(int n) => n == 1
            ? "1 file in this folder — select it to preview"
            : $"{PanelParts.Count(n)} files in this folder — select one to preview";

        /// <summary>"512 B", "12.3 KB", "1.2 MB", "3.4 GB" (1024-based).</summary>
        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            var value = bytes / 1024.0;
            var unit = "KB";
            if (value >= 1024) { value /= 1024; unit = "MB"; }
            if (value >= 1024) { value /= 1024; unit = "GB"; }
            return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit;
        }

        /// <summary>"1.2 MB → 1.4 MB", or the one size there is (a new or deleted file).</summary>
        public static string Sizes(long? oldSize, long? newSize)
        {
            if (oldSize is long before && newSize is long after) return Size(before) + " → " + Size(after);
            if (oldSize is long only) return Size(only);
            return newSize is long size ? Size(size) : string.Empty;
        }

        /// <summary>The header's right side: "+12 −3" for a diff, "N lines" for a new file, the sizes for a binary file.</summary>
        public static string Summary(FileDiffView v)
        {
            if (v.IsBinary || v.IsLfs) return Sizes(v.OldSize, v.NewSize);
            switch (v.Change)
            {
                case DiffChange.None: return string.Empty;
                case DiffChange.Added: return v.AddedCount == 1 ? "1 line" : $"{PanelParts.Count(v.AddedCount)} lines";
                default: return $"+{PanelParts.Count(v.AddedCount)} −{PanelParts.Count(v.RemovedCount)}";
            }
        }

        /// <summary>An audio clip's length as "m:ss.f".</summary>
        public static string Length(float seconds)
        {
            var tenths = (int)Math.Floor(Math.Max(0f, seconds) * 10 + 0.0001);
            var minutes = tenths / 600;
            var rest = tenths % 600;
            return $"{PanelParts.Count(minutes)}:{(rest / 10).ToString("00", CultureInfo.InvariantCulture)}.{PanelParts.Count(rest % 10)}";
        }

        /// <summary>"3 × 2 · 1.2 KB": an image's size in pixels and on disk.</summary>
        public static string ImageSummary(int width, int height, long? bytes)
            => $"{PanelParts.Count(width)} × {PanelParts.Count(height)}" + (bytes is long size ? " · " + Size(size) : string.Empty);

        /// <summary>The path, or "old → new" for a rename.</summary>
        public static string Title(FileDiffView v) => v.OldPath != null ? v.OldPath + " → " + v.Path : v.Path;

        /// <summary>What to say instead of rows, or null when the file has rows to show.</summary>
        public static string? Note(FileDiffView v)
        {
            if (v.Change == DiffChange.None) return NoChanges;
            if (v.IsBinary || v.IsLfs)
            {
                if (v.Change == DiffChange.Deleted) return v.OldSize is long size ? "Deleted · " + Size(size) : "Deleted";
                return (v.IsLfs ? "Stored in Git LFS · " : "Binary file · ") + Sizes(v.OldSize, v.NewSize);
            }
            if (v.Rows.Count == 0 && v.HiddenLines == 0) return v.Change == DiffChange.Added ? EmptyFile : NoContentChanges;
            return null;
        }
    }
}
