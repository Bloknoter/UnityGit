#nullable enable
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityGit.UI;

namespace UnityGit.Tests
{
    /// <summary>The real Package Manager host, read against whatever project runs the tests.</summary>
    public class UnityPackageHostTests
    {
        static string ProjectPath => Path.GetDirectoryName(Application.dataPath)!;

        [Test] public void Find_UnityGit_VersionAndFlexGitNeedFromItsPackageJson()
        {
            // Wherever the package sits: Packages/ when embedded, Library/PackageCache when installed from git.
            var folder = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageNames.UnityGit).resolvedPath;
            var own = PackageJson.Read(File.ReadAllText(Path.Combine(folder, "package.json")))!;

            var unityGit = new UnityPackageHost(ProjectPath).Find(PackageNames.UnityGit);

            Assert.That(unityGit.Source, Is.Not.EqualTo(PackageSource.Missing));
            Assert.That(unityGit.Version?.ToString(), Is.EqualTo(own.Version));
            Assert.That(unityGit.RequiredFlexGit?.ToString(), Is.EqualTo(own.Dependencies[PackageNames.FlexGit]));
        }

        [Test] public void Find_FlexGit_FromGit_HasItsManifestUrl()
        {
            var flexGit = new UnityPackageHost(ProjectPath).Find(PackageNames.FlexGit);

            Assert.That(flexGit.Version, Is.Not.Null);
            if (flexGit.Source != PackageSource.Git) Assert.Ignore("FlexGit isn't a git package in this project");
            Assert.That(flexGit.CanInstall, Is.True);
            Assert.That(flexGit.ManifestEntry, Does.Contain("#"));
            Assert.That(flexGit.GitUrl, Does.Not.StartWith("git+"));
        }

        [Test] public void Find_UnknownPackage_Missing()
            => Assert.That(new UnityPackageHost(ProjectPath).Find("com.example.not-installed").Source, Is.EqualTo(PackageSource.Missing));

        [Test] public void EditorVersion_IsTheRunningEditor()
            => Assert.That(new UnityPackageHost(ProjectPath).EditorVersion, Is.EqualTo(Application.unityVersion));
    }
}
