#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine.UIElements;
using UnityGit.UI.Theme;

namespace UnityGit.UI
{
    internal enum GitignoreMode { Blocks, PlainText }

    internal enum GitignoreSwitchChoice { Save, DontSave, Cancel }

    /// <summary>
    /// The .gitignore tab (spec "UnityGit: .gitignore editor tab"). It keeps the selected file's text and the baseline (the
    /// text last read from or written to disk); the text is unsaved while they differ. It saves at once or after typing
    /// stops when auto-save is on, otherwise on Save. It never saves while a git action runs, and a file that changed on
    /// disk under unsaved edits raises a warning instead of being overwritten unseen. Unsaved text is kept in SessionState,
    /// so a script reload keeps it.
    /// </summary>
    internal sealed class GitignorePanel : VisualElement, IRenderablePanel
    {
        internal const string ModePref = "UnityGit.GitignoreMode";
        internal const string AutoSavePref = "UnityGit.GitignoreAutoSave";
        // SessionState keys, each followed by the project path.
        internal const string FileKey = "UnityGit.GitignoreFile.";
        internal const string DraftPathKey = "UnityGit.GitignoreDraftPath.";
        internal const string DraftTextKey = "UnityGit.GitignoreDraftText.";
        internal const string DraftBaseKey = "UnityGit.GitignoreDraftBase.";
        const long AutoSaveDelayMs = 1000;

        readonly GitSession _session;
        readonly string _project;
        readonly DropdownField _files;
        readonly ToggleButtonGroup _modeGroup;
        readonly Button _undo;
        readonly Toggle _autoSaveToggle;
        readonly Button _save;
        readonly List<string> _undoStack = new List<string>(); // the texts before each ×, drop and Add rule
        const int UndoLimit = 50;
        string? _blocksText;     // what the Blocks view shows, rebuilt only when the text changed
        bool? _blocksReadOnly;
        readonly VisualElement _message;
        readonly Button _createTemplate;
        readonly Button _createEmpty;
        readonly Button _reload;
        readonly VisualElement _body;

        List<string> _fileList = new List<string>();
        bool _listLoaded;
        string? _filePath;  // repo-relative, '/'
        string? _fullPath;  // null until the file is read
        string _text = string.Empty;
        string _baseline = string.Empty;
        GitignoreFileText? _stamp;       // the disk version the baseline came from; null when unknown (a restored draft)
        GitignoreFileText? _diskVersion; // a newer disk version found under unsaved edits
        bool _hasBom;
        string _lineEnding = "\n";
        bool _isUtf8 = true;
        string? _saveError;
        bool _saveBlocked;               // an auto-save waited for a git action to end
        (bool, bool)? _reported;         // (HasUnsavedChanges, AutoSave) last told through UnsavedChanged
        string? _noteText;
        RepoSnapshot? _seen;
        IVisualElementScheduledItem? _pendingSave;
        Task _loaded = Task.CompletedTask;
        bool _reloading;
        bool _reloadQueued;
        string? _shownText;
        bool? _shownReadOnly;

        public GitignorePanel(GitSession session)
        {
            _session = session;
            _project = session.ProjectPath;
            AddToClassList(PanelClasses.Content);
            AddToClassList("unitygit-gitignore");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("unitygit-gitignore-toolbar");
            _files = new DropdownField { name = UiNames.GitignoreFiles };
            _files.AddToClassList("unitygit-gitignore-files");
            _files.RegisterValueChangedCallback(e =>
            {
                if (!string.IsNullOrEmpty(e.newValue) && e.newValue != _filePath) _ = SelectFileAsync(e.newValue);
            });
            _modeGroup = new ToggleButtonGroup { name = UiNames.GitignoreMode, isMultipleSelection = false, allowEmptySelection = false };
            _modeGroup.Add(new Button { text = GitignoreText.Blocks });
            _modeGroup.Add(new Button { text = GitignoreText.PlainText });
            _modeGroup.RegisterValueChangedCallback(e => Mode = e.newValue[(int)GitignoreMode.PlainText] ? GitignoreMode.PlainText : GitignoreMode.Blocks);
            _undo = new Button(Undo) { name = UiNames.GitignoreUndo, text = GitignoreText.Undo };
            _autoSaveToggle = new Toggle(GitignoreText.AutoSave) { name = UiNames.GitignoreAutoSave };
            _autoSaveToggle.AddToClassList("unitygit-gitignore-auto-save");
            _autoSaveToggle.RegisterValueChangedCallback(e => AutoSave = e.newValue);
            _save = new Button(() => Save()) { name = UiNames.GitignoreSave, text = GitignoreText.Save };
            _save.AddToClassList(EditorUiClasses.ButtonPrimary);
            toolbar.Add(_files);
            toolbar.Add(_modeGroup);
            toolbar.Add(_undo);
            toolbar.Add(EditorUiElements.ToolbarSpacer());
            toolbar.Add(_autoSaveToggle);
            toolbar.Add(_save);
            Add(toolbar);

            // A kit message for the empty state, a conflicted or unreadable file, an outside change and a failed save.
            _message = EditorUiElements.Message(string.Empty, EditorUiTone.Info);
            _message.name = UiNames.GitignoreNote;
            _message.AddToClassList("unitygit-gitignore-message");
            var actions = new VisualElement();
            actions.AddToClassList("unitygit-gitignore-actions");
            _createTemplate = new Button(() => _ = CreateAsync(true)) { name = UiNames.GitignoreCreateTemplate, text = GitignoreText.CreateTemplate };
            _createTemplate.AddToClassList(EditorUiClasses.ButtonPrimary);
            _createEmpty = new Button(() => _ = CreateAsync(false)) { name = UiNames.GitignoreCreateEmpty, text = GitignoreText.CreateEmpty };
            _reload = new Button(Reload) { name = UiNames.GitignoreReload, text = GitignoreText.Reload };
            actions.Add(_createTemplate);
            actions.Add(_createEmpty);
            actions.Add(_reload);
            _message.Add(actions);
            Add(_message);

            _body = new VisualElement { name = UiNames.GitignoreBody };
            _body.AddToClassList("unitygit-gitignore-body");
            PlainView = new GitignorePlainView();
            PlainView.Edited += SetPlainText;
            PlainView.Field.RegisterCallback<FocusOutEvent>(_ => FlushPendingSave());
            BlocksView = new GitignoreBlocksView();
            BlocksView.RemoveRequested += RemoveBlock;
            BlocksView.MoveRequested += MoveBlock;
            _body.Add(BlocksView);
            _body.Add(PlainView);
            Add(_body);

            // Switching tabs or closing the window saves what auto-save was waiting to save.
            RegisterCallback<DetachFromPanelEvent>(_ => FlushPendingSave());
            RestoreDraft();
            Render(); // the window doesn't render a panel it just made
        }

        /// <summary>The latest load of the file list and the selected file.</summary>
        internal Task Loaded => _loaded;

        internal IReadOnlyList<string> Files => _fileList;

        /// <summary>The selected .gitignore, repo-relative with '/'; null when there is none.</summary>
        internal string? FilePath => _filePath;

        /// <summary>The current text, LF.</summary>
        internal string Text => _text;

        internal bool HasUnsavedChanges => _fullPath != null && GitignoreFile.Normalize(_text) != GitignoreFile.Normalize(_baseline);

        /// <summary>No file, a conflicted file or one that isn't UTF-8: nothing can be edited.</summary>
        internal bool IsReadOnly => _fullPath == null || !_isUtf8 || IsConflicted;

        /// <summary>The message shown above the editor, or null.</summary>
        internal string? NoteText => _noteText;

        internal GitignorePlainView PlainView { get; }

        internal GitignoreBlocksView BlocksView { get; }

        internal bool CanUndo => _undoStack.Count > 0 && !IsReadOnly;

        internal GitignoreMode Mode
        {
            get => EditorPrefs.GetInt(ModePref, 0) == (int)GitignoreMode.PlainText ? GitignoreMode.PlainText : GitignoreMode.Blocks;
            set
            {
                EditorPrefs.SetInt(ModePref, (int)value);
                RenderState();
            }
        }

        /// <summary>Saved per user (EditorPrefs), on unless turned off. Turning it on saves unsaved text at once.</summary>
        internal bool AutoSave
        {
            get => EditorPrefs.GetBool(AutoSavePref, true);
            set
            {
                if (value == AutoSave) return;
                EditorPrefs.SetBool(AutoSavePref, value);
                if (value) FlushPendingSave();
                NotifyUnsaved();
                RenderState();
            }
        }

        /// <summary>Raised when <see cref="HasUnsavedChanges"/> or <see cref="AutoSave"/> changes.</summary>
        public event Action? UnsavedChanged;

        bool IsConflicted => _filePath != null && _session.Snapshot.ConflictedPaths.Contains(_filePath);

        public void Render()
        {
            var s = _session.Snapshot;
            if (!ReferenceEquals(s, _seen))
            {
                _seen = s;
                CheckDisk(); // before an auto-save that waited, so it can't overwrite what the action wrote
                _ = ReloadAsync();
            }
            if (_saveBlocked && !_session.IsBusy) FlushPendingSave();
            RenderState();
        }

        /// <summary>Typing in Plain text: auto-save waits until typing stops.</summary>
        internal void SetPlainText(string text)
        {
            if (IsReadOnly || text == _text) return;
            _undoStack.Clear(); // Undo steps back through block edits only; typing has the text box's own
            SetText(text);
            if (!AutoSave) return;
            _pendingSave ??= schedule.Execute(FlushPendingSave);
            _pendingSave.ExecuteLater(AutoSaveDelayMs);
        }

        /// <summary>An edit made at once (×, a drop, Add rule): auto-save saves it now.</summary>
        internal void ReplaceText(string text, bool undoable)
        {
            if (IsReadOnly || text == _text) return;
            if (undoable)
            {
                _undoStack.Add(_text);
                if (_undoStack.Count > UndoLimit) _undoStack.RemoveAt(0);
            }
            SetText(text);
            if (AutoSave) FlushPendingSave();
        }

        /// <summary>The × of a block: its lines go (spec "Edits").</summary>
        internal void RemoveBlock(int block) => ReplaceText(GitignoreDocument.Parse(_text).Remove(block).ToText(), undoable: true);

        /// <summary>A block dropped at a position between blocks; nothing happens when it wouldn't move.</summary>
        internal void MoveBlock(int block, int position)
        {
            var moved = GitignoreDocument.Parse(_text).Move(block, position);
            if (moved != null) ReplaceText(moved.ToText(), undoable: true);
        }

        /// <summary>Puts back the text from before the last ×, drop or Add rule, and saves it when auto-save is on.</summary>
        internal void Undo()
        {
            if (!CanUndo) return;
            var previous = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            SetText(previous);
            if (AutoSave) FlushPendingSave();
        }

        /// <summary>Saves now when auto-save is on and the text is unsaved, unless the file changed on disk meanwhile.</summary>
        internal void FlushPendingSave()
        {
            _pendingSave?.Pause();
            if (AutoSave && HasUnsavedChanges && !IsReadOnly && _diskVersion == null) Save();
        }

        /// <summary>Writes the text in the file's own format. False when it failed (the message says why) or a git action runs.</summary>
        internal bool Save()
        {
            if (_fullPath == null || IsReadOnly) return false;
            if (_session.IsBusy)
            {
                _saveBlocked = true;
                return false;
            }
            _saveBlocked = false;
            _pendingSave?.Pause();
            try
            {
                GitignoreFile.Write(_fullPath, _text, _hasBom, _lineEnding);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _saveError = GitignoreText.SaveFailed(_filePath ?? ".gitignore", ex.Message);
                RenderState();
                return false;
            }
            _stamp = GitignoreFile.Read(_fullPath);
            _baseline = _text;
            _diskVersion = null;
            _saveError = null;
            EraseDraft();
            NotifyUnsaved();
            RenderState();
            _ = _session.RefreshAsync(); // Changes shows what the new rules ignore
            return true;
        }

        /// <summary>
        /// Reads the file again when its time or length changed. Same content: nothing to do. No unsaved edits: the new text
        /// replaces the old. Unsaved edits: they stay and a warning offers Reload.
        /// </summary>
        internal void CheckDisk()
        {
            if (_fullPath == null) return;
            if (_stamp != null && GitignoreFile.SameStamp(_stamp, _fullPath)) return;
            var disk = GitignoreFile.Read(_fullPath);
            if (disk == null)
            {
                _ = ReloadAsync(); // gone: the file list decides what's shown
                return;
            }
            _hasBom = disk.HasBom;
            _lineEnding = disk.LineEnding;
            _isUtf8 = disk.IsUtf8;
            if (GitignoreFile.Normalize(disk.Text) == GitignoreFile.Normalize(_baseline)) _stamp = disk;
            else if (!HasUnsavedChanges) TakeDisk(disk);
            else _diskVersion = disk;
            RenderState();
        }

        /// <summary>The Reload button: the disk version replaces the unsaved text.</summary>
        internal void Reload()
        {
            if (_fullPath == null) return;
            TakeDisk(GitignoreFile.Read(_fullPath));
            RenderState();
        }

        /// <summary>Creates the project folder's .gitignore (never over an existing file) and selects it.</summary>
        internal async Task CreateAsync(bool template)
        {
            var full = Path.Combine(_project, ".gitignore");
            if (!File.Exists(full))
            {
                try
                {
                    GitignoreFile.Write(full, template ? GitignoreTemplate.Text : string.Empty, false, "\n");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _saveError = GitignoreText.SaveFailed(".gitignore", ex.Message);
                    RenderState();
                    return;
                }
            }
            _filePath = _session.ProjectGitignorePath ?? ".gitignore";
            _fullPath = null; // read it on the reload
            SessionState.SetString(FileKey + _project, _filePath);
            await ReloadAsync();
            _ = _session.RefreshAsync();
        }

        /// <summary>
        /// Asks what to do with unsaved text before another file is opened. Tests replace it; the default is Unity's
        /// three-button dialog.
        /// </summary>
        internal Func<string, GitignoreSwitchChoice> AskSwitch { get; set; } = AskSwitchInDialog;

        /// <summary>
        /// Opens another .gitignore. Unsaved text is saved first with auto-save on, otherwise <see cref="AskSwitch"/> decides:
        /// save, drop it, or stay. False when it stayed or the save failed.
        /// </summary>
        internal Task<bool> SelectFileAsync(string repoPath)
        {
            if (repoPath == _filePath) return Task.FromResult(true);
            if (AutoSave) FlushPendingSave();
            if (HasUnsavedChanges) // auto-save off, or it couldn't save (a git action runs, the file changed on disk)
            {
                switch (AskSwitch(_filePath ?? ".gitignore"))
                {
                    case GitignoreSwitchChoice.Save:
                        if (Save()) break;
                        RenderState();
                        return Task.FromResult(false);
                    case GitignoreSwitchChoice.DontSave:
                        Discard();
                        break;
                    default:
                        RenderState(); // puts the dropdown back
                        return Task.FromResult(false);
                }
            }
            LoadFile(repoPath);
            RenderState();
            return Task.FromResult(true);
        }

        /// <summary>What the window reports to Unity's close prompt: unsaved text that auto-save won't save.</summary>
        internal bool ReportsUnsavedChanges => !AutoSave && HasUnsavedChanges;

        /// <summary>Drops the unsaved text: back to the baseline.</summary>
        internal void Discard()
        {
            _pendingSave?.Pause();
            _text = _baseline;
            _diskVersion = null;
            EraseDraft();
            NotifyUnsaved();
            RenderState();
        }

        /// <summary>Whether this project has unsaved .gitignore text kept from before a script reload.</summary>
        internal static bool HasDraft(string projectPath) => SessionState.GetString(DraftPathKey + projectPath, string.Empty).Length > 0;

        static GitignoreSwitchChoice AskSwitchInDialog(string path)
        {
            var choice = EditorUtility.DisplayDialogComplex(GitignoreText.SwitchTitle(path), GitignoreText.SwitchMessage,
                GitignoreText.SwitchSave, GitignoreText.SwitchCancel, GitignoreText.SwitchDontSave);
            return choice == 0 ? GitignoreSwitchChoice.Save : choice == 2 ? GitignoreSwitchChoice.DontSave : GitignoreSwitchChoice.Cancel;
        }

        // ---- loading ----

        // One reload at a time: a request during one queues exactly one more, and the task returned covers it.
        Task ReloadAsync()
        {
            if (_reloading)
            {
                _reloadQueued = true;
                return _loaded;
            }
            _reloading = true;
            return _loaded = ReloadLoopAsync();
        }

        async Task ReloadLoopAsync()
        {
            try
            {
                do
                {
                    _reloadQueued = false;
                    await ReloadOnceAsync();
                } while (_reloadQueued);
            }
            finally
            {
                _reloading = false;
            }
        }

        async Task ReloadOnceAsync()
        {
            if (_session.Snapshot.Availability != RepoAvailability.Ready) return; // a session still loading knows no files
            var files = (await _session.ListGitignoreFilesAsync()).ToList();
            // A file with unsaved edits stays listed even when it's gone from disk: saving brings it back.
            if (_filePath != null && !files.Contains(_filePath) && HasUnsavedChanges) files.Add(_filePath);
            _fileList = files;
            _listLoaded = true;
            if (_filePath != null && files.Contains(_filePath))
            {
                if (_fullPath == null) LoadFile(_filePath);
                else CheckDisk();
            }
            else if (files.Count > 0) LoadFile(DefaultFile(files));
            else ClearFile();
            RenderState();
        }

        // The pick kept for this project, else the project folder's, else the root's, else the first.
        string DefaultFile(List<string> files)
        {
            var kept = SessionState.GetString(FileKey + _project, string.Empty);
            if (files.Contains(kept)) return kept;
            var project = _session.ProjectGitignorePath;
            if (project != null && files.Contains(project)) return project;
            return files.Contains(".gitignore") ? ".gitignore" : files[0];
        }

        void LoadFile(string repoPath)
        {
            var root = _session.Snapshot.RepoRoot;
            if (root == null) return;
            _filePath = repoPath;
            _fullPath = Path.GetFullPath(Path.Combine(root, repoPath));
            SessionState.SetString(FileKey + _project, repoPath);
            TakeDisk(GitignoreFile.Read(_fullPath));
        }

        void ClearFile()
        {
            _filePath = null;
            _fullPath = null;
            TakeDisk(null);
        }

        // The disk version becomes the text and the baseline.
        void TakeDisk(GitignoreFileText? disk)
        {
            _text = _baseline = disk?.Text ?? string.Empty;
            _stamp = disk;
            _hasBom = disk?.HasBom ?? false;
            _lineEnding = disk?.LineEnding ?? "\n";
            _isUtf8 = disk?.IsUtf8 ?? true;
            _diskVersion = null;
            _saveError = null;
            _pendingSave?.Pause();
            _undoStack.Clear(); // a reload, a file switch or another file read
            EraseDraft();
            NotifyUnsaved();
        }

        void SetText(string text)
        {
            _text = text;
            if (HasUnsavedChanges) StoreDraft();
            else EraseDraft();
            NotifyUnsaved();
            RenderState();
        }

        // ---- drafts ----

        void RestoreDraft()
        {
            var kept = SessionState.GetString(FileKey + _project, string.Empty);
            _filePath = kept.Length > 0 ? kept : null;
            var draftPath = SessionState.GetString(DraftPathKey + _project, string.Empty);
            if (draftPath.Length == 0 || _filePath == null) return;
            _fullPath = draftPath;
            _text = SessionState.GetString(DraftTextKey + _project, string.Empty);
            _baseline = SessionState.GetString(DraftBaseKey + _project, string.Empty);
            _stamp = null; // unknown: the next check compares the disk with the baseline
            var disk = GitignoreFile.Read(draftPath);
            if (disk != null)
            {
                _hasBom = disk.HasBom;
                _lineEnding = disk.LineEnding;
                _isUtf8 = disk.IsUtf8;
            }
        }

        void StoreDraft()
        {
            SessionState.SetString(DraftPathKey + _project, _fullPath ?? string.Empty);
            SessionState.SetString(DraftTextKey + _project, _text);
            SessionState.SetString(DraftBaseKey + _project, _baseline);
        }

        void EraseDraft()
        {
            SessionState.EraseString(DraftPathKey + _project);
            SessionState.EraseString(DraftTextKey + _project);
            SessionState.EraseString(DraftBaseKey + _project);
        }

        void NotifyUnsaved()
        {
            var now = (HasUnsavedChanges, AutoSave);
            if (_reported == now) return;
            _reported = now;
            UnsavedChanged?.Invoke();
        }

        // ---- showing ----

        void RenderState()
        {
            var choices = _fileList.ToList();
            if (_filePath != null && !choices.Contains(_filePath)) choices.Add(_filePath);
            _files.choices = choices;
            _files.SetValueWithoutNotify(_filePath ?? string.Empty);
            PanelParts.SetDisplayed(_files, choices.Count > 0);

            var mode = new ToggleButtonGroupState(0, 2);
            mode[(int)Mode] = true;
            _modeGroup.SetValueWithoutNotify(mode);
            _autoSaveToggle.SetValueWithoutNotify(AutoSave);
            PanelParts.SetDisplayed(_save, !AutoSave && HasUnsavedChanges);
            _save.SetEnabled(!IsReadOnly);

            string? note = null;
            var tone = EditorUiTone.Info;
            var create = false;
            var reload = false;
            if (_filePath == null)
            {
                if (_listLoaded && _fileList.Count == 0)
                {
                    note = GitignoreText.NoGitignore;
                    create = true;
                }
            }
            else if (IsConflicted) (note, tone) = (GitignoreText.Conflicted, EditorUiTone.Warning);
            else if (!_isUtf8) (note, tone) = (GitignoreText.NotUtf8, EditorUiTone.Warning);
            else if (_diskVersion != null)
            {
                (note, tone) = (GitignoreText.ChangedOnDisk, EditorUiTone.Warning);
                reload = true;
            }
            else if (_saveError != null) (note, tone) = (_saveError, EditorUiTone.Error);
            _noteText = note;
            PanelParts.SetDisplayed(_message, note != null);
            if (note != null) EditorUiElements.SetMessage(_message, note, tone);
            PanelParts.SetDisplayed(_createTemplate, create);
            PanelParts.SetDisplayed(_createEmpty, create);
            PanelParts.SetDisplayed(_reload, reload);

            _undo.SetEnabled(CanUndo);
            PanelParts.SetDisplayed(_body, _fullPath != null);
            var blocks = Mode == GitignoreMode.Blocks;
            PanelParts.SetDisplayed(BlocksView, blocks);
            PanelParts.SetDisplayed(PlainView, !blocks);
            var readOnly = IsReadOnly;
            // The text box follows every change (cheap, and its line numbers are read in either mode); the blocks are
            // rebuilt only while shown and only when the text changed, so a drag in progress survives other renders.
            if (_shownText != _text || _shownReadOnly != readOnly)
            {
                PlainView.Show(_text, readOnly);
                _shownText = _text;
                _shownReadOnly = readOnly;
            }
            if (blocks && (_blocksText != _text || _blocksReadOnly != readOnly))
            {
                BlocksView.Show(GitignoreDocument.Parse(_text), readOnly);
                _blocksText = _text;
                _blocksReadOnly = readOnly;
            }
        }
    }
}
