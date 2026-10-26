using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public enum ActiveSubstanceComboState
{
    None = 0,
    Hyperfocus_NicotineCaffeine = 1,
    HeartAttack_AlcoholCaffeine = 2,
    Hanyinger_AlcoholWeed = 3,
    AnxietyAttack_WeedNicOrCaff = 4,
    DizzyBuzz_AlcoholNicotine = 5,
    SuperOverdose_ThreeEffects = 6
}

[System.Serializable]
public class HyperfocusComboSettings
{
    [Range(2f, 30f)] public float pulseSpeed = 13.5f;
    [Range(0f, 0.08f)] public float pulseZoom = 0.024f;
    [Range(0f, 3f)] public float centerSharpness = 1.45f;
    [Range(0f, 0.12f)] public float peripheralBlur = 0.045f;
    [Range(0f, 0.01f)] public float microTremor = 0.0022f;
    [Range(0f, 0.03f)] public float chromaticSplit = 0.012f;
    [Range(0f, 1.5f)] public float saturationBoost = 0.42f;
    [Range(0f, 1f)] public float contrastBoost = 0.28f;
    [Range(0f, 1.5f)] public float rimGlowStrength = 0.65f;
    [ColorUsage(true, true)] public Color primaryRimColor = new Color(0.05f, 0.95f, 1.0f, 1f);
    [ColorUsage(true, true)] public Color secondaryRimColor = new Color(1.0f, 0.8f, 0.15f, 1f);
    [Range(0f, 2f)] public float cameraBuzzDegrees = 0.25f;
}

[System.Serializable]
public class HeartAttackComboSettings
{
    [Range(0.4f, 4f)] public float heartbeatRate = 1.45f;
    [Range(0f, 0.18f)] public float systolicZoomSlam = 0.075f;
    [Range(0f, 0.18f)] public float radialBlurStrength = 0.085f;
    [Range(0f, 0.04f)] public float chromaticStrength = 0.018f;
    [Range(0f, 0.05f)] public float ghostSplitAmount = 0.014f;
    [Range(0f, 1f)] public float greyscaleDrain = 0.72f;
    [Range(0.2f, 0.9f)] public float tunnelOpenRadius = 0.58f;
    [Range(0.05f, 0.6f)] public float tunnelSlamRadius = 0.22f;
    [ColorUsage(true, true)] public Color arterialBloodColor = new Color(0.42f, 0.0f, 0.02f, 1f);
    [Range(0f, 8f)] public float cameraThumpDegrees = 3.2f;
}

[System.Serializable]
public class HanyingerComboSettings
{
    [Range(0.2f, 4f)] public float spinSpeed = 1.15f;
    [Range(0f, 0.8f)] public float spinMaxAngle = 0.38f;
    [Range(0f, 0.06f)] public float stomachHeaveAmplitude = 0.024f;
    [Range(0f, 0.015f)] public float blurRadius = 0.0045f;
    [Range(0f, 1f)] public float trailBlendWeight = 0.52f;
    [Range(0f, 1f)] public float bileColorStrength = 0.78f;
    [Range(0f, 1f)] public float vignetteStrength = 0.68f;
    [ColorUsage(true, true)] public Color primaryBileTint = new Color(0.68f, 1.05f, 0.42f, 1f);
    [ColorUsage(true, true)] public Color secondaryBileTint = new Color(0.92f, 0.98f, 0.35f, 1f);
    [Range(0f, 15f)] public float cameraVertigoDegrees = 6.8f;
}

[System.Serializable]
public class AnxietyAttackComboSettings
{
    [Range(2f, 35f)] public float panicPulseSpeed = 17.0f;
    [Range(0f, 0.4f)] public float barrelWarpStrength = 0.14f;
    [Range(0f, 0.015f)] public float nerveJitterAmount = 0.0035f;
    [Range(0f, 0.03f)] public float chromaticSplit = 0.008f;
    [Range(0f, 1.5f)] public float coldContrastBoost = 0.55f;
    [Range(0f, 1f)] public float desaturation = 0.55f;
    [Range(0f, 1f)] public float paranoiaTunnelStrength = 0.92f;
    [ColorUsage(true, true)] public Color coldPanicTint = new Color(0.84f, 0.93f, 1.12f, 1f);
    [Range(0f, 4f)] public float cameraTremorDegrees = 0.75f;
}

[System.Serializable]
public class DizzyBuzzComboSettings
{
    [Range(0.5f, 8f)] public float swaySpeed = 2.6f;
    [Range(0f, 0.05f)] public float gravitySinkAmplitude = 0.016f;
    [Range(0f, 0.012f)] public float blurAmount = 0.0035f;
    [Range(0f, 0.015f)] public float chromaticSplit = 0.0045f;
    [Range(0f, 1f)] public float echoTrailStrength = 0.45f;
    [Range(0f, 1f)] public float vignetteStrength = 0.55f;
    [ColorUsage(true, true)] public Color warmAmberTint = new Color(1.12f, 0.94f, 0.76f, 1f);
    [Range(0f, 6f)] public float cameraSwayDegrees = 2.2f;
}

[System.Serializable]
public class SuperOverdoseComboSettings
{
    [Header("Organic Fluid Smearing & Melting")]
    [Range(0.1f, 3f)] public float fluidFlowSpeed = 0.95f;
    [Range(0f, 0.12f)] public float fluidMeltDistortion = 0.042f;
    [Range(0f, 0.6f)] public float vortexSwirlAngle = 0.18f;
    [Range(0f, 0.015f)] public float blurRadius = 0.005f;
    [Range(0f, 1f)] public float persistentSmearWeight = 0.58f;
    [Range(0.5f, 0.97f)] public float smearBufferRetention = 0.88f;
    [Range(0f, 2f)] public float rainbowHueDrift = 0.55f;
    [Range(0f, 0.03f)] public float chromaticSpread = 0.008f;
    [Range(0f, 15f)] public float cameraMeltSwayDegrees = 8.5f;

    [Header("Hallucination Video & Blackout Timings")]
    public VideoClip hallucinationVideoClip;
    [Tooltip("Video file name in Assets/StreamingAssets used for WebGL builds (e.g. 'Overdose.mp4').")]
    public string streamingAssetsVideoFileName = "Overdose.mp4";
    [Range(0f, 1f)] public float maxVideoOpacity = 0.88f;
    public float smearAndVideoFadeInDuration = 3.5f;
    public float hallucinationPlayDuration = 8.0f;
    public float fadeToBlackDuration = 2.5f;
    public float unconsciousHoldDuration = 1.8f;
    public float wakeUpFadeDuration = 3.0f;
}

public class SubstanceCombinationManager : MonoBehaviour
{
    private const string ComboShaderName = "Custom/SubstanceCombinationShader";
    private const int EchoBufferCount = 12;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int EchoTex1Id = Shader.PropertyToID("_EchoTex1");
    private static readonly int EchoTex2Id = Shader.PropertyToID("_EchoTex2");
    private static readonly int EchoTex3Id = Shader.PropertyToID("_EchoTex3");
    private static readonly int SmearAccumTexId = Shader.PropertyToID("_SmearAccumTex");
    private static readonly int VideoTexId = Shader.PropertyToID("_VideoTex");

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int ComboModeId = Shader.PropertyToID("_ComboMode");
    private static readonly int VideoFadeId = Shader.PropertyToID("_VideoFade");
    private static readonly int BlackoutFadeId = Shader.PropertyToID("_BlackoutFade");

    private static readonly int GhostOffset1Id = Shader.PropertyToID("_GhostOffset1");
    private static readonly int GhostOffset2Id = Shader.PropertyToID("_GhostOffset2");
    private static readonly int GhostOffset3Id = Shader.PropertyToID("_GhostOffset3");

    private static readonly int ParamAId = Shader.PropertyToID("_ParamA");
    private static readonly int ParamBId = Shader.PropertyToID("_ParamB");
    private static readonly int ParamCId = Shader.PropertyToID("_ParamC");
    private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
    private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
    private static readonly int SmearParamsId = Shader.PropertyToID("_SmearParams");

    private static SubstanceCombinationManager instance;
    public static bool HasInstance => instance != null;

    public static SubstanceCombinationManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<SubstanceCombinationManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject("SubstanceCombinationManager");
                    instance = go.AddComponent<SubstanceCombinationManager>();
                }
            }
            return instance;
        }
    }

    [Header("Live Inspector Preview / Testing Dropdown")]
    public ActiveSubstanceComboState livePreviewComboDropdown = ActiveSubstanceComboState.None;

    [Header("Core References (Auto-Found if left empty)")]
    public Camera targetCamera;
    public BarManager barManager;

    [Header("Combined Booster Objects (Empty GameObjects with BoosterObject attached)")]
    public BoosterObject hyperfocusBooster;
    public BoosterObject heartAttackBooster;
    public BoosterObject hanyingerBooster;
    public BoosterObject anxietyAttackBooster;
    public BoosterObject dizzyBuzzBooster;
    public BoosterObject threeEffectOverdoseBooster;

    [Header("2-Effect Combination Duration & Original Fade Speed")]
    [Tooltip("How long any 2-substance combined effect lasts before smoothly unwinding (15 seconds).")]
    public float combinedDuration = 15.0f;
    [Tooltip("The original smooth fade/unwind speed (0.35 gives the ~3 second unwind where the distortion/zoom/trails settle back to normal).")]
    public float comboFadeSpeed = 0.35f;

    [Header("Effect Parameters (Click Dropdowns to Customize)")]
    public HyperfocusComboSettings hyperfocusSettings = new HyperfocusComboSettings();
    public HeartAttackComboSettings heartAttackSettings = new HeartAttackComboSettings();
    public HanyingerComboSettings hanyingerSettings = new HanyingerComboSettings();
    public AnxietyAttackComboSettings anxietyAttackSettings = new AnxietyAttackComboSettings();
    public DizzyBuzzComboSettings dizzyBuzzSettings = new DizzyBuzzComboSettings();
    public SuperOverdoseComboSettings superOverdoseSettings = new SuperOverdoseComboSettings();

    [Header("Camera Motion Echo Settings")]
    public float rotationEchoSensitivity = 0.065f;
    public float positionEchoSensitivity = 0.16f;
    public float maxEchoUVOffset = 0.11f;
    public float echoDecaySpeed = 7.0f;
    public float echoFrameStepInterval = 0.035f;

    [Header("Shader Reference")]
    public Shader combinationShader;

    private readonly List<TimedConsumableBooster> activeConsumables = new List<TimedConsumableBooster>();

    private ActiveSubstanceComboState currentComboState = ActiveSubstanceComboState.None;
    public ActiveSubstanceComboState CurrentComboState =>
        livePreviewComboDropdown != ActiveSubstanceComboState.None ? livePreviewComboDropdown : currentComboState;

    private float currentComboIntensity;
    private float currentVideoFade;
    private float currentBlackoutFade;

    // Bulletproof Update-driven state machine for 2-effect combinations so it can NEVER get stuck!
    private bool isTwoEffectComboLocked;
    private bool isTwoEffectComboUnwinding;
    private float comboRemainingTimer;

    private bool isOverdoseSequenceRunning;
    public bool IsOverdoseSequenceRunning => isOverdoseSequenceRunning;

    private Material comboMaterial;
    private Canvas comboCanvas;
    private RawImage comboOverlayImage;
    private Image raycastBlockerImage;
    private VideoPlayer videoPlayer;

    private RenderTexture screenCaptureRT;
    private readonly RenderTexture[] echoHistoryRTs = new RenderTexture[EchoBufferCount];
    private RenderTexture smearAccumRT_A;
    private RenderTexture smearAccumRT_B;
    private bool smearPingPong;
    private RenderTexture videoRT;

    private int currentEchoWriteIdx;
    private float echoCaptureTimer;
    private bool hasInitializedBuffers;

    private Quaternion lastCamRotation;
    private Vector3 lastCamPosition;
    private Vector2 echoVelocity1;
    private Vector2 echoVelocity2;
    private Vector2 echoVelocity3;
    private float echoPulsePhase;
    private Vector3 appliedCameraShake;

    private void OnValidate()
    {
        if (combinedDuration <= 0.1f) combinedDuration = 15.0f;
        if (comboFadeSpeed <= 0.01f) comboFadeSpeed = 0.35f;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        if (combinedDuration <= 0.1f) combinedDuration = 15.0f;
        if (comboFadeSpeed <= 0.01f) comboFadeSpeed = 0.35f;

        if (barManager == null) barManager = FindObjectOfType<BarManager>();
        if (targetCamera == null) targetCamera = Camera.main;

        EnsureOverlayInitialized();
        DeactivateAllComboBoosters();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        RemoveCameraShake();
        ReleaseRTs();
        if (comboMaterial != null) Destroy(comboMaterial);
        if (comboCanvas != null) Destroy(comboCanvas.gameObject);
    }

    /// <summary>
    /// Forcibly shuts down all active combination screen effects, overlays, video playback, and camera shake.
    /// Used when the player dies, when entering the breakdown cutscene, or on emergency reset.
    /// </summary>
    public static void StopAndClearAllEffects()
    {
        if (instance != null)
        {
            instance.StopAllEffectsInternal();
        }
    }

    public void StopAllEffectsInternal()
    {
        StopAllCoroutines();
        isTwoEffectComboLocked = false;
        isTwoEffectComboUnwinding = false;
        comboRemainingTimer = 0f;
        isOverdoseSequenceRunning = false;
        currentComboState = ActiveSubstanceComboState.None;
        currentComboIntensity = 0f;
        currentVideoFade = 0f;
        currentBlackoutFade = 0f;

        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
        }

        if (comboCanvas != null)
        {
            comboCanvas.gameObject.SetActive(false);
        }

        RemoveCameraShake();
        activeConsumables.Clear();
        DeactivateAllComboBoosters();
    }

    // =========================================================================
    // CROSS-SCENE SAVE & RESTORE HOOKS (RunSaveManager)
    // =========================================================================

    public ActiveSubstanceComboState GetRuntimeComboStateForSave(
        out bool isLocked,
        out float remainingTimer,
        out float intensity)
    {
        isLocked = isTwoEffectComboLocked;
        remainingTimer = comboRemainingTimer;
        intensity = currentComboIntensity;
        return currentComboState;
    }

    public void RevertComboBoostersForSceneTransition()
    {
        DeactivateAllComboBoosters();
    }

    public void RestoreTwoEffectComboFromSave(
        ActiveSubstanceComboState savedState,
        float remainingTimer,
        float intensity)
    {
        if (savedState == ActiveSubstanceComboState.None || savedState == ActiveSubstanceComboState.SuperOverdose_ThreeEffects)
            return;

        comboRemainingTimer = Mathf.Max(0.5f, remainingTimer);
        currentComboIntensity = Mathf.Clamp01(intensity);
        isTwoEffectComboUnwinding = remainingTimer <= 0.01f;
    }

    // =========================================================================
    // REGISTRATION & COMBINATION LOCKING
    // =========================================================================

    public void RegisterConsumable(TimedConsumableBooster consumable)
    {
        if (consumable == null) return;
        if (!activeConsumables.Contains(consumable))
        {
            activeConsumables.Add(consumable);
        }
        RecalculateCombinationState();
    }

    public void UnregisterConsumable(TimedConsumableBooster consumable)
    {
        if (consumable == null) return;
        if (isTwoEffectComboLocked || isOverdoseSequenceRunning) return;

        activeConsumables.Remove(consumable);
        RecalculateCombinationState();
    }

    public bool IsTypeActive(ConsumableVisualEffectType type)
    {
        for (int i = 0; i < activeConsumables.Count; i++)
        {
            if (activeConsumables[i] != null && activeConsumables[i].effectType == type)
                return true;
        }
        return false;
    }

    public bool ShouldSuppressSingleScreenEffects()
    {
        return CurrentComboState != ActiveSubstanceComboState.None
            || isTwoEffectComboLocked
            || isOverdoseSequenceRunning
            || currentComboIntensity > 0.001f;
    }

    private void RecalculateCombinationState()
    {
        if (isOverdoseSequenceRunning) return;

        activeConsumables.RemoveAll(c => c == null || !c.gameObject.activeInHierarchy);

        bool alc = IsTypeActive(ConsumableVisualEffectType.Drunk);
        bool caff = IsTypeActive(ConsumableVisualEffectType.Caffeinated);
        bool weed = IsTypeActive(ConsumableVisualEffectType.High);
        bool nic = IsTypeActive(ConsumableVisualEffectType.Nicotined);

        int distinctCount = (alc ? 1 : 0) + (caff ? 1 : 0) + (weed ? 1 : 0) + (nic ? 1 : 0);

        // 1. 3-SUBSTANCE SUPER OVERDOSE
        if (distinctCount >= 3)
        {
            isTwoEffectComboLocked = false;
            isTwoEffectComboUnwinding = false;
            comboRemainingTimer = 0f;
            StartCoroutine(ThreeEffectOverdoseRoutine());
            return;
        }

        // 2. 2-SUBSTANCE COMBINATIONS (Locks both consumables for 15 seconds, then unwinds!)
        if (distinctCount == 2 && !isTwoEffectComboLocked)
        {
            if (nic && caff)
            {
                LockIntoTwoEffectCombo(ActiveSubstanceComboState.Hyperfocus_NicotineCaffeine, keepIndividualBoosters: true, hyperfocusBooster);
                return;
            }
            if (alc && caff)
            {
                LockIntoTwoEffectCombo(ActiveSubstanceComboState.HeartAttack_AlcoholCaffeine, keepIndividualBoosters: false, heartAttackBooster);
                return;
            }
            if (alc && weed)
            {
                LockIntoTwoEffectCombo(ActiveSubstanceComboState.Hanyinger_AlcoholWeed, keepIndividualBoosters: false, hanyingerBooster);
                return;
            }
            if ((weed && nic) || (weed && caff))
            {
                LockIntoTwoEffectCombo(ActiveSubstanceComboState.AnxietyAttack_WeedNicOrCaff, keepIndividualBoosters: false, anxietyAttackBooster);
                return;
            }
            if (alc && nic)
            {
                LockIntoTwoEffectCombo(ActiveSubstanceComboState.DizzyBuzz_AlcoholNicotine, keepIndividualBoosters: false, dizzyBuzzBooster);
                return;
            }
        }

        // 3. 0 OR 1 SUBSTANCE ACTIVE
        if (!isTwoEffectComboLocked)
        {
            ApplyComboStateAndBoosters(ActiveSubstanceComboState.None, keepIndividualBoosters: true, null);
        }
    }

    private void LockIntoTwoEffectCombo(
        ActiveSubstanceComboState comboState,
        bool keepIndividualBoosters,
        BoosterObject targetComboBooster)
    {
        isTwoEffectComboLocked = true;
        isTwoEffectComboUnwinding = false;
        comboRemainingTimer = Mathf.Max(1f, combinedDuration); // Default 15 seconds

        // Stop individual timers on both consumables so the first one NEVER ends early
        for (int i = 0; i < activeConsumables.Count; i++)
        {
            if (activeConsumables[i] != null)
            {
                activeConsumables[i].LockIntoCombinedCycle();
            }
        }

        ApplyComboStateAndBoosters(comboState, keepIndividualBoosters, targetComboBooster);
    }

    private void FinishTwoEffectComboAndResetProps()
    {
        currentComboIntensity = 0f;
        DeactivateAllComboBoosters();

        List<TimedConsumableBooster> snapshot = new List<TimedConsumableBooster>(activeConsumables);
        activeConsumables.Clear();

        isTwoEffectComboLocked = false;
        isTwoEffectComboUnwinding = false;
        comboRemainingTimer = 0f;
        currentComboState = ActiveSubstanceComboState.None;

        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] != null)
            {
                snapshot[i].ForceTerminateAndResetToUnused();
            }
        }
    }

    private void ApplyComboStateAndBoosters(
        ActiveSubstanceComboState newState,
        bool keepIndividualBoosters,
        BoosterObject targetComboBooster)
    {
        currentComboState = newState;

        for (int i = 0; i < activeConsumables.Count; i++)
        {
            TimedConsumableBooster c = activeConsumables[i];
            if (c != null)
            {
                if (c.activeBoosterObject != null)
                {
                    if (keepIndividualBoosters)
                        c.activeBoosterObject.ApplyBooster();
                    else
                        c.activeBoosterObject.RevertBooster();
                }

                c.RefreshVisualSuppression();
            }
        }

        SetComboBoosterState(hyperfocusBooster, targetComboBooster == hyperfocusBooster && newState != ActiveSubstanceComboState.None);
        SetComboBoosterState(heartAttackBooster, targetComboBooster == heartAttackBooster && newState != ActiveSubstanceComboState.None);
        SetComboBoosterState(hanyingerBooster, targetComboBooster == hanyingerBooster && newState != ActiveSubstanceComboState.None);
        SetComboBoosterState(anxietyAttackBooster, targetComboBooster == anxietyAttackBooster && newState != ActiveSubstanceComboState.None);
        SetComboBoosterState(dizzyBuzzBooster, targetComboBooster == dizzyBuzzBooster && newState != ActiveSubstanceComboState.None);
        SetComboBoosterState(threeEffectOverdoseBooster, targetComboBooster == threeEffectOverdoseBooster && newState != ActiveSubstanceComboState.None);
    }

    private void SetComboBoosterState(BoosterObject booster, bool shouldBeActive)
    {
        if (booster == null) return;

        if (shouldBeActive)
        {
            if (!booster.gameObject.activeSelf) booster.gameObject.SetActive(true);
            booster.ApplyBooster();
        }
        else
        {
            booster.RevertBooster();
            if (booster.gameObject.activeSelf) booster.gameObject.SetActive(false);
        }
    }

    private void DeactivateAllComboBoosters()
    {
        SetComboBoosterState(hyperfocusBooster, false);
        SetComboBoosterState(heartAttackBooster, false);
        SetComboBoosterState(hanyingerBooster, false);
        SetComboBoosterState(anxietyAttackBooster, false);
        SetComboBoosterState(dizzyBuzzBooster, false);
        SetComboBoosterState(threeEffectOverdoseBooster, false);
    }

    // =========================================================================
    // 3-EFFECT SUPER OVERDOSE SEQUENCE
    // =========================================================================

    [ContextMenu("Trigger 3-Effect Overdose Sequence Now")]
    public void TriggerOverdoseSequenceManually()
    {
        if (!isOverdoseSequenceRunning)
        {
            StartCoroutine(ThreeEffectOverdoseRoutine());
        }
    }

    private IEnumerator ThreeEffectOverdoseRoutine()
    {
        isOverdoseSequenceRunning = true;
        EnsureOverlayInitialized();

        SetGlobalInputBlocked(true);

        for (int i = 0; i < activeConsumables.Count; i++)
        {
            if (activeConsumables[i] != null)
                activeConsumables[i].LockIntoCombinedCycle();
        }

        ApplyComboStateAndBoosters(ActiveSubstanceComboState.SuperOverdose_ThreeEffects, keepIndividualBoosters: false, threeEffectOverdoseBooster);

        EnsureRTs(Screen.width, Screen.height);
        bool hasVideo = StartHallucinationVideoPlayback();

        float fadeInDur = Mathf.Max(0.1f, superOverdoseSettings.smearAndVideoFadeInDuration);
        float elapsed = 0f;
        float startIntensity = currentComboIntensity;
        while (elapsed < fadeInDur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeInDur);
            currentComboIntensity = Mathf.SmoothStep(startIntensity, 1f, t);
            currentVideoFade = hasVideo ? Mathf.SmoothStep(0f, superOverdoseSettings.maxVideoOpacity, t) : 0f;
            currentBlackoutFade = 0f;
            yield return null;
        }

        currentComboIntensity = 1f;
        currentVideoFade = hasVideo ? superOverdoseSettings.maxVideoOpacity : 0f;

        float playDur = Mathf.Max(0.1f, superOverdoseSettings.hallucinationPlayDuration);
        elapsed = 0f;
        while (elapsed < playDur)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        float blackDur = Mathf.Max(0.1f, superOverdoseSettings.fadeToBlackDuration);
        elapsed = 0f;
        while (elapsed < blackDur)
        {
            elapsed += Time.unscaledDeltaTime;
            currentBlackoutFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / blackDur));
            yield return null;
        }
        currentBlackoutFade = 1f;

        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
        }

        DeactivateAllComboBoosters();

        List<TimedConsumableBooster> snapshot = new List<TimedConsumableBooster>(activeConsumables);
        activeConsumables.Clear();
        foreach (TimedConsumableBooster c in snapshot)
        {
            if (c != null)
            {
                c.ForceTerminateAndResetToUnused();
            }
        }

        MaxOutAllBarsExceptDepression();

        currentComboState = ActiveSubstanceComboState.None;
        currentComboIntensity = 0f;
        currentVideoFade = 0f;
        hasInitializedBuffers = false;

        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, superOverdoseSettings.unconsciousHoldDuration));

        float wakeDur = Mathf.Max(0.1f, superOverdoseSettings.wakeUpFadeDuration);
        elapsed = 0f;
        while (elapsed < wakeDur)
        {
            elapsed += Time.unscaledDeltaTime;
            currentBlackoutFade = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(elapsed / wakeDur));
            yield return null;
        }

        currentBlackoutFade = 0f;
        isOverdoseSequenceRunning = false;

        SetGlobalInputBlocked(false);
    }

    private void MaxOutAllBarsExceptDepression()
    {
        if (barManager == null) barManager = FindObjectOfType<BarManager>();
        if (barManager == null) return;

        barManager.currentHunger = barManager.maxHunger;
        barManager.currentNeeds = barManager.maxNeeds;
        barManager.currentSleep = barManager.maxSleep;
        barManager.currentDirtiness = barManager.maxDirtiness;
    }

    private void SetGlobalInputBlocked(bool blocked)
    {
        if (raycastBlockerImage != null)
        {
            raycastBlockerImage.raycastTarget = blocked;
            raycastBlockerImage.gameObject.SetActive(blocked);
        }

        InteractableObject[] allInteractables = FindObjectsOfType<InteractableObject>(false);
        foreach (InteractableObject io in allInteractables)
        {
            if (io != null) io.enabled = !blocked;
        }
    }

    // =========================================================================
    // MULTI-BUFFER RENDERING & UPDATE LOOP
    // =========================================================================

    private void EnsureOverlayInitialized()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (combinationShader == null) combinationShader = Shader.Find(ComboShaderName);
        if (combinationShader == null) return;

        if (comboMaterial == null)
        {
            comboMaterial = new Material(combinationShader)
            {
                name = "SubstanceCombinationMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (comboCanvas == null)
        {
            GameObject canvasObj = new GameObject("SubstanceCombination_OverlayCanvas");
            canvasObj.transform.SetParent(transform, false);

            comboCanvas = canvasObj.AddComponent<Canvas>();
            comboCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            comboCanvas.sortingOrder = 32765;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();

            GameObject rawObj = new GameObject("ComboScreen_RawImage");
            rawObj.transform.SetParent(comboCanvas.transform, false);

            comboOverlayImage = rawObj.AddComponent<RawImage>();
            comboOverlayImage.raycastTarget = false;
            comboOverlayImage.material = comboMaterial;

            RectTransform rt = comboOverlayImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            GameObject blockerObj = new GameObject("Overdose_RaycastBlocker");
            blockerObj.transform.SetParent(comboCanvas.transform, false);
            raycastBlockerImage = blockerObj.AddComponent<Image>();
            raycastBlockerImage.color = new Color(0f, 0f, 0f, 0f);
            raycastBlockerImage.raycastTarget = false;

            RectTransform bRt = raycastBlockerImage.rectTransform;
            bRt.anchorMin = Vector2.zero;
            bRt.anchorMax = Vector2.one;
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = Vector2.zero;
            blockerObj.SetActive(false);

            comboCanvas.gameObject.SetActive(false);
        }

        if (videoPlayer == null)
        {
            videoPlayer = gameObject.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
            videoPlayer.isLooping = true;
            videoPlayer.skipOnDrop = true;
            videoPlayer.waitForFirstFrame = false;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            videoPlayer.controlledAudioTrackCount = 1;
            videoPlayer.EnableAudioTrack(0, true);
            videoPlayer.SetDirectAudioMute(0, false);
            videoPlayer.SetDirectAudioVolume(0, 1f);
        }
    }

    private bool StartHallucinationVideoPlayback()
    {
        if (videoPlayer == null) return false;

        videoPlayer.targetTexture = videoRT;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        videoPlayer.controlledAudioTrackCount = 1;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetDirectAudioMute(0, false);
        videoPlayer.SetDirectAudioVolume(0, 1f);

        string fileName = string.IsNullOrEmpty(superOverdoseSettings.streamingAssetsVideoFileName)
            ? "Overdose.mp4"
            : superOverdoseSettings.streamingAssetsVideoFileName.Trim();

        bool useStreamingUrl = Application.platform == RuntimePlatform.WebGLPlayer
            || superOverdoseSettings.hallucinationVideoClip == null;

#if !UNITY_EDITOR
        useStreamingUrl = true;
#endif

        if (useStreamingUrl && !string.IsNullOrEmpty(fileName))
        {
            string baseUrl = Application.streamingAssetsPath.TrimEnd('/', '\\');
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = baseUrl + "/" + fileName;
            videoPlayer.Play();
            return true;
        }

        if (superOverdoseSettings.hallucinationVideoClip != null)
        {
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = superOverdoseSettings.hallucinationVideoClip;
            videoPlayer.Play();
            return true;
        }

        return false;
    }

    private void EnsureRTs(int width, int height)
    {
        width = Mathf.Max(64, width);
        height = Mathf.Max(64, height);

        if (screenCaptureRT != null && screenCaptureRT.width == width && screenCaptureRT.height == height)
            return;

        ReleaseRTs();

        screenCaptureRT = CreateRT(width, height, 24, "Combo_ScreenCaptureRT");

        int halfW = Mathf.Max(64, width / 2);
        int halfH = Mathf.Max(64, height / 2);

        for (int i = 0; i < EchoBufferCount; i++)
        {
            echoHistoryRTs[i] = CreateRT(halfW, halfH, 0, "Combo_EchoRT_" + i);
        }

        smearAccumRT_A = CreateRT(halfW, halfH, 0, "Combo_SmearAccum_A");
        smearAccumRT_B = CreateRT(halfW, halfH, 0, "Combo_SmearAccum_B");
        videoRT = CreateVideoRT(width, height, "Combo_HallucinationVideoRT");

        if (comboOverlayImage != null) comboOverlayImage.texture = screenCaptureRT;
        if (videoPlayer != null) videoPlayer.targetTexture = videoRT;
        hasInitializedBuffers = false;
    }

    private RenderTexture CreateRT(int w, int h, int depth, string rtName)
    {
        RenderTexture rt = new RenderTexture(w, h, depth, RenderTextureFormat.DefaultHDR)
        {
            name = rtName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
        return rt;
    }

    private RenderTexture CreateVideoRT(int w, int h, string rtName)
    {
        // Standard 8-bit RGBA (RenderTextureFormat.Default) is required for WebGL HTML5 video texture uploads
        RenderTexture rt = new RenderTexture(w, h, 0, RenderTextureFormat.Default)
        {
            name = rtName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;
        return rt;
    }

    private void ReleaseRTs()
    {
        if (screenCaptureRT != null) { screenCaptureRT.Release(); Destroy(screenCaptureRT); screenCaptureRT = null; }
        for (int i = 0; i < EchoBufferCount; i++)
        {
            if (echoHistoryRTs[i] != null) { echoHistoryRTs[i].Release(); Destroy(echoHistoryRTs[i]); echoHistoryRTs[i] = null; }
        }
        if (smearAccumRT_A != null) { smearAccumRT_A.Release(); Destroy(smearAccumRT_A); smearAccumRT_A = null; }
        if (smearAccumRT_B != null) { smearAccumRT_B.Release(); Destroy(smearAccumRT_B); smearAccumRT_B = null; }
        if (videoRT != null) { videoRT.Release(); Destroy(videoRT); videoRT = null; }
    }

    private void Update()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (Time.timeScale <= 0.0001f || (PauseManager.HasInstance && PauseManager.IsGamePaused))
        {
            // Hide the frozen overlay (and its raycast blocker) so the pause menu is never covered
            if (comboCanvas != null && comboCanvas.gameObject.activeSelf) comboCanvas.gameObject.SetActive(false);
            return;
        }

        float dt = Time.unscaledDeltaTime;

        if (!isOverdoseSequenceRunning)
        {
            if (isTwoEffectComboLocked)
            {
                if (!isTwoEffectComboUnwinding)
                {
                    // Ramp in & hold for 15 seconds (combinedDuration)
                    currentComboIntensity = Mathf.MoveTowards(currentComboIntensity, 1f, comboFadeSpeed * dt);
                    comboRemainingTimer -= dt;
                    if (comboRemainingTimer <= 0f)
                    {
                        comboRemainingTimer = 0f;
                        isTwoEffectComboUnwinding = true;
                    }
                }
                else
                {
                    // The original smooth unwind back to 0 at comboFadeSpeed!
                    currentComboIntensity = Mathf.MoveTowards(currentComboIntensity, 0f, comboFadeSpeed * dt);
                    if (currentComboIntensity <= 0.001f)
                    {
                        FinishTwoEffectComboAndResetProps();
                    }
                }
                currentVideoFade = 0f;
                currentBlackoutFade = 0f;
            }
            else
            {
                // Live Inspector Preview Dropdown
                float target = (livePreviewComboDropdown != ActiveSubstanceComboState.None) ? 1f : 0f;
                currentComboIntensity = Mathf.MoveTowards(currentComboIntensity, target, comboFadeSpeed * dt);

                if (livePreviewComboDropdown == ActiveSubstanceComboState.SuperOverdose_ThreeEffects &&
                    (superOverdoseSettings.hallucinationVideoClip != null || !string.IsNullOrEmpty(superOverdoseSettings.streamingAssetsVideoFileName)))
                {
                    EnsureRTs(Screen.width, Screen.height);
                    if (videoPlayer != null && !videoPlayer.isPlaying)
                    {
                        StartHallucinationVideoPlayback();
                    }
                    currentVideoFade = superOverdoseSettings.maxVideoOpacity * currentComboIntensity;
                }
                else
                {
                    currentVideoFade = 0f;
                }
                currentBlackoutFade = 0f;
            }
        }

        bool shouldShowCanvas = currentComboIntensity > 0.001f || currentBlackoutFade > 0.001f || isOverdoseSequenceRunning;
        if (!shouldShowCanvas)
        {
            if (comboCanvas != null && comboCanvas.gameObject.activeSelf)
                comboCanvas.gameObject.SetActive(false);

            RemoveCameraShake();
            hasInitializedBuffers = false;
            return;
        }

        EnsureOverlayInitialized();
        if (comboCanvas != null && !comboCanvas.gameObject.activeSelf)
            comboCanvas.gameObject.SetActive(true);

        UpdateCameraMotionEchoes();
    }

    private void UpdateCameraMotionEchoes()
    {
        if (targetCamera == null || !hasInitializedBuffers) return;

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

        echoPulsePhase += dt * 26f;
        float pulse1 = 1.0f + 0.25f * Mathf.Sin(echoPulsePhase);
        float pulse2 = 1.0f + 0.25f * Mathf.Sin(echoPulsePhase - 2.094f);
        float pulse3 = 1.0f + 0.25f * Mathf.Sin(echoPulsePhase - 4.188f);

        echoVelocity1 = Vector2.ClampMagnitude(echoVelocity1 * pulse1, maxEchoUVOffset);
        echoVelocity2 = Vector2.ClampMagnitude(echoVelocity2 * pulse2, maxEchoUVOffset);
        echoVelocity3 = Vector2.ClampMagnitude(echoVelocity3 * pulse3, maxEchoUVOffset);
    }

    private void LateUpdate()
    {
        bool shouldShowCanvas = currentComboIntensity > 0.001f || currentBlackoutFade > 0.001f || isOverdoseSequenceRunning;
        if (!shouldShowCanvas || targetCamera == null || comboMaterial == null) return;

        if (Time.timeScale <= 0.0001f || (PauseManager.HasInstance && PauseManager.IsGamePaused))
        {
            RemoveCameraShake();
            return;
        }

        ActiveSubstanceComboState activeState = CurrentComboState;

        RemoveCameraShake();
        ApplyComboCameraMotion(activeState);

        EnsureRTs(Screen.width, Screen.height);

        RenderTexture prev = targetCamera.targetTexture;
        targetCamera.targetTexture = screenCaptureRT;
        targetCamera.Render();
        targetCamera.targetTexture = prev;

        if (!hasInitializedBuffers)
        {
            for (int i = 0; i < EchoBufferCount; i++)
            {
                Graphics.Blit(screenCaptureRT, echoHistoryRTs[i]);
            }
            Graphics.Blit(screenCaptureRT, smearAccumRT_A);
            Graphics.Blit(screenCaptureRT, smearAccumRT_B);
            lastCamRotation = targetCamera.transform.rotation;
            lastCamPosition = targetCamera.transform.position;
            hasInitializedBuffers = true;
        }

        echoCaptureTimer += Time.unscaledDeltaTime;
        if (echoCaptureTimer >= Mathf.Max(0.01f, echoFrameStepInterval))
        {
            echoCaptureTimer = 0f;
            currentEchoWriteIdx = (currentEchoWriteIdx + 1) % EchoBufferCount;
            Graphics.Blit(screenCaptureRT, echoHistoryRTs[currentEchoWriteIdx]);
        }

        int idx1 = (currentEchoWriteIdx - 1 + EchoBufferCount) % EchoBufferCount;
        int idx2 = (currentEchoWriteIdx - 3 + EchoBufferCount) % EchoBufferCount;
        int idx3 = (currentEchoWriteIdx - 6 + EchoBufferCount) % EchoBufferCount;

        int shaderMode = (int)(activeState != ActiveSubstanceComboState.None
            ? activeState
            : ActiveSubstanceComboState.SuperOverdose_ThreeEffects);

        float motionMagnitude = Mathf.Clamp01(
            (echoVelocity1.magnitude + echoVelocity2.magnitude + echoVelocity3.magnitude) /
            Mathf.Max(0.001f, maxEchoUVOffset * 0.45f));
        float activeEchoWeight = Mathf.Lerp(0.35f, 1.0f, motionMagnitude);

        BindModeParametersToMaterial(activeState);

        comboMaterial.SetTexture(MainTexId, screenCaptureRT);
        comboMaterial.SetTexture(EchoTex1Id, echoHistoryRTs[idx1]);
        comboMaterial.SetTexture(EchoTex2Id, echoHistoryRTs[idx2]);
        comboMaterial.SetTexture(EchoTex3Id, echoHistoryRTs[idx3]);

        Texture activeVideoTex = Texture2D.blackTexture;
        if (videoPlayer != null && videoPlayer.texture != null)
        {
            activeVideoTex = videoPlayer.texture;
        }
        else if (videoRT != null)
        {
            activeVideoTex = videoRT;
        }
        comboMaterial.SetTexture(VideoTexId, activeVideoTex);

        comboMaterial.SetFloat(IntensityId, currentComboIntensity);
        comboMaterial.SetInt(ComboModeId, shaderMode);
        comboMaterial.SetFloat(VideoFadeId, currentVideoFade);
        comboMaterial.SetFloat(BlackoutFadeId, currentBlackoutFade);

        comboMaterial.SetVector(GhostOffset1Id, new Vector4(echoVelocity1.x, echoVelocity1.y, activeEchoWeight, 0f));
        comboMaterial.SetVector(GhostOffset2Id, new Vector4(echoVelocity2.x, echoVelocity2.y, activeEchoWeight, 0f));
        comboMaterial.SetVector(GhostOffset3Id, new Vector4(echoVelocity3.x, echoVelocity3.y, activeEchoWeight, 0f));

        RenderTexture smearSource = smearPingPong ? smearAccumRT_A : smearAccumRT_B;
        RenderTexture smearDest = smearPingPong ? smearAccumRT_B : smearAccumRT_A;
        smearPingPong = !smearPingPong;

        comboMaterial.SetTexture(SmearAccumTexId, smearSource);
        Graphics.Blit(screenCaptureRT, smearDest, comboMaterial, 0);
        comboMaterial.SetTexture(SmearAccumTexId, smearDest);
    }

    private void BindModeParametersToMaterial(ActiveSubstanceComboState state)
    {
        comboMaterial.SetVector(SmearParamsId, new Vector4(
            superOverdoseSettings.smearBufferRetention,
            superOverdoseSettings.fluidFlowSpeed,
            superOverdoseSettings.fluidMeltDistortion * 0.2f,
            superOverdoseSettings.rainbowHueDrift * 0.045f
        ));

        switch (state)
        {
            case ActiveSubstanceComboState.Hyperfocus_NicotineCaffeine:
                comboMaterial.SetVector(ParamAId, new Vector4(
                    hyperfocusSettings.pulseSpeed,
                    hyperfocusSettings.pulseZoom,
                    hyperfocusSettings.centerSharpness,
                    hyperfocusSettings.peripheralBlur));
                comboMaterial.SetVector(ParamBId, new Vector4(
                    hyperfocusSettings.microTremor,
                    hyperfocusSettings.chromaticSplit,
                    hyperfocusSettings.saturationBoost,
                    hyperfocusSettings.contrastBoost));
                comboMaterial.SetVector(ParamCId, new Vector4(hyperfocusSettings.rimGlowStrength, 0f, 0f, 0f));
                comboMaterial.SetColor(ColorAId, hyperfocusSettings.primaryRimColor);
                comboMaterial.SetColor(ColorBId, hyperfocusSettings.secondaryRimColor);
                break;

            case ActiveSubstanceComboState.HeartAttack_AlcoholCaffeine:
                comboMaterial.SetVector(ParamAId, new Vector4(
                    heartAttackSettings.heartbeatRate,
                    heartAttackSettings.systolicZoomSlam,
                    heartAttackSettings.radialBlurStrength,
                    heartAttackSettings.chromaticStrength));
                comboMaterial.SetVector(ParamBId, new Vector4(
                    heartAttackSettings.ghostSplitAmount,
                    heartAttackSettings.greyscaleDrain,
                    heartAttackSettings.tunnelOpenRadius,
                    heartAttackSettings.tunnelSlamRadius));
                comboMaterial.SetColor(ColorAId, heartAttackSettings.arterialBloodColor);
                break;

            case ActiveSubstanceComboState.Hanyinger_AlcoholWeed:
                comboMaterial.SetVector(ParamAId, new Vector4(
                    hanyingerSettings.spinSpeed,
                    hanyingerSettings.spinMaxAngle,
                    hanyingerSettings.stomachHeaveAmplitude,
                    hanyingerSettings.blurRadius));
                comboMaterial.SetVector(ParamBId, new Vector4(
                    hanyingerSettings.trailBlendWeight,
                    hanyingerSettings.bileColorStrength,
                    hanyingerSettings.vignetteStrength,
                    0f));
                comboMaterial.SetColor(ColorAId, hanyingerSettings.primaryBileTint);
                comboMaterial.SetColor(ColorBId, hanyingerSettings.secondaryBileTint);
                break;

            case ActiveSubstanceComboState.AnxietyAttack_WeedNicOrCaff:
                comboMaterial.SetVector(ParamAId, new Vector4(
                    anxietyAttackSettings.panicPulseSpeed,
                    anxietyAttackSettings.barrelWarpStrength,
                    anxietyAttackSettings.nerveJitterAmount,
                    anxietyAttackSettings.chromaticSplit));
                comboMaterial.SetVector(ParamBId, new Vector4(
                    anxietyAttackSettings.coldContrastBoost,
                    anxietyAttackSettings.desaturation,
                    anxietyAttackSettings.paranoiaTunnelStrength,
                    0f));
                comboMaterial.SetColor(ColorAId, anxietyAttackSettings.coldPanicTint);
                break;

            case ActiveSubstanceComboState.DizzyBuzz_AlcoholNicotine:
                comboMaterial.SetVector(ParamAId, new Vector4(
                    dizzyBuzzSettings.swaySpeed,
                    dizzyBuzzSettings.gravitySinkAmplitude,
                    dizzyBuzzSettings.blurAmount,
                    dizzyBuzzSettings.chromaticSplit));
                comboMaterial.SetVector(ParamBId, new Vector4(
                    dizzyBuzzSettings.echoTrailStrength,
                    dizzyBuzzSettings.vignetteStrength,
                    0f,
                    0f));
                comboMaterial.SetColor(ColorAId, dizzyBuzzSettings.warmAmberTint);
                break;

            default:
            case ActiveSubstanceComboState.SuperOverdose_ThreeEffects:
                comboMaterial.SetVector(ParamAId, new Vector4(
                    superOverdoseSettings.fluidFlowSpeed,
                    superOverdoseSettings.fluidMeltDistortion,
                    superOverdoseSettings.vortexSwirlAngle,
                    superOverdoseSettings.blurRadius));
                comboMaterial.SetVector(ParamBId, new Vector4(
                    superOverdoseSettings.persistentSmearWeight,
                    superOverdoseSettings.rainbowHueDrift,
                    superOverdoseSettings.chromaticSpread,
                    0f));
                break;
        }
    }

    private void ApplyComboCameraMotion(ActiveSubstanceComboState state)
    {
        float t = Time.unscaledTime;
        switch (state)
        {
            case ActiveSubstanceComboState.Hyperfocus_NicotineCaffeine:
                float buzz = hyperfocusSettings.cameraBuzzDegrees;
                appliedCameraShake = new Vector3(
                    (Mathf.PerlinNoise(t * 55f, 0f) * 2f - 1f) * buzz,
                    (Mathf.PerlinNoise(0f, t * 55f) * 2f - 1f) * buzz,
                    0f
                ) * currentComboIntensity;
                break;

            case ActiveSubstanceComboState.HeartAttack_AlcoholCaffeine:
                float cycle = Mathf.Repeat(t * heartAttackSettings.heartbeatRate, 1.0f);
                float systole = Mathf.Exp(-Mathf.Pow((cycle - 0.12f) * 18.0f, 2.0f));
                float diastole = Mathf.Exp(-Mathf.Pow((cycle - 0.28f) * 14.0f, 2.0f)) * 0.75f;
                float thump = Mathf.Clamp01(systole + diastole);
                float deg = heartAttackSettings.cameraThumpDegrees;
                appliedCameraShake = new Vector3(-thump * deg, Mathf.Sin(t * 6f) * (deg * 0.35f), Mathf.Sin(t * 3.5f) * (deg * 0.75f)) * currentComboIntensity;
                break;

            case ActiveSubstanceComboState.Hanyinger_AlcoholWeed:
                float vDeg = hanyingerSettings.cameraVertigoDegrees;
                float sSpd = hanyingerSettings.spinSpeed;
                appliedCameraShake = new Vector3(
                    Mathf.Sin(t * sSpd * 1.3f) * (vDeg * 0.55f),
                    Mathf.Cos(t * sSpd) * (vDeg * 0.4f),
                    Mathf.Sin(t * sSpd) * vDeg
                ) * currentComboIntensity;
                break;

            case ActiveSubstanceComboState.AnxietyAttack_WeedNicOrCaff:
                float aDeg = anxietyAttackSettings.cameraTremorDegrees;
                appliedCameraShake = new Vector3(
                    (Mathf.PerlinNoise(t * 48f, 0f) * 2f - 1f) * aDeg,
                    (Mathf.PerlinNoise(0f, t * 48f) * 2f - 1f) * aDeg,
                    (Mathf.PerlinNoise(t * 48f, t * 48f) * 2f - 1f) * (aDeg * 0.75f)
                ) * currentComboIntensity;
                break;

            case ActiveSubstanceComboState.DizzyBuzz_AlcoholNicotine:
                float dDeg = dizzyBuzzSettings.cameraSwayDegrees;
                appliedCameraShake = new Vector3(
                    Mathf.Sin(t * 1.9f) * (dDeg * 0.7f),
                    0f,
                    Mathf.Cos(t * 1.4f) * dDeg
                ) * currentComboIntensity;
                break;

            case ActiveSubstanceComboState.SuperOverdose_ThreeEffects:
                float mDeg = superOverdoseSettings.cameraMeltSwayDegrees;
                appliedCameraShake = new Vector3(
                    Mathf.Sin(t * 1.4f) * (mDeg * 0.5f),
                    Mathf.Cos(t * 1.1f) * (mDeg * 0.4f),
                    Mathf.Sin(t * 0.9f) * mDeg
                ) * currentComboIntensity * (1f - currentBlackoutFade);
                break;
        }

        if (appliedCameraShake.sqrMagnitude > 0.00001f && targetCamera != null)
        {
            targetCamera.transform.localRotation *= Quaternion.Euler(appliedCameraShake);
        }
    }

    private void RemoveCameraShake()
    {
        if (appliedCameraShake.sqrMagnitude > 0.00001f && targetCamera != null)
        {
            targetCamera.transform.localRotation *= Quaternion.Inverse(Quaternion.Euler(appliedCameraShake));
            appliedCameraShake = Vector3.zero;
        }
    }
}
