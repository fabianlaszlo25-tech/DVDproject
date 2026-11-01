using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Windows (Standalone) builds have Static Batching enabled while WebGL has it disabled, so statically-flagged
/// interactables (oven, beer/energy/zyn/weed props...) get merged into a non-readable "Combined Mesh" at build time.
/// That breaks InteractVisualizer (no outline/swirl) and moving/toggling the prop.
/// This runs on every scene while building (BEFORE Unity batches) and strips the Batching Static flag from
/// interactive objects only. It only touches the built scene copy - your scene assets are not modified.
/// </summary>
public class InteractableStaticBatchingFix : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        // Runs for play-mode too (report == null); harmless there but skip to avoid touching editor scenes.
        if (report == null) return;

        int fixedCount = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            fixedCount += Fix<InteractVisualizer>(root);
            fixedCount += Fix<InteractableObject>(root);
            fixedCount += Fix<SurvivalAction>(root);
            fixedCount += Fix<BuyableBoosterProp>(root);
            fixedCount += Fix<BoosterObject>(root);
            fixedCount += Fix<TimedConsumableBooster>(root);
            fixedCount += Fix<BreakablePermanentBooster>(root);
        }

        if (fixedCount > 0)
            Debug.Log($"[InteractableStaticBatchingFix] Cleared Batching Static on {fixedCount} objects in '{scene.name}'.");
    }

    private static int Fix<T>(GameObject root) where T : Component
    {
        int count = 0;
        foreach (T comp in root.GetComponentsInChildren<T>(true))
        {
            foreach (Transform tr in comp.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = tr.gameObject;
                if (go.name.StartsWith("Glow_")) continue;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & StaticEditorFlags.BatchingStatic) == 0) continue;
                GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.BatchingStatic);
                count++;
            }
        }
        return count;
    }
}
