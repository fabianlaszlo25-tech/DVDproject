using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Standalone glow for any object with MeshFilter + MeshRenderer children.
/// It never touches the object's own materials: it adds hidden child copies of the meshes
/// that render (1) a glowing fill over the whole surface and (2) a swirling outline.
/// Periodically ramps the outline up and back down with 0 inner fill brightness while idle,
/// and ramps both outline and inner fill when driven with <see cref="SetIntensity"/> (0..1).
/// </summary>
public class InteractVisualizer : MonoBehaviour
{
    private const string ShaderName = "Custom/TrashOutline";
    private const float FillThickness = 0.002f; // tiny push so the fill doesn't z-fight the surface
    private const float StencilRefValue = 173f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int ThicknessScaleId = Shader.PropertyToID("_ThicknessScale");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    private static readonly int RadialBlendId = Shader.PropertyToID("_RadialBlend");
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
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

    [Header("Response")]
    [Tooltip("How quickly the glow follows the intensity you set.")]
    public float smoothing = 10f;

    [Header("Shader")]
    [Tooltip("Leave empty to find 'Custom/TrashOutline' automatically (keep the shader file in a Resources folder).")]
    public Shader outlineShader;

    private readonly List<MeshRenderer> glowRenderers = new List<MeshRenderer>();
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

        // If releasing a hold, reset the periodic timer so it waits cleanly before the next idle glow-up.
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

    private void Start()
    {
        // Built in Start (not Awake) so other scripts that cache this object's renderers
        // in Awake never see the glow copies.
        if (outlineShader == null) outlineShader = Shader.Find(ShaderName);
        if (outlineShader == null)
        {
            Debug.LogWarning("InteractVisualizer: shader not found. Assign it or put it in a Resources folder.", this);
            enabled = false;
            return;
        }

        // Randomize the starting point in the periodic cycle and swirl phase so objects don't sync up.
        float totalCycle = Mathf.Max(0f, periodicWait) + Mathf.Max(0.1f, periodicDuration);
        periodicTimer = Random.Range(0f, totalCycle);
        swirlSeed = Random.Range(0f, Mathf.PI * 2f);

        // Fill (Queue Transparent+10): renders first, stamps Stencil = 173 across the mesh interior.
        // During idle glow its Alpha is 0 (invisible), masking see-through objects for the outline.
        fillMaterial = CreateMaterial(
            "TrashGlow Fill",
            CullMode.Back,
            -1f,
            -1f,
            0f,
            CompareFunction.Always,
            StencilOp.Replace,
            10);

        // Outline (Queue Transparent+11): renders second, only where Stencil != 173 (outside the silhouette).
        outlineMaterial = CreateMaterial(
            "TrashGlow Outline",
            CullMode.Front,
            0f,
            0f,
            radialBlend,
            CompareFunction.NotEqual,
            StencilOp.Keep,
            11);

        BuildRenderers();
    }

    private Material CreateMaterial(
        string materialName,
        CullMode cull,
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
            renderQueue = (int)RenderQueue.Transparent + queueOffset
        };
        material.SetFloat(CullId, (float)cull);
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
    }

    private void BuildRenderers()
    {
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;

            glowRenderers.Add(CreateCopy(filter, "Glow_Fill_", fillMaterial, false));
            glowRenderers.Add(CreateCopy(filter, "Glow_Outline_", outlineMaterial, true));
        }
    }

    private MeshRenderer CreateCopy(MeshFilter source, string namePrefix, Material material, bool needsCenter)
    {
        Mesh mesh = source.sharedMesh;

        GameObject copy = new GameObject(namePrefix + source.name);
        copy.layer = source.gameObject.layer;
        copy.transform.SetParent(source.transform, false);

        copy.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = copy.AddComponent<MeshRenderer>();

        // One material per submesh so the whole mesh is covered.
        Material[] materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        renderer.sharedMaterials = materials;

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        if (needsCenter)
        {
            // Each mesh has its own center, so it goes in a per-renderer property block.
            var block = new MaterialPropertyBlock();
            block.SetVector(CenterId, mesh.bounds.center);
            renderer.SetPropertyBlock(block);
        }

        renderer.enabled = false;
        return renderer;
    }

    private void Update()
    {
        if (glowRenderers.Count == 0) return;

        // 1. Smooth the hold intensity driven by TrashItem.
        currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        if (targetIntensity <= 0f && currentIntensity < 0.002f)
        {
            currentIntensity = 0f;
        }

        // 2. Run the periodic outline ramp (0 -> 1 -> 0) while idle.
        if (enablePeriodicGlow && targetIntensity <= 0f && currentIntensity <= 0f)
        {
            float wait = Mathf.Max(0f, periodicWait);
            float duration = Mathf.Max(0.1f, periodicDuration);
            float cycleLength = wait + duration;

            periodicTimer += Time.deltaTime;
            if (periodicTimer >= cycleLength) periodicTimer -= cycleLength;

            if (periodicTimer >= wait)
            {
                float t = (periodicTimer - wait) / duration; // 0..1 across the glow duration
                // Smoothly ramps 0 -> 1 -> 0 just like holding and releasing
                periodicIntensity = (0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f)) * periodicPeakIntensity;
            }
            else
            {
                periodicIntensity = 0f;
            }
        }
        else
        {
            // Smoothly blend out periodic intensity if the player grabs the item mid-pulse.
            periodicIntensity = Mathf.Lerp(periodicIntensity, 0f, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        }

        float outlineIntensity = Mathf.Max(currentIntensity, periodicIntensity);

        if (outlineIntensity < 0.002f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        ApplyMaterialValues(outlineIntensity, currentIntensity);
    }

    private void ApplyMaterialValues(float outlineIntensity, float holdIntensity)
    {
        float elapsed = Time.time - pulseStartTime;
        float swirlPhase = swirlSeed + (elapsed / Mathf.Max(0.05f, pulsePeriod)) * Mathf.PI * 2f;

        // Outline: uses outlineIntensity (driven by either periodic idle glow OR holding).
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

        // Fill: always renders when outline is visible so it stamps the stencil mask for see-through
        // objects, but its Brightness and Alpha are 0 unless the player is actively holding the item.
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

        foreach (MeshRenderer r in glowRenderers)
        {
            if (r != null) r.enabled = visible;
        }
    }
}