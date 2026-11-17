#nullable enable
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit
{
    internal static class SnapshotLoader
    {
        public static async Task<RepoSnapshot> LoadAsync(GitRepository repo, string projectPath, bool includeLfsLocks, CancellationToken ct)
        {
            var status = await repo.GetStatusAsync(null, ct).ConfigureAwait(false);
            return new RepoSnapshot
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
                Changes = MetaPairing.Group(status.Entries.Where(e => !e.IsIgnored).Select(ToFileChange).ToList()),
            };
        }

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
}
