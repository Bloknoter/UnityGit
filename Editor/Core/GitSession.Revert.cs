#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit
{
    // Reverting rows of the Changes list. It discards work, so it runs only after the user confirms (spec: revert design §4).
    public sealed partial class GitSession
    {
        // FlexGit's DiscardAsync passes its paths to git status as arguments, and Windows caps a command line at 32,767
        // characters: the paths go in batches under this many characters, each row's paths kept together.
        const int RevertPathBudget = 16000;

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

            // Read fresh, before the dialog: it must name what will really be reverted, not what the list showed. A merge or
            // rebase started outside since the last refresh is caught here too, as Commit does.
            var status = await repo.GetStatusAsync();
            var operation = SnapshotLoader.ToOperation(status.State);
            if (operation != RepoOperation.None)
                return await RefusedAfterRefreshAsync($"Finish or abort the {ActionRules.OperationNoun(operation)} first");

            var current = SnapshotLoader.ToChanges(status).ToDictionary(r => r.Path, StringComparer.Ordinal);
            var requested = rows.Distinct(StringComparer.Ordinal)
                .Select(p => current.TryGetValue(p, out var row) ? row : null)
                .Where(r => r != null).Select(r => r!).ToList();
            var targets = requested.Where(r => !r.IsConflicted).ToList();
            var conflicted = requested.Count - targets.Count;
            if (targets.Count == 0)
                return await RefusedAfterRefreshAsync(conflicted > 0
                    ? "Conflicted files can't be reverted — resolve them in Conflicts"
                    : "Nothing to revert");

            // A picked row's path can be another, unpicked row: a staged rename's source that now holds a new file. Discarding
            // the rename would delete that file unannounced, so the user picks it too.
            var picked = new HashSet<string>(targets.Select(t => t.Path), StringComparer.Ordinal);
            var pickedPaths = new HashSet<string>(targets.SelectMany(t => t.GitPaths), StringComparer.Ordinal);
            var unpicked = current.Values.FirstOrDefault(r => !picked.Contains(r.Path) && r.GitPaths.Any(pickedPaths.Contains));
            if (unpicked != null)
                return await RefusedAfterRefreshAsync($"Select {unpicked.Path} too — this revert would discard it as well");

            // A tracked asset's .meta that was never committed is deleted with it, and Unity gives the asset a new GUID.
            var newPaths = new HashSet<string>(
                status.Entries.Where(e => e.IsUntracked || e.IndexStatus == FileStatus.Added).Select(e => e.Path), StringComparer.Ordinal);
            var newMetas = targets.Count(t => t.HasMeta && t.Kind != ChangeKind.Added && t.Kind != ChangeKind.Untracked
                && newPaths.Contains(t.Path + MetaPairing.MetaSuffix));

            if (!Hooks.Confirm(RevertText.Title(targets), RevertText.Message(targets, conflicted, newMetas), "Revert"))
                return new ActionOutcome(revert, OutcomeKind.Cancelled, "Revert cancelled");
            if (!SaveAssetsAndConfirmScenes())
                return new ActionOutcome(revert, OutcomeKind.Cancelled, "Revert cancelled — modified scenes weren't saved");

            // Untracked rows go first: a picked rename's source may be one of them, and it must be gone before the rename is
            // put back, or git would leave the source as a staged deletion instead of HEAD's file.
            var batches = PathBatches(targets.Where(t => t.Kind == ChangeKind.Untracked))
                .Concat(PathBatches(targets.Where(t => t.Kind != ChangeKind.Untracked)))
                .ToList();
            var files = new FileChangeTracker();
            await RunGitOperationAsync(
                () => files.TrackAsync(async () =>
                {
                    foreach (var batch in batches) await repo.Index.DiscardAsync(batch, deleteUntracked: true);
                    RemoveEmptiedFolders(repo.WorkingDirectory, targets.SelectMany(t => t.GitPaths).Where(newPaths.Contains));
                    return true;
                }, _ => false),
                files);
            return new ActionOutcome(revert, OutcomeKind.Succeeded, $"Reverted {OutcomeMessages.Files(targets.Count)}");
        });

        // The list was stale (that's why Revert stops): it catches up before the refusal is shown.
        async Task<ActionOutcome> RefusedAfterRefreshAsync(string message)
        {
            await RefreshAfterActionAsync();
            return Refused(GitAction.Revert, message);
        }

        // git clean deletes the new files but not the folders they were in. A folder that is left empty and has no .meta beside
        // it is removed as well, walking up from each deleted file, or Unity would give it a new .meta and it would be listed
        // again. A folder whose .meta is still on disk (committed, or not picked) belongs to the project and stays.
        static void RemoveEmptiedFolders(string root, IEnumerable<string> deletedPaths)
        {
            foreach (var path in deletedPaths)
            {
                for (var dir = ParentOf(path); dir.Length > 0; dir = ParentOf(dir))
                {
                    var full = Path.Combine(root, dir);
                    if (!Directory.Exists(full)) continue; // removed by an earlier walk
                    try
                    {
                        if (Directory.EnumerateFileSystemEntries(full).Any() || File.Exists(full + MetaPairing.MetaSuffix)) break;
                        Directory.Delete(full);
                    }
                    catch (IOException) { break; }
                    catch (UnauthorizedAccessException) { break; }
                }
            }
        }

        // "a/b/c.txt" → "a/b"; "a/b/" (a nested repository's entry) → "a"; "c.txt" → "".
        static string ParentOf(string path)
        {
            var trimmed = path.TrimEnd('/');
            var slash = trimmed.LastIndexOf('/');
            return slash < 0 ? string.Empty : trimmed.Substring(0, slash);
        }

        static IEnumerable<List<string>> PathBatches(IEnumerable<ChangeItem> rows)
        {
            var batch = new List<string>();
            var length = 0;
            foreach (var row in rows)
            {
                var rowLength = row.GitPaths.Sum(p => p.Length + 3); // quotes and a space around each argument
                if (batch.Count > 0 && length + rowLength > RevertPathBudget)
                {
                    yield return batch.Distinct(StringComparer.Ordinal).ToList();
                    batch = new List<string>();
                    length = 0;
                }
                batch.AddRange(row.GitPaths);
                length += rowLength;
            }
            if (batch.Count > 0) yield return batch.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
