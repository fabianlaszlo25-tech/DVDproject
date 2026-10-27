using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public static class BarMaterialInspector
{
    [MenuItem("Tools/Inspect Bar Materials")]
    public static void Inspect()
    {
        string fbxPath = "Assets/Barni/EverythingElse/BarModels/bar2.fbx";
        GameObject fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxRoot == null)
        {
            Debug.LogError($"Could not load FBX root at {fbxPath}");
            return;
        }

        Renderer[] fbxRenderers = fbxRoot.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"[BarMaterialInspector] FBX Prefab has {fbxRenderers.Length} Renderers.");

        int assignedCount = 0;
        int nullCount = 0;
        HashSet<string> uniqueMats = new HashSet<string>();

        foreach (Renderer r in fbxRenderers)
        {
            Material[] mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                nullCount++;
                continue;
            }

            foreach (Material m in mats)
            {
                if (m != null)
                {
                    assignedCount++;
                    string path = AssetDatabase.GetAssetPath(m);
                    uniqueMats.Add($"{m.name} ({m.shader.name}) at '{path}'");
                }
                else
                {
                    nullCount++;
                }
            }
        }

        Debug.Log($"[BarMaterialInspector] Result: {assignedCount} material slots filled, {nullCount} null slots. {uniqueMats.Count} unique materials:");
        foreach (string u in uniqueMats)
        {
            Debug.Log($"  * {u}");
        }
    }
}
