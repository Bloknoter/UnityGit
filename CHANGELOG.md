# Changelog

## [1.0.0] - Unreleased

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
