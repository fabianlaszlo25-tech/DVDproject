using UnityEngine;
using UnityEngine.UI;

public class DrunkScreenEffect : MonoBehaviour
{
    private const string ShaderName = "Custom/DrunkScreenShader";
    private const int EchoBufferCount = 12;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int EchoTex1Id = Shader.PropertyToID("_EchoTex1");
    private static readonly int EchoTex2Id = Shader.PropertyToID("_EchoTex2");
    private static readonly int EchoTex3Id = Shader.PropertyToID("_EchoTex3");

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int WobbleAmplitudeId = Shader.PropertyToID("_WobbleAmplitude");
    private static readonly int WobbleFrequencyId = Shader.PropertyToID("_WobbleFrequency");
    private static readonly int WobbleSpeedId = Shader.PropertyToID("_WobbleSpeed");
    private static readonly int BlurAmountId = Shader.PropertyToID("_BlurAmount");
    private static readonly int ChromaticSplitId = Shader.PropertyToID("_ChromaticSplit");
    private static readonly int TripleEchoStrengthId = Shader.PropertyToID("_TripleEchoStrength");
    private static readonly int GhostOffset1Id = Shader.PropertyToID("_GhostOffset1");
    private static readonly int GhostOffset2Id = Shader.PropertyToID("_GhostOffset2");
    private static readonly int GhostOffset3Id = Shader.PropertyToID("_GhostOffset3");

    [Header("Target Camera")]
    public Camera targetCamera;

    [Header("Activation & Duration")]
    public float defaultDuration = 60.0f;
    public float fadeInSpeed = 0.35f;
    public float fadeOutSpeed = 0.35f;
    [Range(0f, 1f)] public float maxIntensity = 1.0f;

    [Header("Blurry Swimming / Meshing")]
    public float wobbleAmplitude = 0.014f;
    public float wobbleFrequency = 4.2f;
    public float wobbleSpeed = 2.3f;
    public float blurAmount = 0.0038f;
    public float chromaticSplit = 0.005f;

    [Header("Camera 'Seeing Movement 3 Times' Echo")]
    [Range(0f, 1f)] public float tripleEchoStrength = 0.85f;
    public float rotationEchoSensitivity = 0.065f;
    public float positionEchoSensitivity = 0.16f;
    public float maxEchoUVOffset = 0.11f;
    public float echoDecaySpeed = 7.5f;
    public float echoFrameStepInterval = 0.035f;

    [Header("Optional Physical Camera Sway")]
    public float cameraRollSwayDegrees = 3.5f;

    [Header("Shader Reference")]
    public Shader drunkShader;

    private Material drunkMaterial;
    private Canvas overlayCanvas;
    private RawImage overlayImage;

    private RenderTexture currentFrameRT;
    private readonly RenderTexture[] echoHistoryRTs = new RenderTexture[EchoBufferCount];
    private int currentEchoWriteIdx;
    private float echoCaptureTimer;

    private bool isEffectActive;
    private bool isExternallyDriven;
    private float remainingTimer;
    private float currentIntensity;

    private Quaternion lastCamRotation;
    private Vector3 lastCamPosition;
    private bool hasInitializedCameraPose;

    private Vector2 echoVelocity1;
    private Vector2 echoVelocity2;
    private Vector2 echoVelocity3;
    private float echoPulsePhase;
    private float appliedRollAngle;

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

    [ContextMenu("Trigger Drunk Effect")]
    public void TriggerDrunkEffect()
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = defaultDuration;
    }

    public void TriggerDrunkEffectForSeconds(float seconds)
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = Mathf.Max(0.1f, seconds);
    }

    [ContextMenu("Toggle Drunk Effect")]
    public void ToggleDrunkEffect()
    {
        if (isEffectActive || currentIntensity > 0.01f)
            DisableDrunkEffect();
        else
            EnableDrunkEffect();
    }

    public void EnableDrunkEffect()
    {
        EnsureInitialized();
        isExternallyDriven = false;
        isEffectActive = true;
        remainingTimer = 0f;
    }

    public void DisableDrunkEffect()
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
        RemoveCameraRoll();
        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        RemoveCameraRoll();
        ReleaseRenderTextures();
        if (drunkMaterial != null) Destroy(drunkMaterial);
        if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
    }

    private void EnsureInitialized()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (drunkShader == null) drunkShader = Shader.Find(ShaderName);
        if (drunkShader == null) return;

        if (drunkMaterial == null)
        {
            drunkMaterial = new Material(drunkShader)
            {
                name = "DrunkFullscreenMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObj = new GameObject("DrunkScreenOverlay_Canvas");
            canvasObj.transform.SetParent(transform, false);

            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 32760;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            GameObject rawImgObj = new GameObject("DrunkScreen_RawImage");
            rawImgObj.transform.SetParent(canvasObj.transform, false);

            overlayImage = rawImgObj.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            overlayImage.material = drunkMaterial;

            RectTransform rt = overlayImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            canvasObj.SetActive(false);
        }
    }

    private void EnsureRenderTextures(int width, int height)
    {
        width = Mathf.Max(64, width);
        height = Mathf.Max(64, height);

        if (currentFrameRT != null && currentFrameRT.width == width && currentFrameRT.height == height)
            return;

        ReleaseRenderTextures();

        currentFrameRT = CreateRT(width, height, "Drunk_CurrentFrameRT");

        int echoW = Mathf.Max(64, width / 2);
        int echoH = Mathf.Max(64, height / 2);
        for (int i = 0; i < EchoBufferCount; i++)
        {
            echoHistoryRTs[i] = CreateRT(echoW, echoH, "Drunk_EchoRT_" + i);
        }

        if (overlayImage != null) overlayImage.texture = currentFrameRT;
    }

    private RenderTexture CreateRT(int w, int h, string rtName)
    {
        RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR)
        {
            name = rtName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
        return rt;
    }

    private void ReleaseRenderTextures()
    {
        if (currentFrameRT != null)
        {
            currentFrameRT.Release();
            Destroy(currentFrameRT);
            currentFrameRT = null;
        }

        for (int i = 0; i < EchoBufferCount; i++)
        {
            if (echoHistoryRTs[i] != null)
            {
                echoHistoryRTs[i].Release();
                Destroy(echoHistoryRTs[i]);
                echoHistoryRTs[i] = null;
            }
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

            RemoveCameraRoll();
            hasInitializedCameraPose = false;
            return;
        }

        EnsureInitialized();
        if (overlayCanvas != null && !overlayCanvas.gameObject.activeSelf)
            overlayCanvas.gameObject.SetActive(true);

        UpdateCameraMotionEchoes();
    }

    private void LateUpdate()
    {
        if (currentIntensity <= 0.001f || targetCamera == null || drunkMaterial == null) return;

        RemoveCameraRoll();
        if (cameraRollSwayDegrees > 0.001f)
        {
            float t = Time.unscaledTime * wobbleSpeed * 0.7f;
            appliedRollAngle = (Mathf.Sin(t) * 0.65f + Mathf.Cos(t * 1.7f) * 0.35f) * cameraRollSwayDegrees * currentIntensity;
            targetCamera.transform.localRotation *= Quaternion.Euler(0f, 0f, appliedRollAngle);
        }

        EnsureRenderTextures(Screen.width, Screen.height);

        RenderTexture prevTarget = targetCamera.targetTexture;
        targetCamera.targetTexture = currentFrameRT;
        targetCamera.Render();
        targetCamera.targetTexture = prevTarget;

        if (!hasInitializedCameraPose)
        {
            for (int i = 0; i < EchoBufferCount; i++)
            {
                Graphics.Blit(currentFrameRT, echoHistoryRTs[i]);
            }
            lastCamRotation = targetCamera.transform.rotation;
            lastCamPosition = targetCamera.transform.position;
            hasInitializedCameraPose = true;
        }

        echoCaptureTimer += Time.unscaledDeltaTime;
        if (echoCaptureTimer >= Mathf.Max(0.01f, echoFrameStepInterval))
        {
            echoCaptureTimer = 0f;
            currentEchoWriteIdx = (currentEchoWriteIdx + 1) % EchoBufferCount;
            Graphics.Blit(currentFrameRT, echoHistoryRTs[currentEchoWriteIdx]);
        }

        int idx1 = (currentEchoWriteIdx - 1 + EchoBufferCount) % EchoBufferCount;
        int idx2 = (currentEchoWriteIdx - 3 + EchoBufferCount) % EchoBufferCount;
        int idx3 = (currentEchoWriteIdx - 6 + EchoBufferCount) % EchoBufferCount;

        ApplyShaderProperties(echoHistoryRTs[idx1], echoHistoryRTs[idx2], echoHistoryRTs[idx3]);
    }

    private void RemoveCameraRoll()
    {
        if (Mathf.Abs(appliedRollAngle) > 0.0001f && targetCamera != null)
        {
            targetCamera.transform.localRotation *= Quaternion.Euler(0f, 0f, -appliedRollAngle);
            appliedRollAngle = 0f;
        }
    }

    private void UpdateCameraMotionEchoes()
    {
        if (targetCamera == null || !hasInitializedCameraPose) return;

        Transform camTransform = targetCamera.transform;
        float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);

        Quaternion deltaRot = Quaternion.Inverse(lastCamRotation) * camTransform.rotation;
        deltaRot.ToAngleAxis(out float angleDegrees, out Vector3 axis);
        if (angleDegrees > 180f) angleDegrees -= 360f;

        Vector2 angularScreenDelta = Vector2.zero;
        if (Mathf.Abs(angleDegrees) > 0.0005f)
        {
            Vector3 localAngular = axis * angleDegrees;
            angularScreenDelta = new Vector2(-localAngular.y, localAngular.x) * (rotationEchoSensitivity * 0.1f / dt) * 0.016f;
        }

        Vector3 worldDeltaPos = camTransform.position - lastCamPosition;
        Vector3 localDeltaPos = camTransform.InverseTransformDirection(worldDeltaPos);
        Vector2 posScreenDelta = new Vector2(-localDeltaPos.x, -localDeltaPos.y) * (positionEchoSensitivity / dt) * 0.016f;

        lastCamRotation = camTransform.rotation;
        lastCamPosition = camTransform.position;

        Vector2 rawDelta = Vector2.ClampMagnitude(angularScreenDelta + posScreenDelta, maxEchoUVOffset);

        echoVelocity1 = Vector2.Lerp(echoVelocity1, rawDelta * 0.55f, 1f - Mathf.Exp(-echoDecaySpeed * 1.5f * dt));
        echoVelocity2 = Vector2.Lerp(echoVelocity2, rawDelta * 1.00f, 1f - Mathf.Exp(-echoDecaySpeed * 0.95f * dt));
        echoVelocity3 = Vector2.Lerp(echoVelocity3, rawDelta * 1.45f, 1f - Mathf.Exp(-echoDecaySpeed * 0.55f * dt));

        echoPulsePhase += dt * 30f;
        float pulse1 = 1.0f + 0.28f * Mathf.Sin(echoPulsePhase);
        float pulse2 = 1.0f + 0.28f * Mathf.Sin(echoPulsePhase - 2.094f);
        float pulse3 = 1.0f + 0.28f * Mathf.Sin(echoPulsePhase - 4.188f);

        echoVelocity1 = Vector2.ClampMagnitude(echoVelocity1 * pulse1, maxEchoUVOffset);
        echoVelocity2 = Vector2.ClampMagnitude(echoVelocity2 * pulse2, maxEchoUVOffset);
        echoVelocity3 = Vector2.ClampMagnitude(echoVelocity3 * pulse3, maxEchoUVOffset);
    }

    private void ApplyShaderProperties(Texture echo1, Texture echo2, Texture echo3)
    {
        float motionMagnitude = Mathf.Clamp01(
            (echoVelocity1.magnitude + echoVelocity2.magnitude + echoVelocity3.magnitude) /
            Mathf.Max(0.001f, maxEchoUVOffset * 0.45f));

        float activeEchoWeight = Mathf.Lerp(0.35f, 1.0f, motionMagnitude);

        drunkMaterial.SetTexture(MainTexId, currentFrameRT);
        drunkMaterial.SetTexture(EchoTex1Id, echo1);
        drunkMaterial.SetTexture(EchoTex2Id, echo2);
        drunkMaterial.SetTexture(EchoTex3Id, echo3);

        drunkMaterial.SetFloat(IntensityId, currentIntensity);
        drunkMaterial.SetFloat(WobbleAmplitudeId, wobbleAmplitude);
        drunkMaterial.SetFloat(WobbleFrequencyId, wobbleFrequency);
        drunkMaterial.SetFloat(WobbleSpeedId, wobbleSpeed);
        drunkMaterial.SetFloat(BlurAmountId, blurAmount);
        drunkMaterial.SetFloat(ChromaticSplitId, chromaticSplit);
        drunkMaterial.SetFloat(TripleEchoStrengthId, tripleEchoStrength);

        drunkMaterial.SetVector(GhostOffset1Id, new Vector4(echoVelocity1.x, echoVelocity1.y, activeEchoWeight, 0f));
        drunkMaterial.SetVector(GhostOffset2Id, new Vector4(echoVelocity2.x, echoVelocity2.y, activeEchoWeight, 0f));
        drunkMaterial.SetVector(GhostOffset3Id, new Vector4(echoVelocity3.x, echoVelocity3.y, activeEchoWeight, 0f));
    }
}