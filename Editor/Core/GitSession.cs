#nullable enable
using System;
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

        internal IEditorHooks Hooks { get; }

        public Task RefreshAsync(bool includeLfsLocks = false)
        {
            lock (_gate)
            {
                return _running ??= RunRefreshAsync(includeLfsLocks);
            }
        }

        async Task RunRefreshAsync(bool includeLfsLocks)
        {
            // Yield so RefreshAsync has stored _running before this body can finish and clear it.
            await Task.Yield();
            try
            {
                _snapshot = await LoadAsync(includeLfsLocks).ConfigureAwait(false);
                Raise(() => Changed?.Invoke());
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
                Raise(() => UnexpectedError?.Invoke(ex));
            }
            finally
            {
                lock (_gate) _running = null;
            }
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

        void Raise(Action action)
        {
            if (_context == null || SynchronizationContext.Current == _context) action();
            else _context.Post(_ => action(), null);
        }

        public void Dispose() => _cts.Cancel();
    }
}
