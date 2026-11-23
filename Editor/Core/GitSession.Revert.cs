#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnityGit
{
    // Reverting rows of the Changes list. It discards work, so it runs only after the user confirms (spec: revert design §4).
    public sealed partial class GitSession
    {
        /// <summary>
        /// Puts these rows (<see cref="ChangeItem.Path"/> values) back as they are in HEAD, in the index and the working tree,
        /// with each row's paired .meta and rename source. New files are deleted. Conflicted rows are skipped, and rows that
        /// are no longer changed are ignored. Asks first, then saves assets and asks about modified scenes.
        /// </summary>
        public Task<ActionOutcome> RevertAsync(IReadOnlyCollection<string> rows)
            => RunAsync(GitAction.Revert, false, async (_, _) =>
        {
            const GitAction revert = GitAction.Revert;
            if (rows.Count == 0) return Refused(revert, "Select a file to revert");
            var repo = _repo ?? throw new InvalidOperationException("Revert ran without an open repository.");

            // Read fresh, before the dialog: it must name what will really be reverted, not what the list showed.
            var current = SnapshotLoader.ToChanges(await repo.GetStatusAsync())
                .ToDictionary(r => r.Path, StringComparer.Ordinal);
            var requested = rows.Distinct(StringComparer.Ordinal)
                .Select(p => current.TryGetValue(p, out var row) ? row : null)
                .Where(r => r != null).Select(r => r!).ToList();
            var targets = requested.Where(r => !r.IsConflicted).ToList();
            var conflicted = requested.Count - targets.Count;
            if (targets.Count == 0)
            {
                await RefreshAfterActionAsync(); // the list was stale: let it catch up
                return Refused(revert, conflicted > 0
                    ? "Conflicted files can't be reverted — resolve them in Conflicts"
                    : "Nothing to revert");
            }

            if (!Hooks.Confirm(RevertText.Title(targets), RevertText.Message(targets, conflicted), "Revert"))
                return new ActionOutcome(revert, OutcomeKind.Cancelled, "Revert cancelled");
            if (!SaveAssetsAndConfirmScenes())
                return new ActionOutcome(revert, OutcomeKind.Cancelled, "Revert cancelled — modified scenes weren't saved");

            var paths = targets.SelectMany(r => r.GitPaths).Distinct(StringComparer.Ordinal).ToList();
            var files = new FileChangeTracker();
            await RunGitOperationAsync(
                () => files.TrackAsync(async () =>
                {
                    await repo.Index.DiscardAsync(paths, deleteUntracked: true);
                    return true;
                }, _ => false),
                files);
            return new ActionOutcome(revert, OutcomeKind.Succeeded, $"Reverted {OutcomeMessages.Files(targets.Count)}");
        });
    }
}
