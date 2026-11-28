#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UnityGit
{
    public sealed partial class GitSession
    {
        /// <summary>The repo-relative path ('/') of the Unity project folder's .gitignore; null before the repo is read.</summary>
        public string? ProjectGitignorePath
        {
            get
            {
                var root = Snapshot.RepoRoot;
                if (root == null) return null;
                var relative = Path.GetRelativePath(root, Path.Combine(ProjectPath, ".gitignore")).Replace('\\', '/');
                return Path.IsPathRooted(relative) || relative.StartsWith("../", StringComparison.Ordinal) ? null : relative;
            }
        }

        /// <summary>
        /// Every .gitignore in the repository, repo-relative with '/': the root's first, then by path. The ones git lists
        /// (tracked, or untracked and not ignored), plus the root's and the project folder's when they're on disk, since a
        /// .gitignore that ignores itself isn't listed. Not an action: no busy state, no hooks. Empty without a repository
        /// or when git fails (with a warning).
        /// </summary>
        public async Task<IReadOnlyList<string>> ListGitignoreFilesAsync()
        {
            var repo = _repo;
            if (repo == null || Snapshot.Availability != RepoAvailability.Ready) return Array.Empty<string>();
            try
            {
                var result = await repo.RunRawAsync(
                    new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.gitignore" }, null, _cts.Token);
                if (!result.Succeeded)
                {
                    InvokeWarning("Listing .gitignore files failed: " + result.StandardError.Trim());
                    return Array.Empty<string>();
                }
                // A conflicted file is listed once per stage.
                var paths = result.StandardOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.Ordinal)
                    .Where(p => Path.GetFileName(p) == ".gitignore" && File.Exists(repo.GetFullPath(p)))
                    .ToList();
                foreach (var extra in new[] { ".gitignore", ProjectGitignorePath })
                    if (extra != null && !paths.Contains(extra) && File.Exists(repo.GetFullPath(extra))) paths.Add(extra);
                return paths.OrderBy(p => p == ".gitignore" ? 0 : 1).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<string>(); // the session was disposed
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                InvokeWarning("Listing .gitignore files failed: " + ex.Message);
                return Array.Empty<string>();
            }
        }
    }
}
