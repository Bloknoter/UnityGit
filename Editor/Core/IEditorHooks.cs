namespace UnityGit
{
    public interface IEditorHooks
    {
        void SaveAssets();
        /// <summary>False means the user cancelled or declined, so the action is cancelled.</summary>
        bool ConfirmSaveModifiedScenes();
        void BeginGitOperation();
        void EndGitOperation();
        void RefreshAssets();
    }
}
