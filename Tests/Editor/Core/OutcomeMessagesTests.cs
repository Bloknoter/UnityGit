#nullable enable
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class OutcomeMessagesTests
    {
        [TestCase(GitErrorKind.AuthenticationFailed, "Sign-in failed — check your Git credentials")]
        [TestCase(GitErrorKind.IndexLocked, "Another git process is running")]
        [TestCase(GitErrorKind.IdentityNotConfigured, "Git doesn't know who you are — set user.name and user.email in your git config")]
        [TestCase(GitErrorKind.Unknown, "fallback")]
        public void ForErrorKind_Maps(GitErrorKind kind, string expected) => Assert.That(OutcomeMessages.ForErrorKind(kind, "fallback"), Is.EqualTo(expected));

        [Test] public void FromError_CarriesStderrAsDetails()
        {
            var o = OutcomeMessages.FromError(GitAction.Push, new GitError(GitErrorKind.NetworkError, "x", 128, "fatal: unable to access"));
            Assert.That((o.Kind, o.Message, o.Details), Is.EqualTo((OutcomeKind.Failed, "Network error — check your connection and the remote URL", "fatal: unable to access")));
        }
        [Test] public void Files_Pluralizes() => Assert.That((OutcomeMessages.Files(1), OutcomeMessages.Files(3)), Is.EqualTo(("1 file", "3 files")));
        [Test] public void GitignoreTemplate_HasUnityEntries()
            => Assert.That(GitignoreTemplate.Text, Does.Contain("/[Ll]ibrary/").And.Contain("/[Tt]emp/").And.Contain("/[Ll]ogs/")
                .And.Contain("/[Uu]ser[Ss]ettings/").And.Contain("/[Bb]uild/").And.Contain("*.csproj").And.Contain("*.sln"));
        [Test] public void GitignoreTemplate_UsesLfLineEndingsWithTrailingNewline()
            => Assert.That(GitignoreTemplate.Text, Does.StartWith("# Unity generated\n/[Ll]ibrary/\n").And.EndWith("/[Aa]ssets/[Ss]treamingAssets/aa/*\n").And.Not.Contain("\r"));
    }
}
