#nullable enable
using System.IO;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;
using UnityGit.UI;

namespace UnityGit.Tests
{
    /// <summary>
    /// A project on UnityGit 1.2.0 + FlexGit 2.2.0 whose package repositories are local folders with release tags:
    /// UnityGit v1.2.0 (needs FlexGit 2.2.0) and v1.4.0 (needs 2.3.0); FlexGit upm/v2.2.0, upm/v2.3.0, upm/v3.0.0 and a
    /// main-branch v2.3.0 tag.
    /// </summary>
    public abstract class UpdatesFixture
    {
        internal GitSandbox Sb = null!;
        internal FakePackageHost Host = null!;
        internal FakeEditorHooks Hooks = null!;
        internal GitSession Session = null!;
        internal string UnityGitUrl = null!;
        internal string FlexGitUrl = null!;

        [SetUp] public async Task SetUpUpdates()
        {
            Sb = await GitSandbox.CreateAsync();
            Host = new FakePackageHost();
            Hooks = new FakeEditorHooks();

            var unityGit = await Sb.InitRepoAsync("unitygit-src");
            await ReleaseAsync(unityGit, "v1.2.0", Package("com.bloknoter.unitygit", "1.2.0", "2.2.0"));
            await ReleaseAsync(unityGit, "v1.4.0", Package("com.bloknoter.unitygit", "1.4.0", "2.3.0"));
            UnityGitUrl = unityGit.WorkingDirectory;

            var flexGit = await Sb.InitRepoAsync("flexgit-src");
            await ReleaseAsync(flexGit, "upm/v2.2.0", Package("com.bloknoter.flexgit", "2.2.0", null));
            await ReleaseAsync(flexGit, "upm/v2.3.0", Package("com.bloknoter.flexgit", "2.3.0", null));
            await flexGit.Tags.CreateAsync("v2.3.0"); // FlexGit's main-branch tags: never offered
            await ReleaseAsync(flexGit, "upm/v3.0.0", Package("com.bloknoter.flexgit", "3.0.0", null));
            FlexGitUrl = flexGit.WorkingDirectory;

            Install(PackageNames.UnityGit, "1.2.0", requires: "2.2.0");
            Install(PackageNames.FlexGit, "2.2.0");

            var project = await Sb.InitRepoAsync("project");
            await GitSandbox.CommitFileAsync(project, "a.txt", "a", "project");
            Session = Sb.CreateSession(project.WorkingDirectory, Hooks);
            await Session.RefreshAsync();
        }

        [TearDown] public void TearDownUpdates()
        {
            Session.Dispose();
            Sb.Dispose();
            GitLocator.ClearCache();
        }

        internal static string Package(string name, string version, string? flexGit, string unity = "6000.3")
            => $"{{ \"name\": \"{name}\", \"version\": \"{version}\", \"unity\": \"{unity}\"" +
               (flexGit == null ? "" : $", \"dependencies\": {{ \"com.bloknoter.flexgit\": \"{flexGit}\" }}") + " }\n";

        internal static async Task ReleaseAsync(GitRepository repo, string tag, string packageJson)
        {
            await GitSandbox.CommitFileAsync(repo, "package.json", packageJson, tag);
            await repo.Tags.CreateAsync(tag);
        }

        internal void Install(string name, string version, string? requires = null, PackageSource source = PackageSource.Git)
        {
            var url = name == PackageNames.UnityGit ? UnityGitUrl : FlexGitUrl;
            var tag = (name == PackageNames.UnityGit ? "v" : "upm/v") + version;
            Host.Installed[name] = new InstalledPackage(name, source, SemVer.Parse(version),
                source == PackageSource.Git ? $"{url}#{tag}" : "file:" + name, requires == null ? (SemVer?)null : SemVer.Parse(requires));
        }

        internal PackageUpdates Create()
            => new PackageUpdates(Host, new ReleaseSource(Path.Combine(Sb.Root, "cache.git"), Sb.Options), Hooks, () => Session);

        internal async Task<PackageUpdates> CheckedAsync()
        {
            var updates = Create();
            await updates.CheckAsync();
            Assert.That(updates.Error, Is.Null, updates.ErrorDetails);
            return updates;
        }
    }
}
