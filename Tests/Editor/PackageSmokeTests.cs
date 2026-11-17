using System;
using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class PackageSmokeTests
    {
        [Test]
        public void FlexGitAssembly_IsReferenced()
            => Assert.That(typeof(FlexGit.GitRepository).Assembly.GetName().Name, Is.EqualTo("FlexGit"));

        [Test]
        public void Package_IsEmbedded_AndDeclaresFlexGit()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.bloknoter.unitygit/package.json");
            Assert.That(info, Is.Not.Null);
            Assert.That(info.source, Is.EqualTo(UnityEditor.PackageManager.PackageSource.Embedded));
            Assert.That(info.dependencies.Select(d => d.name), Contains.Item("com.bloknoter.flexgit"));
        }

        [Test]
        public void UiAssembly_DoesNotReferenceFlexGit()
        {
            var ui = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "UnityGit.UI");
            Assert.That(ui.GetReferencedAssemblies().Select(a => a.Name), Has.No.Member("FlexGit"));
        }
    }
}
