using UnityEditor;

namespace UnityGit.UI
{
    /// <summary>Refreshes the repo state after Unity imports, deletes or moves assets (debounced by the provider).</summary>
    internal sealed class AssetChangeWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            => SessionProvider.RequestRefresh();
    }
}
