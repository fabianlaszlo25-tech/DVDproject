using UnityEngine;
using UnityEngine.UI;

public class HighScreenEffect : MonoBehaviour
{
    private const string ShaderName = "Custom/HighScreenShader";

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int EchoTexId = Shader.PropertyToID("_EchoTex");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int BreathSpeedId = Shader.PropertyToID("_BreathSpeed");
    private static readonly int WaveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    private static readonly int HueShiftSpeedId = Shader.PropertyToID("_HueShiftSpeed");
    private static readonly int HueShiftStrengthId = Shader.PropertyToID("_HueShiftStrength");
    private static readonly int DreamBloomId = Shader.PropertyToID("_DreamBloom");
    private static readonly int WarmVignetteColorId = Shader.PropertyToID("_WarmVignetteColor");
    private static readonly int TrailBlendId = Shader.PropertyToID("_TrailBlend");

    [Header("Target Camera")]
    public Camera targetCamera;

    [Header("Standalone Activation")]
    public float defaultDuration = 60.0f;
    public float fadeInSpeed = 0.35f;
    public float fadeOutSpeed = 0.35f;
    [Range(0f, 1f)] public float maxIntensity = 1.0f;

    [Header("Dreamy / High Visual Settings")]
    public float breathSpeed = 1.1f;
    public float waveAmplitude = 0.009f;
    public float hueShiftSpeed = 0.35f;
    [Range(0f, 1f)] public float hueShiftStrength = 0.55f;
    public float dreamBloom = 0.0055f;
    public Color warmVignetteColor = new Color(0.95f, 0.35f, 0.85f, 1.0f);
    [Range(0f, 1f)] public float trailBlend = 0.45f;

    [Header("Floaty Camera Drift")]
    public float floatyCameraDegrees = 2.2f;

    [Header("Shader Reference")]
    public Shader highShader;

    private Material highMaterial;
    private Canvas overlayCanvas;
    private RawImage overlayImage;
    private RenderTexture currentFrameRT;
    private RenderTexture dreamTrailRT;

    private bool isEffectActive;
    private bool isExternallyDriven;
    private float remainingTimer;
    private float currentIntensity;
    private float trailCaptureTimer;
    private Vector3 appliedDriftEuler;

    public float CurrentIntensity => currentIntensity;

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

    [ContextMenu("Trigger High Effect")]
    public void TriggerHighEffect()
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = defaultDuration;
    }

    public void TriggerHighEffectForSeconds(float seconds)
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = Mathf.Max(0.1f, seconds);
    }

    [ContextMenu("Toggle High Effect")]
    public void ToggleHighEffect()
    {
        if (isEffectActive || currentIntensity > 0.01f)
            DisableHighEffect();
        else
            EnableHighEffect();
    }

    public void EnableHighEffect()
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = 0f;
    }

    public void DisableHighEffect()
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
        RemoveCameraDrift();
        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        RemoveCameraDrift();
        ReleaseRTs();
        if (highMaterial != null) Destroy(highMaterial);
        if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
    }

    private void EnsureInitialized()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (highShader == null) highShader = Shader.Find(ShaderName);
        if (highShader == null) return;

        if (highMaterial == null)
        {
            highMaterial = new Material(highShader)
            {
                name = "HighFullscreenMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObj = new GameObject("HighScreenOverlay_Canvas");
            canvasObj.transform.SetParent(transform, false);

            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 32762;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            GameObject rawImgObj = new GameObject("HighScreen_RawImage");
            rawImgObj.transform.SetParent(canvasObj.transform, false);

            overlayImage = rawImgObj.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            overlayImage.material = highMaterial;

            RectTransform rt = overlayImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            canvasObj.SetActive(false);
        }
    }

    private void EnsureRTs(int width, int height)
    {
        width = Mathf.Max(64, width);
        height = Mathf.Max(64, height);

        if (currentFrameRT != null && currentFrameRT.width == width && currentFrameRT.height == height)
            return;

        ReleaseRTs();

        currentFrameRT = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
        {
            name = "High_CurrentFrameRT",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        currentFrameRT.Create();

        dreamTrailRT = new RenderTexture(Mathf.Max(64, width / 2), Mathf.Max(64, height / 2), 0, RenderTextureFormat.DefaultHDR)
        {
            name = "High_DreamTrailRT",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        dreamTrailRT.Create();
        Graphics.Blit(currentFrameRT, dreamTrailRT);

        if (overlayImage != null) overlayImage.texture = currentFrameRT;
    }

    private void ReleaseRTs()
    {
        if (currentFrameRT != null) { currentFrameRT.Release(); Destroy(currentFrameRT); currentFrameRT = null; }
        if (dreamTrailRT != null) { dreamTrailRT.Release(); Destroy(dreamTrailRT); dreamTrailRT = null; }
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

            RemoveCameraDrift();
            return;
        }

        EnsureInitialized();
        if (overlayCanvas != null && !overlayCanvas.gameObject.activeSelf)
            overlayCanvas.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (currentIntensity <= 0.001f || targetCamera == null || highMaterial == null) return;

        RemoveCameraDrift();

        if (floatyCameraDegrees > 0.001f)
        {
            float t = Time.unscaledTime * breathSpeed * 0.65f;
            appliedDriftEuler = new Vector3(
                Mathf.Sin(t * 0.9f) * (floatyCameraDegrees * 0.5f) * currentIntensity,
                0f,
                Mathf.Cos(t * 0.7f) * floatyCameraDegrees * currentIntensity
            );
            targetCamera.transform.localRotation *= Quaternion.Euler(appliedDriftEuler);
        }

        EnsureRTs(Screen.width, Screen.height);

        RenderTexture prev = targetCamera.targetTexture;
        targetCamera.targetTexture = currentFrameRT;
        targetCamera.Render();
        targetCamera.targetTexture = prev;

        trailCaptureTimer += Time.unscaledDeltaTime;
        if (trailCaptureTimer >= 0.06f)
        {
            trailCaptureTimer = 0f;
            Graphics.Blit(currentFrameRT, dreamTrailRT);
        }

        highMaterial.SetTexture(MainTexId, currentFrameRT);
        highMaterial.SetTexture(EchoTexId, dreamTrailRT);
        highMaterial.SetFloat(IntensityId, currentIntensity);
        highMaterial.SetFloat(BreathSpeedId, breathSpeed);
        highMaterial.SetFloat(WaveAmplitudeId, waveAmplitude);
        highMaterial.SetFloat(HueShiftSpeedId, hueShiftSpeed);
        highMaterial.SetFloat(HueShiftStrengthId, hueShiftStrength);
        highMaterial.SetFloat(DreamBloomId, dreamBloom);
        highMaterial.SetColor(WarmVignetteColorId, warmVignetteColor);
        highMaterial.SetFloat(TrailBlendId, trailBlend);
    }

    private void RemoveCameraDrift()
    {
        if (appliedDriftEuler.sqrMagnitude > 0.000001f && targetCamera != null)
        {
            targetCamera.transform.localRotation *= Quaternion.Inverse(Quaternion.Euler(appliedDriftEuler));
            appliedDriftEuler = Vector3.zero;
        }
    }
}