#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit
{
    // Resolving conflicts. Each action refuses before it writes anything: no picks, a path that is no longer conflicted,
    // and (for Restore) a path that cannot be rebuilt. Then the git write runs inside RunGitOperationAsync.
    public sealed partial class GitSession
    {
        /// <summary>Applies the picked places of a text file. Places without a pick keep their conflict markers.</summary>
        public Task<ActionOutcome> ApplyPicksAsync(ConflictFileView file, IReadOnlyDictionary<int, ConflictPick> picks)
            => RunAsync(GitAction.Resolve, false, async (_, _) =>
        {
            const GitAction resolve = GitAction.Resolve;
            var path = file.Info.Path;
            if (picks.Count == 0) return Refused(resolve, "Pick an option for at least one place");
            var repo = _repo ?? throw new InvalidOperationException("Resolve ran without an open repository.");
            if (await FindConflictAsync(repo, path) == null) return NoLongerConflicted(resolve, path);

            var files = new FileChangeTracker();
            var result = await RunGitOperationAsync(
                () => files.TrackAsync(
                    () => repo.Conflicts.ResolveAsync(file.Source, picks.Select(p => new ConflictHunkResolution(p.Key, ConflictLoader.ToChoice(p.Value)))),
                    r => r.Status == ConflictResolutionStatus.FileChanged),
                files);

            switch (result.Status)
            {
                case ConflictResolutionStatus.Resolved:
                    return new ActionOutcome(resolve, OutcomeKind.Succeeded, $"Resolved {path}");
                case ConflictResolutionStatus.PartiallyResolved:
                    return new ActionOutcome(resolve, OutcomeKind.Succeeded,
                        $"Applied {OutcomeMessages.Places(picks.Count)} in {path} — {result.RemainingHunks} left");
                case ConflictResolutionStatus.FileChanged:
                    return Refused(resolve, $"{path} changed since it was shown — review it and apply again");
                default:
                    return new ActionOutcome(resolve, OutcomeKind.Failed, result.Message, OutcomeMessages.Details(result.Error));
            }
        });

        /// <summary>Takes one side's whole file. Only for a file without places: a binary file, or one deleted on a side.</summary>
        public Task<ActionOutcome> UseSideAsync(ConflictFileView file, bool ours)
            => RunAsync(GitAction.Resolve, false, async (_, _) =>
        {
            const GitAction resolve = GitAction.Resolve;
            var path = file.Info.Path;
            var repo = _repo ?? throw new InvalidOperationException("Resolve ran without an open repository.");
            if (await FindConflictAsync(repo, path) == null) return NoLongerConflicted(resolve, path);
            if (file.Info.HasPlaces) return Refused(resolve, "Pick options for the places instead");

            var files = new FileChangeTracker();
            await RunGitOperationAsync(
                () => files.TrackAsync(async () =>
                {
                    if (ours) await repo.Conflicts.TakeOursAsync(new[] { path });
                    else await repo.Conflicts.TakeTheirsAsync(new[] { path });
                    return true;
                }, _ => false),
                files);
            return new ActionOutcome(resolve, OutcomeKind.Succeeded, $"Used {(ours ? file.OursName : file.TheirsName)} for {path}");
        });

        /// <summary>Stages a file the user edited by hand, unless it still has conflict markers.</summary>
        public Task<ActionOutcome> MarkResolvedAsync(string path)
            => RunAsync(GitAction.Resolve, false, async (_, _) =>
        {
            const GitAction resolve = GitAction.Resolve;
            var repo = _repo ?? throw new InvalidOperationException("Resolve ran without an open repository.");
            if (await FindConflictAsync(repo, path) == null) return NoLongerConflicted(resolve, path);

            // Staging does not touch the file, so Unity has nothing to reimport.
            var result = await RunGitOperationAsync(() => repo.Conflicts.MarkResolvedAsync(new[] { path }));

            switch (result.Status)
            {
                case ConflictResolutionStatus.Resolved:
                    return new ActionOutcome(resolve, OutcomeKind.Succeeded, $"Marked {path} as resolved");
                case ConflictResolutionStatus.ConflictMarkersFound:
                    return Refused(resolve, $"{path} still has conflict markers");
                default:
                    return new ActionOutcome(resolve, OutcomeKind.Failed, result.Message, OutcomeMessages.Details(result.Error));
            }
        });

        /// <summary>Puts the conflict markers back in a file changed on both sides, discarding the choices and edits made in it.</summary>
        public Task<ActionOutcome> RestoreConflictAsync(string path)
            => RunAsync(GitAction.RestoreConflict, false, async (_, _) =>
        {
            const GitAction restore = GitAction.RestoreConflict;
            var repo = _repo ?? throw new InvalidOperationException("Restore ran without an open repository.");
            var entry = await FindConflictAsync(repo, path);
            if (entry == null) return NoLongerConflicted(restore, path);
            if (entry.Conflict != ConflictKind.BothModified && entry.Conflict != ConflictKind.BothAdded)
                return Refused(restore, "Only files changed on both sides can start over");
            if (!Hooks.Confirm("Start over?", $"Put back every conflict in {path}? Your choices and edits in this file will be lost.", "Start over"))
                return new ActionOutcome(restore, OutcomeKind.Cancelled, "Start over cancelled");

            var files = new FileChangeTracker();
            await RunGitOperationAsync(
                () => files.TrackAsync(async () =>
                {
                    await repo.Conflicts.RestoreConflictAsync(new[] { path });
                    return true;
                }, _ => false),
                files);
            return new ActionOutcome(restore, OutcomeKind.Succeeded, $"Put back the conflicts in {path}");
        });

        // Read fresh from git, not from the snapshot: another tool may have resolved the path since the last refresh.
        static async Task<StatusEntry?> FindConflictAsync(GitRepository repo, string path)
            => (await repo.Conflicts.ListAsync()).FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.Ordinal));

        static ActionOutcome NoLongerConflicted(GitAction action, string path) => Refused(action, $"{path} is no longer conflicted");
    }
}
