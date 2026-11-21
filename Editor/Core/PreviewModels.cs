#nullable enable
using System;
using System.Collections.Generic;

namespace UnityGit
{
    /// <summary>How a file differs between the last commit and the working tree.</summary>
    public enum DiffChange { None, Added, Deleted, Modified, Renamed }

    /// <summary>What a row of the preview is: a line on both sides, a removed or added line, or a gap between changes.</summary>
    public enum DiffRowKind { Unchanged, Removed, Added, Gap }

    /// <summary>One row of a file's preview.</summary>
    public sealed class DiffRow
    {
        public DiffRowKind Kind { get; internal set; }

        /// <summary>The line's text without its line break; empty for a <see cref="DiffRowKind.Gap"/>.</summary>
        public string Text { get; internal set; } = "";

        /// <summary>1-based line number in the last commit; null for added lines and gaps.</summary>
        public int? OldLine { get; internal set; }

        /// <summary>1-based line number on disk; null for removed lines and gaps.</summary>
        public int? NewLine { get; internal set; }

        /// <summary>How many unchanged lines a <see cref="DiffRowKind.Gap"/> stands for; 0 for other rows.</summary>
        public int GapLines { get; internal set; }

        internal DiffRow() { }
    }

    /// <summary>
    /// What changed in one file between the last commit and the working tree (staged and unstaged together, what Commit
    /// takes), as rows ready to show. At most <see cref="DiffLoader.MaxRows"/> rows; the counts cover the whole file.
    /// </summary>
    public sealed class FileDiffView
    {
        /// <summary>Repo-relative, '/' separators.</summary>
        public string Path { get; internal set; } = "";

        /// <summary>The path in the last commit for a rename, otherwise null.</summary>
        public string? OldPath { get; internal set; }

        /// <summary>The file on disk (it may not exist: a deleted file).</summary>
        public string FullPath { get; internal set; } = "";

        public DiffChange Change { get; internal set; }

        /// <summary>A new file that isn't staged.</summary>
        public bool IsUntracked { get; internal set; }

        /// <summary>git sees binary content: no rows, show the sizes.</summary>
        public bool IsBinary { get; internal set; }

        /// <summary>Stored in Git LFS: no rows (git only compares pointers); the sizes are the real content's.</summary>
        public bool IsLfs { get; internal set; }

        /// <summary>Bytes in the last commit; null when the file isn't there.</summary>
        public long? OldSize { get; internal set; }

        /// <summary>Bytes on disk; null when the file isn't there.</summary>
        public long? NewSize { get; internal set; }

        public IReadOnlyList<DiffRow> Rows { get; internal set; } = Array.Empty<DiffRow>();

        /// <summary>Lines left out after the last row because of the row cap.</summary>
        public int HiddenLines { get; internal set; }

        /// <summary>Added lines in the whole file.</summary>
        public int AddedCount { get; internal set; }

        /// <summary>Removed lines in the whole file.</summary>
        public int RemovedCount { get; internal set; }

        internal FileDiffView() { }
    }

    /// <summary>The preview of one row of the Changes list: the file, and its .meta when the row holds one.</summary>
    public sealed class ChangePreview
    {
        public FileDiffView File { get; internal set; } = new FileDiffView();
        public FileDiffView? Meta { get; internal set; }
        internal ChangePreview() { }
    }
}
