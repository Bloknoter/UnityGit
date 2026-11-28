#nullable enable
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class GitignoreListTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();

        [TearDown] public void TearDown()
        {
            _sb.Dispose();
            GitLocator.ClearCache();
        }

        async Task<GitRepository> RepoAsync()
        {
            var repo = await _sb.InitRepoAsync("r");
            await GitSandbox.CommitFileAsync(repo, "base.txt", "base\n", "initial");
            return repo;
        }

        async Task<GitSession> SessionAsync(string projectPath)
        {
            var s = _sb.CreateSession(projectPath, new FakeEditorHooks());
            await s.RefreshAsync();
            return s;
        }

        [Test]
        public async Task List_RootNestedAndUntracked()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, ".gitignore", "*.tmp\n", "root");
            await GitSandbox.CommitFileAsync(repo, "a/.gitignore", "*.log\n", "nested");
            GitSandbox.WriteFile(repo, "b/c/.gitignore", "x\n");
            GitSandbox.WriteFile(repo, "x.gitignore", "y\n");

            var s = await SessionAsync(repo.WorkingDirectory);

            Assert.That(await s.ListGitignoreFilesAsync(), Is.EqualTo(new[] { ".gitignore", "a/.gitignore", "b/c/.gitignore" }));
        }

        [Test]
        public async Task List_IgnoredFolder_Skipped()
        {
            var repo = await RepoAsync();
            await GitSandbox.CommitFileAsync(repo, ".gitignore", "ignored/\n", "root");
            GitSandbox.WriteFile(repo, "ignored/.gitignore", "x\n");

            var s = await SessionAsync(repo.WorkingDirectory);

            Assert.That(await s.ListGitignoreFilesAsync(), Is.EqualTo(new[] { ".gitignore" }));
        }

        [Test]
        public async Task List_SelfIgnoredRoot_StillListed()
        {
            var repo = await RepoAsync();
            GitSandbox.WriteFile(repo, ".gitignore", ".*\n");

            var s = await SessionAsync(repo.WorkingDirectory);

            Assert.That(await s.ListGitignoreFilesAsync(), Is.EqualTo(new[] { ".gitignore" }));
        }

        [Test]
        public async Task List_ProjectInSubfolder_IncludesProjectGitignore()
        {
            var repo = await RepoAsync();
            GitSandbox.WriteFile(repo, "Game/.gitignore", "*\n"); // ignores itself, so git doesn't list it

            var s = await SessionAsync(Path.Combine(repo.WorkingDirectory, "Game"));

            Assert.That(s.ProjectGitignorePath, Is.EqualTo("Game/.gitignore"));
            Assert.That(await s.ListGitignoreFilesAsync(), Is.EqualTo(new[] { "Game/.gitignore" }));
        }

        [Test]
        public async Task List_Conflicted_ListedOnce()
        {
            var repo = await _sb.InitRepoAsync("r");
            await ConflictScenarios.MergeAsync(repo, ConflictScenarios.Text(".gitignore", "a\n", "b\n", "c\n"));

            var s = await SessionAsync(repo.WorkingDirectory);

            Assert.That(await s.ListGitignoreFilesAsync(), Is.EqualTo(new[] { ".gitignore" }));
        }

        [Test]
        public async Task List_NoRepository_Empty()
        {
            var s = await SessionAsync(_sb.MakeDir("plain"));
            Assert.That(await s.ListGitignoreFilesAsync(), Is.Empty);
            Assert.That(s.ProjectGitignorePath, Is.Null);
        }

        // The brackets would be a glob if they weren't escaped: "[old]" matches the one letter o, l or d. ('*' and '?' can't
        // be in a Windows file name; GitignoreRulesTests covers their escaping.)
        [Test]
        public async Task Rules_EscapedPath_IsIgnoredByGit()
        {
            var repo = await RepoAsync();
            const string picked = "Assets/Мои [old]/a b.txt";
            const string lookalike = "Assets/Мои o/a b.txt";
            GitSandbox.WriteFile(repo, picked, "x\n");
            GitSandbox.WriteFile(repo, lookalike, "x\n");
            var root = repo.WorkingDirectory;
            var rule = GitignoreRules.Build(GitignoreRuleKind.File, "", picked, false, false, root, root);
            Assert.That(rule.Error, Is.Null, "setup");
            File.WriteAllText(Path.Combine(root, ".gitignore"), string.Join("\n", rule.Lines) + "\n");

            var ignored = await repo.RunRawAsync("check-ignore", "-q", picked);
            var other = await repo.RunRawAsync("check-ignore", "-q", lookalike);

            Assert.That(ignored.ExitCode, Is.EqualTo(0), "the picked file is ignored: " + string.Join("\n", rule.Lines) + "\n" + ignored.StandardError);
            Assert.That(other.ExitCode, Is.EqualTo(1), "the look-alike is not: " + other.StandardError);
        }
    }
}
