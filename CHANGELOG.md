# Changelog

## [1.1.0] - 2026-11-20

### Added

- Conflicts tab, shown while a file is conflicted or a merge or rebase is in progress.
- Per-place and whole-file choices (Use A, Use B, A then B, B then A), previewed until Apply; Use A and Use B for binary and deleted files.
- Mark resolved, Start over and Open in editor.
- Finish merge and Abort merge; Continue rebase and Abort rebase. Abort asks first and, with Continue rebase, handles unsaved assets and modified scenes.
- Warning for scenes and prefabs.
- "Resolve conflicts" button on the outcome bar.

### Changed

- Requires FlexGit 2.1.0.
- Pull's conflict message now says "resolve them in Conflicts".
- Commit during a rebase now says "Continue or abort the rebase first".

## [1.0.0] - 2026-11-19

Initial release.

- One editor window (Tools → Git → UnityGit) with Overview, Changes, Branches, History, Authors and More panels.
- Repo info: branch, upstream, ahead/behind, repository state, working tree changes, conflicts, local and remote branches, authors, recent commits, remotes, tags, stashes, git identity and LFS status.
- Actions: Commit (exactly the ticked files, asset and `.meta` paired), Push, Fetch, Pull (follows the user's git config) and Switch branch.
- Changes list with tick-all header, Ctrl/Shift multi-select and Space to toggle the selection.
- Git init with a Unity `.gitignore`, offered when the project is not a repository.
- Set up LFS button; Push is blocked until LFS hooks are installed in an LFS repo.
- Unsaved assets and scenes are handled before Commit, Pull and Switch; Unity reimports after Pull and Switch.
- Optional main-toolbar elements: Branch, Commit, Fetch, Pull and Push (hidden by default).
- Local changes are never discarded.
