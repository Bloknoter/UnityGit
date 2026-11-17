#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit
{
    internal static class SnapshotLoader
    {
        internal const int RecentCommitLimit = 50;

        public static async Task<RepoSnapshot> LoadAsync(GitRepository repo, string projectPath, bool includeLfsLocks, CancellationToken ct, Action<string>? log = null)
        {
            var status = await repo.GetStatusAsync(null, ct).ConfigureAwait(false);
            var snapshot = new RepoSnapshot
            {
                Availability = RepoAvailability.Ready,
                ProjectPath = projectPath,
                RepoRoot = repo.WorkingDirectory,
                Branch = status.Branch,
                HeadSha = status.HeadSha,
                IsDetached = status.IsDetachedHead,
                IsUnborn = status.IsUnborn,
                Upstream = status.Upstream,
                Ahead = status.Ahead,
                Behind = status.Behind,
                Operation = ToOperation(repo.GetState()),
                Changes = ToChanges(status),
            };
            await LoadDetailsAsync(snapshot, repo, status.IsUnborn, includeLfsLocks, log, ct).ConfigureAwait(false);
            return snapshot;
        }

        static async Task LoadDetailsAsync(RepoSnapshot snapshot, GitRepository repo, bool unborn, bool includeLfsLocks, Action<string>? log, CancellationToken ct)
        {
            snapshot.LocalBranches = (await repo.Branches.ListAsync(BranchScope.Local, ct).ConfigureAwait(false))
                .Select(ToBranchItem).ToList();
            snapshot.RemoteBranches = (await repo.Branches.ListAsync(BranchScope.Remote, ct).ConfigureAwait(false))
                .Where(b => !b.Name.EndsWith("/HEAD", StringComparison.Ordinal))
                .Select(ToBranchItem).ToList();

            if (!unborn)
            {
                var commits = await repo.History.GetLogAsync(new LogQuery { MaxCount = RecentCommitLimit }, ct).ConfigureAwait(false);
                snapshot.RecentCommits = commits.Select(c => new CommitItem
                {
                    Sha = c.Sha,
                    ShortSha = c.ShortSha,
                    Subject = c.Subject,
                    AuthorName = c.Author.Name,
                    AuthorEmail = c.Author.Email,
                    Date = c.Author.When,
                }).ToList();

                var shortlog = await repo.RunRawAsync(new[] { "shortlog", "-sne", "HEAD" }, null, ct).ConfigureAwait(false);
                if (shortlog.Succeeded) snapshot.Authors = AuthorsParser.Parse(shortlog.StandardOutput);
            }

            snapshot.Remotes = (await repo.Remotes.ListAsync(ct).ConfigureAwait(false))
                .Select(r => new RemoteItem { Name = r.Name, FetchUrl = r.FetchUrl, PushUrl = r.PushUrl }).ToList();
            snapshot.Tags = (await repo.Tags.ListAsync(ct).ConfigureAwait(false))
                .Select(t => new TagItem { Name = t.Name, TargetSha = t.TargetSha, IsAnnotated = t.IsAnnotated, Message = t.Message }).ToList();
            snapshot.Stashes = (await repo.Stash.ListAsync(ct).ConfigureAwait(false))
                .Select(s => new StashItem { Index = s.Index, Name = s.Name, Message = s.Message, Date = s.Date }).ToList();

            var identity = await repo.Config.GetUserIdentityAsync(ct).ConfigureAwait(false);
            snapshot.UserName = identity?.Name;
            snapshot.UserEmail = identity?.Email;

            var lfs = new LfsInfo { GitLfsAvailable = repo.Lfs.IsInstalled };
            lfs.UsedByRepo = await repo.Lfs.IsUsedInRepositoryAsync(ct).ConfigureAwait(false);
            lfs.HooksInstalled = await LfsHooksCheck.AreInstalledAsync(repo, ct).ConfigureAwait(false);
            snapshot.Lfs = lfs;

            if (includeLfsLocks && lfs.UsedByRepo && lfs.GitLfsAvailable)
            {
                try
                {
                    snapshot.LfsLocks = (await repo.Lfs.ListLocksAsync(false, null, ct).ConfigureAwait(false))
                        .Select(l => new LfsLockItem { Id = l.Id, Path = l.Path, OwnerName = l.OwnerName, LockedAt = l.LockedAt }).ToList();
                }
                catch (GitCommandException ex)
                {
                    log?.Invoke(ex.Message);
                }
            }
        }

        static BranchItem ToBranchItem(BranchInfo b)
        {
            var prefix = b.RemoteName + "/";
            return new BranchItem
            {
                Name = b.Name,
                ShortName = b.IsRemote && b.RemoteName != null && b.Name.StartsWith(prefix, StringComparison.Ordinal)
                    ? b.Name.Substring(prefix.Length) : b.Name,
                IsRemote = b.IsRemote,
                RemoteName = b.RemoteName,
                Upstream = b.Upstream,
                IsUpstreamGone = b.IsUpstreamGone,
                Ahead = b.Ahead,
                Behind = b.Behind,
                IsCurrent = b.IsCurrent,
                LastCommitDate = b.LastCommitDate,
                LastCommitSubject = b.LastCommitSubject,
            };
        }

        /// <summary>The Changes rows for a status. Commit reloads its rows through this too, so both see the same list.</summary>
        internal static IReadOnlyList<ChangeItem> ToChanges(RepositoryStatus status)
            => MetaPairing.Group(status.Entries.Where(e => !e.IsIgnored).Select(ToFileChange).ToList());

        internal static FileChange ToFileChange(StatusEntry entry)
        {
            ChangeKind kind;
            if (entry.IsConflicted) kind = ChangeKind.Conflicted;
            else if (entry.IsUntracked) kind = ChangeKind.Untracked;
            else kind = MapKind(entry.IndexStatus != FileStatus.Unmodified ? entry.IndexStatus : entry.WorkTreeStatus);

            return new FileChange
            {
                Path = entry.Path,
                OriginalPath = entry.OriginalPath,
                Kind = kind,
                IsConflicted = entry.IsConflicted,
                IsStaged = entry.IsStaged,
            };
        }

        static ChangeKind MapKind(FileStatus status)
        {
            switch (status)
            {
                case FileStatus.Added: return ChangeKind.Added;
                case FileStatus.Deleted: return ChangeKind.Deleted;
                case FileStatus.Renamed: return ChangeKind.Renamed;
                case FileStatus.Copied: return ChangeKind.Copied;
                case FileStatus.TypeChanged: return ChangeKind.TypeChanged;
                case FileStatus.Unmerged: return ChangeKind.Conflicted;
                case FileStatus.Untracked: return ChangeKind.Untracked;
                default: return ChangeKind.Modified;
            }
        }

        internal static RepoOperation ToOperation(RepositoryState state)
        {
            switch (state)
            {
                case RepositoryState.Merging: return RepoOperation.Merging;
                case RepositoryState.Rebasing: return RepoOperation.Rebasing;
                case RepositoryState.CherryPicking: return RepoOperation.CherryPicking;
                case RepositoryState.Reverting: return RepoOperation.Reverting;
                case RepositoryState.Bisecting: return RepoOperation.Bisecting;
                default: return RepoOperation.None;
            }
        }
    }

    internal static class AuthorsParser
    {
        static readonly Regex Line = new Regex(@"^\s*(\d+)\t(.*?)\s*<([^>]*)>\s*$", RegexOptions.Compiled);

        public static IReadOnlyList<AuthorItem> Parse(string shortlogOutput)
        {
            var result = new List<AuthorItem>();
            foreach (var raw in shortlogOutput.Split('\n'))
            {
                var m = Line.Match(raw.TrimEnd('\r'));
                if (!m.Success) continue;
                result.Add(new AuthorItem
                {
                    CommitCount = int.Parse(m.Groups[1].Value),
                    Name = m.Groups[2].Value,
                    Email = m.Groups[3].Value,
                });
            }
            return result.OrderByDescending(a => a.CommitCount).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();
        }
    }

    internal static class LfsHooksCheck
    {
        public static async Task<bool> AreInstalledAsync(GitRepository repo, CancellationToken ct)
        {
            var filter = await repo.Config.GetAsync("filter.lfs.process", null, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(filter)) return false;

            var result = await repo.RunRawAsync(new[] { "rev-parse", "--git-path", "hooks/pre-push" }, null, ct).ConfigureAwait(false);
            if (!result.Succeeded) return false;
            var path = result.StandardOutput.Trim();
            if (path.Length == 0) return false;
            if (!Path.IsPathRooted(path)) path = Path.Combine(repo.WorkingDirectory, path);
            if (!File.Exists(path)) return false;

            var content = File.ReadAllText(path);
            return content.Contains("git lfs") || content.Contains("git-lfs");
        }
    }
}
