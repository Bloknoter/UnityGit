#nullable enable
using System;
using System.Text.RegularExpressions;

namespace UnityGit
{
    /// <summary>Names for the two sides of a conflict (spec §5.3).</summary>
    internal static class ConflictNames
    {
        internal const int MaxLength = 40;
        static readonly Regex Hex = new Regex("^([0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.Compiled);

        internal static string? Short(string? label)
        {
            var text = label?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (Hex.IsMatch(text!)) return text!.Substring(0, 7);
            return text!.Length > MaxLength ? text.Substring(0, MaxLength - 1) + "…" : text;
        }

        internal static (string Ours, string Theirs) Resolve(RepoOperation operation, string? branch, string? upstream,
            string? upstreamSha, string? mergeHeadSha, string? oursLabel, string? theirsLabel)
        {
            var merging = operation == RepoOperation.Merging;
            var ours = merging && branch != null ? branch : Short(oursLabel) ?? "ours";

            string theirs;
            if (merging && upstream != null && mergeHeadSha != null && mergeHeadSha == upstreamSha) theirs = upstream;
            else theirs = Short(theirsLabel)
                ?? (merging && mergeHeadSha != null ? mergeHeadSha.Substring(0, Math.Min(7, mergeHeadSha.Length)) : "theirs");
            return (ours, theirs);
        }
    }
}
