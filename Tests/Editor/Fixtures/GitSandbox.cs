#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit.Tests
{
    internal sealed class RemoteTrio
    {
        public GitRepository Origin = null!;
        public GitRepository Local = null!;
        public GitRepository Teammate = null!;

        // Teammate commits and pushes a README change; local commits a conflicting one.
        public async Task PrepareConflictAsync()
        {
            await GitSandbox.CommitFileAsync(Teammate, "README.md", "teammate line\n", "teammate edit");
            GitSandbox.EnsurePushed(await Teammate.PushAsync());
            await GitSandbox.CommitFileAsync(Local, "README.md", "local line\n", "local edit");
        }
    }

    internal sealed class GitSandbox : IDisposable
    {
        GitSandbox(string root)
        {
            Root = root;
            Options.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "global.gitconfig");
            Options.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        }

        public string Root { get; }
        public GitOptions Options { get; } = new GitOptions();

        public static Task<GitSandbox> CreateAsync() => CreateAsync(true);

        public static Task<GitSandbox> CreateAsync(bool withIdentity)
        {
            var root = Path.Combine(Path.GetTempPath(), "UnityGitTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "global.gitconfig"),
                (withIdentity
                    ? "[user]\n\tname = Test User\n\temail = test@example.com\n"
                    // Stops git guessing an identity from the machine (EMAIL, user@host) on some computers.
                    : "[user]\n\tuseConfigOnly = true\n") +
                "[core]\n\tautocrlf = false\n" +
                "[commit]\n\tgpgsign = false\n" +
                "[init]\n\tdefaultBranch = main\n");
            return Task.FromResult(new GitSandbox(root));
        }

        public string MakeDir(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relative));
            Directory.CreateDirectory(path);
            return path;
        }

        public Task<GitRepository> InitRepoAsync(string relative)
            => GitRepository.InitAsync(MakeDir(relative), "main", false, Options);

        public async Task<RemoteTrio> CreateTrioAsync()
        {
            var trio = new RemoteTrio();
            trio.Origin = await GitRepository.InitAsync(MakeDir("origin.git"), "main", true, Options);
            trio.Local = await InitRepoAsync("local");
            await trio.Local.Remotes.AddAsync("origin", trio.Origin.GitDirectory);
            await CommitFileAsync(trio.Local, "README.md", "readme\n", "initial");
            EnsurePushed(await trio.Local.PushAsync(new PushOptions { Remote = "origin", SetUpstream = true }));

            var clone = await GitRepository.CloneAsync(trio.Origin.GitDirectory, Path.Combine(Root, "teammate"), null, null, Options);
            trio.Teammate = clone.Repository ?? throw new InvalidOperationException("Teammate clone failed: " + clone.Message);
            await trio.Teammate.Config.SetAsync("user.name", "Teammate");
            await trio.Teammate.Config.SetAsync("user.email", "mate@example.com");
            return trio;
        }

        internal static void EnsurePushed(PushResult result)
        {
            if (!result.Succeeded) throw new InvalidOperationException("Push failed: " + result.Message);
        }

        public static void WriteFile(GitRepository repo, string relativePath, string content)
        {
            var path = Path.Combine(repo.WorkingDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public static async Task<string> CommitFileAsync(GitRepository repo, string relativePath, string content, string message)
        {
            WriteFile(repo, relativePath, content);
            await repo.Index.StageAsync(new[] { relativePath });
            var result = await repo.CommitAsync(message);
            if (!result.Succeeded) throw new InvalidOperationException("Commit failed: " + result.Message);
            return (await repo.ResolveRevisionAsync("HEAD"))!;
        }

        public GitSession CreateSession(string projectPath, FakeEditorHooks hooks)
            => new GitSession(projectPath, hooks, Options);

        public void Dispose()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!Directory.Exists(Root)) return;
                    foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(Root, true);
                    return;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }
    }
}
