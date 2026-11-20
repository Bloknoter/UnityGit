namespace UnityGit
{
    public interface IEditorHooks
    {
        void SaveAssets();
        /// <summary>False means the user cancelled or declined, so the action is cancelled.</summary>
        bool ConfirmSaveModifiedScenes();
        /// <summary>Asks the user to confirm discarding work. False means the user declined.</summary>
        bool Confirm(string title, string message, string ok);
        void BeginGitOperation();
        void EndGitOperation();
        void RefreshAssets();
    }
}
