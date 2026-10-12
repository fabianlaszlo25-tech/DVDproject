#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class PreloadCollector : EditorWindow
{
    [MenuItem("Tools/Auto-Collect Preload Materials")]
    public static void CollectMaterials()
    {
        // Find the Preloader script in the current open scene
        WebGLPreloader preloader = FindObjectOfType<WebGLPreloader>();
        if (preloader == null)
        {
            Debug.LogError("Could not find the WebGLPreloader script in this scene. Open your Loading Scene first!");
            return;
        }

        HashSet<Material> uniqueMaterials = new HashSet<Material>();

        // Scan every scene included in your Build Settings
        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (!buildScene.enabled) continue;

            Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);

            // Find every MeshRenderer in the scene
            MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>(true);
            foreach (MeshRenderer r in renderers)
            {
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat != null) uniqueMaterials.Add(mat);
                }
            }

            // Close the scene after scanning (unless it's the active loading scene)
            if (scene.name != SceneManager.GetActiveScene().name)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // Apply the collected materials to the Preloader script
        preloader.allGameMaterials = new List<Material>(uniqueMaterials);
        EditorUtility.SetDirty(preloader);

        Debug.Log($"Successfully found and linked {uniqueMaterials.Count} unique materials for pre-compilation!");
    }
}
#endif