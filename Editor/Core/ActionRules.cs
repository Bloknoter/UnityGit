#nullable enable

namespace UnityGit
{
    /// <summary>The single source of enable/disable rules for the window and the main-toolbar buttons.</summary>
    public static class ActionRules
    {
        const string NotInPlayMode = "Not available in Play Mode";
        const string NotOnBranch = "Not on a branch";
        const string NoRemote = "No remote configured";

        public static ActionAvailability Evaluate(GitAction action, RepoSnapshot snapshot, bool isBusy, bool isPlayMode)
        {
            var reason = Why(action, snapshot, isBusy, isPlayMode);
            return reason == null ? ActionAvailability.Ok : ActionAvailability.No(reason);
        }

        public static string OperationNoun(RepoOperation op)
        {
            switch (op)
            {
                case RepoOperation.Merging: return "merge";
                case RepoOperation.Rebasing: return "rebase";
                case RepoOperation.CherryPicking: return "cherry-pick";
                case RepoOperation.Reverting: return "revert";
                case RepoOperation.Bisecting: return "bisect";
                default: return "operation";
            }
        }

        static bool IsMergeOrRebase(RepoOperation op) => op == RepoOperation.Merging || op == RepoOperation.Rebasing;

        // Checks run in order and the first match wins.
        static string? Why(GitAction action, RepoSnapshot s, bool isBusy, bool isPlayMode)
        {
            if (isBusy) return "Another git action is running";
            if (s.Availability == RepoAvailability.Unknown) return "Loading…";
            if (s.Availability == RepoAvailability.GitMissing) return s.Message ?? string.Empty;
            if (s.Availability == RepoAvailability.Unavailable) return "Git can't open this repository";

            if (action == GitAction.Init)
            {
                if (s.Availability == RepoAvailability.Ready) return "Already a git repository";
                if (isPlayMode) return NotInPlayMode;
                return null;
            }

            if (s.Availability == RepoAvailability.NotARepository) return "This project is not in a git repository";

            switch (action)
            {
                case GitAction.Commit:
                    if (s.HasConflicts) return "Resolve conflicts first";
                    if (s.Operation == RepoOperation.Rebasing) return "Continue or abort the rebase first";
                    if (s.Operation != RepoOperation.None && s.Operation != RepoOperation.Merging)
                        return $"Finish or abort the {OperationNoun(s.Operation)} outside UnityGit";
                    if (s.Changes.Count == 0) return "Nothing to commit";
                    return null;

                case GitAction.Push:
                    if (s.IsDetached) return NotOnBranch;
                    if (s.IsUnborn) return "Nothing to push yet";
                    if (s.Remotes.Count == 0) return NoRemote;
                    if (s.Lfs.UsedByRepo && !s.Lfs.HooksInstalled) return "LFS isn't set up for this repository";
                    return null;

                case GitAction.Fetch:
                    if (s.Remotes.Count == 0) return NoRemote;
                    return null;

                case GitAction.Pull:
                    if (isPlayMode) return NotInPlayMode;
                    if (s.IsDetached) return NotOnBranch;
                    if (s.Operation != RepoOperation.None) return $"Finish or abort the {OperationNoun(s.Operation)} first";
                    if (s.Upstream == null) return "This branch has no upstream — push it first";
                    return null;

                case GitAction.Switch:
                    if (isPlayMode) return NotInPlayMode;
                    if (s.Operation != RepoOperation.None) return $"Finish or abort the {OperationNoun(s.Operation)} first";
                    return null;

                case GitAction.Resolve:
                case GitAction.RestoreConflict:
                    if (isPlayMode) return NotInPlayMode;
                    if (s.ConflictedPaths.Count == 0) return "No conflicts to resolve";
                    return null;

                case GitAction.Continue:
                    if (isPlayMode) return NotInPlayMode;
                    if (s.Operation == RepoOperation.None) return "Nothing to continue";
                    if (!IsMergeOrRebase(s.Operation)) return $"Finish the {OperationNoun(s.Operation)} outside UnityGit";
                    if (s.ConflictedPaths.Count > 0) return "Resolve all conflicts first";
                    return null;

                case GitAction.Abort:
                    if (isPlayMode) return NotInPlayMode;
                    if (s.Operation == RepoOperation.None) return "Nothing to abort";
                    if (!IsMergeOrRebase(s.Operation)) return $"Abort the {OperationNoun(s.Operation)} outside UnityGit";
                    return null;

                case GitAction.Revert:
                    if (isPlayMode) return NotInPlayMode;
                    // During a merge every row is locked in, so the merge commit can't drop an incoming change: reverting
                    // one would. Rebase, cherry-pick, revert and bisect put their own changes in the working tree too.
                    if (s.Operation != RepoOperation.None) return $"Finish or abort the {OperationNoun(s.Operation)} first";
                    if (s.Changes.Count == 0) return "Nothing to revert";
                    return null;

                case GitAction.UpdatePackages:
                    // Installing recompiles and reloads scripts, which a merge or rebase in the middle of its work shouldn't meet.
                    if (isPlayMode) return NotInPlayMode;
                    if (s.Operation != RepoOperation.None) return $"Finish or abort the {OperationNoun(s.Operation)} first";
                    return null;

                case GitAction.SetUpLfs:
                    if (!s.Lfs.UsedByRepo) return "This repository doesn't use LFS";
                    if (!s.Lfs.GitLfsAvailable) return "git-lfs isn't installed — install it from https://git-lfs.com";
                    if (s.Lfs.HooksInstalled) return "LFS is already set up";
                    return null;

                default:
                    return null;
            }
        }
    }
}
