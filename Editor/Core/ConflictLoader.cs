#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FlexGit;

namespace UnityGit
{
    /// <summary>Maps FlexGit's conflict types onto the model types.</summary>
    internal static class ConflictLoader
    {
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

        internal static ConflictOverviewInfo ToOverview(ConflictOverview overview) => new ConflictOverviewInfo
        {
            Operation = SnapshotLoader.ToOperation(overview.Operation),
            Files = overview.Files.Select(f => new ConflictFileInfo
            {
                Path = f.Path,
                Type = ToType(f.Kind),
                IsBinary = f.IsBinary,
                IsSubmodule = f.IsSubmodule,
                ExistsInWorkingTree = f.ExistsInWorkingTree,
                HasMalformedMarkers = f.HasMalformedMarkers,
                PlaceCount = f.HunkCount,
            }).ToList(),
        };

        internal static ConflictFileView ToView(ConflictedFile file, string fullPath, string oursName, string theirsName)
        {
            var places = new List<ConflictPlace>();
            var parts = new List<ConflictPart>();
            foreach (var segment in file.Segments)
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
                    IsBinary = file.IsBinary,
                    IsSubmodule = file.IsSubmodule,
                    ExistsInWorkingTree = file.ExistsInWorkingTree,
                    HasMalformedMarkers = file.HasMalformedMarkers,
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
    }
}
