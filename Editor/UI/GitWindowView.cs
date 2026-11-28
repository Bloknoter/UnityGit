#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    public enum WindowTab { Overview, Changes, Conflicts, Branches, History, Authors, Gitignore, More, Updates }

    /// <summary>
    /// The window's content (spec §5.7), built in code so it can be created and tested without an EditorWindow.
    /// <see cref="Render"/> updates the existing elements in place, so keyboard focus, an open Details foldout and each
    /// panel's own state survive the frequent refreshes. The body is rebuilt only when the repo's availability changes.
    /// It wears the vendored EditorUI kit (<see cref="EditorUiTheme"/>) plus GitWindow.uss for layout, so it looks the
    /// same in a window, in tests and in both editor skins.
    /// </summary>
    public sealed class GitWindowView : VisualElement
    {
        const string GitDownloadUrl = "https://git-scm.com/downloads";
        const string LayoutSheetPath = "Packages/com.bloknoter.unitygit/Editor/UI/GitWindow.uss";
        static readonly WindowTab[] Tabs = (WindowTab[])Enum.GetValues(typeof(WindowTab));

        readonly GitSession _session;
        readonly PackageUpdates _updates;
        // The selected tab is kept by name in SessionState, so the view a domain reload builds (a script change, entering
        // Play Mode) opens on it. The key holds the project path, so sessions on other folders (the tests' sandboxes) keep
        // their own.
        readonly string _tabKey;
        readonly Dictionary<WindowTab, Func<GitSession, VisualElement>> _factories = new Dictionary<WindowTab, Func<GitSession, VisualElement>>();
        readonly Dictionary<WindowTab, VisualElement> _panels = new Dictionary<WindowTab, VisualElement>();
        readonly List<WindowTab> _visibleTabs = new List<WindowTab>(); // the nav's items

        readonly ToolbarMenu _branchMenu;
        readonly Button _commitButton;
        readonly Button _fetchButton;
        readonly Button _pullButton;
        readonly Button _pushButton;
        readonly Button _refreshButton;
        readonly Button _cancelButton;
        readonly Label _progress;

        readonly VisualElement _outcomeBar;
        readonly Label _outcomeMessage;
        readonly Foldout _outcomeConflicts;
        readonly Button _outcomeResolve;
        readonly Foldout _outcomeDetails;
        readonly Label _outcomeDetailsText;
        ActionOutcome? _shownOutcome;

        readonly VisualElement _body;
        RepoAvailability? _bodyAvailability;
        Action _renderBody = () => { };

        public GitWindowView(GitSession session) : this(session, null) { }

        internal GitWindowView(GitSession session, PackageUpdates? updates)
        {
            _session = session;
            _updates = updates ?? PackageUpdatesProvider.Current;
            _tabKey = SelectedTabKey(session.ProjectPath);
            var saved = SessionState.GetString(_tabKey, string.Empty);
            foreach (var tab in Tabs)
                if (tab.ToString() == saved) SelectedTab = tab; // a hidden Conflicts tab gives way to Changes on render
            AddToClassList("unitygit-window");
            EditorUiTheme.Apply(this);
            var layout = AssetDatabase.LoadAssetAtPath<StyleSheet>(LayoutSheetPath); // after the theme, so it wins ties
            if (layout != null) styleSheets.Add(layout);

            // The kit's toolbar row without its panel (see GitWindow.uss), lined up with the message and body.
            var toolbar = EditorUiElements.Toolbar();
            toolbar.name = UiNames.Toolbar;
            _branchMenu = new ToolbarMenu { name = UiNames.BranchMenu };
            GitIcons.Apply(_branchMenu, GitIcon.Branch);
            _commitButton = ToolbarAction(UiNames.CommitButton, "Commit", GitIcon.Commit, () => SelectTab(WindowTab.Changes));
            _fetchButton = ToolbarAction(UiNames.FetchButton, "Fetch", GitIcon.Fetch, () => _ = _session.FetchAsync());
            _pullButton = ToolbarAction(UiNames.PullButton, "Pull", GitIcon.Pull, () => _ = _session.PullAsync());
            _pushButton = ToolbarAction(UiNames.PushButton, "Push", GitIcon.Push, () => _ = _session.PushAsync());
            _progress = new Label { name = UiNames.Progress };
            _progress.AddToClassList(EditorUiClasses.Muted);
            _progress.AddToClassList(EditorUiClasses.Small);
            // Local, like every refresh (spec §5.4): only "Refresh locks" asks the LFS server for the lock list.
            _refreshButton = ToolbarAction(UiNames.RefreshButton, "Refresh", GitIcon.Refresh, () => _ = _session.RefreshAsync());
            _cancelButton = ToolbarAction(UiNames.CancelButton, "Cancel", GitIcon.Cancel, _session.Cancel);
            toolbar.Add(_branchMenu);
            toolbar.Add(_commitButton);
            toolbar.Add(_fetchButton);
            toolbar.Add(_pullButton);
            toolbar.Add(_pushButton);
            toolbar.Add(EditorUiElements.ToolbarSpacer());
            toolbar.Add(_progress);
            toolbar.Add(_refreshButton);
            toolbar.Add(_cancelButton);
            Add(toolbar);

            // A kit message: the tone follows the outcome; the conflicts and Details foldouts sit inside it.
            _outcomeBar = new VisualElement();
            _outcomeBar.AddToClassList(EditorUiClasses.Message);
            _outcomeBar.AddToClassList("unitygit-outcome-bar");
            var outcomeContent = new VisualElement();
            outcomeContent.AddToClassList("unitygit-outcome-content");
            _outcomeMessage = new Label { name = UiNames.Outcome };
            _outcomeMessage.AddToClassList(EditorUiClasses.MessageText);
            _outcomeConflicts = new Foldout { name = UiNames.OutcomeConflicts };
            _outcomeResolve = Primary(new Button(() => SelectTab(WindowTab.Conflicts)) { name = UiNames.OutcomeResolve, text = "Resolve conflicts" });
            _outcomeResolve.AddToClassList("unitygit-outcome-resolve");
            _outcomeDetails = new Foldout { text = "Details", value = false };
            _outcomeDetailsText = new Label();
            _outcomeDetailsText.AddToClassList(EditorUiClasses.Mono);
            _outcomeDetailsText.AddToClassList(EditorUiClasses.Small);
            _outcomeDetailsText.AddToClassList("unitygit-outcome-details");
            _outcomeDetailsText.selection.isSelectable = true; // so git's message can be copied
            _outcomeDetails.Add(_outcomeDetailsText);
            outcomeContent.Add(_outcomeMessage);
            outcomeContent.Add(_outcomeResolve);
            outcomeContent.Add(_outcomeConflicts);
            outcomeContent.Add(_outcomeDetails);
            _outcomeBar.Add(outcomeContent);
            var dismiss = new Button(() => _session.LastOutcome = null) { name = UiNames.OutcomeDismiss, text = "×", tooltip = "Dismiss" };
            dismiss.AddToClassList(EditorUiClasses.MessageClose);
            _outcomeBar.Add(dismiss);
            Add(_outcomeBar);

            _body = new VisualElement();
            _body.AddToClassList("unitygit-body");
            Add(_body);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                _session.Changed -= Render;
                _session.Changed += Render;
                _updates.Changed -= Render;
                _updates.Changed += Render;
                Render(); // catch up on changes made while detached
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _session.Changed -= Render;
                _updates.Changed -= Render;
            });
            _factories[WindowTab.Overview] = session => new OverviewPanel(session);
            _factories[WindowTab.Changes] = session => new ChangesPanel(session);
            _factories[WindowTab.Conflicts] = session => new ConflictsPanel(session);
            _factories[WindowTab.Branches] = session => new BranchesPanel(session);
            _factories[WindowTab.History] = session => new HistoryPanel(session);
            _factories[WindowTab.Authors] = session => new AuthorsPanel(session);
            _factories[WindowTab.Gitignore] = session => new GitignorePanel(session);
            _factories[WindowTab.More] = session => new MorePanel(session);
            _factories[WindowTab.Updates] = _ => new UpdatesPanel(_updates);
            Render();
        }

        public WindowTab SelectedTab { get; private set; }

        /// <summary>The SessionState key that keeps the selected tab of the project at <paramref name="projectPath"/>.</summary>
        internal static string SelectedTabKey(string projectPath) => "UnityGit.SelectedTab." + projectPath;

        /// <summary>The tabs in the nav: all of them, with Conflicts only while it has something to show.</summary>
        internal IReadOnlyList<WindowTab> VisibleTabs => _visibleTabs;

        public void SelectTab(WindowTab tab)
        {
            SetSelectedTab(tab);
            Render();
        }

        void SetSelectedTab(WindowTab tab)
        {
            SelectedTab = tab;
            SessionState.SetString(_tabKey, tab.ToString());
        }

        /// <summary>Updates every element from the session's current state.</summary>
        internal void Render()
        {
            var s = _session.Snapshot;
            UpdateVisibleTabs(s);
            RenderToolbar(s);
            RenderOutcome();
            RenderBody(s);
        }

        // Spec §5.1: Conflicts shows while a file is conflicted or a merge or rebase is in progress. When it goes away while
        // selected, Changes is selected instead: only once the repo has been read, because a session still loading (the one
        // a domain reload starts) knows of no conflicts yet.
        void UpdateVisibleTabs(RepoSnapshot s)
        {
            var conflicts = s.ConflictedPaths.Count > 0 || s.Operation == RepoOperation.Merging || s.Operation == RepoOperation.Rebasing;
            if (_visibleTabs.Count == 0 || _visibleTabs.Contains(WindowTab.Conflicts) != conflicts)
            {
                _visibleTabs.Clear();
                foreach (var tab in Tabs)
                    if (tab != WindowTab.Conflicts || conflicts) _visibleTabs.Add(tab);
            }
            if (!conflicts && SelectedTab == WindowTab.Conflicts && s.Availability == RepoAvailability.Ready) SetSelectedTab(WindowTab.Changes);
        }

        /// <summary>
        /// Sets how the panel for <paramref name="tab"/> is made. A panel is created when first shown and then kept, so its
        /// state survives renders. A tab without a factory shows an empty label.
        /// </summary>
        internal void RegisterPanel(WindowTab tab, Func<GitSession, VisualElement> factory)
        {
            _factories[tab] = factory;
            if (_panels.TryGetValue(tab, out var old))
            {
                old.RemoveFromHierarchy();
                _panels.Remove(tab);
            }
            Render();
        }

        void RenderToolbar(RepoSnapshot s)
        {
            _branchMenu.text = ActionLabels.Branch(s);
            ApplyAvailability(_branchMenu, GitAction.Switch);
            _branchMenu.menu.ClearItems();
            foreach (var branch in ActionLabels.BranchMenuItems(s))
                _branchMenu.menu.AppendAction(branch, item => _ = _session.SwitchBranchAsync(branch), item => BranchItemStatus(branch));

            ApplyAvailability(_commitButton, GitAction.Commit);
            ApplyAvailability(_fetchButton, GitAction.Fetch);
            _pullButton.text = ActionLabels.Pull(s);
            ApplyAvailability(_pullButton, GitAction.Pull);
            _pushButton.text = ActionLabels.Push(s);
            ApplyAvailability(_pushButton, GitAction.Push);
            // Emphasis only: the next sync step, when it can run, is the primary button (pull first when behind, else push).
            _pullButton.EnableInClassList(EditorUiClasses.ButtonPrimary, s.Behind > 0 && _pullButton.enabledSelf);
            _pushButton.EnableInClassList(EditorUiClasses.ButtonPrimary, s.Behind == 0 && s.Ahead > 0 && _pushButton.enabledSelf);

            _refreshButton.SetEnabled(!_session.IsBusy);
            SetDisplayed(_cancelButton, _session.CanCancel);
            SetDisplayed(_progress, _session.IsBusy);
            _progress.text = _session.ProgressText ?? "Working…";
        }

        DropdownMenuAction.Status BranchItemStatus(string branch)
        {
            if (!_session.GetAvailability(GitAction.Switch).Enabled) return DropdownMenuAction.Status.Disabled;
            var s = _session.Snapshot;
            return !s.IsDetached && s.Branch == branch
                ? DropdownMenuAction.Status.Checked | DropdownMenuAction.Status.Disabled // already on it
                : DropdownMenuAction.Status.Normal;
        }

        void RenderOutcome()
        {
            var outcome = _session.LastOutcome;
            SetDisplayed(_outcomeBar, outcome != null);
            // Checked on every render: a refresh can find the conflicts gone (resolved elsewhere) and the tab with them.
            SetDisplayed(_outcomeResolve, outcome != null && outcome.ConflictedPaths.Count > 0 && _visibleTabs.Contains(WindowTab.Conflicts));
            if (outcome == null || ReferenceEquals(outcome, _shownOutcome)) return; // outcomes are immutable
            _shownOutcome = outcome;

            EditorUiElements.SetMessage(_outcomeBar, outcome.Message, OutcomeTone(outcome.Kind));

            // Spec §5.5: on conflicts the outcome lists the conflicted files.
            var conflicts = outcome.ConflictedPaths;
            SetDisplayed(_outcomeConflicts, conflicts.Count > 0);
            _outcomeConflicts.text = $"Conflicted files ({conflicts.Count})";
            _outcomeConflicts.Clear();
            foreach (var path in conflicts)
            {
                var label = new Label(path);
                label.AddToClassList(EditorUiClasses.Mono);
                label.AddToClassList(EditorUiClasses.Small);
                _outcomeConflicts.Add(label);
            }
            _outcomeConflicts.value = true;

            SetDisplayed(_outcomeDetails, outcome.Details != null);
            _outcomeDetailsText.text = outcome.Details ?? string.Empty;
            _outcomeDetails.value = false; // each new outcome starts collapsed
        }

        void RenderBody(RepoSnapshot s)
        {
            if (_bodyAvailability != s.Availability)
            {
                _bodyAvailability = s.Availability;
                _body.Clear();
                _renderBody = BuildBody(s.Availability);
            }
            _renderBody();
        }

        // Builds the body for an availability and returns how to update it on later renders.
        Action BuildBody(RepoAvailability availability)
        {
            switch (availability)
            {
                case RepoAvailability.Ready:
                    return BuildReadyBody();

                case RepoAvailability.GitMissing:
                    var message = StateMessage(new Label { name = UiNames.GitMissing });
                    _body.Add(StateCard(message, Primary(new Button(() => Application.OpenURL(GitDownloadUrl)) { text = "Download Git" })));
                    return () => message.text = _session.Snapshot.Message ?? string.Empty;

                case RepoAvailability.Unavailable:
                    // git found a repo but refuses it. Its message, shown as is, usually ends with the fix (for example a
                    // safe.directory command), so it can be selected and copied. No Init: the repo exists.
                    var gitMessage = new Label { name = UiNames.UnavailableMessage };
                    gitMessage.AddToClassList(EditorUiClasses.Mono);
                    gitMessage.AddToClassList(EditorUiClasses.Small);
                    gitMessage.AddToClassList("unitygit-state-details");
                    gitMessage.selection.isSelectable = true;
                    _body.Add(StateCard(StateMessage(new Label("Git can't open this repository") { name = UiNames.Unavailable }), gitMessage));
                    return () => gitMessage.text = _session.Snapshot.Message ?? string.Empty;

                case RepoAvailability.NotARepository:
                    var init = Primary(new Button(() => _ = _session.InitAsync()) { name = UiNames.InitButton, text = "Git init" });
                    GitIcons.Apply(init, GitIcon.Init);
                    _body.Add(StateCard(StateMessage(new Label("This project is not in a git repository") { name = UiNames.NotRepo }), init));
                    return () => ApplyAvailability(init, GitAction.Init);

                default:
                    var loading = StateMessage(new Label("Loading…"));
                    loading.AddToClassList(EditorUiClasses.Muted);
                    _body.Add(StateCard(loading));
                    return () => { };
            }
        }

        Action BuildReadyBody()
        {
            // The nav is a kit card listing the visible tabs; Changes and Conflicts carry count badges that hide at 0.
            var nav = new ListView
            {
                name = UiNames.Nav,
                itemsSource = _visibleTabs,
                selectionType = SelectionType.Single,
                fixedItemHeight = 28,
                makeItem = () => new NavRow(),
                bindItem = (item, i) => ((NavRow)item).Bind(TabLabel(_visibleTabs[i]), TabBadge(_visibleTabs[i]), TabBadgeTone(_visibleTabs[i])),
            };
            nav.AddToClassList(EditorUiClasses.Card);
            nav.selectedIndicesChanged += _ =>
            {
                if (nav.selectedIndex >= 0 && nav.selectedIndex < _visibleTabs.Count) SelectTab(_visibleTabs[nav.selectedIndex]);
            };
            var host = new VisualElement { name = UiNames.PanelHost };
            var split = new VisualElement();
            split.AddToClassList("unitygit-split");
            split.Add(nav);
            split.Add(host);
            _body.Add(split);

            var shownNav = (-1, -1, -1, -1);
            return () =>
            {
                var s = _session.Snapshot;
                var navState = (_visibleTabs.Count, s.Changes.Count, s.ConflictedPaths.Count, _updates.UpdateCount);
                if (navState != shownNav)
                {
                    shownNav = navState;
                    nav.RefreshItems(); // the rows (Conflicts comes and goes) and their badges
                }
                nav.SetSelectionWithoutNotify(new[] { _visibleTabs.IndexOf(SelectedTab) });

                var existed = _panels.ContainsKey(SelectedTab);
                var panel = PanelFor(SelectedTab);
                if (existed && panel is IRenderablePanel renderable) renderable.Render(); // a new panel just rendered itself
                if (panel.parent != host)
                {
                    host.Clear();
                    host.Add(panel);
                }
            };
        }

        VisualElement PanelFor(WindowTab tab)
        {
            if (!_panels.TryGetValue(tab, out var panel))
            {
                panel = _factories.TryGetValue(tab, out var factory) ? factory(_session) : new Label();
                _panels[tab] = panel;
            }
            return panel;
        }

        static string TabLabel(WindowTab tab) => tab == WindowTab.Gitignore ? GitignoreText.TabLabel : tab.ToString();

        /// <summary>
        /// The nav badge for <paramref name="tab"/>: the change count on Changes, the conflicted path count on Conflicts,
        /// the number of packages with a newer release on Updates; null (hidden) at 0 and elsewhere.
        /// </summary>
        internal string? TabBadge(WindowTab tab)
        {
            var s = _session.Snapshot;
            var count = tab == WindowTab.Changes ? s.Changes.Count
                : tab == WindowTab.Conflicts ? s.ConflictedPaths.Count
                : tab == WindowTab.Updates ? _updates.UpdateCount
                : 0;
            return count > 0 ? PanelParts.Count(count) : null;
        }

        /// <summary>The nav badge's tone: the error tone on Conflicts, the accent elsewhere.</summary>
        internal EditorUiTone TabBadgeTone(WindowTab tab) => tab == WindowTab.Conflicts ? EditorUiTone.Error : EditorUiTone.Accent;

        /// <summary>The outcome message's tone: success, error, or warning for a cancelled or refused action.</summary>
        internal static EditorUiTone OutcomeTone(OutcomeKind kind)
        {
            switch (kind)
            {
                case OutcomeKind.Succeeded: return EditorUiTone.Success;
                case OutcomeKind.Failed: return EditorUiTone.Error;
                default: return EditorUiTone.Warning; // Cancelled, Refused
            }
        }

        void ApplyAvailability(VisualElement element, GitAction action)
        {
            var availability = _session.GetAvailability(action);
            element.SetEnabled(availability.Enabled);
            element.tooltip = availability.Reason ?? string.Empty;
        }

        // Kit buttons rather than ToolbarButtons, which are drawn for Unity's flat toolbar strip. The icon sits before the text.
        static Button ToolbarAction(string name, string text, GitIcon icon, Action onClick)
        {
            var button = new Button(onClick) { name = name, text = text };
            GitIcons.Apply(button, icon);
            return button;
        }

        static Button Primary(Button button)
        {
            button.AddToClassList(EditorUiClasses.ButtonPrimary);
            return button;
        }

        static Label StateMessage(Label label)
        {
            label.AddToClassList("unitygit-state-message");
            return label;
        }

        // No repository, Git missing, Git can't open the repository and Loading: a centred kit card holding the message
        // and its button or details.
        static VisualElement StateCard(params VisualElement[] children)
        {
            var card = EditorUiElements.Card();
            card.AddToClassList("unitygit-state-card");
            foreach (var child in children) card.Add(child);
            return card;
        }

        static void SetDisplayed(VisualElement element, bool displayed)
            => element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>A nav row: the tab name and a kit badge. Rows are recycled, so <see cref="Bind"/> sets both parts.</summary>
        sealed class NavRow : VisualElement
        {
            readonly Label _name = new Label();
            readonly Label _badge = EditorUiElements.Badge(string.Empty, EditorUiTone.Accent);

            public NavRow()
            {
                AddToClassList("unitygit-nav-row");
                _name.AddToClassList("unitygit-nav-label");
                _badge.AddToClassList("unitygit-nav-badge");
                Add(_name);
                Add(_badge);
            }

            public void Bind(string text, string? badge, EditorUiTone tone)
            {
                _name.text = text;
                EditorUiElements.SetBadge(_badge, badge ?? string.Empty, tone);
                SetDisplayed(_badge, badge != null);
            }
        }
    }
}
