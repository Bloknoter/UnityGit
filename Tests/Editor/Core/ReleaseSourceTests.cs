#nullable enable
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class ReleaseSourceTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        static string Package(string version, string flexGit, string unity = "6000.3")
            => $"{{\n  \"name\": \"com.bloknoter.unitygit\",\n  \"version\": \"{version}\",\n  \"unity\": \"{unity}\",\n" +
               $"  \"dependencies\": {{ \"com.bloknoter.flexgit\": \"{flexGit}\" }}\n}}\n";

        // A package repo with v1.0.0 (lightweight), v1.1.0 (annotated), a pre-release and an upm/ tag.
        async Task<(GitRepository repo, string first, string second)> PackageRepoAsync(string name = "unitygit-src", string firstVersion = "1.0.0")
        {
            var repo = await _sb.InitRepoAsync(name);
            var first = await GitSandbox.CommitFileAsync(repo, "package.json", Package(firstVersion, "2.0.0"), "first");
            await repo.Tags.CreateAsync("v1.0.0");
            var second = await GitSandbox.CommitFileAsync(repo, "package.json", Package("1.1.0", "2.1.0"), "second");
            await repo.Tags.CreateAsync("v1.1.0", message: "Release 1.1.0");
            await repo.Tags.CreateAsync("v1.2.0-beta");
            await repo.Tags.CreateAsync("upm/v9.0.0");
            return (repo, first, second);
        }

        ReleaseSource Source() => new ReleaseSource(Path.Combine(_sb.Root, "project", "Library", "UnityGit", "ReleaseCache.git"), _sb.Options);

        [Test] public async Task List_ReleasesNewestFirst_WithCommitShas()
        {
            var (repo, first, second) = await PackageRepoAsync();

            var list = await Source().ListAsync(repo.WorkingDirectory, "v");

            Assert.That(list.Error, Is.Null, list.Details);
            Assert.That(list.Tags.Select(t => t.Tag), Is.EqualTo(new[] { "v1.1.0", "v1.0.0" }));
            Assert.That(list.Tags.Select(t => t.Sha), Is.EqualTo(new[] { second, first }));
        }

        [Test] public async Task List_UnreachableUrl_ErrorWithoutThrowing()
        {
            var list = await Source().ListAsync(Path.Combine(_sb.Root, "missing.git"), "v");

            Assert.That(list.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(list.Tags, Is.Empty);
        }

        [Test] public async Task List_UrlLikeAnOption_Refused()
        {
            var list = await Source().ListAsync("--upload-pack=touch pwned", "v");

            Assert.That(list.Error, Is.EqualTo("Not a repository URL: --upload-pack=touch pwned"));
            Assert.That(File.Exists(Path.Combine(_sb.Root, "pwned")), Is.False);
        }

        [Test] public async Task Read_PackageJsonAtATag()
        {
            var (repo, _, _) = await PackageRepoAsync();
            var source = Source();

            var first = await source.ReadPackageAsync(repo.WorkingDirectory, "v1.0.0");
            var second = await source.ReadPackageAsync(repo.WorkingDirectory, "v1.1.0"); // annotated

            Assert.That(first.Error, Is.Null, first.Details);
            Assert.That(first.Package!.Version, Is.EqualTo("1.0.0"));
            Assert.That(first.Package.Dependencies[PackageNames.FlexGit], Is.EqualTo("2.0.0"));
            Assert.That(first.Package.Unity, Is.EqualTo("6000.3"));
            Assert.That(second.Error, Is.Null, second.Details);
            Assert.That(second.Package!.Version, Is.EqualTo("1.1.0"));
        }

        [Test] public async Task Read_MissingTag_Error()
        {
            var (repo, _, _) = await PackageRepoAsync();

            var read = await Source().ReadPackageAsync(repo.WorkingDirectory, "v7.0.0");

            Assert.That(read.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(read.Package, Is.Null);
        }

        [Test] public async Task Read_SameTagInTwoRepos_EachReadsItsOwn()
        {
            var (a, _, _) = await PackageRepoAsync("a", firstVersion: "1.0.0");
            var (b, _, _) = await PackageRepoAsync("b", firstVersion: "1.0.5");
            var source = Source();

            var fromA = await source.ReadPackageAsync(a.WorkingDirectory, "v1.0.0");
            var fromB = await source.ReadPackageAsync(b.WorkingDirectory, "v1.0.0");

            Assert.That(fromA.Package!.Version, Is.EqualTo("1.0.0"));
            Assert.That(fromB.Package!.Version, Is.EqualTo("1.0.5"));
        }

        [Test] public async Task Read_BrokenCache_Recreated()
        {
            var (repo, _, _) = await PackageRepoAsync();
            var cache = Path.Combine(_sb.Root, "project", "Library", "UnityGit", "ReleaseCache.git");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "HEAD"), "garbage");

            var read = await Source().ReadPackageAsync(repo.WorkingDirectory, "v1.0.0");

            Assert.That(read.Error, Is.Null, read.Details);
            Assert.That(read.Package!.Version, Is.EqualTo("1.0.0"));
        }

        [Test] public async Task Read_CacheInsideTheProjectRepo_NeverTouchesTheProjectRepo()
        {
            var (repo, _, _) = await PackageRepoAsync();
            var project = await _sb.InitRepoAsync("project");
            await GitSandbox.CommitFileAsync(project, "a.txt", "a", "project commit");

            var read = await Source().ReadPackageAsync(repo.WorkingDirectory, "v1.0.0");

            Assert.That(read.Error, Is.Null, read.Details);
            var refs = await project.RunRawAsync("for-each-ref", "--format=%(refname)");
            Assert.That(refs.StandardOutput, Does.Not.Contain("unitygit"));
        }
    }
}
