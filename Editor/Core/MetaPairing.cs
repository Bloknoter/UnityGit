#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityGit
{
    internal sealed class FileChange
    {
        public string Path { get; set; } = "";
        public string? OriginalPath { get; set; }
        public ChangeKind Kind { get; set; }
        public bool IsConflicted { get; set; }
        public bool IsStaged { get; set; }
    }

    internal static class MetaPairing
    {
        public const string MetaSuffix = ".meta";

        // Folds each "X.meta" change into the row of "X"; unpaired changes stay rows of their own.
        public static IReadOnlyList<ChangeItem> Group(IReadOnlyList<FileChange> changes)
        {
            var byPath = new Dictionary<string, FileChange>(StringComparer.Ordinal);
            foreach (var c in changes) byPath[c.Path] = c;

            var metaOf = new Dictionary<FileChange, FileChange>();
            var pairedMetas = new HashSet<FileChange>();
            foreach (var c in changes)
            {
                if (!c.Path.EndsWith(MetaSuffix, StringComparison.Ordinal)) continue;
                var assetPath = c.Path.Substring(0, c.Path.Length - MetaSuffix.Length);
                if (assetPath.Length > 0 && byPath.TryGetValue(assetPath, out var asset)
                    && !ReferenceEquals(asset, c) && !metaOf.ContainsKey(asset))
                {
                    metaOf[asset] = c;
                    pairedMetas.Add(c);
                }
            }

            var rows = new List<ChangeItem>();
            foreach (var c in changes)
            {
                if (pairedMetas.Contains(c)) continue;
                metaOf.TryGetValue(c, out var meta);

                var paths = new List<string> { c.Path };
                if (c.OriginalPath != null) paths.Add(c.OriginalPath);
                if (meta != null)
                {
                    paths.Add(meta.Path);
                    if (meta.OriginalPath != null) paths.Add(meta.OriginalPath);
                }

                rows.Add(new ChangeItem
                {
                    Path = c.Path,
                    GitPaths = paths,
                    Kind = c.Kind,
                    OriginalPath = c.OriginalPath,
                    IsConflicted = c.IsConflicted || (meta?.IsConflicted ?? false),
                    IsStaged = c.IsStaged || (meta?.IsStaged ?? false),
                    HasMeta = meta != null,
                });
            }

            return rows.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
