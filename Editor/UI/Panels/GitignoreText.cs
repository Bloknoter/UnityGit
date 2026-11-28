#nullable enable

namespace UnityGit.UI
{
    /// <summary>The texts of the .gitignore tab and its Add rule window (spec "UnityGit: .gitignore editor tab").</summary>
    internal static class GitignoreText
    {
        public const string TabLabel = ".gitignore";

        public const string Blocks = "Blocks";
        public const string PlainText = "Plain text";
        public const string AddRule = "Add rule";
        public const string ByExtension = "By extension";
        public const string AllInFolder = "All in folder";
        public const string SpecificFile = "Specific file";
        public const string Custom = "Custom";
        public const string Undo = "Undo";
        public const string AutoSave = "Auto-save";
        public const string Save = "Save";

        public const string NoGitignore = "No .gitignore in this repository";
        public const string CreateTemplate = "Create with Unity template";
        public const string CreateEmpty = "Create empty";
        public const string Conflicted = "Resolve this file in the Conflicts tab first";
        public const string NotUtf8 = "This file isn't UTF-8 text, so it can't be edited here";
        public const string ChangedOnDisk = "This .gitignore changed on disk";
        public const string Reload = "Reload";
        public const string EmptyFile = "Empty file — add a rule, or type in Plain text";
        public const string Remove = "Remove";

        public static string SaveFailed(string path, string message) => $"Couldn't save {path}: {message}";

        public static string SwitchTitle(string path) => $"Save changes to {path}?";
        public const string SwitchMessage = "Your changes will be lost if you don't save them.";
        public const string SwitchSave = "Save";
        public const string SwitchCancel = "Cancel";
        public const string SwitchDontSave = "Don't save";

        /// <summary>Unity's close prompt for the window.</summary>
        public static string Unsaved(string path) => $"{path} has unsaved changes.";

        public static string WindowTitle(string item) => "Add rule — " + item;

        /// <summary>The Add rule menu item for a kind, which the window's title repeats.</summary>
        public static string KindName(GitignoreRuleKind kind)
        {
            switch (kind)
            {
                case GitignoreRuleKind.Extension: return ByExtension;
                case GitignoreRuleKind.Folder: return AllInFolder;
                case GitignoreRuleKind.File: return SpecificFile;
                default: return Custom;
            }
        }

        public const string Comment = "Comment (optional)";
        public const string Extension = "Extension";
        public const string Folder = "Folder";
        public const string File = "File";
        public const string Rule = "Rule";
        public const string Negate = "Negate";
        public const string AlsoMeta = "Also ignore .meta";
        public const string Browse = "Browse…";
        public const string Add = "Add";
        public const string Cancel = "Cancel";
        public const string NegateHint = "Git can't re-include a file whose parent folder is ignored.";
    }
}
