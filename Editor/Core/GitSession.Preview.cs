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
        /// or git failed (then <see cref="Warning"/> says why). Also null when <paramref name="cancellationToken"/> is cancelled:
        /// the caller cancels a read whose selection has moved on, which stops its git processes and frees the repository.
        /// </summary>
        public async Task<ChangePreview?> LoadChangePreviewAsync(ChangeItem row, CancellationToken cancellationToken = default)
        {
            var repo = _repo;
            if (repo == null || Snapshot.Availability != RepoAvailability.Ready) return null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
            try
            {
                var ct = linked.Token;
                var file = await LoadDiffAsync(repo, row.Path, row.OriginalPath, row.Kind, ct);
                var meta = row.HasMeta
                    ? await LoadDiffAsync(repo, row.Path + MetaPairing.MetaSuffix,
                        row.OriginalPath == null ? null : row.OriginalPath + MetaPairing.MetaSuffix, ChangeKind.Modified, ct)
                    : null;
                return new ChangePreview { File = file, Meta = meta };
            }
            catch (OperationCanceledException)
            {
                return null; // the caller moved on, or the session was disposed
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                InvokeWarning("Loading the preview failed: " + ex.Message);
                return null;
            }
        }

        // A big new text file is read in part here; anything else goes to FlexGit. A big binary file goes to FlexGit too: it
        // reads only git's 8000-byte probe of it, and knows from the attributes whether it's stored in LFS. Only the row's own
        // kind is known, so a .meta always goes to FlexGit.
        static async Task<FileDiffView> LoadDiffAsync(GitRepository repo, string path, string? originalPath, ChangeKind kind, CancellationToken ct)
        {
            var fullPath = repo.GetFullPath(path);
            if (kind == ChangeKind.Added || kind == ChangeKind.Untracked)
            {
                var info = new FileInfo(fullPath);
                if (info.Exists && info.Length > DiffLoader.LargeNewFileBytes && !await Task.Run(() => DiffLoader.StartsBinary(fullPath), ct))
                    return await Task.Run(() => DiffLoader.ReadNewFileHead(path, fullPath, kind == ChangeKind.Untracked, cancellationToken: ct), ct);
            }
            var diff = await repo.Diff.GetWorkingTreeDiffAsync(path, DiffLoader.ContextLines, originalPath, ct);
            return DiffLoader.ToView(diff, fullPath);
        }
    }
}
