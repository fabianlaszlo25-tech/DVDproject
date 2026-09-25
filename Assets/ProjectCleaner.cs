using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ProjectCleaner : EditorWindow
{
    [MenuItem("Tools/Finalize: Trash Unused Meshes & Textures")]
    public static void CleanProject()
    {
        // 1. Safety Check
        if (!EditorUtility.DisplayDialog("Warning: Project Cleanup",
            "This will scan all enabled scenes in your Build Settings and move unreferenced Textures and Meshes to your OS Recycle Bin.\n\nMake sure your project is backed up before proceeding.",
            "Move to Trash", "Cancel"))
        {
            return;
        }

        // 2. Get all enabled scenes in Build Settings
        string[] scenePaths = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenePaths.Length == 0)
        {
            Debug.LogWarning("Cleanup Aborted: No enabled scenes found in Build Settings.");
            return;
        }

        // 3. Find every asset referenced by those scenes
        string[] allDependencies = AssetDatabase.GetDependencies(scenePaths, true);
        HashSet<string> usedAssets = new HashSet<string>(allDependencies);

        // Explicitly protect everything inside a Resources folder, as these are loaded dynamically
        string[] allProjectAssets = AssetDatabase.GetAllAssetPaths();
        foreach (string path in allProjectAssets)
        {
            if (path.Contains("/Resources/"))
            {
                usedAssets.Add(path);
            }
        }

        // 4. Find all Textures and Meshes in the project
        string[] searchGuids = AssetDatabase.FindAssets("t:Texture t:Mesh");
        int trashedCount = 0;

        try
        {
            for (int i = 0; i < searchGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(searchGuids[i]);

                EditorUtility.DisplayProgressBar("Scanning Assets", assetPath, (float)i / searchGuids.Length);

                // Ignore Unity packages and Editor tools
                if (assetPath.StartsWith("Packages/") || assetPath.Contains("/Editor/"))
                    continue;

                // 5. If it's not in the used list, trash it
                if (!usedAssets.Contains(assetPath))
                {
                    Debug.Log($"[CleanUp] Trashed Unused Asset: {assetPath}");
                    AssetDatabase.MoveAssetToTrash(assetPath);
                    trashedCount++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        Debug.Log($"Cleanup Complete! Moved {trashedCount} unused meshes and textures to the Recycle Bin.");
    }
}