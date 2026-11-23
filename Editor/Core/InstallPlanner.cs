#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityGit
{
    /// <summary>One manifest entry to change: a package moved to a release tag.</summary>
    public sealed class InstallStep
    {
        public InstallStep(string name, string installUrl, string tag, SemVer? from, SemVer to)
        {
            Name = name;
            InstallUrl = installUrl;
            Tag = tag;
            From = from;
            To = to;
        }

        public string Name { get; }
        public string InstallUrl { get; }
        public string Tag { get; }
        public SemVer? From { get; }
        public SemVer To { get; }

        /// <summary>What Package Manager installs: the manifest URL with the tag as its revision.</summary>
        public string Identifier => InstallUrl + "#" + Tag;

        /// <summary>"UnityGit 1.2.0 → 1.4.0".</summary>
        public string Describe() => $"{PackageNames.Display(Name)} {(From.HasValue ? From.Value + " → " : string.Empty)}{To}";
    }

    /// <summary>What an install changes, applied in one Package Manager resolve, or why it can't run.</summary>
    public sealed class InstallPlan
    {
        InstallPlan(IReadOnlyList<InstallStep> steps, string? refused)
        {
            Steps = steps;
            Refused = refused;
        }

        public static InstallPlan Refuse(string reason) => new InstallPlan(Array.Empty<InstallStep>(), reason);

        public static InstallPlan Of(IReadOnlyList<InstallStep> steps) => new InstallPlan(steps, null);

        public IReadOnlyList<InstallStep> Steps { get; }
        public string? Refused { get; }

        /// <summary>True when it installs a UnityGit release from before the Updates tab, which can't come back by itself.</summary>
        public bool RemovesUpdater => Steps.Any(s => s.Name == PackageNames.UnityGit && s.To < InstallPlanner.FirstWithUpdates);

        /// <summary>"UnityGit 1.2.0 → 1.4.0, FlexGit 2.2.0 → 2.3.0".</summary>
        public string Summary => string.Join(", ", Steps.Select(s => s.Describe()));
    }

    /// <summary>
    /// Which manifest entries an install changes. UnityGit needs the FlexGit its <c>package.json</c> names, and a manifest
    /// git URL overrides that dependency, so a UnityGit change brings the FlexGit it needs in the same step and a FlexGit
    /// change that UnityGit can't compile against is refused; otherwise UnityGit, and the updater in it, would break.
    /// FlexGit releases count as compatible with a requirement when they have its major version and aren't older.
    /// </summary>
    public static class InstallPlanner
    {
        /// <summary>The first UnityGit release with the Updates tab.</summary>
        public static readonly SemVer FirstWithUpdates = new SemVer(1, 4, 0);

        public static bool Compatible(SemVer flexGit, SemVer required) => flexGit.Major == required.Major && flexGit >= required;

        /// <summary>
        /// Installs UnityGit <paramref name="target"/>, whose <c>package.json</c> needs FlexGit <paramref name="requiredFlexGit"/>.
        /// When the installed FlexGit doesn't meet that, FlexGit moves to the required release (the one it was built with), or
        /// the newest compatible one when that tag doesn't exist.
        /// </summary>
        public static InstallPlan UnityGit(InstalledPackage unityGit, InstalledPackage flexGit, ReleaseTag target, SemVer? requiredFlexGit, IReadOnlyList<ReleaseTag> flexGitTags)
            => PlanUnityGit(unityGit, flexGit, target, requiredFlexGit, flexGitTags, preferNewestFlexGit: false);

        /// <summary>Installs FlexGit <paramref name="target"/>, unless the installed UnityGit can't compile against it.</summary>
        public static InstallPlan FlexGit(InstalledPackage unityGit, InstalledPackage flexGit, ReleaseTag target)
        {
            if (!flexGit.CanInstall) return InstallPlan.Refuse("FlexGit isn't installed from a git URL");
            if (flexGit.Version == target.Version) return InstallPlan.Refuse($"FlexGit {target.Version} is already installed");
            var conflict = FlexGitConflict(unityGit, target.Version);
            if (conflict != null) return InstallPlan.Refuse(conflict);
            return InstallPlan.Of(new[] { Step(flexGit, target) });
        }

        /// <summary>
        /// Update all: the newest UnityGit (when newer) with the newest FlexGit compatible with it, or else the newest
        /// FlexGit compatible with the installed UnityGit. <paramref name="requiredByNewest"/> is what the newest UnityGit needs.
        /// </summary>
        public static InstallPlan All(InstalledPackage unityGit, InstalledPackage flexGit, IReadOnlyList<ReleaseTag> unityGitTags, SemVer? requiredByNewest, IReadOnlyList<ReleaseTag> flexGitTags)
        {
            var unityTarget = UnityGitUpdate(unityGit, unityGitTags);
            if (unityTarget != null)
                return PlanUnityGit(unityGit, flexGit, unityTarget, requiredByNewest, flexGitTags, preferNewestFlexGit: true);
            var flexTarget = FlexGitUpdate(unityGit, flexGit, flexGitTags);
            if (flexTarget != null) return FlexGit(unityGit, flexGit, flexTarget);
            return InstallPlan.Refuse("Everything is up to date");
        }

        /// <summary>The newest UnityGit release when it is newer than the installed one and UnityGit can change it.</summary>
        public static ReleaseTag? UnityGitUpdate(InstalledPackage unityGit, IReadOnlyList<ReleaseTag> tags)
        {
            if (!unityGit.CanInstall || tags.Count == 0) return null;
            var newest = Newest(tags);
            return unityGit.Version is SemVer installed && newest.Version <= installed ? null : newest;
        }

        /// <summary>The newest FlexGit release the installed UnityGit can use, when it is newer than the installed FlexGit.</summary>
        public static ReleaseTag? FlexGitUpdate(InstalledPackage unityGit, InstalledPackage flexGit, IReadOnlyList<ReleaseTag> tags)
        {
            if (!flexGit.CanInstall) return null;
            var newest = tags.Where(t => FlexGitConflict(unityGit, t.Version) == null).OrderByDescending(t => t.Version).FirstOrDefault();
            if (newest == null) return null;
            return flexGit.Version is SemVer installed && newest.Version <= installed ? null : newest;
        }

        /// <summary>Why the installed UnityGit can't use FlexGit <paramref name="flexGit"/>; null when it can, or when its need is unknown.</summary>
        public static string? FlexGitConflict(InstalledPackage unityGit, SemVer flexGit)
        {
            if (!(unityGit.RequiredFlexGit is SemVer required)) return null;
            var name = unityGit.Version is SemVer v ? $"UnityGit {v}" : "UnityGit";
            if (flexGit < required) return $"{name} needs FlexGit {required} or newer";
            if (flexGit.Major != required.Major) return $"{name} is built for FlexGit {required.Major}.x";
            return null;
        }

        static InstallPlan PlanUnityGit(InstalledPackage unityGit, InstalledPackage flexGit, ReleaseTag target, SemVer? requiredFlexGit,
            IReadOnlyList<ReleaseTag> flexGitTags, bool preferNewestFlexGit)
        {
            if (!unityGit.CanInstall) return InstallPlan.Refuse("UnityGit isn't installed from a git URL");
            if (unityGit.Version == target.Version) return InstallPlan.Refuse($"UnityGit {target.Version} is already installed");
            var steps = new List<InstallStep> { Step(unityGit, target) };
            if (!(requiredFlexGit is SemVer required)) return InstallPlan.Of(steps);

            var compatible = flexGitTags.Where(t => Compatible(t.Version, required)).OrderByDescending(t => t.Version).ToList();
            var installedFits = flexGit.Version is SemVer installed && Compatible(installed, required);
            if (preferNewestFlexGit)
            {
                // Update all: the newest compatible FlexGit, when it's newer than a compatible installed one.
                var newest = compatible.FirstOrDefault();
                if (installedFits && (newest == null || newest.Version <= flexGit.Version!.Value)) return InstallPlan.Of(steps);
                if (newest == null) return NoFlexGitRelease(target, required);
                if (!flexGit.CanInstall) return FlexGitNotFromGit(target, required);
                steps.Add(Step(flexGit, newest));
                return InstallPlan.Of(steps);
            }

            if (installedFits) return InstallPlan.Of(steps);
            if (!flexGit.CanInstall) return FlexGitNotFromGit(target, required);
            var pick = compatible.FirstOrDefault(t => t.Version == required) ?? compatible.FirstOrDefault();
            if (pick == null) return NoFlexGitRelease(target, required);
            steps.Add(Step(flexGit, pick));
            return InstallPlan.Of(steps);
        }

        static InstallPlan NoFlexGitRelease(ReleaseTag target, SemVer required)
            => InstallPlan.Refuse($"UnityGit {target.Version} needs FlexGit {required}, and no such FlexGit release was found");

        static InstallPlan FlexGitNotFromGit(ReleaseTag target, SemVer required)
            => InstallPlan.Refuse($"UnityGit {target.Version} needs FlexGit {required} — update FlexGit first (it isn't installed from a git URL)");

        static ReleaseTag Newest(IReadOnlyList<ReleaseTag> tags) => tags.OrderByDescending(t => t.Version).First();

        static InstallStep Step(InstalledPackage package, ReleaseTag target)
            => new InstallStep(package.Name, package.InstallUrl!, target.Tag, package.Version, target.Version);
    }
}
