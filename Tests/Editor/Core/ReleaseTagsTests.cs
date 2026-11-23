#nullable enable
using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class ReleaseTagsTests
    {
        [TestCase("1.2.3", 1, 2, 3)]
        [TestCase("10.0.25", 10, 0, 25)]
        public void SemVer_Parses(string text, int major, int minor, int patch)
        {
            Assert.That(SemVer.TryParse(text, out var v), Is.True);
            Assert.That((v.Major, v.Minor, v.Patch), Is.EqualTo((major, minor, patch)));
            Assert.That(v.ToString(), Is.EqualTo(text));
        }

        [TestCase("1.2")]
        [TestCase("1.2.3-beta")]
        [TestCase("v1.2.3")]
        [TestCase(" 1.2.3")]
        [TestCase("1.2.3.4")]
        [TestCase("")]
        [TestCase(null)]
        public void SemVer_RejectsAnythingButThreeNumbers(string? text) => Assert.That(SemVer.TryParse(text, out _), Is.False);

        [Test] public void SemVer_ComparesNumerically()
        {
            Assert.That(SemVer.Parse("1.10.0"), Is.GreaterThan(SemVer.Parse("1.9.0")));
            Assert.That(SemVer.Parse("2.0.0"), Is.GreaterThan(SemVer.Parse("1.99.99")));
            Assert.That(SemVer.Parse("1.2.3") == SemVer.Parse("1.2.3"), Is.True);
            Assert.That(SemVer.Parse("1.2.3") < SemVer.Parse("1.2.4"), Is.True);
        }

        const string LsRemote =
            "aaa1\trefs/tags/v1.0.0\n" +
            "aaa2\trefs/tags/v1.0.0^{}\n" +
            "bbb1\trefs/tags/upm/v2.0.0\r\n" +
            "ccc1\trefs/tags/v1.10.0\n" +
            "ddd1\trefs/tags/v1.2.0-beta\n" +
            "eee1\trefs/tags/v1.9.0\n" +
            "fff1\trefs/tags/release-1\n" +
            "ggg1\trefs/heads/main\n";

        [Test] public void Parse_KeepsOnlyThePrefixedReleases_NewestFirst()
        {
            var tags = ReleaseTags.Parse(LsRemote, "v");
            Assert.That(tags.Select(t => t.Tag), Is.EqualTo(new[] { "v1.10.0", "v1.9.0", "v1.0.0" }));
            Assert.That(tags.Select(t => t.Version.ToString()), Is.EqualTo(new[] { "1.10.0", "1.9.0", "1.0.0" }));
        }

        [Test] public void Parse_UsesThePeeledCommitOfAnAnnotatedTag()
            => Assert.That(ReleaseTags.Parse(LsRemote, "v").Single(t => t.Tag == "v1.0.0").Sha, Is.EqualTo("aaa2"));

        [Test] public void Parse_UpmPrefix_IgnoresThePlainTags()
        {
            var tags = ReleaseTags.Parse(LsRemote, "upm/v");
            Assert.That(tags.Select(t => t.Tag), Is.EqualTo(new[] { "upm/v2.0.0" }));
            Assert.That(tags[0].Sha, Is.EqualTo("bbb1"));
        }

        [Test] public void Parse_EmptyOutput_NoReleases() => Assert.That(ReleaseTags.Parse("", "v"), Is.Empty);

        [TestCase("upm/v2.0.0", "upm/v")]
        [TestCase("v1.0.0", "v")]
        [TestCase("1.0.0", "")]
        [TestCase("main", null)]
        [TestCase("v1.0.0-beta", null)]
        [TestCase("f6964dafbde7632b58bc40b7534dd532434b21d3", null)]
        [TestCase(null, null)]
        public void PrefixOf_IsTheRevisionWithoutItsVersion(string? revision, string? prefix)
            => Assert.That(ReleaseTags.PrefixOf(revision), Is.EqualTo(prefix));
    }
}
