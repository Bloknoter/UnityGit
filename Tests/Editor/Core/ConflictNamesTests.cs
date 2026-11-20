#nullable enable
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class ConflictNamesTests
    {
        const string Sha = "df3a02cbbc2d854e76b247b21995894700600e33";
        const string OtherSha = "0123456789abcdef0123456789abcdef01234567";

        [TestCase(null, null)]
        [TestCase("  ", null)]
        [TestCase("feature", "feature")]
        [TestCase("df3a02cbbc2d854e76b247b21995894700600e33", "df3a02c")]
        public void Short(string? label, string? expected)
            => Assert.That(ConflictNames.Short(label), Is.EqualTo(expected));

        [Test]
        public void Short_CapsLongLabels()
            => Assert.That(ConflictNames.Short(new string('x', 50)), Is.EqualTo(new string('x', 39) + "…"));

        [Test]
        public void Resolve_PullMerge_UsesBranchAndUpstream()
            => Assert.That(ConflictNames.Resolve(RepoOperation.Merging, "main", "origin/main", Sha, Sha, "HEAD", Sha),
                Is.EqualTo(("main", "origin/main")));

        [Test]
        public void Resolve_LocalMerge_UsesBranchAndLabel()
            => Assert.That(ConflictNames.Resolve(RepoOperation.Merging, "main", null, null, OtherSha, "HEAD", "feature"),
                Is.EqualTo(("main", "feature")));

        [Test]
        public void Resolve_DetachedMerge_KeepsHead()
            => Assert.That(ConflictNames.Resolve(RepoOperation.Merging, null, null, null, OtherSha, "HEAD", "feature"),
                Is.EqualTo(("HEAD", "feature")));

        [Test]
        public void Resolve_Rebase_UsesGitLabels()
            => Assert.That(ConflictNames.Resolve(RepoOperation.Rebasing, null, null, null, null, "HEAD", "7c6c68e (local)"),
                Is.EqualTo(("HEAD", "7c6c68e (local)")));

        [Test]
        public void Resolve_MergeWithoutLabels_UsesMergeHead()
            => Assert.That(ConflictNames.Resolve(RepoOperation.Merging, "main", null, null, OtherSha, null, null),
                Is.EqualTo(("main", "0123456")));

        [Test]
        public void Resolve_NoLabels_FallsBack()
            => Assert.That(ConflictNames.Resolve(RepoOperation.None, null, null, null, null, null, null),
                Is.EqualTo(("ours", "theirs")));
    }
}
