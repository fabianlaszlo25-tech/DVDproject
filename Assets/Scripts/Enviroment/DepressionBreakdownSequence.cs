using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Triggered automatically when the Depression bar in <see cref="BarManager"/> reaches 100%.
/// 1. Blocks world interaction while slowly ramping up the Heart-Attack breakdown visual.
/// 2. Hard-cuts to pitch black:
///    - Fully disables the Player GameObject and the player's AudioListener.
///    - Enables a replacement isolated AudioListener and pauses all other scene audio so ONLY
///      this script's AudioSource can be heard.
/// 3. Waits in dead silence, plays the Gun Cocking sound, waits a tasteful delay,
///    plays the Gunshot sound, and pops up the Ending UI!
/// </summary>
public class DepressionBreakdownSequence : MonoBehaviour
{
    private const string ShaderName = "Custom/DepressionBreakdownShader";

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int EchoTex1Id = Shader.PropertyToID("_EchoTex1");
    private static readonly int EchoTex2Id = Shader.PropertyToID("_EchoTex2");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int BlackCutId = Shader.PropertyToID("_BlackCut");
    private static readonly int PulsePhaseId = Shader.PropertyToID("_PulsePhase");

    [Header("Core References (Auto-Filled if left empty)")]
    public BarManager barManager;
    public Camera targetCamera;

    [Header("Player & AudioListener Swap (Happens on Cut to Black)")]
    [Tooltip("Drag your root Player GameObject here. It will be completely disabled the moment the screen cuts to black.")]
    public GameObject playerRootObject;
    [Tooltip("Auto-found if left empty. Disabled when the screen cuts to black and replaced by the isolated listener.")]
    public AudioListener playerAudioListener;
    [Tooltip("Additional scripts to disable immediately when the ramp-up starts (so the player can't walk away during the heart attack).")]
    public MonoBehaviour[] scriptsToDisableDuringRampUp;

    [Header("UI To Pop Up On Gunshot")]
    [Tooltip("Drag the UI GameObject (Panel/Canvas) that pops up when the gunshot fires!")]
    public GameObject endingPopupUI;
    [Tooltip("If true, unlocks and shows the mouse cursor when the UI pops up.")]
    public bool unlockCursorWhenUIPopsUp = true;
    [Tooltip("If true, switches CRT to duringpaused.mat when ending popup UI appears. If false, uses duringgameplay.mat.")]
    public bool usePausedCrtOnEnding = false;

    [Header("Ending Audio Clips")]
    [Tooltip("Played first in the pitch-black silence.")]
    public AudioClip gunCockingSound;
    [Range(0f, 1f)] public float gunCockingVolume = 1.0f;

    [Tooltip("Played after the tasteful delay following the gun cock.")]
    public AudioClip gunshotSound;
    [Range(0f, 1f)] public float gunshotVolume = 1.0f;

    [Tooltip("Optional heartbeat loop that speeds up during the ramp-up and cuts off on black.")]
    public AudioClip optionalHeartbeatLoop;
    [Range(0f, 1f)] public float heartbeatVolume = 0.85f;

    [Header("Sequence Timings (Seconds)")]
    [Tooltip("How many seconds the Heart-Attack-like breakdown slowly ramps up before cutting to black.")]
    public float heartAttackRampDuration = 6.5f;
    [Tooltip("Seconds of dead silence in pitch black BEFORE the gun cocking sound plays.")]
    public float silenceBeforeGunCock = 1.2f;
    [Tooltip("Tasteful pause in seconds AFTER the gun cocking sound finishes before the gunshot fires.")]
    public float tastefulDelayAfterCockFinishes = 1.4f;
    [Tooltip("Seconds after the gunshot fires before the UI pops up (0 = instant pop-up right on the gunshot!).")]
    public float delayAfterGunshotBeforeUI = 0.0f;

    [Header("Heart-Attack Ramp Visual & Camera Settings")]
    [Tooltip("Starting heartbeat speed (beats per second) at the beginning of the ramp.")]
    public float startPulseFrequency = 1.1f;
    [Tooltip("Peak frantic heartbeat speed right before cutting to black.")]
    public float endPulseFrequency = 3.2f;
    [Tooltip("Physical camera chest-thump & collapse tilt in degrees.")]
    public float maxCameraCollapseDegrees = 6.5f;

    [Header("Shader Reference")]
    public Shader breakdownShader;

    private Material breakdownMaterial;
    private Canvas overlayCanvas;
    private RawImage overlayImage;
    private Image raycastBlocker;

    private RenderTexture currentFrameRT;
    private RenderTexture echoRT1;
    private RenderTexture echoRT2;
    private float echoTimer;
    private bool echoToggle;

    private GameObject isolatedAudioObject;
    private AudioListener isolatedEndingListener;
    private AudioSource isolatedEndingSfxSource;
    private AudioSource heartbeatSource;

    private bool hasTriggered;
    private bool isRenderingActive;
    private bool isCutToBlack;
    private float currentIntensity;
    private float currentBlackCut;
    private float accumulatedPulsePhase;
    private Vector3 appliedCameraEuler;

    public bool HasTriggered => hasTriggered;

    private void Awake()
    {
        if (barManager == null) barManager = FindObjectOfType<BarManager>();
        if (targetCamera == null) targetCamera = Camera.main;
        if (playerAudioListener == null) playerAudioListener = FindObjectOfType<AudioListener>();

        // Ensure the popup UI starts hidden
        if (endingPopupUI != null && endingPopupUI.activeSelf)
        {
            endingPopupUI.SetActive(false);
        }

        // Preload ending audio clips immediately so WebGL never has to decode them on-the-fly during the blackout sequence
        if (gunCockingSound != null && gunCockingSound.loadState == AudioDataLoadState.Unloaded)
        {
            gunCockingSound.LoadAudioData();
        }
        if (gunshotSound != null && gunshotSound.loadState == AudioDataLoadState.Unloaded)
        {
            gunshotSound.LoadAudioData();
        }
        if (optionalHeartbeatLoop != null && optionalHeartbeatLoop.loadState == AudioDataLoadState.Unloaded)
        {
            optionalHeartbeatLoop.LoadAudioData();
        }

        // Create the isolated audio rig (starts with its AudioListener disabled until the black cut)
        isolatedAudioObject = new GameObject("IsolatedEndingAudioRig");
        isolatedAudioObject.transform.SetParent(transform, false);

        isolatedEndingListener = isolatedAudioObject.AddComponent<AudioListener>();
        isolatedEndingListener.enabled = false;

        isolatedEndingSfxSource = isolatedAudioObject.AddComponent<AudioSource>();
        isolatedEndingSfxSource.playOnAwake = false;
        isolatedEndingSfxSource.loop = false;
        isolatedEndingSfxSource.spatialBlend = 0f;
        isolatedEndingSfxSource.priority = 0;
        isolatedEndingSfxSource.ignoreListenerPause = true;
        isolatedEndingSfxSource.ignoreListenerVolume = false;

        heartbeatSource = gameObject.AddComponent<AudioSource>();
        heartbeatSource.playOnAwake = false;
        heartbeatSource.loop = true;
        heartbeatSource.spatialBlend = 0f;

        EnsureOverlayInitialized();
    }

    private void OnDestroy()
    {
        // Make sure global audio pause is restored if the scene reloads or exits Play Mode
        AudioListener.pause = false;
        CrtPostProcessManager.SetPausedCrtActive(false);

        RemoveCameraOffset();
        ReleaseRTs();
        if (breakdownMaterial != null) Destroy(breakdownMaterial);
        if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
    }

    private void Update()
    {
        if (hasTriggered) return;

        if (barManager == null) barManager = FindObjectOfType<BarManager>();
        if (barManager != null && barManager.currentDepression >= barManager.maxDepression - 0.005f)
        {
            TriggerDepressionBreakdown();
        }
    }

    /// <summary>
    /// Automatically called when Depression reaches 100%, or can be triggered manually from the Inspector ContextMenu.
    /// </summary>
    [ContextMenu("Trigger Depression Breakdown Now")]
    public void TriggerDepressionBreakdown()
    {
        if (hasTriggered) return;
        hasTriggered = true;

        // Immediately terminate any active booster combinations or screen distortion effects
        SubstanceCombinationManager.StopAndClearAllEffects();
        TimedConsumableBooster.StopAllActiveBoosters();

        StartCoroutine(BreakdownRoutine());
    }

    private IEnumerator BreakdownRoutine()
    {
        EnsureOverlayInitialized();
        isRenderingActive = true;
        isCutToBlack = false;

        if (overlayCanvas != null)
        {
            overlayCanvas.gameObject.SetActive(true);
        }

        // 1. Block world interactions and freeze movement during the heart-attack ramp-up
        //    (Player object & original AudioListener stay enabled here until the black cut!)
        BlockRampUpInteractions();

        if (optionalHeartbeatLoop != null && heartbeatSource != null)
        {
            heartbeatSource.clip = optionalHeartbeatLoop;
            heartbeatSource.volume = 0.1f;
            heartbeatSource.pitch = 0.9f;
            heartbeatSource.Play();
        }

        // 2. Slowly ramp up the Heart-Attack breakdown effect
        float rampDur = Mathf.Max(0.5f, heartAttackRampDuration);
        float elapsed = 0f;
        while (elapsed < rampDur)
        {
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            float norm = Mathf.Clamp01(elapsed / rampDur);

            currentIntensity = Mathf.SmoothStep(0f, 1f, norm);
            currentBlackCut = 0f;

            float pulseFreq = Mathf.Lerp(startPulseFrequency, endPulseFrequency, norm * norm);
            accumulatedPulsePhase += dt * pulseFreq;

            if (optionalHeartbeatLoop != null && heartbeatSource != null)
            {
                heartbeatSource.volume = Mathf.Lerp(0.2f, heartbeatVolume, norm);
                heartbeatSource.pitch = Mathf.Lerp(0.9f, 1.45f, norm * norm);
            }

            yield return null;
        }

        // 3. HARD CUT TO BLACK -> DISABLE PLAYER & ISOLATE ENDING AUDIO!
        currentIntensity = 1f;
        currentBlackCut = 1f;
        isCutToBlack = true;
        RemoveCameraOffset();

        // Ensure all combination and standalone booster effects are stopped
        SubstanceCombinationManager.StopAndClearAllEffects();
        TimedConsumableBooster.StopAllActiveBoosters();

        if (breakdownMaterial != null)
        {
            breakdownMaterial.SetFloat(IntensityId, 1f);
            breakdownMaterial.SetFloat(BlackCutId, 1f);
        }
        if (overlayImage != null)
        {
            overlayImage.material = breakdownMaterial;
            overlayImage.color = Color.white;
        }
        if (overlayCanvas != null)
        {
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 32766; // High priority above all boosters!
            overlayCanvas.gameObject.SetActive(true);
        }
        if (targetCamera != null)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = Color.black;
            targetCamera.cullingMask = (1 << 5); // UI layer only, completely culling 3D room
        }

        SwitchToIsolatedBlackoutState();

        // 4. Dead silence in pitch black before the gun cock
        if (silenceBeforeGunCock > 0.01f)
        {
            yield return WaitRealtimeSafe(silenceBeforeGunCock);
        }

        // 5. Play Gun Cocking Sound
        float cockClipLength = 0.6f;
        if (gunCockingSound != null)
        {
            if (gunCockingSound.loadState == AudioDataLoadState.Unloaded)
            {
                gunCockingSound.LoadAudioData();
            }
            if (gunCockingSound.length > 0.05f && gunCockingSound.length < 15f && !float.IsNaN(gunCockingSound.length))
            {
                cockClipLength = gunCockingSound.length;
            }
            PlayEndingClipReliable(gunCockingSound, gunCockingVolume);
        }

        // Wait for the cocking sound to finish + the tasteful delay before the gunshot
        float totalWaitBeforeShot = Mathf.Clamp(cockClipLength + Mathf.Max(0f, tastefulDelayAfterCockFinishes), 0.1f, 20f);
        if (totalWaitBeforeShot > 0.01f)
        {
            yield return WaitRealtimeSafe(totalWaitBeforeShot);
        }

        // 6. Play Gunshot Sound!
        if (gunshotSound != null)
        {
            if (gunshotSound.loadState == AudioDataLoadState.Unloaded)
            {
                gunshotSound.LoadAudioData();
            }
            PlayEndingClipReliable(gunshotSound, gunshotVolume);
        }

        if (delayAfterGunshotBeforeUI > 0.001f)
        {
            yield return WaitRealtimeSafe(Mathf.Clamp(delayAfterGunshotBeforeUI, 0f, 10f));
        }

        // 7. Pop up the Ending UI!
        if (raycastBlocker != null)
        {
            raycastBlocker.raycastTarget = false;
            raycastBlocker.gameObject.SetActive(false);
        }

        // Deactivate the fullscreen blackout overlay now that the ending popup UI is ready to show
        if (overlayCanvas != null)
        {
            overlayCanvas.gameObject.SetActive(false);
        }

        if (endingPopupUI != null)
        {
            // Ensure all parent GameObjects of endingPopupUI are active first
            Transform parentTransform = endingPopupUI.transform.parent;
            while (parentTransform != null)
            {
                if (!parentTransform.gameObject.activeSelf)
                {
                    parentTransform.gameObject.SetActive(true);
                }
                parentTransform = parentTransform.parent;
            }

            // Activate endingPopupUI FIRST before configuring Canvas sortingOrder (required in Unity Player/WebGL builds!)
            endingPopupUI.SetActive(true);

            Canvas popupParentCanvas = endingPopupUI.GetComponentInParent<Canvas>(true);
            if (popupParentCanvas != null && popupParentCanvas != overlayCanvas)
            {
                if (!popupParentCanvas.gameObject.activeSelf)
                {
                    popupParentCanvas.gameObject.SetActive(true);
                }
                popupParentCanvas.enabled = true;
                if (targetCamera != null)
                {
                    popupParentCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                    popupParentCanvas.worldCamera = targetCamera;
                    popupParentCanvas.planeDistance = 0.5f;
                }
                else
                {
                    popupParentCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                }

                bool hasParentCanvas = popupParentCanvas.transform.parent != null &&
                                       popupParentCanvas.transform.parent.GetComponentInParent<Canvas>(true) != null;
                popupParentCanvas.overrideSorting = hasParentCanvas;
                popupParentCanvas.sortingOrder = 32767; // Above the blackout overlay!
            }

            Canvas.ForceUpdateCanvases();
        }

        CrtPostProcessManager.SetPausedCrtActive(usePausedCrtOnEnding);

        if (EventSystem.current != null)
        {
            EventSystem.current.enabled = true;
        }

        if (unlockCursorWhenUIPopsUp)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Wait 1 frame and re-enforce Canvas sortingOrder & rebuild for WebGL UI canvas registration
        yield return null;
        if (endingPopupUI != null)
        {
            Canvas popupParentCanvas = endingPopupUI.GetComponentInParent<Canvas>(true);
            if (popupParentCanvas != null && popupParentCanvas != overlayCanvas)
            {
                popupParentCanvas.sortingOrder = 32767;
            }
            Canvas.ForceUpdateCanvases();
        }
    }

    private IEnumerator WaitRealtimeSafe(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.5f || float.IsNaN(dt))
            {
                dt = 0.016f;
            }
            elapsed += dt;
            yield return null;
        }
    }

    private void PlayEndingClipReliable(AudioClip clip, float volume)
    {
        if (clip == null || isolatedEndingSfxSource == null) return;

        AudioListener.pause = false;
        AudioListener.volume = GlobalAudioSettings.SfxVolume;

        EnsureActiveAudioListener();

        if (!isolatedEndingObjectActive())
        {
            isolatedAudioObject.SetActive(true);
        }
        isolatedEndingSfxSource.enabled = true;
        isolatedEndingSfxSource.mute = false;
        isolatedEndingSfxSource.volume = Mathf.Clamp01(volume);
        isolatedEndingSfxSource.pitch = 1f;
        isolatedEndingSfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private bool isolatedEndingObjectActive()
    {
        return isolatedAudioObject != null && isolatedAudioObject.activeInHierarchy;
    }

    private void EnsureActiveAudioListener()
    {
        if (playerAudioListener != null && playerAudioListener.enabled && playerAudioListener.gameObject.activeInHierarchy)
        {
            if (isolatedEndingListener != null) isolatedEndingListener.enabled = false;
            return;
        }

        AudioListener[] activeListeners = FindObjectsOfType<AudioListener>(false);
        foreach (AudioListener listener in activeListeners)
        {
            if (listener != null && listener.enabled && listener.gameObject.activeInHierarchy)
            {
                if (listener != isolatedEndingListener && isolatedEndingListener != null)
                {
                    isolatedEndingListener.enabled = false;
                }
                return;
            }
        }

        if (isolatedEndingListener != null)
        {
            isolatedEndingListener.enabled = true;
        }
    }

    private void BlockRampUpInteractions()
    {
        if (raycastBlocker != null)
        {
            raycastBlocker.raycastTarget = true;
            raycastBlocker.gameObject.SetActive(true);
        }

        if (scriptsToDisableDuringRampUp != null)
        {
            foreach (MonoBehaviour script in scriptsToDisableDuringRampUp)
            {
                if (script != null) script.enabled = false;
            }
        }

        InteractableObject[] allInteractables = FindObjectsOfType<InteractableObject>(false);
        foreach (InteractableObject io in allInteractables)
        {
            if (io != null) io.enabled = false;
        }
    }

    /// <summary>
    /// Executed the exact moment the screen cuts to black:
    /// - Stops heartbeat & all scene audio.
    /// - Disables the Player root object and old AudioListener.
    /// - Enables the replacement isolated AudioListener and sets AudioListener.pause = true
    ///   so literally NOTHING in the game can make a sound except isolatedEndingSfxSource!
    /// </summary>
    private void SwitchToIsolatedBlackoutState()
    {
        if (heartbeatSource != null && heartbeatSource.isPlaying)
        {
            heartbeatSource.Stop();
        }

        // Disable DynamicMusicManager so it does not restart background music tracks in Update()
        DynamicMusicManager[] musicManagers = FindObjectsOfType<DynamicMusicManager>(true);
        foreach (DynamicMusicManager dmm in musicManagers)
        {
            if (dmm != null) dmm.enabled = false;
        }

        // Disable any active substance screen effect overlays so their high sortingOrder canvases cannot cover the ending UI
        CaffeineScreenEffect[] caffEffects = FindObjectsOfType<CaffeineScreenEffect>(true);
        foreach (var e in caffEffects) { if (e != null) e.enabled = false; }
        DrunkScreenEffect[] drunkEffects = FindObjectsOfType<DrunkScreenEffect>(true);
        foreach (var e in drunkEffects) { if (e != null) e.enabled = false; }
        HighScreenEffect[] highEffects = FindObjectsOfType<HighScreenEffect>(true);
        foreach (var e in highEffects) { if (e != null) e.enabled = false; }
        NicotineScreenEffect[] nicEffects = FindObjectsOfType<NicotineScreenEffect>(true);
        foreach (var e in nicEffects) { if (e != null) e.enabled = false; }
        SubstanceCombinationManager[] comboManagers = FindObjectsOfType<SubstanceCombinationManager>(true);
        foreach (var cm in comboManagers) { if (cm != null) cm.enabled = false; }

        // Stop, mute, and disable all other AudioSources in the scene (without setting AudioListener.pause = true, which suspends WebGL WebAudio!)
        AudioListener.pause = false;
        AudioSource[] allSources = FindObjectsOfType<AudioSource>(true);
        foreach (AudioSource src in allSources)
        {
            if (src != null && src != isolatedEndingSfxSource)
            {
                src.Stop();
                src.mute = true;
                src.enabled = false;
            }
        }

        // Disable SmoothCameraFollow so the camera stays valid when the player is disabled
        if (targetCamera != null)
        {
            SmoothCameraFollow follow = targetCamera.GetComponent<SmoothCameraFollow>();
            if (follow != null) follow.enabled = false;
        }

        // Fully disable the Player GameObject (safely unparenting any shared objects first)
        if (playerRootObject != null)
        {
            if (transform.IsChildOf(playerRootObject.transform))
            {
                transform.SetParent(null, true);
            }
            if (targetCamera != null && targetCamera.transform.IsChildOf(playerRootObject.transform))
            {
                targetCamera.transform.SetParent(null, true);
            }
            if (endingPopupUI != null && endingPopupUI.transform.IsChildOf(playerRootObject.transform))
            {
                Canvas rootCanvas = endingPopupUI.GetComponentInParent<Canvas>(true);
                if (rootCanvas != null && rootCanvas.transform.IsChildOf(playerRootObject.transform))
                {
                    rootCanvas.transform.SetParent(null, true);
                }
                else
                {
                    endingPopupUI.transform.SetParent(null, true);
                }
            }

            playerRootObject.SetActive(false);
        }

        EnsureActiveAudioListener();
    }

    // =========================================================================
    // OVERLAY RENDERING & HEART-ATTACK CAMERA MOTION
    // =========================================================================

    private void EnsureOverlayInitialized()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (breakdownShader == null) breakdownShader = Shader.Find(ShaderName);
        if (breakdownShader == null) return;

        if (breakdownMaterial == null)
        {
            breakdownMaterial = new Material(breakdownShader)
            {
                name = "DepressionBreakdown_Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObj = new GameObject("DepressionBreakdown_OverlayCanvas");
            canvasObj.transform.SetParent(transform, false);

            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 32766;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();

            GameObject rawObj = new GameObject("DepressionBreakdown_RawImage");
            rawObj.transform.SetParent(overlayCanvas.transform, false);

            overlayImage = rawObj.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            overlayImage.material = breakdownMaterial;

            RectTransform rt = overlayImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            GameObject blockerObj = new GameObject("Breakdown_RaycastBlocker");
            blockerObj.transform.SetParent(overlayCanvas.transform, false);
            raycastBlocker = blockerObj.AddComponent<Image>();
            raycastBlocker.color = new Color(0f, 0f, 0f, 0f);
            raycastBlocker.raycastTarget = false;

            RectTransform bRt = raycastBlocker.rectTransform;
            bRt.anchorMin = Vector2.zero;
            bRt.anchorMax = Vector2.one;
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = Vector2.zero;
            blockerObj.SetActive(false);

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

        currentFrameRT = CreateRT(width, height, 24, "Breakdown_CurrentRT");
        echoRT1 = CreateRT(Mathf.Max(64, width / 2), Mathf.Max(64, height / 2), 0, "Breakdown_EchoRT1");
        echoRT2 = CreateRT(Mathf.Max(64, width / 2), Mathf.Max(64, height / 2), 0, "Breakdown_EchoRT2");

        Graphics.Blit(currentFrameRT, echoRT1);
        Graphics.Blit(currentFrameRT, echoRT2);

        if (overlayImage != null) overlayImage.texture = currentFrameRT;
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

    private void ReleaseRTs()
    {
        if (currentFrameRT != null) { currentFrameRT.Release(); Destroy(currentFrameRT); currentFrameRT = null; }
        if (echoRT1 != null) { echoRT1.Release(); Destroy(echoRT1); echoRT1 = null; }
        if (echoRT2 != null) { echoRT2.Release(); Destroy(echoRT2); echoRT2 = null; }
    }

    private void LateUpdate()
    {
        if (!isRenderingActive || breakdownMaterial == null) return;

        // While ramping up (before the hard cut to black), render the camera & apply heart-attack camera motion.
        // Once cut to black (and player/camera is disabled), the shader outputs pure black without needing targetCamera!
        if (!isCutToBlack && targetCamera != null && targetCamera.gameObject.activeInHierarchy)
        {
            RemoveCameraOffset();

            if (currentIntensity > 0.001f)
            {
                float cycle = Mathf.Repeat(accumulatedPulsePhase, 1.0f);
                float systole = Mathf.Exp(-Mathf.Pow((cycle - 0.12f) * 18.0f, 2.0f));
                float diastole = Mathf.Exp(-Mathf.Pow((cycle - 0.28f) * 14.0f, 2.0f)) * 0.78f;
                float thump = Mathf.Clamp01(systole + diastole);

                float t = Time.unscaledTime;
                appliedCameraEuler = new Vector3(
                    (thump * 3.8f + currentIntensity * 2.5f),
                    Mathf.Sin(t * 4.5f) * 1.5f * currentIntensity,
                    (Mathf.Sin(t * 2.2f) * maxCameraCollapseDegrees + thump * 2.5f) * currentIntensity
                );
                targetCamera.transform.localRotation *= Quaternion.Euler(appliedCameraEuler);

                EnsureRTs(Screen.width, Screen.height);

                RenderTexture prev = targetCamera.targetTexture;
                targetCamera.targetTexture = currentFrameRT;
                targetCamera.Render();
                targetCamera.targetTexture = prev;

                echoTimer += Time.unscaledDeltaTime;
                if (echoTimer >= 0.045f)
                {
                    echoTimer = 0f;
                    echoToggle = !echoToggle;
                    Graphics.Blit(currentFrameRT, echoToggle ? echoRT1 : echoRT2);
                }
            }
        }

        breakdownMaterial.SetTexture(MainTexId, currentFrameRT != null ? currentFrameRT : Texture2D.blackTexture);
        breakdownMaterial.SetTexture(EchoTex1Id, echoRT1 != null ? echoRT1 : Texture2D.blackTexture);
        breakdownMaterial.SetTexture(EchoTex2Id, echoRT2 != null ? echoRT2 : Texture2D.blackTexture);
        breakdownMaterial.SetFloat(IntensityId, currentIntensity);
        breakdownMaterial.SetFloat(BlackCutId, currentBlackCut);
        breakdownMaterial.SetFloat(PulsePhaseId, accumulatedPulsePhase);
    }

    private void RemoveCameraOffset()
    {
        if (appliedCameraEuler.sqrMagnitude > 0.00001f && targetCamera != null)
        {
            targetCamera.transform.localRotation *= Quaternion.Inverse(Quaternion.Euler(appliedCameraEuler));
            appliedCameraEuler = Vector3.zero;
        }
    }
}
