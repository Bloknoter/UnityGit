#nullable enable
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class InstalledPackageTests
    {
        static InstalledPackage Git(string name, string entry)
            => new InstalledPackage(name, PackageSource.Git, SemVer.Parse("1.0.0"), entry);

        [Test] public void GitUrlWithTag_SplitsUrlAndRevision()
        {
            var p = Git(PackageNames.FlexGit, "https://github.com/Bloknoter/FlexGit.git#upm/v2.2.0");
            Assert.That(p.InstallUrl, Is.EqualTo("https://github.com/Bloknoter/FlexGit.git"));
            Assert.That(p.GitUrl, Is.EqualTo("https://github.com/Bloknoter/FlexGit.git"));
            Assert.That(p.Revision, Is.EqualTo("upm/v2.2.0"));
            Assert.That(p.TagPrefix, Is.EqualTo("upm/v"));
        }

        [Test] public void GitPlusSchemeAndPath_GitUrlDropsBoth_InstallUrlKeepsBoth()
        {
            var p = Git(PackageNames.UnityGit, "git+ssh://git@github.com/x/y.git?path=/pkg#v1.0.0");
            Assert.That(p.InstallUrl, Is.EqualTo("git+ssh://git@github.com/x/y.git?path=/pkg"));
            Assert.That(p.GitUrl, Is.EqualTo("ssh://git@github.com/x/y.git"));
            Assert.That(p.TagPrefix, Is.EqualTo("v"));
        }

        [Test] public void ScpStyleUrl_Unchanged()
            => Assert.That(Git(PackageNames.UnityGit, "git@github.com:x/y.git#v1.0.0").GitUrl, Is.EqualTo("git@github.com:x/y.git"));

        [TestCase(PackageNames.UnityGit, "https://github.com/Bloknoter/UnityGit.git", "v")]
        [TestCase(PackageNames.FlexGit, "https://github.com/Bloknoter/FlexGit.git#upm", "upm/v")]
        [TestCase(PackageNames.UnityGit, "https://github.com/Bloknoter/UnityGit.git#f6964dafbde7632b58bc40b7534dd532434b21d3", "v")]
        public void NoVersionTag_DefaultPrefix(string name, string entry, string prefix)
            => Assert.That(Git(name, entry).TagPrefix, Is.EqualTo(prefix));

        [Test] public void NoRevision_RevisionNull()
            => Assert.That(Git(PackageNames.UnityGit, "https://github.com/Bloknoter/UnityGit.git").Revision, Is.Null);

        [Test] public void Embedded_NoUrls()
        {
            var p = new InstalledPackage(PackageNames.UnityGit, PackageSource.Embedded, SemVer.Parse("1.3.0"), "file:com.bloknoter.unitygit");
            Assert.That(p.InstallUrl, Is.Null);
            Assert.That(p.GitUrl, Is.Null);
            Assert.That(p.CanInstall, Is.False);
        }

        [Test] public void GitWithoutEntry_CannotInstall()
            => Assert.That(new InstalledPackage(PackageNames.FlexGit, PackageSource.Git, SemVer.Parse("2.2.0")).CanInstall, Is.False);

        [Test] public void Git_CanInstall() => Assert.That(Git(PackageNames.FlexGit, "https://x/y.git#upm/v2.2.0").CanInstall, Is.True);
    }
}
