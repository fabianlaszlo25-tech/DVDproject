using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Central manager for the FullScreenPass CRT post-processing effect.
/// Safely swaps between duringgameplay.mat and duringpaused.mat for:
/// - Pause Menu
/// - Death Sequence final statistics
/// - Dream Sequence summary
/// Automatically restores normal gameplay CRT on scene loads so it never gets stuck.
/// </summary>
public static class CrtPostProcessManager
{
    private static FullScreenPassRendererFeature cachedCrtFeature;
    private static Material duringGameplayMaterial;
    private static Material duringPausedMaterial;
    private static bool isInitialized = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        if (isInitialized) return;
        isInitialized = true;

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Always ensure normal gameplay CRT is active on scene load
        SetPausedCrtActive(false);
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        SetPausedCrtActive(false);
    }

    /// <summary>
    /// Explicitly registers the CRT materials and feature (called by PauseManager or auto-located).
    /// </summary>
    public static void RegisterMaterials(Material gameplayMat, Material pausedMat, FullScreenPassRendererFeature feature = null)
    {
        if (gameplayMat != null) duringGameplayMaterial = gameplayMat;
        if (pausedMat != null) duringPausedMaterial = pausedMat;
        if (feature != null) cachedCrtFeature = feature;
    }

    /// <summary>
    /// Activates or deactivates the duringpaused CRT effect (with scanlines, curvature, and vignette).
    /// </summary>
    public static void SetPausedCrtActive(bool active)
    {
        EnsureReferences();

        Material targetMat = active ? duringPausedMaterial : duringGameplayMaterial;
        if (targetMat == null || cachedCrtFeature == null) return;

        cachedCrtFeature.passMaterial = targetMat;
    }

    private static void EnsureReferences()
    {
        if (cachedCrtFeature == null)
        {
            FindCrtRendererFeature();
        }

        if (duringGameplayMaterial == null || duringPausedMaterial == null)
        {
            FindCrtMaterials();
        }
    }

    private static void FindCrtMaterials()
    {
        // 1. Try PauseManager if in current scene
        PauseManager pm = Object.FindObjectOfType<PauseManager>(true);
        if (pm != null)
        {
            if (duringGameplayMaterial == null) duringGameplayMaterial = pm.duringGameplayMaterial;
            if (duringPausedMaterial == null) duringPausedMaterial = pm.duringPausedMaterial;
            if (cachedCrtFeature == null && pm.crtRendererFeature != null) cachedCrtFeature = pm.crtRendererFeature;
        }

        // 2. Search loaded materials in memory
        if (duringGameplayMaterial == null || duringPausedMaterial == null)
        {
            Material[] allMats = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < allMats.Length; i++)
            {
                Material m = allMats[i];
                if (m == null) continue;
                string name = m.name.ToLowerInvariant();
                if (duringGameplayMaterial == null && name.Equals("duringgameplay"))
                {
                    duringGameplayMaterial = m;
                }
                else if (duringPausedMaterial == null && name.Equals("duringpaused"))
                {
                    duringPausedMaterial = m;
                }
            }
        }
    }

    private static void FindCrtRendererFeature()
    {
        RenderPipelineAsset pipeline = QualitySettings.renderPipeline != null
            ? QualitySettings.renderPipeline
            : GraphicsSettings.currentRenderPipeline;

        if (pipeline != null)
        {
            FieldInfo listField = pipeline.GetType().GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (listField != null && listField.GetValue(pipeline) is ScriptableRendererData[] dataList)
            {
                for (int i = 0; i < dataList.Length; i++)
                {
                    if (dataList[i] != null && TryFindCrtFeatureInRendererData(dataList[i], out FullScreenPassRendererFeature feature))
                    {
                        cachedCrtFeature = feature;
                        return;
                    }
                }
            }
        }

        // Fallback: search loaded features in memory
        FullScreenPassRendererFeature[] loadedFeatures = Resources.FindObjectsOfTypeAll<FullScreenPassRendererFeature>();
        for (int i = 0; i < loadedFeatures.Length; i++)
        {
            FullScreenPassRendererFeature f = loadedFeatures[i];
            if (f != null && IsMatchingCrtFeature(f))
            {
                cachedCrtFeature = f;
                return;
            }
        }
    }

    private static bool TryFindCrtFeatureInRendererData(ScriptableRendererData data, out FullScreenPassRendererFeature result)
    {
        result = null;
        if (data == null || data.rendererFeatures == null) return false;

        for (int i = 0; i < data.rendererFeatures.Count; i++)
        {
            if (data.rendererFeatures[i] is FullScreenPassRendererFeature f && IsMatchingCrtFeature(f))
            {
                result = f;
                return true;
            }
        }
        return false;
    }

    private static bool IsMatchingCrtFeature(FullScreenPassRendererFeature feature)
    {
        if (feature == null) return false;

        if (duringGameplayMaterial != null && feature.passMaterial == duringGameplayMaterial) return true;
        if (duringPausedMaterial != null && feature.passMaterial == duringPausedMaterial) return true;

        if (feature.passMaterial != null)
        {
            string matName = feature.passMaterial.name.ToLowerInvariant();
            if (matName.Contains("duringgameplay") || matName.Contains("duringpaused") || matName.Contains("crt"))
                return true;
        }

        string featName = feature.name.ToLowerInvariant();
        return featName.Contains("crt") || featName.Contains("fullscreenpassrendererfeatureoldguid");
    }
}
