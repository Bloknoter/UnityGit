# UnityGit

UnityGit lets you work with your Unity project's git repository without leaving the editor. One window shows the repo (branch, changes, branches, history, authors, remotes, tags, stashes, LFS) and runs Commit, Push, Fetch, Pull and Switch branch. The same actions can also be shown as buttons on Unity's main toolbar. It is built on FlexGit, which runs the real `git` command line, so LFS, Git Credential Manager, SSH and hooks behave as in a terminal. The window uses the EditorUI kit, vendored inside the package as internal types, so it does not clash with a project's own copy.

## Requirements

- Unity 6000.3 or newer. The package is tested on 6000.4.x only.
- Git 2.32 or newer on your `PATH`. Git for Windows includes Git LFS and Git Credential Manager.

## Install

Add both lines to your project's `Packages/manifest.json`. FlexGit must come first:

```json
"com.bloknoter.flexgit": "https://github.com/Bloknoter/FlexGit.git#upm/v2.1.0",
"com.bloknoter.unitygit": "https://github.com/Bloknoter/UnityGit.git#v1.1.0"
```

Or do the same in Package Manager with **Add package from git URL**: add the FlexGit URL first, then the UnityGit URL.

Unity does not accept git URLs in a package's own dependencies, so FlexGit has to be added by hand. If you skip it, Package Manager reports "cannot find com.bloknoter.flexgit".

## Usage

Open the window from **Tools → Git → UnityGit**. If the project is not in a git repository, the window offers **Git init**. It creates a repository at the project root and writes a Unity `.gitignore` (an existing `.gitignore` is left alone). No commit is made. If a repository is there but git refuses to open it (for example a `safe.directory` ownership check), the window shows git's own message instead, which usually ends with the command that fixes it.

The window has an action toolbar (branch dropdown, Commit, Fetch, Pull, Push, Refresh, Cancel while an action can be cancelled), a bar showing the last outcome, and a left navigation with these panels:

- **Overview**: branch, upstream, ahead/behind, repository state, HEAD, your git identity, remotes and LFS status.
- **Changes**: the files to commit, with a message box and a Commit button.
- **Branches**: local and remote branches; double-click to switch.
- **History**: recent commits on the current branch.
- **Authors**: names, emails and commit counts.
- **Conflicts**: shown while a file is conflicted or a merge or rebase is in progress. See **Conflicts** below.
- **More**: tags, stashes, LFS details and locks.

Action rules:

- **Commit** commits exactly the files you tick in Changes. Ticked files are staged and unticked files are unstaged. An asset and its `.meta` file appear as one row, and ticking it commits both. A header row has a tick-all checkbox and shows how many files are ticked. Click, Ctrl+click and Shift+click select rows; ticking a row that is part of the selection applies to the whole selection, and Space toggles the selected rows. The Commit button is disabled with an empty message or nothing ticked.
- Before Commit, Pull and Switch, unsaved assets are saved and you are asked about modified scenes. If you cancel, or choose **Don't Save**, the action is cancelled and nothing happens. If saving reveals changed files you have not seen, the commit stops and the new files are listed (ticked) so you can review them and commit again.
- **Push** pushes the current branch and sets the upstream on the first push. A rejected push is shown as an outcome ("someone pushed first — pull, then push"). If the repo uses LFS but LFS hooks are not installed, Push is blocked until you click **Set up LFS**. UnityGit never installs LFS hooks on its own.
- **Fetch** fetches from the default remote.
- **Pull** follows `pull.rebase` and `pull.ff` from your git config: `pull.rebase=true` rebases, `pull.ff=only` fast-forwards only, otherwise it merges. The per-branch `branch.<name>.rebase` setting isn't read. Unity reimports the changed files afterwards.
- **Switch branch** lists local branches and remote-only branches (picking one creates a local tracking branch). If git refuses because local changes would be overwritten, nothing is discarded.
- **Conflicts** are resolved in the Conflicts tab (see below). Conflicted rows cannot be ticked, and Commit is disabled while any conflict exists. During a rebase it says "Continue or abort the rebase first". During a cherry-pick, revert or bisect the window tells you to finish or abort it outside the tool.
- **During a merge**, every row is locked ticked and goes into the merge commit, including untracked and unrelated files. A `git commit` in a terminal would leave those out.
- **Play Mode** disables Init, Pull, Switch and all conflict actions. Commit, Push and Fetch stay available.
- UnityGit discards local changes only where you confirm it: **Abort** (merge or rebase) and **Start over** ask first. Using a side of a conflicted file replaces that file with the chosen version.

## Conflicts

The Conflicts tab appears while a file is conflicted or a merge or rebase is in progress. Pull's conflict message says "resolve them in Conflicts", and the outcome bar has a **Resolve conflicts** button.

- Pick **Use A**, **Use B**, **A then B** or **B then A** for each conflict place, or for the whole file. Choices are a preview until you click **Apply**. Places left without a choice keep their conflict markers. When none remain, the file is staged.
- Binary files, LFS-tracked files, files deleted on one side and files whose conflict markers can't be read offer only **Use A** and **Use B**, which take that side's whole file.
- **Mark resolved** accepts a file you edited by hand. **Start over** restores the conflict markers (it asks first). **Open in editor** opens the file at the selected place.
- A text file whose conflict markers you removed by hand is finished with **Mark resolved**. **Use A** and **Use B** aren't offered for it: taking a whole side would drop your edits and the changes git merged cleanly from the other side.
- **Merge**: **Finish merge** commits what is staged (the resolved files) with git's own message, while **Commit** in the Changes panel commits with your own message. **Abort merge** cancels the merge. **Rebase**: **Continue rebase** and **Abort rebase**. Abort asks for confirmation. Abort and Continue rebase save assets and ask about modified scenes first.
- Conflicts from a cherry-pick, revert or stash apply can have their files resolved here, but finishing or aborting the operation is done outside UnityGit.
- Scenes and prefabs show a warning: picking places one by one can break them.
- Pending choices are not kept across a script reload. The selected tab is.

## Main toolbar buttons

UnityGit adds five elements to Unity's main toolbar (the bar with Play): Branch, Commit, Fetch, Pull and Push. Pull and Push show the behind and ahead counts. They read the same state as the window, so both always agree.

Unity 6000.4 has no API for choosing an element's default visibility, so all five start hidden. To show them, right-click the main toolbar and tick each element under **UnityGit**, or choose **UnityGit → Show All**. **UnityGit → Hide All** hides them again, and so does **Presets → Unity Default**. Unity remembers your choice.

## Limitations

- No diff viewer or history graph.
- No Unity-aware scene or prefab merging, no custom text for a conflict place, and no continue or abort for a cherry-pick or revert.
- Stash, tag, remote and LFS-lock actions are not available; they are shown as information only.
- No clone, no adding remotes and no credential UI (Git Credential Manager handles sign-in).
- Git init does not write an LFS `.gitattributes`.
- Commit stages whole files for the ticked rows. Partial staging done outside the tool (for example `git add -p`) is replaced by whole-file staging.
- Windows is the primary platform. macOS and Linux are untested.
- Unity versions older than 6000.3 are not supported.

## License

MIT. See `LICENSE.md`.
