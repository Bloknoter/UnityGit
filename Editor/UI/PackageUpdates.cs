#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityGit.UI
{
    /// <summary>What Package Manager reports about an install: the packages are going in (a script reload follows), it failed, or the scripts don't compile afterwards.</summary>
    internal enum InstallReport { Registered, Failed, CompileFailed }

    /// <summary>The Package Manager side of the Updates tab, so tests can stand in for it.</summary>
    internal interface IPackageHost
    {
        /// <summary>The running editor's version, such as 6000.4.1f1.</summary>
        string EditorVersion { get; }

        InstalledPackage Find(string name);

        /// <summary>Changes the manifest entries to <paramref name="identifiers"/> (url#tag) in one resolve and reports how it went.</summary>
        void Install(IReadOnlyList<string> identifiers, Action<InstallReport, string?> report);
    }

    /// <summary>One entry of a package's version list.</summary>
    internal sealed class VersionItem
    {
        public VersionItem(ReleaseTag tag, string label, bool enabled, bool installed)
        {
            Tag = tag;
            Label = label;
            Enabled = enabled;
            Installed = installed;
        }

        public ReleaseTag Tag { get; }
        public string Label { get; }
        public bool Enabled { get; }
        public bool Installed { get; }
    }

    /// <summary>
    /// The Updates tab's state and actions: the releases of UnityGit and FlexGit, which of them are updates, and installing
    /// one (or both) after a confirm. Outcomes go to the session's outcome bar. <see cref="PackageUpdatesProvider"/> keeps the
    /// project's instance and its state across script reloads.
    /// </summary>
    internal sealed class PackageUpdates
    {
        public const string Location = "Tools → Git → UnityGit → Updates";

        readonly IPackageHost _host;
        readonly ReleaseSource _source;
        readonly IEditorHooks _hooks;
        readonly Func<GitSession> _session;
        IReadOnlyList<ReleaseTag> _unityGitTags = Array.Empty<ReleaseTag>();
        IReadOnlyList<ReleaseTag> _flexGitTags = Array.Empty<ReleaseTag>();
        string? _newestTag; // the UnityGit tag NewestUnityGit was read from

        public PackageUpdates(IPackageHost host, ReleaseSource source, IEditorHooks hooks, Func<GitSession> session)
        {
            _host = host;
            _source = source;
            _hooks = hooks;
            _session = session;
            UnityGit = host.Find(PackageNames.UnityGit);
            FlexGit = host.Find(PackageNames.FlexGit);
        }

        public event Action? Changed;

        public InstalledPackage UnityGit { get; private set; }
        public InstalledPackage FlexGit { get; private set; }
        public IReadOnlyList<ReleaseTag> UnityGitTags => _unityGitTags;
        public IReadOnlyList<ReleaseTag> FlexGitTags => _flexGitTags;
        /// <summary>The <c>package.json</c> of the newest UnityGit release, read when it is newer than the installed one.</summary>
        public PackageManifest? NewestUnityGit { get; private set; }
        public string? Error { get; private set; }
        public string? ErrorDetails { get; private set; }
        public DateTime? CheckedAt { get; private set; }
        public bool IsChecking { get; private set; }
        public bool IsInstalling { get; private set; }

        /// <summary>The newest UnityGit release, when it is newer than the installed one and this editor can run it.</summary>
        public ReleaseTag? UnityGitUpdate => UnityGitBlocked == null ? InstallPlanner.UnityGitUpdate(UnityGit, _unityGitTags) : null;

        /// <summary>The newest FlexGit release the installed UnityGit can use, when it is newer than the installed FlexGit.</summary>
        public ReleaseTag? FlexGitUpdate => InstallPlanner.FlexGitUpdate(UnityGit, FlexGit, _flexGitTags);

        /// <summary>Why the newest UnityGit release isn't offered although it is newer: it needs a newer Unity.</summary>
        public string? UnityGitBlocked
        {
            get
            {
                var newest = InstallPlanner.UnityGitUpdate(UnityGit, _unityGitTags);
                if (newest == null || NewestUnityGit == null || _newestTag != newest.Tag) return null;
                return UnityProblem(PackageNames.UnityGit, newest.Version, NewestUnityGit);
            }
        }

        public int UpdateCount => (UnityGitUpdate != null ? 1 : 0) + (FlexGitUpdate != null ? 1 : 0);

        /// <summary>The Console line for found updates: "UnityGit 1.4.0 is available — …"; null when there are none.</summary>
        public string? Announcement
        {
            get
            {
                var found = new List<string>();
                if (UnityGitUpdate is ReleaseTag u) found.Add($"UnityGit {u.Version}");
                if (FlexGitUpdate is ReleaseTag f) found.Add($"FlexGit {f.Version}");
                if (found.Count == 0) return null;
                return $"{string.Join(" and ", found)} {(found.Count == 1 ? "is" : "are")} available — {Location}";
            }
        }

        /// <summary>Reads both packages' releases, and the newest UnityGit's <c>package.json</c> when it is newer. Never throws for git failures.</summary>
        public async Task CheckAsync(CancellationToken cancellationToken = default)
        {
            if (IsChecking) return;
            IsChecking = true;
            RaiseChanged();
            try
            {
                UnityGit = _host.Find(PackageNames.UnityGit);
                FlexGit = _host.Find(PackageNames.FlexGit);
                var errors = new List<(string message, string? details)>();
                _unityGitTags = await ListAsync(UnityGit, errors, cancellationToken);
                _flexGitTags = await ListAsync(FlexGit, errors, cancellationToken);

                NewestUnityGit = null;
                _newestTag = null;
                if (InstallPlanner.UnityGitUpdate(UnityGit, _unityGitTags) is ReleaseTag newest)
                {
                    var read = await _source.ReadPackageAsync(UnityGit.GitUrl!, newest.Tag, cancellationToken);
                    if (read.Package != null)
                    {
                        NewestUnityGit = read.Package;
                        _newestTag = newest.Tag;
                    }
                    else errors.Add(($"UnityGit: {read.Error}", read.Details));
                }

                Error = errors.Count == 0 ? null : string.Join("\n", errors.Select(e => e.message));
                ErrorDetails = errors.Count == 0 ? null : string.Join("\n\n", errors.Select(e => e.details).Where(d => d != null));
                if (ErrorDetails == string.Empty) ErrorDetails = null;
                CheckedAt = DateTime.Now;
            }
            finally
            {
                IsChecking = false;
                RaiseChanged();
            }
        }

        async Task<IReadOnlyList<ReleaseTag>> ListAsync(InstalledPackage package, List<(string, string?)> errors, CancellationToken cancellationToken)
        {
            if (!package.CanInstall) return Array.Empty<ReleaseTag>();
            var list = await _source.ListAsync(package.GitUrl!, package.TagPrefix, cancellationToken);
            if (list.Error != null) errors.Add(($"{package.DisplayName}: {list.Error}", list.Details));
            return list.Tags;
        }

        public Task UpdateAllAsync() => ApplyAsync(PlanAllAsync);

        public Task UpdateUnityGitAsync()
            => UnityGitUpdate is ReleaseTag tag ? InstallAsync(PackageNames.UnityGit, tag) : Refuse(UnityGitBlocked ?? "UnityGit is up to date");

        public Task UpdateFlexGitAsync()
            => FlexGitUpdate is ReleaseTag tag ? InstallAsync(PackageNames.FlexGit, tag) : Refuse("FlexGit is up to date");

        /// <summary>Installs <paramref name="name"/> at <paramref name="tag"/>, newer or older, with whatever the other package needs.</summary>
        public Task InstallAsync(string name, ReleaseTag tag)
            => ApplyAsync(() => name == PackageNames.UnityGit
                ? PlanUnityGitAsync(tag)
                : Task.FromResult(InstallPlanner.FlexGit(UnityGit, FlexGit, tag)));

        /// <summary>Each release of <paramref name="name"/>, newest first: the installed one and those the other package can't use are disabled.</summary>
        public IReadOnlyList<VersionItem> VersionItems(string name)
        {
            var package = name == PackageNames.UnityGit ? UnityGit : FlexGit;
            var tags = name == PackageNames.UnityGit ? _unityGitTags : _flexGitTags;
            var items = new List<VersionItem>();
            foreach (var tag in tags)
            {
                var installed = package.Version == tag.Version;
                var conflict = name == PackageNames.FlexGit ? InstallPlanner.FlexGitConflict(UnityGit, tag.Version) : null;
                var label = tag.Version + (installed ? " (installed)" : conflict != null ? " — " + conflict : string.Empty);
                items.Add(new VersionItem(tag, label, !installed && conflict == null && package.CanInstall, installed));
            }
            return items;
        }

        async Task<InstallPlan> PlanUnityGitAsync(ReleaseTag tag)
        {
            if (!UnityGit.CanInstall) return InstallPlanner.UnityGit(UnityGit, FlexGit, tag, null, _flexGitTags); // refuses
            var read = await _source.ReadPackageAsync(UnityGit.GitUrl!, tag.Tag);
            if (read.Package == null) return InstallPlan.Refuse($"Couldn't read UnityGit {tag.Version}: {read.Error}");
            return InstallPlanner.UnityGit(UnityGit, FlexGit, tag, RequiredFlexGit(read.Package), _flexGitTags);
        }

        async Task<InstallPlan> PlanAllAsync()
        {
            var unityGitTags = UnityGitBlocked == null ? _unityGitTags : Array.Empty<ReleaseTag>(); // this editor can't run the newest
            SemVer? required = null;
            if (InstallPlanner.UnityGitUpdate(UnityGit, unityGitTags) is ReleaseTag newest)
            {
                var read = await _source.ReadPackageAsync(UnityGit.GitUrl!, newest.Tag);
                if (read.Package == null) return InstallPlan.Refuse($"Couldn't read UnityGit {newest.Version}: {read.Error}");
                required = RequiredFlexGit(read.Package);
            }
            return InstallPlanner.All(UnityGit, FlexGit, unityGitTags, required, _flexGitTags);
        }

        // Every release the plan installs must accept this editor's version, or it wouldn't compile, and with UnityGit gone
        // there'd be no updater to come back with.
        async Task<InstallPlan> CheckUnityAsync(InstallPlan plan)
        {
            if (plan.Refused != null) return plan;
            foreach (var step in plan.Steps)
            {
                var package = step.Name == PackageNames.UnityGit ? UnityGit : FlexGit;
                var read = await _source.ReadPackageAsync(package.GitUrl!, step.Tag);
                if (read.Package == null) return InstallPlan.Refuse($"Couldn't read {package.DisplayName} {step.To}: {read.Error}");
                if (UnityProblem(step.Name, step.To, read.Package) is string problem) return InstallPlan.Refuse(problem);
            }
            return plan;
        }

        async Task ApplyAsync(Func<Task<InstallPlan>> makePlan)
        {
            var refusal = Refusal();
            if (refusal != null)
            {
                SetOutcome(OutcomeKind.Refused, refusal);
                return;
            }

            IsInstalling = true;
            RaiseChanged();
            var handedOver = false;
            try
            {
                var plan = await CheckUnityAsync(await makePlan());
                if (plan.Refused != null)
                {
                    SetOutcome(OutcomeKind.Refused, plan.Refused);
                    return;
                }
                if (!_hooks.Confirm("Update packages", ConfirmText(plan), "Install"))
                {
                    SetOutcome(OutcomeKind.Cancelled, "Install cancelled");
                    return;
                }
                // A git action or Play Mode could have started while the dialog was open.
                var availability = _session().GetAvailability(GitAction.UpdatePackages);
                if (!availability.Enabled)
                {
                    SetOutcome(OutcomeKind.Refused, availability.Reason ?? "Not available now");
                    return;
                }

                handedOver = true;
                _host.Install(plan.Steps.Select(s => s.Identifier).ToList(), (report, message) => OnReport(plan, report, message));
            }
            catch (Exception ex)
            {
                handedOver = false;
                SetOutcome(OutcomeKind.Failed, "Couldn't install the packages", ex.Message);
            }
            finally
            {
                if (!handedOver)
                {
                    IsInstalling = false;
                    RaiseChanged();
                }
            }
        }

        void OnReport(InstallPlan plan, InstallReport report, string? message)
        {
            IsInstalling = false;
            switch (report)
            {
                case InstallReport.Registered:
                    var verb = plan.Steps.All(s => !s.From.HasValue || s.To > s.From.Value) ? "Updated" : "Installed";
                    SetOutcome(OutcomeKind.Succeeded, $"{verb} {plan.Summary} — Unity is recompiling");
                    break;
                case InstallReport.Failed:
                    SetOutcome(OutcomeKind.Failed, $"Package Manager couldn't install {plan.Summary}", message);
                    break;
                case InstallReport.CompileFailed:
                    SetOutcome(OutcomeKind.Failed, $"Scripts don't compile after installing {plan.Summary} — see the Console", message);
                    break;
            }
            RaiseChanged();
        }

        static string ConfirmText(InstallPlan plan)
        {
            var text = new StringBuilder();
            text.Append(plan.Summary).Append("\n\nUnity recompiles scripts once Package Manager has the new packages.");
            var unityGit = plan.Steps.FirstOrDefault(s => s.Name == PackageNames.UnityGit);
            if (plan.RemovesUpdater && unityGit != null)
                text.Append($"\n\nUnityGit {unityGit.To} has no Updates tab. To come back, use Package Manager or edit Packages/manifest.json.");
            return text.ToString();
        }

        string? Refusal()
        {
            if (IsInstalling) return "An install is already running";
            var availability = _session().GetAvailability(GitAction.UpdatePackages);
            return availability.Enabled ? null : availability.Reason;
        }

        Task Refuse(string reason)
        {
            SetOutcome(OutcomeKind.Refused, reason);
            return Task.CompletedTask;
        }

        void SetOutcome(OutcomeKind kind, string message, string? details = null)
            => _session().LastOutcome = new ActionOutcome(GitAction.UpdatePackages, kind, message, details);

        string? UnityProblem(string name, SemVer version, PackageManifest package)
            => PackageJson.UnitySatisfies(package.Unity, _host.EditorVersion) ? null : $"{PackageNames.Display(name)} {version} needs Unity {package.Unity} or newer";

        static SemVer? RequiredFlexGit(PackageManifest package)
            => package.Dependencies.TryGetValue(PackageNames.FlexGit, out var text) && SemVer.TryParse(text, out var v) ? v : (SemVer?)null;

        void RaiseChanged() => Changed?.Invoke();

        // ---- state kept across script reloads (SessionState), so the badge stays without checking again ----

        public string SaveState()
        {
            var lines = new List<string>();
            if (CheckedAt is DateTime at) lines.Add("checked=" + at.Ticks.ToString(CultureInfo.InvariantCulture));
            if (Error != null) lines.Add("error=" + Escape(Error));
            if (ErrorDetails != null) lines.Add("details=" + Escape(ErrorDetails));
            foreach (var t in _unityGitTags) lines.Add($"u={Escape(t.Tag)}\t{t.Sha}\t{t.Version}");
            foreach (var t in _flexGitTags) lines.Add($"f={Escape(t.Tag)}\t{t.Sha}\t{t.Version}");
            if (NewestUnityGit != null && _newestTag != null)
            {
                lines.Add("newest-tag=" + Escape(_newestTag));
                if (NewestUnityGit.Version != null) lines.Add("newest-version=" + Escape(NewestUnityGit.Version));
                if (NewestUnityGit.Unity != null) lines.Add("newest-unity=" + Escape(NewestUnityGit.Unity));
                if (NewestUnityGit.Dependencies.TryGetValue(PackageNames.FlexGit, out var flex)) lines.Add("newest-flexgit=" + Escape(flex));
            }
            return string.Join("\n", lines);
        }

        public void RestoreState(string state)
        {
            var unityGitTags = new List<ReleaseTag>();
            var flexGitTags = new List<ReleaseTag>();
            string? newestTag = null, newestVersion = null, newestUnity = null, newestFlexGit = null;
            foreach (var line in state.Split('\n'))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq);
                var value = line.Substring(eq + 1);
                switch (key)
                {
                    case "checked":
                        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) CheckedAt = new DateTime(ticks);
                        break;
                    case "error": Error = Unescape(value); break;
                    case "details": ErrorDetails = Unescape(value); break;
                    case "u": if (ParseTag(value) is ReleaseTag u) unityGitTags.Add(u); break;
                    case "f": if (ParseTag(value) is ReleaseTag f) flexGitTags.Add(f); break;
                    case "newest-tag": newestTag = Unescape(value); break;
                    case "newest-version": newestVersion = Unescape(value); break;
                    case "newest-unity": newestUnity = Unescape(value); break;
                    case "newest-flexgit": newestFlexGit = Unescape(value); break;
                }
            }
            _unityGitTags = unityGitTags;
            _flexGitTags = flexGitTags;
            _newestTag = newestTag;
            NewestUnityGit = newestTag == null ? null : new PackageManifest
            {
                Version = newestVersion,
                Unity = newestUnity,
                Dependencies = newestFlexGit == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { [PackageNames.FlexGit] = newestFlexGit },
            };
            RaiseChanged();
        }

        static ReleaseTag? ParseTag(string value)
        {
            var parts = value.Split('\t');
            if (parts.Length != 3 || !SemVer.TryParse(parts[2], out var version)) return null;
            return new ReleaseTag(version, Unescape(parts[0]), parts[1]);
        }

        static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r", string.Empty).Replace("\n", "\\n");

        static string Unescape(string text)
        {
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i++;
                    sb.Append(text[i] == 'n' ? '\n' : text[i]);
                }
                else sb.Append(text[i]);
            }
            return sb.ToString();
        }
    }
}
