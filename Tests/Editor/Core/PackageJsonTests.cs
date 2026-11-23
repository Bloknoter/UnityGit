#nullable enable
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class PackageJsonTests
    {
        const string UnityGitPackage = @"{
  ""name"": ""com.bloknoter.unitygit"",
  ""version"": ""1.2.0"",
  ""description"": ""Quotes \""inside\"" and a backslash \\ and é"",
  ""keywords"": [""git"", ""vcs""],
  ""author"": { ""name"": ""Bloknoter"", ""url"": ""https://github.com/Bloknoter"" },
  ""hideInEditor"": false,
  ""priority"": 1.5e2,
  ""extra"": null,
  ""dependencies"": {
    ""com.bloknoter.flexgit"": ""2.2.0"",
    ""com.unity.modules.audio"": ""1.0.0""
  }
}";

        [Test] public void Read_VersionAndDependencies()
        {
            var p = PackageJson.Read(UnityGitPackage);
            Assert.That(p, Is.Not.Null);
            Assert.That(p!.Version, Is.EqualTo("1.2.0"));
            Assert.That(p.Dependencies["com.bloknoter.flexgit"], Is.EqualTo("2.2.0"));
            Assert.That(p.Dependencies.Count, Is.EqualTo(2));
        }

        [Test] public void Read_NoDependencies_Empty()
        {
            var p = PackageJson.Read(@"{ ""version"": ""2.0.0"" }");
            Assert.That(p!.Dependencies, Is.Empty);
        }

        [Test] public void Read_Manifest_GitUrlEntry()
        {
            var p = PackageJson.Read(@"{ ""dependencies"": { ""com.bloknoter.flexgit"": ""https://github.com/Bloknoter/FlexGit.git#upm/v2.2.0"" }, ""testables"": [""x""] }");
            Assert.That(p!.Version, Is.Null);
            Assert.That(p.Dependencies["com.bloknoter.flexgit"], Is.EqualTo("https://github.com/Bloknoter/FlexGit.git#upm/v2.2.0"));
        }

        [Test] public void Read_UnityField()
            => Assert.That(PackageJson.Read(@"{ ""version"": ""1.0.0"", ""unity"": ""6000.3"" }")!.Unity, Is.EqualTo("6000.3"));

        [TestCase("6000.3", "6000.4.1f1", true)]
        [TestCase("6000.4", "6000.4.1f1", true)]
        [TestCase("6000.5", "6000.4.1f1", false)]
        [TestCase("7000.0", "6000.4.1f1", false)]
        [TestCase("2021.3", "6000.4.1f1", true)]
        [TestCase(null, "6000.4.1f1", true)]
        [TestCase("not a version", "6000.4.1f1", true)]
        public void UnitySatisfies(string? required, string editor, bool ok) => Assert.That(PackageJson.UnitySatisfies(required, editor), Is.EqualTo(ok));

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{ \"version\": ")]
        [TestCase("[1, 2]")]
        [TestCase("{ \"version\": \"1.0.0\" } trailing")]
        public void Read_Invalid_Null(string text) => Assert.That(PackageJson.Read(text), Is.Null);
    }
}
