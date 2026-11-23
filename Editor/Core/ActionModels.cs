#nullable enable
using System;
using System.Collections.Generic;

namespace UnityGit
{
    public enum GitAction { Init, Commit, Push, Fetch, Pull, Switch, SetUpLfs, Resolve, RestoreConflict, Continue, Abort, Revert, UpdatePackages }

    public enum OutcomeKind { Succeeded, Failed, Cancelled, Refused }

    public sealed class ActionOutcome
    {
        public ActionOutcome(GitAction action, OutcomeKind kind, string message, string? details = null, IReadOnlyList<string>? conflictedPaths = null)
        {
            Action = action;
            Kind = kind;
            Message = message;
            Details = details;
            ConflictedPaths = conflictedPaths ?? Array.Empty<string>();
        }

        public GitAction Action { get; }
        public OutcomeKind Kind { get; }
        public bool Succeeded => Kind == OutcomeKind.Succeeded;
        public string Message { get; }
        public string? Details { get; }
        public IReadOnlyList<string> ConflictedPaths { get; }
    }

    public sealed class ActionAvailability
    {
        public static readonly ActionAvailability Ok = new ActionAvailability(true, null);

        ActionAvailability(bool enabled, string? reason)
        {
            Enabled = enabled;
            Reason = reason;
        }

        public static ActionAvailability No(string reason) => new ActionAvailability(false, reason);

        public bool Enabled { get; }
        public string? Reason { get; }
    }
}
