#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;
using PmPackageInfo = UnityEditor.PackageManager.PackageInfo;
using PmPackageSource = UnityEditor.PackageManager.PackageSource;

namespace UnityGit.UI
{
    /// <summary>
    /// Keeps the project's <see cref="PackageUpdates"/>. Its state lives in <see cref="SessionState"/>, so a script reload
    /// keeps the Updates badge without checking again. The check runs once per editor session, a few seconds after the
    /// editor has loaded, and never in batch mode (test runs and builds). Tests swap in their own with <see cref="Override"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class PackageUpdatesProvider
    {
        const string StateKey = "UnityGit.Updates.State";
        const string CheckedKey = "UnityGit.Updates.Checked";
        const double StartupDelaySeconds = 5;

        static readonly CancellationTokenSource Cancel = new CancellationTokenSource();
        static PackageUpdates? _current;
        static PackageUpdates? _override;
        static double _checkAt;

        static PackageUpdatesProvider()
        {
            try
            {
                AssemblyReloadEvents.beforeAssemblyReload += Cancel.Cancel; // a git call must not outlive its domain
                if (Application.isBatchMode || SessionState.GetBool(CheckedKey, false)) return;
                _checkAt = EditorApplication.timeSinceStartup + StartupDelaySeconds;
                EditorApplication.update += CheckWhenDue;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static PackageUpdates Current => _override ?? (_current ??= Create());

        internal static void Override(PackageUpdates updates) => _override = updates;

        internal static void ClearOverride() => _override = null;

        static PackageUpdates Create()
        {
            var project = Path.GetDirectoryName(Application.dataPath)!;
            var source = new ReleaseSource(Path.Combine(project, "Library", "UnityGit", "ReleaseCache.git"));
            var updates = new PackageUpdates(new UnityPackageHost(project), source, new UnityEditorHooks(), () => SessionProvider.Current);
            var saved = SessionState.GetString(StateKey, string.Empty);
            if (saved.Length > 0) updates.RestoreState(saved);
            updates.Changed += () => SessionState.SetString(StateKey, updates.SaveState());
            return updates;
        }

        static void CheckWhenDue()
        {
            if (EditorApplication.timeSinceStartup < _checkAt) return;
            EditorApplication.update -= CheckWhenDue;
            _ = CheckAtStartupAsync();
        }

        static async Task CheckAtStartupAsync()
        {
            try
            {
                var updates = Current;
                await updates.CheckAsync(Cancel.Token);
                if (Cancel.IsCancellationRequested) return; // a reload stopped it: the next domain checks again
                SessionState.SetBool(CheckedKey, true);
                if (updates.Announcement is string found) Debug.Log("[UnityGit] " + found);
                if (updates.Error != null) Debug.LogWarning("[UnityGit] Couldn't check for updates — " + updates.Error.Replace("\n", "; "));
            }
            catch (OperationCanceledException)
            {
                // A script reload: the next domain checks again.
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    /// <summary>The real Package Manager: what's installed, and installing by git URL in one resolve.</summary>
    internal sealed class UnityPackageHost : IPackageHost
    {
        readonly string _projectPath;

        public UnityPackageHost(string projectPath) => _projectPath = projectPath;

        public string EditorVersion => Application.unityVersion;

        public InstalledPackage Find(string name)
        {
            var info = PmPackageInfo.FindForPackageName(name);
            if (info == null) return new InstalledPackage(name, PackageSource.Missing, null);
            var source = SourceOf(info.source);
            SemVer? version = SemVer.TryParse(info.version, out var v) ? v : (SemVer?)null;
            SemVer? requiredFlexGit = null;
            foreach (var dependency in info.dependencies ?? Array.Empty<DependencyInfo>())
                if (dependency.name == PackageNames.FlexGit && SemVer.TryParse(dependency.version, out var r)) requiredFlexGit = r;
            // The manifest's own entry, as written there; a git package's id is "name@" followed by the same string.
            var entry = ManifestEntry(name) ?? AfterName(info.packageId, name);
            return new InstalledPackage(name, source, version, entry, requiredFlexGit);
        }

        public void Install(IReadOnlyList<string> identifiers, Action<InstallReport, string?> report)
        {
            var registered = false;
            void OnRegistering(PackageRegistrationEventArgs _)
            {
                // Called before the new packages go in, by this (old) code: the outcome it saves survives the reload.
                Events.registeringPackages -= OnRegistering;
                if (registered) return;
                registered = true;
                report(InstallReport.Registered, null);
                WatchCompilation(report);
            }

            Events.registeringPackages += OnRegistering;
            var request = Client.AddAndRemove(identifiers.ToArray());
            void Poll()
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= Poll;
                Events.registeringPackages -= OnRegistering;
                if (request.Status == StatusCode.Failure)
                {
                    report(InstallReport.Failed, request.Error?.message);
                    return;
                }
                if (registered) return;
                registered = true;
                report(InstallReport.Registered, null);
                WatchCompilation(report);
            }
            EditorApplication.update += Poll;
        }

        // When the new scripts compile, Unity reloads and this handler is gone. When they don't, the old scripts stay loaded
        // and this reports it.
        static void WatchCompilation(Action<InstallReport, string?> report)
        {
            void OnFinished(object _)
            {
                CompilationPipeline.compilationFinished -= OnFinished;
                if (EditorUtility.scriptCompilationFailed) report(InstallReport.CompileFailed, null);
            }
            CompilationPipeline.compilationFinished += OnFinished;
        }

        string? ManifestEntry(string name)
        {
            try
            {
                var manifest = PackageJson.Read(File.ReadAllText(Path.Combine(_projectPath, "Packages", "manifest.json")));
                return manifest != null && manifest.Dependencies.TryGetValue(name, out var entry) ? entry : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        static string? AfterName(string? packageId, string name)
            => packageId != null && packageId.StartsWith(name + "@", StringComparison.Ordinal) ? packageId.Substring(name.Length + 1) : null;

        static PackageSource SourceOf(PmPackageSource source)
        {
            switch (source)
            {
                case PmPackageSource.Git: return PackageSource.Git;
                case PmPackageSource.Embedded: return PackageSource.Embedded;
                case PmPackageSource.Local:
                case PmPackageSource.LocalTarball: return PackageSource.Local;
                case PmPackageSource.Registry:
                case PmPackageSource.BuiltIn: return PackageSource.Registry;
                default: return PackageSource.Other;
            }
        }
    }
}
