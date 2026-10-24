using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

public class WebGLMaterialSimplifier : EditorWindow
{
    private const string ROOT_FOLDER = "Assets/WebGLSimplifiedMaterials";
    private const string GENERATED_MATS_FOLDER = "Assets/WebGLSimplifiedMaterials/Materials";
    private const string BACKUP_ASSET_PATH = "Assets/WebGLSimplifiedMaterials/SceneMaterialBackup.json";
    private const string BACKUP_FALLBACK_PATH = "Assets/WebGLSimplifiedMaterials/SceneMaterialBackup.bak.json";
    private const string OVERRIDES_ASSET_PATH = "Assets/WebGLSimplifiedMaterials/SavedMaterialOverrides.json";
    private const string SIMPLE_LIT_SHADER_NAME = "Universal Render Pipeline/Simple Lit";
    private const int TEXTURE_SAMPLE_SIZE = 128;

    private static readonly Regex LodSuffixRegex = new Regex(@"(_lod\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstanceSuffixRegex = new Regex(@"\s*\(\d+\)$", RegexOptions.Compiled);

    private static readonly string[] PreferredTextureProperties =
    {
        "_BaseMap",
        "_MainTex",
        "_BaseColorMap",
        "_Albedo",
        "_AlbedoMap",
        "_Diffuse",
        "_DiffuseMap"
    };

    private static readonly string[] IgnoredTextureKeywords =
    {
        "normal", "bump", "metal", "rough", "smooth", "spec", "occ", "ao",
        "height", "parallax", "detail", "mask", "emission", "emissive", "lightmap",
        "shadow", "bentnormal", "anisotropy", "coat", "iridescence", "thickness",
        "transmittance", "subsurface", "tangent", "mre", "orm"
    };

    private static readonly string[] PreferredColorProperties =
    {
        "_BaseColor",
        "_Color",
        "_TintColor",
        "_MainColor",
        "_AlbedoColor"
    };

    [Serializable]
    private class MaterialSlotBackup
    {
        public int slotIndex;
        public string materialGuid;
        public string materialAssetPath;
        public string materialName;
        public bool wasNull;
    }

    [Serializable]
    private class RendererBackupEntry
    {
        public bool isPrefabAsset;
        public string prefabAssetPath;
        public string prefabAssetGuid;
        public string scenePath;
        public string sceneName;
        public string rendererGlobalObjectId;
        public string gameObjectGlobalObjectId;
        public string hierarchyPath;
        public string indexedHierarchyPath;
        public string rendererTypeName;
        public int rendererComponentIndex;
        public List<MaterialSlotBackup> slots = new List<MaterialSlotBackup>();
    }

    [Serializable]
    private class GeneratedMaterialLink
    {
        public string simplifiedMaterialPath;
        public string simplifiedMaterialGuid;
        public string meshKey;
        public string originalMaterialGuid;
        public string originalMaterialPath;
        public string originalMaterialName;
    }

    [Serializable]
    private class SceneMaterialBackupDatabase
    {
        public string lastUpdatedTimestamp;
        public List<RendererBackupEntry> entries = new List<RendererBackupEntry>();
        public List<GeneratedMaterialLink> materialLinks = new List<GeneratedMaterialLink>();
    }

    [Serializable]
    private class SavedMaterialOverrideEntry
    {
        public string meshAndMaterialKey;
        public string originalMaterialGuid;
        public string originalMaterialPath;
        public string simplifiedMaterialPath;
        public Color lastAutoColor = Color.white;
        public bool hasCustomOverride;
        public Color customColor = Color.white;
        public float customSmoothness = 0.5f;
        public Color customSpecColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        public Color customEmissionColor = Color.black;
    }

    [Serializable]
    private class SavedMaterialOverridesDatabase
    {
        public string lastSavedTimestamp;
        public List<SavedMaterialOverrideEntry> overrides = new List<SavedMaterialOverrideEntry>();
    }

    private Vector2 _scrollPos;
    private bool _showSavedOverridesList;

    [MenuItem("Tools/WebGL Material Simplifier")]
    public static void ShowWindow()
    {
        GetWindow<WebGLMaterialSimplifier>("WebGL Material Simplifier");
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        GUILayout.Label("Safe Scene & Prefab Material Simplifier (WebGL)", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUILayout.HelpBox(
            "Scans all Renderers across the entire scene AND all Prefabs in Assets.\n\n" +
            "• Never modifies or deletes your original Materials or Textures.\n" +
            "• Creates a unique Simple Lit material per individual Mesh so assets sharing a material or atlas are never linked together.\n" +
            "• Samples texture colors using each Mesh's actual UVs (plus tint detection).\n" +
            "• Automatically skips RenderTexture materials (TVs/video screens) and Particle Systems.\n" +
            "• Saves a permanent backup so you can restore original materials at any time.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        SceneMaterialBackupDatabase db = LoadBackupDatabase();
        int sceneBackupCount = 0;
        int prefabBackupCount = 0;
        if (db != null && db.entries != null)
        {
            foreach (RendererBackupEntry e in db.entries)
            {
                if (e.isPrefabAsset) prefabBackupCount++;
                else sceneBackupCount++;
            }
        }

        SavedMaterialOverridesDatabase overridesDb = LoadOverridesDatabase();
        int customOverrideCount = 0;
        if (overridesDb != null && overridesDb.overrides != null)
        {
            foreach (SavedMaterialOverrideEntry o in overridesDb.overrides)
            {
                if (o.hasCustomOverride) customOverrideCount++;
            }
        }

        EditorGUILayout.LabelField("Permanent Backup Status:",
            (sceneBackupCount + prefabBackupCount) > 0
                ? $"{sceneBackupCount} Scene Renderers, {prefabBackupCount} Prefab Renderers (Updated: {db.lastUpdatedTimestamp})"
                : "No backup created yet");
        EditorGUILayout.LabelField("Saved Modified Colors:",
            $"{customOverrideCount} custom material overrides saved");
        EditorGUILayout.LabelField("Backup Folder:", ROOT_FOLDER);

        EditorGUILayout.Space(10);

        GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
        if (GUILayout.Button("1. Scan Scene + Prefabs, Backup & Simplify (Per-Mesh Materials)", GUILayout.Height(40)))
        {
            SimplifySceneAndPrefabs();
        }

        EditorGUILayout.Space(6);

        GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
        if (GUILayout.Button("2. Save Modified (Remember Custom Tweaked Material Colors)", GUILayout.Height(38)))
        {
            SaveModifiedGeneratedMaterials();
        }

        EditorGUILayout.Space(6);

        GUI.backgroundColor = new Color(1f, 0.7f, 0.4f);
        if (GUILayout.Button("3. Restore Original Materials (Scene + Prefabs)", GUILayout.Height(40)))
        {
            RestoreOriginalMaterials();
        }

        GUI.backgroundColor = Color.white;

        if (customOverrideCount > 0 && overridesDb != null)
        {
            EditorGUILayout.Space(10);
            _showSavedOverridesList = EditorGUILayout.Foldout(_showSavedOverridesList, $"View Saved Modified Materials ({customOverrideCount})", true);
            if (_showSavedOverridesList)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < overridesDb.overrides.Count; i++)
                {
                    SavedMaterialOverrideEntry entry = overridesDb.overrides[i];
                    if (!entry.hasCustomOverride) continue;

                    EditorGUILayout.BeginHorizontal();
                    string label = !string.IsNullOrEmpty(entry.simplifiedMaterialPath)
                        ? Path.GetFileNameWithoutExtension(entry.simplifiedMaterialPath).Replace("_SimpleLit", "")
                        : Path.GetFileNameWithoutExtension(entry.originalMaterialPath);
                    EditorGUILayout.LabelField(label, GUILayout.Width(240));
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ColorField(entry.customColor);
                    EditorGUI.EndDisabledGroup();
                    if (GUILayout.Button("Reset Auto", GUILayout.Width(85)))
                    {
                        entry.hasCustomOverride = false;
                        SaveOverridesDatabase(overridesDb);
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void SimplifySceneAndPrefabs()
    {
        Shader simpleLitShader = Shader.Find(SIMPLE_LIT_SHADER_NAME);
        if (simpleLitShader == null)
        {
            Debug.LogError($"[WebGLMaterialSimplifier] Could not find shader '{SIMPLE_LIT_SHADER_NAME}'. Make sure URP is installed.");
            return;
        }

        EnsureFoldersExist();

        SceneMaterialBackupDatabase backupDb = LoadBackupDatabase() ?? new SceneMaterialBackupDatabase();
        SavedMaterialOverridesDatabase overridesDb = LoadOverridesDatabase() ?? new SavedMaterialOverridesDatabase();

        Dictionary<string, RendererBackupEntry> existingEntryMap = BuildEntryLookupMap(backupDb);
        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath = BuildSimplifiedLinkMap(backupDb);
        Dictionary<string, SavedMaterialOverrideEntry> overrideLookup = BuildOverrideLookupMap(overridesDb);
        Dictionary<string, Material> simplifiedMatCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        Dictionary<Texture, Color32[]> texturePixelsCache = new Dictionary<Texture, Color32[]>();

        List<Renderer> sceneRenderers = CollectAllSceneRenderers();
        List<string> prefabPaths = CollectAllPrefabPaths();

        HashSet<Scene> dirtyScenes = new HashSet<Scene>();
        int modifiedSceneRenderers = 0;
        int modifiedPrefabRenderers = 0;
        int modifiedPrefabsCount = 0;
        int skippedSpecialCount = 0;

        if (sceneRenderers.Count > 0)
        {
            Undo.RecordObjects(sceneRenderers.ToArray(), "Simplify Scene Materials for WebGL");
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            // STEP 1: Process Scene Renderers first so prefab instances record their original materials before prefabs are modified
            for (int rIdx = 0; rIdx < sceneRenderers.Count; rIdx++)
            {
                Renderer renderer = sceneRenderers[rIdx];
                if (renderer == null) continue;

                EditorUtility.DisplayProgressBar(
                    "Simplifying Scene Materials",
                    $"Scene: {renderer.gameObject.name} ({rIdx + 1}/{sceneRenderers.Count})",
                    (float)(rIdx + 1) / Mathf.Max(1, sceneRenderers.Count));

                RendererBackupEntry backupEntry = GetOrCreateSceneBackupEntry(renderer, existingEntryMap, backupDb);
                bool changed = ProcessRendererSlots(
                    renderer,
                    backupEntry,
                    simpleLitShader,
                    simplifiedMatCache,
                    texturePixelsCache,
                    linkBySimplifiedPath,
                    backupDb,
                    overrideLookup,
                    overridesDb,
                    ref skippedSpecialCount);

                if (changed)
                {
                    EditorUtility.SetDirty(renderer);
                    if (renderer.gameObject.scene.IsValid())
                    {
                        dirtyScenes.Add(renderer.gameObject.scene);
                    }
                    modifiedSceneRenderers++;
                }
            }

            // STEP 2: Process all Prefab Assets across Assets/
            for (int pIdx = 0; pIdx < prefabPaths.Count; pIdx++)
            {
                string prefabPath = prefabPaths[pIdx];
                EditorUtility.DisplayProgressBar(
                    "Simplifying Prefab Materials",
                    $"Prefab: {Path.GetFileName(prefabPath)} ({pIdx + 1}/{prefabPaths.Count})",
                    (float)(pIdx + 1) / Mathf.Max(1, prefabPaths.Count));

                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabRoot == null) continue;

                Renderer[] prefabRenderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
                if (prefabRenderers == null || prefabRenderers.Length == 0) continue;

                bool prefabChanged = false;
                string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);

                foreach (Renderer renderer in prefabRenderers)
                {
                    if (renderer == null) continue;

                    RendererBackupEntry backupEntry = GetOrCreatePrefabBackupEntry(
                        renderer,
                        prefabRoot.transform,
                        prefabPath,
                        prefabGuid,
                        existingEntryMap,
                        backupDb);

                    bool rendererChanged = ProcessRendererSlots(
                        renderer,
                        backupEntry,
                        simpleLitShader,
                        simplifiedMatCache,
                        texturePixelsCache,
                        linkBySimplifiedPath,
                        backupDb,
                        overrideLookup,
                        overridesDb,
                        ref skippedSpecialCount);

                    if (rendererChanged)
                    {
                        EditorUtility.SetDirty(renderer);
                        prefabChanged = true;
                        modifiedPrefabRenderers++;
                    }
                }

                if (prefabChanged)
                {
                    EditorUtility.SetDirty(prefabRoot);
                    modifiedPrefabsCount++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        SaveBackupDatabase(backupDb);
        SaveOverridesDatabase(overridesDb);

        foreach (Scene scene in dirtyScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[WebGLMaterialSimplifier] Simplification complete! " +
                  $"Updated {modifiedSceneRenderers} Scene Renderers and {modifiedPrefabRenderers} Prefab Renderers across {modifiedPrefabsCount} Prefabs " +
                  $"({simplifiedMatCache.Count} per-mesh Simple Lit materials active). " +
                  $"Skipped/preserved {skippedSpecialCount} Particle System / RenderTexture slots.");
    }

    private bool ProcessRendererSlots(
        Renderer renderer,
        RendererBackupEntry backupEntry,
        Shader simpleLitShader,
        Dictionary<string, Material> simplifiedMatCache,
        Dictionary<Texture, Color32[]> texturePixelsCache,
        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath,
        SceneMaterialBackupDatabase backupDb,
        Dictionary<string, SavedMaterialOverrideEntry> overrideLookup,
        SavedMaterialOverridesDatabase overridesDb,
        ref int skippedSpecialCount)
    {
        Material[] currentMaterials = renderer.sharedMaterials;
        if (currentMaterials == null || currentMaterials.Length == 0)
            return false;

        EnsureBackupSlotCount(backupEntry, currentMaterials.Length);

        Mesh rendererMesh = GetRendererMesh(renderer);
        string meshIdentifier = GetMeshUniqueIdentifier(renderer, rendererMesh, out string readableMeshName);

        Material[] newMaterials = new Material[currentMaterials.Length];
        bool rendererChanged = false;

        for (int slotIdx = 0; slotIdx < currentMaterials.Length; slotIdx++)
        {
            Material currentMat = currentMaterials[slotIdx];
            MaterialSlotBackup slotBackup = backupEntry.slots[slotIdx];

            Material sourceOriginalMaterial = currentMat;
            bool currentIsGenerated = IsGeneratedSimplifiedMaterial(currentMat);

            if (currentIsGenerated)
            {
                Material recoveredOriginal = LoadOriginalMaterialFromSlotBackup(slotBackup);
                if (recoveredOriginal == null)
                {
                    recoveredOriginal = LoadOriginalMaterialFromGeneratedLink(currentMat, linkBySimplifiedPath);
                    if (recoveredOriginal != null)
                    {
                        RecordMaterialInSlotBackup(slotBackup, slotIdx, recoveredOriginal);
                    }
                }

                if (recoveredOriginal != null)
                {
                    sourceOriginalMaterial = recoveredOriginal;
                }
                else
                {
                    newMaterials[slotIdx] = currentMat;
                    continue;
                }
            }
            else
            {
                RecordMaterialInSlotBackup(slotBackup, slotIdx, currentMat);
            }

            if (sourceOriginalMaterial == null)
            {
                newMaterials[slotIdx] = null;
                continue;
            }

            // Skip Particle Systems, RenderTextures, VideoPlayers, UI, and 2D Sprites
            // (and automatically restore their original material if they were previously simplified)
            if (ShouldSkipRendererOrMaterial(renderer, sourceOriginalMaterial))
            {
                skippedSpecialCount++;
                newMaterials[slotIdx] = sourceOriginalMaterial;
                if (newMaterials[slotIdx] != currentMat)
                {
                    rendererChanged = true;
                }
                continue;
            }

            string origMatPath = NormalizePath(AssetDatabase.GetAssetPath(sourceOriginalMaterial));
            string origMatGuid = !string.IsNullOrEmpty(origMatPath)
                ? AssetDatabase.AssetPathToGUID(origMatPath)
                : sourceOriginalMaterial.GetInstanceID().ToString();

            // Key by (Mesh Identifier + Submesh Slot + Original Material) so every individual mesh gets its own distinct Material asset!
            string meshAndMaterialKey = $"{meshIdentifier}|S{slotIdx}|{origMatGuid}";

            if (!simplifiedMatCache.TryGetValue(meshAndMaterialKey, out Material simplifiedMat) || simplifiedMat == null)
            {
                simplifiedMat = GetOrCreatePerMeshSimplifiedMaterial(
                    sourceOriginalMaterial,
                    rendererMesh,
                    slotIdx,
                    meshAndMaterialKey,
                    readableMeshName,
                    simpleLitShader,
                    texturePixelsCache,
                    linkBySimplifiedPath,
                    backupDb,
                    overrideLookup,
                    overridesDb);
                simplifiedMatCache[meshAndMaterialKey] = simplifiedMat;
            }

            newMaterials[slotIdx] = simplifiedMat != null ? simplifiedMat : currentMat;

            if (newMaterials[slotIdx] != currentMat)
            {
                rendererChanged = true;
            }
        }

        if (rendererChanged)
        {
            renderer.sharedMaterials = newMaterials;
        }

        return rendererChanged;
    }

    /// <summary>
    /// Scans all generated Simple Lit materials in Assets/WebGLSimplifiedMaterials/Materials/
    /// and saves any manual color/property modifications made by the user so future Restore -> Simplify cycles preserve them.
    /// </summary>
    private void SaveModifiedGeneratedMaterials()
    {
        EnsureFoldersExist();
        AssetDatabase.SaveAssets();

        SavedMaterialOverridesDatabase overridesDb = LoadOverridesDatabase() ?? new SavedMaterialOverridesDatabase();
        SceneMaterialBackupDatabase backupDb = LoadBackupDatabase() ?? new SceneMaterialBackupDatabase();

        Dictionary<string, SavedMaterialOverrideEntry> overrideLookup = BuildOverrideLookupMap(overridesDb);
        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath = BuildSimplifiedLinkMap(backupDb);

        HashSet<string> explicitlySelectedMatPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj is Material selMat && IsGeneratedSimplifiedMaterial(selMat))
            {
                explicitlySelectedMatPaths.Add(NormalizePath(AssetDatabase.GetAssetPath(selMat)));
            }
            else if (obj is GameObject selGo)
            {
                Renderer[] rs = selGo.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in rs)
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (IsGeneratedSimplifiedMaterial(m))
                        {
                            explicitlySelectedMatPaths.Add(NormalizePath(AssetDatabase.GetAssetPath(m)));
                        }
                    }
                }
            }
        }

        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { GENERATED_MATS_FOLDER });
        int newlySavedCount = 0;
        int totalSavedCount = 0;

        foreach (string guid in matGuids)
        {
            string matPath = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
            Material genMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (genMat == null) continue;

            Color currentColor = GetMaterialBaseColor(genMat);
            float currentSmoothness = genMat.HasProperty("_Smoothness") ? genMat.GetFloat("_Smoothness") : 0.5f;
            Color currentSpecColor = genMat.HasProperty("_SpecColor") ? genMat.GetColor("_SpecColor") : new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Color currentEmissionColor = genMat.HasProperty("_EmissionColor") ? genMat.GetColor("_EmissionColor") : Color.black;

            linkBySimplifiedPath.TryGetValue(matPath, out GeneratedMaterialLink link);
            string meshAndMatKey = link != null ? link.meshKey : "";
            string origGuid = link != null ? link.originalMaterialGuid : "";
            string origPath = link != null ? link.originalMaterialPath : "";

            SavedMaterialOverrideEntry entry = FindOrCreateOverrideEntry(
                meshAndMatKey,
                origGuid,
                origPath,
                matPath,
                overrideLookup,
                overridesDb);

            bool colorChangedFromAuto = !ColorsAreClose(currentColor, entry.lastAutoColor, 0.003f);
            bool isExplicitlySelected = explicitlySelectedMatPaths.Contains(matPath);

            if (colorChangedFromAuto || isExplicitlySelected || entry.hasCustomOverride)
            {
                if (!entry.hasCustomOverride || !ColorsAreClose(entry.customColor, currentColor, 0.001f))
                {
                    newlySavedCount++;
                }

                entry.hasCustomOverride = true;
                entry.customColor = currentColor;
                entry.customSmoothness = currentSmoothness;
                entry.customSpecColor = currentSpecColor;
                entry.customEmissionColor = currentEmissionColor;
                totalSavedCount++;
            }
        }

        SaveOverridesDatabase(overridesDb);
        AssetDatabase.SaveAssets();

        Debug.Log($"[WebGLMaterialSimplifier] Saved modified material colors! " +
                  $"({newlySavedCount} newly updated, {totalSavedCount} total custom overrides stored in '{OVERRIDES_ASSET_PATH}'). " +
                  "These custom colors will be automatically reapplied whenever you Simplify again after Restoring.");
    }

    private void RestoreOriginalMaterials()
    {
        SceneMaterialBackupDatabase backupDb = LoadBackupDatabase();
        if (backupDb == null || backupDb.entries == null || backupDb.entries.Count == 0)
        {
            Debug.LogError($"[WebGLMaterialSimplifier] No permanent backup found at '{BACKUP_ASSET_PATH}'. Cannot restore.");
            return;
        }

        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath = BuildSimplifiedLinkMap(backupDb);

        List<Renderer> allSceneRenderers = CollectAllSceneRenderers();
        if (allSceneRenderers.Count > 0)
        {
            Undo.RecordObjects(allSceneRenderers.ToArray(), "Restore Original Scene Materials");
        }

        Dictionary<string, Renderer> byRendererGlobalId = new Dictionary<string, Renderer>();
        Dictionary<string, Renderer> byGoGlobalIdAndCompIndex = new Dictionary<string, Renderer>();
        Dictionary<string, Renderer> byIndexedPath = new Dictionary<string, Renderer>();
        Dictionary<string, List<Renderer>> byHierarchyPath = new Dictionary<string, List<Renderer>>();

        foreach (Renderer r in allSceneRenderers)
        {
            if (r == null) continue;

            string rGlobalId = GetGlobalObjectIdString(r);
            if (!string.IsNullOrEmpty(rGlobalId) && !byRendererGlobalId.ContainsKey(rGlobalId))
            {
                byRendererGlobalId[rGlobalId] = r;
            }

            string goGlobalId = GetGlobalObjectIdString(r.gameObject);
            int compIdx = GetRendererComponentIndex(r);
            string goKey = $"{goGlobalId}|{compIdx}";
            if (!string.IsNullOrEmpty(goGlobalId) && !byGoGlobalIdAndCompIndex.ContainsKey(goKey))
            {
                byGoGlobalIdAndCompIndex[goKey] = r;
            }

            string scenePrefix = r.gameObject.scene.path ?? r.gameObject.scene.name;
            string idxPathKey = $"{scenePrefix}|{GetIndexedHierarchyPath(r.transform)}|{compIdx}";
            if (!byIndexedPath.ContainsKey(idxPathKey))
            {
                byIndexedPath[idxPathKey] = r;
            }

            string plainPathKey = $"{scenePrefix}|{GetHierarchyPath(r.transform)}|{r.GetType().FullName}";
            if (!byHierarchyPath.TryGetValue(plainPathKey, out List<Renderer> list))
            {
                list = new List<Renderer>();
                byHierarchyPath[plainPathKey] = list;
            }
            list.Add(r);
        }

        HashSet<Scene> dirtyScenes = new HashSet<Scene>();
        HashSet<GameObject> dirtyPrefabRoots = new HashSet<GameObject>();
        int restoredSceneRenderers = 0;
        int restoredPrefabRenderers = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < backupDb.entries.Count; i++)
            {
                RendererBackupEntry entry = backupDb.entries[i];
                EditorUtility.DisplayProgressBar(
                    "Restoring Original Materials",
                    $"Restoring {entry.hierarchyPath} ({i + 1}/{backupDb.entries.Count})",
                    (float)(i + 1) / backupDb.entries.Count);

                if (entry.isPrefabAsset)
                {
                    Renderer prefabRenderer = FindMatchingPrefabRenderer(entry, out GameObject prefabRoot);
                    if (prefabRenderer != null && RestoreRendererFromEntry(prefabRenderer, entry, linkBySimplifiedPath))
                    {
                        EditorUtility.SetDirty(prefabRenderer);
                        if (prefabRoot != null)
                        {
                            EditorUtility.SetDirty(prefabRoot);
                            dirtyPrefabRoots.Add(prefabRoot);
                        }
                        restoredPrefabRenderers++;
                    }
                }
                else
                {
                    Renderer sceneRenderer = FindMatchingSceneRenderer(
                        entry,
                        byRendererGlobalId,
                        byGoGlobalIdAndCompIndex,
                        byIndexedPath,
                        byHierarchyPath);

                    if (sceneRenderer != null && RestoreRendererFromEntry(sceneRenderer, entry, linkBySimplifiedPath))
                    {
                        EditorUtility.SetDirty(sceneRenderer);
                        if (sceneRenderer.gameObject.scene.IsValid())
                        {
                            dirtyScenes.Add(sceneRenderer.gameObject.scene);
                        }
                        restoredSceneRenderers++;
                    }
                }
            }

            // Safety pass: restore any remaining generated materials via the global link table
            foreach (Renderer r in allSceneRenderers)
            {
                if (r != null && RestoreAnyRemainingSimplifiedMaterials(r, linkBySimplifiedPath))
                {
                    EditorUtility.SetDirty(r);
                    if (r.gameObject.scene.IsValid())
                        dirtyScenes.Add(r.gameObject.scene);
                    restoredSceneRenderers++;
                }
            }

            List<string> allPrefabPaths = CollectAllPrefabPaths();
            foreach (string prefabPath in allPrefabPaths)
            {
                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabRoot == null) continue;

                Renderer[] rs = prefabRoot.GetComponentsInChildren<Renderer>(true);
                bool anyFixed = false;
                foreach (Renderer r in rs)
                {
                    if (r != null && RestoreAnyRemainingSimplifiedMaterials(r, linkBySimplifiedPath))
                    {
                        EditorUtility.SetDirty(r);
                        anyFixed = true;
                        restoredPrefabRenderers++;
                    }
                }
                if (anyFixed)
                {
                    EditorUtility.SetDirty(prefabRoot);
                    dirtyPrefabRoots.Add(prefabRoot);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        foreach (Scene scene in dirtyScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[WebGLMaterialSimplifier] Restored {restoredSceneRenderers} Scene Renderers and {restoredPrefabRenderers} Prefab Renderers ({dirtyPrefabRoots.Count} Prefabs) to their original materials.");
    }

    private static bool RestoreRendererFromEntry(
        Renderer targetRenderer,
        RendererBackupEntry entry,
        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath)
    {
        Material[] currentMaterials = targetRenderer.sharedMaterials;
        if (currentMaterials == null || entry.slots == null || entry.slots.Count == 0)
            return false;

        int slotCount = currentMaterials.Length;
        Material[] restoredMaterials = new Material[slotCount];
        Array.Copy(currentMaterials, restoredMaterials, slotCount);

        bool changed = false;

        foreach (MaterialSlotBackup slotBackup in entry.slots)
        {
            if (slotBackup.slotIndex < 0 || slotBackup.slotIndex >= slotCount)
                continue;

            if (slotBackup.wasNull)
            {
                if (restoredMaterials[slotBackup.slotIndex] != null)
                {
                    restoredMaterials[slotBackup.slotIndex] = null;
                    changed = true;
                }
                continue;
            }

            Material originalMat = LoadOriginalMaterialFromSlotBackup(slotBackup);
            if (originalMat == null && IsGeneratedSimplifiedMaterial(restoredMaterials[slotBackup.slotIndex]))
            {
                originalMat = LoadOriginalMaterialFromGeneratedLink(restoredMaterials[slotBackup.slotIndex], linkBySimplifiedPath);
            }

            if (originalMat != null && restoredMaterials[slotBackup.slotIndex] != originalMat)
            {
                restoredMaterials[slotBackup.slotIndex] = originalMat;
                changed = true;
            }
        }

        if (changed)
        {
            targetRenderer.sharedMaterials = restoredMaterials;
        }

        return changed;
    }

    private static bool RestoreAnyRemainingSimplifiedMaterials(
        Renderer renderer,
        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath)
    {
        Material[] mats = renderer.sharedMaterials;
        if (mats == null || mats.Length == 0)
            return false;

        bool changed = false;
        for (int i = 0; i < mats.Length; i++)
        {
            if (IsGeneratedSimplifiedMaterial(mats[i]))
            {
                Material orig = LoadOriginalMaterialFromGeneratedLink(mats[i], linkBySimplifiedPath);
                if (orig != null)
                {
                    mats[i] = orig;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            renderer.sharedMaterials = mats;
        }
        return changed;
    }

    private Material GetOrCreatePerMeshSimplifiedMaterial(
        Material originalMat,
        Mesh rendererMesh,
        int submeshIndex,
        string meshAndMaterialKey,
        string readableMeshName,
        Shader simpleLitShader,
        Dictionary<Texture, Color32[]> texturePixelsCache,
        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath,
        SceneMaterialBackupDatabase backupDb,
        Dictionary<string, SavedMaterialOverrideEntry> overrideLookup,
        SavedMaterialOverridesDatabase overridesDb)
    {
        EnsureFoldersExist();

        string originalPath = NormalizePath(AssetDatabase.GetAssetPath(originalMat));
        string originalGuid = !string.IsNullOrEmpty(originalPath) ? AssetDatabase.AssetPathToGUID(originalPath) : "";

        string safeMeshName = SanitizeFileName(string.IsNullOrEmpty(readableMeshName) ? "Mesh" : readableMeshName);
        string safeMatName = SanitizeFileName(string.IsNullOrEmpty(originalMat.name) ? "Mat" : originalMat.name.Replace("_backup", ""));
        string keyHash = ComputeShortStableHash(meshAndMaterialKey);

        string simplifiedAssetPath = NormalizePath($"{GENERATED_MATS_FOLDER}/{safeMeshName}__{safeMatName}_{keyHash}_SimpleLit.mat");

        Color autoColor = ComputeAutoSimplifiedColorForMesh(originalMat, rendererMesh, submeshIndex, texturePixelsCache);

        SavedMaterialOverrideEntry overrideEntry = FindOrCreateOverrideEntry(
            meshAndMaterialKey,
            originalGuid,
            originalPath,
            simplifiedAssetPath,
            overrideLookup,
            overridesDb);

        overrideEntry.lastAutoColor = autoColor;

        Color targetColor = overrideEntry.hasCustomOverride ? overrideEntry.customColor : autoColor;
        targetColor.a = 1f;

        Material simplifiedMat = AssetDatabase.LoadAssetAtPath<Material>(simplifiedAssetPath);
        if (simplifiedMat == null)
        {
            simplifiedMat = new Material(simpleLitShader)
            {
                name = $"{safeMeshName}__{safeMatName}_SimpleLit"
            };
            ApplySimpleLitColorSettings(simplifiedMat, simpleLitShader, targetColor, overrideEntry);
            AssetDatabase.CreateAsset(simplifiedMat, simplifiedAssetPath);
        }
        else
        {
            ApplySimpleLitColorSettings(simplifiedMat, simpleLitShader, targetColor, overrideEntry);
            EditorUtility.SetDirty(simplifiedMat);
        }

        RegisterGeneratedMaterialLink(
            simplifiedAssetPath,
            meshAndMaterialKey,
            originalGuid,
            originalPath,
            originalMat.name,
            linkBySimplifiedPath,
            backupDb);

        return simplifiedMat;
    }

    private static Mesh GetRendererMesh(Renderer renderer)
    {
        if (renderer is MeshRenderer mr)
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }
        if (renderer is SkinnedMeshRenderer smr)
        {
            return smr.sharedMesh;
        }
        return null;
    }

    private static string GetMeshUniqueIdentifier(Renderer renderer, Mesh mesh, out string readableMeshName)
    {
        if (mesh != null)
        {
            string meshAssetPath = NormalizePath(AssetDatabase.GetAssetPath(mesh));
            string strippedMeshName = LodSuffixRegex.Replace(mesh.name ?? "Mesh", "");

            // If it's a built-in Unity primitive (Cube, Plane, Sphere, Quad, Cylinder, Capsule),
            // combine with the GameObject base name so unrelated primitive objects don't share one material
            if (string.IsNullOrEmpty(meshAssetPath) || meshAssetPath.Contains("unity default resources"))
            {
                string goBaseName = InstanceSuffixRegex.Replace(renderer.gameObject.name, "").Trim();
                readableMeshName = $"{goBaseName}_{strippedMeshName}";
                return $"PRIM:{goBaseName}|{strippedMeshName}";
            }

            string meshAssetGuid = AssetDatabase.AssetPathToGUID(meshAssetPath);
            readableMeshName = strippedMeshName;
            return $"MESH:{meshAssetGuid}|{strippedMeshName}";
        }

        string fallbackGoName = InstanceSuffixRegex.Replace(renderer.gameObject.name, "").Trim();
        readableMeshName = fallbackGoName;
        return $"OBJ:{fallbackGoName}";
    }

    private static string ComputeShortStableHash(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "00000000";

        uint hash = 2166136261u;
        for (int i = 0; i < input.Length; i++)
        {
            hash ^= input[i];
            hash *= 16777619u;
        }
        return hash.ToString("x8");
    }

    private static Color ComputeAutoSimplifiedColorForMesh(
        Material originalMat,
        Mesh mesh,
        int submeshIndex,
        Dictionary<Texture, Color32[]> texturePixelsCache)
    {
        Texture mainTex = ScanForMainTexture(originalMat, out string matchedTexProp);
        Color matColor = ScanForColor(originalMat, out bool foundExplicitTint);

        if (mainTex != null)
        {
            Vector2 texScale = Vector2.one;
            Vector2 texOffset = Vector2.zero;
            if (!string.IsNullOrEmpty(matchedTexProp) && originalMat.HasProperty(matchedTexProp))
            {
                texScale = originalMat.GetTextureScale(matchedTexProp);
                texOffset = originalMat.GetTextureOffset(matchedTexProp);
            }

            Color texAvgColor = CalculateAverageTextureColorForMesh(
                mainTex,
                mesh,
                submeshIndex,
                texScale,
                texOffset,
                matColor,
                texturePixelsCache);

            if (foundExplicitTint && !IsApproximatelyWhite(matColor) && !IsApproximatelyBlack(matColor))
            {
                float texSaturation = GetColorSaturation(texAvgColor);
                if (texSaturation < 0.12f)
                {
                    float luminance = Mathf.Clamp(GetColorLuminance(texAvgColor), 0.35f, 1f);
                    return new Color(
                        Mathf.Clamp01(matColor.r * luminance * 1.15f),
                        Mathf.Clamp01(matColor.g * luminance * 1.15f),
                        Mathf.Clamp01(matColor.b * luminance * 1.15f),
                        1f);
                }

                return new Color(
                    Mathf.Clamp01(texAvgColor.r * matColor.r),
                    Mathf.Clamp01(texAvgColor.g * matColor.g),
                    Mathf.Clamp01(texAvgColor.b * matColor.b),
                    1f);
            }

            return new Color(texAvgColor.r, texAvgColor.g, texAvgColor.b, 1f);
        }

        return new Color(matColor.r, matColor.g, matColor.b, 1f);
    }

    private static void ApplySimpleLitColorSettings(
        Material mat,
        Shader simpleLitShader,
        Color solidColor,
        SavedMaterialOverrideEntry overrideEntry)
    {
        if (mat.shader != simpleLitShader)
        {
            mat.shader = simpleLitShader;
        }

        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", null);

        if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", null);

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", solidColor);

        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", solidColor);

        if (overrideEntry != null && overrideEntry.hasCustomOverride)
        {
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", overrideEntry.customSmoothness);
            if (mat.HasProperty("_SpecColor"))
                mat.SetColor("_SpecColor", overrideEntry.customSpecColor);
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", overrideEntry.customEmissionColor);
        }

        mat.enableInstancing = true;
    }

    /// <summary>
    /// Excludes Particle Systems, RenderTextures, VideoPlayers, Sprites, and UI from material simplification.
    /// </summary>
    private static bool ShouldSkipRendererOrMaterial(Renderer renderer, Material mat)
    {
        if (mat == null)
            return true;

        // Exclude Particle Systems and Sprites
        if (renderer is ParticleSystemRenderer || renderer is SpriteRenderer)
            return true;

        if (renderer != null && renderer.GetComponent<ParticleSystem>() != null)
            return true;

        if (mat.shader != null)
        {
            string shaderName = mat.shader.name;
            if (shaderName.StartsWith("Sprites/", StringComparison.OrdinalIgnoreCase) ||
                shaderName.StartsWith("UI/", StringComparison.OrdinalIgnoreCase) ||
                shaderName.StartsWith("Hidden/", StringComparison.OrdinalIgnoreCase) ||
                shaderName.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("VFX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.Contains("TextMeshPro"))
            {
                return true;
            }
        }

        // Exclude any material that uses a RenderTexture as input
        if (DoesMaterialUseRenderTexture(mat))
            return true;

        // Exclude Renderers driven by a VideoPlayer
        if (renderer != null)
        {
            VideoPlayer vp = renderer.GetComponent<VideoPlayer>();
            if (vp == null) vp = renderer.GetComponentInParent<VideoPlayer>();
            if (vp != null && (vp.targetMaterialRenderer == renderer || vp.renderMode == VideoRenderMode.MaterialOverride))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoesMaterialUseRenderTexture(Material mat)
    {
        if (mat == null)
            return false;

        if (mat.mainTexture is RenderTexture)
            return true;

        string[] texPropNames = mat.GetTexturePropertyNames();
        if (texPropNames != null)
        {
            for (int i = 0; i < texPropNames.Length; i++)
            {
                Texture tex = mat.GetTexture(texPropNames[i]);
                if (tex is RenderTexture)
                    return true;
            }
        }

        if (mat.shader != null)
        {
            int propCount = mat.shader.GetPropertyCount();
            for (int i = 0; i < propCount; i++)
            {
                if (mat.shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                {
                    string propName = mat.shader.GetPropertyName(i);
                    Texture tex = mat.GetTexture(propName);
                    if (tex is RenderTexture)
                        return true;
                }
            }
        }

        return false;
    }

    private static Texture ScanForMainTexture(Material mat, out string matchedPropertyName)
    {
        matchedPropertyName = "";
        if (mat == null || mat.shader == null)
            return null;

        foreach (string propName in PreferredTextureProperties)
        {
            if (mat.HasProperty(propName))
            {
                Texture tex = mat.GetTexture(propName);
                if (tex != null && !IsDataOrNormalTexture(tex))
                {
                    matchedPropertyName = propName;
                    return tex;
                }
            }
        }

        Texture bestTexture = null;
        int bestScore = int.MinValue;

        int propCount = mat.shader.GetPropertyCount();
        for (int i = 0; i < propCount; i++)
        {
            if (mat.shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                continue;

            string rawPropName = mat.shader.GetPropertyName(i);
            Texture tex = mat.GetTexture(rawPropName);
            if (tex == null)
                continue;

            string propNameLower = rawPropName.ToLowerInvariant();
            string propDescLower = mat.shader.GetPropertyDescription(i).ToLowerInvariant();
            string texNameLower = (tex.name ?? "").ToLowerInvariant();

            if (ContainsIgnoredTextureKeyword(propNameLower) ||
                ContainsIgnoredTextureKeyword(propDescLower) ||
                IsDataOrNormalTexture(tex))
            {
                continue;
            }

            int score = 0;

            if (propNameLower.Contains("base") || propDescLower.Contains("base") ||
                propNameLower.Contains("albedo") || propDescLower.Contains("albedo") ||
                propNameLower.Contains("main") || propDescLower.Contains("main") ||
                propNameLower.Contains("diffuse") || propDescLower.Contains("diffuse") ||
                propNameLower.Contains("color") || propDescLower.Contains("color"))
            {
                score += 50;
            }

            if (texNameLower.Contains("basecolor") || texNameLower.Contains("base_color") ||
                texNameLower.Contains("albedo") || texNameLower.Contains("diffuse") ||
                texNameLower.Contains("color") || texNameLower.EndsWith("_bc") ||
                texNameLower.EndsWith("_d") || texNameLower.EndsWith("_c"))
            {
                score += 60;
            }

            string texPath = AssetDatabase.GetAssetPath(tex);
            if (!string.IsNullOrEmpty(texPath) && AssetImporter.GetAtPath(texPath) is TextureImporter ti)
            {
                if (ti.sRGBTexture)
                    score += 25;
                else
                    score -= 30;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestTexture = tex;
                matchedPropertyName = rawPropName;
            }
        }

        return bestTexture;
    }

    private static bool IsDataOrNormalTexture(Texture tex)
    {
        if (tex == null)
            return true;

        string path = AssetDatabase.GetAssetPath(tex);
        if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            if (ti.textureType == TextureImporterType.NormalMap ||
                ti.textureType == TextureImporterType.SingleChannel ||
                ti.textureType == TextureImporterType.Lightmap ||
                ti.textureType == TextureImporterType.DirectionalLightmap ||
                ti.textureType == TextureImporterType.Shadowmask)
            {
                return true;
            }
        }

        string nameLower = (tex.name ?? "").ToLowerInvariant();
        if (nameLower.EndsWith("_n") || nameLower.EndsWith("_normal") || nameLower.Contains("_normal_") ||
            nameLower.EndsWith("_orm") || nameLower.EndsWith("_mre") || nameLower.EndsWith("_h") ||
            nameLower.EndsWith("_height") || nameLower.EndsWith("_ao") || nameLower.EndsWith("_roughness") ||
            nameLower.EndsWith("_metallic") || nameLower.EndsWith("_mask") || nameLower.EndsWith("_spec") ||
            nameLower.Contains("normalmap") || nameLower.Contains("ambient_occlusion") ||
            nameLower.Contains("ambientocclusion") || nameLower.Contains("_roughness") ||
            nameLower.Contains("_metallic"))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsIgnoredTextureKeyword(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        for (int i = 0; i < IgnoredTextureKeywords.Length; i++)
        {
            if (text.Contains(IgnoredTextureKeywords[i]))
                return true;
        }
        return false;
    }

    private static Color ScanForColor(Material mat, out bool foundExplicitColor)
    {
        foundExplicitColor = false;
        if (mat == null || mat.shader == null)
            return Color.white;

        foreach (string propName in PreferredColorProperties)
        {
            if (mat.HasProperty(propName))
            {
                Color col = mat.GetColor(propName);
                if (col.a > 0.01f || (col.r + col.g + col.b) > 0.01f)
                {
                    foundExplicitColor = true;
                    return new Color(col.r, col.g, col.b, 1f);
                }
            }
        }

        Color fallbackColor = Color.white;
        int propCount = mat.shader.GetPropertyCount();

        for (int i = 0; i < propCount; i++)
        {
            if (mat.shader.GetPropertyType(i) != ShaderPropertyType.Color)
                continue;

            string rawPropName = mat.shader.GetPropertyName(i);
            string propNameLower = rawPropName.ToLowerInvariant();
            string propDescLower = mat.shader.GetPropertyDescription(i).ToLowerInvariant();

            if (propNameLower.Contains("emission") || propNameLower.Contains("emissive") ||
                propNameLower.Contains("spec") || propDescLower.Contains("emission") ||
                propDescLower.Contains("specular"))
            {
                continue;
            }

            if (propNameLower.Contains("base") || propDescLower.Contains("base") ||
                propNameLower.Contains("color") || propDescLower.Contains("color") ||
                propNameLower.Contains("tint") || propDescLower.Contains("tint") ||
                propNameLower.Contains("albedo") || propDescLower.Contains("albedo") ||
                propNameLower.Contains("diffuse") || propDescLower.Contains("diffuse"))
            {
                Color col = mat.GetColor(rawPropName);
                if (col.a > 0.01f && (col.r + col.g + col.b) > 0.01f)
                {
                    foundExplicitColor = true;
                    if (!IsApproximatelyWhite(col))
                    {
                        return new Color(col.r, col.g, col.b, 1f);
                    }
                    fallbackColor = new Color(col.r, col.g, col.b, 1f);
                }
            }
        }

        return fallbackColor;
    }

    private static Color32[] GetOrSampleTexturePixels(Texture texture, Dictionary<Texture, Color32[]> texturePixelsCache)
    {
        if (texture == null)
            return null;

        if (texturePixelsCache != null && texturePixelsCache.TryGetValue(texture, out Color32[] cached))
            return cached;

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture tempRT = RenderTexture.GetTemporary(
            TEXTURE_SAMPLE_SIZE,
            TEXTURE_SAMPLE_SIZE,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);

        Texture2D readableTex = new Texture2D(TEXTURE_SAMPLE_SIZE, TEXTURE_SAMPLE_SIZE, TextureFormat.RGBA32, false, false);

        try
        {
            Graphics.Blit(texture, tempRT);
            RenderTexture.active = tempRT;
            readableTex.ReadPixels(new Rect(0, 0, TEXTURE_SAMPLE_SIZE, TEXTURE_SAMPLE_SIZE), 0, 0, false);
            readableTex.Apply(false, false);

            Color32[] pixels = readableTex.GetPixels32();
            if (texturePixelsCache != null)
            {
                texturePixelsCache[texture] = pixels;
            }
            return pixels;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WebGLMaterialSimplifier] Could not sample texture '{texture.name}': {ex.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(tempRT);
            DestroyImmediate(readableTex);
        }
    }

    /// <summary>
    /// Samples the texture at the actual UV coordinates used by the given Mesh & submesh (if available),
    /// so meshes sharing a texture atlas each get their own accurate local color.
    /// Falls back to whole-texture averaging if mesh UVs are unavailable.
    /// </summary>
    private static Color CalculateAverageTextureColorForMesh(
        Texture texture,
        Mesh mesh,
        int submeshIndex,
        Vector2 texScale,
        Vector2 texOffset,
        Color fallbackColor,
        Dictionary<Texture, Color32[]> texturePixelsCache)
    {
        Color32[] pixels = GetOrSampleTexturePixels(texture, texturePixelsCache);
        if (pixels == null || pixels.Length != TEXTURE_SAMPLE_SIZE * TEXTURE_SAMPLE_SIZE)
            return fallbackColor;

        double sumR = 0.0;
        double sumG = 0.0;
        double sumB = 0.0;
        double totalWeight = 0.0;

        // 1. Try UV-specific sampling for this mesh + submesh
        if (mesh != null && submeshIndex >= 0 && submeshIndex < mesh.subMeshCount)
        {
            try
            {
                Vector2[] uvs = mesh.uv;
                if (uvs != null && uvs.Length > 0)
                {
                    int[] indices = mesh.GetIndices(submeshIndex);
                    if (indices != null && indices.Length > 0)
                    {
                        int step = Mathf.Max(1, indices.Length / 1500);
                        for (int i = 0; i < indices.Length; i += step)
                        {
                            int vIdx = indices[i];
                            if (vIdx < 0 || vIdx >= uvs.Length) continue;

                            AccumulateSampleAtUV(uvs[vIdx], texScale, texOffset, pixels, ref sumR, ref sumG, ref sumB, ref totalWeight);

                            // Also sample triangle centroids when possible
                            if (i + 2 < indices.Length)
                            {
                                int v1 = indices[i + 1];
                                int v2 = indices[i + 2];
                                if (v1 >= 0 && v1 < uvs.Length && v2 >= 0 && v2 < uvs.Length)
                                {
                                    Vector2 centroidUV = (uvs[vIdx] + uvs[v1] + uvs[v2]) / 3f;
                                    AccumulateSampleAtUV(centroidUV, texScale, texOffset, pixels, ref sumR, ref sumG, ref sumB, ref totalWeight);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore and fall back to full-texture average if mesh UV read fails
            }
        }

        // 2. Fallback: sample full texture if UV sampling yielded no weight
        if (totalWeight <= 0.0001)
        {
            sumR = 0.0;
            sumG = 0.0;
            sumB = 0.0;
            totalWeight = 0.0;

            for (int i = 0; i < pixels.Length; i++)
            {
                AccumulatePixel(pixels[i], ref sumR, ref sumG, ref sumB, ref totalWeight);
            }
        }

        if (totalWeight <= 0.0001)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                sumR += p.r / 255.0;
                sumG += p.g / 255.0;
                sumB += p.b / 255.0;
            }
            totalWeight = pixels.Length;
        }

        float avgR = Mathf.Clamp01((float)(sumR / totalWeight));
        float avgG = Mathf.Clamp01((float)(sumG / totalWeight));
        float avgB = Mathf.Clamp01((float)(sumB / totalWeight));

        return new Color(avgR, avgG, avgB, 1f);
    }

    private static void AccumulateSampleAtUV(
        Vector2 uv,
        Vector2 scale,
        Vector2 offset,
        Color32[] pixels,
        ref double sumR,
        ref double sumG,
        ref double sumB,
        ref double totalWeight)
    {
        float u = Mathf.Repeat(uv.x * scale.x + offset.x, 1f);
        float v = Mathf.Repeat(uv.y * scale.y + offset.y, 1f);

        int px = Mathf.Clamp((int)(u * TEXTURE_SAMPLE_SIZE), 0, TEXTURE_SAMPLE_SIZE - 1);
        int py = Mathf.Clamp((int)(v * TEXTURE_SAMPLE_SIZE), 0, TEXTURE_SAMPLE_SIZE - 1);

        Color32 p = pixels[py * TEXTURE_SAMPLE_SIZE + px];
        AccumulatePixel(p, ref sumR, ref sumG, ref sumB, ref totalWeight);
    }

    private static void AccumulatePixel(
        Color32 p,
        ref double sumR,
        ref double sumG,
        ref double sumB,
        ref double totalWeight)
    {
        double alpha = p.a / 255.0;
        if (alpha <= 0.01)
            return;

        double r = p.r / 255.0;
        double g = p.g / 255.0;
        double b = p.b / 255.0;

        double maxC = Math.Max(r, Math.Max(g, b));
        double minC = Math.Min(r, Math.Min(g, b));
        double saturation = maxC - minC;

        double pixelWeight = alpha * (1.0 + saturation * 1.5);
        if (maxC < 0.03)
        {
            pixelWeight *= 0.05;
        }

        sumR += r * pixelWeight;
        sumG += g * pixelWeight;
        sumB += b * pixelWeight;
        totalWeight += pixelWeight;
    }

    private static List<Renderer> CollectAllSceneRenderers()
    {
        HashSet<Renderer> uniqueRenderers = new HashSet<Renderer>();
        List<Renderer> result = new List<Renderer>();

        int sceneCount = SceneManager.sceneCount;
        for (int s = 0; s < sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                Renderer[] childRenderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in childRenderers)
                {
                    if (r != null && !EditorUtility.IsPersistent(r) && uniqueRenderers.Add(r))
                    {
                        result.Add(r);
                    }
                }
            }
        }

        Renderer[] foundRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Renderer r in foundRenderers)
        {
            if (r != null && !EditorUtility.IsPersistent(r) && r.gameObject.scene.IsValid() && uniqueRenderers.Add(r))
            {
                result.Add(r);
            }
        }

        return result;
    }

    private static List<string> CollectAllPrefabPaths()
    {
        List<string> paths = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                continue;
            if (path.StartsWith(ROOT_FOLDER, StringComparison.OrdinalIgnoreCase))
                continue;

            paths.Add(path);
        }
        return paths;
    }

    private static bool IsGeneratedSimplifiedMaterial(Material mat)
    {
        if (mat == null)
            return false;

        string path = NormalizePath(AssetDatabase.GetAssetPath(mat));
        return !string.IsNullOrEmpty(path) &&
               path.StartsWith(GENERATED_MATS_FOLDER, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, RendererBackupEntry> BuildEntryLookupMap(SceneMaterialBackupDatabase db)
    {
        Dictionary<string, RendererBackupEntry> map = new Dictionary<string, RendererBackupEntry>();
        if (db == null || db.entries == null)
            return map;

        foreach (RendererBackupEntry entry in db.entries)
        {
            if (!string.IsNullOrEmpty(entry.rendererGlobalObjectId) &&
                entry.rendererGlobalObjectId != "GlobalObjectId_V1-0-00000000000000000000000000000000-0-0")
            {
                map[$"GID:{entry.rendererGlobalObjectId}"] = entry;
            }

            if (entry.isPrefabAsset)
            {
                string prefabKey = $"PREFAB:{NormalizePath(entry.prefabAssetPath)}|{entry.indexedHierarchyPath}|{entry.rendererComponentIndex}";
                map[prefabKey] = entry;
            }
            else
            {
                string indexedKey = $"IDX:{NormalizePath(entry.scenePath)}|{entry.indexedHierarchyPath}|{entry.rendererComponentIndex}";
                map[indexedKey] = entry;
            }
        }

        return map;
    }

    private static Dictionary<string, GeneratedMaterialLink> BuildSimplifiedLinkMap(SceneMaterialBackupDatabase db)
    {
        Dictionary<string, GeneratedMaterialLink> map = new Dictionary<string, GeneratedMaterialLink>(StringComparer.OrdinalIgnoreCase);
        if (db == null || db.materialLinks == null)
            return map;

        foreach (GeneratedMaterialLink link in db.materialLinks)
        {
            if (!string.IsNullOrEmpty(link.simplifiedMaterialPath))
            {
                map[NormalizePath(link.simplifiedMaterialPath)] = link;
            }
        }

        return map;
    }

    private static void RegisterGeneratedMaterialLink(
        string simplifiedPath,
        string meshAndMaterialKey,
        string origGuid,
        string origPath,
        string origName,
        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath,
        SceneMaterialBackupDatabase db)
    {
        simplifiedPath = NormalizePath(simplifiedPath);
        if (!linkBySimplifiedPath.TryGetValue(simplifiedPath, out GeneratedMaterialLink link))
        {
            link = new GeneratedMaterialLink
            {
                simplifiedMaterialPath = simplifiedPath
            };
            if (db.materialLinks == null)
                db.materialLinks = new List<GeneratedMaterialLink>();
            db.materialLinks.Add(link);
            linkBySimplifiedPath[simplifiedPath] = link;
        }

        link.simplifiedMaterialGuid = AssetDatabase.AssetPathToGUID(simplifiedPath);
        link.meshKey = meshAndMaterialKey;
        link.originalMaterialGuid = origGuid;
        link.originalMaterialPath = origPath;
        link.originalMaterialName = origName;
    }

    private static Material LoadOriginalMaterialFromGeneratedLink(
        Material simplifiedMat,
        Dictionary<string, GeneratedMaterialLink> linkBySimplifiedPath)
    {
        if (simplifiedMat == null || linkBySimplifiedPath == null)
            return null;

        string simpPath = NormalizePath(AssetDatabase.GetAssetPath(simplifiedMat));
        if (linkBySimplifiedPath.TryGetValue(simpPath, out GeneratedMaterialLink link))
        {
            return LoadMaterialByGuidOrPath(link.originalMaterialGuid, link.originalMaterialPath, link.originalMaterialName);
        }

        return null;
    }

    private static Dictionary<string, SavedMaterialOverrideEntry> BuildOverrideLookupMap(SavedMaterialOverridesDatabase db)
    {
        Dictionary<string, SavedMaterialOverrideEntry> map = new Dictionary<string, SavedMaterialOverrideEntry>(StringComparer.OrdinalIgnoreCase);
        if (db == null || db.overrides == null)
            return map;

        foreach (SavedMaterialOverrideEntry entry in db.overrides)
        {
            if (!string.IsNullOrEmpty(entry.meshAndMaterialKey))
                map[$"KEY:{entry.meshAndMaterialKey}"] = entry;
            if (!string.IsNullOrEmpty(entry.simplifiedMaterialPath))
                map[$"SIMP:{NormalizePath(entry.simplifiedMaterialPath)}"] = entry;
        }

        return map;
    }

    private static SavedMaterialOverrideEntry FindOrCreateOverrideEntry(
        string meshAndMaterialKey,
        string origGuid,
        string origPath,
        string simplifiedPath,
        Dictionary<string, SavedMaterialOverrideEntry> lookup,
        SavedMaterialOverridesDatabase db)
    {
        simplifiedPath = NormalizePath(simplifiedPath);
        origPath = NormalizePath(origPath);

        if (!string.IsNullOrEmpty(meshAndMaterialKey) && lookup.TryGetValue($"KEY:{meshAndMaterialKey}", out SavedMaterialOverrideEntry byKey))
        {
            if (!string.IsNullOrEmpty(simplifiedPath))
            {
                byKey.simplifiedMaterialPath = simplifiedPath;
                lookup[$"SIMP:{simplifiedPath}"] = byKey;
            }
            return byKey;
        }

        if (!string.IsNullOrEmpty(simplifiedPath) && lookup.TryGetValue($"SIMP:{simplifiedPath}", out SavedMaterialOverrideEntry bySimp))
        {
            if (!string.IsNullOrEmpty(meshAndMaterialKey))
            {
                bySimp.meshAndMaterialKey = meshAndMaterialKey;
                lookup[$"KEY:{meshAndMaterialKey}"] = bySimp;
            }
            if (!string.IsNullOrEmpty(origGuid))
                bySimp.originalMaterialGuid = origGuid;
            if (!string.IsNullOrEmpty(origPath))
                bySimp.originalMaterialPath = origPath;
            return bySimp;
        }

        SavedMaterialOverrideEntry created = new SavedMaterialOverrideEntry
        {
            meshAndMaterialKey = meshAndMaterialKey,
            originalMaterialGuid = origGuid,
            originalMaterialPath = origPath,
            simplifiedMaterialPath = simplifiedPath
        };

        if (db.overrides == null)
            db.overrides = new List<SavedMaterialOverrideEntry>();
        db.overrides.Add(created);

        if (!string.IsNullOrEmpty(meshAndMaterialKey))
            lookup[$"KEY:{meshAndMaterialKey}"] = created;
        if (!string.IsNullOrEmpty(simplifiedPath))
            lookup[$"SIMP:{simplifiedPath}"] = created;

        return created;
    }

    private static RendererBackupEntry GetOrCreateSceneBackupEntry(
        Renderer renderer,
        Dictionary<string, RendererBackupEntry> lookupMap,
        SceneMaterialBackupDatabase db)
    {
        string rendererGid = GetGlobalObjectIdString(renderer);
        string goGid = GetGlobalObjectIdString(renderer.gameObject);
        string scenePath = NormalizePath(renderer.gameObject.scene.path ?? "");
        string sceneName = renderer.gameObject.scene.name ?? "";
        string hierarchyPath = GetHierarchyPath(renderer.transform);
        string indexedPath = GetIndexedHierarchyPath(renderer.transform);
        int compIndex = GetRendererComponentIndex(renderer);

        string gidKey = $"GID:{rendererGid}";
        string idxKey = $"IDX:{scenePath}|{indexedPath}|{compIndex}";

        if (!string.IsNullOrEmpty(rendererGid) &&
            rendererGid != "GlobalObjectId_V1-0-00000000000000000000000000000000-0-0" &&
            lookupMap.TryGetValue(gidKey, out RendererBackupEntry existingByGid))
        {
            UpdateSceneEntryMetadata(existingByGid, scenePath, sceneName, rendererGid, goGid, hierarchyPath, indexedPath, renderer, compIndex);
            return existingByGid;
        }

        if (lookupMap.TryGetValue(idxKey, out RendererBackupEntry existingByPath))
        {
            UpdateSceneEntryMetadata(existingByPath, scenePath, sceneName, rendererGid, goGid, hierarchyPath, indexedPath, renderer, compIndex);
            return existingByPath;
        }

        RendererBackupEntry newEntry = new RendererBackupEntry();
        UpdateSceneEntryMetadata(newEntry, scenePath, sceneName, rendererGid, goGid, hierarchyPath, indexedPath, renderer, compIndex);

        db.entries.Add(newEntry);
        if (!string.IsNullOrEmpty(rendererGid))
            lookupMap[gidKey] = newEntry;
        lookupMap[idxKey] = newEntry;

        return newEntry;
    }

    private static RendererBackupEntry GetOrCreatePrefabBackupEntry(
        Renderer renderer,
        Transform prefabRoot,
        string prefabPath,
        string prefabGuid,
        Dictionary<string, RendererBackupEntry> lookupMap,
        SceneMaterialBackupDatabase db)
    {
        prefabPath = NormalizePath(prefabPath);
        string rendererGid = GetGlobalObjectIdString(renderer);
        string goGid = GetGlobalObjectIdString(renderer.gameObject);
        string hierarchyPath = GetRelativeHierarchyPath(renderer.transform, prefabRoot);
        string indexedPath = GetRelativeIndexedHierarchyPath(renderer.transform, prefabRoot);
        int compIndex = GetRendererComponentIndex(renderer);

        string gidKey = $"GID:{rendererGid}";
        string prefabKey = $"PREFAB:{prefabPath}|{indexedPath}|{compIndex}";

        if (!string.IsNullOrEmpty(rendererGid) &&
            rendererGid != "GlobalObjectId_V1-0-00000000000000000000000000000000-0-0" &&
            lookupMap.TryGetValue(gidKey, out RendererBackupEntry existingByGid))
        {
            UpdatePrefabEntryMetadata(existingByGid, prefabPath, prefabGuid, rendererGid, goGid, hierarchyPath, indexedPath, renderer, compIndex);
            return existingByGid;
        }

        if (lookupMap.TryGetValue(prefabKey, out RendererBackupEntry existingByKey))
        {
            UpdatePrefabEntryMetadata(existingByKey, prefabPath, prefabGuid, rendererGid, goGid, hierarchyPath, indexedPath, renderer, compIndex);
            return existingByKey;
        }

        RendererBackupEntry newEntry = new RendererBackupEntry();
        UpdatePrefabEntryMetadata(newEntry, prefabPath, prefabGuid, rendererGid, goGid, hierarchyPath, indexedPath, renderer, compIndex);

        db.entries.Add(newEntry);
        if (!string.IsNullOrEmpty(rendererGid))
            lookupMap[gidKey] = newEntry;
        lookupMap[prefabKey] = newEntry;

        return newEntry;
    }

    private static void UpdateSceneEntryMetadata(
        RendererBackupEntry entry,
        string scenePath,
        string sceneName,
        string rendererGid,
        string goGid,
        string hierarchyPath,
        string indexedPath,
        Renderer renderer,
        int compIndex)
    {
        entry.isPrefabAsset = false;
        entry.prefabAssetPath = "";
        entry.prefabAssetGuid = "";
        entry.scenePath = scenePath;
        entry.sceneName = sceneName;
        entry.rendererGlobalObjectId = rendererGid;
        entry.gameObjectGlobalObjectId = goGid;
        entry.hierarchyPath = hierarchyPath;
        entry.indexedHierarchyPath = indexedPath;
        entry.rendererTypeName = renderer.GetType().FullName;
        entry.rendererComponentIndex = compIndex;
    }

    private static void UpdatePrefabEntryMetadata(
        RendererBackupEntry entry,
        string prefabPath,
        string prefabGuid,
        string rendererGid,
        string goGid,
        string hierarchyPath,
        string indexedPath,
        Renderer renderer,
        int compIndex)
    {
        entry.isPrefabAsset = true;
        entry.prefabAssetPath = prefabPath;
        entry.prefabAssetGuid = prefabGuid;
        entry.scenePath = "";
        entry.sceneName = "";
        entry.rendererGlobalObjectId = rendererGid;
        entry.gameObjectGlobalObjectId = goGid;
        entry.hierarchyPath = hierarchyPath;
        entry.indexedHierarchyPath = indexedPath;
        entry.rendererTypeName = renderer.GetType().FullName;
        entry.rendererComponentIndex = compIndex;
    }

    private static void EnsureBackupSlotCount(RendererBackupEntry entry, int slotCount)
    {
        if (entry.slots == null)
            entry.slots = new List<MaterialSlotBackup>();

        while (entry.slots.Count < slotCount)
        {
            entry.slots.Add(new MaterialSlotBackup
            {
                slotIndex = entry.slots.Count
            });
        }
    }

    private static void RecordMaterialInSlotBackup(MaterialSlotBackup slotBackup, int slotIndex, Material mat)
    {
        slotBackup.slotIndex = slotIndex;
        if (mat == null)
        {
            slotBackup.wasNull = true;
            slotBackup.materialGuid = "";
            slotBackup.materialAssetPath = "";
            slotBackup.materialName = "";
            return;
        }

        string path = NormalizePath(AssetDatabase.GetAssetPath(mat));
        string guid = !string.IsNullOrEmpty(path) ? AssetDatabase.AssetPathToGUID(path) : "";

        slotBackup.wasNull = false;
        slotBackup.materialAssetPath = path;
        slotBackup.materialGuid = guid;
        slotBackup.materialName = mat.name;
    }

    private static Material LoadOriginalMaterialFromSlotBackup(MaterialSlotBackup slotBackup)
    {
        if (slotBackup == null || slotBackup.wasNull)
            return null;

        return LoadMaterialByGuidOrPath(slotBackup.materialGuid, slotBackup.materialAssetPath, slotBackup.materialName);
    }

    private static Material LoadMaterialByGuidOrPath(string guid, string assetPath, string matName = "")
    {
        if (!string.IsNullOrEmpty(guid))
        {
            string pathFromGuid = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(pathFromGuid))
            {
                Material matByGuid = AssetDatabase.LoadAssetAtPath<Material>(pathFromGuid);
                if (matByGuid != null)
                    return matByGuid;

                UnityEngine.Object[] allAtGuidPath = AssetDatabase.LoadAllAssetsAtPath(pathFromGuid);
                foreach (UnityEngine.Object obj in allAtGuidPath)
                {
                    if (obj is Material subMat && (string.IsNullOrEmpty(matName) || subMat.name == matName))
                        return subMat;
                }
            }
        }

        if (!string.IsNullOrEmpty(assetPath))
        {
            Material matByPath = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (matByPath != null)
                return matByPath;

            UnityEngine.Object[] allAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (UnityEngine.Object obj in allAtPath)
            {
                if (obj is Material subMat && (string.IsNullOrEmpty(matName) || subMat.name == matName))
                    return subMat;
            }
        }

        if (!string.IsNullOrEmpty(matName))
        {
            string[] guids = AssetDatabase.FindAssets($"{matName} t:Material");
            foreach (string g in guids)
            {
                string p = NormalizePath(AssetDatabase.GUIDToAssetPath(g));
                if (p.StartsWith(GENERATED_MATS_FOLDER, StringComparison.OrdinalIgnoreCase))
                    continue;

                Material candidate = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (candidate != null && candidate.name == matName)
                    return candidate;
            }
        }

        return null;
    }

    private static Renderer FindMatchingPrefabRenderer(RendererBackupEntry entry, out GameObject prefabRoot)
    {
        prefabRoot = null;
        string prefabPath = entry.prefabAssetPath;
        if (!string.IsNullOrEmpty(entry.prefabAssetGuid))
        {
            string pathFromGuid = AssetDatabase.GUIDToAssetPath(entry.prefabAssetGuid);
            if (!string.IsNullOrEmpty(pathFromGuid))
                prefabPath = pathFromGuid;
        }

        if (string.IsNullOrEmpty(prefabPath))
            return null;

        prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabRoot == null)
            return null;

        Renderer[] renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            string rGid = GetGlobalObjectIdString(r);
            if (!string.IsNullOrEmpty(entry.rendererGlobalObjectId) && rGid == entry.rendererGlobalObjectId)
                return r;
        }

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            string relIdxPath = GetRelativeIndexedHierarchyPath(r.transform, prefabRoot.transform);
            int compIdx = GetRendererComponentIndex(r);
            if (relIdxPath == entry.indexedHierarchyPath && compIdx == entry.rendererComponentIndex)
                return r;
        }

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            string relPath = GetRelativeHierarchyPath(r.transform, prefabRoot.transform);
            if (relPath == entry.hierarchyPath && r.GetType().FullName == entry.rendererTypeName)
                return r;
        }

        return null;
    }

    private static Renderer FindMatchingSceneRenderer(
        RendererBackupEntry entry,
        Dictionary<string, Renderer> byRendererGlobalId,
        Dictionary<string, Renderer> byGoGlobalIdAndCompIndex,
        Dictionary<string, Renderer> byIndexedPath,
        Dictionary<string, List<Renderer>> byHierarchyPath)
    {
        if (!string.IsNullOrEmpty(entry.rendererGlobalObjectId) &&
            byRendererGlobalId.TryGetValue(entry.rendererGlobalObjectId, out Renderer rByGid) &&
            rByGid != null)
        {
            return rByGid;
        }

        if (!string.IsNullOrEmpty(entry.rendererGlobalObjectId) &&
            GlobalObjectId.TryParse(entry.rendererGlobalObjectId, out GlobalObjectId parsedGid))
        {
            Renderer resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedGid) as Renderer;
            if (resolved != null && !EditorUtility.IsPersistent(resolved))
                return resolved;
        }

        if (!string.IsNullOrEmpty(entry.gameObjectGlobalObjectId))
        {
            string goKey = $"{entry.gameObjectGlobalObjectId}|{entry.rendererComponentIndex}";
            if (byGoGlobalIdAndCompIndex.TryGetValue(goKey, out Renderer rByGoGid) && rByGoGid != null)
                return rByGoGid;
        }

        string scenePrefix = !string.IsNullOrEmpty(entry.scenePath) ? entry.scenePath : entry.sceneName;
        string idxKey = $"{scenePrefix}|{entry.indexedHierarchyPath}|{entry.rendererComponentIndex}";
        if (byIndexedPath.TryGetValue(idxKey, out Renderer rByIdxPath) && rByIdxPath != null)
            return rByIdxPath;

        string plainKey = $"{scenePrefix}|{entry.hierarchyPath}|{entry.rendererTypeName}";
        if (byHierarchyPath.TryGetValue(plainKey, out List<Renderer> candidates) && candidates.Count > 0)
            return candidates[0];

        return null;
    }

    private static Color GetMaterialBaseColor(Material mat)
    {
        if (mat == null) return Color.white;
        if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
        return Color.white;
    }

    private static bool ColorsAreClose(Color a, Color b, float tolerance)
    {
        return Mathf.Abs(a.r - b.r) <= tolerance &&
               Mathf.Abs(a.g - b.g) <= tolerance &&
               Mathf.Abs(a.b - b.b) <= tolerance;
    }

    private static bool IsApproximatelyWhite(Color c)
    {
        return c.r >= 0.96f && c.g >= 0.96f && c.b >= 0.96f;
    }

    private static bool IsApproximatelyBlack(Color c)
    {
        return c.r <= 0.02f && c.g <= 0.02f && c.b <= 0.02f;
    }

    private static float GetColorSaturation(Color c)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        return max - min;
    }

    private static float GetColorLuminance(Color c)
    {
        return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }

    private static string GetGlobalObjectIdString(UnityEngine.Object obj)
    {
        if (obj == null)
            return "";

        GlobalObjectId gid = GlobalObjectId.GetGlobalObjectIdSlow(obj);
        return gid.ToString();
    }

    private static int GetRendererComponentIndex(Renderer renderer)
    {
        Renderer[] renderersOnGo = renderer.gameObject.GetComponents<Renderer>();
        for (int i = 0; i < renderersOnGo.Length; i++)
        {
            if (renderersOnGo[i] == renderer)
                return i;
        }
        return 0;
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

    private static string GetIndexedHierarchyPath(Transform current)
    {
        List<string> parts = new List<string>();
        while (current != null)
        {
            parts.Add($"{current.name}[{current.GetSiblingIndex()}]");
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string GetRelativeHierarchyPath(Transform current, Transform root)
    {
        List<string> parts = new List<string>();
        while (current != null)
        {
            parts.Add(current.name);
            if (current == root) break;
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string GetRelativeIndexedHierarchyPath(Transform current, Transform root)
    {
        List<string> parts = new List<string>();
        while (current != null)
        {
            int siblingIdx = current == root ? 0 : current.GetSiblingIndex();
            parts.Add($"{current.name}[{siblingIdx}]");
            if (current == root) break;
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/');
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            name = name.Replace(c, '_');
        }
        return name.Replace('/', '_').Replace('\\', '_');
    }

    private static void EnsureFoldersExist()
    {
        if (!AssetDatabase.IsValidFolder(ROOT_FOLDER))
        {
            AssetDatabase.CreateFolder("Assets", "WebGLSimplifiedMaterials");
        }
        if (!AssetDatabase.IsValidFolder(GENERATED_MATS_FOLDER))
        {
            AssetDatabase.CreateFolder(ROOT_FOLDER, "Materials");
        }
    }

    private static SceneMaterialBackupDatabase LoadBackupDatabase()
    {
        string fullPrimaryPath = Path.GetFullPath(BACKUP_ASSET_PATH);
        if (File.Exists(fullPrimaryPath))
        {
            try
            {
                string json = File.ReadAllText(fullPrimaryPath);
                SceneMaterialBackupDatabase db = JsonUtility.FromJson<SceneMaterialBackupDatabase>(json);
                if (db != null)
                    return db;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebGLMaterialSimplifier] Failed to read primary backup file, trying fallback. Error: {ex.Message}");
            }
        }

        string fullFallbackPath = Path.GetFullPath(BACKUP_FALLBACK_PATH);
        if (File.Exists(fullFallbackPath))
        {
            try
            {
                string json = File.ReadAllText(fullFallbackPath);
                return JsonUtility.FromJson<SceneMaterialBackupDatabase>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebGLMaterialSimplifier] Failed to read fallback backup file. Error: {ex.Message}");
            }
        }

        return null;
    }

    private static void SaveBackupDatabase(SceneMaterialBackupDatabase db)
    {
        EnsureFoldersExist();
        db.lastUpdatedTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string json = JsonUtility.ToJson(db, true);

        File.WriteAllText(Path.GetFullPath(BACKUP_ASSET_PATH), json);
        File.WriteAllText(Path.GetFullPath(BACKUP_FALLBACK_PATH), json);
    }

    private static SavedMaterialOverridesDatabase LoadOverridesDatabase()
    {
        string fullPath = Path.GetFullPath(OVERRIDES_ASSET_PATH);
        if (File.Exists(fullPath))
        {
            try
            {
                string json = File.ReadAllText(fullPath);
                return JsonUtility.FromJson<SavedMaterialOverridesDatabase>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebGLMaterialSimplifier] Could not read SavedMaterialOverrides.json: {ex.Message}");
            }
        }
        return null;
    }

    private static void SaveOverridesDatabase(SavedMaterialOverridesDatabase db)
    {
        EnsureFoldersExist();
        db.lastSavedTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string json = JsonUtility.ToJson(db, true);
        File.WriteAllText(Path.GetFullPath(OVERRIDES_ASSET_PATH), json);
    }
}
