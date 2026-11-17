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
        Task? _running;
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
            _options.Logger = (level, msg) => Log?.Invoke(msg);
            _context = SynchronizationContext.Current;
            _snapshot = RepoSnapshot.Loading(projectPath);
        }

        public string ProjectPath { get; }
        public RepoSnapshot Snapshot => _snapshot;
        public Action<string>? Log { get; set; }
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

        /// <summary>Does nothing while an action runs; every action refreshes when it finishes.</summary>
        public Task RefreshAsync(bool includeLfsLocks = false)
        {
            lock (_gate)
            {
                if (IsBusy) return Task.CompletedTask;
                return _running ??= RunRefreshAsync(includeLfsLocks);
            }
        }

        public Task<ActionOutcome> InitAsync() => RunAsync(GitAction.Init, false, async (_, _) =>
        {
            var gitignore = Path.Combine(ProjectPath, ".gitignore");
            bool kept;
            Hooks.BeginGitOperation();
            try
            {
                _repo = await GitRepository.InitAsync(ProjectPath, "main", false, _options);
                kept = File.Exists(gitignore);
                if (!kept) File.WriteAllText(gitignore, GitignoreTemplate.Text);
            }
            finally
            {
                Hooks.EndGitOperation();
                await RefreshAfterActionAsync();
            }
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
            CommitResult result;
            Hooks.BeginGitOperation();
            try
            {
                var status = await repo.GetStatusAsync();
                var rows = SnapshotLoader.ToChanges(status);
                var current = new Dictionary<string, ChangeItem>(StringComparer.Ordinal);
                foreach (var row in rows) current[row.Path] = row;

                var shown = new HashSet<string>(shownRows, StringComparer.Ordinal);
                if (rows.Any(r => !shown.Contains(r.Path)))
                    return Refused(commit, "Unsaved changes were saved — review the list and commit again.");

                if (SnapshotLoader.ToOperation(status.State) == RepoOperation.Merging && selected.Count != rows.Count)
                    return Refused(commit, "Finish the merge by committing all files");

                if (selected.Any(p => current.TryGetValue(p, out var c) && c.IsConflicted))
                    return Refused(commit, "Resolve conflicts first");
                if (selected.Any(p => !current.ContainsKey(p)))
                    return Refused(commit, "The file list changed — review it and commit again");

                // Every staged row is unstaged: unselected ones so they stay out, selected ones so they are staged again
                // from scratch (git add rejects the old path of a rename staged outside the tool, since the index has
                // already dropped it).
                var unstage = rows.Where(r => r.IsStaged).SelectMany(r => r.GitPaths).ToList();
                if (unstage.Count > 0) await repo.Index.UnstageAsync(unstage);
                await repo.Index.StageAsync(rows.Where(r => selected.Contains(r.Path)).SelectMany(r => r.GitPaths).ToList());
                result = await repo.CommitAsync(message.Trim());
            }
            finally
            {
                Hooks.EndGitOperation();
                await RefreshAfterActionAsync();
            }

            switch (result.Status)
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

        // The runner every action goes through. Bodies call Hooks.BeginGitOperation() before their first git call,
        // Hooks.EndGitOperation() in a finally, then RefreshAfterActionAsync(). They stay on the caller's context
        // (no ConfigureAwait(false)) so the hooks run on Unity's main thread. Cancellable bodies pass the token
        // to every FlexGit call.
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
            bool loaded;
            try
            {
                loaded = await TryLoadAsync(includeLfsLocks).ConfigureAwait(false);
            }
            finally
            {
                // Cleared before Changed is raised, so a Changed handler can start a new refresh.
                lock (_gate) _running = null;
            }
            if (loaded) RaiseChanged();
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

        /// <summary>Loads a new snapshot into <see cref="Snapshot"/>. Returns false if loading failed.</summary>
        async Task<bool> TryLoadAsync(bool includeLfsLocks)
        {
            try
            {
                _snapshot = await LoadAsync(includeLfsLocks).ConfigureAwait(false);
                return true;
            }
            catch (GitCommandException ex)
            {
                Log?.Invoke(ex.Message);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // Disposed while refreshing.
            }
            catch (Exception ex)
            {
                Raise(() => ReportUnexpected(ex));
            }
            return false;
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

            if (_repo == null)
            {
                if (!await GitRepository.IsRepositoryAsync(ProjectPath, _options, ct).ConfigureAwait(false))
                    return RepoSnapshot.NotARepository(ProjectPath);
                _repo = await GitRepository.OpenAsync(ProjectPath, _options, ct).ConfigureAwait(false);
            }

            return await SnapshotLoader.LoadAsync(_repo, ProjectPath, includeLfsLocks, ct, msg => Log?.Invoke(msg)).ConfigureAwait(false);
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

        sealed class ProgressReporter : IProgress<GitProgress>
        {
            readonly Action<GitProgress> _report;
            public ProgressReporter(Action<GitProgress> report) => _report = report;
            public void Report(GitProgress value) => _report(value);
        }
    }
}
