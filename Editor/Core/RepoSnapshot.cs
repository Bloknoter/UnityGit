#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityGit
{
    public sealed class RepoSnapshot
    {
        public RepoAvailability Availability { get; internal set; }
        public string ProjectPath { get; internal set; } = "";
        public string? RepoRoot { get; internal set; }
        public string? Message { get; internal set; }

        public string? Branch { get; internal set; }
        public string? HeadSha { get; internal set; }
        public bool IsDetached { get; internal set; }
        public bool IsUnborn { get; internal set; }
        public string? Upstream { get; internal set; }
        public int Ahead { get; internal set; }
        public int Behind { get; internal set; }
        public RepoOperation Operation { get; internal set; }

        public IReadOnlyList<ChangeItem> Changes { get; internal set; } = Array.Empty<ChangeItem>();
        /// <summary>Every unmerged path from status (an asset and its .meta separately), repo-relative.</summary>
        public IReadOnlyList<string> ConflictedPaths { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<BranchItem> LocalBranches { get; internal set; } = Array.Empty<BranchItem>();
        public IReadOnlyList<BranchItem> RemoteBranches { get; internal set; } = Array.Empty<BranchItem>();
        public IReadOnlyList<AuthorItem> Authors { get; internal set; } = Array.Empty<AuthorItem>();
        public IReadOnlyList<CommitItem> RecentCommits { get; internal set; } = Array.Empty<CommitItem>();
        public IReadOnlyList<RemoteItem> Remotes { get; internal set; } = Array.Empty<RemoteItem>();
        public IReadOnlyList<TagItem> Tags { get; internal set; } = Array.Empty<TagItem>();
        public IReadOnlyList<StashItem> Stashes { get; internal set; } = Array.Empty<StashItem>();

        public string? UserName { get; internal set; }
        public string? UserEmail { get; internal set; }
        public LfsInfo Lfs { get; internal set; } = new LfsInfo();

        /// <summary>Null means the lock list has not been loaded.</summary>
        public IReadOnlyList<LfsLockItem>? LfsLocks { get; internal set; }

        public bool HasConflicts => Changes.Any(c => c.IsConflicted);

        internal RepoSnapshot() { }

        public static RepoSnapshot Loading(string projectPath)
            => new RepoSnapshot { Availability = RepoAvailability.Unknown, ProjectPath = projectPath };

        public static RepoSnapshot NotARepository(string projectPath)
            => new RepoSnapshot { Availability = RepoAvailability.NotARepository, ProjectPath = projectPath };

        public static RepoSnapshot GitMissing(string projectPath, string message)
            => new RepoSnapshot { Availability = RepoAvailability.GitMissing, ProjectPath = projectPath, Message = message };

        /// <summary>A repository git can't open or read; <see cref="Message"/> is git's own explanation.</summary>
        public static RepoSnapshot Unavailable(string projectPath, string message)
            => new RepoSnapshot { Availability = RepoAvailability.Unavailable, ProjectPath = projectPath, Message = message };
    }
}
