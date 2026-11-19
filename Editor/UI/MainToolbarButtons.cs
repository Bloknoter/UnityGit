#nullable enable
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace UnityGit.UI
{
    /// <summary>What a main-toolbar click shows once its action has finished.</summary>
    internal enum ClickFeedback { None, Notification, OpenWindow }

    /// <summary>
    /// Optional buttons on Unity's main toolbar (spec §5.8): Branch, Commit, Fetch, Pull and Push. Unity builds each element
    /// from its [MainToolbarElement] method and lists it under UnityGit in the toolbar's context menu, where the user shows
    /// or hides it; Unity 6000.4 starts every element outside its own preset hidden, and offers no way to change that.
    /// Each session change rebuilds all five (<see cref="RefreshAll"/>), so labels and enabled states follow the snapshot.
    /// The factories take the session, so tests call them directly.
    /// </summary>
    internal static class MainToolbarButtons
    {
        internal const string BranchPath = "UnityGit/Branch", CommitPath = "UnityGit/Commit", FetchPath = "UnityGit/Fetch",
                              PullPath = "UnityGit/Pull", PushPath = "UnityGit/Push";

        static readonly string[] Paths = { BranchPath, CommitPath, FetchPath, PullPath, PushPath };
        const double NotificationSeconds = 2;

        [MainToolbarElement(BranchPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement Branch() => CreateBranch(SessionProvider.Current);

        [MainToolbarElement(CommitPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement Commit() => CreateCommit(SessionProvider.Current);

        [MainToolbarElement(FetchPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement Fetch() => CreateFetch(SessionProvider.Current);

        [MainToolbarElement(PullPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement Pull() => CreatePull(SessionProvider.Current);

        [MainToolbarElement(PushPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement Push() => CreatePush(SessionProvider.Current);

        /// <summary>A dropdown showing the current branch; picking another branch switches to it.</summary>
        internal static MainToolbarElement CreateBranch(GitSession s)
            => Create(s, GitAction.Switch, ActionLabels.Branch(s.Snapshot), GitIcon.Branch, "Switch branch",
                content => new MainToolbarDropdown(content, rect => ShowBranchMenu(s, rect)));

        /// <summary>Opens the window on the Changes panel, where the user commits.</summary>
        internal static MainToolbarElement CreateCommit(GitSession s)
            => Create(s, GitAction.Commit, "Commit", GitIcon.Commit, "Commit", content => new MainToolbarButton(content, GitWindow.ShowChanges));

        internal static MainToolbarElement CreateFetch(GitSession s)
            => ActionButton(s, GitAction.Fetch, "Fetch", GitIcon.Fetch, "Fetch", s.FetchAsync);

        internal static MainToolbarElement CreatePull(GitSession s)
            => ActionButton(s, GitAction.Pull, ActionLabels.Pull(s.Snapshot), GitIcon.Pull, "Pull", s.PullAsync);

        internal static MainToolbarElement CreatePush(GitSession s)
            => ActionButton(s, GitAction.Push, ActionLabels.Push(s.Snapshot), GitIcon.Push, "Push", s.PushAsync);

        /// <summary>Rebuilds the five elements from the current session. Does nothing while there is no main toolbar, as in batch mode.</summary>
        internal static void RefreshAll()
        {
            foreach (var path in Paths) MainToolbar.Refresh(path);
        }

        /// <summary>
        /// Records what the window needs and picks the click's feedback, since the toolbar has no outcome bar: a failed or
        /// refused action opens the window, whose outcome bar explains it, and a success shows a short notification. The
        /// runner records the outcome of every action that ran; a refusal by availability never ran, so it is recorded here.
        /// The window's branch list records its double-click outcomes through this too, and ignores the feedback.
        /// </summary>
        internal static ClickFeedback RecordOutcome(GitSession s, ActionOutcome outcome)
        {
            switch (outcome.Kind)
            {
                case OutcomeKind.Succeeded:
                    return ClickFeedback.Notification;
                case OutcomeKind.Failed:
                    return ClickFeedback.OpenWindow;
                case OutcomeKind.Refused:
                    s.LastOutcome = outcome;
                    return ClickFeedback.OpenWindow;
                default:
                    return ClickFeedback.None; // Cancelled: only the window can cancel, and it shows the outcome
            }
        }

        // The element follows the action's availability: disabled when it can't run, with the reason as its tooltip.
        static MainToolbarElement Create(GitSession s, GitAction action, string text, GitIcon icon, string name,
            Func<MainToolbarContent, MainToolbarElement> create)
        {
            var availability = s.GetAvailability(action);
            var element = create(new MainToolbarContent(text, GitIcons.LoadForMainToolbar(icon), availability.Reason ?? name));
            element.enabled = availability.Enabled;
            return element;
        }

        static MainToolbarElement ActionButton(GitSession s, GitAction action, string text, GitIcon icon, string name,
            Func<Task<ActionOutcome>> run)
            => Create(s, action, text, icon, name, content => new MainToolbarButton(content, () => _ = RunAsync(s, run)));

        // Built when it opens, so it lists the branches as they are then. Like the window's branch menu, the current branch
        // is ticked and can't be picked, and nothing can be picked while switching isn't available.
        static void ShowBranchMenu(GitSession s, Rect rect)
        {
            var menu = new GenericMenu();
            var snapshot = s.Snapshot;
            var canSwitch = s.GetAvailability(GitAction.Switch).Enabled;
            foreach (var branch in ActionLabels.BranchMenuItems(snapshot))
            {
                var item = new GUIContent(branch);
                var current = !snapshot.IsDetached && snapshot.Branch == branch;
                if (current || !canSwitch) menu.AddDisabledItem(item, current);
                else menu.AddItem(item, false, () => _ = RunAsync(s, () => s.SwitchBranchAsync(branch)));
            }
            menu.DropDown(rect);
        }

        // Clicks fire and forget, as in the window. The session returns an outcome rather than throwing; anything else that
        // fails is logged, so nothing throws into the console from a click (spec §5.9).
        static async Task RunAsync(GitSession s, Func<Task<ActionOutcome>> run)
        {
            try
            {
                var outcome = await run();
                switch (RecordOutcome(s, outcome))
                {
                    case ClickFeedback.Notification:
                        var focused = EditorWindow.focusedWindow;
                        if (focused != null) focused.ShowNotification(new GUIContent(outcome.Message), NotificationSeconds);
                        break;
                    case ClickFeedback.OpenWindow:
                        GitWindow.Open();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
