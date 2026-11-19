#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlexGit;

namespace UnityGit
{
    public sealed class GitSession : IDisposable
    {
        readonly GitOptions _options;
        readonly SynchronizationContext? _context;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly object _gate = new object();
        GitRepository? _repo;
        bool _reopenRepo; // git said the repo is gone: the next load opens it again instead of using _repo
        Task? _running;
        bool _runningLoadsLocks;
        Task? _locksAfterRunning;
        RepoSnapshot _snapshot;
        CancellationTokenSource? _actionCts;
        bool _isPlayMode;
        ActionOutcome? _lastOutcome;

        public GitSession(string projectPath, IEditorHooks hooks) : this(projectPath, hooks, new GitOptions()) { }

        internal GitSession(string projectPath, IEditorHooks hooks, GitOptions options)
        {
            ProjectPath = projectPath;
            Hooks = hooks;
            _options = options;
            _options.Logger = (level, msg) => InvokeLog(msg);
            _context = SynchronizationContext.Current;
            _snapshot = RepoSnapshot.Loading(projectPath);
        }

        public string ProjectPath { get; }
        public RepoSnapshot Snapshot => _snapshot;

        /// <summary>Verbose diagnostics: every git command FlexGit runs. Called from background threads.</summary>
        public Action<string>? Log { get; set; }

        /// <summary>
        /// Problems worth showing without verbose logging, such as a refresh that failed. Called from background threads,
        /// so the handler must be thread-safe. A handler that throws never affects a refresh or an action.
        /// </summary>
        public Action<string>? Warning { get; set; }

        public event Action? Changed;
        public event Action<Exception>? UnexpectedError;

        public bool IsBusy { get; private set; }
        public GitAction? CurrentAction { get; private set; }
        public string? ProgressText { get; private set; }
        public bool CanCancel { get; private set; }

        public bool IsPlayMode
        {
            get => _isPlayMode;
            set { _isPlayMode = value; RaiseChanged(); }
        }

        /// <summary>Settable so the window can restore it after a domain reload or clear it on dismiss.</summary>
        public ActionOutcome? LastOutcome
        {
            get => _lastOutcome;
            set { _lastOutcome = value; RaiseChanged(); }
        }

        internal IEditorHooks Hooks { get; }

        public ActionAvailability GetAvailability(GitAction action) => ActionRules.Evaluate(action, Snapshot, IsBusy, IsPlayMode);

        /// <summary>
        /// Does nothing while an action runs; every action refreshes when it finishes. A request made while a refresh runs
        /// joins it, except that a request for LFS locks while a refresh without them runs gets one lock refresh after it.
        /// </summary>
        public Task RefreshAsync(bool includeLfsLocks = false)
        {
            lock (_gate)
            {
                if (IsBusy) return Task.CompletedTask;
                if (_running == null)
                {
                    _runningLoadsLocks = includeLfsLocks;
                    return _running = RunRefreshAsync(includeLfsLocks);
                }
                if (!includeLfsLocks || _runningLoadsLocks) return _running;
                return _locksAfterRunning ??= RefreshLocksAfterAsync(_running);
            }
        }

        // _running is still set, so it hasn't finished: the continuation below always runs after ??= stored this task.
        async Task RefreshLocksAfterAsync(Task running)
        {
            await running.ConfigureAwait(false);
            lock (_gate) _locksAfterRunning = null;
            await RefreshAsync(true).ConfigureAwait(false);
        }

        public Task<ActionOutcome> InitAsync() => RunAsync(GitAction.Init, false, async (_, _) =>
        {
            var gitignore = Path.Combine(ProjectPath, ".gitignore");
            var kept = await RunGitOperationAsync(async () =>
            {
                _repo = await GitRepository.InitAsync(ProjectPath, "main", false, _options);
                var exists = File.Exists(gitignore);
                if (!exists) File.WriteAllText(gitignore, GitignoreTemplate.Text);
                return exists;
            });
            return new ActionOutcome(GitAction.Init, OutcomeKind.Succeeded,
                kept ? "Repository created — existing .gitignore kept" : "Repository created with a Unity .gitignore");
        });

        /// <summary>
        /// Commits exactly the selected rows. Rows are <see cref="ChangeItem.Path"/> values: <paramref name="shownRows"/>
        /// is the list the user saw, so files that saving revealed stop the commit instead of slipping in unseen.
        /// </summary>
        public Task<ActionOutcome> CommitAsync(string message, IReadOnlyCollection<string> selectedRows, IReadOnlyCollection<string> shownRows)
            => RunAsync(GitAction.Commit, false, async (_, _) =>
        {
            const GitAction commit = GitAction.Commit;
            if (string.IsNullOrWhiteSpace(message)) return Refused(commit, "Enter a commit message");
            if (selectedRows.Count == 0) return Refused(commit, "Tick at least one file");

            Hooks.SaveAssets();
            if (!Hooks.ConfirmSaveModifiedScenes())
                return new ActionOutcome(commit, OutcomeKind.Cancelled, "Commit cancelled — modified scenes weren't saved");

            var repo = _repo ?? throw new InvalidOperationException("Commit ran without an open repository.");
            var selected = new HashSet<string>(selectedRows, StringComparer.Ordinal);
            var (refusal, result) = await RunGitOperationAsync<(ActionOutcome? Refusal, CommitResult? Result)>(async () =>
            {
                var status = await repo.GetStatusAsync();
                var rows = SnapshotLoader.ToChanges(status);
                var current = new Dictionary<string, ChangeItem>(StringComparer.Ordinal);
                foreach (var row in rows) current[row.Path] = row;

                var shown = new HashSet<string>(shownRows, StringComparer.Ordinal);
                if (rows.Any(r => !shown.Contains(r.Path)))
                    return (Refused(commit, "Unsaved changes were saved — review the list and commit again."), null);

                if (SnapshotLoader.ToOperation(status.State) == RepoOperation.Merging && selected.Count != rows.Count)
                    return (Refused(commit, "Finish the merge by committing all files"), null);

                if (selected.Any(p => current.TryGetValue(p, out var c) && c.IsConflicted))
                    return (Refused(commit, "Resolve conflicts first"), null);
                if (selected.Any(p => !current.ContainsKey(p)))
                    return (Refused(commit, "The file list changed — review it and commit again"), null);

                // Unselected staged rows are unstaged so they stay out. The rename sources of selected rows are unstaged
                // too: a staged rename has already dropped its old path from the index, so git add would reject it.
                // Restored, it is staged again below as the deletion half of the rename.
                var renameSource = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in status.Entries)
                    if (entry.OriginalPath != null) renameSource[entry.Path] = entry.OriginalPath;
                var unstage = rows.Where(r => !selected.Contains(r.Path) && r.IsStaged).SelectMany(r => r.GitPaths)
                    .Concat(rows.Where(r => selected.Contains(r.Path)).SelectMany(r => r.GitPaths)
                        .Where(renameSource.ContainsKey).Select(p => renameSource[p]))
                    .ToList();
                if (unstage.Count > 0) await repo.Index.UnstageAsync(unstage);
                await repo.Index.StageAsync(rows.Where(r => selected.Contains(r.Path)).SelectMany(r => r.GitPaths).ToList());
                return (null, await repo.CommitAsync(message.Trim()));
            });
            if (refusal != null) return refusal;

            switch (result!.Status)
            {
                case CommitStatus.Committed:
                    return new ActionOutcome(commit, OutcomeKind.Succeeded, $"Committed {OutcomeMessages.Files(selected.Count)}");
                case CommitStatus.NothingToCommit:
                    return Refused(commit, "Nothing to commit");
                default:
                    return result.Error != null
                        ? OutcomeMessages.FromError(commit, result.Error)
                        : new ActionOutcome(commit, OutcomeKind.Failed, result.Message);
            }
        });

        public Task<ActionOutcome> PushAsync() => RunAsync(GitAction.Push, true, async (ct, progress) =>
        {
            const GitAction push = GitAction.Push;
            var repo = _repo ?? throw new InvalidOperationException("Push ran without an open repository.");
            var result = await RunGitOperationAsync(() => repo.PushAsync(null, progress, ct));

            switch (result.Status)
            {
                case PushStatus.Pushed:
                    // The upstream comes from the refresh after the push; if that refresh failed, there may be none to name.
                    var upstream = Snapshot.Upstream;
                    return new ActionOutcome(push, OutcomeKind.Succeeded, upstream != null ? $"Pushed to {upstream}" : "Pushed");
                case PushStatus.UpToDate:
                    return new ActionOutcome(push, OutcomeKind.Succeeded, "Already up to date");
                case PushStatus.Rejected:
                    return new ActionOutcome(push, OutcomeKind.Failed, "Push rejected: someone pushed first — pull, then push");
                default:
                    return result.Error != null
                        ? OutcomeMessages.FromError(push, result.Error)
                        : new ActionOutcome(push, OutcomeKind.Failed, result.Message);
            }
        });

        public Task<ActionOutcome> FetchAsync() => RunAsync(GitAction.Fetch, true, async (ct, progress) =>
        {
            const GitAction fetch = GitAction.Fetch;
            var repo = _repo ?? throw new InvalidOperationException("Fetch ran without an open repository.");
            var result = await RunGitOperationAsync(() => repo.FetchAsync(null, progress, ct));

            if (result.Status == FetchStatus.Succeeded)
                return new ActionOutcome(fetch, OutcomeKind.Succeeded, "Fetch complete");
            return result.Error != null
                ? OutcomeMessages.FromError(fetch, result.Error)
                : new ActionOutcome(fetch, OutcomeKind.Failed, result.Message);
        });

        /// <summary>Pulls the upstream branch the way the user's git config says: <c>pull.rebase</c>, then <c>pull.ff</c>, else merge.</summary>
        public Task<ActionOutcome> PullAsync() => RunAsync(GitAction.Pull, true, async (ct, progress) =>
        {
            const GitAction pull = GitAction.Pull;
            Hooks.SaveAssets();
            if (!Hooks.ConfirmSaveModifiedScenes())
                return new ActionOutcome(pull, OutcomeKind.Cancelled, "Pull cancelled — modified scenes weren't saved");

            var repo = _repo ?? throw new InvalidOperationException("Pull ran without an open repository.");
            var files = new FileChangeTracker();
            var result = await RunGitOperationAsync(async () =>
            {
                var mode = PullModeResolver.Resolve(
                    await repo.Config.GetAsync("pull.rebase", null, ct),
                    await repo.Config.GetAsync("pull.ff", null, ct));
                return await files.TrackAsync(() => repo.PullAsync(new PullOptions { Mode = mode }, progress, ct),
                    r => r.Status is PullStatus.UpToDate or PullStatus.NoUpstream or PullStatus.CannotFastForward
                        or PullStatus.LocalChangesWouldBeOverwritten);
            }, files);

            switch (result.Status)
            {
                case PullStatus.UpToDate:
                    return new ActionOutcome(pull, OutcomeKind.Succeeded, "Already up to date");
                case PullStatus.FastForwarded:
                case PullStatus.Merged:
                case PullStatus.Rebased:
                    return new ActionOutcome(pull, OutcomeKind.Succeeded, "Pulled latest changes");
                case PullStatus.Conflicts:
                    var conflicted = Snapshot.Changes.Where(c => c.IsConflicted).Select(c => c.Path).ToList();
                    return new ActionOutcome(pull, OutcomeKind.Failed,
                        $"Pull stopped with conflicts in {OutcomeMessages.Files(conflicted.Count)} — resolve them outside UnityGit",
                        conflictedPaths: conflicted);
                case PullStatus.LocalChangesWouldBeOverwritten:
                    // git's output names the blocking files (or, for a rebase pull, says the tree is dirty).
                    return new ActionOutcome(pull, OutcomeKind.Failed, "Pull blocked: your local changes would be overwritten — commit or stash them first",
                        OutcomeMessages.Details(result.Error));
                case PullStatus.NoUpstream:
                    return new ActionOutcome(pull, OutcomeKind.Failed, "This branch has no upstream — push it first");
                case PullStatus.CannotFastForward:
                    return new ActionOutcome(pull, OutcomeKind.Failed, "Can't fast-forward — your branch and the remote have diverged");
                default:
                    return result.Error != null
                        ? OutcomeMessages.FromError(pull, result.Error)
                        : new ActionOutcome(pull, OutcomeKind.Failed, result.Message);
            }
        });

        /// <summary>
        /// Switches to <paramref name="branch"/>: a local branch name, or a remote branch's short name, for which git creates
        /// a local tracking branch. Never discards local changes: git refuses instead.
        /// </summary>
        public Task<ActionOutcome> SwitchBranchAsync(string branch) => RunAsync(GitAction.Switch, true, async (ct, progress) =>
        {
            Hooks.SaveAssets();
            if (!Hooks.ConfirmSaveModifiedScenes())
                return new ActionOutcome(GitAction.Switch, OutcomeKind.Cancelled, "Switch cancelled — modified scenes weren't saved");

            var repo = _repo ?? throw new InvalidOperationException("Switch ran without an open repository.");
            var files = new FileChangeTracker();
            var result = await RunGitOperationAsync(
                () => files.TrackAsync(() => repo.Branches.SwitchAsync(branch, null, progress, ct),
                    r => r.Status is SwitchStatus.LocalChangesWouldBeOverwritten or SwitchStatus.NotFound or SwitchStatus.AlreadyExists),
                files);

            switch (result.Status)
            {
                case SwitchStatus.Switched:
                    return new ActionOutcome(GitAction.Switch, OutcomeKind.Succeeded, $"Switched to {branch}");
                case SwitchStatus.LocalChangesWouldBeOverwritten:
                    return new ActionOutcome(GitAction.Switch, OutcomeKind.Failed,
                        "Switch blocked: your local changes would be overwritten — commit or stash them first",
                        OutcomeMessages.Details(result.Error));
                case SwitchStatus.NotFound:
                    return new ActionOutcome(GitAction.Switch, OutcomeKind.Failed, $"Branch {branch} not found");
                default:
                    return result.Error != null
                        ? OutcomeMessages.FromError(GitAction.Switch, result.Error)
                        : new ActionOutcome(GitAction.Switch, OutcomeKind.Failed, result.Message);
            }
        });

        public Task<ActionOutcome> SetUpLfsAsync() => RunAsync(GitAction.SetUpLfs, false, async (_, _) =>
        {
            var repo = _repo ?? throw new InvalidOperationException("LFS setup ran without an open repository.");
            var installed = await RunGitOperationAsync(() => repo.Lfs.EnsureInstalledAsync());
            return new ActionOutcome(GitAction.SetUpLfs, OutcomeKind.Succeeded,
                installed ? "LFS set up for this repository" : "LFS was already set up");
        });

        public void Cancel()
        {
            CancellationTokenSource? run;
            lock (_gate) run = CanCancel ? _actionCts : null;
            try
            {
                run?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The action finished in the meantime.
            }
        }

        // The runner every action goes through. Bodies make their git calls inside RunGitOperationAsync. They stay on
        // the caller's context (no ConfigureAwait(false)) so the hooks run on Unity's main thread. Cancellable bodies
        // pass the token to every FlexGit call.
        Task<ActionOutcome> RunAsync(GitAction action, bool cancellable,
            Func<CancellationToken, IProgress<GitProgress>, Task<ActionOutcome>> body)
        {
            CancellationTokenSource run;
            lock (_gate)
            {
                var availability = GetAvailability(action);
                if (!availability.Enabled)
                    return Task.FromResult(Refused(action, availability.Reason ?? string.Empty));

                run = new CancellationTokenSource();
                _actionCts = run;
                IsBusy = true;
                CurrentAction = action;
                CanCancel = cancellable;
                ProgressText = null;
            }
            return RunBodyAsync(action, run, body);
        }

        async Task<ActionOutcome> RunBodyAsync(GitAction action, CancellationTokenSource run,
            Func<CancellationToken, IProgress<GitProgress>, Task<ActionOutcome>> body)
        {
            ActionOutcome? outcome = null;
            try
            {
                // Inside the try, so the finally always clears the busy state.
                RaiseChanged();
                outcome = await body(run.Token, new ProgressReporter(p => ReportProgress(run, p)));
            }
            catch (OperationCanceledException)
            {
                outcome = new ActionOutcome(action, OutcomeKind.Cancelled, $"{action} cancelled");
            }
            catch (GitCommandException ex)
            {
                outcome = OutcomeMessages.FromError(action, ex.Error);
            }
            catch (Exception ex)
            {
                outcome = new ActionOutcome(action, OutcomeKind.Failed, $"Unexpected error: {ex.Message}", ex.ToString());
                Raise(() => ReportUnexpected(ex));
            }
            finally
            {
                lock (_gate)
                {
                    IsBusy = false;
                    CurrentAction = null;
                    CanCancel = false;
                    ProgressText = null;
                    _actionCts = null;
                }
                run.Dispose();
                _lastOutcome = outcome;
                RaiseChanged();
            }
            return outcome;
        }

        // Runs an action's git work between Hooks.BeginGitOperation() and Hooks.EndGitOperation(), then refreshes the
        // snapshot (also when the work throws). Pull and Switch pass the tracker their git call ran through: when it says
        // files may have changed, Unity reimports them (Hooks.RefreshAssets()) after End, once the lock is released, and
        // before the snapshot refresh. Bodies build their outcome from the returned result, so it can read the refreshed
        // snapshot.
        async Task<T> RunGitOperationAsync<T>(Func<Task<T>> work, FileChangeTracker? files = null)
        {
            Hooks.BeginGitOperation();
            try
            {
                return await work();
            }
            finally
            {
                Hooks.EndGitOperation();
                if (files != null && files.MayHaveChanged) Hooks.RefreshAssets();
                await RefreshAfterActionAsync();
            }
        }

        void ReportProgress(CancellationTokenSource run, GitProgress progress)
        {
            var text = $"{progress.Phase} {progress.Percent}%";
            Raise(() =>
            {
                lock (_gate)
                {
                    if (_actionCts != run) return; // Late report from an action that has finished.
                    ProgressText = text;
                }
                InvokeChanged();
            });
        }

        async Task RunRefreshAsync(bool includeLfsLocks)
        {
            // Yield so RefreshAsync has stored _running before this body can finish and clear it.
            await Task.Yield();
            bool changed;
            try
            {
                changed = await TryLoadAsync(includeLfsLocks).ConfigureAwait(false);
            }
            finally
            {
                // Cleared before Changed is raised, so a Changed handler can start a new refresh.
                lock (_gate) _running = null;
            }
            if (changed) RaiseChanged();
        }

        // The refresh path for actions: it runs while IsBusy, when the public RefreshAsync does nothing.
        // The runner raises Changed when the action ends.
        async Task RefreshAfterActionAsync()
        {
            Task? pending;
            lock (_gate) pending = _running;
            // A refresh started before the action must not overwrite the newer snapshot.
            if (pending != null) await pending.ConfigureAwait(false);
            await TryLoadAsync(false).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads a new snapshot into <see cref="Snapshot"/>. Returns whether <see cref="Snapshot"/> changed. A failed load
        /// keeps the last snapshot (stale but useful) and warns; with nothing loaded yet, the failure becomes the snapshot,
        /// so the window and the toolbar don't stay on "Loading…".
        /// </summary>
        async Task<bool> TryLoadAsync(bool includeLfsLocks)
        {
            try
            {
                _snapshot = await LoadAsync(includeLfsLocks).ConfigureAwait(false);
                return true;
            }
            catch (GitCommandException ex)
            {
                // The repo is gone (.git deleted or moved): the next refresh looks for it again. _repo is kept until then,
                // so an action clicked meanwhile (the stale snapshot still allows it) gets git's own error.
                if (ex.Kind == GitErrorKind.NotARepository) _reopenRepo = true;
                InvokeWarning($"Refresh failed: {ex.Message}");
                return ShowFailureIfNothingLoaded(OutcomeMessages.GitsWords(ex.Error));
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // Disposed while refreshing.
                return false;
            }
            catch (Exception ex)
            {
                Raise(() => ReportUnexpected(ex));
                return ShowFailureIfNothingLoaded(ex.Message);
            }
        }

        bool ShowFailureIfNothingLoaded(string message)
        {
            if (_snapshot.Availability == RepoAvailability.Ready) return false;
            _snapshot = RepoSnapshot.Unavailable(ProjectPath, message);
            return true;
        }

        async Task<RepoSnapshot> LoadAsync(bool includeLfsLocks)
        {
            var ct = _cts.Token;
            try
            {
                await GitLocator.LocateAsync(_options, ct).ConfigureAwait(false);
            }
            catch (GitCommandException ex) when (ex.Kind == GitErrorKind.GitNotFound || ex.Kind == GitErrorKind.GitTooOld)
            {
                return RepoSnapshot.GitMissing(ProjectPath, OutcomeMessages.ForErrorKind(ex.Kind, ex.Error.Message));
            }

            var repo = _reopenRepo ? null : _repo;
            if (repo == null)
            {
                try
                {
                    repo = await GitRepository.OpenAsync(ProjectPath, _options, ct).ConfigureAwait(false);
                }
                catch (GitCommandException ex) when (ex.Kind == GitErrorKind.NotARepository)
                {
                    return RepoSnapshot.NotARepository(ProjectPath);
                }
                catch (GitCommandException ex)
                {
                    // A repo is there but git refuses it (dubious ownership, an unsupported format, ...): show git's
                    // reason, which often ends with its own fix, rather than offer Git init on top of the repo.
                    return RepoSnapshot.Unavailable(ProjectPath, OutcomeMessages.GitsWords(ex.Error));
                }
                _repo = repo;
                _reopenRepo = false;
            }

            var snapshot = await SnapshotLoader.LoadAsync(repo, ProjectPath, includeLfsLocks, ct, InvokeWarning).ConfigureAwait(false);
            // Locks have their own refresh (spec §5.7), so a load without them keeps the list the user loaded last.
            if (!includeLfsLocks && snapshot.Lfs.UsedByRepo) snapshot.LfsLocks = _snapshot.LfsLocks;
            return snapshot;
        }

        static ActionOutcome Refused(GitAction action, string message) => new ActionOutcome(action, OutcomeKind.Refused, message);

        void RaiseChanged() => Raise(InvokeChanged);

        // Subscriber exceptions never propagate: they would leave the session busy or throw into a button handler.
        // Each subscriber is called on its own, so one failing handler doesn't stop the others (window and toolbar) updating.
        void InvokeChanged()
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception ex)
                {
                    ReportUnexpected(ex);
                }
            }
        }

        void InvokeLog(string message) => InvokeSafely(Log, message);

        void InvokeWarning(string message) => InvokeSafely(Warning, message);

        // Log and Warning run inside refreshes and actions: a handler that throws must not turn a successful action into
        // "Unexpected error", so its exception is reported instead.
        void InvokeSafely(Action<string>? handler, string message)
        {
            if (handler == null) return;
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                Raise(() => ReportUnexpected(ex));
            }
        }

        void ReportUnexpected(Exception ex)
        {
            try
            {
                UnexpectedError?.Invoke(ex);
            }
            catch (Exception)
            {
                // A failing error handler has nowhere left to report to.
            }
        }

        void Raise(Action action)
        {
            if (_context == null || SynchronizationContext.Current == _context) action();
            else _context.Post(_ => action(), null);
        }

        public void Dispose() => _cts.Cancel();

        // Pull and Switch rewrite files under the running editor. Their git call runs through TrackAsync: once the call
        // has started, the files count as possibly changed, also when it throws or is cancelled midway, unless its result
        // shows that git left them alone.
        sealed class FileChangeTracker
        {
            public bool MayHaveChanged { get; private set; }

            public async Task<T> TrackAsync<T>(Func<Task<T>> gitCall, Func<T, bool> leftFilesUntouched)
            {
                MayHaveChanged = true;
                var result = await gitCall();
                MayHaveChanged = !leftFilesUntouched(result);
                return result;
            }
        }

        sealed class ProgressReporter : IProgress<GitProgress>
        {
            readonly Action<GitProgress> _report;
            public ProgressReporter(Action<GitProgress> report) => _report = report;
            public void Report(GitProgress value) => _report(value);
        }
    }
}
