using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityGit.UI
{
    /// <summary>The real editor side effects around git actions (spec §5.6).</summary>
    public sealed class UnityEditorHooks : IEditorHooks
    {
        public void SaveAssets() => AssetDatabase.SaveAssets();

        public bool ConfirmSaveModifiedScenes()
        {
            // Checked first, so the user isn't asked about scenes for an action that stops anyway.
            if (PrefabStageUtility.GetCurrentPrefabStage()?.scene.isDirty == true)
            {
                EditorUtility.DisplayDialog("UnityGit", "Save or discard the prefab open in Prefab Mode first.", "OK");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            // "Don't Save" returns true but leaves the scenes dirty, and git must not rewrite a scene that is dirty in memory.
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return false;
            return true;
        }

        public void BeginGitOperation()
        {
            EditorApplication.LockReloadAssemblies();
            AssetDatabase.DisallowAutoRefresh();
        }

        public void EndGitOperation()
        {
            AssetDatabase.AllowAutoRefresh();
            EditorApplication.UnlockReloadAssemblies();
        }

        public void RefreshAssets() => AssetDatabase.Refresh();
    }
}
