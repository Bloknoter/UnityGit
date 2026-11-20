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

        [Test] public void ForDisplay_DropsCrAndOneTrailingNewline()
        {
            Assert.That(ConflictText.ForDisplay("a\r\nb\r\n"), Is.EqualTo("a\nb"));
            Assert.That(ConflictText.ForDisplay("a\n\n"), Is.EqualTo("a\n"));
        }

        [Test] public void Cap_KeepsFirstLinesAndCountsTheRest()
        {
            var text = string.Join("\n", Enumerable.Range(1, 205).Select(i => $"line {i}"));

            var (shown, hidden) = ConflictText.Cap(text);

            var lines = shown.Split('\n');
            Assert.That(lines, Has.Length.EqualTo(200));
            Assert.That((lines.First(), lines.Last()), Is.EqualTo(("line 1", "line 200")));
            Assert.That(hidden, Is.EqualTo(5));
            Assert.That(ConflictText.Cap("a\nb\nc"), Is.EqualTo(("a\nb\nc", 0)));
        }

        // Text-serialized meshes and terrains have single lines of hundreds of KB: each reaches a label cut short.
        [Test] public void Cap_TruncatesVeryLongLines()
        {
            var (shown, hidden) = ConflictText.Cap(new string('x', 5000));

            Assert.That(shown, Is.EqualTo(new string('x', 1000) + "…"));
            Assert.That(hidden, Is.EqualTo(0));
            Assert.That(ConflictText.Cap("a\n" + new string('x', 1001) + "\nb"), Is.EqualTo(("a\n" + new string('x', 1000) + "…\nb", 0)));
        }

        [Test] public void Cap_StopsAtBlockBudget()
        {
            var text = string.Join("\n", Enumerable.Repeat(new string('x', 900), 100));

            var (shown, hidden) = ConflictText.Cap(text);

            var lines = shown.Split('\n');
            Assert.That(lines.Length, Is.LessThan(ConflictText.MaxSideLines));
            Assert.That(hidden, Is.GreaterThan(0));
            Assert.That(lines.Length + hidden, Is.EqualTo(100));
            Assert.That(shown.Length, Is.LessThanOrEqualTo(ConflictText.MaxBlockChars));
            Assert.That(lines, Is.All.EqualTo(new string('x', 900)));
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
