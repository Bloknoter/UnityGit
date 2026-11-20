#nullable enable
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class ConflictLoaderTests
    {
        [TestCase("version https://git-lfs.github.com/spec/v1", true)]
        [TestCase("one", false)]
        [TestCase("version https://git-lfs.github.com/spec/v2", false)]
        [TestCase(" version https://git-lfs.github.com/spec/v1", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsLfsPointer(string? firstLine, bool expected)
            => Assert.That(ConflictLoader.IsLfsPointer(firstLine), Is.EqualTo(expected));

        [TestCase("a\nb", "a")]
        [TestCase("a\r\nb", "a")]
        [TestCase("a", "a")]
        [TestCase("", "")]
        public void FirstLine_EndsAtTheLineBreak(string text, string expected)
            => Assert.That(ConflictLoader.FirstLine(text), Is.EqualTo(expected));
    }
}
