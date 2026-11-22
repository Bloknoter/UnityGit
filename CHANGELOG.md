# Changelog

## [1.2.0] - 2026-11-22

### Added

- Change preview to the right of the Changes list, for the one selected file: a diff for text files (red `-`, green `+`, line numbers), the content of new files, the picture of images, Play/Stop for audio, and sizes for binary and LFS files. An asset's `.meta` changes follow the asset.
- A divider between the list and the preview to resize them.

### Changed

- Requires FlexGit 2.2.0.
- Conflicts tab: the selected file's places are to the right of the file list instead of under it, with a divider to resize them.
- Changes tab: the commit message box and the Commit button are on one row.

## [1.1.0] - 2026-11-20

### Added

- Conflicts tab, shown while a file is conflicted or a merge or rebase is in progress.
- Per-place and whole-file choices (Use A, Use B, A then B, B then A), previewed until Apply; Use A and Use B for binary and deleted files.
- The picked option is highlighted, with the sides it uses; a whole-file button shows when every place has its pick.
- Line numbers in the conflict preview, matching the file on disk.
- LFS-tracked files in conflict take a whole side (Use A or Use B), which restores the real file instead of LFS pointer text.
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
