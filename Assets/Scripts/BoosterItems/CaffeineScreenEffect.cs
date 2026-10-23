using UnityEngine;
using UnityEngine.UI;

public class CaffeineScreenEffect : MonoBehaviour
{
    private const string ShaderName = "Custom/CaffeineScreenShader";

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int JitterAmountId = Shader.PropertyToID("_JitterAmount");
    private static readonly int JitterSpeedId = Shader.PropertyToID("_JitterSpeed");
    private static readonly int HeartbeatSpeedId = Shader.PropertyToID("_HeartbeatSpeed");
    private static readonly int PulseZoomId = Shader.PropertyToID("_PulseZoom");
    private static readonly int ChromaticEdgeId = Shader.PropertyToID("_ChromaticEdge");
    private static readonly int ContrastBoostId = Shader.PropertyToID("_ContrastBoost");
    private static readonly int SaturationBoostId = Shader.PropertyToID("_SaturationBoost");
    private static readonly int EdgeTintId = Shader.PropertyToID("_EdgeTint");
    private static readonly int CameraVelocityId = Shader.PropertyToID("_CameraVelocity");

    [Header("Target Camera")]
    public Camera targetCamera;

    [Header("Standalone Activation")]
    public float defaultDuration = 60.0f;
    public float fadeInSpeed = 0.35f;
    public float fadeOutSpeed = 0.35f;
    [Range(0f, 1f)] public float maxIntensity = 1.0f;

    [Header("Caffeine Rush Look")]
    public float jitterAmount = 0.0026f;
    public float jitterSpeed = 55.0f;
    public float heartbeatSpeed = 11.5f;
    public float pulseZoom = 0.022f;
    public float chromaticEdge = 0.0085f;
    public float contrastBoost = 1.22f;
    public float saturationBoost = 1.38f;
    [ColorUsage(true, true)] public Color edgeTint = new Color(0.15f, 1.0f, 0.55f, 1.0f);

    [Header("Physical Camera Micro-Tremor")]
    public float cameraJitterDegrees = 0.35f;

    [Header("Shader Reference")]
    public Shader caffeineShader;

    private Material caffeineMaterial;
    private Canvas overlayCanvas;
    private RawImage overlayImage;
    private RenderTexture currentFrameRT;

    private bool isEffectActive;
    private bool isExternallyDriven;
    private float remainingTimer;
    private float currentIntensity;

    private Quaternion lastCamRotation;
    private bool hasInitializedCam;
    private Vector2 smoothedCamVel;
    private Vector3 appliedCameraJitterEuler;

    public void SetExternalIntensity(float value)
    {
        EnsureInitialized();
        isExternallyDriven = true;
        currentIntensity = Mathf.Clamp01(value);

        if (currentIntensity <= 0.0001f)
        {
            isExternallyDriven = false;
            isEffectActive = false;
        }
    }

    [ContextMenu("Trigger Caffeine Effect")]
    public void TriggerCaffeineEffect()
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = defaultDuration;
    }

    public void TriggerCaffeineEffectForSeconds(float seconds)
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = Mathf.Max(0.1f, seconds);
    }

    [ContextMenu("Toggle Caffeine Effect")]
    public void ToggleCaffeineEffect()
    {
        if (isEffectActive || currentIntensity > 0.01f)
            DisableCaffeineEffect();
        else
            EnableCaffeineEffect();
    }

    public void EnableCaffeineEffect()
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = 0f;
    }

    public void DisableCaffeineEffect()
    {
        isExternallyDriven = false;
        isEffectActive = false;
        remainingTimer = 0f;
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnDisable()
    {
        RemoveCameraJitter();
        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        RemoveCameraJitter();
        ReleaseRT();
        if (caffeineMaterial != null) Destroy(caffeineMaterial);
        if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
    }

    private void EnsureInitialized()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (caffeineShader == null) caffeineShader = Shader.Find(ShaderName);
        if (caffeineShader == null) return;

        if (caffeineMaterial == null)
        {
            caffeineMaterial = new Material(caffeineShader)
            {
                name = "CaffeineFullscreenMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObj = new GameObject("CaffeineScreenOverlay_Canvas");
            canvasObj.transform.SetParent(transform, false);

            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 32761;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            GameObject rawImgObj = new GameObject("CaffeineScreen_RawImage");
            rawImgObj.transform.SetParent(canvasObj.transform, false);

            overlayImage = rawImgObj.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            overlayImage.material = caffeineMaterial;

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

        if (currentFrameRT != null && currentFrameRT.width == width && currentFrameRT.height == height)
            return;

        ReleaseRT();
        currentFrameRT = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
        {
            name = "Caffeine_CurrentFrameRT",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        currentFrameRT.Create();

        if (overlayImage != null) overlayImage.texture = currentFrameRT;
    }

    private void ReleaseRT()
    {
        if (currentFrameRT != null)
        {
            currentFrameRT.Release();
            Destroy(currentFrameRT);
            currentFrameRT = null;
        }
    }

    private void Update()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (!isExternallyDriven)
        {
            if (isEffectActive && remainingTimer > 0f)
            {
                remainingTimer -= Time.unscaledDeltaTime;
                if (remainingTimer <= 0f)
                {
                    remainingTimer = 0f;
                    isEffectActive = false;
                }
            }

            float target = isEffectActive ? maxIntensity : 0f;
            float speed = isEffectActive ? fadeInSpeed : fadeOutSpeed;
            currentIntensity = Mathf.MoveTowards(currentIntensity, target, speed * Time.unscaledDeltaTime);
        }

        if (currentIntensity <= 0.001f)
        {
            if (overlayCanvas != null && overlayCanvas.gameObject.activeSelf)
                overlayCanvas.gameObject.SetActive(false);

            RemoveCameraJitter();
            hasInitializedCam = false;
            return;
        }

        EnsureInitialized();
        if (overlayCanvas != null && !overlayCanvas.gameObject.activeSelf)
            overlayCanvas.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (currentIntensity <= 0.001f || targetCamera == null || caffeineMaterial == null) return;

        RemoveCameraJitter();

        if (cameraJitterDegrees > 0.001f)
        {
            float t = Time.unscaledTime * jitterSpeed;
            appliedCameraJitterEuler = new Vector3(
                (Mathf.PerlinNoise(t, 0f) * 2f - 1f) * cameraJitterDegrees * currentIntensity,
                (Mathf.PerlinNoise(0f, t) * 2f - 1f) * cameraJitterDegrees * currentIntensity,
                (Mathf.PerlinNoise(t, t) * 2f - 1f) * (cameraJitterDegrees * 0.5f) * currentIntensity
            );
            targetCamera.transform.localRotation *= Quaternion.Euler(appliedCameraJitterEuler);
        }

        if (!hasInitializedCam)
        {
            lastCamRotation = targetCamera.transform.rotation;
            hasInitializedCam = true;
        }

        Quaternion deltaRot = Quaternion.Inverse(lastCamRotation) * targetCamera.transform.rotation;
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        lastCamRotation = targetCamera.transform.rotation;

        Vector2 rawVel = Mathf.Abs(angle) > 0.001f
            ? new Vector2(-axis.y * angle, axis.x * angle) * 0.004f
            : Vector2.zero;
        smoothedCamVel = Vector2.Lerp(smoothedCamVel, Vector2.ClampMagnitude(rawVel, 0.05f), 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));

        EnsureRT(Screen.width, Screen.height);
        RenderTexture prev = targetCamera.targetTexture;
        targetCamera.targetTexture = currentFrameRT;
        targetCamera.Render();
        targetCamera.targetTexture = prev;

        caffeineMaterial.SetTexture(MainTexId, currentFrameRT);
        caffeineMaterial.SetFloat(IntensityId, currentIntensity);
        caffeineMaterial.SetFloat(JitterAmountId, jitterAmount);
        caffeineMaterial.SetFloat(JitterSpeedId, jitterSpeed);
        caffeineMaterial.SetFloat(HeartbeatSpeedId, heartbeatSpeed);
        caffeineMaterial.SetFloat(PulseZoomId, pulseZoom);
        caffeineMaterial.SetFloat(ChromaticEdgeId, chromaticEdge);
        caffeineMaterial.SetFloat(ContrastBoostId, contrastBoost);
        caffeineMaterial.SetFloat(SaturationBoostId, saturationBoost);
        caffeineMaterial.SetColor(EdgeTintId, edgeTint);
        caffeineMaterial.SetVector(CameraVelocityId, new Vector4(smoothedCamVel.x, smoothedCamVel.y, smoothedCamVel.magnitude, 0f));
    }

    private void RemoveCameraJitter()
    {
        if (appliedCameraJitterEuler.sqrMagnitude > 0.000001f && targetCamera != null)
        {
            targetCamera.transform.localRotation *= Quaternion.Inverse(Quaternion.Euler(appliedCameraJitterEuler));
            appliedCameraJitterEuler = Vector3.zero;
        }
    }
}