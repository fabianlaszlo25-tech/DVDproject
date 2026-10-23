using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fullscreen Nicotine Buzz Effect: a subtle hybrid combining a lightheaded alcohol swim
/// and motion echo with a crisp caffeine focus pulse and micro-tremor.
/// Driven automatically by <see cref="TimedConsumableBooster"/> or via Button OnClick().
/// </summary>
public class NicotineScreenEffect : MonoBehaviour
{
    private const string ShaderName = "Custom/NicotineScreenShader";

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int EchoTexId = Shader.PropertyToID("_EchoTex");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int BuzzSwimAmplitudeId = Shader.PropertyToID("_BuzzSwimAmplitude");
    private static readonly int BuzzSwimSpeedId = Shader.PropertyToID("_BuzzSwimSpeed");
    private static readonly int MicroTremorId = Shader.PropertyToID("_MicroTremor");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int FocusContrastId = Shader.PropertyToID("_FocusContrast");
    private static readonly int ChromaticSplitId = Shader.PropertyToID("_ChromaticSplit");
    private static readonly int EchoOffsetId = Shader.PropertyToID("_EchoOffset");

    [Header("Target Camera")]
    public Camera targetCamera;

    [Header("Standalone Activation")]
    public float defaultDuration = 60.0f;
    public float fadeInSpeed = 0.25f;
    public float fadeOutSpeed = 0.2f;
    [Range(0f, 1f)] public float maxIntensity = 1.0f;

    [Header("Nicotine Buzz Settings (Subtle Alcohol + Caffeine Mix)")]
    public float buzzSwimAmplitude = 0.0055f;
    public float buzzSwimSpeed = 1.8f;
    public float microTremor = 0.0012f;
    public float pulseSpeed = 7.5f;
    public float focusContrast = 1.12f;
    public float chromaticSplit = 0.003f;
    public float echoSensitivity = 0.035f;

    [Header("Subtle Head-Rush Camera Sway")]
    public float cameraSwayDegrees = 1.2f;

    [Header("Shader Reference")]
    public Shader nicotineShader;

    private Material nicotineMaterial;
    private Canvas overlayCanvas;
    private RawImage overlayImage;
    private RenderTexture currentFrameRT;
    private RenderTexture echoFrameRT;

    private bool isEffectActive;
    private bool isExternallyDriven;
    private float remainingTimer;
    private float currentIntensity;

    private Quaternion lastCamRotation;
    private bool hasInitializedCam;
    private Vector2 smoothedEchoOffset;
    private float echoCaptureTimer;
    private Vector3 appliedSwayEuler;

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

    [ContextMenu("Trigger Nicotine Effect")]
    public void TriggerNicotineEffect()
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = defaultDuration;
    }

    public void TriggerNicotineEffectForSeconds(float seconds)
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = Mathf.Max(0.1f, seconds);
    }

    [ContextMenu("Toggle Nicotine Effect")]
    public void ToggleNicotineEffect()
    {
        if (isEffectActive || currentIntensity > 0.01f)
            DisableNicotineEffect();
        else
            EnableNicotineEffect();
    }

    public void EnableNicotineEffect()
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = 0f;
    }

    public void DisableNicotineEffect()
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
        RemoveCameraSway();
        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        RemoveCameraSway();
        ReleaseRTs();
        if (nicotineMaterial != null) Destroy(nicotineMaterial);
        if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
    }

    private void EnsureInitialized()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (nicotineShader == null) nicotineShader = Shader.Find(ShaderName);

        if (nicotineShader == null)
        {
            Debug.LogWarning("NicotineScreenEffect: 'Custom/NicotineScreenShader' not found! Put it in a Resources folder or assign it.", this);
            return;
        }

        if (nicotineMaterial == null)
        {
            nicotineMaterial = new Material(nicotineShader)
            {
                name = "NicotineFullscreenMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObj = new GameObject("NicotineScreenOverlay_Canvas");
            canvasObj.transform.SetParent(transform, false);

            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 32763;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            GameObject rawImgObj = new GameObject("NicotineScreen_RawImage");
            rawImgObj.transform.SetParent(canvasObj.transform, false);

            overlayImage = rawImgObj.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            overlayImage.material = nicotineMaterial;

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
            name = "Nicotine_CurrentFrameRT",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        currentFrameRT.Create();

        echoFrameRT = new RenderTexture(Mathf.Max(64, width / 2), Mathf.Max(64, height / 2), 0, RenderTextureFormat.DefaultHDR)
        {
            name = "Nicotine_EchoFrameRT",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        echoFrameRT.Create();
        Graphics.Blit(currentFrameRT, echoFrameRT);

        if (overlayImage != null) overlayImage.texture = currentFrameRT;
    }

    private void ReleaseRTs()
    {
        if (currentFrameRT != null)
        {
            currentFrameRT.Release();
            Destroy(currentFrameRT);
            currentFrameRT = null;
        }
        if (echoFrameRT != null)
        {
            echoFrameRT.Release();
            Destroy(echoFrameRT);
            echoFrameRT = null;
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

            RemoveCameraSway();
            hasInitializedCam = false;
            return;
        }

        EnsureInitialized();
        if (overlayCanvas != null && !overlayCanvas.gameObject.activeSelf)
            overlayCanvas.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (currentIntensity <= 0.001f || targetCamera == null || nicotineMaterial == null) return;

        RemoveCameraSway();

        if (cameraSwayDegrees > 0.001f)
        {
            float slowT = Time.unscaledTime * buzzSwimSpeed * 0.7f;
            float fastT = Time.unscaledTime * 35f;
            appliedSwayEuler = new Vector3(
                (Mathf.PerlinNoise(fastT, 0f) * 2f - 1f) * 0.12f * currentIntensity,
                0f,
                Mathf.Sin(slowT) * cameraSwayDegrees * currentIntensity
            );
            targetCamera.transform.localRotation *= Quaternion.Euler(appliedSwayEuler);
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

        Vector2 rawDelta = Mathf.Abs(angle) > 0.001f
            ? new Vector2(-axis.y * angle, axis.x * angle) * (echoSensitivity * 0.1f)
            : Vector2.zero;

        smoothedEchoOffset = Vector2.Lerp(
            smoothedEchoOffset,
            Vector2.ClampMagnitude(rawDelta, 0.045f),
            1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));

        EnsureRTs(Screen.width, Screen.height);

        RenderTexture prev = targetCamera.targetTexture;
        targetCamera.targetTexture = currentFrameRT;
        targetCamera.Render();
        targetCamera.targetTexture = prev;

        echoCaptureTimer += Time.unscaledDeltaTime;
        if (echoCaptureTimer >= 0.04f)
        {
            echoCaptureTimer = 0f;
            Graphics.Blit(currentFrameRT, echoFrameRT);
        }

        float motionWeight = Mathf.Lerp(0.25f, 1.0f, Mathf.Clamp01(smoothedEchoOffset.magnitude / 0.025f));

        nicotineMaterial.SetTexture(MainTexId, currentFrameRT);
        nicotineMaterial.SetTexture(EchoTexId, echoFrameRT);
        nicotineMaterial.SetFloat(IntensityId, currentIntensity);
        nicotineMaterial.SetFloat(BuzzSwimAmplitudeId, buzzSwimAmplitude);
        nicotineMaterial.SetFloat(BuzzSwimSpeedId, buzzSwimSpeed);
        nicotineMaterial.SetFloat(MicroTremorId, microTremor);
        nicotineMaterial.SetFloat(PulseSpeedId, pulseSpeed);
        nicotineMaterial.SetFloat(FocusContrastId, focusContrast);
        nicotineMaterial.SetFloat(ChromaticSplitId, chromaticSplit);
        nicotineMaterial.SetVector(EchoOffsetId, new Vector4(smoothedEchoOffset.x, smoothedEchoOffset.y, motionWeight, 0f));
    }

    private void RemoveCameraSway()
    {
        if (appliedSwayEuler.sqrMagnitude > 0.000001f && targetCamera != null)
        {
            targetCamera.transform.localRotation *= Quaternion.Inverse(Quaternion.Euler(appliedSwayEuler));
            appliedSwayEuler = Vector3.zero;
        }
    }
}