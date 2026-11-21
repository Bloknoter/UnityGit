#nullable enable
using System.Linq;
using NUnit.Framework;
using UnityGit.UI;

namespace UnityGit.Tests
{
    public class ConflictTextTests
    {
        // "l{from}\n" … "l{to}\n"
        static string Lines(int from, int to) => string.Concat(Enumerable.Range(from, to - from + 1).Select(i => $"l{i}\n"));

        static (string?, int)[] Pieces(string text, bool placeBefore, bool placeAfter)
            => ConflictText.Context(text, placeBefore, placeAfter).Select(p => (p.Text, p.Collapsed)).ToArray();

        static ConflictPlace PlaceAt(int start, int end, string ours, string theirs, string? @base = null)
            => new ConflictPlace { StartLine = start, EndLine = end, Ours = ours, Theirs = theirs, Base = @base };

        [Test] public void Rows_KeepsFirstLinesAndCountsTheRest()
        {
            var text = string.Join("\n", Enumerable.Range(1, 205).Select(i => $"line {i}"));

            var (rows, hidden) = ConflictText.Rows(new[] { (text, 1) });

            var texts = rows.Select(r => r.Text).ToArray();
            Assert.That(texts, Has.Length.EqualTo(200));
            Assert.That((texts.First(), texts.Last()), Is.EqualTo(("line 1", "line 200")));
            Assert.That((rows[0].Line, rows[199].Line), Is.EqualTo((1, 200)));
            Assert.That(hidden, Is.EqualTo(5));
            var (all, none) = ConflictText.Rows(new[] { ("a\nb\nc", 1) });
            Assert.That(all.Select(r => r.Text), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(none, Is.Zero);
        }

        // Text-serialized meshes and terrains have single lines of hundreds of KB: each reaches a label cut short.
        [Test] public void Rows_TruncatesVeryLongLines()
        {
            var (rows, hidden) = ConflictText.Rows(new[] { (new string('x', 5000), 1) });

            Assert.That(rows.Select(r => r.Text), Is.EqualTo(new[] { new string('x', 1000) + "…" }));
            Assert.That(hidden, Is.EqualTo(0));
            Assert.That(ConflictText.Rows(new[] { ("a\n" + new string('x', 1001) + "\nb", 1) }).Rows.Select(r => r.Text),
                Is.EqualTo(new[] { "a", new string('x', 1000) + "…", "b" }));
        }

        [Test] public void Rows_StopsAtBlockBudget()
        {
            var text = string.Join("\n", Enumerable.Repeat(new string('x', 900), 100));

            var (rows, hidden) = ConflictText.Rows(new[] { (text, 1) });

            Assert.That(rows.Count, Is.LessThan(ConflictText.MaxSideLines));
            Assert.That(hidden, Is.GreaterThan(0));
            Assert.That(rows.Count + hidden, Is.EqualTo(100));
            Assert.That(rows.Sum(r => r.Text.Length) + rows.Count - 1, Is.LessThanOrEqualTo(ConflictText.MaxBlockChars));
            Assert.That(rows.Select(r => r.Text), Is.All.EqualTo(new string('x', 900)));
        }

        [Test] public void LineCount_CountsLikeTheFile()
            => Assert.That(new[] { "", "a", "a\n", "a\nb\n", "a\r\nb" }.Select(ConflictText.LineCount), Is.EqualTo(new[] { 0, 1, 1, 2, 2 }));

        [Test] public void SideStarts_FollowTheMarkers()
        {
            var plain = ConflictText.SideStarts(PlaceAt(2, 6, "x\n", "y\n"));
            Assert.That((plain.Ours, plain.Theirs), Is.EqualTo((3, 5)));
            Assert.That(ConflictText.SideStarts(PlaceAt(2, 8, "x\n", "y\n", "b\n")), Is.EqualTo((3, 5, 7)));
            Assert.That(ConflictText.SideStarts(PlaceAt(10, 15, "", "y\n", "b\n")), Is.EqualTo((11, 12, 14))); // A removed its lines
            Assert.That(ConflictText.SideStarts(PlaceAt(1, 5, "x\n", "y")).Theirs, Is.EqualTo(4));            // no final newline
        }

        [Test] public void Rows_NumberEachSegmentFromItsFirstLine()
        {
            var (rows, hidden) = ConflictText.Rows(new[] { ("a\r\nb\n", 3), ("c\n", 10) });
            Assert.That(rows.Select(r => (r.Line, r.Text)), Is.EqualTo(new[] { (3, "a"), (4, "b"), (10, "c") }));
            Assert.That(hidden, Is.Zero);
        }

        [Test] public void Rows_CapAcrossSegments()
        {
            var (rows, hidden) = ConflictText.Rows(new[] { (Lines(1, 150), 1), (Lines(1, 100), 500) });
            Assert.That((rows.Count, rows[149].Line, rows[150].Line, rows[199].Line, hidden), Is.EqualTo((200, 150, 500, 549, 50)));
        }

        [Test] public void Context_GivesEachPieceItsFirstLine()
        {
            Assert.That(ConflictText.Context(Lines(1, 10), true, true).Select(p => p.Line), Is.EqualTo(new[] { 0, 3, 7 }));
            Assert.That(ConflictText.Context(Lines(1, 10), false, true).Select(p => p.Line), Is.EqualTo(new[] { 0, 7 }));
        }

        [Test] public void GutterWidth_FitsTheLargestNumber()
        {
            Assert.That(ConflictsPanel.GutterWidth(9), Is.EqualTo(ConflictsPanel.GutterWidth(99)));
            Assert.That(ConflictsPanel.GutterWidth(12345), Is.GreaterThan(ConflictsPanel.GutterWidth(999)));
        }

        [Test] public void Context_TruncatesVeryLongLines()
        {
            var longLine = new string('x', 5000) + "\n";

            Assert.That(Pieces(longLine + "short\n", true, false),
                Is.EqualTo(new (string?, int)[] { (new string('x', 1000) + "…\nshort\n", 0) }));
            Assert.That(Pieces(Lines(1, 10) + longLine, false, true),
                Is.EqualTo(new (string?, int)[] { (null, 8), (Lines(9, 10) + new string('x', 1000) + "…\n", 0) }));
        }

        [Test] public void Context_Between_CollapsesLongRuns()
        {
            Assert.That(Pieces(Lines(1, 10), true, true), Is.EqualTo(new (string?, int)[] { (Lines(1, 3), 0), (null, 4), (Lines(8, 10), 0) }));
            Assert.That(Pieces(Lines(1, 7), true, true), Is.EqualTo(new (string?, int)[] { (Lines(1, 7), 0) }));
        }

        [Test] public void Context_BeforeFirstPlace_KeepsLastLines()
        {
            Assert.That(Pieces(Lines(1, 10), false, true), Is.EqualTo(new (string?, int)[] { (null, 7), (Lines(8, 10), 0) }));
            Assert.That(Pieces(Lines(1, 4), false, true), Is.EqualTo(new (string?, int)[] { (Lines(1, 4), 0) }));
        }

        [Test] public void Context_AfterLastPlace_KeepsFirstLines()
        {
            Assert.That(Pieces(Lines(1, 10), true, false), Is.EqualTo(new (string?, int)[] { (Lines(1, 3), 0), (null, 7) }));
        }
    }
}
