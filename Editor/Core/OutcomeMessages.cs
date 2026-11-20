#nullable enable
using FlexGit;

namespace UnityGit
{
    internal static class OutcomeMessages
    {
        public static string ForErrorKind(GitErrorKind kind, string fallback)
        {
            switch (kind)
            {
                case GitErrorKind.AuthenticationFailed: return "Sign-in failed — check your Git credentials";
                case GitErrorKind.IndexLocked: return "Another git process is running";
                case GitErrorKind.NetworkError: return "Network error — check your connection and the remote URL";
                case GitErrorKind.RemoteNotFound: return "Remote not found — check the remote URL";
                case GitErrorKind.IdentityNotConfigured: return "Git doesn't know who you are — set user.name and user.email in your git config";
                case GitErrorKind.GitNotFound: return "Git isn't installed or wasn't found — install Git 2.32 or newer";
                case GitErrorKind.GitTooOld: return "Git is too old — install Git 2.32 or newer";
                case GitErrorKind.LfsNotInstalled: return "git-lfs isn't installed — install it from https://git-lfs.com";
                case GitErrorKind.NoUpstream: return "This branch has no upstream — push it first";
                default: return fallback;
            }
        }

        public static ActionOutcome FromError(GitAction action, GitError error)
            => new ActionOutcome(action, OutcomeKind.Failed, ForErrorKind(error.Kind, error.Message), Details(error));

        /// <summary>An outcome's Details: git's error output, or null when there is none.</summary>
        public static string? Details(GitError? error)
            => error == null || string.IsNullOrEmpty(error.StandardError) ? null : error.StandardError;

        /// <summary>Git's own explanation of a failure: its full error output, which often ends with how to fix it.</summary>
        public static string GitsWords(GitError error)
            => string.IsNullOrWhiteSpace(error.StandardError) ? error.Message : error.StandardError.Trim();

        public static string Places(int n) => n == 1 ? "1 place" : $"{n} places";

        public static string Files(int n) => n == 1 ? "1 file" : $"{n} files";
    }
}
