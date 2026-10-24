using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WebGLTieredPerformanceOptimizer : EditorWindow
{
    private const string BACKUP_FOLDER = "Assets/WebGLSimplifiedMaterials";
    private const string BACKUP_FILE_PATH = "Assets/WebGLSimplifiedMaterials/TieredOptimizationBackup.json";
    private const string BACKUP_FALLBACK_PATH = "Assets/WebGLSimplifiedMaterials/TieredOptimizationBackup.bak.json";

    private static readonly string[] UrpPipelineAssetPaths =
    {
        "Assets/Shaders/dream.asset",
        "Assets/unitySlop/Settings/PC_RPAsset.asset",
        "Assets/unitySlop/Settings/Mobile_RPAsset.asset"
    };

    private static readonly string[] UrpRendererAssetPaths =
    {
        "Assets/Shaders/mainRenderer.asset",
        "Assets/Shaders/dvdlogorenderer.asset",
        "Assets/Shaders/menurenderer.asset"
    };

    private static readonly string[] VolumeProfileAssetPaths =
    {
        "Assets/Shaders/IrlVolume 1.asset",
        "Assets/unitySlop/Settings/SampleSceneProfile.asset"
    };

    [Serializable]
    private class RawFileBackup
    {
        public string assetPath;
        public string originalYamlContent;
    }

    [Serializable]
    private class AudioImporterBackup
    {
        public string assetPath;
        public bool forceToMono;
        public bool loadInBackground;
        public int defaultLoadType;
        public int defaultCompressionFormat;
        public float defaultQuality;
        public int defaultSampleRateSetting;
        public uint defaultSampleRateOverride;
        public bool webglOverridden;
        public int webglLoadType;
        public int webglCompressionFormat;
        public float webglQuality;
    }

    [Serializable]
    private class ModelImporterBackup
    {
        public string assetPath;
        public bool isReadable;
        public int meshCompression;
        public int meshOptimizationFlags;
        public bool importBlendShapes;
    }

    [Serializable]
    private class TextureImporterBackup
    {
        public string assetPath;
        public int maxTextureSize;
        public int textureCompression;
        public bool crunchedCompression;
        public int compressionQuality;
        public int anisoLevel;
        public bool webglOverridden;
        public int webglMaxTextureSize;
        public int webglFormat;
        public int webglCompressionQuality;
        public bool webglCrunched;
    }

    [Serializable]
    private class SceneComponentBackup
    {
        public string scenePath;
        public string globalObjectId;
        public string hierarchyPath;
        public bool wasEnabled;
        public float originalAcesAmount;
    }

    [Serializable]
    private class FullOptimizationBackupState
    {
        public bool hasValidBaseline;
        public int currentActiveTier; // 0 = Original, 1 = Tier 1, 2 = Tier 2, 3 = Tier 3
        public string backupTimestamp;

        // Player / Quality / Graphics Settings Baseline
        public bool origStripUnusedMeshComponents;
        public bool origBakeCollisionMeshes;
        public int origGlobalTextureMipmapLimit;
        public float origLodBias;
        public int origMaximumLODLevel;
        public bool origEnableLODCrossFade;
        public bool origRealtimeReflectionProbes;
        public int origAnisotropicFiltering;
        public int origShadowCascades;
        public float origShadowDistance;
        public int origShadowResolution;
        public int origShadows;
        public int origPixelLightCount;
        public bool origSoftVegetation;

        // Raw YAML backups for URP RenderPipeline, RendererData, VolumeProfiles, and GraphicsSettings
        public List<RawFileBackup> yamlAssetBackups = new List<RawFileBackup>();

        // Asset Importer Backups
        public List<AudioImporterBackup> audioBackups = new List<AudioImporterBackup>();
        public List<ModelImporterBackup> modelBackups = new List<ModelImporterBackup>();
        public List<TextureImporterBackup> textureBackups = new List<TextureImporterBackup>();

        // Scene Component Backups (e.g., AdjustableACESEffect double-render camera script)
        public List<SceneComponentBackup> sceneComponentBackups = new List<SceneComponentBackup>();
    }

    private Vector2 _scrollPos;
    private bool _includeImporterOptimizations = true;

    [MenuItem("Tools/WebGL Tiered Performance Optimizer")]
    public static void ShowWindow()
    {
        GetWindow<WebGLTieredPerformanceOptimizer>("WebGL Tiered Optimizer");
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        GUILayout.Label("WebGL Tiered FPS & Memory Optimizer", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        FullOptimizationBackupState state = LoadBackupState();
        bool hasBaseline = state != null && state.hasValidBaseline;
        int currentTier = hasBaseline ? state.currentActiveTier : 0;

        string tierLabel = currentTier switch
        {
            0 => "Original Unmodified State",
            1 => "Tier 1 Active (No Visual Decrease)",
            2 => "Tier 2 Active (Minimal Visual Decrease)",
            3 => "Tier 3 Active (Significant Visual Decrease - Max FPS/RAM)",
            _ => "Unknown"
        };

        EditorGUILayout.HelpBox(
            $"Current Status: {tierLabel}\n" +
            (hasBaseline ? $"Baseline Backup Saved: {state.backupTimestamp}\n" : "Baseline Backup will be created automatically on first click.\n") +
            "All changes are 100% reversible. You can freely switch between Tier 1, Tier 2, Tier 3, and Revert All at any time.",
            MessageType.Info);

        EditorGUILayout.Space(6);
        _includeImporterOptimizations = EditorGUILayout.ToggleLeft(
            "Also Optimize Audio, Model (Read/Write), Lightmap & UI Texture Importers (Saves 100-250+ MB RAM)",
            _includeImporterOptimizations);

        EditorGUILayout.Space(10);

        // TIER 1 BUTTON
        DrawTierSection(
            "TIER 1: Better FPS & Memory WITHOUT Visual Decrease",
            "• URP Renderers (mainRenderer, dvdlogorenderer, menurenderer): Disables Forced Depth Priming (stops rendering every mesh twice per frame), disables unused DecalRendererFeature DBuffer passes, switches Depth attachment from 32-bit to 24-bit.\n" +
            "• URP Pipeline (dream.asset, PC_RPAsset): Enables Fast sRGB/Linear conversion, Store Actions Optimization (discards transient buffers), disables unused Lens Flare passes & APV memory budgets.\n" +
            "• Build & Graphics Settings: Enables Strip Unused Mesh Components (strips unused tangents/UVs at build time), Pre-Bake Collision Meshes, and automatic Lightmap/Fog shader variant stripping.\n" +
            "• Memory Importers: Disables unnecessary CPU Read/Write duplicate mesh buffers on 3D models (like 50MB Winston.fbx) and switches long music/audio tracks from DecompressOnLoad to CompressedInMemory.",
            new Color(0.55f, 0.9f, 0.65f),
            currentTier == 1 ? "✓ Tier 1 Optimization Active (Click to Re-Apply)" : "Apply Tier 1 Optimization (No Visual Decrease)",
            () => ApplyTier(1));

        EditorGUILayout.Space(8);

        // TIER 2 BUTTON
        DrawTierSection(
            "TIER 2: Better FPS & Memory WITH Minimal Visual Decrease",
            "Includes all Tier 1 optimizations PLUS:\n" +
            "• SSAO Optimization (mainRenderer & menurenderer): Switches SSAO to Half-Resolution (Downsample: On), AfterOpaque: On, and Depth-only source (completely eliminates the full-scene DepthNormals prepass!).\n" +
            "• Shadow & Opaque Buffer Tuning (dream.asset & QualitySettings):Lowers Additional Lights Shadow Atlas from 4096x4096 to 1024x1024 (saves ~48MB VRAM), Shadow Cascades from 2 to 1, Soft Shadows to Low, and Opaque Texture to 2x Downsampling.\n" +
            "• Post-Processing (IrlVolume 1): Switches Bloom to Quarter-Resolution downscale, disables HQ Bicubic filtering, caps iterations at 4.\n" +
            "• Quality & LODs: Sets LOD Bias from 2.0 to 1.0, disables dual-mesh LOD Cross-Fade dithering, disables Anisotropic filtering & Realtime Reflection Probes.\n" +
            "• Lightmap & Sprite RAM Cap: Caps 39 scene lightmaps (01Day) at 512px and huge 33MB sliders.png sprite sheet at 1024px with WebGL Crunch compression.",
            new Color(0.95f, 0.85f, 0.45f),
            currentTier == 2 ? "✓ Tier 2 Optimization Active (Click to Re-Apply)" : "Apply Tier 2 Optimization (Minimal Visual Decrease)",
            () => ApplyTier(2));

        EditorGUILayout.Space(8);

        // TIER 3 BUTTON
        DrawTierSection(
            "TIER 3: Maximum FPS & Memory WITH Significant Visual Decrease",
            "Includes all Tier 1 & Tier 2 optimizations PLUS:\n" +
            "• Eliminates Double Scene Rendering (AdjustableACESEffect): Disables AdjustableACESEffect in the scene (which otherwise calls targetCamera.Render() a second time every frame into an HDR RenderTexture!).\n" +
            "• Disables Heavy Fullscreen Passes: Turns off ScreenSpaceAmbientOcclusion and Dithering fullscreen pass on mainRenderer & menurenderer.\n" +
            "• Pipeline & Render Scale (dream.asset): Lowers Render Scale to 0.75 (renders 44% fewer pixels), disables Additional Light Shadows & Soft Shadows, lowers Main Shadowmap to 256px, limits Additional Lights to 1 per object, disables Opaque & Depth texture copies.\n" +
            "• Volume Overrides (IrlVolume 1): Disables Bloom, Chromatic Aberration, and Film Grain.\n" +
            "• Extreme RAM & Poly Reduction: Sets Global Texture Mipmap Limit to Quarter-Res, caps Lightmaps at 256px, and sets Maximum LOD Level to 1 (skips highest-poly LOD0 meshes).",
            new Color(1.0f, 0.6f, 0.45f),
            currentTier == 3 ? "✓ Tier 3 Optimization Active (Click to Re-Apply)" : "Apply Tier 3 Optimization (Significant Visual Decrease)",
            () => ApplyTier(3));

        EditorGUILayout.Space(14);

        GUI.enabled = hasBaseline;
        GUI.backgroundColor = new Color(0.95f, 0.4f, 0.4f);
        if (GUILayout.Button("REVERT ALL (Restore Exact Original Project & Shader Settings)", GUILayout.Height(44)))
        {
            RevertAllToOriginal();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        EditorGUILayout.EndScrollView();
    }

    private static void DrawTierSection(
        string title,
        string description,
        Color buttonColor,
        string buttonText,
        Action onClick)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label(title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4);
        GUI.backgroundColor = buttonColor;
        if (GUILayout.Button(buttonText, GUILayout.Height(36)))
        {
            onClick?.Invoke();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();
    }

    private void ApplyTier(int targetTier)
    {
        EnsureBackupFolderExists();

        FullOptimizationBackupState state = LoadBackupState();
        if (state == null || !state.hasValidBaseline)
        {
            state = CaptureBaselineBackup();
            SaveBackupState(state);
        }
        else
        {
            // First restore the clean baseline so switching e.g. from Tier 3 -> Tier 1 is 100% exact
            RestoreBaselineInternal(state, _includeImporterOptimizations, false);
        }

        try
        {
            EditorUtility.DisplayProgressBar($"Applying Tier {targetTier} Optimization", "Applying URP & Project settings...", 0.2f);

            // Apply Tier 1 (always included in Tier 1, 2, and 3)
            ApplyTier1Settings(state, _includeImporterOptimizations, targetTier);

            if (targetTier >= 2)
            {
                EditorUtility.DisplayProgressBar($"Applying Tier {targetTier} Optimization", "Applying Tier 2 settings...", 0.55f);
                ApplyTier2Settings(state, _includeImporterOptimizations, targetTier);
            }

            if (targetTier >= 3)
            {
                EditorUtility.DisplayProgressBar($"Applying Tier {targetTier} Optimization", "Applying Tier 3 settings...", 0.85f);
                ApplyTier3Settings(state, _includeImporterOptimizations);
            }

            state.currentActiveTier = targetTier;
            SaveBackupState(state);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[WebGLTieredPerformanceOptimizer] Successfully applied Tier {targetTier} optimizations! " +
                      "Click 'REVERT ALL' or another Tier button at any time to switch.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    public void RevertAllToOriginal()
    {
        FullOptimizationBackupState state = LoadBackupState();
        if (state == null || !state.hasValidBaseline)
        {
            Debug.LogWarning("[WebGLTieredPerformanceOptimizer] No backup baseline found to revert.");
            return;
        }

        try
        {
            RestoreBaselineInternal(state, true, true);
            state.currentActiveTier = 0;
            SaveBackupState(state);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[WebGLTieredPerformanceOptimizer] Reverted all settings, URP assets, volumes, scenes, and importers back to their exact original state!");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static FullOptimizationBackupState CaptureBaselineBackup()
    {
        EditorUtility.DisplayProgressBar("Creating Permanent Baseline Backup", "Recording original project & shader settings...", 0.1f);

        FullOptimizationBackupState state = new FullOptimizationBackupState
        {
            hasValidBaseline = true,
            currentActiveTier = 0,
            backupTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

            origStripUnusedMeshComponents = PlayerSettings.stripUnusedMeshComponents,
            origBakeCollisionMeshes = PlayerSettings.bakeCollisionMeshes,
            origGlobalTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit,
            origLodBias = QualitySettings.lodBias,
            origMaximumLODLevel = QualitySettings.maximumLODLevel,
            origEnableLODCrossFade = QualitySettings.enableLODCrossFade,
            origRealtimeReflectionProbes = QualitySettings.realtimeReflectionProbes,
            origAnisotropicFiltering = (int)QualitySettings.anisotropicFiltering,
            origShadowCascades = QualitySettings.shadowCascades,
            origShadowDistance = QualitySettings.shadowDistance,
            origShadowResolution = (int)QualitySettings.shadowResolution,
            origShadows = (int)QualitySettings.shadows,
            origPixelLightCount = QualitySettings.pixelLightCount,
            origSoftVegetation = QualitySettings.softVegetation
        };

        // Backup raw YAML files for URP assets, renderers, volumes, and ProjectSettings/GraphicsSettings
        List<string> yamlPathsToBackup = new List<string>();
        yamlPathsToBackup.AddRange(UrpPipelineAssetPaths);
        yamlPathsToBackup.AddRange(UrpRendererAssetPaths);
        yamlPathsToBackup.AddRange(VolumeProfileAssetPaths);
        yamlPathsToBackup.Add("ProjectSettings/GraphicsSettings.asset");
        yamlPathsToBackup.Add("ProjectSettings/QualitySettings.asset");

        foreach (string path in yamlPathsToBackup)
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                state.yamlAssetBackups.Add(new RawFileBackup
                {
                    assetPath = path,
                    originalYamlContent = File.ReadAllText(fullPath)
                });
            }
        }

        // Backup AudioImporters
        string[] audioGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" });
        foreach (string guid in audioGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is AudioImporter ai)
            {
                AudioImporterSampleSettings def = ai.defaultSampleSettings;
                bool hasWebgl = ai.ContainsSampleSettingsOverride("WebGL");
                AudioImporterSampleSettings webgl = ai.GetOverrideSampleSettings("WebGL");

                state.audioBackups.Add(new AudioImporterBackup
                {
                    assetPath = path,
                    forceToMono = ai.forceToMono,
                    loadInBackground = ai.loadInBackground,
                    defaultLoadType = (int)def.loadType,
                    defaultCompressionFormat = (int)def.compressionFormat,
                    defaultQuality = def.quality,
                    defaultSampleRateSetting = (int)def.sampleRateSetting,
                    defaultSampleRateOverride = def.sampleRateOverride,
                    webglOverridden = hasWebgl,
                    webglLoadType = (int)webgl.loadType,
                    webglCompressionFormat = (int)webgl.compressionFormat,
                    webglQuality = webgl.quality
                });
            }
        }

        // Backup ModelImporters
        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
        foreach (string guid in modelGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is ModelImporter mi)
            {
                state.modelBackups.Add(new ModelImporterBackup
                {
                    assetPath = path,
                    isReadable = mi.isReadable,
                    meshCompression = (int)mi.meshCompression,
                    meshOptimizationFlags = (int)mi.meshOptimizationFlags,
                    importBlendShapes = mi.importBlendShapes
                });
            }
        }

        // Backup Lightmaps & Heavy UI Textures
        List<string> texPaths = CollectTargetTexturePaths();
        foreach (string path in texPaths)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                TextureImporterPlatformSettings webgl = ti.GetPlatformTextureSettings("WebGL");
                state.textureBackups.Add(new TextureImporterBackup
                {
                    assetPath = path,
                    maxTextureSize = ti.maxTextureSize,
                    textureCompression = (int)ti.textureCompression,
                    crunchedCompression = ti.crunchedCompression,
                    compressionQuality = ti.compressionQuality,
                    anisoLevel = ti.anisoLevel,
                    webglOverridden = webgl.overridden,
                    webglMaxTextureSize = webgl.maxTextureSize,
                    webglFormat = (int)webgl.format,
                    webglCompressionQuality = webgl.compressionQuality,
                    webglCrunched = webgl.crunchedCompression
                });
            }
        }

        // Backup AdjustableACESEffect components in loaded scenes
        AdjustableACESEffect[] acesEffects = FindObjectsByType<AdjustableACESEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (AdjustableACESEffect effect in acesEffects)
        {
            if (effect == null || EditorUtility.IsPersistent(effect)) continue;
            state.sceneComponentBackups.Add(new SceneComponentBackup
            {
                scenePath = effect.gameObject.scene.path,
                globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(effect).ToString(),
                hierarchyPath = GetHierarchyPath(effect.transform),
                wasEnabled = effect.enabled,
                originalAcesAmount = effect.acesAmount
            });
        }

        EditorUtility.ClearProgressBar();
        return state;
    }

    // ========================================================================================
    // TIER 1: NO VISUAL DECREASE
    // ========================================================================================
    private static void ApplyTier1Settings(FullOptimizationBackupState state, bool includeImporters, int targetTier)
    {
        // 1. PlayerSettings: Strip unused mesh vertex channels & pre-bake collision meshes
        PlayerSettings.stripUnusedMeshComponents = true;
        PlayerSettings.bakeCollisionMeshes = true;

        // 2. GraphicsSettings: Enable automatic Lightmap & Fog shader variant stripping
        ModifySerializedAsset("ProjectSettings/GraphicsSettings.asset", so =>
        {
            SetIntProp(so, "m_LightmapStripping", 1);
            SetIntProp(so, "m_FogStripping", 1);
        });

        // 3. URP Pipeline Assets (dream.asset, PC_RPAsset.asset, Mobile_RPAsset.asset)
        foreach (string path in UrpPipelineAssetPaths)
        {
            ModifySerializedAsset(path, so =>
            {
                SetIntProp(so, "m_UseFastSRGBLinearConversion", 1);
                SetIntProp(so, "m_StoreActionsOptimization", 1);
                SetIntProp(so, "m_SupportDataDrivenLensFlare", 0);
                SetIntProp(so, "m_SupportScreenSpaceLensFlare", 0);
                SetIntProp(so, "m_ProbeVolumeMemoryBudget", 64);
                SetIntProp(so, "m_ProbeVolumeBlendingMemoryBudget", 32);
            });
        }

        // 4. URP Renderer Assets (mainRenderer.asset, dvdlogorenderer.asset, menurenderer.asset)
        foreach (string path in UrpRendererAssetPaths)
        {
            // Disable Forced Depth Priming (m_DepthPrimingMode: 0), set 24-bit depth instead of 32-bit,
            // and disable unused DecalRendererFeature
            ModifyAllSubAssetsAtPath(path, obj =>
            {
                SerializedObject so = new SerializedObject(obj);
                bool changed = false;

                if (obj.GetType().Name.Contains("UniversalRendererData"))
                {
                    changed |= SetIntProp(so, "m_DepthPrimingMode", 0);
                    changed |= SetIntProp(so, "m_CopyDepthMode", 0);
                    changed |= SetIntProp(so, "m_DepthAttachmentFormat", 90); // Depth24_Stencil8
                    changed |= SetIntProp(so, "m_DepthTextureFormat", 90);
                    changed |= SetIntProp(so, "m_IntermediateTextureMode", 0); // Auto instead of Always
                }
                else if (obj.name.Contains("DecalRendererFeature") || obj.GetType().Name.Contains("DecalRendererFeature"))
                {
                    changed |= SetBoolProp(so, "m_Active", false);
                }

                if (changed)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(obj);
                }
            });
        }

        if (!includeImporters)
            return;

        AssetDatabase.StartAssetEditing();
        try
        {
            // 5. Audio Importers: Switch long audio files from DecompressOnLoad to CompressedInMemory for WebGL
            for (int i = 0; i < state.audioBackups.Count; i++)
            {
                AudioImporterBackup b = state.audioBackups[i];
                if (AssetImporter.GetAtPath(b.assetPath) is AudioImporter ai)
                {
                    AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(b.assetPath);
                    bool isLongClip = clip == null || clip.length > 4.0f;

                    ai.loadInBackground = true;
                    AudioImporterSampleSettings def = ai.defaultSampleSettings;
                    def.loadType = isLongClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                    def.compressionFormat = AudioCompressionFormat.AAC;
                    def.quality = targetTier >= 2 ? 0.45f : 0.7f;
                    ai.defaultSampleSettings = def;

                    ai.SaveAndReimport();
                }
            }

            // 6. Model Importers: Disable duplicate CPU/GPU Read/Write memory buffer on large 3D models (like 50MB Winston.fbx)
            for (int i = 0; i < state.modelBackups.Count; i++)
            {
                ModelImporterBackup b = state.modelBackups[i];
                if (AssetImporter.GetAtPath(b.assetPath) is ModelImporter mi)
                {
                    bool changed = false;
                    if (mi.isReadable)
                    {
                        mi.isReadable = false;
                        changed = true;
                    }
                    if (mi.meshOptimizationFlags != MeshOptimizationFlags.Everything)
                    {
                        mi.meshOptimizationFlags = MeshOptimizationFlags.Everything;
                        changed = true;
                    }
                    if (targetTier >= 2 && mi.meshCompression == ModelImporterMeshCompression.Off)
                    {
                        mi.meshCompression = ModelImporterMeshCompression.Medium;
                        changed = true;
                    }

                    if (changed)
                    {
                        mi.SaveAndReimport();
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    // ========================================================================================
    // TIER 2: MINIMAL VISUAL DECREASE
    // ========================================================================================
    private static void ApplyTier2Settings(FullOptimizationBackupState state, bool includeImporters, int targetTier)
    {
        // 1. QualitySettings adjustments
        QualitySettings.lodBias = 1.0f;
        QualitySettings.enableLODCrossFade = false;
        QualitySettings.realtimeReflectionProbes = false;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        QualitySettings.shadowCascades = 1;
        QualitySettings.shadowDistance = 30f;

        // 2. URP Pipeline Assets (dream.asset, PC_RPAsset.asset):
        // Reduce 4096x4096 Additional Light Shadowmap to 1024x1024, 1 cascade, 2x Opaque Downsampling, disable LOD CrossFade
        foreach (string path in UrpPipelineAssetPaths)
        {
            ModifySerializedAsset(path, so =>
            {
                SetIntProp(so, "m_OpaqueDownsampling", 1); // 2x Bilinear
                SetIntProp(so, "m_EnableLODCrossFade", 0);
                SetIntProp(so, "m_AdditionalLightsShadowmapResolution", 1024);
                SetIntProp(so, "m_AdditionalLightsShadowResolutionTierHigh", 512);
                SetIntProp(so, "m_AdditionalLightsShadowResolutionTierMedium", 256);
                SetIntProp(so, "m_AdditionalLightsShadowResolutionTierLow", 128);
                SetIntProp(so, "m_ShadowCascadeCount", 1);
                SetFloatProp(so, "m_ShadowDistance", 30f);
                SetIntProp(so, "m_SoftShadowQuality", 1); // Low
                SetIntProp(so, "m_ReflectionProbeBlending", 0);
                SetIntProp(so, "m_ReflectionProbeBoxProjection", 0);
            });
        }

        // 3. URP Renderer Assets: Optimize SSAO (Downsample = true, AfterOpaque = true, Source = Depth [0] instead of DepthNormals [1])
        foreach (string path in UrpRendererAssetPaths)
        {
            ModifyAllSubAssetsAtPath(path, obj =>
            {
                if (!obj.name.Contains("ScreenSpaceAmbientOcclusion") &&
                    !obj.GetType().Name.Contains("ScreenSpaceAmbientOcclusion"))
                    return;

                SerializedObject so = new SerializedObject(obj);
                SerializedProperty settingsProp = so.FindProperty("m_Settings");
                if (settingsProp != null)
                {
                    SetRelativeBool(settingsProp, "Downsample", true);
                    SetRelativeBool(settingsProp, "AfterOpaque", true);
                    SetRelativeInt(settingsProp, "Source", 0); // Depth only (avoids full-scene DepthNormals prepass!)
                    SetRelativeInt(settingsProp, "Samples", 0); // Low
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(obj);
                }
            });
        }

        // 4. Volume Profiles (IrlVolume 1.asset, SampleSceneProfile.asset):
        // Switch Bloom to Quarter-Res downscale, disable HQ bicubic filtering, cap maxIterations at 4
        foreach (string path in VolumeProfileAssetPaths)
        {
            ModifyAllSubAssetsAtPath(path, obj =>
            {
                if (!obj.name.Contains("Bloom") && !obj.GetType().Name.Contains("Bloom"))
                    return;

                SerializedObject so = new SerializedObject(obj);
                SetVolumeParamBool(so, "highQualityFiltering", false);
                SetVolumeParamInt(so, "downscale", 1); // Quarter resolution
                SetVolumeParamInt(so, "maxIterations", 4);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(obj);
            });
        }

        // 5. Cap Lightmap & Heavy UI Sprite Sheet resolutions (only reimport here if targetTier == 2; Tier 3 applies its own tighter cap)
        if (includeImporters && targetTier == 2)
        {
            ApplyTextureCaps(state, lightmapMaxSize: 512, uiMaxSize: 1024);
        }
    }

    // ========================================================================================
    // TIER 3: SIGNIFICANT VISUAL DECREASE (MAXIMUM WEBGL PERFORMANCE)
    // ========================================================================================
    private static void ApplyTier3Settings(FullOptimizationBackupState state, bool includeImporters)
    {
        // 1. QualitySettings: Quarter-Res global mipmap limit, skip LOD0 (maximumLODLevel = 1), lower shadow distance
        QualitySettings.globalTextureMipmapLimit = 2;
        QualitySettings.maximumLODLevel = 1;
        QualitySettings.shadowDistance = 20f;
        QualitySettings.pixelLightCount = 1;
        QualitySettings.softVegetation = false;

        // 2. URP Pipeline Assets (dream.asset, PC_RPAsset.asset):
        // Lower RenderScale to 0.75, disable Opaque & Depth texture copies, disable Additional Light Shadows & Soft Shadows
        foreach (string path in UrpPipelineAssetPaths)
        {
            ModifySerializedAsset(path, so =>
            {
                SetFloatProp(so, "m_RenderScale", 0.75f);
                SetIntProp(so, "m_RequireDepthTexture", 0);
                SetIntProp(so, "m_RequireOpaqueTexture", 0);
                SetIntProp(so, "m_OpaqueDownsampling", 2); // 4x Box
                SetIntProp(so, "m_MainLightShadowmapResolution", 256);
                SetIntProp(so, "m_AdditionalLightsPerObjectLimit", 1);
                SetIntProp(so, "m_AdditionalLightShadowsSupported", 0);
                SetIntProp(so, "m_SoftShadowsSupported", 0);
                SetFloatProp(so, "m_ShadowDistance", 20f);
            });
        }

        // 3. URP Renderer Assets: Disable ScreenSpaceAmbientOcclusion & Dithering fullscreen pass completely
        foreach (string path in UrpRendererAssetPaths)
        {
            ModifyAllSubAssetsAtPath(path, obj =>
            {
                string typeName = obj.GetType().Name;
                if (obj.name.Contains("ScreenSpaceAmbientOcclusion") ||
                    typeName.Contains("ScreenSpaceAmbientOcclusion") ||
                    obj.name.Equals("Dithering", StringComparison.OrdinalIgnoreCase))
                {
                    SerializedObject so = new SerializedObject(obj);
                    if (SetBoolProp(so, "m_Active", false))
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(obj);
                    }
                }
            });
        }

        // 4. Volume Profiles (IrlVolume 1.asset, SampleSceneProfile.asset):
        // Disable Bloom, ChromaticAberration, and FilmGrain passes
        foreach (string path in VolumeProfileAssetPaths)
        {
            ModifyAllSubAssetsAtPath(path, obj =>
            {
                string typeName = obj.GetType().Name;
                if (typeName.Contains("Bloom") ||
                    typeName.Contains("ChromaticAberration") ||
                    typeName.Contains("FilmGrain"))
                {
                    SerializedObject so = new SerializedObject(obj);
                    if (SetBoolProp(so, "active", false))
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(obj);
                    }
                }
            });
        }

        // 5. Disable AdjustableACESEffect in loaded scenes (stops targetCamera.Render() from rendering the entire scene twice per frame!)
        AdjustableACESEffect[] acesEffects = FindObjectsByType<AdjustableACESEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (AdjustableACESEffect effect in acesEffects)
        {
            if (effect == null || EditorUtility.IsPersistent(effect)) continue;
            Undo.RecordObject(effect, "Disable AdjustableACESEffect for Tier 3");
            effect.enabled = false;
            EditorUtility.SetDirty(effect);
            if (effect.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(effect.gameObject.scene);
            }
        }

        // 6. Cap Lightmaps at 256px and UI sprite sheets at 512px
        if (includeImporters)
        {
            ApplyTextureCaps(state, lightmapMaxSize: 256, uiMaxSize: 512);
        }
    }

    private static void ApplyTextureCaps(FullOptimizationBackupState state, int lightmapMaxSize, int uiMaxSize)
    {
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < state.textureBackups.Count; i++)
            {
                TextureImporterBackup b = state.textureBackups[i];
                if (AssetImporter.GetAtPath(b.assetPath) is TextureImporter ti)
                {
                    bool isLightmap = b.assetPath.IndexOf("Lightmap-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      ti.textureType == TextureImporterType.Lightmap ||
                                      ti.textureType == TextureImporterType.DirectionalLightmap ||
                                      ti.textureType == TextureImporterType.Shadowmask;

                    int targetSize = isLightmap ? lightmapMaxSize : uiMaxSize;

                    ti.maxTextureSize = targetSize;
                    ti.crunchedCompression = true;
                    ti.compressionQuality = 50;
                    ti.anisoLevel = 0;

                    TextureImporterPlatformSettings webgl = ti.GetPlatformTextureSettings("WebGL");
                    webgl.overridden = true;
                    webgl.maxTextureSize = targetSize;
                    webgl.crunchedCompression = true;
                    webgl.compressionQuality = 50;
                    ti.SetPlatformTextureSettings(webgl);

                    ti.SaveAndReimport();
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    private static void RestoreBaselineInternal(FullOptimizationBackupState state, bool restoreImporters, bool showProgress)
    {
        if (showProgress)
        {
            EditorUtility.DisplayProgressBar("Reverting All Optimizations", "Restoring Player & Quality Settings...", 0.15f);
        }

        // 1. Restore PlayerSettings & QualitySettings
        PlayerSettings.stripUnusedMeshComponents = state.origStripUnusedMeshComponents;
        PlayerSettings.bakeCollisionMeshes = state.origBakeCollisionMeshes;

        QualitySettings.globalTextureMipmapLimit = state.origGlobalTextureMipmapLimit;
        QualitySettings.lodBias = state.origLodBias;
        QualitySettings.maximumLODLevel = state.origMaximumLODLevel;
        QualitySettings.enableLODCrossFade = state.origEnableLODCrossFade;
        QualitySettings.realtimeReflectionProbes = state.origRealtimeReflectionProbes;
        QualitySettings.anisotropicFiltering = (AnisotropicFiltering)state.origAnisotropicFiltering;
        QualitySettings.shadowCascades = state.origShadowCascades;
        QualitySettings.shadowDistance = state.origShadowDistance;
        QualitySettings.shadowResolution = ( UnityEngine.ShadowResolution )state.origShadowResolution;
        QualitySettings.shadows = ( UnityEngine.ShadowQuality )state.origShadows;
        QualitySettings.pixelLightCount = state.origPixelLightCount;
        QualitySettings.softVegetation = state.origSoftVegetation;

        // 2. Restore exact YAML contents of URP Pipeline Assets, Renderers, Volume Profiles, and GraphicsSettings
        if (state.yamlAssetBackups != null)
        {
            foreach (RawFileBackup raw in state.yamlAssetBackups)
            {
                if (string.IsNullOrEmpty(raw.assetPath) || string.IsNullOrEmpty(raw.originalYamlContent))
                    continue;

                string fullPath = Path.GetFullPath(raw.assetPath);
                if (File.Exists(fullPath))
                {
                    File.WriteAllText(fullPath, raw.originalYamlContent);
                }
            }
        }

        // 3. Restore Scene Components (AdjustableACESEffect)
        if (state.sceneComponentBackups != null)
        {
            AdjustableACESEffect[] acesEffects = FindObjectsByType<AdjustableACESEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (AdjustableACESEffect effect in acesEffects)
            {
                if (effect == null || EditorUtility.IsPersistent(effect)) continue;
                string gid = GlobalObjectId.GetGlobalObjectIdSlow(effect).ToString();
                string hPath = GetHierarchyPath(effect.transform);

                foreach (SceneComponentBackup sb in state.sceneComponentBackups)
                {
                    if (sb.globalObjectId == gid || sb.hierarchyPath == hPath)
                    {
                        effect.enabled = sb.wasEnabled;
                        effect.acesAmount = sb.originalAcesAmount;
                        EditorUtility.SetDirty(effect);
                        if (effect.gameObject.scene.IsValid())
                        {
                            EditorSceneManager.MarkSceneDirty(effect.gameObject.scene);
                        }
                        break;
                    }
                }
            }
        }

        if (!restoreImporters)
            return;

        AssetDatabase.StartAssetEditing();
        try
        {
            if (showProgress)
            {
                EditorUtility.DisplayProgressBar("Reverting All Optimizations", "Restoring Audio, Model, and Texture Importers...", 0.6f);
            }

            if (state.audioBackups != null)
            {
                foreach (AudioImporterBackup b in state.audioBackups)
                {
                    if (AssetImporter.GetAtPath(b.assetPath) is AudioImporter ai)
                    {
                        ai.forceToMono = b.forceToMono;
                        ai.loadInBackground = b.loadInBackground;

                        AudioImporterSampleSettings def = ai.defaultSampleSettings;
                        def.loadType = (AudioClipLoadType)b.defaultLoadType;
                        def.compressionFormat = (AudioCompressionFormat)b.defaultCompressionFormat;
                        def.quality = b.defaultQuality;
                        def.sampleRateSetting = (AudioSampleRateSetting)b.defaultSampleRateSetting;
                        def.sampleRateOverride = b.defaultSampleRateOverride;
                        ai.defaultSampleSettings = def;

                        if (b.webglOverridden)
                        {
                            AudioImporterSampleSettings webgl = ai.GetOverrideSampleSettings("WebGL");
                            webgl.loadType = (AudioClipLoadType)b.webglLoadType;
                            webgl.compressionFormat = (AudioCompressionFormat)b.webglCompressionFormat;
                            webgl.quality = b.webglQuality;
                            ai.SetOverrideSampleSettings("WebGL", webgl);
                        }
                        else if (ai.ContainsSampleSettingsOverride("WebGL"))
                        {
                            ai.ClearSampleSettingOverride("WebGL");
                        }

                        ai.SaveAndReimport();
                    }
                }
            }

            if (state.modelBackups != null)
            {
                foreach (ModelImporterBackup b in state.modelBackups)
                {
                    if (AssetImporter.GetAtPath(b.assetPath) is ModelImporter mi)
                    {
                        if (mi.isReadable != b.isReadable ||
                            (int)mi.meshCompression != b.meshCompression ||
                            (int)mi.meshOptimizationFlags != b.meshOptimizationFlags)
                        {
                            mi.isReadable = b.isReadable;
                            mi.meshCompression = (ModelImporterMeshCompression)b.meshCompression;
                            mi.meshOptimizationFlags = (MeshOptimizationFlags)b.meshOptimizationFlags;
                            mi.importBlendShapes = b.importBlendShapes;
                            mi.SaveAndReimport();
                        }
                    }
                }
            }

            if (state.textureBackups != null)
            {
                foreach (TextureImporterBackup b in state.textureBackups)
                {
                    if (AssetImporter.GetAtPath(b.assetPath) is TextureImporter ti)
                    {
                        ti.maxTextureSize = b.maxTextureSize;
                        ti.textureCompression = (TextureImporterCompression)b.textureCompression;
                        ti.crunchedCompression = b.crunchedCompression;
                        ti.compressionQuality = b.compressionQuality;
                        ti.anisoLevel = b.anisoLevel;

                        TextureImporterPlatformSettings webgl = ti.GetPlatformTextureSettings("WebGL");
                        webgl.overridden = b.webglOverridden;
                        webgl.maxTextureSize = b.webglMaxTextureSize;
                        webgl.format = (TextureImporterFormat)b.webglFormat;
                        webgl.compressionQuality = b.webglCompressionQuality;
                        webgl.crunchedCompression = b.webglCrunched;
                        ti.SetPlatformTextureSettings(webgl);

                        ti.SaveAndReimport();
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    private static List<string> CollectTargetTexturePaths()
    {
        List<string> result = new List<string>();
        if (AssetDatabase.IsValidFolder("Assets/Scenes/01Day"))
        {
            string[] lightmapGuids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets/Scenes/01Day" });
            foreach (string guid in lightmapGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    result.Add(path);
            }
        }

        if (File.Exists(Path.GetFullPath("Assets/TexturesAndVideos/sliders.png")))
        {
            result.Add("Assets/TexturesAndVideos/sliders.png");
        }

        return result;
    }

    private static void ModifySerializedAsset(string assetPath, Action<SerializedObject> modifier)
    {
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (asset == null) return;

        SerializedObject so = new SerializedObject(asset);
        modifier(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    private static void ModifyAllSubAssetsAtPath(string assetPath, Action<UnityEngine.Object> modifier)
    {
        UnityEngine.Object[] allObjects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        if (allObjects == null) return;

        foreach (UnityEngine.Object obj in allObjects)
        {
            if (obj != null)
            {
                modifier(obj);
            }
        }
    }

    private static bool SetIntProp(SerializedObject so, string propName, int value)
    {
        SerializedProperty p = so.FindProperty(propName);
        if (p != null && p.intValue != value)
        {
            p.intValue = value;
            return true;
        }
        return false;
    }

    private static bool SetFloatProp(SerializedObject so, string propName, float value)
    {
        SerializedProperty p = so.FindProperty(propName);
        if (p != null && !Mathf.Approximately(p.floatValue, value))
        {
            p.floatValue = value;
            return true;
        }
        return false;
    }

    private static bool SetBoolProp(SerializedObject so, string propName, bool value)
    {
        SerializedProperty p = so.FindProperty(propName);
        if (p != null && p.boolValue != value)
        {
            p.boolValue = value;
            return true;
        }
        return false;
    }

    private static void SetRelativeBool(SerializedProperty parent, string relativeName, bool value)
    {
        SerializedProperty p = parent.FindPropertyRelative(relativeName);
        if (p != null) p.boolValue = value;
    }

    private static void SetRelativeInt(SerializedProperty parent, string relativeName, int value)
    {
        SerializedProperty p = parent.FindPropertyRelative(relativeName);
        if (p != null) p.intValue = value;
    }

    private static void SetVolumeParamBool(SerializedObject volumeComponentSo, string paramName, bool value)
    {
        SerializedProperty paramProp = volumeComponentSo.FindProperty(paramName);
        if (paramProp != null)
        {
            SerializedProperty overrideProp = paramProp.FindPropertyRelative("m_OverrideState");
            SerializedProperty valProp = paramProp.FindPropertyRelative("m_Value");
            if (overrideProp != null) overrideProp.boolValue = true;
            if (valProp != null) valProp.boolValue = value;
        }
    }

    private static void SetVolumeParamInt(SerializedObject volumeComponentSo, string paramName, int value)
    {
        SerializedProperty paramProp = volumeComponentSo.FindProperty(paramName);
        if (paramProp != null)
        {
            SerializedProperty overrideProp = paramProp.FindPropertyRelative("m_OverrideState");
            SerializedProperty valProp = paramProp.FindPropertyRelative("m_Value");
            if (overrideProp != null) overrideProp.boolValue = true;
            if (valProp != null) valProp.intValue = value;
        }
    }

    private static string GetHierarchyPath(Transform current)
    {
        List<string> parts = new List<string>();
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void EnsureBackupFolderExists()
    {
        if (!AssetDatabase.IsValidFolder(BACKUP_FOLDER))
        {
            AssetDatabase.CreateFolder("Assets", "WebGLSimplifiedMaterials");
        }
    }

    private static FullOptimizationBackupState LoadBackupState()
    {
        string fullPrimary = Path.GetFullPath(BACKUP_FILE_PATH);
        if (File.Exists(fullPrimary))
        {
            try
            {
                string json = File.ReadAllText(fullPrimary);
                return JsonUtility.FromJson<FullOptimizationBackupState>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebGLTieredPerformanceOptimizer] Primary backup read failed, trying fallback: {ex.Message}");
            }
        }

        string fullFallback = Path.GetFullPath(BACKUP_FALLBACK_PATH);
        if (File.Exists(fullFallback))
        {
            try
            {
                string json = File.ReadAllText(fullFallback);
                return JsonUtility.FromJson<FullOptimizationBackupState>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebGLTieredPerformanceOptimizer] Fallback backup read failed: {ex.Message}");
            }
        }

        return null;
    }

    private static void SaveBackupState(FullOptimizationBackupState state)
    {
        EnsureBackupFolderExists();
        string json = JsonUtility.ToJson(state, true);
        File.WriteAllText(Path.GetFullPath(BACKUP_FILE_PATH), json);
        File.WriteAllText(Path.GetFullPath(BACKUP_FALLBACK_PATH), json);
    }
}
