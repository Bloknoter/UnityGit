#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityGit.UI
{
    /// <summary>One line of the Changes list: a file row, or a folder above the files under it.</summary>
    internal sealed class ChangeEntry
    {
        ChangeEntry(string key, bool isFolder, string label, ChangeItem? item, IReadOnlyList<string> files, string? metaRow)
        {
            Key = key;
            IsFolder = isFolder;
            Label = label;
            Item = item;
            Files = files;
            MetaRow = metaRow;
        }

        internal static ChangeEntry ForFile(ChangeItem item)
            => new ChangeEntry(item.Path, false, item.Path, item, new[] { item.Path }, null);

        internal static ChangeEntry ForFolder(string key, IReadOnlyList<string> files, string? metaRow)
            => new ChangeEntry(key, true, key.Substring(0, key.Length - 1), null, files, metaRow);

        /// <summary>A file's <see cref="ChangeItem.Path"/>; a folder's path with a trailing '/' ("Assets/Editor/"), so the two never collide.</summary>
        public string Key { get; }

        public bool IsFolder { get; }

        /// <summary>The full path shown: the file's path, or the folder's without the trailing '/'.</summary>
        public string Label { get; }

        /// <summary>The file's row; null for a folder.</summary>
        public ChangeItem? Item { get; }

        /// <summary>The file rows this line ticks: a file's own path, or every row under a folder (folded .meta rows included), in row order.</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>A folder's own "X.meta" row folded into it, or null. It shows as the ".meta" chip.</summary>
        public string? MetaRow { get; }
    }

    /// <summary>Builds the Changes list's lines from its file rows, with or without folder lines.</summary>
    internal static class ChangeTree
    {
        // Core's MetaPairing.MetaSuffix, which UnityGit.UI can't see.
        const string MetaSuffix = ".meta";

        /// <summary>
        /// Hidden folders: one line per row, in the rows' order. Shown: a line for every folder level of every row's path as
        /// well, everything sorted by key ignoring case (the rows' own sort), so each folder sits right above its contents and
        /// the file rows keep their order. A folder's .meta row is folded into that folder's line unless it is conflicted.
        /// </summary>
        public static IReadOnlyList<ChangeEntry> Build(IReadOnlyList<ChangeItem> rows, bool showFolders)
        {
            if (!showFolders) return rows.Select(ChangeEntry.ForFile).ToList();

            // The folders a row's path is in. A row may itself end in '/' (git lists an untracked nested repository that way):
            // it stays a file row, and its key is never also a folder's, so keys stay unique.
            var folders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
                for (var i = row.Path.IndexOf('/'); i >= 0 && i < row.Path.Length - 1; i = row.Path.IndexOf('/', i + 1))
                    folders.Add(row.Path.Substring(0, i + 1));
            folders.ExceptWith(rows.Select(r => r.Path));

            // A folder's .meta sits beside the folder; it is folded into the folder's line, which ticks it with its contents.
            var metaOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var folded = new Dictionary<string, string>(StringComparer.Ordinal); // meta row → folder key
            foreach (var row in rows)
            {
                if (row.IsConflicted || !row.Path.EndsWith(MetaSuffix, StringComparison.Ordinal)) continue;
                var folder = row.Path.Substring(0, row.Path.Length - MetaSuffix.Length) + "/";
                if (!folders.Contains(folder)) continue;
                metaOf[folder] = row.Path;
                folded[row.Path] = folder;
            }

            var files = folders.ToDictionary(f => f, _ => new List<string>(), StringComparer.Ordinal);
            foreach (var row in rows)
            {
                // A folded .meta belongs to its folder as well as to the folders its own path is in.
                var under = folded.TryGetValue(row.Path, out var own) ? own : row.Path;
                for (var i = under.IndexOf('/'); i >= 0; i = under.IndexOf('/', i + 1))
                    if (files.TryGetValue(under.Substring(0, i + 1), out var list)) list.Add(row.Path);
            }

            var entries = folders.Select(f => ChangeEntry.ForFolder(f, files[f], metaOf.TryGetValue(f, out var meta) ? meta : null))
                .Concat(rows.Where(r => !folded.ContainsKey(r.Path)).Select(ChangeEntry.ForFile));
            return entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
