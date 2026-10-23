using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Standalone Fullscreen Adjustable ACES Tonemapping Effect.
/// Clones Unity's exact ACES Filmic RRT+ODT tonemapping curve in a fullscreen shader pass
/// so you can smoothly dial the ACES strength anywhere from 0% to 100% (unlike URP's built-in
/// Tonemapping Volume override, which is an enum that snaps on/off at weight 0.5).
/// </summary>
public class AdjustableACESEffect : MonoBehaviour
{
    private const string ShaderName = "Custom/AdjustableACESShader";

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ACESAmountId = Shader.PropertyToID("_ACESAmount");
    private static readonly int PostExposureId = Shader.PropertyToID("_PostExposure");
    private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
    private static readonly int SaturationId = Shader.PropertyToID("_Saturation");

    [Header("Target Camera")]
    [Tooltip("Leave empty to automatically use Camera.main.")]
    public Camera targetCamera;

    [Header("Adjustable ACES Tonemapping Controls")]
    [Tooltip("Smoothly blends ACES Tonemapping from 0 (Completely Off) to 1 (100% Full Unity ACES).")]
    [Range(0f, 1f)]
    public float acesAmount = 1.0f;

    [Tooltip("Pre-tonemap exposure in EV stops (0 = default).")]
    [Range(-3f, 3f)]
    public float postExposure = 0.0f;

    [Tooltip("Fine-tune contrast of the ACES curve (1 = standard ACES).")]
    [Range(0.5f, 1.5f)]
    public float contrast = 1.0f;

    [Tooltip("Fine-tune color saturation of the ACES curve (1 = standard ACES).")]
    [Range(0f, 2f)]
    public float saturation = 1.0f;

    [Header("Auto-Disable Built-In Volume Tonemapping")]
    [Tooltip("If a Volume is on this GameObject, automatically disables its snapping Tonemapping override so this smooth ACES clone takes over.")]
    public bool disableBuiltInVolumeTonemapping = true;

    [Header("Shader Reference")]
    [Tooltip("Assign 'Custom/AdjustableACESShader' here, or keep it in a Resources folder.")]
    public Shader acesShader;

    private Material acesMaterial;
    private Canvas overlayCanvas;
    private RawImage overlayImage;
    private RenderTexture captureRT;

    public void SetACESAmount(float value)
    {
        acesAmount = Mathf.Clamp01(value);
    }

    private void Awake()
    {
        if (disableBuiltInVolumeTonemapping)
        {
            Volume vol = GetComponent<Volume>();
            if (vol != null && vol.profile != null && vol.profile.TryGet(out Tonemapping tm))
            {
                tm.active = false;
            }
        }

        EnsureInitialized();
    }

    private void OnDisable()
    {
        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        ReleaseRT();
        if (acesMaterial != null) Destroy(acesMaterial);
        if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
    }

    private void EnsureInitialized()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (acesShader == null) acesShader = Shader.Find(ShaderName);
        if (acesShader == null) return;

        if (acesMaterial == null)
        {
            acesMaterial = new Material(acesShader)
            {
                name = "AdjustableACES_Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObj = new GameObject("AdjustableACES_OverlayCanvas");
            canvasObj.transform.SetParent(transform, false);

            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Sits just below UI/Breakdown overlays or at the top of the camera stack
            overlayCanvas.sortingOrder = 32750;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            GameObject rawObj = new GameObject("AdjustableACES_RawImage");
            rawObj.transform.SetParent(overlayCanvas.transform, false);

            overlayImage = rawObj.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            overlayImage.material = acesMaterial;

            RectTransform rt = overlayImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            canvasObj.SetActive(false);
        }
    }

    private void EnsureRT(int width, int height)
    {
        width = Mathf.Max(64, width);
        height = Mathf.Max(64, height);

        if (captureRT != null && captureRT.width == width && captureRT.height == height)
            return;

        ReleaseRT();
        captureRT = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
        {
            name = "AdjustableACES_CaptureRT",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        captureRT.Create();

        if (overlayImage != null) overlayImage.texture = captureRT;
    }

    private void ReleaseRT()
    {
        if (captureRT != null)
        {
            captureRT.Release();
            Destroy(captureRT);
            captureRT = null;
        }
    }

    private void Update()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (acesAmount <= 0.0001f)
        {
            if (overlayCanvas != null && overlayCanvas.gameObject.activeSelf)
                overlayCanvas.gameObject.SetActive(false);
            return;
        }

        EnsureInitialized();
        if (overlayCanvas != null && !overlayCanvas.gameObject.activeSelf)
            overlayCanvas.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (acesAmount <= 0.0001f || targetCamera == null || acesMaterial == null) return;

        EnsureRT(Screen.width, Screen.height);

        RenderTexture prev = targetCamera.targetTexture;
        targetCamera.targetTexture = captureRT;
        targetCamera.Render();
        targetCamera.targetTexture = prev;

        acesMaterial.SetTexture(MainTexId, captureRT);
        acesMaterial.SetFloat(ACESAmountId, acesAmount);
        acesMaterial.SetFloat(PostExposureId, postExposure);
        acesMaterial.SetFloat(ContrastId, contrast);
        acesMaterial.SetFloat(SaturationId, saturation);
    }
}