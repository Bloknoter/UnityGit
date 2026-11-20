#nullable enable
using System;
using System.Collections.Generic;

namespace UnityGit.UI
{
    /// <summary>Unchanged text around a conflict place: lines shown as they are, or a run of lines collapsed to a count.</summary>
    internal readonly struct ContextPiece
    {
        ContextPiece(string? text, int collapsed)
        {
            Text = text;
            Collapsed = collapsed;
        }

        /// <summary>The lines shown, with their line breaks; null for a collapsed run.</summary>
        public string? Text { get; }

        /// <summary>How many lines the run hides; 0 for shown lines.</summary>
        public int Collapsed { get; }

        public static ContextPiece Lines(string text) => new ContextPiece(text, 0);
        public static ContextPiece Run(int lines) => new ContextPiece(null, lines);
    }

    /// <summary>
    /// What the conflict preview shows of a file (spec §5.5): a few unchanged lines around each place, at most
    /// <see cref="MaxSideLines"/> lines of a side, and no CR characters. Large files never reach a label whole, so the
    /// editor doesn't freeze laying them out.
    /// </summary>
    internal static class ConflictText
    {
        public const int ContextLines = 3;
        public const int MaxSideLines = 200;

        // A run is collapsed only when it hides at least this many lines: "⋯ 1 unchanged line" takes a line itself.
        const int MinCollapsed = 2;

        /// <summary>The text for a label: CR characters dropped, and one trailing line break, which would show as an empty line.</summary>
        public static string ForDisplay(string text)
        {
            var shown = text.Replace("\r", string.Empty);
            return shown.EndsWith("\n", StringComparison.Ordinal) ? shown.Substring(0, shown.Length - 1) : shown;
        }

        /// <summary>
        /// The first <paramref name="maxLines"/> lines of <paramref name="text"/> (a <see cref="ForDisplay"/> result), and
        /// how many lines are left out. One trailing line break doesn't start another line.
        /// </summary>
        public static (string Shown, int Hidden) Cap(string text, int maxLines = MaxSideLines)
        {
            if (maxLines < 1) throw new ArgumentOutOfRangeException(nameof(maxLines));
            var end = -1;
            for (var i = 0; i < maxLines; i++)
            {
                end = text.IndexOf('\n', end + 1);
                if (end < 0) return (text, 0);
            }
            var hidden = CountLines(text, end + 1);
            return hidden == 0 ? (text, 0) : (text.Substring(0, end), hidden);
        }

        /// <summary>
        /// The pieces to show of an unchanged run between places: its first <see cref="ContextLines"/> lines when a place
        /// comes before it, its last ones when a place comes after it, and the lines between collapsed into a count.
        /// </summary>
        public static IReadOnlyList<ContextPiece> Context(string text, bool placeBefore, bool placeAfter)
        {
            var starts = LineStarts(text);
            var count = starts.Count;
            if (count == 0) return Array.Empty<ContextPiece>();

            var head = placeBefore ? ContextLines : 0;
            var tail = placeAfter ? ContextLines : 0;
            var hidden = count - head - tail;
            if (hidden < MinCollapsed) return new[] { ContextPiece.Lines(text) };

            var pieces = new List<ContextPiece>(3);
            if (head > 0) pieces.Add(ContextPiece.Lines(text.Substring(0, starts[head])));
            pieces.Add(ContextPiece.Run(hidden));
            if (tail > 0) pieces.Add(ContextPiece.Lines(text.Substring(starts[count - tail])));
            return pieces;
        }

        /// <summary>"⋯ 4 unchanged lines": a collapsed run of unchanged text.</summary>
        public static string Unchanged(int lines)
            => lines == 1 ? "⋯ 1 unchanged line" : $"⋯ {PanelParts.Count(lines)} unchanged lines";

        /// <summary>"⋯ 51 more lines — open the file to see all": what <see cref="Cap"/> left out of a side.</summary>
        public static string MoreLines(int lines)
            => $"⋯ {PanelParts.Count(lines)} more {(lines == 1 ? "line" : "lines")} — open the file to see all";

        // The lines from start on, counted as Cap counts them.
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
