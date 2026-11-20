#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityGit
{
    public enum ConflictType { BothModified, BothAdded, BothDeleted, AddedByUs, AddedByThem, DeletedByUs, DeletedByThem }

    public enum ConflictPick { Ours, Theirs, OursThenTheirs, TheirsThenOurs }

    /// <summary>What a <see cref="ConflictType"/> means, for the session's actions and the window alike.</summary>
    public static class ConflictTypes
    {
        /// <summary>Both sides changed (or added) the file, so git merged it and a text file can have places.</summary>
        public static bool ChangedOnBothSides(ConflictType type) => type == ConflictType.BothModified || type == ConflictType.BothAdded;
    }

    public sealed class ConflictFileInfo
    {
        public string Path { get; internal set; } = "";
        public ConflictType Type { get; internal set; }
        public bool IsBinary { get; internal set; }
        public bool IsSubmodule { get; internal set; }
        public bool ExistsInWorkingTree { get; internal set; }
        public bool HasMalformedMarkers { get; internal set; }
        public int PlaceCount { get; internal set; }
        public bool HasPlaces => PlaceCount > 0;
        internal ConflictFileInfo() { }
    }

    public sealed class ConflictOverviewInfo
    {
        public RepoOperation Operation { get; internal set; }
        public IReadOnlyList<ConflictFileInfo> Files { get; internal set; } = Array.Empty<ConflictFileInfo>();
        internal ConflictOverviewInfo() { }
    }

    /// <summary>One conflict place of a text file. Side texts keep their line breaks.</summary>
    public sealed class ConflictPlace
    {
        public int Index { get; internal set; }
        public int StartLine { get; internal set; }
        public int EndLine { get; internal set; }
        public string Ours { get; internal set; } = "";
        public string Theirs { get; internal set; } = "";
        public string? Base { get; internal set; }
        public string OursName { get; internal set; } = "";
        public string TheirsName { get; internal set; } = "";
        internal ConflictPlace() { }

        public string Label(ConflictPick pick) => ConflictLabels.For(pick, OursName, TheirsName);

        public string TextFor(ConflictPick pick)
        {
            switch (pick)
            {
                case ConflictPick.Ours: return Ours;
                case ConflictPick.Theirs: return Theirs;
                case ConflictPick.OursThenTheirs: return Ours + Theirs;
                default: return Theirs + Ours;
            }
        }
    }

    /// <summary>Either unchanged text or a conflict place.</summary>
    public sealed class ConflictPart
    {
        public string? Text { get; internal set; }
        public ConflictPlace? Place { get; internal set; }
        public bool IsPlace => Place != null;
        internal ConflictPart() { }
    }

    public sealed class ConflictFileView
    {
        public ConflictFileInfo Info { get; internal set; } = new ConflictFileInfo();
        public string FullPath { get; internal set; } = "";
        public string OursName { get; internal set; } = "";
        public string TheirsName { get; internal set; } = "";
        public IReadOnlyList<ConflictPart> Parts { get; internal set; } = Array.Empty<ConflictPart>();
        public IReadOnlyList<ConflictPlace> Places { get; internal set; } = Array.Empty<ConflictPlace>();
        public bool IsSceneOrPrefab { get; internal set; }
        internal FlexGit.ConflictedFile Source { get; set; } = null!;
        internal ConflictFileView() { }

        public string Label(ConflictPick pick) => ConflictLabels.For(pick, OursName, TheirsName);

        /// <summary>The file's text with the picked places resolved and the rest still in markers. Writes nothing.</summary>
        public string PreviewText(IReadOnlyDictionary<int, ConflictPick> picks)
            => Source.GetResolvedText(picks.Select(p => new FlexGit.ConflictHunkResolution(p.Key, ConflictLoader.ToChoice(p.Value))));
    }

    static class ConflictLabels
    {
        internal static string For(ConflictPick pick, string ours, string theirs)
        {
            switch (pick)
            {
                case ConflictPick.Ours: return "Use " + ours;
                case ConflictPick.Theirs: return "Use " + theirs;
                case ConflictPick.OursThenTheirs: return $"Use {ours} then {theirs}";
                default: return $"Use {theirs} then {ours}";
            }
        }
    }
}
