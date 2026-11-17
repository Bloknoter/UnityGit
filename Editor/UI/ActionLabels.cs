#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityGit.UI
{
    /// <summary>Button and menu labels shared by the window and the main-toolbar buttons.</summary>
    internal static class ActionLabels
    {
        public static string Pull(RepoSnapshot s) => s.Behind > 0 ? $"Pull ↓{s.Behind}" : "Pull";

        public static string Push(RepoSnapshot s) => s.Ahead > 0 ? $"Push ↑{s.Ahead}" : "Push";

        public static string Branch(RepoSnapshot s)
        {
            switch (s.Availability)
            {
                case RepoAvailability.Ready:
                    return s.IsDetached ? $"Detached at {ShortSha(s.HeadSha)}" : s.Branch ?? string.Empty;
                case RepoAvailability.NotARepository:
                    return "No repository";
                case RepoAvailability.GitMissing:
                    return "Git missing";
                default:
                    return "Loading…";
            }
        }

        /// <summary>
        /// The branches the user can switch to: local branch names, then the short names of remote branches that have
        /// no local branch of that name (switching to one creates a local tracking branch). Each group is sorted ordinally.
        /// </summary>
        public static IReadOnlyList<string> BranchMenuItems(RepoSnapshot s)
        {
            var items = s.LocalBranches.Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var local = new HashSet<string>(items, StringComparer.Ordinal);
            items.AddRange(s.RemoteBranches.Select(b => b.ShortName)
                .Where(n => !local.Contains(n))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal));
            return items;
        }

        static string ShortSha(string? sha)
            => sha == null ? string.Empty : sha.Length > 7 ? sha.Substring(0, 7) : sha;
    }
}
