#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FlexGit;

namespace UnityGit
{
    /// <summary>
    /// Maps FlexGit's conflict types onto the model types. A conflicted Git LFS file is mapped as binary: FlexGit decides
    /// "binary" by a NUL byte, so it reads the pointer text git merged (with conflict markers) as a text file with one
    /// place. Picking that place would leave pointer text in the working tree, so only a whole side is offered, and git
    /// checkout's LFS filter puts back the real file.
    /// </summary>
    internal static class ConflictLoader
    {
        /// <summary>The first line of every Git LFS pointer file.</summary>
        internal const string LfsPointerFirstLine = "version https://git-lfs.github.com/spec/v1";

        // How much of a file the overview reads to find its first line: more than LfsPointerFirstLine and a CR LF.
        const int FirstLineBytes = 64;

        /// <summary>Whether a file whose first line is <paramref name="firstLine"/> is a Git LFS pointer.</summary>
        internal static bool IsLfsPointer(string? firstLine) => string.Equals(firstLine, LfsPointerFirstLine, StringComparison.Ordinal);

        /// <summary>The text up to its first line break, without a trailing CR.</summary>
        internal static string FirstLine(string text)
        {
            var end = text.IndexOf('\n');
            var line = end < 0 ? text : text.Substring(0, end);
            return line.EndsWith("\r", StringComparison.Ordinal) ? line.Substring(0, line.Length - 1) : line;
        }

        internal static ConflictChoice ToChoice(ConflictPick pick)
        {
            switch (pick)
            {
                case ConflictPick.Ours: return ConflictChoice.Ours;
                case ConflictPick.Theirs: return ConflictChoice.Theirs;
                case ConflictPick.OursThenTheirs: return ConflictChoice.OursThenTheirs;
                default: return ConflictChoice.TheirsThenOurs;
            }
        }

        internal static ConflictType ToType(ConflictKind kind)
        {
            switch (kind)
            {
                case ConflictKind.BothDeleted: return ConflictType.BothDeleted;
                case ConflictKind.AddedByUs: return ConflictType.AddedByUs;
                case ConflictKind.AddedByThem: return ConflictType.AddedByThem;
                case ConflictKind.DeletedByUs: return ConflictType.DeletedByUs;
                case ConflictKind.DeletedByThem: return ConflictType.DeletedByThem;
                case ConflictKind.BothAdded: return ConflictType.BothAdded;
                default: return ConflictType.BothModified;
            }
        }

        /// <summary>
        /// The overview keeps no file content, so the first line of each text file in the working tree is read again
        /// (<paramref name="fullPath"/> maps a repository path to the file) to find LFS pointers.
        /// </summary>
        internal static ConflictOverviewInfo ToOverview(ConflictOverview overview, Func<string, string> fullPath) => new ConflictOverviewInfo
        {
            Operation = SnapshotLoader.ToOperation(overview.Operation),
            Files = overview.Files.Select(f =>
            {
                var lfs = f.ExistsInWorkingTree && !f.IsBinary && !f.IsSubmodule && IsLfsPointer(ReadFirstLine(fullPath(f.Path)));
                return new ConflictFileInfo
                {
                    Path = f.Path,
                    Type = ToType(f.Kind),
                    IsBinary = f.IsBinary || lfs,
                    IsSubmodule = f.IsSubmodule,
                    ExistsInWorkingTree = f.ExistsInWorkingTree,
                    HasMalformedMarkers = f.HasMalformedMarkers && !lfs,
                    PlaceCount = lfs ? 0 : f.HunkCount,
                };
            }).ToList(),
        };

        internal static ConflictFileView ToView(ConflictedFile file, string fullPath, string oursName, string theirsName)
        {
            var segments = file.Segments;
            // A pointer's first line is the same on both sides, so git leaves it outside the markers.
            var lfs = !file.IsBinary && segments.Count > 0 && segments[0].Hunk == null && IsLfsPointer(FirstLine(segments[0].Text ?? string.Empty));
            var places = new List<ConflictPlace>();
            var parts = new List<ConflictPart>();
            foreach (var segment in lfs ? Array.Empty<ConflictSegment>() : segments)
            {
                var hunk = segment.Hunk;
                if (hunk == null)
                {
                    parts.Add(new ConflictPart { Text = segment.Text });
                    continue;
                }
                var place = new ConflictPlace
                {
                    Index = hunk.Index,
                    StartLine = hunk.StartLine,
                    EndLine = hunk.EndLine,
                    Ours = hunk.Ours,
                    Theirs = hunk.Theirs,
                    Base = hunk.Base,
                    OursName = oursName,
                    TheirsName = theirsName,
                };
                places.Add(place);
                parts.Add(new ConflictPart { Place = place });
            }

            return new ConflictFileView
            {
                Info = new ConflictFileInfo
                {
                    Path = file.Path,
                    Type = ToType(file.Kind),
                    IsBinary = file.IsBinary || lfs,
                    IsSubmodule = file.IsSubmodule,
                    ExistsInWorkingTree = file.ExistsInWorkingTree,
                    HasMalformedMarkers = file.HasMalformedMarkers && !lfs,
                    PlaceCount = places.Count,
                },
                FullPath = fullPath,
                OursName = oursName,
                TheirsName = theirsName,
                Parts = parts,
                Places = places,
                IsSceneOrPrefab = file.Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                    || file.Path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase),
                Source = file,
            };
        }

        // The first line of a working-tree file, from a bounded read (a longer first line comes back cut, so it doesn't
        // match). Null when the file can't be read: then it just isn't taken for an LFS pointer.
        static string? ReadFirstLine(string fullPath)
        {
            try
            {
                var buffer = new byte[FirstLineBytes];
                var read = 0;
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int n;
                    while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0) read += n;
                }
                return FirstLine(Encoding.UTF8.GetString(buffer, 0, read));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
