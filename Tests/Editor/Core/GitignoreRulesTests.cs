#nullable enable
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UnityGit.Tests
{
    public class GitignoreRulesTests
    {
        static readonly string F = Path.Combine(Path.GetTempPath(), "UnityGitRulesTests", "F");
        static string Slashed(string path) => path.Replace('\\', '/');

        string _dir = null!;

        [SetUp] public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "UnityGitRulesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown] public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static GitignoreRuleResult Build(GitignoreRuleKind kind, string value, string comment = "", bool negate = false, bool meta = true,
            string? folder = null, string? project = null)
            => GitignoreRules.Build(kind, comment, value, negate, meta, folder ?? F, project ?? F);

        // ---- comment and escaping ----

        [Test] public void CommentLine_Forms()
        {
            Assert.That(GitignoreRules.CommentLine(null), Is.Null);
            Assert.That(GitignoreRules.CommentLine("  "), Is.Null);
            Assert.That(GitignoreRules.CommentLine("## Logs "), Is.EqualTo("# Logs"));
            Assert.That(GitignoreRules.CommentLine("a\nb"), Is.EqualTo("# a b"));
        }

        [Test] public void Escape_GlobAndTrailingSpace()
            => Assert.That(GitignoreRules.Escape("a*b?[c]\\ "), Is.EqualTo("a\\*b\\?\\[c]\\\\\\ "));

        // ---- by extension ----

        [Test] public void Build_Extension_Forms()
        {
            foreach (var value in new[] { "psd", ".psd", "*.psd" })
                Assert.That(Build(GitignoreRuleKind.Extension, value).Lines, Is.EqualTo(new[] { "*.psd", "*.psd.meta" }), value);
            Assert.That(Build(GitignoreRuleKind.Extension, "psd", "PSDs").Lines, Is.EqualTo(new[] { "# PSDs", "*.psd", "*.psd.meta" }));
            Assert.That(Build(GitignoreRuleKind.Extension, "psd", meta: false).Lines, Is.EqualTo(new[] { "*.psd" }));
        }

        [Test] public void Build_Extension_Negate()
            => Assert.That(Build(GitignoreRuleKind.Extension, "psd", negate: true).Lines, Is.EqualTo(new[] { "!*.psd", "!*.psd.meta" }));

        [Test] public void Build_Extension_Errors()
        {
            Assert.That(Build(GitignoreRuleKind.Extension, "").Error, Is.EqualTo("Enter an extension"));
            Assert.That(Build(GitignoreRuleKind.Extension, "*.").Error, Is.EqualTo("Enter an extension"));
            Assert.That(Build(GitignoreRuleKind.Extension, "a/b").Error, Is.EqualTo("An extension can't contain /"));
            Assert.That(Build(GitignoreRuleKind.Extension, "a/b").Lines, Is.Empty);
        }

        // ---- folders and files ----

        [Test] public void Build_Folder_RelativeAndAbsolute()
        {
            foreach (var value in new[] { "Assets/Foo", "/Assets/Foo", Path.Combine(F, "Assets", "Foo") })
                Assert.That(Build(GitignoreRuleKind.Folder, value).Lines, Is.EqualTo(new[] { "/Assets/Foo/", "/Assets/Foo.meta" }), value);
        }

        [Test] public void Build_Folder_TrailingSlash()
        {
            foreach (var value in new[] { "Assets/Foo/", @"Assets\Foo\", "/Assets/Foo/", Path.Combine(F, "Assets", "Foo") + Path.DirectorySeparatorChar })
                Assert.That(Build(GitignoreRuleKind.Folder, value).Lines, Is.EqualTo(new[] { "/Assets/Foo/", "/Assets/Foo.meta" }), value);
        }

        [Test] public void Build_Folder_RelativeToNestedGitignore()
        {
            var game = Path.Combine(F, "Game");
            var art = Path.Combine(game, "Assets", "Art");
            Assert.That(Build(GitignoreRuleKind.Folder, art, folder: game, project: game).Lines, Is.EqualTo(new[] { "/Assets/Art/", "/Assets/Art.meta" }));
            Assert.That(Build(GitignoreRuleKind.Folder, art, folder: F, project: game).Lines,
                Is.EqualTo(new[] { "/Game/Assets/Art/", "/Game/Assets/Art.meta" }));
        }

        [Test] public void Build_File_OutsideProjectAssets_NoMetaLine()
        {
            Assert.That(Build(GitignoreRuleKind.File, "Builds/x.txt").Lines, Is.EqualTo(new[] { "/Builds/x.txt" }));
            Assert.That(GitignoreRules.MetaApplies(GitignoreRuleKind.File, "Builds/x.txt", F, F), Is.False);
            Assert.That(GitignoreRules.MetaApplies(GitignoreRuleKind.File, "Packages/a/x.txt", F, F), Is.True);
            Assert.That(Build(GitignoreRuleKind.File, "Assets/x.txt", negate: true).Lines, Is.EqualTo(new[] { "!/Assets/x.txt", "!/Assets/x.txt.meta" }));
        }

        [Test] public void Build_Path_Errors()
        {
            var inside = "Pick a path inside " + Slashed(F);
            Assert.That(Build(GitignoreRuleKind.Folder, "").Error, Is.EqualTo("Enter a folder path"));
            Assert.That(Build(GitignoreRuleKind.File, " ").Error, Is.EqualTo("Enter a file path"));
            Assert.That(Build(GitignoreRuleKind.Folder, "../x").Error, Is.EqualTo(inside));
            Assert.That(Build(GitignoreRuleKind.Folder, F).Error, Is.EqualTo(inside));
            Assert.That(Build(GitignoreRuleKind.File, "a\0b").Error, Is.EqualTo("Not a valid path"));
        }

        // ---- custom ----

        [Test] public void Build_Custom_LinesAsTyped()
        {
            Assert.That(Build(GitignoreRuleKind.Custom, "\n[Ll]ib/\n  x\n\n", negate: true).Lines, Is.EqualTo(new[] { "[Ll]ib/", "  x" }));
            Assert.That(Build(GitignoreRuleKind.Custom, "").Error, Is.EqualTo("Enter a rule"));
            Assert.That(Build(GitignoreRuleKind.Custom, " \n ").Error, Is.EqualTo("Enter a rule"));
            Assert.That(GitignoreRules.MetaApplies(GitignoreRuleKind.Custom, "x", F, F), Is.False);
        }

        // ---- file IO ----

        [Test] public void File_RoundTrip_CrlfBom()
        {
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.ASCII.GetBytes("a\r\nb\r\n")).ToArray();
            var path = Path.Combine(_dir, ".gitignore");
            File.WriteAllBytes(path, bytes);

            var read = GitignoreFile.Read(path)!;
            Assert.That((read.Text, read.HasBom, read.LineEnding, read.IsUtf8), Is.EqualTo(("a\nb\n", true, "\r\n", true)));

            var copy = Path.Combine(_dir, "copy");
            GitignoreFile.Write(copy, read.Text, read.HasBom, read.LineEnding);
            Assert.That(File.ReadAllBytes(copy), Is.EqualTo(bytes));
        }

        [Test] public void File_MixedEndings_UsesMostCommon()
        {
            var path = Path.Combine(_dir, ".gitignore");
            File.WriteAllText(path, "a\r\nb\r\nc\n");
            Assert.That(GitignoreFile.Read(path)!.LineEnding, Is.EqualTo("\r\n"));
            File.WriteAllText(path, "a\nb\nc\r\n");
            Assert.That(GitignoreFile.Read(path)!.LineEnding, Is.EqualTo("\n"));
        }

        [Test] public void File_InvalidUtf8_IsNotUtf8()
        {
            var path = Path.Combine(_dir, ".gitignore");
            File.WriteAllBytes(path, new byte[] { 0x61, 0xFF, 0x0A });
            Assert.That(GitignoreFile.Read(path)!.IsUtf8, Is.False);
        }

        [Test] public void File_Missing_ReadsNull()
            => Assert.That(GitignoreFile.Read(Path.Combine(_dir, "missing")), Is.Null);
    }
}
