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

        // Checks run in order and the first match wins.
        static string? Why(GitAction action, RepoSnapshot s, bool isBusy, bool isPlayMode)
        {
            if (isBusy) return "Another git action is running";
            if (s.Availability == RepoAvailability.Unknown) return "Loading…";
            if (s.Availability == RepoAvailability.GitMissing) return s.Message ?? string.Empty;

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
