#nullable enable
using System;
using System.Collections.Generic;

namespace UnityGit
{
    public enum RepoAvailability { Unknown, Ready, NotARepository, GitMissing }
    public enum RepoOperation { None, Merging, Rebasing, CherryPicking, Reverting, Bisecting }
    public enum ChangeKind { Modified, Added, Deleted, Renamed, Copied, TypeChanged, Untracked, Conflicted }

    public sealed class ChangeItem
    {
        public string Path { get; internal set; } = "";
        public IReadOnlyList<string> GitPaths { get; internal set; } = Array.Empty<string>();
        public ChangeKind Kind { get; internal set; }
        public bool IsConflicted { get; internal set; }
        public bool IsStaged { get; internal set; }
        public string? OriginalPath { get; internal set; }
        public bool HasMeta { get; internal set; }
        internal ChangeItem() { }
    }

    public sealed class BranchItem
    {
        public string Name { get; internal set; } = "";
        public string ShortName { get; internal set; } = "";
        public bool IsRemote { get; internal set; }
        public string? RemoteName { get; internal set; }
        public string? Upstream { get; internal set; }
        public bool IsUpstreamGone { get; internal set; }
        public int Ahead { get; internal set; }
        public int Behind { get; internal set; }
        public bool IsCurrent { get; internal set; }
        public DateTimeOffset? LastCommitDate { get; internal set; }
        public string LastCommitSubject { get; internal set; } = "";
        internal BranchItem() { }
    }

    public sealed class AuthorItem
    {
        public string Name { get; internal set; } = "";
        public string Email { get; internal set; } = "";
        public int CommitCount { get; internal set; }
        internal AuthorItem() { }
    }

    public sealed class CommitItem
    {
        public string Sha { get; internal set; } = "";
        public string ShortSha { get; internal set; } = "";
        public string Subject { get; internal set; } = "";
        public string AuthorName { get; internal set; } = "";
        public string AuthorEmail { get; internal set; } = "";
        public DateTimeOffset Date { get; internal set; }
        internal CommitItem() { }
    }

    public sealed class RemoteItem
    {
        public string Name { get; internal set; } = "";
        public string FetchUrl { get; internal set; } = "";
        public string PushUrl { get; internal set; } = "";
        internal RemoteItem() { }
    }

    public sealed class TagItem
    {
        public string Name { get; internal set; } = "";
        public string TargetSha { get; internal set; } = "";
        public bool IsAnnotated { get; internal set; }
        public string? Message { get; internal set; }
        internal TagItem() { }
    }

    public sealed class StashItem
    {
        public int Index { get; internal set; }
        public string Name { get; internal set; } = "";
        public string Message { get; internal set; } = "";
        public DateTimeOffset? Date { get; internal set; }
        internal StashItem() { }
    }

    public sealed class LfsInfo
    {
        public bool GitLfsAvailable { get; internal set; }
        public bool UsedByRepo { get; internal set; }
        public bool HooksInstalled { get; internal set; }
        internal LfsInfo() { }
    }

    public sealed class LfsLockItem
    {
        public string Id { get; internal set; } = "";
        public string Path { get; internal set; } = "";
        public string OwnerName { get; internal set; } = "";
        public DateTimeOffset? LockedAt { get; internal set; }
        internal LfsLockItem() { }
    }
}
