#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace UnityGit.UI
{
    /// <summary>Unchanged text around a conflict place: lines shown as they are, or a run of lines collapsed to a count.</summary>
    internal readonly struct ContextPiece
    {
        ContextPiece(string? text, int collapsed, int line)
        {
            Text = text;
            Collapsed = collapsed;
            Line = line;
        }

        /// <summary>The lines shown, with their line breaks; null for a collapsed run.</summary>
        public string? Text { get; }

        /// <summary>How many lines the run hides; 0 for shown lines.</summary>
        public int Collapsed { get; }

        /// <summary>The 0-based index, within the unchanged run, of the piece's first line (of a collapsed run, its first hidden one).</summary>
        public int Line { get; }

        public static ContextPiece Lines(string text, int line) => new ContextPiece(text, 0, line);
        public static ContextPiece Run(int lines, int line) => new ContextPiece(null, lines, line);
    }

    /// <summary>One line of the file as the conflict preview shows it: its 1-based line number on disk and its text.</summary>
    internal readonly struct CodeRow
    {
        public CodeRow(int line, string text)
        {
            Line = line;
            Text = text;
        }

        public int Line { get; }
        public string Text { get; }
    }

    /// <summary>
    /// What the conflict preview shows of a file (spec §5.5): a few unchanged lines around each place, at most
    /// <see cref="MaxSideLines"/> lines and <see cref="MaxBlockChars"/> characters of a side, no line longer than
    /// <see cref="MaxLineChars"/>, and no CR characters. Large files (a text-serialized mesh has single lines of hundreds
    /// of KB) never reach a label whole, so the editor doesn't freeze laying them out.
    /// </summary>
    internal static class ConflictText
    {
        public const int ContextLines = 3;
        public const int MaxSideLines = 200;

        /// <summary>A longer line shows its first this many characters and "…".</summary>
        public const int MaxLineChars = 1000;

        /// <summary>The most characters <see cref="Rows"/> shows of one block; the lines after it are counted instead.</summary>
        public const int MaxBlockChars = 20000;

        // A run is collapsed only when it hides at least this many lines: "⋯ 1 unchanged line" takes a line itself.
        const int MinCollapsed = 2;

        /// <summary>How many lines the file has for this text: a trailing line break doesn't start a line, "" has none, CR is ignored.</summary>
        public static int LineCount(string text) => CountLines(text, 0);

        /// <summary>
        /// The line on disk where each side of the place starts, the conflict markers counted: A after <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>,
        /// the base after <c>|||||||</c> and B after <c>=======</c>. Base means nothing when the place has none.
        /// </summary>
        public static (int Ours, int Base, int Theirs) SideStarts(ConflictPlace place)
            => (place.StartLine + 1, place.StartLine + LineCount(place.Ours) + 2, place.EndLine - LineCount(place.Theirs));

        /// <summary>
        /// The rows to show of text that runs in segments, each numbered from its own first line: CR dropped, each line cut to
        /// <see cref="MaxLineChars"/>, at most <paramref name="maxLines"/> rows and <see cref="MaxBlockChars"/> characters
        /// (a line break between rows counts as one; the first row always fits), and how many lines are left out. A trailing
        /// line break doesn't start another row.
        /// </summary>
        public static (IReadOnlyList<CodeRow> Rows, int Hidden) Rows(IReadOnlyList<(string Text, int FirstLine)> segments, int maxLines = MaxSideLines)
        {
            if (maxLines < 1) throw new ArgumentOutOfRangeException(nameof(maxLines));
            var rows = new List<CodeRow>();
            var line = new StringBuilder();
            var chars = 0;
            var hidden = 0;
            var full = false;
            foreach (var (text, firstLine) in segments)
            {
                if (full)
                {
                    hidden += LineCount(text);
                    continue;
                }
                var number = firstLine;
                for (var start = 0; start < text.Length;)
                {
                    var end = text.IndexOf('\n', start);
                    var stop = end < 0 ? text.Length : end;
                    var shown = rows.Count < maxLines ? ShownLine(line, text, start, stop) : null;
                    if (shown == null || (rows.Count > 0 && chars + 1 + shown.Length > MaxBlockChars))
                    {
                        full = true;
                        hidden += CountLines(text, start);
                        break;
                    }
                    chars += (rows.Count > 0 ? 1 : 0) + shown.Length;
                    rows.Add(new CodeRow(number++, shown));
                    start = end < 0 ? text.Length : end + 1;
                }
            }
            return (rows, hidden);
        }

        /// <summary>
        /// The pieces to show of an unchanged run between places: its first <see cref="ContextLines"/> lines when a place
        /// comes before it, its last ones when a place comes after it, and the lines between collapsed into a count. Lines
        /// longer than <see cref="MaxLineChars"/> are cut.
        /// </summary>
        public static IReadOnlyList<ContextPiece> Context(string text, bool placeBefore, bool placeAfter)
        {
            var starts = LineStarts(text);
            var count = starts.Count;
            if (count == 0) return Array.Empty<ContextPiece>();

            var head = placeBefore ? ContextLines : 0;
            var tail = placeAfter ? ContextLines : 0;
            var hidden = count - head - tail;
            if (hidden < MinCollapsed) return new[] { ContextPiece.Lines(CutLongLines(text), 0) };

            var pieces = new List<ContextPiece>(3);
            if (head > 0) pieces.Add(ContextPiece.Lines(CutLongLines(text.Substring(0, starts[head])), 0));
            pieces.Add(ContextPiece.Run(hidden, head));
            if (tail > 0) pieces.Add(ContextPiece.Lines(CutLongLines(text.Substring(starts[count - tail])), count - tail));
            return pieces;
        }

        /// <summary>"⋯ 4 unchanged lines": a collapsed run of unchanged text.</summary>
        public static string Unchanged(int lines)
            => lines == 1 ? "⋯ 1 unchanged line" : $"⋯ {PanelParts.Count(lines)} unchanged lines";

        /// <summary>"⋯ 51 more lines — open the file to see all": what <see cref="Rows"/> left out of a side.</summary>
        public static string MoreLines(int lines)
            => $"⋯ {PanelParts.Count(lines)} more {(lines == 1 ? "line" : "lines")} — open the file to see all";

        // The text with each line longer than MaxLineChars cut, line breaks kept. The same string when nothing is cut.
        static string CutLongLines(string text)
        {
            if (text.Length <= MaxLineChars) return text;
            var shown = new StringBuilder();
            var cut = false;
            for (var start = 0; start < text.Length;)
            {
                var end = text.IndexOf('\n', start);
                cut |= AppendLine(shown, text, start, (end < 0 ? text.Length : end) - start);
                if (end < 0) break;
                shown.Append('\n');
                start = end + 1;
            }
            return cut ? shown.ToString() : text;
        }

        // Appends one line (without its line break), cut to MaxLineChars characters and "…" when longer. True when cut.
        static bool AppendLine(StringBuilder shown, string text, int start, int length)
        {
            if (length <= MaxLineChars)
            {
                shown.Append(text, start, length);
                return false;
            }
            var keep = char.IsHighSurrogate(text[start + MaxLineChars - 1]) ? MaxLineChars - 1 : MaxLineChars; // a whole character
            shown.Append(text, start, keep).Append('…');
            return true;
        }

        // The line from start to stop as a row shows it: CR dropped, cut as AppendLine cuts. Reads at most MaxLineChars + 1
        // characters (plus any CRs), so a line of hundreds of KB costs no more than a short one.
        static string ShownLine(StringBuilder line, string text, int start, int stop)
        {
            line.Clear();
            for (var i = start; i < stop; i++)
            {
                var c = text[i];
                if (c == '\r') continue;
                if (line.Length == MaxLineChars)
                {
                    if (char.IsHighSurrogate(line[line.Length - 1])) line.Length--; // a whole character
                    return line.Append('…').ToString();
                }
                line.Append(c);
            }
            return line.ToString();
        }

        // The lines from start on: a trailing line break doesn't start another one.
        static int CountLines(string text, int start)
        {
            if (start >= text.Length) return 0;
            var breaks = 0;
            for (var i = text.IndexOf('\n', start); i >= 0; i = text.IndexOf('\n', i + 1)) breaks++;
            return text[text.Length - 1] == '\n' ? breaks : breaks + 1;
        }

        // Where each line begins. A trailing line break doesn't begin another line.
        static List<int> LineStarts(string text)
        {
            var starts = new List<int>();
            if (text.Length == 0) return starts;
            starts.Add(0);
            for (var i = text.IndexOf('\n'); i >= 0 && i + 1 < text.Length; i = text.IndexOf('\n', i + 1)) starts.Add(i + 1);
            return starts;
        }
    }
}
