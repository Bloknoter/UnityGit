#nullable enable
using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    /// <summary>
    /// The Updates tab: a card with Check for updates and Update all, then one card per package with the installed and
    /// latest release, Update, and a menu of every release to move to.
    /// </summary>
    internal sealed class UpdatesPanel : VisualElement, IRenderablePanel
    {
        readonly PackageUpdates _updates;
        readonly Button _check;
        readonly Button _all;
        readonly Label _status;
        readonly Label _error;
        readonly Foldout _details;
        readonly Label _detailsText;
        readonly PackageCard _unityGit;
        readonly PackageCard _flexGit;

        public UpdatesPanel(PackageUpdates updates)
        {
            _updates = updates;
            AddToClassList(PanelClasses.Content);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList(PanelClasses.Scroll);
            Add(scroll);

            var card = EditorUiElements.Card("Updates");
            card.AddToClassList("unitygit-updates-card");
            _check = new Button(() => _ = _updates.CheckAsync()) { name = UiNames.UpdatesCheck, text = "Check for updates" };
            GitIcons.Apply(_check, GitIcon.Refresh);
            _all = new Button(() => _ = _updates.UpdateAllAsync()) { name = UiNames.UpdatesAll, text = "Update all" };
            card.Header.Add(_check);
            card.Header.Add(_all);
            _status = PanelParts.Text(null, EditorUiClasses.Muted);
            _status.name = UiNames.UpdatesStatus;
            _error = PanelParts.Text(null, "unitygit-updates-error");
            _error.name = UiNames.UpdatesError;
            _error.selection.isSelectable = true;
            _details = new Foldout { text = "Details", value = false };
            _detailsText = PanelParts.Text(null, EditorUiClasses.Mono, EditorUiClasses.Small, "unitygit-outcome-details");
            _detailsText.selection.isSelectable = true; // git's words, to copy
            _details.Add(_detailsText);
            card.Add(_status);
            card.Add(_error);
            card.Add(_details);
            scroll.Add(card);

            _unityGit = new PackageCard(PackageNames.UnityGit, updates);
            _flexGit = new PackageCard(PackageNames.FlexGit, updates);
            scroll.Add(_unityGit.Card);
            scroll.Add(_flexGit.Card);
            Render();
        }

        public void Render()
        {
            var busy = _updates.IsChecking || _updates.IsInstalling;
            _check.SetEnabled(!busy);
            _check.text = _updates.IsChecking ? "Checking…" : "Check for updates";
            _all.SetEnabled(!busy && _updates.UpdateCount > 0);
            _all.EnableInClassList(EditorUiClasses.ButtonPrimary, _all.enabledSelf);

            _status.text = _updates.IsChecking ? "Checking…"
                : _updates.IsInstalling ? "Installing…"
                : _updates.CheckedAt is DateTime at ? "Checked at " + at.ToString("HH:mm", CultureInfo.InvariantCulture)
                : "Not checked yet";
            _error.text = _updates.Error ?? string.Empty;
            PanelParts.SetDisplayed(_error, _updates.Error != null);
            _detailsText.text = _updates.ErrorDetails ?? string.Empty;
            PanelParts.SetDisplayed(_details, _updates.Error != null && _updates.ErrorDetails != null);

            _unityGit.Render(busy);
            _flexGit.Render(busy);
        }

        /// <summary>One package: installed and latest release, Update, and the menu of other releases.</summary>
        sealed class PackageCard
        {
            readonly string _name;
            readonly PackageUpdates _updates;
            readonly Label _installed;
            readonly Label _source;
            readonly Label _latest;
            readonly Button _update;
            readonly Button _versions;

            public PackageCard(string name, PackageUpdates updates)
            {
                _name = name;
                _updates = updates;
                var prefix = UiNames.UpdatesCard(name);
                Card = EditorUiElements.Card(PackageNames.Display(name));
                Card.name = prefix;

                _installed = PanelParts.Text(null, EditorUiClasses.Strong, "unitygit-updates-version");
                _installed.name = prefix + UiNames.UpdatesInstalled;
                _source = PanelParts.Text(null, EditorUiClasses.Muted, PanelClasses.Ellipsis);
                _source.name = prefix + UiNames.UpdatesSource;
                Card.Add(PanelParts.Field("Installed", _installed, _source));
                _latest = PanelParts.Text(null, PanelClasses.Value);
                _latest.name = prefix + UiNames.UpdatesLatest;
                Card.Add(PanelParts.Field("Latest", _latest));

                _update = new Button(Update) { name = prefix + UiNames.UpdatesUpdate };
                _versions = new Button(ShowVersions) { name = prefix + UiNames.UpdatesVersions, text = "Other versions ▾" };
                var buttons = new VisualElement();
                buttons.AddToClassList(PanelClasses.Inline);
                buttons.AddToClassList("unitygit-updates-buttons");
                buttons.Add(_update);
                buttons.Add(_versions);
                Card.Add(buttons);
            }

            public EditorUiCard Card { get; }

            InstalledPackage Package => _name == PackageNames.UnityGit ? _updates.UnityGit : _updates.FlexGit;
            ReleaseTag? UpdateTag => _name == PackageNames.UnityGit ? _updates.UnityGitUpdate : _updates.FlexGitUpdate;

            public void Render(bool busy)
            {
                var package = Package;
                var update = UpdateTag;
                var tags = _name == PackageNames.UnityGit ? _updates.UnityGitTags : _updates.FlexGitTags;
                _installed.text = package.Version?.ToString() ?? "—";
                _source.text = SourceText(package);
                _latest.text = LatestText(package, update);

                PanelParts.SetDisplayed(_update, package.CanInstall);
                PanelParts.SetDisplayed(_versions, package.CanInstall);
                _update.text = update != null ? $"Update to {update.Version}" : "Up to date";
                _update.SetEnabled(update != null && !busy);
                _update.EnableInClassList(EditorUiClasses.ButtonPrimary, _update.enabledSelf);
                _update.tooltip = _name == PackageNames.UnityGit ? _updates.UnityGitBlocked ?? string.Empty : string.Empty;
                _versions.SetEnabled(tags.Count > 0 && !busy);
            }

            string LatestText(InstalledPackage package, ReleaseTag? update)
            {
                if (_name == PackageNames.UnityGit)
                {
                    if (update != null)
                    {
                        var needs = _updates.NewestUnityGit?.Dependencies.TryGetValue(PackageNames.FlexGit, out var flex) == true ? flex : null;
                        return needs != null ? $"{update.Version} — needs FlexGit {needs}" : update.Version.ToString();
                    }
                    if (_updates.UnityGitBlocked is string blocked) return blocked;
                    return Newest(package, _updates.UnityGitTags.FirstOrDefault());
                }
                if (update != null) return update.Version.ToString();
                // The newest release the installed UnityGit can use.
                return Newest(package, _updates.FlexGitTags.FirstOrDefault(t => InstallPlanner.FlexGitConflict(_updates.UnityGit, t.Version) == null));
            }

            static string Newest(InstalledPackage package, ReleaseTag? newest)
            {
                if (newest == null) return "—";
                return package.Version == newest.Version ? $"{newest.Version} (installed)" : newest.Version.ToString();
            }

            static string SourceText(InstalledPackage package)
            {
                switch (package.Source)
                {
                    case PackageSource.Git: return package.InstallUrl == null ? "git" : $"git · {package.Revision ?? "default branch"}";
                    case PackageSource.Embedded: return "embedded in Packages — UnityGit leaves it alone";
                    case PackageSource.Local: return "local folder — UnityGit leaves it alone";
                    case PackageSource.Registry: return "package registry — UnityGit leaves it alone";
                    case PackageSource.Missing: return "not installed";
                    default: return "UnityGit leaves it alone";
                }
            }

            void Update()
            {
                if (_name == PackageNames.UnityGit) _ = _updates.UpdateUnityGitAsync();
                else _ = _updates.UpdateFlexGitAsync();
            }

            // Every release, newest first. Picking one asks before installing, like Update.
            void ShowVersions()
            {
                var menu = new GenericMenu();
                foreach (var item in _updates.VersionItems(_name))
                {
                    var content = new GUIContent(item.Label);
                    if (!item.Enabled)
                    {
                        menu.AddDisabledItem(content, item.Installed);
                        continue;
                    }
                    var tag = item.Tag;
                    menu.AddItem(content, item.Installed, () => _ = _updates.InstallAsync(_name, tag));
                }
                menu.DropDown(_versions.worldBound);
            }
        }
    }
}
