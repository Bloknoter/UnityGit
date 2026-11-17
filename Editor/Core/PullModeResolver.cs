#nullable enable
using System;
using FlexGit;

namespace UnityGit
{
    /// <summary>Picks the pull mode from the user's <c>pull.rebase</c> and <c>pull.ff</c> config, as a terminal pull would.</summary>
    internal static class PullModeResolver
    {
        // git reads pull.rebase as a boolean (true, yes, on, 1) or as merges/m/interactive/i. FlexGit has no
        // rebase-merges mode, so merges is a plain rebase.
        static readonly string[] RebaseValues = { "true", "yes", "on", "1", "merges", "m", "interactive", "i" };

        public static PullMode Resolve(string? pullRebase, string? pullFf)
        {
            var rebase = pullRebase?.Trim();
            if (rebase != null && Array.Exists(RebaseValues, v => string.Equals(v, rebase, StringComparison.OrdinalIgnoreCase)))
                return PullMode.Rebase;
            return pullFf == "only" ? PullMode.FastForwardOnly : PullMode.Merge;
        }
    }
}
