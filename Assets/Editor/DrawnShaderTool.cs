using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fast, stable Scene-wide manager, WebGL build stripper, and Editor Window for Drawn Shader ("Custom/DrawnShader").
/// - Manages Assets/Shaders/DrawnShader.mat as a drop-in additional overlay material.
/// - Includes ALL 3D objects in the scene first (zero pre-guessed exclusions); only excludes objects that
///   YOU manually delete DrawnShader from in the Inspector.
/// - Can also optionally apply to Prefabs referenced by the scene (without scanning thousands of unused
///   asset pack prefabs on disk, preventing any Editor freeze or crash).
/// - Includes a one-click "Toggle OFF for WebGL Builds" option.
/// </summary>
[InitializeOnLoad]
public class DrawnShaderTool : EditorWindow, IProcessSceneWithReport
{
    public const string SHADER_NAME = "Custom/DrawnShader";
    public const string MATERIAL_ASSET_PATH = "Assets/Shaders/DrawnShader.mat";
    public const string STATE_JSON_PATH = "Assets/Shaders/DrawnShaderState.json";

    public int callbackOrder => 10;

    [Serializable]
    public class TrackedRendererRecord
    {
        public string key;
        public string globalObjectId;
        public string scenePath;
        public string hierarchyPath;
        public string objectName;
        public int originalMaterialCount = -1;
        public string timestamp;
    }

    [Serializable]
    public class DrawnShaderStateDatabase
    {
        public bool isSceneEffectActive;
        public bool disableInWebGLBuild;
        public List<TrackedRendererRecord> managedRenderers = new List<TrackedRendererRecord>();
        public List<TrackedRendererRecord> excludedRenderers = new List<TrackedRendererRecord>();
    }

    private static DrawnShaderStateDatabase _db;
    private static readonly Dictionary<string, TrackedRendererRecord> _managedByKey = new Dictionary<string, TrackedRendererRecord>();
    private static readonly Dictionary<string, TrackedRendererRecord> _excludedByKey = new Dictionary<string, TrackedRendererRecord>();
    private static readonly Dictionary<int, Material[]> _originalBaseMaterialsCache = new Dictionary<int, Material[]>();

    private static Material _cachedDrawnMaterial;
    private static bool _isPerformingBulkOperation;
    private static double _nextSelectionPollTime;

    private Vector2 _windowScrollPos;
    private Vector2 _excludedScrollPos;
    private Editor _embeddedMaterialEditor;
    private bool _showMaterialSettings = true;
    private bool _showExcludedList = true;
    private string _excludeSearchFilter = "";

    static DrawnShaderTool()
    {
        EditorApplication.delayCall += InitializeToolOnLoad;
        ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
        Selection.selectionChanged += OnSelectionChanged;
        EditorApplication.update += OnEditorUpdate;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void InitializeToolOnLoad()
    {
        LoadDatabase();
        EnsureDrawnMaterialExists(logCreation: false);
        Shader.SetGlobalFloat("_DrawnShaderGlobalDisabled", 0.0f);
        CacheOriginalMaterialsInLoadedScenes();
    }

    // ========================================================================
    // WebGL Build Hook (IProcessSceneWithReport)
    // ========================================================================

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report == null)
            return;

        LoadDatabase();
        if (!_db.disableInWebGLBuild)
            return;

        if (report.summary.platform != BuildTarget.WebGL)
            return;

        int strippedCount = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int r = 0; r < roots.Length; r++)
        {
            Renderer[] renderers = roots[r].GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer ren = renderers[i];
                if (ren == null || !RendererHasDrawnMaterial(ren))
                    continue;

                Material[] mats = ren.sharedMaterials;
                List<Material> kept = new List<Material>(mats.Length);
                for (int m = 0; m < mats.Length; m++)
                {
                    if (!IsDrawnMaterial(mats[m]))
                        kept.Add(mats[m]);
                }
                ren.sharedMaterials = kept.ToArray();
                strippedCount++;
            }
        }

        if (strippedCount > 0)
        {
            Debug.Log($"[DrawnShaderTool] WebGL Build Mode: Stripped DrawnShader from {strippedCount} objects in scene '{scene.name}'.");
        }
    }

    // ========================================================================
    // Menu Items
    // ========================================================================

    [MenuItem("Tools/Drawn Shader/Open Drawn Shader Manager...", priority = 100)]
    public static void OpenWindow()
    {
        DrawnShaderTool window = GetWindow<DrawnShaderTool>("Drawn Shader");
        window.minSize = new Vector2(460f, 580f);
        window.Show();
    }

    [MenuItem("Tools/Drawn Shader/Set ACTIVE on All Scene Objects", priority = 120)]
    public static void MenuSetActiveAllSceneObjects()
    {
        SetActiveAllSceneObjects();
    }

    [MenuItem("Tools/Drawn Shader/Set INACTIVE on All Scene Objects", priority = 121)]
    public static void MenuSetInactiveAllSceneObjects()
    {
        SetInactiveAllSceneObjects();
    }

    [MenuItem("Tools/Drawn Shader/Toggle Disable in WebGL Builds", priority = 130)]
    public static void MenuToggleWebGLDisable()
    {
        LoadDatabase();
        _db.disableInWebGLBuild = !_db.disableInWebGLBuild;
        SaveDatabase();
        RepaintOpenWindows();
        Debug.Log($"[DrawnShaderTool] Auto-Disable Drawn Shader in WebGL Builds is now: {(_db.disableInWebGLBuild ? "ON" : "OFF")}");
    }

    [MenuItem("Tools/Drawn Shader/Exclude Selected Object(s)", priority = 140)]
    public static void MenuExcludeSelectedObjects()
    {
        ExcludeSelectedObjects();
    }

    [MenuItem("Tools/Drawn Shader/Re-Include Selected Object(s)", priority = 141)]
    public static void MenuReIncludeSelectedObjects()
    {
        ReIncludeSelectedObjects();
    }

    // ========================================================================
    // Material Asset Creation & Default Preset
    // ========================================================================

    public static Material EnsureDrawnMaterialExists(bool logCreation = true)
    {
        if (_cachedDrawnMaterial != null)
            return _cachedDrawnMaterial;

        _cachedDrawnMaterial = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_ASSET_PATH);
        Shader shader = Shader.Find(SHADER_NAME);
        if (shader == null)
        {
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/DrawnShader.shader");
        }

        if (_cachedDrawnMaterial != null)
        {
            if (shader != null && _cachedDrawnMaterial.shader != shader)
            {
                _cachedDrawnMaterial.shader = shader;
                EditorUtility.SetDirty(_cachedDrawnMaterial);
                AssetDatabase.SaveAssets();
            }
            return _cachedDrawnMaterial;
        }

        if (shader == null)
            return null;

        string folder = Path.GetDirectoryName(MATERIAL_ASSET_PATH)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets", "Shaders");
        }

        Material mat = new Material(shader)
        {
            name = "DrawnShader"
        };

        ApplyDefaultDrawnPreset(mat);

        AssetDatabase.CreateAsset(mat, MATERIAL_ASSET_PATH);
        AssetDatabase.SaveAssets();
        _cachedDrawnMaterial = mat;

        if (logCreation)
        {
            Debug.Log($"[DrawnShaderTool] Created Drawn Shader material at '{MATERIAL_ASSET_PATH}'.");
        }

        return _cachedDrawnMaterial;
    }

    public static void ApplyDefaultDrawnPreset(Material mat)
    {
        if (mat == null) return;

        // 1. Overall Drawn Effect
        mat.SetFloat("_EffectEnabled", 1.0f);
        mat.SetFloat("_EffectIntensity", 1.15f);

        // 2. Seamless World Triplanar Brush Mapping
        mat.SetFloat("_Stroke3DScale", 0.72f);
        mat.SetFloat("_TriplanarSharpness", 4.5f);
        mat.SetFloat("_BrushAspect", 1.65f);
        mat.SetFloat("_BrushFlowSweep", 0.42f);
        mat.SetVector("_MappingScale", new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        mat.SetVector("_MappingLocation", Vector4.zero);

        // 3. Blender Node Graph: 3D fBM Noise & Linear Light
        mat.SetFloat("_NoiseScale", 4.8f);
        mat.SetFloat("_NoiseDetail", 2.0f);
        mat.SetFloat("_NoiseRoughness", 0.52f);
        mat.SetFloat("_NoiseLacunarity", 2.0f);
        mat.SetFloat("_LinearLightFactor", 0.22f);
        mat.SetFloat("_FlatSurfaceSpread", 0.42f);

        // 4. Blender Node Graph: Minkowski Voronoi -> Position -> Normal
        mat.SetFloat("_VoronoiScale", 7.4f);
        mat.SetFloat("_VoronoiExponent", 0.5f);
        mat.SetFloat("_VoronoiRandomness", 1.0f);
        mat.SetFloat("_PainterlyNormalStrength", 1.85f);

        // 5. Painted Light Color Switches & Gouache Impasto
        mat.SetFloat("_PaintedLightStrength", 1.12f);
        mat.SetFloat("_SmoothFlattenStrength", 0.65f);
        mat.SetFloat("_LightSteps", 4.0f);
        mat.SetFloat("_LightStepSharpness", 14.0f);
        mat.SetFloat("_Roughness", 0.48f);
        mat.SetFloat("_PaintedSpecularStrength", 0.85f);
        mat.SetFloat("_FresnelRimStrength", 0.68f);
        mat.SetFloat("_BrushOverlapImpasto", 0.22f);
        mat.SetFloat("_PerStrokeVariation", 0.18f);
        mat.SetFloat("_WarmCoolShift", 0.24f);
        mat.SetFloat("_ShadowProtection", 0.65f);

        EditorUtility.SetDirty(mat);
    }

    // ========================================================================
    // Persistent State Database
    // ========================================================================

    private static void LoadDatabase()
    {
        if (_db != null)
            return;

        if (File.Exists(STATE_JSON_PATH))
        {
            try
            {
                string json = File.ReadAllText(STATE_JSON_PATH);
                _db = JsonUtility.FromJson<DrawnShaderStateDatabase>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DrawnShaderTool] Failed to read state database, creating fresh state: {e.Message}");
            }
        }

        if (_db == null)
        {
            _db = new DrawnShaderStateDatabase();
        }

        RebuildLookupDictionaries();
    }

    private static void RebuildLookupDictionaries()
    {
        _managedByKey.Clear();
        _excludedByKey.Clear();

        if (_db.managedRenderers == null)
            _db.managedRenderers = new List<TrackedRendererRecord>();
        if (_db.excludedRenderers == null)
            _db.excludedRenderers = new List<TrackedRendererRecord>();

        for (int i = _db.managedRenderers.Count - 1; i >= 0; i--)
        {
            TrackedRendererRecord rec = _db.managedRenderers[i];
            if (rec == null || string.IsNullOrEmpty(rec.key))
            {
                _db.managedRenderers.RemoveAt(i);
                continue;
            }
            _managedByKey[rec.key] = rec;
        }

        for (int i = _db.excludedRenderers.Count - 1; i >= 0; i--)
        {
            TrackedRendererRecord rec = _db.excludedRenderers[i];
            if (rec == null || string.IsNullOrEmpty(rec.key))
            {
                _db.excludedRenderers.RemoveAt(i);
                continue;
            }
            _excludedByKey[rec.key] = rec;
        }
    }

    private static void SaveDatabase()
    {
        if (_db == null)
            return;

        _db.managedRenderers = new List<TrackedRendererRecord>(_managedByKey.Values);
        _db.excludedRenderers = new List<TrackedRendererRecord>(_excludedByKey.Values);

        try
        {
            string dir = Path.GetDirectoryName(STATE_JSON_PATH);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonUtility.ToJson(_db, true);
            File.WriteAllText(STATE_JSON_PATH, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DrawnShaderTool] Could not save state database: {e.Message}");
        }
    }

    // ========================================================================
    // Fast Scene Renderer Identification (Never Scans Thousands of Disk Prefabs!)
    // ========================================================================

    private static void CacheOriginalMaterialsInLoadedScenes()
    {
        List<Renderer> allRenderers = CollectAllEligibleSceneRenderers();
        for (int i = 0; i < allRenderers.Count; i++)
        {
            CacheOriginalBaseMaterialsForRenderer(allRenderers[i]);
        }
    }

    private static void CacheOriginalBaseMaterialsForRenderer(Renderer r)
    {
        if (r == null)
            return;

        Material[] mats = r.sharedMaterials;
        if (mats == null || mats.Length == 0)
            return;

        List<Material> baseMats = new List<Material>(mats.Length);
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null && !IsDrawnMaterial(mats[i]))
            {
                baseMats.Add(mats[i]);
            }
        }

        if (baseMats.Count > 0)
        {
            _originalBaseMaterialsCache[r.GetInstanceID()] = baseMats.ToArray();
        }
    }

    private static readonly string[] GameplayPrefabFolders =
    {
        "Assets/Prefabs",
        "Assets/Scripts/TrashSystem"
    };

    private static bool IsEligibleSceneRenderer(Renderer r)
    {
        return IsEligibleRenderer(r, allowPrefabAsset: false);
    }

    private static bool IsEligibleRenderer(Renderer r, bool allowPrefabAsset)
    {
        if (r == null || r.gameObject == null)
            return false;

        if (!(r is MeshRenderer || r is SkinnedMeshRenderer))
            return false;

        bool isPersistentAsset = EditorUtility.IsPersistent(r);
        if (isPersistentAsset && !allowPrefabAsset)
            return false;

        if (!isPersistentAsset)
        {
            Scene scene = r.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return false;
        }

        // Only skip runtime glow helper clones spawned by InteractVisualizer
        if (r.gameObject.name.StartsWith("Glow_", StringComparison.Ordinal))
            return false;

        if (r is MeshRenderer mr)
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                return false;
        }
        else if (r is SkinnedMeshRenderer smr)
        {
            if (smr.sharedMesh == null)
                return false;
        }

        return true;
    }

    private static List<Renderer> CollectGameplayPrefabRenderers()
    {
        List<Renderer> result = new List<Renderer>();
        List<string> validFolders = new List<string>();
        for (int i = 0; i < GameplayPrefabFolders.Length; i++)
        {
            if (AssetDatabase.IsValidFolder(GameplayPrefabFolders[i]))
                validFolders.Add(GameplayPrefabFolders[i]);
        }

        if (validFolders.Count == 0)
            return result;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", validFolders.ToArray());
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path))
                continue;

            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabRoot == null)
                continue;

            Renderer[] renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < renderers.Length; j++)
            {
                if (IsEligibleRenderer(renderers[j], allowPrefabAsset: true))
                    result.Add(renderers[j]);
            }
        }

        return result;
    }

    private static string GetHierarchyPath(Transform tr)
    {
        List<string> parts = new List<string>();
        Transform current = tr;
        while (current != null)
        {
            int siblingIdx = current.GetSiblingIndex();
            parts.Add($"{current.name}[{siblingIdx}]");
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void GetRendererIdentifiers(
        Renderer r,
        out string primaryKey,
        out string globalIdStr,
        out string hierarchyFallbackKey,
        out string scenePath,
        out string hierarchyPath)
    {
        if (EditorUtility.IsPersistent(r))
        {
            scenePath = AssetDatabase.GetAssetPath(r);
        }
        else
        {
            scenePath = r.gameObject.scene.path;
            if (string.IsNullOrEmpty(scenePath))
                scenePath = r.gameObject.scene.name;
        }

        hierarchyPath = GetHierarchyPath(r.transform);
        hierarchyFallbackKey = $"{scenePath}::{hierarchyPath}::{r.GetType().Name}";

        primaryKey = hierarchyFallbackKey;
        globalIdStr = "";
    }

    private static TrackedRendererRecord FindRecordInDict(Dictionary<string, TrackedRendererRecord> dict, Renderer r)
    {
        GetRendererIdentifiers(r, out string primaryKey, out _, out string hierarchyFallbackKey, out string scenePath, out string hierarchyPath);

        if (dict.TryGetValue(primaryKey, out TrackedRendererRecord rec))
            return rec;
        if (dict.TryGetValue(hierarchyFallbackKey, out rec))
            return rec;

        foreach (var kvp in dict)
        {
            if (kvp.Value.scenePath == scenePath && kvp.Value.hierarchyPath == hierarchyPath)
                return kvp.Value;
        }

        return null;
    }

    private static TrackedRendererRecord CreateRecordForRenderer(Renderer r, int originalMatCount = -1)
    {
        GetRendererIdentifiers(r, out string primaryKey, out string globalIdStr, out _, out string scenePath, out string hierarchyPath);
        return new TrackedRendererRecord
        {
            key = primaryKey,
            globalObjectId = globalIdStr,
            scenePath = scenePath,
            hierarchyPath = hierarchyPath,
            objectName = r.gameObject.name,
            originalMaterialCount = originalMatCount,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private static bool IsDrawnMaterial(Material m)
    {
        if (m == null)
            return false;
        if (_cachedDrawnMaterial != null && m == _cachedDrawnMaterial)
            return true;
        if (m.shader != null && (m.shader.name == SHADER_NAME || m.shader.name == "Custom/IwaacStylizedPainterly"))
            return true;
        return false;
    }

    private static bool RendererHasDrawnMaterial(Renderer r)
    {
        if (r == null)
            return false;
        Material[] mats = r.sharedMaterials;
        if (mats == null)
            return false;

        for (int i = 0; i < mats.Length; i++)
        {
            if (IsDrawnMaterial(mats[i]))
                return true;
        }
        return false;
    }

    // ========================================================================
    // Live Detection of Manual Deletions
    // ========================================================================

    private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
    {
        if (_isPerformingBulkOperation || Application.isPlaying)
            return;

        LoadDatabase();
        bool stateChanged = false;

        for (int i = 0; i < stream.length; i++)
        {
            ObjectChangeKind kind = stream.GetEventType(i);
            if (kind == ObjectChangeKind.ChangeGameObjectOrComponentProperties)
            {
                stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs data);
                UnityEngine.Object obj = EditorUtility.InstanceIDToObject(data.instanceId);
                if (obj is Renderer r)
                {
                    if (InspectSingleRendererForManualChange(r))
                        stateChanged = true;
                }
                else if (obj is GameObject go)
                {
                    Renderer[] renderers = go.GetComponents<Renderer>();
                    foreach (Renderer gr in renderers)
                    {
                        if (InspectSingleRendererForManualChange(gr))
                            stateChanged = true;
                    }
                }
            }
        }

        if (stateChanged)
        {
            SaveDatabase();
            RepaintOpenWindows();
        }
    }

    private static void OnSelectionChanged()
    {
        if (_isPerformingBulkOperation || Application.isPlaying)
            return;
        PollSelectedObjectsForManualChanges();
    }

    private static void OnEditorUpdate()
    {
        if (_isPerformingBulkOperation || Application.isPlaying)
            return;

        double now = EditorApplication.timeSinceStartup;
        if (now < _nextSelectionPollTime)
            return;

        _nextSelectionPollTime = now + 0.45;
        PollSelectedObjectsForManualChanges();
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (_isPerformingBulkOperation || Application.isPlaying)
            return;
        CacheOriginalMaterialsInLoadedScenes();
    }

    private static void PollSelectedObjectsForManualChanges()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
            return;

        LoadDatabase();
        bool changed = false;

        for (int i = 0; i < selected.Length; i++)
        {
            if (selected[i] == null)
                continue;
            Renderer[] renderers = selected[i].GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < renderers.Length; j++)
            {
                if (InspectSingleRendererForManualChange(renderers[j]))
                    changed = true;
            }
        }

        if (changed)
        {
            SaveDatabase();
            RepaintOpenWindows();
        }
    }

    private static bool InspectSingleRendererForManualChange(Renderer r)
    {
        if (!IsEligibleRenderer(r, allowPrefabAsset: true))
            return false;

        bool currentlyHasMat = RendererHasDrawnMaterial(r);

        // Protection against 3D Scene View drag-and-drop overwriting Slot 0
        if (currentlyHasMat)
        {
            Material[] currentMats = r.sharedMaterials;
            bool hasNonDrawnBaseMat = false;
            Material drawnMatInstance = null;

            for (int i = 0; i < currentMats.Length; i++)
            {
                if (IsDrawnMaterial(currentMats[i]))
                    drawnMatInstance = currentMats[i];
                else if (currentMats[i] != null)
                    hasNonDrawnBaseMat = true;
            }

            if (!hasNonDrawnBaseMat && drawnMatInstance != null)
            {
                if (_originalBaseMaterialsCache.TryGetValue(r.GetInstanceID(), out Material[] cachedBase) && cachedBase != null && cachedBase.Length > 0)
                {
                    Material[] fixedMats = new Material[cachedBase.Length + 1];
                    Array.Copy(cachedBase, fixedMats, cachedBase.Length);
                    fixedMats[cachedBase.Length] = drawnMatInstance;
                    r.sharedMaterials = fixedMats;
                    EditorUtility.SetDirty(r);
                }
            }
            else if (hasNonDrawnBaseMat)
            {
                CacheOriginalBaseMaterialsForRenderer(r);
            }
        }
        else
        {
            CacheOriginalBaseMaterialsForRenderer(r);
        }

        TrackedRendererRecord managedRec = FindRecordInDict(_managedByKey, r);
        TrackedRendererRecord excludedRec = FindRecordInDict(_excludedByKey, r);

        if (managedRec != null && !currentlyHasMat)
        {
            CleanupTrailingNullSlotAfterManualDelete(r, managedRec.originalMaterialCount);
            _managedByKey.Remove(managedRec.key);

            TrackedRendererRecord ex = CreateRecordForRenderer(r, managedRec.originalMaterialCount);
            _excludedByKey[ex.key] = ex;

            Debug.Log($"[DrawnShaderTool] Remembered manual deletion of DrawnShader from '{r.gameObject.name}'. It will remain excluded when toggling.");
            return true;
        }
        else if (managedRec == null && currentlyHasMat)
        {
            int origCount = Mathf.Max(1, r.sharedMaterials.Length - 1);
            TrackedRendererRecord newManaged = CreateRecordForRenderer(r, origCount);
            _managedByKey[newManaged.key] = newManaged;

            if (excludedRec != null)
            {
                _excludedByKey.Remove(excludedRec.key);
                Debug.Log($"[DrawnShaderTool] Detected manual assignment of DrawnShader onto previously excluded object '{r.gameObject.name}' — automatically re-included!");
            }
            return true;
        }

        return false;
    }

    private static void CleanupTrailingNullSlotAfterManualDelete(Renderer r, int originalMaterialCount)
    {
        if (r == null || originalMaterialCount <= 0)
            return;

        Material[] mats = r.sharedMaterials;
        if (mats != null && mats.Length > originalMaterialCount && mats[mats.Length - 1] == null)
        {
            List<Material> trimmed = new List<Material>(mats);
            while (trimmed.Count > originalMaterialCount && trimmed[trimmed.Count - 1] == null)
            {
                trimmed.RemoveAt(trimmed.Count - 1);
            }
            r.sharedMaterials = trimmed.ToArray();
            EditorUtility.SetDirty(r);
        }
    }

    public static void SyncManualChangesInLoadedScenes(bool logChanges = true)
    {
        LoadDatabase();
        List<Renderer> allRenderers = CollectAllEligibleSceneRenderers();
        allRenderers.AddRange(CollectGameplayPrefabRenderers());

        bool changed = false;
        for (int i = 0; i < allRenderers.Count; i++)
        {
            if (InspectSingleRendererForManualChange(allRenderers[i]))
                changed = true;
        }

        if (changed)
        {
            SaveDatabase();
            RepaintOpenWindows();
        }
    }

    private static List<Renderer> CollectAllEligibleSceneRenderers()
    {
        List<Renderer> result = new List<Renderer>();
        int sceneCount = SceneManager.sceneCount;

        for (int s = 0; s < sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Renderer[] renderers = roots[r].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (IsEligibleSceneRenderer(renderers[i]))
                    {
                        result.Add(renderers[i]);
                    }
                }
            }
        }

        return result;
    }

    // ========================================================================
    // Instant Scene-Wide Set Active / Set Inactive Operations (< 0.05s)
    // ========================================================================

    public static void SetActiveAllSceneObjects()
    {
        LoadDatabase();
        SyncManualChangesInLoadedScenes(logChanges: true);

        Material drawnMat = EnsureDrawnMaterialExists(logCreation: true);
        if (drawnMat == null)
        {
            Debug.LogError($"[DrawnShaderTool] Could not find or create '{MATERIAL_ASSET_PATH}' with shader '{SHADER_NAME}'.");
            return;
        }

        Shader.SetGlobalFloat("_DrawnShaderGlobalDisabled", 0.0f);

        _isPerformingBulkOperation = true;

        int appliedSceneCount = 0;
        int alreadyActiveCount = 0;
        int skippedExcludedCount = 0;

        try
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Active Drawn Shader on Scene");

            List<Renderer> allRenderers = CollectAllEligibleSceneRenderers();
            allRenderers.AddRange(CollectGameplayPrefabRenderers());
            bool modifiedPrefabs = false;

            for (int i = 0; i < allRenderers.Count; i++)
            {
                Renderer r = allRenderers[i];
                CacheOriginalBaseMaterialsForRenderer(r);

                TrackedRendererRecord excludedRec = FindRecordInDict(_excludedByKey, r);
                if (excludedRec != null)
                {
                    if (RendererHasDrawnMaterial(r))
                    {
                        RemoveDrawnMaterialFromRenderer(r);
                        if (EditorUtility.IsPersistent(r))
                            modifiedPrefabs = true;
                    }
                    skippedExcludedCount++;
                    continue;
                }

                if (RendererHasDrawnMaterial(r))
                {
                    alreadyActiveCount++;
                    if (FindRecordInDict(_managedByKey, r) == null)
                    {
                        TrackedRendererRecord rec = CreateRecordForRenderer(r, Mathf.Max(1, r.sharedMaterials.Length - 1));
                        _managedByKey[rec.key] = rec;
                    }
                    continue;
                }

                Material[] currentMats = r.sharedMaterials;
                int origCount = currentMats != null ? currentMats.Length : 0;
                if (origCount == 0)
                    continue;

                Undo.RecordObject(r, "Add Drawn Shader Material");

                Material[] newMats = new Material[origCount + 1];
                for (int m = 0; m < origCount; m++)
                {
                    newMats[m] = currentMats[m];
                }
                newMats[origCount] = drawnMat;

                r.sharedMaterials = newMats;
                EditorUtility.SetDirty(r);
                if (EditorUtility.IsPersistent(r))
                    modifiedPrefabs = true;

                TrackedRendererRecord managedRec = CreateRecordForRenderer(r, origCount);
                _managedByKey[managedRec.key] = managedRec;
                appliedSceneCount++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            _db.isSceneEffectActive = true;
            SaveDatabase();
            if (modifiedPrefabs)
                AssetDatabase.SaveAssets();
            MarkLoadedScenesDirtyIfModified(appliedSceneCount > 0);
        }
        finally
        {
            _isPerformingBulkOperation = false;
            RepaintOpenWindows();
            SceneView.RepaintAll();
        }

        Debug.Log($"[DrawnShaderTool] Set ACTIVE complete! Applied DrawnShader to {appliedSceneCount} scene objects ({alreadyActiveCount} already active, {skippedExcludedCount} skipped due to your remembered manual deletions).");
    }

    public static void SetInactiveAllSceneObjects()
    {
        LoadDatabase();
        SyncManualChangesInLoadedScenes(logChanges: true);

        _isPerformingBulkOperation = true;
        int removedSceneCount = 0;

        try
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Inactive Drawn Shader on Scene");

            List<Renderer> allRenderers = CollectAllEligibleSceneRenderers();
            allRenderers.AddRange(CollectGameplayPrefabRenderers());
            bool modifiedPrefabs = false;

            for (int i = 0; i < allRenderers.Count; i++)
            {
                Renderer r = allRenderers[i];
                if (!RendererHasDrawnMaterial(r))
                {
                    TrackedRendererRecord staleRec = FindRecordInDict(_managedByKey, r);
                    if (staleRec != null)
                        _managedByKey.Remove(staleRec.key);
                    continue;
                }

                RemoveDrawnMaterialFromRenderer(r);
                if (EditorUtility.IsPersistent(r))
                    modifiedPrefabs = true;
                removedSceneCount++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            _db.isSceneEffectActive = false;
            SaveDatabase();
            if (modifiedPrefabs)
                AssetDatabase.SaveAssets();
            MarkLoadedScenesDirtyIfModified(removedSceneCount > 0);
        }
        finally
        {
            _isPerformingBulkOperation = false;
            RepaintOpenWindows();
            SceneView.RepaintAll();
        }

        Debug.Log($"[DrawnShaderTool] Set INACTIVE complete! Removed DrawnShader from {removedSceneCount} scene objects ({_excludedByKey.Count} manually removed objects remain remembered).");
    }

    private static void RemoveDrawnMaterialFromRenderer(Renderer r)
    {
        Material[] currentMats = r.sharedMaterials;
        List<Material> keptMats = new List<Material>(currentMats.Length);
        for (int m = 0; m < currentMats.Length; m++)
        {
            if (!IsDrawnMaterial(currentMats[m]))
            {
                keptMats.Add(currentMats[m]);
            }
        }

        if (keptMats.Count == 0 && _originalBaseMaterialsCache.TryGetValue(r.GetInstanceID(), out Material[] cachedBase) && cachedBase != null)
        {
            keptMats.AddRange(cachedBase);
        }

        Undo.RecordObject(r, "Remove Drawn Shader Material");
        r.sharedMaterials = keptMats.ToArray();
        EditorUtility.SetDirty(r);

        TrackedRendererRecord managedRec = FindRecordInDict(_managedByKey, r);
        if (managedRec != null)
        {
            _managedByKey.Remove(managedRec.key);
        }
    }

    public static void ExcludeSelectedObjects()
    {
        LoadDatabase();
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[DrawnShaderTool] No GameObjects selected to exclude.");
            return;
        }

        _isPerformingBulkOperation = true;
        int excludedCount = 0;

        try
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Exclude Objects from Drawn Shader");

            for (int i = 0; i < selected.Length; i++)
            {
                Renderer[] renderers = selected[i].GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    Renderer r = renderers[j];
                    if (!IsEligibleSceneRenderer(r))
                        continue;

                    if (RendererHasDrawnMaterial(r))
                    {
                        RemoveDrawnMaterialFromRenderer(r);
                    }

                    TrackedRendererRecord managedRec = FindRecordInDict(_managedByKey, r);
                    if (managedRec != null)
                        _managedByKey.Remove(managedRec.key);

                    if (FindRecordInDict(_excludedByKey, r) == null)
                    {
                        TrackedRendererRecord ex = CreateRecordForRenderer(r, Mathf.Max(1, r.sharedMaterials.Length));
                        _excludedByKey[ex.key] = ex;
                        excludedCount++;
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            SaveDatabase();
            MarkLoadedScenesDirtyIfModified(excludedCount > 0);
        }
        finally
        {
            _isPerformingBulkOperation = false;
            RepaintOpenWindows();
            SceneView.RepaintAll();
        }

        Debug.Log($"[DrawnShaderTool] Excluded {excludedCount} selected Renderer(s).");
    }

    public static void ReIncludeSelectedObjects()
    {
        LoadDatabase();
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[DrawnShaderTool] No GameObjects selected to re-include.");
            return;
        }

        Material drawnMat = EnsureDrawnMaterialExists(logCreation: false);
        _isPerformingBulkOperation = true;
        int restoredCount = 0;

        try
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Re-Include Objects in Drawn Shader");

            for (int i = 0; i < selected.Length; i++)
            {
                Renderer[] renderers = selected[i].GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    Renderer r = renderers[j];
                    if (!IsEligibleSceneRenderer(r))
                        continue;

                    TrackedRendererRecord exRec = FindRecordInDict(_excludedByKey, r);
                    if (exRec != null)
                    {
                        _excludedByKey.Remove(exRec.key);
                        restoredCount++;
                    }

                    if (_db.isSceneEffectActive && drawnMat != null && !RendererHasDrawnMaterial(r))
                    {
                        Material[] currentMats = r.sharedMaterials;
                        int origCount = currentMats != null ? currentMats.Length : 0;
                        if (origCount > 0)
                        {
                            Undo.RecordObject(r, "Re-Include Drawn Shader Material");
                            Material[] newMats = new Material[origCount + 1];
                            Array.Copy(currentMats, newMats, origCount);
                            newMats[origCount] = drawnMat;
                            r.sharedMaterials = newMats;
                            EditorUtility.SetDirty(r);

                            TrackedRendererRecord managedRec = CreateRecordForRenderer(r, origCount);
                            _managedByKey[managedRec.key] = managedRec;
                        }
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            SaveDatabase();
            MarkLoadedScenesDirtyIfModified(restoredCount > 0);
        }
        finally
        {
            _isPerformingBulkOperation = false;
            RepaintOpenWindows();
            SceneView.RepaintAll();
        }

        Debug.Log($"[DrawnShaderTool] Re-included {restoredCount} Renderer(s).");
    }

    private static void MarkLoadedScenesDirtyIfModified(bool modified)
    {
        if (!modified || Application.isPlaying)
            return;
        EditorSceneManager.MarkAllScenesDirty();
    }

    private static void RepaintOpenWindows()
    {
        DrawnShaderTool[] windows = Resources.FindObjectsOfTypeAll<DrawnShaderTool>();
        for (int i = 0; i < windows.Length; i++)
        {
            if (windows[i] != null)
                windows[i].Repaint();
        }
    }

    // ========================================================================
    // Editor Window UI
    // ========================================================================

    private void OnEnable()
    {
        LoadDatabase();
        EnsureDrawnMaterialExists(logCreation: false);
        CacheOriginalMaterialsInLoadedScenes();
    }

    private void OnDisable()
    {
        if (_embeddedMaterialEditor != null)
        {
            DestroyImmediate(_embeddedMaterialEditor);
            _embeddedMaterialEditor = null;
        }
    }

    private void OnGUI()
    {
        LoadDatabase();
        Material drawnMat = EnsureDrawnMaterialExists(logCreation: false);

        _windowScrollPos = EditorGUILayout.BeginScrollView(_windowScrollPos);

        // Header Banner
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleLeft
        };
        EditorGUILayout.LabelField("Drawn Shader Manager (@a_iwaac Blender Node Graph)", titleStyle);
        EditorGUILayout.LabelField(
            "Fast Single-Pass 3D fBM Noise -> Linear Light -> Minkowski Voronoi Position -> Principled BSDF Normal",
            EditorStyles.miniLabel
        );
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        // Material Asset Field + Quick Ping / Reset
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Drop-In Additional Material Asset", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Drawn Shader Material", drawnMat, typeof(Material), false);
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button("Ping / Select Material", GUILayout.Width(145)))
        {
            if (drawnMat != null)
            {
                Selection.activeObject = drawnMat;
                EditorGUIUtility.PingObject(drawnMat);
            }
        }
        if (GUILayout.Button("Reset Preset", GUILayout.Width(95)))
        {
            if (drawnMat != null)
            {
                Undo.RecordObject(drawnMat, "Reset Drawn Shader Preset");
                ApplyDefaultDrawnPreset(drawnMat);
                SceneView.RepaintAll();
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // Scene-Wide Set Active / Set Inactive + WebGL Toggle
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Scene-Wide & WebGL Build Toggle", EditorStyles.boldLabel);

        string statusText = _db.isSceneEffectActive
            ? $"STATUS: ACTIVE  ({_managedByKey.Count} scene objects active | {_excludedByKey.Count} manually excluded)"
            : $"STATUS: INACTIVE  ({_managedByKey.Count} scene objects active | {_excludedByKey.Count} manually excluded)";

        GUIStyle statusStyle = new GUIStyle(EditorStyles.boldLabel);
        statusStyle.normal.textColor = _db.isSceneEffectActive
            ? new Color(0.25f, 0.85f, 0.4f)
            : new Color(0.85f, 0.65f, 0.25f);
        EditorGUILayout.LabelField(statusText, statusStyle);

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
        if (GUILayout.Button("SET ACTIVE ON ALL SCENE OBJECTS", GUILayout.Height(36)))
        {
            SetActiveAllSceneObjects();
        }

        GUI.backgroundColor = new Color(0.92f, 0.42f, 0.38f);
        if (GUILayout.Button("SET INACTIVE ON ALL SCENE OBJECTS", GUILayout.Height(36)))
        {
            SetInactiveAllSceneObjects();
        }
        GUI.backgroundColor = prevBg;

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        bool newWebGLDisable = EditorGUILayout.ToggleLeft(
            " Toggle OFF for WebGL Builds (Automatically strips DrawnShader when building for WebGL)",
            _db.disableInWebGLBuild,
            EditorStyles.boldLabel
        );
        if (EditorGUI.EndChangeCheck())
        {
            _db.disableInWebGLBuild = newWebGLDisable;
            SaveDatabase();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Sync Manual Removals"))
        {
            SyncManualChangesInLoadedScenes(logChanges: true);
        }
        if (GUILayout.Button("Exclude Selected Object(s)"))
        {
            ExcludeSelectedObjects();
        }
        if (GUILayout.Button("Re-Include Selected Object(s)"))
        {
            ReIncludeSelectedObjects();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // Remembered Manual Deletions List
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        _showExcludedList = EditorGUILayout.Foldout(
            _showExcludedList,
            $"Remembered Manual Deletions / Excluded Objects ({_excludedByKey.Count})",
            true,
            EditorStyles.foldoutHeader
        );
        if (_excludedByKey.Count > 0)
        {
            if (GUILayout.Button("Clear All Exclusions", GUILayout.Width(135)))
            {
                if (EditorUtility.DisplayDialog(
                    "Clear All Exclusions?",
                    $"Forget all {_excludedByKey.Count} remembered manual material removals so they will receive DrawnShader next time you click Set Active?",
                    "Clear Exclusions",
                    "Cancel"))
                {
                    _excludedByKey.Clear();
                    SaveDatabase();
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        if (_showExcludedList)
        {
            if (_excludedByKey.Count == 0)
            {
                EditorGUILayout.LabelField("0 objects excluded — ALL scene objects are included!", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                _excludeSearchFilter = EditorGUILayout.TextField("Filter Excluded Objects", _excludeSearchFilter);
                _excludedScrollPos = EditorGUILayout.BeginScrollView(_excludedScrollPos, GUILayout.MaxHeight(165));

                List<TrackedRendererRecord> records = new List<TrackedRendererRecord>(_excludedByKey.Values);
                for (int i = 0; i < records.Count; i++)
                {
                    TrackedRendererRecord rec = records[i];
                    if (!string.IsNullOrEmpty(_excludeSearchFilter))
                    {
                        bool matchName = rec.objectName != null && rec.objectName.IndexOf(_excludeSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        bool matchPath = rec.hierarchyPath != null && rec.hierarchyPath.IndexOf(_excludeSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!matchName && !matchPath)
                            continue;
                    }

                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    EditorGUILayout.LabelField(
                        new GUIContent(rec.objectName, $"{rec.scenePath}\n{rec.hierarchyPath}"),
                        GUILayout.MinWidth(140)
                    );
                    EditorGUILayout.LabelField(rec.hierarchyPath, EditorStyles.miniLabel);

                    if (GUILayout.Button("Find", GUILayout.Width(48)))
                    {
                        Renderer found = TryResolveRendererInLoadedScenes(rec);
                        if (found != null)
                        {
                            Selection.activeGameObject = found.gameObject;
                            EditorGUIUtility.PingObject(found.gameObject);
                        }
                    }

                    if (GUILayout.Button("Re-Include", GUILayout.Width(78)))
                    {
                        _excludedByKey.Remove(rec.key);
                        Renderer found = TryResolveRendererInLoadedScenes(rec);
                        if (found != null && _db.isSceneEffectActive && drawnMat != null && !RendererHasDrawnMaterial(found))
                        {
                            Material[] mats = found.sharedMaterials;
                            if (mats != null && mats.Length > 0)
                            {
                                Undo.RecordObject(found, "Re-Include Drawn Shader Material");
                                Material[] newMats = new Material[mats.Length + 1];
                                Array.Copy(mats, newMats, mats.Length);
                                newMats[mats.Length] = drawnMat;
                                found.sharedMaterials = newMats;
                                EditorUtility.SetDirty(found);
                                TrackedRendererRecord mRec = CreateRecordForRenderer(found, mats.Length);
                                _managedByKey[mRec.key] = mRec;
                            }
                        }
                        SaveDatabase();
                        SceneView.RepaintAll();
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // Live Embedded Shader / Material Properties Editor
        if (drawnMat != null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _showMaterialSettings = EditorGUILayout.Foldout(
                _showMaterialSettings,
                "Live Drawn Shader Fine-Tuning (Updates Material Instantly)",
                true,
                EditorStyles.foldoutHeader
            );

            if (_showMaterialSettings)
            {
                if (_embeddedMaterialEditor == null || _embeddedMaterialEditor.target != drawnMat)
                {
                    if (_embeddedMaterialEditor != null)
                        DestroyImmediate(_embeddedMaterialEditor);
                    _embeddedMaterialEditor = Editor.CreateEditor(drawnMat);
                }

                if (_embeddedMaterialEditor is MaterialEditor matEd)
                {
                    matEd.PropertiesGUI();
                }
                else if (_embeddedMaterialEditor != null)
                {
                    _embeddedMaterialEditor.OnInspectorGUI();
                }
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.EndScrollView();
    }

    private static Renderer TryResolveRendererInLoadedScenes(TrackedRendererRecord rec)
    {
        if (rec == null)
            return null;

        List<Renderer> allRenderers = CollectAllEligibleSceneRenderers();
        for (int i = 0; i < allRenderers.Count; i++)
        {
            GetRendererIdentifiers(allRenderers[i], out string pKey, out _, out string hKey, out string sPath, out string hPath);
            if (pKey == rec.key || hKey == rec.key || (sPath == rec.scenePath && hPath == rec.hierarchyPath))
                return allRenderers[i];
        }

        return null;
    }
}
