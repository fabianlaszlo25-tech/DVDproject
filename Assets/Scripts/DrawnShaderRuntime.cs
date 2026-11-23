using UnityEngine;

/// <summary>
/// Runtime & WebGL controller for the Drawn Shader ("Custom/DrawnShader").
/// Can be called from any script (DrawnShaderRuntime.SetActive(false)) or dropped onto
/// a scene GameObject to toggle the Drawn Shader off automatically on WebGL if desired.
/// </summary>
public class DrawnShaderRuntime : MonoBehaviour
{
    private static readonly int GlobalDisabledPropId = Shader.PropertyToID("_DrawnShaderGlobalDisabled");

    [Header("Runtime & WebGL Toggle")]
    [Tooltip("If true, the Drawn Shader is active at runtime.")]
    public bool drawnShaderActive = true;

    [Tooltip("If checked, automatically disables the Drawn Shader when running in a WebGL build.")]
    public bool autoDisableOnWebGL = false;

    public static bool IsActive { get; private set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetOnLoad()
    {
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
