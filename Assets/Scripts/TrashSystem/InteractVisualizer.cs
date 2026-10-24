using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Standalone glow for any object with MeshFilter + MeshRenderer children.
/// Fixed for Windows/WebGL platform switching, Static Batching ("Combined Mesh" ghosting),
/// and LODGroup hierarchies:
/// 1. Caches original unbatched meshes in Awake() BEFORE Unity's Static Batcher merges them into
///    a scene-wide megamesh, while delaying child GameObject creation until Start() so other scripts
///    caching renderers in Awake() never see the glow copies.
/// 2. Detects LODGroups so only the active LOD0 (or currently visible LOD) glows, preventing
///    LOD1/LOD2/LOD3 meshes from stacking or floating in mid-air.
/// </summary>
public class InteractVisualizer : MonoBehaviour
{
    private const string ShaderName = "Custom/TrashOutline";
    private const float FillThickness = 0f; // 0 so vertices match exact silhouette without opening seams
    private const float StencilRefValue = 173f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int ThicknessScaleId = Shader.PropertyToID("_ThicknessScale");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    private static readonly int RadialBlendId = Shader.PropertyToID("_RadialBlend");
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int OffsetFactorId = Shader.PropertyToID("_OffsetFactor");
    private static readonly int OffsetUnitsId = Shader.PropertyToID("_OffsetUnits");
    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
    private static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");

    private static readonly int SwirlAmountId = Shader.PropertyToID("_SwirlAmount");
    private static readonly int SwirlPhaseId = Shader.PropertyToID("_SwirlPhase");
    private static readonly int SwirlBrightnessId = Shader.PropertyToID("_SwirlBrightness");
    private static readonly int SwirlThicknessMultiplierId = Shader.PropertyToID("_SwirlThicknessMultiplier");
    private static readonly int SwirlBandsId = Shader.PropertyToID("_SwirlBands");
    private static readonly int SwirlTwistId = Shader.PropertyToID("_SwirlTwist");

    [Header("Look")]
    [ColorUsage(true, true)] public Color glowColor = new Color(1f, 0.8f, 0.2f, 1f);
    [Tooltip("Outline thickness in world units.")]
    public float thickness = 0.03f;
    [Tooltip("0 = push outline along surface normals, 1 = push away from the mesh center. Raise it if hard-edged meshes show gaps.")]
    [Range(0f, 1f)] public float radialBlend = 0.5f;

    [Header("Fill Glow")]
    [Tooltip("How strongly the whole object glows at full hold intensity (0 = outline only).")]
    [Range(0f, 1f)] public float fillStrength = 0.6f;

    [Header("Periodic Idle Glow")]
    [Tooltip("When enabled, periodically glows the outline up and back down on its own with 0 inner mesh brightness.")]
    public bool enablePeriodicGlow = true;
    [Tooltip("Seconds to wait between periodic outline glow-ups.")]
    public float periodicWait = 2.0f;
    [Tooltip("Seconds for the periodic outline glow to ramp up (0 -> 1) and back down (1 -> 0).")]
    public float periodicDuration = 1.6f;
    [Tooltip("Peak outline intensity reached during the periodic idle glow (1 = same as full hold).")]
    [Range(0f, 1f)] public float periodicPeakIntensity = 1.0f;

    [Header("Outline Swirl")]
    [Tooltip("Outline brightness in the troughs between swirl bands.")]
    public float baseBrightness = 0.6f;
    [Tooltip("Outline brightness at the crest of each swirl band.")]
    public float pulseBrightness = 2.2f;
    [Tooltip("Outline thickness at the crest of each swirl band, as a multiple of the normal thickness.")]
    public float pulseThicknessMultiplier = 1.6f;
    [Tooltip("Seconds for one full rotation of the swirl.")]
    public float pulsePeriod = 0.45f;
    [Tooltip("Number of spiral bands wrapping around the outline.")]
    [Min(1f)] public float swirlBands = 2f;
    [Tooltip("How tightly the swirl twists vertically along the mesh.")]
    public float swirlTwist = 8f;

    [Header("LOD Settings")]
    [Tooltip("If true and an LODGroup is present, only builds glow meshes for LOD0 so lower LODs never double-render.")]
    public bool onlyUseLOD0WhenLODGroupPresent = true;

    [Header("Response")]
    [Tooltip("How quickly the glow follows the intensity you set.")]
    public float smoothing = 10f;

    [Header("Shader")]
    [Tooltip("Leave empty to find 'Custom/TrashOutline' automatically (keep the shader file in a Resources folder).")]
    public Shader outlineShader;

    private struct CachedSourceMesh
    {
        public MeshFilter filter;
        public MeshRenderer sourceRenderer;
        public Mesh originalMesh;
        public Vector3 localCenter;
        public int submeshCount;
    }

    private struct GlowRendererPair
    {
        public MeshRenderer sourceRenderer;
        public MeshRenderer fillRenderer;
        public MeshRenderer outlineRenderer;
    }

    private readonly List<CachedSourceMesh> preBatchCache = new List<CachedSourceMesh>();
    private readonly List<GlowRendererPair> glowPairs = new List<GlowRendererPair>();
    private readonly List<Mesh> runtimeExtractedMeshes = new List<Mesh>();

    private Material outlineMaterial;
    private Material fillMaterial;
    private float currentIntensity;
    private float targetIntensity;
    private float periodicIntensity;
    private float periodicTimer;
    private float pulseStartTime;
    private float swirlSeed;
    private bool isVisible;

    /// <summary>Sets the hold glow strength, 0 (off) to 1 (full). Smoothed over time.</summary>
    public void SetIntensity(float value)
    {
        float clamped = Mathf.Clamp01(value);

        if (targetIntensity > 0f && clamped <= 0f)
        {
            periodicTimer = 0f;
            periodicIntensity = 0f;
        }

        targetIntensity = clamped;
    }

    /// <summary>Turns the glow off instantly (e.g. right before the object is destroyed).</summary>
    public void HideImmediate()
    {
        targetIntensity = 0f;
        currentIntensity = 0f;
        periodicIntensity = 0f;
        periodicTimer = 0f;
        enablePeriodicGlow = false;
        SetVisible(false);
    }

    private void Awake()
    {
        // Cache the original unbatched meshes in Awake() BEFORE Unity's StaticBatchingUtility runs!
        // We do NOT create child GameObjects here so BuyableBoosterProp.Awake() never caches our glow renderers.
        CacheSourceMeshesBeforeStaticBatching();
    }

    private void Start()
    {
        if (outlineShader == null) outlineShader = Shader.Find(ShaderName);
        if (outlineShader == null)
        {
            Debug.LogWarning("InteractVisualizer: shader not found. Assign it or put it in a Resources folder.", this);
            enabled = false;
            return;
        }

        float totalCycle = Mathf.Max(0f, periodicWait) + Mathf.Max(0.1f, periodicDuration);
        periodicTimer = Random.Range(0f, totalCycle);
        swirlSeed = Random.Range(0f, Mathf.PI * 2f);

        fillMaterial = CreateMaterial(
            "TrashGlow Fill",
            CullMode.Back,
            1f, // ZWrite ON
            -1f,
            -1f,
            0f,
            CompareFunction.Always,
            StencilOp.Replace,
            10);

        outlineMaterial = CreateMaterial(
            "TrashGlow Outline",
            CullMode.Front,
            0f, // ZWrite OFF
            0f,
            0f,
            radialBlend,
            CompareFunction.NotEqual,
            StencilOp.Keep,
            11);

        BuildRenderersFromCache();
    }

    private void CacheSourceMeshesBeforeStaticBatching()
    {
        preBatchCache.Clear();

        // Collect allowed renderers if an LODGroup exists on this object or its children
        HashSet<Renderer> allowedLODRenderers = null;
        LODGroup[] lodGroups = GetComponentsInChildren<LODGroup>(true);
        if (lodGroups != null && lodGroups.Length > 0 && onlyUseLOD0WhenLODGroupPresent)
        {
            allowedLODRenderers = new HashSet<Renderer>();
            foreach (LODGroup group in lodGroups)
            {
                if (group == null) continue;
                LOD[] lods = group.GetLODs();
                if (lods != null && lods.Length > 0 && lods[0].renderers != null)
                {
                    foreach (Renderer r in lods[0].renderers)
                    {
                        if (r != null) allowedLODRenderers.Add(r);
                    }
                }
            }
        }

        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.gameObject.name.StartsWith("Glow_")) continue;

            MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null) continue;

            // If this hierarchy has an LODGroup, skip LOD1/LOD2/LOD3 renderers so they never spawn duplicate/offset ghosts
            if (allowedLODRenderers != null && allowedLODRenderers.Count > 0 && !allowedLODRenderers.Contains(sourceRenderer))
            {
                continue;
            }

            // Skip non-LOD0 meshes by name convention even if LODGroup component was stripped
            string objNameLower = filter.gameObject.name.ToLowerInvariant();
            if (objNameLower.Contains("_lod1") || objNameLower.Contains("_lod2") || objNameLower.Contains("_lod3") || objNameLower.Contains("_shadow"))
            {
                continue;
            }

            Mesh shared = filter.sharedMesh;
            if (shared == null) continue;

            // Guard against meshes that were already static-batched at build time ("Combined Mesh (root: scene)")
            Mesh validMesh = ResolveUnbatchedMesh(filter, sourceRenderer, shared);
            if (validMesh == null) continue;

            preBatchCache.Add(new CachedSourceMesh
            {
                filter = filter,
                sourceRenderer = sourceRenderer,
                originalMesh = validMesh,
                localCenter = validMesh.bounds.center,
                submeshCount = Mathf.Max(1, validMesh.subMeshCount)
            });
        }
    }

    /// <summary>
    /// If Unity's build-time Static Batching replaced filter.sharedMesh with "Combined Mesh (root: scene)",
    /// its vertices are in World Space and contain all static objects in the scene!
    /// This method either extracts ONLY this renderer's submesh slice and transforms it back to local space,
    /// or skips corrupt combined meshes so random ghost objects never spawn in mid-air.
    /// </summary>
    private Mesh ResolveUnbatchedMesh(MeshFilter filter, MeshRenderer renderer, Mesh shared)
    {
        if (!renderer.isPartOfStaticBatch && !shared.name.StartsWith("Combined Mesh"))
        {
            return shared;
        }

        // If the combined mesh is readable, extract ONLY this object's submesh slice and convert World -> Local space
        if (shared.isReadable && renderer.subMeshStartIndex >= 0 && renderer.subMeshStartIndex < shared.subMeshCount)
        {
            int subIndex = renderer.subMeshStartIndex;
            int[] tris = shared.GetTriangles(subIndex);
            if (tris != null && tris.Length > 0)
            {
                Vector3[] allVerts = shared.vertices;
                Vector3[] allNormals = shared.normals;

                Dictionary<int, int> indexMap = new Dictionary<int, int>(tris.Length);
                List<Vector3> localVerts = new List<Vector3>();
                List<Vector3> localNormals = new List<Vector3>();
                int[] newTris = new int[tris.Length];

                Matrix4x4 worldToLocal = filter.transform.worldToLocalMatrix;

                for (int i = 0; i < tris.Length; i++)
                {
                    int oldIdx = tris[i];
                    if (!indexMap.TryGetValue(oldIdx, out int newIdx))
                    {
                        newIdx = localVerts.Count;
                        indexMap[oldIdx] = newIdx;
                        localVerts.Add(worldToLocal.MultiplyPoint3x4(allVerts[oldIdx]));
                        if (allNormals != null && oldIdx < allNormals.Length)
                            localNormals.Add(worldToLocal.MultiplyVector(allNormals[oldIdx]).normalized);
                        else
                            localNormals.Add(Vector3.up);
                    }
                    newTris[i] = newIdx;
                }

                Mesh extracted = new Mesh
                {
                    name = "ExtractedLocal_" + filter.name
                };
                extracted.SetVertices(localVerts);
                extracted.SetNormals(localNormals);
                extracted.SetTriangles(newTris, 0);
                extracted.RecalculateBounds();
                runtimeExtractedMeshes.Add(extracted);
                return extracted;
            }
        }

        // If Unity locked the static batch megamesh as non-readable, never assign the megamesh to a new MeshRenderer
        // (doing so draws the entire room's static geometry floating in the air!).
        Debug.LogWarning($"InteractVisualizer: '{filter.name}' is marked Batching Static with a non-readable Combined Mesh. Uncheck 'Batching Static' on interactive props so their outline can render.", filter);
        return null;
    }

    private Material CreateMaterial(
        string materialName,
        CullMode cull,
        float zWrite,
        float offsetFactor,
        float offsetUnits,
        float blend,
        CompareFunction stencilComp,
        StencilOp stencilPass,
        int queueOffset)
    {
        var material = new Material(outlineShader)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Transparent + queueOffset,
            enableInstancing = false // Prevents Windows DX11/DX12 instancing buffer stomping across different meshes
        };
        material.SetFloat(CullId, (float)cull);
        material.SetFloat(ZWriteId, zWrite);
        material.SetFloat(OffsetFactorId, offsetFactor);
        material.SetFloat(OffsetUnitsId, offsetUnits);
        material.SetFloat(RadialBlendId, blend);
        material.SetFloat(StencilRefId, StencilRefValue);
        material.SetFloat(StencilCompId, (float)stencilComp);
        material.SetFloat(StencilPassId, (float)stencilPass);
        return material;
    }

    private void OnDestroy()
    {
        if (outlineMaterial != null) Destroy(outlineMaterial);
        if (fillMaterial != null) Destroy(fillMaterial);

        for (int i = 0; i < runtimeExtractedMeshes.Count; i++)
        {
            if (runtimeExtractedMeshes[i] != null) Destroy(runtimeExtractedMeshes[i]);
        }
        runtimeExtractedMeshes.Clear();
    }

    private void BuildRenderersFromCache()
    {
        if (preBatchCache.Count == 0)
        {
            CacheSourceMeshesBeforeStaticBatching();
        }

        for (int i = 0; i < preBatchCache.Count; i++)
        {
            CachedSourceMesh item = preBatchCache[i];
            if (item.filter == null || item.sourceRenderer == null || item.originalMesh == null) continue;

            int baseSort = item.sourceRenderer.sortingOrder;
            MeshRenderer fillR = CreateCopy(item, "Glow_Fill_", fillMaterial, baseSort + 1);
            MeshRenderer outlineR = CreateCopy(item, "Glow_Outline_", outlineMaterial, baseSort + 2);

            glowPairs.Add(new GlowRendererPair
            {
                sourceRenderer = item.sourceRenderer,
                fillRenderer = fillR,
                outlineRenderer = outlineR
            });
        }
    }

    private MeshRenderer CreateCopy(CachedSourceMesh item, string namePrefix, Material material, int sortingOrder)
    {
        GameObject copy = new GameObject(namePrefix + item.filter.name);
        copy.layer = item.filter.gameObject.layer;
        copy.transform.SetParent(item.filter.transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.transform.localScale = Vector3.one;

        copy.AddComponent<MeshFilter>().sharedMesh = item.originalMesh;
        MeshRenderer renderer = copy.AddComponent<MeshRenderer>();

        Material[] materials = new Material[item.submeshCount];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        renderer.sharedMaterials = materials;

        renderer.sortingLayerID = item.sourceRenderer.sortingLayerID;
        renderer.sortingOrder = sortingOrder;

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;

        var block = new MaterialPropertyBlock();
        block.SetVector(CenterId, item.localCenter);
        renderer.SetPropertyBlock(block);

        renderer.enabled = false;
        return renderer;
    }

    private void Update()
    {
        if (glowPairs.Count == 0) return;

        // 1. Smooth the hold intensity
        currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        if (targetIntensity <= 0f && currentIntensity < 0.002f)
        {
            currentIntensity = 0f;
        }

        // 2. Run the periodic outline ramp (0 -> 1 -> 0) while idle
        if (enablePeriodicGlow && targetIntensity <= 0f && currentIntensity <= 0f)
        {
            float wait = Mathf.Max(0f, periodicWait);
            float duration = Mathf.Max(0.1f, periodicDuration);
            float cycleLength = wait + duration;

            periodicTimer += Time.deltaTime;
            if (periodicTimer >= cycleLength) periodicTimer -= cycleLength;

            if (periodicTimer >= wait)
            {
                float t = (periodicTimer - wait) / duration;
                periodicIntensity = (0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f)) * periodicPeakIntensity;
            }
            else
            {
                periodicIntensity = 0f;
            }
        }
        else
        {
            periodicIntensity = Mathf.Lerp(periodicIntensity, 0f, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        }

        float outlineIntensity = Mathf.Max(currentIntensity, periodicIntensity);

        if (outlineIntensity < 0.002f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        SyncWithSourceRendererVisibility();
        ApplyMaterialValues(outlineIntensity, currentIntensity);
    }

    private void SyncWithSourceRendererVisibility()
    {
        if (!isVisible) return;

        // Ensure that if the source MeshRenderer was disabled (e.g. by BuyableBoosterProp purchase or LOD culling),
        // its corresponding glow copies never stay floating in the air!
        for (int i = 0; i < glowPairs.Count; i++)
        {
            GlowRendererPair pair = glowPairs[i];
            bool sourceActive = pair.sourceRenderer != null
                             && pair.sourceRenderer.enabled
                             && pair.sourceRenderer.gameObject.activeInHierarchy;

            if (pair.fillRenderer != null && pair.fillRenderer.enabled != sourceActive)
                pair.fillRenderer.enabled = sourceActive;

            if (pair.outlineRenderer != null && pair.outlineRenderer.enabled != sourceActive)
                pair.outlineRenderer.enabled = sourceActive;
        }
    }

    private void ApplyMaterialValues(float outlineIntensity, float holdIntensity)
    {
        float elapsed = Time.time - pulseStartTime;
        float swirlPhase = swirlSeed + (elapsed / Mathf.Max(0.05f, pulsePeriod)) * Mathf.PI * 2f;

        float growth = Mathf.Lerp(0.3f, 1f, outlineIntensity);
        outlineMaterial.SetColor(ColorId, glowColor);
        outlineMaterial.SetFloat(ThicknessId, thickness);
        outlineMaterial.SetFloat(ThicknessScaleId, growth);
        outlineMaterial.SetFloat(BrightnessId, baseBrightness);
        outlineMaterial.SetFloat(AlphaId, outlineIntensity);
        outlineMaterial.SetFloat(RadialBlendId, radialBlend);

        outlineMaterial.SetFloat(SwirlAmountId, 1f);
        outlineMaterial.SetFloat(SwirlPhaseId, swirlPhase);
        outlineMaterial.SetFloat(SwirlBrightnessId, pulseBrightness);
        outlineMaterial.SetFloat(SwirlThicknessMultiplierId, pulseThicknessMultiplier);
        outlineMaterial.SetFloat(SwirlBandsId, Mathf.Max(1f, Mathf.Round(swirlBands)));
        outlineMaterial.SetFloat(SwirlTwistId, swirlTwist);

        fillMaterial.SetColor(ColorId, glowColor);
        fillMaterial.SetFloat(ThicknessId, FillThickness);
        fillMaterial.SetFloat(ThicknessScaleId, 1f);
        fillMaterial.SetFloat(BrightnessId, holdIntensity > 0.001f ? 1f : 0f);
        fillMaterial.SetFloat(AlphaId, holdIntensity * fillStrength);
        fillMaterial.SetFloat(SwirlAmountId, 0f);
    }

    private void SetVisible(bool visible)
    {
        if (isVisible == visible) return;
        isVisible = visible;

        if (visible) pulseStartTime = Time.time;

        for (int i = 0; i < glowPairs.Count; i++)
        {
            GlowRendererPair pair = glowPairs[i];
            bool shouldEnable = visible
                             && pair.sourceRenderer != null
                             && pair.sourceRenderer.enabled
                             && pair.sourceRenderer.gameObject.activeInHierarchy;

            if (pair.fillRenderer != null) pair.fillRenderer.enabled = shouldEnable;
            if (pair.outlineRenderer != null) pair.outlineRenderer.enabled = shouldEnable;
        }
    }
}