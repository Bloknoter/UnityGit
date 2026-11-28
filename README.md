# UnityGit

UnityGit lets you work with your Unity project's git repository without leaving the editor. One window shows the repo (branch, changes, branches, history, authors, remotes, tags, stashes, LFS) and runs Commit, Push, Fetch, Pull and Switch branch. The same actions can also be shown as buttons on Unity's main toolbar. It is built on FlexGit, which runs the real `git` command line, so LFS, Git Credential Manager, SSH and hooks behave as in a terminal. The window uses the EditorUI kit, vendored inside the package as internal types, so it does not clash with a project's own copy.

## Requirements

- Unity 6000.3 or newer. The package is tested on 6000.4.x only.
- Git 2.32 or newer on your `PATH`. Git for Windows includes Git LFS and Git Credential Manager.

## Install

Add both lines to your project's `Packages/manifest.json`. FlexGit must come first:

```json
"com.bloknoter.flexgit": "https://github.com/Bloknoter/FlexGit.git#upm/v2.2.0",
"com.bloknoter.unitygit": "https://github.com/Bloknoter/UnityGit.git#v1.5.0"
```

Or do the same in Package Manager with **Add package from git URL**: add the FlexGit URL first, then the UnityGit URL.

Unity does not accept git URLs in a package's own dependencies, so FlexGit has to be added by hand. If you skip it, Package Manager reports "cannot find com.bloknoter.flexgit".

## Usage

Open the window from **Tools → Git → UnityGit**. If the project is not in a git repository, the window offers **Git init**. It creates a repository at the project root and writes a Unity `.gitignore` (an existing `.gitignore` is left alone). No commit is made. If a repository is there but git refuses to open it (for example a `safe.directory` ownership check), the window shows git's own message instead, which usually ends with the command that fixes it.

The window has an action toolbar (branch dropdown, Commit, Fetch, Pull, Push, Refresh, Cancel while an action can be cancelled), a bar showing the last outcome, and a left navigation with these panels:

- **Overview**: branch, upstream, ahead/behind, repository state, HEAD, your git identity, remotes and LFS status.
- **Changes**: the files to commit, a preview of the selected file, a message box and a Commit button. See **Change preview** below.
- **Branches**: local and remote branches; double-click to switch.
- **History**: recent commits on the current branch.
- **Authors**: names, emails and commit counts.
- **Conflicts**: shown while a file is conflicted or a merge or rebase is in progress. See **Conflicts** below.
- **More**: tags, stashes, LFS details and locks.
- **Updates**: newer releases of UnityGit and FlexGit, Update buttons and every older release. See **Updates** below.

Action rules:

- **Commit** commits exactly the files you tick in Changes. Ticked files are staged and unticked files are unstaged. An asset and its `.meta` file appear as one row, and ticking it commits both. A header row has a tick-all checkbox and shows how many files are ticked. Click, Ctrl+click and Shift+click select rows; ticking a row that is part of the selection applies to the whole selection, and Space toggles the selected rows. Folder rows (on by default) sit above the files in each folder. Ticking a folder ticks every file under it, and a folder shows – when only some of its files are ticked. A folder's own `.meta` is part of its row, and ticking any file under the folder ticks it too. The folder button at the right of the header hides folder rows. Right-click a folder to revert everything in it, or to show the folder in Explorer or the Project window. The Commit button is disabled with an empty message or nothing ticked.
- **Revert** (right-click a file in Changes): puts the selected files back as they are in the last commit, or only the clicked file when it isn't part of the selection. Each asset's `.meta` goes with it. New files are deleted, and so are the folders this leaves empty unless a `.meta` for the folder is still there. It asks first and names the files. Unsaved assets are saved and you are asked about modified scenes, as for Pull. Not available during a merge, rebase, cherry-pick, revert or bisect, or in Play Mode. Conflicted files are skipped. If reverting a file would also discard another changed file you didn't select (a renamed file whose old name now holds a new file), Revert asks you to select that one too. The dialog also warns when an asset's `.meta` was never committed: it is deleted, and Unity gives the asset a new GUID. The same menu has **Show in Explorer** and **Show in Project**, which use the first selected file.
- Before Commit, Pull and Switch, unsaved assets are saved and you are asked about modified scenes. If you cancel, or choose **Don't Save**, the action is cancelled and nothing happens. If saving reveals changed files you have not seen, the commit stops and the new files are listed (ticked) so you can review them and commit again.
- **Push** pushes the current branch and sets the upstream on the first push. A rejected push is shown as an outcome ("someone pushed first — pull, then push"). If the repo uses LFS but LFS hooks are not installed, Push is blocked until you click **Set up LFS**. UnityGit never installs LFS hooks on its own.
- **Fetch** fetches from the default remote.
- **Pull** follows `pull.rebase` and `pull.ff` from your git config: `pull.rebase=true` rebases, `pull.ff=only` fast-forwards only, otherwise it merges. The per-branch `branch.<name>.rebase` setting isn't read. Unity reimports the changed files afterwards.
- **Switch branch** lists local branches and remote-only branches (picking one creates a local tracking branch). If git refuses because local changes would be overwritten, nothing is discarded.
- **Conflicts** are resolved in the Conflicts tab (see below). Conflicted rows cannot be ticked, and Commit is disabled while any conflict exists. During a rebase it says "Continue or abort the rebase first". During a cherry-pick, revert or bisect the window tells you to finish or abort it outside the tool.
- **During a merge**, every row is locked ticked and goes into the merge commit, including untracked and unrelated files. A `git commit` in a terminal would leave those out.
- **Play Mode** disables Init, Pull, Switch and all conflict actions. Commit, Push and Fetch stay available.
- UnityGit discards local changes only where you confirm it: **Revert**, **Abort** (merge or rebase) and **Start over** ask first. Using a side of a conflicted file replaces that file with the chosen version.

## Change preview

Select one file in Changes to see what changed in it since the last commit (staged and unstaged together, which is what Commit takes). The preview sits to the right of the list and runs the full height of the panel; drag the divider between them to resize. The commit message box and the Commit button share one row under the list. It only reads: selecting a file never changes what is ticked.

- **Text files**: removed lines in red with `-`, added lines in green with `+`, three unchanged lines around each change, and the old and new line numbers. The header shows the path and `+added −removed`.
- **New files**: the content with line numbers, no diff. **Deleted files**: the old content, every line red.
- **Images** (anything Unity imports as a texture, plus PNG and JPG outside the project): the current picture, scaled to fit, with its size in pixels.
- **Audio** (anything Unity imports as an audio clip): Play/Stop and the clip's length. Playback uses Unity's internal audio preview; if a Unity version removes it, Play is disabled.
- **Other binary files**: their sizes. **Git LFS files**: their real sizes.
- An asset with its `.meta` shows the asset first, then the `.meta`'s changes.
- Two or more selected files show "X files selected". A conflicted file points to the Conflicts tab.
- To keep the editor responsive, a preview shows at most 1,000 lines and cuts lines longer than 1,000 characters. A new file over 4 MB is only read in part.

## Conflicts

The Conflicts tab appears while a file is conflicted or a merge or rebase is in progress. Pull's conflict message says "resolve them in Conflicts", and the outcome bar has a **Resolve conflicts** button. The conflicted files are listed on the left and the selected file's places on the right, with a divider to drag between them.

- Pick **Use A**, **Use B**, **A then B** or **B then A** for each conflict place, or for the whole file. Choices are a preview until you click **Apply**. Places left without a choice keep their conflict markers. When none remain, the file is staged.
- Each line of the preview shows its line number in the file as it is on disk, so it matches your editor.
- Binary files, LFS-tracked files, files deleted on one side and files whose conflict markers can't be read offer only **Use A** and **Use B**, which take that side's whole file.
- **Mark resolved** accepts a file you edited by hand. **Start over** restores the conflict markers (it asks first). **Open in editor** opens the file at the selected place.
- A text file whose conflict markers you removed by hand is finished with **Mark resolved**. **Use A** and **Use B** aren't offered for it: taking a whole side would drop your edits and the changes git merged cleanly from the other side.
- **Merge**: **Finish merge** commits what is staged (the resolved files) with git's own message, while **Commit** in the Changes panel commits with your own message. **Abort merge** cancels the merge. **Rebase**: **Continue rebase** and **Abort rebase**. Abort asks for confirmation. Abort and Continue rebase save assets and ask about modified scenes first.
- Conflicts from a cherry-pick, revert or stash apply can have their files resolved here, but finishing or aborting the operation is done outside UnityGit.
- Scenes and prefabs show a warning: picking places one by one can break them.
- Pending choices are not kept across a script reload. The selected tab is.

## Updates

The Updates tab installs newer or older releases of UnityGit and FlexGit when they come from git URLs in `Packages/manifest.json`. A release is a tag: `vX.Y.Z` for UnityGit and `upm/vX.Y.Z` for FlexGit (FlexGit's plain `vX.Y.Z` tags are not in the package layout and are never offered). The repository URLs are the ones in your manifest, so forks work too.

- **Checking**: once per editor session, a few seconds after the editor loads, UnityGit reads both repositories' tags with `git ls-remote`, which downloads nothing, whether or not the window is open. **Check for updates** checks again. When something is newer, the Updates tab shows how many packages have an update and the Console has one line saying so. The check never asks for a sign-in: on a private repository without saved credentials, or offline, it fails quietly and the tab shows why. Each git call gives up after 30 seconds.
- **Update** installs the newest release of one package. **Update all** installs the newest UnityGit with the newest FlexGit it can use. **Other versions** lists every release, newest first, to move to; the installed one is ticked.
- **The two packages stay compatible.** UnityGit needs the FlexGit version its `package.json` names (same major version, that version or newer). Installing a UnityGit release also installs the FlexGit it needs, in the same step. FlexGit releases the installed UnityGit can't use are disabled in the list, with the reason. A release whose `package.json` needs a newer Unity than yours isn't offered.
- **Installing** asks first and lists each change ("UnityGit 1.2.0 → 1.4.0, FlexGit 2.2.0 → 2.3.0"). Package Manager then rewrites the manifest entries in one go and Unity recompiles. The outcome bar says what was installed, or Package Manager's error, or that the scripts don't compile afterwards. Not available in Play Mode, during a merge or rebase, or while another git action runs.
- Releases before 1.4.0 have no Updates tab. Moving back to one asks first and says so; to come back, use Package Manager or edit `Packages/manifest.json`.
- Embedded, local and registry packages are shown but never changed. The release cache lives in `Library/UnityGit/ReleaseCache.git` and can be deleted at any time.

## Main toolbar buttons

UnityGit adds five elements to Unity's main toolbar (the bar with Play): Branch, Commit, Fetch, Pull and Push. Pull and Push show the behind and ahead counts. They read the same state as the window, so both always agree.

Unity 6000.4 has no API for choosing an element's default visibility, so all five start hidden. To show them, right-click the main toolbar and tick each element under **UnityGit**, or choose **UnityGit → Show All**. **UnityGit → Hide All** hides them again, and so does **Presets → Unity Default**. Unity remembers your choice.

## Limitations

- The change preview is read-only and compares with the last commit only: no side-by-side view, no staging or reverting single lines, no diffs of past commits. No history graph.
- No Unity-aware scene or prefab merging, no custom text for a conflict place, and no continue or abort for a cherry-pick or revert.
- Stash, tag, remote and LFS-lock actions are not available; they are shown as information only.
- No clone, no adding remotes and no credential UI (Git Credential Manager handles sign-in).
- Git init does not write an LFS `.gitattributes`.
- Commit stages whole files for the ticked rows. Partial staging done outside the tool (for example `git add -p`) is replaced by whole-file staging.
- Windows is the primary platform. macOS and Linux are untested.
- Unity versions older than 6000.3 are not supported.
- Reverting every file of a new folder leaves the empty folder behind, and Unity gives it a new `.meta`.
- The Updates tab is in the window's navigation, which shows only while the project is in a repository git can open. It reads tags only (no release notes), ignores pre-release tags such as `v1.4.0-beta`, and offers releases of UnityGit and FlexGit only.

## License

MIT. See `LICENSE.md`.
