using UnityEngine;

/// <summary>
/// Runtime & WebGL controller for the Drawn Shader ("Custom/DrawnShader").
/// Can be called from any script (DrawnShaderRuntime.SetActive(false)) or dropped onto
/// a scene GameObject to toggle the Drawn Shader off automatically on WebGL if desired.
/// Also provides a lightweight helper so dynamically spawned prefabs (like TrashItem)
/// always receive the Drawn Shader overlay material when active.
/// </summary>
public class DrawnShaderRuntime : MonoBehaviour
{
    public const string SHADER_NAME = "Custom/DrawnShader";
    private static readonly int GlobalDisabledPropId = Shader.PropertyToID("_DrawnShaderGlobalDisabled");

    [Header("Runtime & WebGL Toggle")]
    [Tooltip("If true, the Drawn Shader is active at runtime.")]
    public bool drawnShaderActive = true;

    [Tooltip("If checked, automatically disables the Drawn Shader when running in a WebGL build.")]
    public bool autoDisableOnWebGL = false;

    public static bool IsActive { get; private set; } = true;

    private static Material _cachedRuntimeMaterial;
    private static bool _searchedRuntimeMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetOnLoad()
    {
        _cachedRuntimeMaterial = null;
        _searchedRuntimeMaterial = false;
        SetActive(true);
    }

    public static void SetActive(bool active)
    {
        IsActive = active;
        Shader.SetGlobalFloat(GlobalDisabledPropId, active ? 0.0f : 1.0f);
    }

    public static void Toggle()
    {
        SetActive(!IsActive);
    }

    /// <summary>
    /// Ensures all eligible MeshRenderers/SkinnedMeshRenderers under <paramref name="root"/>
    /// have the active DrawnShader overlay material appended if the scene is using DrawnShader.
    /// Respects WebGL stripping (if no scene object uses DrawnShader, does nothing).
    /// </summary>
    public static void EnsureAppliedTo(GameObject root)
    {
        if (!IsActive || root == null)
            return;

        Material drawnMat = GetRuntimeDrawnMaterial();
        if (drawnMat == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                continue;
            if (r.gameObject.name.StartsWith("Glow_"))
                continue;

            Material[] mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0)
                continue;

            bool alreadyHas = false;
            for (int m = 0; m < mats.Length; m++)
            {
                if (IsDrawnMaterial(mats[m]))
                {
                    alreadyHas = true;
                    break;
                }
            }

            if (alreadyHas)
                continue;

            Material[] updated = new Material[mats.Length + 1];
            for (int m = 0; m < mats.Length; m++)
                updated[m] = mats[m];
            updated[mats.Length] = drawnMat;
            r.sharedMaterials = updated;
        }
    }

    private static Material GetRuntimeDrawnMaterial()
    {
        if (_cachedRuntimeMaterial != null)
            return _cachedRuntimeMaterial;
        if (_searchedRuntimeMaterial)
            return null;

        _searchedRuntimeMaterial = true;

        // Discover the DrawnShader material already referenced by active scene renderers.
        // If DrawnShader was stripped for WebGL, none will be found and spawned prefabs stay stripped too.
        Renderer[] sceneRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sceneRenderers.Length; i++)
        {
            Renderer r = sceneRenderers[i];
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            if (mats == null) continue;
            for (int m = 0; m < mats.Length; m++)
            {
                if (IsDrawnMaterial(mats[m]))
                {
                    _cachedRuntimeMaterial = mats[m];
                    return _cachedRuntimeMaterial;
                }
            }
        }

        return null;
    }

    private static bool IsDrawnMaterial(Material m)
    {
        return m != null && m.shader != null && m.shader.name == SHADER_NAME;
    }

    private void Awake()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (autoDisableOnWebGL)
        {
            drawnShaderActive = false;
        }
#endif
        SetActive(drawnShaderActive);
    }

    private void OnValidate()
    {
        SetActive(drawnShaderActive);
    }
}
