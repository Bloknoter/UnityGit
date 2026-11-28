#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityGit
{
    /// <summary>What a .gitignore line is, as git reads it.</summary>
    public enum GitignoreLineKind { Blank, Comment, Code }

    public enum GitignoreBlockKind { Comment, Code }

    /// <summary>A run of lines the .gitignore editor shows, moves and removes as one block.</summary>
    public sealed class GitignoreBlock
    {
        public GitignoreBlock(GitignoreBlockKind kind, int startLine, int lineCount)
        {
            Kind = kind;
            StartLine = startLine;
            LineCount = lineCount;
        }

        public GitignoreBlockKind Kind { get; }

        /// <summary>The first line's index in <see cref="GitignoreDocument.Lines"/> (0-based).</summary>
        public int StartLine { get; }

        public int LineCount { get; }
    }

    /// <summary>
    /// A .gitignore's text split into blocks (spec "Block rules"), and the edits the Blocks view makes. Comments and code
    /// are always separate blocks. A code block runs to the next comment line, so blank lines between rules stay in it; a
    /// comment block splits where two or more blank lines separate its lines. The blank lines outside blocks are gaps.
    /// Edits return a new document. Text is LF; the file layer (<see cref="GitignoreFile"/>) keeps the BOM and line endings.
    /// </summary>
    public sealed class GitignoreDocument
    {
        static readonly GitignoreDocument Empty = new GitignoreDocument(new List<string>(), false);

        readonly List<string> _lines;
        readonly List<GitignoreBlock> _blocks;

        GitignoreDocument(List<string> lines, bool endsWithNewline)
        {
            _lines = lines;
            EndsWithNewline = endsWithNewline && lines.Count > 0;
            _blocks = FindBlocks(lines);
        }

        /// <summary>The lines, without their line endings. A final line ending doesn't make an extra empty line.</summary>
        public IReadOnlyList<string> Lines => _lines;

        public IReadOnlyList<GitignoreBlock> Blocks => _blocks;

        public bool EndsWithNewline { get; }

        /// <summary>Reads LF or CRLF text (without a BOM).</summary>
        public static GitignoreDocument Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return Empty;
            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n').ToList();
            var endsWithNewline = normalized.EndsWith("\n", StringComparison.Ordinal);
            if (endsWithNewline) lines.RemoveAt(lines.Count - 1);
            return new GitignoreDocument(lines, endsWithNewline);
        }

        /// <summary>A comment starts with '#' ("\#" and " #" are rules); a blank line is empty or whitespace only.</summary>
        public static GitignoreLineKind KindOf(string line)
        {
            if (line.Length > 0 && line[0] == '#') return GitignoreLineKind.Comment;
            return string.IsNullOrWhiteSpace(line) ? GitignoreLineKind.Blank : GitignoreLineKind.Code;
        }

        /// <summary>The text with LF line endings, and a final one when the document had it.</summary>
        public string ToText() => string.Join("\n", _lines) + (EndsWithNewline ? "\n" : string.Empty);

        /// <summary>
        /// Deletes block <paramref name="block"/>. The first block keeps the blank lines above it, the last block those
        /// after it; in between, the larger of the two gaps around it stays.
        /// </summary>
        public GitignoreDocument Remove(int block)
        {
            var s = Segments.Of(this);
            s.RemoveAt(block);
            return s.ToDocument(EndsWithNewline);
        }

        /// <summary>
        /// Moves block <paramref name="block"/> to drop position <paramref name="position"/> (0 = above the first block,
        /// Blocks.Count = below the last). The gap at the drop point stays above it and one blank line goes below it (above
        /// it at the bottom). Returns null when the block wouldn't move.
        /// </summary>
        public GitignoreDocument? Move(int block, int position)
        {
            if (block < 0 || block >= _blocks.Count) throw new ArgumentOutOfRangeException(nameof(block));
            if (position < 0 || position > _blocks.Count) throw new ArgumentOutOfRangeException(nameof(position));
            if (position == block || position == block + 1) return null;

            var s = Segments.Of(this);
            var moved = s.Blocks[block];
            s.RemoveAt(block);
            if (position > block) position--;
            s.Insert(position, moved);
            return s.ToDocument(EndsWithNewline);
        }

        /// <summary>
        /// Adds <paramref name="lines"/> (a rule's comment and rule lines) at the end, after one blank line, or two when a
        /// comment follows a comment. Blank lines at either end of <paramref name="lines"/> are dropped.
        /// </summary>
        public GitignoreDocument Append(IReadOnlyList<string> lines)
        {
            var added = lines.SkipWhile(IsBlank).Reverse().SkipWhile(IsBlank).Reverse().ToList();
            if (added.Count == 0) return this;
            if (_blocks.Count == 0) return new GitignoreDocument(added, true);

            var s = Segments.Of(this);
            var commentAfterComment = s.Blocks[s.Blocks.Count - 1].Kind == GitignoreBlockKind.Comment
                && KindOf(added[0]) == GitignoreLineKind.Comment;
            s.Gaps[s.Gaps.Count - 1] = Blanks(commentAfterComment ? 2 : 1);
            var result = s.Lines();
            result.AddRange(added);
            return Parse(string.Join("\n", result) + "\n");
        }

        static bool IsBlank(string line) => KindOf(line) == GitignoreLineKind.Blank;

        static List<string> Blanks(int count) => Enumerable.Repeat(string.Empty, count).ToList();

        static List<GitignoreBlock> FindBlocks(List<string> lines)
        {
            var blocks = new List<GitignoreBlock>();
            var i = 0;
            while (i < lines.Count)
            {
                var kind = KindOf(lines[i]);
                if (kind == GitignoreLineKind.Blank)
                {
                    i++;
                    continue;
                }
                var end = i;
                var j = i + 1;
                if (kind == GitignoreLineKind.Comment)
                {
                    // One blank line between comments stays in the block; two or more end it.
                    while (true)
                    {
                        if (j < lines.Count && KindOf(lines[j]) == GitignoreLineKind.Comment) end = j++;
                        else if (j + 1 < lines.Count && IsBlank(lines[j]) && KindOf(lines[j + 1]) == GitignoreLineKind.Comment)
                        {
                            end = j + 1;
                            j += 2;
                        }
                        else break;
                    }
                    blocks.Add(new GitignoreBlock(GitignoreBlockKind.Comment, i, end - i + 1));
                }
                else
                {
                    // Code runs to the next comment line; the blank lines before that comment are a gap.
                    for (; j < lines.Count && KindOf(lines[j]) != GitignoreLineKind.Comment; j++)
                        if (KindOf(lines[j]) == GitignoreLineKind.Code) end = j;
                    blocks.Add(new GitignoreBlock(GitignoreBlockKind.Code, i, end - i + 1));
                }
                i = end + 1;
            }
            return blocks;
        }

        /// <summary>The document as gap, block, gap, …, block, gap: the form the edits work on.</summary>
        sealed class Segments
        {
            public readonly List<List<string>> Gaps = new List<List<string>>();
            public readonly List<(GitignoreBlockKind Kind, List<string> Lines)> Blocks = new List<(GitignoreBlockKind, List<string>)>();

            public static Segments Of(GitignoreDocument d)
            {
                var s = new Segments();
                var next = 0;
                foreach (var b in d._blocks)
                {
                    s.Gaps.Add(d._lines.GetRange(next, b.StartLine - next));
                    s.Blocks.Add((b.Kind, d._lines.GetRange(b.StartLine, b.LineCount)));
                    next = b.StartLine + b.LineCount;
                }
                s.Gaps.Add(d._lines.GetRange(next, d._lines.Count - next));
                return s;
            }

            public void RemoveAt(int block)
            {
                if (block < 0 || block >= Blocks.Count) throw new ArgumentOutOfRangeException(nameof(block));
                var last = Blocks.Count - 1;
                if (block == 0) Gaps.RemoveAt(1); // the gap above stays (with one block, both gaps stay; only blanks are left)
                else if (block == last) Gaps.RemoveAt(block); // the gap after it stays
                else
                {
                    if (Gaps[block + 1].Count > Gaps[block].Count) Gaps[block] = Gaps[block + 1];
                    Gaps.RemoveAt(block + 1);
                }
                Blocks.RemoveAt(block);
            }

            // Into the block list of the document without the moved block.
            public void Insert(int position, (GitignoreBlockKind, List<string>) block)
            {
                if (position == 0)
                {
                    Blocks.Insert(0, block);
                    Gaps.Insert(1, Blanks(1)); // the top gap stays above it
                }
                else if (position == Blocks.Count)
                {
                    Blocks.Add(block);
                    Gaps.Insert(Gaps.Count - 1, Blanks(1)); // the end gap stays after it
                }
                else
                {
                    Blocks.Insert(position, block);
                    Gaps.Insert(position + 1, Blanks(1)); // the drop point's gap stays above it
                }
            }

            public List<string> Lines()
            {
                var lines = new List<string>(Gaps[0]);
                for (var i = 0; i < Blocks.Count; i++)
                {
                    lines.AddRange(Blocks[i].Lines);
                    lines.AddRange(Gaps[i + 1]);
                }
                return lines;
            }

            public GitignoreDocument ToDocument(bool endsWithNewline)
            {
                if (Blocks.Count == 0) return Empty;
                // Two comment blocks that meet keep two blank lines between them, so they stay separate blocks.
                for (var i = 1; i < Blocks.Count; i++)
                    if (Blocks[i - 1].Kind == GitignoreBlockKind.Comment && Blocks[i].Kind == GitignoreBlockKind.Comment && Gaps[i].Count < 2)
                        Gaps[i] = Gaps[i].Concat(Blanks(2 - Gaps[i].Count)).ToList();
                // Re-parse: code blocks that now touch become one.
                return Parse(string.Join("\n", Lines()) + (endsWithNewline ? "\n" : string.Empty));
            }
        }
    }
}
