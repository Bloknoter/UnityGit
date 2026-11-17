#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;
using NUnit.Framework;

namespace UnityGit.Tests
{
    [TestFixture, Category("Integration")]
    public class CommitActionTests
    {
        GitSandbox _sb = null!;

        [SetUp] public async Task SetUp() => _sb = await GitSandbox.CreateAsync();
        [TearDown] public void TearDown() => _sb.Dispose();

        // A repo with one initial commit, and a session on it.
        async Task<(GitRepository repo, GitSession s, FakeEditorHooks hooks)> CreateAsync()
        {
            var repo = await _sb.InitRepoAsync("p");
            await GitSandbox.CommitFileAsync(repo, "README.md", "readme\n", "initial");
            var hooks = new FakeEditorHooks();
            return (repo, _sb.CreateSession(repo.WorkingDirectory, hooks), hooks);
        }

        // The default setup: a.txt written (so Commit is enabled) and the session refreshed.
        async Task<(GitRepository repo, GitSession s, FakeEditorHooks hooks)> CreateWithChangeAsync()
        {
            var (repo, s, hooks) = await CreateAsync();
            GitSandbox.WriteFile(repo, "a.txt", "a");
            await s.RefreshAsync();
            return (repo, s, hooks);
        }

        // A local repo stopped in a merge with README.md conflicted.
        async Task<GitRepository> ConflictedMergeAsync()
        {
            var trio = await _sb.CreateTrioAsync();
            await trio.PrepareConflictAsync();
            await trio.Local.RunRawAsync("pull", "--no-rebase"); // Exits non-zero: the conflict is the point.
            return trio.Local;
        }

        static string[] Rows(GitSession s) => s.Snapshot.Changes.Select(c => c.Path).ToArray();

        static Task<string?> Head(GitRepository repo) => repo.ResolveRevisionAsync("HEAD");

        static GitResult Ok(GitResult r)
        {
            Assert.That(r.Succeeded, Is.True, $"{r.CommandLine} failed with exit code {r.ExitCode}: {r.StandardError}");
            return r;
        }

        static async Task<string[]> HeadFiles(GitRepository repo)
        {
            var r = Ok(await repo.RunRawAsync("show", "--name-only", "--format=", "HEAD"));
            return r.StandardOutput.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        }

        [Test] public async Task Commit_OnlyTickedRows()
        {
            var (repo, s, _) = await CreateAsync();
            GitSandbox.WriteFile(repo, "a.txt", "a");
            GitSandbox.WriteFile(repo, "b.txt", "b");
            await s.RefreshAsync();
            var o = await s.CommitAsync("add a", new[] { "a.txt" }, new[] { "a.txt", "b.txt" });
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Committed 1 file")));
            Assert.That(await HeadFiles(repo), Is.EquivalentTo(new[] { "a.txt" }));
            Assert.That(Rows(s), Is.EqualTo(new[] { "b.txt" }));
        }

        [Test] public async Task Commit_UntickedStagedFile_IsLeftOut()
        {
            var (repo, s, _) = await CreateAsync();
            GitSandbox.WriteFile(repo, "a.txt", "a");
            GitSandbox.WriteFile(repo, "b.txt", "b");
            await repo.Index.StageAsync(new[] { "b.txt" });
            await s.RefreshAsync();
            var o = await s.CommitAsync("add a", new[] { "a.txt" }, Rows(s));
            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded));
            Assert.That(await HeadFiles(repo), Is.EquivalentTo(new[] { "a.txt" }));
            Assert.That(Rows(s), Is.EqualTo(new[] { "b.txt" }));
        }

        [Test] public async Task Commit_AssetAndMeta_CommittedTogether()
        {
            var (repo, s, _) = await CreateAsync();
            GitSandbox.WriteFile(repo, "Assets/X.asset", "x");
            GitSandbox.WriteFile(repo, "Assets/X.asset.meta", "guid: 1");
            await s.RefreshAsync();
            var o = await s.CommitAsync("add X", new[] { "Assets/X.asset" }, Rows(s));
            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded));
            Assert.That(await HeadFiles(repo), Is.EquivalentTo(new[] { "Assets/X.asset", "Assets/X.asset.meta" }));
        }

        [Test] public async Task Commit_StagedRename_IsCommitted()
        {
            var repo = await _sb.InitRepoAsync("p");
            GitSandbox.WriteFile(repo, "Assets/Old.asset", "asset content\n");
            GitSandbox.WriteFile(repo, "Assets/Old.asset.meta", "guid: 1\n");
            await repo.Index.StageAsync(new[] { "Assets/Old.asset", "Assets/Old.asset.meta" });
            var initial = await repo.CommitAsync("initial");
            Assert.That(initial.Succeeded, Is.True, initial.Message);
            Ok(await repo.RunRawAsync("mv", "Assets/Old.asset", "Assets/New.asset"));
            Ok(await repo.RunRawAsync("mv", "Assets/Old.asset.meta", "Assets/New.asset.meta"));
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(Rows(s), Is.EqualTo(new[] { "Assets/New.asset" }));
            Assert.That(s.Snapshot.Changes[0].Kind, Is.EqualTo(ChangeKind.Renamed));

            var o = await s.CommitAsync("rename", Rows(s), Rows(s));
            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded), o.Message);
            var show = Ok(await repo.RunRawAsync("show", "--name-status", "--format=", "-M", "HEAD"));
            var renames = show.StandardOutput.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)
                .Select(l => l.Split('\t')).Select(f => $"{f[0][0]} {f[1]} -> {f[f.Length - 1]}");
            Assert.That(renames, Is.EquivalentTo(new[]
            {
                "R Assets/Old.asset -> Assets/New.asset",
                "R Assets/Old.asset.meta -> Assets/New.asset.meta",
            }));
            Assert.That(s.Snapshot.Changes, Is.Empty);
        }

        [Test] public async Task Commit_CallsSaveThenConfirmThenBeginEnd()
        {
            var (_, s, hooks) = await CreateWithChangeAsync();
            var o = await s.CommitAsync("add a", new[] { "a.txt" }, Rows(s));
            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded));
            Assert.That(hooks.Calls, Is.EqualTo(new[] { "SaveAssets", "ConfirmScenes", "Begin", "End" }));
        }

        [Test] public async Task Commit_ScenesDeclined_NothingCommitted()
        {
            var (repo, s, hooks) = await CreateWithChangeAsync();
            var head = await Head(repo);
            hooks.ConfirmScenesResult = false;
            var o = await s.CommitAsync("add a", new[] { "a.txt" }, Rows(s));
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Cancelled, "Commit cancelled — modified scenes weren't saved")));
            Assert.That(await Head(repo), Is.EqualTo(head));
            Assert.That(hooks.Calls, Does.Not.Contain("Begin"));
        }

        [Test] public async Task Commit_SaveRevealsUnseenFile_Stops()
        {
            var (repo, s, hooks) = await CreateWithChangeAsync();
            var head = await Head(repo);
            hooks.OnSaveAssets = () => GitSandbox.WriteFile(repo, "Assets/Saved.asset", "saved");
            var o = await s.CommitAsync("add a", new[] { "a.txt" }, new[] { "a.txt" });
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Unsaved changes were saved — review the list and commit again.")));
            Assert.That(await Head(repo), Is.EqualTo(head));
            Assert.That(Rows(s), Does.Contain("Assets/Saved.asset"));
        }

        [Test] public async Task Commit_EmptyMessage_Refused()
        {
            var (_, s, hooks) = await CreateWithChangeAsync();
            var o = await s.CommitAsync("  ", new[] { "a.txt" }, Rows(s));
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Enter a commit message")));
            Assert.That(hooks.Calls, Is.Empty);
        }

        [Test] public async Task Commit_NothingTicked_Refused()
        {
            var (_, s, _) = await CreateWithChangeAsync();
            var o = await s.CommitAsync("add a", Array.Empty<string>(), Rows(s));
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Tick at least one file")));
        }

        [Test] public async Task Commit_WithConflicts_Refused()
        {
            var repo = await ConflictedMergeAsync();
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(s.Snapshot.HasConflicts, Is.True);
            var o = await s.CommitAsync("merge", Rows(s), Rows(s));
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Refused, "Resolve conflicts first")));
        }

        [Test] public async Task Commit_DuringResolvedMerge_RequiresAllRows()
        {
            var repo = await ConflictedMergeAsync();
            GitSandbox.WriteFile(repo, "README.md", "merged line\n");
            var resolved = await repo.Conflicts.MarkResolvedAsync(new[] { "README.md" });
            Assert.That(resolved.Succeeded, Is.True, resolved.Message);
            GitSandbox.WriteFile(repo, "c.txt", "c");
            var s = _sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That((s.Snapshot.Operation, s.Snapshot.HasConflicts), Is.EqualTo((RepoOperation.Merging, false)));

            var partial = await s.CommitAsync("merge", new[] { "README.md" }, Rows(s));
            Assert.That((partial.Kind, partial.Message), Is.EqualTo((OutcomeKind.Refused, "Finish the merge by committing all files")));

            var all = await s.CommitAsync("merge", Rows(s), Rows(s));
            Assert.That((all.Kind, all.Message), Is.EqualTo((OutcomeKind.Succeeded, "Committed 2 files")));
            Assert.That(s.Snapshot.Operation, Is.EqualTo(RepoOperation.None));
            Assert.That((await repo.History.GetCommitAsync("HEAD"))!.IsMerge, Is.True);
        }

        [Test] public async Task Commit_PathWithSpacesAndCyrillic_IsCommitted()
        {
            const string scene = "Assets/Мои сцены/Level 1.unity";
            var (repo, s, _) = await CreateAsync();
            GitSandbox.WriteFile(repo, scene, "scene");
            GitSandbox.WriteFile(repo, scene + ".meta", "guid: 2");
            await s.RefreshAsync();
            var o = await s.CommitAsync("add level", new[] { scene }, Rows(s));
            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded));
            Assert.That(await HeadFiles(repo), Is.EquivalentTo(new[] { scene, scene + ".meta" }));
        }

        [Test] public async Task Commit_InUnbornRepo_CreatesFirstCommit()
        {
            var dir = _sb.MakeDir("p");
            var s = _sb.CreateSession(dir, new FakeEditorHooks());
            await s.RefreshAsync();
            var init = await s.InitAsync();
            Assert.That(init.Succeeded, Is.True, init.Message);
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "b");
            await s.RefreshAsync();
            var o = await s.CommitAsync("first", new[] { "a.txt" }, Rows(s));
            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded));
            Assert.That(s.Snapshot.IsUnborn, Is.False);
            Assert.That(Rows(s), Is.EqualTo(new[] { ".gitignore", "b.txt" }));
        }

        [Test] public async Task Commit_WithoutIdentity_SaysHowToFix()
        {
            using var sb = await GitSandbox.CreateAsync(withIdentity: false);
            var repo = await sb.InitRepoAsync("p");
            GitSandbox.WriteFile(repo, "a.txt", "a");
            var s = sb.CreateSession(repo.WorkingDirectory, new FakeEditorHooks());
            await s.RefreshAsync();
            var o = await s.CommitAsync("first", new[] { "a.txt" }, Rows(s));
            Assert.That((o.Kind, o.Message),
                Is.EqualTo((OutcomeKind.Failed, "Git doesn't know who you are — set user.name and user.email in your git config")));
        }

        [Test] public async Task Commit_ProjectInRepoSubfolder_CommitsSelectedFile()
        {
            var repo = await _sb.InitRepoAsync("repo");
            await GitSandbox.CommitFileAsync(repo, "README.md", "readme\n", "initial");
            GitSandbox.WriteFile(repo, "UnityProject/Assets/a.txt", "a");
            var s = _sb.CreateSession(_sb.MakeDir("repo/UnityProject"), new FakeEditorHooks());
            await s.RefreshAsync();
            Assert.That(Rows(s), Is.EqualTo(new[] { "UnityProject/Assets/a.txt" }));
            var o = await s.CommitAsync("add a", new[] { "UnityProject/Assets/a.txt" }, Rows(s));
            Assert.That(o.Kind, Is.EqualTo(OutcomeKind.Succeeded));
            Assert.That(await HeadFiles(repo), Is.EquivalentTo(new[] { "UnityProject/Assets/a.txt" }));
        }

        [Test] public async Task Commit_ThousandFiles_AllCommitted()
        {
            var (repo, s, _) = await CreateAsync();
            for (var i = 0; i < 1000; i++)
            {
                GitSandbox.WriteFile(repo, $"Assets/Gen/F{i:D4}.asset", "asset " + i);
                GitSandbox.WriteFile(repo, $"Assets/Gen/F{i:D4}.asset.meta", "guid: " + i);
            }
            await s.RefreshAsync();
            Assert.That(Rows(s), Has.Length.EqualTo(1000));
            var o = await s.CommitAsync("generate", Rows(s), Rows(s));
            Assert.That((o.Kind, o.Message), Is.EqualTo((OutcomeKind.Succeeded, "Committed 1000 files")));
            Assert.That(s.Snapshot.Changes, Is.Empty);
        }
    }
}
