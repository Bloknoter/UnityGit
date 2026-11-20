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

        /// <summary>
        /// Takes one side's whole file. Only for a file without places: a binary file (an LFS pointer too), one deleted on a
        /// side, or one whose markers can't be read. A text file changed on both sides whose markers were removed by hand is
        /// refused: its whole side would drop the hand merge and the changes git merged cleanly from the other side.
        /// </summary>
        public Task<ActionOutcome> UseSideAsync(ConflictFileView file, bool ours)
            => RunAsync(GitAction.Resolve, false, async (_, _) =>
        {
            const GitAction resolve = GitAction.Resolve;
            var info = file.Info;
            var path = info.Path;
            var repo = _repo ?? throw new InvalidOperationException("Resolve ran without an open repository.");
            var entry = await FindConflictAsync(repo, path);
            if (entry == null) return NoLongerConflicted(resolve, path);
            if (info.HasPlaces) return Refused(resolve, "Pick options for the places instead");
            // The type as git reports it now; the rest as the file was shown.
            if (ConflictTypes.ChangedOnBothSides(ConflictLoader.ToType(entry.Conflict))
                && !info.IsBinary && !info.HasMalformedMarkers && info.ExistsInWorkingTree)
                return Refused(resolve, "Mark it resolved, or start over to pick again");

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

        /// <summary>Commits a finished merge, or carries on with a rebase. A rebase may stop again at its next commit.</summary>
        public Task<ActionOutcome> ContinueAsync()
            => RunAsync(GitAction.Continue, false, async (_, _) =>
        {
            const GitAction cont = GitAction.Continue;
            var repo = _repo ?? throw new InvalidOperationException("Continue ran without an open repository.");

            if (Snapshot.Operation == RepoOperation.Merging)
            {
                // A merge commit changes no files in the working tree, so there is nothing to save or reimport.
                var merge = await RunGitOperationAsync(() => repo.ContinueMergeAsync());
                switch (merge.Status)
                {
                    case MergeStatus.Merged:
                        return new ActionOutcome(cont, OutcomeKind.Succeeded, "Merge finished");
                    case MergeStatus.Conflicts:
                        return Refused(cont, "Resolve all conflicts first");
                    case MergeStatus.NoMergeInProgress:
                        return Refused(cont, "No merge in progress");
                    default:
                        return FailedFrom(cont, merge.Error, merge.Message);
                }
            }

            if (!SaveAssetsAndConfirmScenes())
                return new ActionOutcome(cont, OutcomeKind.Cancelled, "Continue cancelled — modified scenes weren't saved");

            var files = new FileChangeTracker();
            var rebase = await RunGitOperationAsync(
                () => files.TrackAsync(() => repo.Rebase.ContinueAsync(), r => r.Status == RebaseStatus.NoRebaseInProgress),
                files);

            switch (rebase.Status)
            {
                case RebaseStatus.Completed:
                    return new ActionOutcome(cont, OutcomeKind.Succeeded, "Rebase finished");
                case RebaseStatus.Conflicts when rebase.Error == null:
                    // FlexGit's own check, before it ran git: conflicts are still left in this step.
                    return Refused(cont, "Resolve all conflicts first");
                case RebaseStatus.Conflicts:
                    var conflicted = Snapshot.ConflictedPaths;
                    return new ActionOutcome(cont, OutcomeKind.Failed,
                        $"Rebase stopped again with conflicts in {OutcomeMessages.Files(conflicted.Count)} — resolve them in Conflicts",
                        conflictedPaths: conflicted);
                case RebaseStatus.LocalChangesWouldBeOverwritten:
                    return new ActionOutcome(cont, OutcomeKind.Failed,
                        "Rebase blocked: your local changes would be overwritten — commit or stash them first",
                        OutcomeMessages.Details(rebase.Error));
                case RebaseStatus.NoRebaseInProgress:
                    return Refused(cont, "No rebase in progress");
                default:
                    return FailedFrom(cont, rebase.Error, rebase.Message);
            }
        });

        /// <summary>Gives up on a merge or rebase and puts the branch back as it was. Asks first: the resolved conflicts are lost.</summary>
        public Task<ActionOutcome> AbortAsync()
            => RunAsync(GitAction.Abort, false, async (_, _) =>
        {
            const GitAction abort = GitAction.Abort;
            var repo = _repo ?? throw new InvalidOperationException("Abort ran without an open repository.");
            var merging = Snapshot.Operation == RepoOperation.Merging;
            var noun = merging ? "merge" : "rebase";

            if (!Hooks.Confirm($"Abort the {noun}?",
                    $"The branch goes back to how it was before the {noun}. Conflicts you've resolved so far will be lost.", $"Abort {noun}"))
                return new ActionOutcome(abort, OutcomeKind.Cancelled, "Abort cancelled");
            if (!SaveAssetsAndConfirmScenes())
                return new ActionOutcome(abort, OutcomeKind.Cancelled, "Abort cancelled — modified scenes weren't saved");

            var files = new FileChangeTracker();
            if (merging)
            {
                var merge = await RunGitOperationAsync(
                    () => files.TrackAsync(() => repo.AbortMergeAsync(), r => r.Status == MergeStatus.NoMergeInProgress),
                    files);
                switch (merge.Status)
                {
                    case MergeStatus.Aborted:
                        return new ActionOutcome(abort, OutcomeKind.Succeeded, "Merge aborted");
                    case MergeStatus.NoMergeInProgress:
                        return Refused(abort, "No merge in progress");
                    default:
                        return FailedFrom(abort, merge.Error, merge.Message);
                }
            }

            var rebase = await RunGitOperationAsync(
                () => files.TrackAsync(() => repo.Rebase.AbortAsync(), r => r.Status == RebaseStatus.NoRebaseInProgress),
                files);
            switch (rebase.Status)
            {
                case RebaseStatus.Aborted:
                    return new ActionOutcome(abort, OutcomeKind.Succeeded, "Rebase aborted");
                case RebaseStatus.NoRebaseInProgress:
                    return Refused(abort, "No rebase in progress");
                default:
                    return FailedFrom(abort, rebase.Error, rebase.Message);
            }
        });

        // Unity's unsaved assets and scenes are about to be overwritten by git: save them, and let the user stop first.
        bool SaveAssetsAndConfirmScenes()
        {
            Hooks.SaveAssets();
            return Hooks.ConfirmSaveModifiedScenes();
        }

        static ActionOutcome FailedFrom(GitAction action, GitError? error, string message)
            => error != null ? OutcomeMessages.FromError(action, error) : new ActionOutcome(action, OutcomeKind.Failed, message);

        // Read fresh from git, not from the snapshot: another tool may have resolved the path since the last refresh.
        static async Task<StatusEntry?> FindConflictAsync(GitRepository repo, string path)
            => (await repo.Conflicts.ListAsync()).FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.Ordinal));

        static ActionOutcome NoLongerConflicted(GitAction action, string path) => Refused(action, $"{path} is no longer conflicted");
    }
}
