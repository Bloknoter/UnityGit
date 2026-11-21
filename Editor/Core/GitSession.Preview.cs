#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit
{
    public sealed partial class GitSession
    {
        /// <summary>
        /// Reads what changed in a row of the Changes list, and in its .meta when the row holds one, for the preview
        /// (spec §5.4). Not an action: no busy state, no hooks. Null when there is no repository, the session is disposed,
        /// or git failed (then <see cref="Warning"/> says why).
        /// </summary>
        public async Task<ChangePreview?> LoadChangePreviewAsync(ChangeItem row)
        {
            var repo = _repo;
            if (repo == null || Snapshot.Availability != RepoAvailability.Ready) return null;
            try
            {
                var ct = _cts.Token;
                var file = await LoadDiffAsync(repo, row.Path, row.OriginalPath, row.Kind, ct);
                var meta = row.HasMeta
                    ? await LoadDiffAsync(repo, row.Path + MetaPairing.MetaSuffix,
                        row.OriginalPath == null ? null : row.OriginalPath + MetaPairing.MetaSuffix, ChangeKind.Modified, ct)
                    : null;
                return new ChangePreview { File = file, Meta = meta };
            }
            catch (OperationCanceledException)
            {
                return null; // the session was disposed
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                InvokeWarning("Loading the preview failed: " + ex.Message);
                return null;
            }
        }

        // A big new file is read in part here; anything else goes to FlexGit. Only the row's own kind is known, so a .meta
        // (always small) always goes to FlexGit.
        static async Task<FileDiffView> LoadDiffAsync(GitRepository repo, string path, string? originalPath, ChangeKind kind, CancellationToken ct)
        {
            var fullPath = repo.GetFullPath(path);
            if (kind == ChangeKind.Added || kind == ChangeKind.Untracked)
            {
                var info = new FileInfo(fullPath);
                if (info.Exists && info.Length > DiffLoader.LargeNewFileBytes)
                    return await Task.Run(() => DiffLoader.ReadNewFileHead(path, fullPath, kind == ChangeKind.Untracked), ct);
            }
            var diff = await repo.Diff.GetWorkingTreeDiffAsync(path, DiffLoader.ContextLines, originalPath, ct);
            return DiffLoader.ToView(diff, fullPath);
        }
    }
}
