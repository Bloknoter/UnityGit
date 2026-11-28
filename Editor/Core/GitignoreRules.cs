#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnityGit
{
    /// <summary>The Add rule menu's kinds.</summary>
    public enum GitignoreRuleKind { Extension, Folder, File, Custom }

    /// <summary>The lines a rule adds (its comment first), or why it can't be added.</summary>
    public sealed class GitignoreRuleResult
    {
        GitignoreRuleResult(IReadOnlyList<string> lines, string? error)
        {
            Lines = lines;
            Error = error;
        }

        /// <summary>Empty when <see cref="Error"/> is set.</summary>
        public IReadOnlyList<string> Lines { get; }

        public string? Error { get; }

        internal static GitignoreRuleResult Of(IReadOnlyList<string> lines) => new GitignoreRuleResult(lines, null);

        internal static GitignoreRuleResult Fail(string error) => new GitignoreRuleResult(Array.Empty<string>(), error);
    }

    /// <summary>
    /// Turns what the user typed in the Add rule window into .gitignore lines (spec "Add rule"). Paths are written relative
    /// to the .gitignore's folder with a leading '/', and glob characters in them are escaped, so a rule matches exactly the
    /// folder or file picked.
    /// </summary>
    public static class GitignoreRules
    {
        static readonly StringComparison PathComparison =
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>"# text", or null for a blank comment. A '#' typed at its start is dropped; line breaks become spaces.</summary>
        public static string? CommentLine(string? comment)
        {
            if (comment == null) return null;
            var text = comment.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim().TrimStart('#').Trim();
            return text.Length == 0 ? null : "# " + text;
        }

        /// <summary>A literal name as a pattern: '\' before * ? [ and \, and before each trailing space (git drops those).</summary>
        public static string Escape(string literal)
        {
            var trailingFrom = literal.TrimEnd(' ').Length;
            var sb = new StringBuilder(literal.Length + 4);
            for (var i = 0; i < literal.Length; i++)
            {
                var c = literal[i];
                if (c is '*' or '?' or '[' or '\\' || (c == ' ' && i >= trailingFrom)) sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Whether the "Also ignore .meta" toggle applies: always for an extension, never for a custom rule, and for a
        /// folder or file only inside the Unity project's Assets or Packages folder.
        /// </summary>
        public static bool MetaApplies(GitignoreRuleKind kind, string value, string gitignoreFolder, string projectPath)
        {
            switch (kind)
            {
                case GitignoreRuleKind.Extension: return true;
                case GitignoreRuleKind.Custom: return false;
            }
            var full = FullPath((value ?? string.Empty).Trim(), gitignoreFolder);
            if (full == null) return false;
            try
            {
                return IsInsideOrEqual(full, Path.Combine(projectPath, "Assets")) || IsInsideOrEqual(full, Path.Combine(projectPath, "Packages"));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static GitignoreRuleResult Build(GitignoreRuleKind kind, string comment, string value, bool negate, bool includeMeta,
            string gitignoreFolder, string projectPath)
        {
            value ??= string.Empty;
            var rules = new List<string>();
            string? meta = null;
            switch (kind)
            {
                case GitignoreRuleKind.Custom:
                {
                    var lines = value.Replace("\r\n", "\n").Split('\n').ToList();
                    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
                    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1])) lines.RemoveAt(lines.Count - 1);
                    if (lines.Count == 0) return GitignoreRuleResult.Fail("Enter a rule");
                    return GitignoreRuleResult.Of(WithComment(comment, lines)); // as typed: no Negate, no .meta
                }

                case GitignoreRuleKind.Extension:
                {
                    var ext = value.Trim();
                    if (ext.StartsWith("*", StringComparison.Ordinal)) ext = ext.Substring(1);
                    if (ext.StartsWith(".", StringComparison.Ordinal)) ext = ext.Substring(1);
                    if (ext.Length == 0) return GitignoreRuleResult.Fail("Enter an extension");
                    if (ext.IndexOf('/') >= 0) return GitignoreRuleResult.Fail("An extension can't contain /");
                    var pattern = "*." + Escape(ext);
                    rules.Add(pattern);
                    meta = pattern + ".meta";
                    break;
                }

                default: // Folder, File
                {
                    var typed = value.Trim();
                    if (typed.Length == 0) return GitignoreRuleResult.Fail(kind == GitignoreRuleKind.Folder ? "Enter a folder path" : "Enter a file path");
                    var full = FullPath(typed, gitignoreFolder);
                    if (full == null) return GitignoreRuleResult.Fail("Not a valid path");
                    var relative = RelativeInside(full, gitignoreFolder);
                    if (relative == null) return GitignoreRuleResult.Fail("Pick a path inside " + Display(gitignoreFolder));
                    var pattern = "/" + Escape(relative);
                    rules.Add(kind == GitignoreRuleKind.Folder ? pattern + "/" : pattern);
                    meta = pattern + ".meta";
                    break;
                }
            }

            if (includeMeta && meta != null && MetaApplies(kind, value, gitignoreFolder, projectPath)) rules.Add(meta);
            if (negate) rules = rules.Select(r => "!" + r).ToList();
            return GitignoreRuleResult.Of(WithComment(comment, rules));
        }

        static List<string> WithComment(string comment, List<string> rules)
        {
            var lines = new List<string>();
            if (CommentLine(comment) is string line) lines.Add(line);
            lines.AddRange(rules);
            return lines;
        }

        // A typed path's full path, or null when it isn't a valid path. An absolute path counts as absolute when it's inside
        // the folder or exists; otherwise it's read relative to the folder, so "/Assets/Foo" means the folder's Assets/Foo
        // the way .gitignore patterns do, on every OS.
        static string? FullPath(string value, string folder)
        {
            try
            {
                var root = Path.GetFullPath(folder);
                if (Path.IsPathFullyQualified(value))
                {
                    var absolute = Path.GetFullPath(value);
                    if (IsInsideOrEqual(absolute, root) || Directory.Exists(absolute) || File.Exists(absolute)) return absolute;
                }
                return Path.GetFullPath(Path.Combine(root, value.TrimStart('/', '\\')));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        // The path below the folder with '/', or null when it isn't strictly inside it.
        static string? RelativeInside(string full, string folder)
        {
            var relative = Relative(full, folder);
            return relative == null || relative == "." ? null : relative.Replace('\\', '/');
        }

        static bool IsInsideOrEqual(string full, string folder) => Relative(full, folder) != null;

        // Path.GetRelativePath, or null when the path is outside the folder (above it, or on another drive).
        static string? Relative(string full, string folder)
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(full));
            if (Path.IsPathRooted(relative)) return null;
            if (relative == ".." || relative.StartsWith("../", PathComparison) || relative.StartsWith("..\\", PathComparison)) return null;
            return relative;
        }

        static string Display(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/').Replace('\\', '/');
    }
}
