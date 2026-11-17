#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityGit.UI
{
    public enum WindowTab { Overview, Changes, Branches, History, Authors, More }

    /// <summary>
    /// The window's content (spec §5.7), built in code so it can be created and tested without an EditorWindow.
    /// <see cref="Render"/> updates the existing elements in place, so keyboard focus, an open Details foldout and each
    /// panel's own state survive the frequent refreshes. The body is rebuilt only when the repo's availability changes.
    /// </summary>
    public sealed class GitWindowView : VisualElement
    {
        const string GitDownloadUrl = "https://git-scm.com/downloads";
        static readonly WindowTab[] Tabs = (WindowTab[])Enum.GetValues(typeof(WindowTab));

        readonly GitSession _session;
        readonly Dictionary<WindowTab, Func<GitSession, VisualElement>> _factories = new Dictionary<WindowTab, Func<GitSession, VisualElement>>();
        readonly Dictionary<WindowTab, VisualElement> _panels = new Dictionary<WindowTab, VisualElement>();

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
        readonly Foldout _outcomeDetails;
        readonly Label _outcomeDetailsText;
        ActionOutcome? _shownOutcome;

        readonly VisualElement _body;
        RepoAvailability? _bodyAvailability;
        Action _renderBody = () => { };

        public GitWindowView(GitSession session)
        {
            _session = session;
            AddToClassList("unitygit-window");

            var toolbar = new Toolbar { name = UiNames.Toolbar };
            _branchMenu = new ToolbarMenu { name = UiNames.BranchMenu };
            _commitButton = ToolbarAction(UiNames.CommitButton, "Commit", () => SelectTab(WindowTab.Changes));
            _fetchButton = ToolbarAction(UiNames.FetchButton, "Fetch", () => _ = _session.FetchAsync());
            _pullButton = ToolbarAction(UiNames.PullButton, "Pull", () => _ = _session.PullAsync());
            _pushButton = ToolbarAction(UiNames.PushButton, "Push", () => _ = _session.PushAsync());
            var spacer = new VisualElement();
            spacer.AddToClassList("unitygit-toolbar-spacer");
            _progress = new Label { name = UiNames.Progress };
            _refreshButton = ToolbarAction(UiNames.RefreshButton, "Refresh", () => _ = _session.RefreshAsync(true));
            _cancelButton = ToolbarAction(UiNames.CancelButton, "Cancel", _session.Cancel);
            toolbar.Add(_branchMenu);
            toolbar.Add(_commitButton);
            toolbar.Add(_fetchButton);
            toolbar.Add(_pullButton);
            toolbar.Add(_pushButton);
            toolbar.Add(spacer);
            toolbar.Add(_progress);
            toolbar.Add(_refreshButton);
            toolbar.Add(_cancelButton);
            Add(toolbar);

            _outcomeBar = new VisualElement();
            _outcomeBar.AddToClassList("unitygit-outcome-bar");
            var outcomeContent = new VisualElement();
            outcomeContent.AddToClassList("unitygit-outcome-content");
            _outcomeMessage = new Label { name = UiNames.Outcome };
            _outcomeDetails = new Foldout { text = "Details", value = false };
            _outcomeDetailsText = new Label();
            _outcomeDetailsText.selection.isSelectable = true; // so git's message can be copied
            _outcomeDetails.Add(_outcomeDetailsText);
            outcomeContent.Add(_outcomeMessage);
            outcomeContent.Add(_outcomeDetails);
            _outcomeBar.Add(outcomeContent);
            _outcomeBar.Add(new Button(() => _session.LastOutcome = null) { name = UiNames.OutcomeDismiss, text = "×", tooltip = "Dismiss" });
            Add(_outcomeBar);

            _body = new VisualElement();
            _body.AddToClassList("unitygit-body");
            Add(_body);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                _session.Changed -= Render;
                _session.Changed += Render;
                Render(); // catch up on changes made while detached
            });
            RegisterCallback<DetachFromPanelEvent>(_ => _session.Changed -= Render);
            Render();
        }

        public WindowTab SelectedTab { get; private set; }

        public void SelectTab(WindowTab tab)
        {
            SelectedTab = tab;
            Render();
        }

        /// <summary>Updates every element from the session's current state.</summary>
        internal void Render()
        {
            var s = _session.Snapshot;
            RenderToolbar(s);
            RenderOutcome();
            RenderBody(s);
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
            if (outcome == null || ReferenceEquals(outcome, _shownOutcome)) return; // outcomes are immutable
            _shownOutcome = outcome;

            _outcomeMessage.text = outcome.Message;
            _outcomeBar.EnableInClassList("success", outcome.Succeeded);
            _outcomeBar.EnableInClassList("failure", !outcome.Succeeded);
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
                    var message = new Label { name = UiNames.GitMissing };
                    _body.Add(MessageBox(message, new Button(() => Application.OpenURL(GitDownloadUrl)) { text = "Download Git" }));
                    return () => message.text = _session.Snapshot.Message ?? string.Empty;

                case RepoAvailability.NotARepository:
                    var init = new Button(() => _ = _session.InitAsync()) { name = UiNames.InitButton, text = "Git init" };
                    _body.Add(MessageBox(new Label("This project is not in a git repository") { name = UiNames.NotRepo }, init));
                    return () => ApplyAvailability(init, GitAction.Init);

                default:
                    _body.Add(MessageBox(new Label("Loading…")));
                    return () => { };
            }
        }

        Action BuildReadyBody()
        {
            var nav = new ListView
            {
                name = UiNames.Nav,
                itemsSource = Tabs,
                selectionType = SelectionType.Single,
                makeItem = () => new Label(),
                bindItem = (item, i) => ((Label)item).text = TabLabel(Tabs[i]),
            };
            nav.selectedIndicesChanged += _ =>
            {
                if (nav.selectedIndex >= 0) SelectTab(Tabs[nav.selectedIndex]);
            };
            var host = new VisualElement { name = UiNames.PanelHost };
            var split = new VisualElement();
            split.AddToClassList("unitygit-split");
            split.Add(nav);
            split.Add(host);
            _body.Add(split);

            var shownChanges = -1;
            return () =>
            {
                var changes = _session.Snapshot.Changes.Count;
                if (changes != shownChanges)
                {
                    shownChanges = changes;
                    nav.RefreshItems(); // rebinds "Changes (n)"
                }
                nav.SetSelectionWithoutNotify(new[] { Array.IndexOf(Tabs, SelectedTab) });

                var panel = PanelFor(SelectedTab);
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

        string TabLabel(WindowTab tab)
            => tab == WindowTab.Changes ? $"Changes ({_session.Snapshot.Changes.Count})" : tab.ToString();

        void ApplyAvailability(VisualElement element, GitAction action)
        {
            var availability = _session.GetAvailability(action);
            element.SetEnabled(availability.Enabled);
            element.tooltip = availability.Reason ?? string.Empty;
        }

        static ToolbarButton ToolbarAction(string name, string text, Action onClick)
            => new ToolbarButton(onClick) { name = name, text = text };

        static VisualElement MessageBox(params VisualElement[] children)
        {
            var box = new VisualElement();
            box.AddToClassList("unitygit-message");
            foreach (var child in children) box.Add(child);
            return box;
        }

        static void SetDisplayed(VisualElement element, bool displayed)
            => element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
