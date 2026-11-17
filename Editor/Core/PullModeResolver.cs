#nullable enable
using System;
using FlexGit;

namespace UnityGit
{
    /// <summary>Picks the pull mode from the user's <c>pull.rebase</c> and <c>pull.ff</c> config, as a terminal pull would.</summary>
    internal static class PullModeResolver
    {
        static readonly string[] RebaseValues = { "true", "merges", "interactive", "i", "m" };

        public static PullMode Resolve(string? pullRebase, string? pullFf)
        {
            if (pullRebase != null && Array.Exists(RebaseValues, v => string.Equals(v, pullRebase, StringComparison.OrdinalIgnoreCase)))
                return PullMode.Rebase;
            return pullFf == "only" ? PullMode.FastForwardOnly : PullMode.Merge;
        }
    }
}
