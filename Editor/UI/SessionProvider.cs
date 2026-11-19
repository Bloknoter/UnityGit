#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityGit.UI
{
    /// <summary>
    /// Gives the window and the main-toolbar buttons the project's one <see cref="GitSession"/>. The session is created
    /// on first use and refreshed on the spec's triggers (§5.4); its last outcome survives domain reloads. Tests swap in
    /// their own session with <see cref="Override"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class SessionProvider
    {
        internal const string VerboseLoggingKey = "UnityGit.VerboseLogging";

        // Asset changes arrive in bursts (an import, a save, a branch switch): one refresh this long after the last
        // request covers the whole burst, and regaining focus still feels immediate.
        const double RefreshDebounceSeconds = 0.5;

        static GitSession? _session;
        static GitSession? _override;
        static double _refreshDueAt;
        static bool _refreshScheduled;
        // Read by the session's Log callback, which git runs off the main thread, where EditorPrefs can't be read.
        static volatile bool _verboseLogging;

        // A static constructor runs once per domain load, so each editor event is subscribed exactly once. The handlers
        // act on the session only once it exists, because it is created lazily.
        static SessionProvider()
        {
            try
            {
                EditorApplication.focusChanged += OnFocusChanged;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
                CurrentChanged += MainToolbarButtons.RefreshAll; // the toolbar shows whichever session is current
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>The session to show. Creating it starts its first refresh.</summary>
        public static GitSession Current => _override ?? (_session ??= Create());

        /// <summary>Raised when tests swap the session, so long-lived UI can rebind to <see cref="Current"/>.</summary>
        internal static event Action? CurrentChanged;

        /// <summary>
        /// Refreshes <see cref="Current"/> once no new request has arrived for half a second, so a burst of asset
        /// changes costs one refresh.
        /// </summary>
        public static void RequestRefresh()
        {
            _refreshDueAt = EditorApplication.timeSinceStartup + RefreshDebounceSeconds;
            if (_refreshScheduled) return;
            _refreshScheduled = true;
            EditorApplication.update += RefreshWhenDue;
        }

        internal static void Override(GitSession session)
        {
            if (ReferenceEquals(_override, session)) return;
            _override = session;
            CurrentChanged?.Invoke();
        }

        internal static void ClearOverride()
        {
            if (_override == null) return;
            _override = null;
            CurrentChanged?.Invoke();
        }

        static GitSession Create()
        {
            var session = new GitSession(Path.GetDirectoryName(Application.dataPath)!, new UnityEditorHooks());
            UpdateVerboseLogging();
            session.Log = msg =>
            {
                if (_verboseLogging) Debug.Log("[UnityGit] " + msg);
            };
            // Set here too, not only on change: entering Play Mode reloads the domain, so a session created afterwards
            // never sees the event.
            session.IsPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;
            session.LastOutcome = OutcomeStore.Load();
            session.Changed += () =>
            {
                OutcomeStore.Save(session.LastOutcome);
                UpdateVerboseLogging(); // Changed runs on the main thread, so a toggled preference applies from here on.
            };
            session.Changed += MainToolbarButtons.RefreshAll; // its own handler, so a failure above can't stop it
            session.UnexpectedError += Debug.LogException;
            _ = session.RefreshAsync();
            return session;
        }

        static void RefreshWhenDue()
        {
            if (EditorApplication.timeSinceStartup < _refreshDueAt) return;
            EditorApplication.update -= RefreshWhenDue;
            _refreshScheduled = false;
            _ = Current.RefreshAsync();
        }

        static void UpdateVerboseLogging() => _verboseLogging = EditorPrefs.GetBool(VerboseLoggingKey);

        static void OnFocusChanged(bool focused)
        {
            if (focused && _session != null) RequestRefresh();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (_session != null) _session.IsPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;
        }

        static void OnBeforeAssemblyReload() => _session?.Dispose();
    }

    /// <summary>Keeps the last outcome in <see cref="SessionState"/>, so the outcome bar survives a domain reload.</summary>
    internal static class OutcomeStore
    {
        const string StoredKey = "UnityGit.Outcome.Stored";
        const string ActionKey = "UnityGit.Outcome.Action";
        const string KindKey = "UnityGit.Outcome.Kind";
        const string MessageKey = "UnityGit.Outcome.Message";
        const string HasDetailsKey = "UnityGit.Outcome.HasDetails";
        const string DetailsKey = "UnityGit.Outcome.Details";
        // Newline-separated: the pull that reported the conflicts often triggers the reimport and reload that follow.
        const string ConflictedPathsKey = "UnityGit.Outcome.ConflictedPaths";

        public static void Save(ActionOutcome? o)
        {
            if (o == null)
            {
                SessionState.EraseBool(StoredKey);
                SessionState.EraseInt(ActionKey);
                SessionState.EraseInt(KindKey);
                SessionState.EraseString(MessageKey);
                SessionState.EraseBool(HasDetailsKey);
                SessionState.EraseString(DetailsKey);
                SessionState.EraseString(ConflictedPathsKey);
                return;
            }

            SessionState.SetInt(ActionKey, (int)o.Action);
            SessionState.SetInt(KindKey, (int)o.Kind);
            SessionState.SetString(MessageKey, o.Message);
            SessionState.SetBool(HasDetailsKey, o.Details != null);
            SessionState.SetString(DetailsKey, o.Details ?? string.Empty);
            SessionState.SetString(ConflictedPathsKey, string.Join("\n", o.ConflictedPaths));
            SessionState.SetBool(StoredKey, true);
        }

        public static ActionOutcome? Load()
        {
            if (!SessionState.GetBool(StoredKey, false)) return null;
            var conflicted = SessionState.GetString(ConflictedPathsKey, string.Empty);
            return new ActionOutcome(
                (GitAction)SessionState.GetInt(ActionKey, 0),
                (OutcomeKind)SessionState.GetInt(KindKey, 0),
                SessionState.GetString(MessageKey, string.Empty),
                SessionState.GetBool(HasDetailsKey, false) ? SessionState.GetString(DetailsKey, string.Empty) : null,
                conflicted.Length == 0 ? null : conflicted.Split('\n'));
        }
    }
}
