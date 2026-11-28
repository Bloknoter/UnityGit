#nullable enable
using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class GitignoreDocumentTests
    {
        // "C1-2" is a comment block on lines 1 to 2, "K4-5" a code block; lines are 1-based.
        static string[] Kinds(GitignoreDocument d)
            => d.Blocks.Select(b => (b.Kind == GitignoreBlockKind.Comment ? "C" : "K") + (b.StartLine + 1) + "-" + (b.StartLine + b.LineCount)).ToArray();

        static string[] Kinds(string text) => Kinds(GitignoreDocument.Parse(text));

        // ---- parsing ----

        [Test] public void Parse_CommentThenCode_TwoBlocks()
            => Assert.That(Kinds("# a\nx\ny\n"), Is.EqualTo(new[] { "C1-1", "K2-3" }));

        [Test] public void Parse_CodeWithBlankLines_OneBlock()
            => Assert.That(Kinds("x\n\ny\n\n# c\n"), Is.EqualTo(new[] { "K1-3", "C5-5" }));

        [Test] public void Parse_CommentsOneBlankApart_OneBlock()
            => Assert.That(Kinds("# a\n\n# b\n"), Is.EqualTo(new[] { "C1-3" }));

        [Test] public void Parse_CommentsTwoBlanksApart_TwoBlocks()
            => Assert.That(Kinds("# a\n\n\n# b\n"), Is.EqualTo(new[] { "C1-1", "C4-4" }));

        [Test] public void Parse_EscapedHashAndIndentedHash_AreCode()
            => Assert.That(Kinds("\\#x\n #y\n"), Is.EqualTo(new[] { "K1-2" }));

        [Test] public void Parse_WhitespaceOnlyLine_IsBlank()
            => Assert.That(GitignoreDocument.KindOf("  \t"), Is.EqualTo(GitignoreLineKind.Blank));

        [Test] public void Parse_LeadingAndTrailingBlanks_AreGaps()
        {
            var d = GitignoreDocument.Parse("\n\nx\n\n");
            Assert.That(Kinds(d), Is.EqualTo(new[] { "K3-3" }));
            Assert.That(d.Lines.Count, Is.EqualTo(4));
        }

        [Test] public void Parse_Crlf_SameAsLf()
            => Assert.That(GitignoreDocument.Parse("a\r\nb\r\n").ToText(), Is.EqualTo("a\nb\n"));

        [Test] public void Parse_EmptyAndNoFinalNewline()
        {
            Assert.That(GitignoreDocument.Parse("").Lines, Is.Empty);
            Assert.That(GitignoreDocument.Parse("a").EndsWithNewline, Is.False);
            Assert.That(GitignoreDocument.Parse("a").ToText(), Is.EqualTo("a"));
        }

        // ---- Remove ----

        [Test] public void Remove_Middle_KeepsLargerGap()
            => Assert.That(GitignoreDocument.Parse("x\n# a\n\n\n# b\ny\n").Remove(1).ToText(), Is.EqualTo("x\n\n\n# b\ny\n"));

        [Test] public void Remove_First_KeepsTopGap()
            => Assert.That(GitignoreDocument.Parse("\n# a\nx\n").Remove(0).ToText(), Is.EqualTo("\nx\n"));

        [Test] public void Remove_Last_KeepsEndGapAndNoFinalNewline()
            => Assert.That(GitignoreDocument.Parse("\n# a\nx").Remove(1).ToText(), Is.EqualTo("\n# a"));

        [Test] public void Remove_CodeBetweenComments_CommentsStaySeparate()
        {
            var d = GitignoreDocument.Parse("# a\nx\n# b\n").Remove(1);
            Assert.That(d.ToText(), Is.EqualTo("# a\n\n\n# b\n"));
            Assert.That(Kinds(d), Is.EqualTo(new[] { "C1-1", "C4-4" }));
        }

        [Test] public void Remove_CommentBetweenCode_CodeMerges()
            => Assert.That(Kinds(GitignoreDocument.Parse("x\n# a\ny\n").Remove(1)), Is.EqualTo(new[] { "K1-2" }));

        [Test] public void Remove_OnlyBlock_GivesEmptyText()
            => Assert.That(GitignoreDocument.Parse("\n# a\n\n").Remove(0).ToText(), Is.EqualTo(""));

        // ---- Move ----

        [Test] public void Move_SamePlace_ReturnsNull()
        {
            var d = GitignoreDocument.Parse("# a\nx\n# b\n");
            Assert.That(d.Move(1, 1), Is.Null);
            Assert.That(d.Move(1, 2), Is.Null);
        }

        [Test] public void Move_ToTop()
            => Assert.That(GitignoreDocument.Parse("# a\n\n\n# b\n").Move(1, 0)!.ToText(), Is.EqualTo("# b\n\n\n# a\n"));

        [Test] public void Move_ToBottom()
            => Assert.That(GitignoreDocument.Parse("# a\nx\n# b\n").Move(0, 3)!.ToText(), Is.EqualTo("x\n# b\n\n\n# a\n"));

        [Test] public void Move_CodeNextToCode_Merges()
        {
            var d = GitignoreDocument.Parse("x\n# a\ny\n").Move(2, 1)!;
            Assert.That(d.ToText(), Is.EqualTo("x\ny\n\n# a\n"));
            Assert.That(Kinds(d), Is.EqualTo(new[] { "K1-2", "C4-4" }));
        }

        [Test] public void Move_CommentNextToComment_StaysSeparate()
        {
            var d = GitignoreDocument.Parse("# a\n\n\n# b\n\n\n# c\n").Move(0, 2)!;
            Assert.That(d.ToText(), Is.EqualTo("# b\n\n\n# a\n\n\n# c\n"));
            Assert.That(d.Blocks.Count, Is.EqualTo(3));
        }

        // ---- Append ----

        [Test] public void Append_ToEmpty_IsJustTheLines()
        {
            Assert.That(GitignoreDocument.Parse("").Append(new[] { "# c", "*.psd" }).ToText(), Is.EqualTo("# c\n*.psd\n"));
            Assert.That(GitignoreDocument.Parse("\n\n").Append(new[] { "# c", "*.psd" }).ToText(), Is.EqualTo("# c\n*.psd\n"));
        }

        [Test] public void Append_AfterComment_TwoBlankLines()
            => Assert.That(GitignoreDocument.Parse("# a\n").Append(new[] { "# c", "x" }).ToText(), Is.EqualTo("# a\n\n\n# c\nx\n"));

        [Test] public void Append_AfterCode_OneBlankLine()
            => Assert.That(GitignoreDocument.Parse("x\n\n\n").Append(new[] { "y" }).ToText(), Is.EqualTo("x\n\ny\n"));

        [Test] public void Append_TrimsBlankLines()
            => Assert.That(GitignoreDocument.Parse("x\n").Append(new[] { "", "y", "" }).ToText(), Is.EqualTo("x\n\ny\n"));

        [Test] public void Append_NoFinalNewline_StartsNewLine()
            => Assert.That(GitignoreDocument.Parse("x").Append(new[] { "y" }).ToText(), Is.EqualTo("x\n\ny\n"));
    }
}
