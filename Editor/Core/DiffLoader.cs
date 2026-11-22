#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using FlexGit;

namespace UnityGit
{
    /// <summary>
    /// Turns FlexGit's <see cref="FileDiff"/> into the rows the Changes preview shows (spec §5.3, §5.4): hunks flattened
    /// with a gap row between them, at most <see cref="MaxRows"/> rows. Also reads the start of a big new file itself,
    /// so a scene of tens of MB is never read whole.
    /// </summary>
    internal static class DiffLoader
    {
        /// <summary>Unchanged lines shown around each change.</summary>
        public const int ContextLines = 3;

        /// <summary>The most rows a section shows; the lines after it are counted in <see cref="FileDiffView.HiddenLines"/>.</summary>
        public const int MaxRows = 1000;

        /// <summary>A new file larger than this is read in part by <see cref="ReadNewFileHead"/> instead of by FlexGit.</summary>
        public const long LargeNewFileBytes = 4 * 1024 * 1024;

        /// <summary>The most bytes <see cref="ReadNewFileHead"/> decodes into rows.</summary>
        public const int HeadReadBytes = 256 * 1024;

        // git's binary check: a NUL byte in the first 8000 bytes.
        const int BinaryProbeLength = 8000;
        const int CountBlockBytes = 64 * 1024;

        public static FileDiffView ToView(FileDiff diff, string fullPath, int maxRows = MaxRows)
        {
            var rows = new List<DiffRow>();
            int added = 0, removed = 0, hidden = 0;
            DiffHunk? previous = null;
            foreach (var hunk in diff.Hunks)
            {
                if (previous != null && rows.Count < maxRows)
                {
                    var gap = FirstOldLine(hunk) - EndOldLine(previous);
                    if (gap > 0) rows.Add(new DiffRow { Kind = DiffRowKind.Gap, GapLines = gap });
                }
                foreach (var line in hunk.Lines)
                {
                    var kind = ToKind(line.Kind);
                    if (kind == DiffRowKind.Added) added++;
                    else if (kind == DiffRowKind.Removed) removed++;
                    if (rows.Count < maxRows)
                        rows.Add(new DiffRow { Kind = kind, Text = line.Text, OldLine = line.OldLineNumber, NewLine = line.NewLineNumber });
                    else hidden++;
                }
                previous = hunk;
            }

            return new FileDiffView
            {
                Path = diff.Path,
                OldPath = diff.OldPath,
                FullPath = fullPath,
                Change = ToChange(diff.Change),
                IsUntracked = diff.IsUntracked,
                IsBinary = diff.IsBinary,
                IsLfs = diff.IsLfs,
                OldSize = diff.OldSize,
                NewSize = diff.NewSize,
                Rows = rows,
                HiddenLines = hidden,
                AddedCount = added,
                RemovedCount = removed,
            };
        }

        /// <summary>
        /// The start of a new file as every line added: its first <paramref name="maxRows"/> lines within
        /// <paramref name="maxBytes"/> bytes, the rest only counted. Binary (git's NUL check) gives no rows.
        /// </summary>
        public static FileDiffView ReadNewFileHead(string path, string fullPath, bool untracked, int maxRows = MaxRows, int maxBytes = HeadReadBytes,
            CancellationToken cancellationToken = default)
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var size = stream.Length;
            var head = new byte[(int)Math.Min(size, maxBytes)];
            var read = ReadUpTo(stream, head, head.Length);
            var view = new FileDiffView
            {
                Path = path,
                FullPath = fullPath,
                Change = DiffChange.Added,
                IsUntracked = untracked,
                NewSize = size,
            };
            if (Array.IndexOf(head, (byte)0, 0, Math.Min(read, BinaryProbeLength)) >= 0)
            {
                view.IsBinary = true;
                return view;
            }

            // Every line of the file, counted without decoding: the line breaks, plus a last line without one.
            long breaks = CountBreaks(head, read);
            var last = read > 0 ? head[read - 1] : (byte)'\n';
            var block = new byte[CountBlockBytes];
            int n;
            while ((n = stream.Read(block, 0, block.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                breaks += CountBreaks(block, n);
                last = block[n - 1];
            }
            var total = (int)Math.Min(int.MaxValue, breaks + (size > 0 && last != (byte)'\n' ? 1 : 0));

            var text = Encoding.UTF8.GetString(head, 0, read);
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            var lines = text.Split('\n');
            // The last piece is either "" after a final line break or a line the read cut short: neither is a whole line,
            // unless the cut line is the only one (a file of one huge line still shows its start).
            var whole = lines.Length - 1;
            if (read == size && text.Length > 0 && text[text.Length - 1] != '\n') whole = lines.Length;
            if (whole == 0 && text.Length > 0) whole = 1;

            var rows = new List<DiffRow>(Math.Min(whole, maxRows));
            for (var i = 0; i < whole && rows.Count < maxRows; i++)
            {
                var line = lines[i];
                if (line.Length > 0 && line[line.Length - 1] == '\r') line = line.Substring(0, line.Length - 1);
                rows.Add(new DiffRow { Kind = DiffRowKind.Added, Text = line, NewLine = i + 1 });
            }
            view.Rows = rows;
            view.AddedCount = total;
            view.HiddenLines = Math.Max(0, total - rows.Count);
            return view;
        }

        /// <summary>git's binary check on a file: a NUL byte in its first 8000 bytes.</summary>
        public static bool StartsBinary(string fullPath)
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var probe = new byte[BinaryProbeLength];
            var read = ReadUpTo(stream, probe, probe.Length);
            return Array.IndexOf(probe, (byte)0, 0, read) >= 0;
        }

        // The first old line a hunk covers. With no old lines, git gives the line it comes after.
        static int FirstOldLine(DiffHunk hunk) => hunk.OldCount > 0 ? hunk.OldStart : hunk.OldStart + 1;

        // The old line after the hunk's last one.
        static int EndOldLine(DiffHunk hunk) => hunk.OldCount > 0 ? hunk.OldStart + hunk.OldCount : hunk.OldStart + 1;

        static DiffRowKind ToKind(DiffLineKind kind)
        {
            switch (kind)
            {
                case DiffLineKind.Added: return DiffRowKind.Added;
                case DiffLineKind.Removed: return DiffRowKind.Removed;
                default: return DiffRowKind.Unchanged;
            }
        }

        static DiffChange ToChange(DiffChangeKind kind)
        {
            switch (kind)
            {
                case DiffChangeKind.Added: return DiffChange.Added;
                case DiffChangeKind.Deleted: return DiffChange.Deleted;
                case DiffChangeKind.Modified: return DiffChange.Modified;
                case DiffChangeKind.Renamed: return DiffChange.Renamed;
                default: return DiffChange.None;
            }
        }

        static long CountBreaks(byte[] buffer, int count)
        {
            long breaks = 0;
            for (var i = Array.IndexOf(buffer, (byte)'\n', 0, count); i >= 0; i = i + 1 < count ? Array.IndexOf(buffer, (byte)'\n', i + 1, count - i - 1) : -1)
                breaks++;
            return breaks;
        }

        static int ReadUpTo(Stream stream, byte[] buffer, int count)
        {
            var total = 0;
            int read;
            while (total < count && (read = stream.Read(buffer, total, count - total)) > 0) total += read;
            return total;
        }
    }
}
