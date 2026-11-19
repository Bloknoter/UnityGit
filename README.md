# UnityGit

UnityGit lets you work with your Unity project's git repository without leaving the editor. One window shows the repo (branch, changes, branches, history, authors, remotes, tags, stashes, LFS) and runs Commit, Push, Fetch, Pull and Switch branch. The same actions can also be shown as buttons on Unity's main toolbar. It is built on FlexGit, which runs the real `git` command line, so LFS, Git Credential Manager, SSH and hooks behave as in a terminal. The window uses the EditorUI kit, vendored inside the package as internal types, so it does not clash with a project's own copy.

## Requirements

- Unity 6000.3 or newer. The package is tested on 6000.4.x only.
- Git 2.32 or newer on your `PATH`. Git for Windows includes Git LFS and Git Credential Manager.

## Install

Add both lines to your project's `Packages/manifest.json`. FlexGit must come first:

```json
"com.bloknoter.flexgit": "https://github.com/Bloknoter/FlexGit.git#upm/v2.0.0",
"com.bloknoter.unitygit": "https://github.com/Bloknoter/UnityGit.git#v1.0.0"
```

Or do the same in Package Manager with **Add package from git URL**: add the FlexGit URL first, then the UnityGit URL.

Unity does not accept git URLs in a package's own dependencies, so FlexGit has to be added by hand. If you skip it, Package Manager reports "cannot find com.bloknoter.flexgit".

## Usage

Open the window from **Tools → Git → UnityGit**. If the project is not in a git repository, the window offers **Git init**. It creates a repository at the project root and writes a Unity `.gitignore` (an existing `.gitignore` is left alone). No commit is made.

The window has an action toolbar (branch dropdown, Commit, Fetch, Pull, Push, Refresh, Cancel while an action can be cancelled), a bar showing the last outcome, and a left navigation with these panels:

- **Overview**: branch, upstream, ahead/behind, repository state, HEAD, your git identity, remotes and LFS status.
- **Changes**: the files to commit, with a message box and a Commit button.
- **Branches**: local and remote branches; double-click to switch.
- **History**: recent commits on the current branch.
- **Authors**: names, emails and commit counts.
- **More**: tags, stashes, LFS details and locks.

Action rules:

- **Commit** commits exactly the files you tick in Changes. Ticked files are staged and unticked files are unstaged. An asset and its `.meta` file appear as one row, and ticking it commits both. A header row has a tick-all checkbox and shows how many files are ticked. Click, Ctrl+click and Shift+click select rows; ticking a row that is part of the selection applies to the whole selection, and Space toggles the selected rows. The Commit button is disabled with an empty message or nothing ticked.
- Before Commit, Pull and Switch, unsaved assets are saved and you are asked about modified scenes. If you cancel, nothing happens. If saving reveals changed files you have not seen, the commit stops and the new files are listed (ticked) so you can review them and commit again.
- **Push** pushes the current branch and sets the upstream on the first push. A rejected push is shown as an outcome ("someone pushed first — pull, then push"). If the repo uses LFS but LFS hooks are not installed, Push is blocked until you click **Set up LFS**. UnityGit never installs LFS hooks on its own.
- **Fetch** fetches from the default remote.
- **Pull** follows your git config: `pull.rebase=true` rebases, `pull.ff=only` fast-forwards only, otherwise it merges. Unity reimports the changed files afterwards.
- **Switch branch** lists local branches and remote-only branches (picking one creates a local tracking branch). If git refuses because local changes would be overwritten, nothing is discarded.
- **Conflicts, merge and rebase** are resolved outside the tool. Conflicted rows cannot be ticked, and Commit is disabled while any conflict exists and during a rebase, cherry-pick, revert or bisect. The window tells you to finish or abort it outside the tool.
- **Play Mode** disables Init, Pull and Switch. Commit, Push and Fetch stay available.
- UnityGit never discards local changes.

## Main toolbar buttons

UnityGit adds five elements to Unity's main toolbar (the bar with Play): Branch, Commit, Fetch, Pull and Push. Pull and Push show the behind and ahead counts. They read the same state as the window, so both always agree.

Unity 6000.4 has no API for choosing an element's default visibility, so all five start hidden. To show them, right-click the main toolbar and tick each element under **UnityGit**, or choose **UnityGit → Show All**. **UnityGit → Hide All** hides them again, and so does **Presets → Unity Default**. Unity remembers your choice.

## Limitations

- No diff viewer, conflict-resolution UI or history graph.
- Stash, tag, remote and LFS-lock actions are not available; they are shown as information only.
- No clone, no adding remotes and no credential UI (Git Credential Manager handles sign-in).
- Git init does not write an LFS `.gitattributes`.
- Commit stages whole files for the ticked rows. Partial staging done outside the tool (for example `git add -p`) is replaced by whole-file staging.
- Windows is the primary platform. macOS and Linux are untested.
- Unity versions older than 6000.3 are not supported.

## License

MIT. See `LICENSE.md`.
