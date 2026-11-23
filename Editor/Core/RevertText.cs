#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace UnityGit
{
    /// <summary>
    /// The words of Revert's confirm dialog, and of the menu item when only conflicted files were picked. Public because
    /// the UI assembly names <see cref="AllConflicted"/> and doesn't see Core's internals.
    /// </summary>
    public static class RevertText
    {
        /// <summary>Why Revert is disabled when every picked row is conflicted.</summary>
        public const string AllConflicted = "Resolve conflicts in the Conflicts tab";

        const int ListedPaths = 10;

        public static string Title(IReadOnlyList<ChangeItem> targets)
            => targets.Count == 1 ? $"Revert {targets[0].Path}?" : $"Revert {targets.Count} files?";

        /// <summary>
        /// The warning, how many new files are deleted, how many never-committed .meta files of tracked assets are deleted
        /// (<paramref name="newMetas"/>: their assets get new GUIDs, which breaks references to them), the paths when there are
        /// several (the title names a single one), and how many conflicted files are left out.
        /// </summary>
        public static string Message(IReadOnlyList<ChangeItem> targets, int conflictedSkipped, int newMetas = 0)
        {
            var lines = new List<string> { "Your changes will be lost. This can't be undone." };
            var added = targets.Count(t => t.Kind == ChangeKind.Added || t.Kind == ChangeKind.Untracked);
            if (added > 0) lines.Add(added == 1 ? "1 new file will be deleted." : $"{added} new files will be deleted.");
            if (newMetas > 0)
                lines.Add(newMetas == 1
                    ? "1 .meta file that was never committed will be deleted, and Unity will give its asset a new GUID."
                    : $"{newMetas} .meta files that were never committed will be deleted, and Unity will give their assets new GUIDs.");

            if (targets.Count > 1)
            {
                lines.Add(string.Empty);
                lines.AddRange(targets.Take(ListedPaths).Select(t => t.Path));
                if (targets.Count > ListedPaths) lines.Add($"…and {targets.Count - ListedPaths} more");
            }

            if (conflictedSkipped > 0)
            {
                lines.Add(string.Empty);
                lines.Add(conflictedSkipped == 1 ? "1 conflicted file is skipped." : $"{conflictedSkipped} conflicted files are skipped.");
            }
            return string.Join("\n", lines);
        }
    }
}
