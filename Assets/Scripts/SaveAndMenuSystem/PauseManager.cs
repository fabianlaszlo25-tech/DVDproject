using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Handles the ESC / HUD Button Pause Menu & Settings:
/// - Pressing ESC or clicking the HUD "pause" image under croper pauses the game completely (Time.timeScale = 0, disables world/movement input).
/// - Mutes/pauses every other AudioSource in the scene and plays the isolated paused sound.
/// - Switches the FullScreenPassRendererFeature CRT material from duringgameplay to duringpaused.
/// - Shows the Pause Canvas (the only clickable UI while paused) with 3 buttons:
///   1. Resume (closes canvas, resumes game & audio, switches CRT shader back to duringgameplay).
///   2. Settings (opens the 2 audio sliders: Music Volume & Every Other Sound / SFX Volume via AudioListener,
///      while DynamicMusicManager secretly boosts music to offset AudioListener.volume).
///   3. End Run (unpauses cleanly and immediately triggers the Heart Attack & Game End sequence).
/// - Supports both Unity EventSystem AND direct regular-cursor hit-testing/slider dragging for 100% reliability in Editor and WebGL.
/// </summary>
[DefaultExecutionOrder(100)]
public class PauseManager : MonoBehaviour
{
    private struct PausedAudioSourceState
    {
        public AudioSource source;
        public bool wasPlaying;
        public bool previousMute;
    }

    private struct DisabledBehaviourState
    {
        public Behaviour behaviour;
        public bool wasEnabled;
    }

    [Header("Core Game References (Auto-Found if left empty)")]
    public DynamicMusicManager musicManager;
    public BarManager barManager;
    public DepressionBreakdownSequence depressionBreakdown;
    public GridCrawlerController playerController;
    public SmoothCameraFollow cameraFollow;

    [Header("HUD Pause Button (Under Player Controller Canvas 'croper')")]
    [Tooltip("The 'pause' Image under the player controller's 'croper' overlay panel that uses the raycast cursor.")]
    public PauseButtonUI hudPauseButton;

    [Header("CRT Full Screen Renderer Feature & Materials")]
    [Tooltip("The URP FullScreenPassRendererFeature that renders the CRT effect (auto-found from mainRenderer if left empty).")]
    public FullScreenPassRendererFeature crtRendererFeature;
    [Tooltip("Optional reference to the UniversalRendererData / ScriptableRendererData asset (e.g. mainRenderer).")]
    public ScriptableRendererData rendererData;
    [Tooltip("Material used during normal gameplay (Assets/Shaders/duringgameplay.mat).")]
    public Material duringGameplayMaterial;
    [Tooltip("Material used while the game is paused (Assets/Shaders/duringpaused.mat).")]
    public Material duringPausedMaterial;

    [Header("Pause Menu UI References")]
    [Tooltip("Root Canvas for the Pause Menu (only active while paused).")]
    public Canvas pauseCanvas;
    [Tooltip("Panel containing the 3 main buttons: Resume, Settings, End Run.")]
    public GameObject mainButtonsPanel;
    [Tooltip("Panel containing the 2 audio sliders (Music & SFX) and Back button.")]
    public GameObject settingsPanel;

    [Header("Pause Menu Buttons")]
    public Button resumeButton;
    public Button settingsButton;
    public Button endRunButton;
    public Button backFromSettingsButton;

    [Header("Settings Audio Sliders")]
    [Tooltip("Controls music volume on DynamicMusicManager.")]
    public Slider musicVolumeSlider;
    [Tooltip("Controls every other sound via AudioListener.volume while secretly boosting music to offset it.")]
    public Slider sfxVolumeSlider;

    [Header("Paused Audio (Only Sound Playing While Paused)")]
    [Tooltip("Dedicated AudioSource on PauseManager that plays the paused sound while everything else is muted.")]
    public AudioSource pausedAudioSource;
    [Tooltip("Sound clip that plays only while the Pause menu is open.")]
    public AudioClip pausedSound;
    [Tooltip("If true, the paused sound loops while the game remains paused.")]
    public bool loopPausedSound = true;
    [Range(0f, 1f)]
    public float pausedSoundVolume = 1.0f;

    [Header("Default Slider Values")]
    [Range(0f, 1f)] public float initialMusicVolume = 1.0f;
    [Range(0f, 1f)] public float initialSfxVolume = 1.0f;

    private readonly List<PausedAudioSourceState> pausedAudioSources = new List<PausedAudioSourceState>();
    private readonly List<DisabledBehaviourState> disabledBehavioursWhilePaused = new List<DisabledBehaviourState>();

    private EventSystem sceneEventSystem;
    private Slider activeDraggedSlider;

    private bool isPaused = false;
    private bool hasTriggeredEndRun = false;
    private float timeScaleBeforePause = 1.0f;
    private CursorLockMode cursorLockBeforePause = CursorLockMode.Confined;
    private bool cursorVisibleBeforePause = false;
    private bool crosshairWasActive = true;
    private bool isUpdatingSlidersInternally = false;
    private bool pendingRestoreInteractionsNextFrame = false;

    private int pauseOpenedFrame = -1;
    private int lastMenuActionFrame = -1;

    public bool IsPaused => isPaused;

    private static PauseManager activeInstance;

    /// <summary>
    /// The real game speed (what Time.timeScale will be once unpaused). While paused, Time.timeScale is 0,
    /// so the true speed is the value we are going to restore on Resume.
    /// </summary>
    public static float GetGameSpeed()
    {
        if (activeInstance != null && activeInstance.isPaused)
        {
            return activeInstance.timeScaleBeforePause;
        }
        return Time.timeScale;
    }

    /// <summary>
    /// Sets the real game speed. While paused this updates the value restored on Resume instead of touching
    /// Time.timeScale (which must stay 0), so game speed boosters that start/expire during a pause are never lost or made permanent.
    /// </summary>
    public static void SetGameSpeed(float speed)
    {
        if (activeInstance != null && activeInstance.isPaused)
        {
            activeInstance.timeScaleBeforePause = speed > 0.001f ? speed : 1.0f;
            return;
        }
        Time.timeScale = speed;
    }

    private void Awake()
    {
        activeInstance = this;
        AutoFindReferences();
        EnsurePausedAudioSource();
        EnsureRuntimeFallbackUIIfMissing();
        ConfigurePauseCanvasForClicking();
        BindUIEvents();

        // Ensure game starts unpaused with the gameplay CRT material active
        CrtPostProcessManager.RegisterMaterials(duringGameplayMaterial, duringPausedMaterial, crtRendererFeature);
        SetCrtMaterial(duringGameplayMaterial);

        if (pauseCanvas != null)
        {
            pauseCanvas.gameObject.SetActive(false);
        }
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
        if (mainButtonsPanel != null)
        {
            mainButtonsPanel.SetActive(true);
        }
    }

    private void Start()
    {
        AutoFindReferences();
        ConfigurePauseCanvasForClicking();

        initialMusicVolume = GlobalAudioSettings.MusicVolume;
        initialSfxVolume = GlobalAudioSettings.SfxVolume;
        GlobalAudioSettings.ApplyLoadedSettings();
        SyncSlidersToCurrentVolumes();
    }

    private void OnDisable()
    {
        RestoreRuntimeStateOnExit();
    }

    private void OnDestroy()
    {
        RestoreRuntimeStateOnExit();
        if (activeInstance == this) activeInstance = null;
    }

    private void RestoreRuntimeStateOnExit()
    {
        // Always restore the CRT material and timeScale if exiting Play Mode or reloading scene while paused
        SetCrtMaterial(duringGameplayMaterial);
        if (isPaused)
        {
            Time.timeScale = 1.0f;
            isPaused = false;
        }
    }

    private void Update()
    {
        // Complete any deferred gameplay interaction restore on the frame AFTER Resume was clicked
        // so the mouse click that pressed Resume never leaks into 3D world or HUD arrow clicks.
        if (pendingRestoreInteractionsNextFrame && !isPaused)
        {
            pendingRestoreInteractionsNextFrame = false;
            RestoreGameplayInteractionsAfterPauseImmediate();
        }

        if (WasEscapePressedThisFrame())
        {
            if (!CanTogglePause()) return;

            if (!isPaused)
            {
                PauseGame();
            }
            else
            {
                ResumeGame();
            }
            return;
        }

        if (isPaused)
        {
            // Keep the regular OS/WebGL cursor unlocked and visible while paused
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;

            EnsureEventSystemActive();

            if (pausedAudioSource != null)
            {
                pausedAudioSource.volume = pausedSoundVolume;
            }

            // Direct regular-cursor hover, button click, and slider drag handling
            // (guarantees 100% responsiveness with regular cursor in both Editor and WebGL)
            HandleDirectRegularCursorPauseUI();
        }
    }

    private bool WasEscapePressedThisFrame()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            return true;
        }
        return Input.GetKeyDown(KeyCode.Escape);
    }

    private bool CanTogglePause()
    {
        if (hasTriggeredEndRun) return false;
        if (depressionBreakdown == null) depressionBreakdown = FindObjectOfType<DepressionBreakdownSequence>();
        if (depressionBreakdown != null && depressionBreakdown.HasTriggered) return false;
        if (SubstanceCombinationManager.HasInstance && SubstanceCombinationManager.Instance.IsOverdoseSequenceRunning) return false;
        return true;
    }

    // =========================================================================
    // PAUSE / RESUME / SETTINGS / END RUN ACTIONS
    // =========================================================================

    public void PauseGame()
    {
        if (isPaused || !CanTogglePause()) return;

        pendingRestoreInteractionsNextFrame = false;
        pauseOpenedFrame = Time.frameCount;
        lastMenuActionFrame = Time.frameCount;
        activeDraggedSlider = null;

        AutoFindReferences();
        EnsurePausedAudioSource();
        ConfigurePauseCanvasForClicking();

        isPaused = true;
        timeScaleBeforePause = Time.timeScale > 0.001f ? Time.timeScale : 1.0f;
        Time.timeScale = 0f;

        // 1. Switch Full Screen CRT effect material from duringgameplay to duringpaused
        SetCrtMaterial(duringPausedMaterial);

        // 2. Mute & pause all other AudioSources in the game and play the isolated paused sound
        MuteAllOtherAudioAndPlayPausedSound();

        // 3. Disable world interactions & movement so ONLY the Pause Canvas is clickable
        DisableGameplayInteractionsForPause();

        // 4. Show Pause Canvas on the main 3-button view and unlock regular cursor
        if (pauseCanvas != null)
        {
            pauseCanvas.gameObject.SetActive(true);
            ConfigurePauseCanvasForClicking();
        }
        if (mainButtonsPanel != null) mainButtonsPanel.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        ResetButtonHoverScales();
        SyncSlidersToCurrentVolumes();

        // 5. Ensure EventSystem is active (SceneTransitionIn disables EventSystem.current on Start,
        //    which makes EventSystem.current null unless re-enabled via FindObjectOfType<EventSystem>(true)!)
        EnsureEventSystemActive();

        cursorLockBeforePause = Cursor.lockState;
        cursorVisibleBeforePause = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Canvas.ForceUpdateCanvases();
    }

    public void ResumeGame()
    {
        if (!isPaused) return;

        isPaused = false;
        lastMenuActionFrame = Time.frameCount;
        activeDraggedSlider = null;

        // 1. Stop the paused sound and restore all previously playing audio sources
        StopPausedSoundAndRestoreAudio();

        // 2. Switch Full Screen CRT effect material back to duringgameplay
        SetCrtMaterial(duringGameplayMaterial);

        // 3. Hide Pause Canvas & Settings Panel
        ResetButtonHoverScales();
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainButtonsPanel != null) mainButtonsPanel.SetActive(true);
        if (pauseCanvas != null) pauseCanvas.gameObject.SetActive(false);

        // 4. Restore crosshair visibility & defer re-enabling playerController by 1 frame
        //    so clicking RESUME with the regular cursor cannot accidentally click a 3D prop on the same frame
        if (playerController != null && playerController.crosshairContainer != null)
        {
            playerController.crosshairContainer.gameObject.SetActive(crosshairWasActive);
        }
        pendingRestoreInteractionsNextFrame = true;

        EnsureEventSystemActive();

        Cursor.lockState = cursorLockBeforePause;
        Cursor.visible = cursorVisibleBeforePause;

        Time.timeScale = timeScaleBeforePause > 0.001f ? timeScaleBeforePause : 1.0f;
    }

    public void OpenSettings()
    {
        if (!isPaused) return;
        if (lastMenuActionFrame == Time.frameCount && settingsPanel != null && settingsPanel.activeSelf) return;
        lastMenuActionFrame = Time.frameCount;
        activeDraggedSlider = null;

        SyncSlidersToCurrentVolumes();
        ResetButtonHoverScales();

        if (mainButtonsPanel != null && settingsPanel != null && mainButtonsPanel != settingsPanel)
        {
            mainButtonsPanel.SetActive(false);
            settingsPanel.SetActive(true);
        }
        else if (settingsPanel != null)
        {
            settingsPanel.SetActive(!settingsPanel.activeSelf);
        }

        Canvas.ForceUpdateCanvases();
    }

    public void CloseSettings()
    {
        if (lastMenuActionFrame == Time.frameCount && mainButtonsPanel != null && mainButtonsPanel.activeSelf) return;
        lastMenuActionFrame = Time.frameCount;
        activeDraggedSlider = null;

        ResetButtonHoverScales();
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainButtonsPanel != null) mainButtonsPanel.SetActive(true);
        Canvas.ForceUpdateCanvases();
    }

    public void EndRun()
    {
        if (hasTriggeredEndRun) return;
        lastMenuActionFrame = Time.frameCount;
        activeDraggedSlider = null;

        // First cleanly unpause (closes Pause Canvas, restores duringgameplay shader, unmutes audio, sets timeScale = 1)
        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            SetCrtMaterial(duringGameplayMaterial);
            if (pauseCanvas != null) pauseCanvas.gameObject.SetActive(false);
            Time.timeScale = 1.0f;
        }

        // Immediately restore behaviours before triggering breakdown so Breakdown can manage its own state
        pendingRestoreInteractionsNextFrame = false;
        RestoreGameplayInteractionsAfterPauseImmediate();

        hasTriggeredEndRun = true;

        AutoFindReferences();

        // Trigger the Heart Attack & Game End sequence via BarManager and DepressionBreakdownSequence
        if (barManager != null)
        {
            barManager.currentDepression = barManager.maxDepression;
        }

        if (depressionBreakdown == null && barManager != null)
        {
            depressionBreakdown = barManager.depressionBreakdown;
        }
        if (depressionBreakdown == null)
        {
            depressionBreakdown = FindObjectOfType<DepressionBreakdownSequence>();
        }

        if (depressionBreakdown != null && !depressionBreakdown.HasTriggered)
        {
            depressionBreakdown.TriggerDepressionBreakdown();
        }
    }

    // =========================================================================
    // DIRECT REGULAR-CURSOR UI INTERACTION (BUTTONS + SLIDERS)
    // =========================================================================

    private void HandleDirectRegularCursorPauseUI()
    {
        if (pauseCanvas == null || !pauseCanvas.gameObject.activeInHierarchy) return;

        Vector2 mouseScreenPos = GetRegularMouseScreenPosition();
        Camera uiCam = GetPauseCanvasEventCamera();

        bool mainActive = mainButtonsPanel != null && mainButtonsPanel.activeInHierarchy;
        bool settingsActive = settingsPanel != null && settingsPanel.activeInHierarchy;

        // 1. Update button hover scale feedback using unscaled time
        UpdateButtonHoverFeedback(resumeButton, mainActive, mouseScreenPos, uiCam);
        UpdateButtonHoverFeedback(settingsButton, mainActive, mouseScreenPos, uiCam);
        UpdateButtonHoverFeedback(endRunButton, mainActive, mouseScreenPos, uiCam);
        UpdateButtonHoverFeedback(backFromSettingsButton, settingsActive, mouseScreenPos, uiCam);

        // Ignore mouse clicks on the exact frame the pause menu was opened (e.g. from clicking the HUD pause button)
        if (Time.frameCount == pauseOpenedFrame)
        {
            return;
        }

        bool leftPressedThisFrame = WasLeftMousePressedThisFrame();
        bool leftHeld = IsLeftMousePressed();

        // 2. Handle Slider Dragging in Settings Panel with the regular cursor
        if (settingsActive)
        {
            if (leftPressedThisFrame)
            {
                if (IsScreenPointNearSlider(musicVolumeSlider, mouseScreenPos, uiCam, 24f))
                {
                    activeDraggedSlider = musicVolumeSlider;
                }
                else if (IsScreenPointNearSlider(sfxVolumeSlider, mouseScreenPos, uiCam, 24f))
                {
                    activeDraggedSlider = sfxVolumeSlider;
                }
            }

            if (activeDraggedSlider != null)
            {
                if (leftHeld)
                {
                    UpdateSliderValueFromScreenPoint(activeDraggedSlider, mouseScreenPos, uiCam);
                    return;
                }
                else
                {
                    activeDraggedSlider = null;
                }
            }
        }
        else
        {
            activeDraggedSlider = null;
        }

        // 3. Handle Direct Button Clicks with the regular cursor (if EventSystem didn't already fire this frame)
        if (leftPressedThisFrame && lastMenuActionFrame != Time.frameCount)
        {
            if (mainActive)
            {
                if (IsScreenPointOverButton(resumeButton, mouseScreenPos, uiCam))
                {
                    ResumeGame();
                    return;
                }
                if (IsScreenPointOverButton(settingsButton, mouseScreenPos, uiCam))
                {
                    OpenSettings();
                    return;
                }
                if (IsScreenPointOverButton(endRunButton, mouseScreenPos, uiCam))
                {
                    EndRun();
                    return;
                }
            }

            if (settingsActive)
            {
                if (IsScreenPointOverButton(backFromSettingsButton, mouseScreenPos, uiCam))
                {
                    CloseSettings();
                    return;
                }
            }
        }
    }

    private void UpdateButtonHoverFeedback(Button btn, bool panelActive, Vector2 mouseScreenPos, Camera uiCam)
    {
        if (btn == null) return;
        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt == null) return;

        bool hovered = panelActive && btn.gameObject.activeInHierarchy && btn.interactable && IsScreenPointOverRect(rt, mouseScreenPos, uiCam, 6f);
        Vector3 targetScale = hovered ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;
        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);
        rt.localScale = Vector3.Lerp(rt.localScale, targetScale, dt * 14f);
    }

    private void ResetButtonHoverScales()
    {
        if (resumeButton != null) resumeButton.transform.localScale = Vector3.one;
        if (settingsButton != null) settingsButton.transform.localScale = Vector3.one;
        if (endRunButton != null) endRunButton.transform.localScale = Vector3.one;
        if (backFromSettingsButton != null) backFromSettingsButton.transform.localScale = Vector3.one;
    }

    private bool IsScreenPointOverButton(Button btn, Vector2 screenPos, Camera uiCam)
    {
        if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) return false;
        RectTransform rt = btn.GetComponent<RectTransform>();
        return IsScreenPointOverRect(rt, screenPos, uiCam, 6f);
    }

    private bool IsScreenPointNearSlider(Slider slider, Vector2 screenPos, Camera uiCam, float padding)
    {
        if (slider == null || !slider.gameObject.activeInHierarchy || !slider.interactable) return false;
        RectTransform rt = slider.GetComponent<RectTransform>();
        if (IsScreenPointOverRect(rt, screenPos, uiCam, padding)) return true;

        if (slider.handleRect != null && IsScreenPointOverRect(slider.handleRect, screenPos, uiCam, padding))
            return true;

        return false;
    }

    private bool IsScreenPointOverRect(RectTransform rt, Vector2 screenPos, Camera uiCam, float padding)
    {
        if (rt == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, uiCam, out Vector2 localPoint))
            return false;

        Rect r = rt.rect;
        r.xMin -= padding;
        r.xMax += padding;
        r.yMin -= padding;
        r.yMax += padding;
        return r.Contains(localPoint);
    }

    private void UpdateSliderValueFromScreenPoint(Slider slider, Vector2 screenPos, Camera uiCam)
    {
        if (slider == null) return;

        RectTransform trackRect = slider.GetComponent<RectTransform>();
        if (trackRect == null) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRect, screenPos, uiCam, out Vector2 localPoint))
            return;

        Rect r = trackRect.rect;
        float normalized = Mathf.InverseLerp(r.xMin, r.xMax, localPoint.x);
        if (slider.direction == Slider.Direction.RightToLeft)
        {
            normalized = 1f - normalized;
        }

        float newValue = Mathf.Lerp(slider.minValue, slider.maxValue, Mathf.Clamp01(normalized));
        slider.value = newValue;

        if (slider == musicVolumeSlider)
        {
            OnMusicVolumeChanged(newValue);
        }
        else if (slider == sfxVolumeSlider)
        {
            OnSfxVolumeChanged(newValue);
        }
    }

    private Camera GetPauseCanvasEventCamera()
    {
        if (pauseCanvas == null) return null;
        if (pauseCanvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return pauseCanvas.worldCamera != null ? pauseCanvas.worldCamera : Camera.main;
    }

    private static Vector2 GetRegularMouseScreenPosition()
    {
        if (Mouse.current != null)
        {
            return Mouse.current.position.ReadValue();
        }
        return Input.mousePosition;
    }

    private static bool WasLeftMousePressedThisFrame()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }
        return Input.GetMouseButtonDown(0);
    }

    private static bool IsLeftMousePressed()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            return true;
        }
        return Input.GetMouseButton(0);
    }

    // =========================================================================
    // AUDIO SLIDER HANDLERS (MUSIC & EVERY OTHER SOUND / SFX)
    // =========================================================================

    public void OnMusicVolumeChanged(float value)
    {
        if (isUpdatingSlidersInternally) return;

        float clamped = Mathf.Clamp01(value);
        initialMusicVolume = clamped;
        GlobalAudioSettings.SetMusicVolume(clamped);
    }

    public void OnSfxVolumeChanged(float value)
    {
        if (isUpdatingSlidersInternally) return;

        float clamped = Mathf.Clamp01(value);
        initialSfxVolume = clamped;
        GlobalAudioSettings.SetSfxVolume(clamped);
    }

    private void ApplyAudioSettings(float musicVol, float sfxVol)
    {
        AudioListener.volume = Mathf.Clamp01(sfxVol);

        if (musicManager == null) musicManager = FindObjectOfType<DynamicMusicManager>();
        if (musicManager != null)
        {
            musicManager.SetMusicSliderVolume(Mathf.Clamp01(musicVol));
            musicManager.SetSfxListenerVolume(Mathf.Clamp01(sfxVol));
        }
    }

    private void SyncSlidersToCurrentVolumes()
    {
        isUpdatingSlidersInternally = true;

        float currentMusic = GlobalAudioSettings.MusicVolume;
        float currentSfx = GlobalAudioSettings.SfxVolume;

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.SetValueWithoutNotify(Mathf.Clamp01(currentMusic));
        }
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.SetValueWithoutNotify(Mathf.Clamp01(currentSfx));
        }

        isUpdatingSlidersInternally = false;
    }

    // =========================================================================
    // PAUSE AUDIO ISOLATION & MUTE MANAGEMENT
    // =========================================================================

    private void EnsurePausedAudioSource()
    {
        if (pausedAudioSource == null)
        {
            pausedAudioSource = GetComponent<AudioSource>();
            if (pausedAudioSource == null)
            {
                pausedAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        pausedAudioSource.playOnAwake = false;
        pausedAudioSource.spatialBlend = 0f;
        pausedAudioSource.loop = loopPausedSound;
        pausedAudioSource.ignoreListenerPause = true;
        // Keep ignoreListenerVolume = false so adjusting the SFX (AudioListener) slider in Settings can be heard on the pause sound
        pausedAudioSource.ignoreListenerVolume = false;

        if (pausedSound != null && pausedSound.loadState == AudioDataLoadState.Unloaded)
        {
            pausedSound.LoadAudioData();
        }
    }

    private void MuteAllOtherAudioAndPlayPausedSound()
    {
        pausedAudioSources.Clear();

        if (musicManager != null)
        {
            musicManager.SetPausedState(true);
        }

        AudioSource[] allSources = FindObjectsOfType<AudioSource>(true);
        for (int i = 0; i < allSources.Length; i++)
        {
            AudioSource src = allSources[i];
            if (src == null || src == pausedAudioSource) continue;

            pausedAudioSources.Add(new PausedAudioSourceState
            {
                source = src,
                wasPlaying = src.isPlaying,
                previousMute = src.mute
            });

            if (src.isPlaying)
            {
                src.Pause();
            }
            src.mute = true;
        }

        if (pausedAudioSource != null && pausedSound != null)
        {
            pausedAudioSource.clip = pausedSound;
            pausedAudioSource.loop = loopPausedSound;
            pausedAudioSource.volume = pausedSoundVolume;
            pausedAudioSource.mute = false;
            pausedAudioSource.Play();
        }
    }

    private void StopPausedSoundAndRestoreAudio()
    {
        if (pausedAudioSource != null)
        {
            pausedAudioSource.Stop();
        }

        for (int i = 0; i < pausedAudioSources.Count; i++)
        {
            PausedAudioSourceState state = pausedAudioSources[i];
            if (state.source == null) continue;

            state.source.mute = state.previousMute;
            if (state.wasPlaying && state.source.enabled && state.source.gameObject.activeInHierarchy)
            {
                state.source.UnPause();
            }
        }
        pausedAudioSources.Clear();

        if (musicManager != null)
        {
            musicManager.SetPausedState(false);
        }
    }

    // =========================================================================
    // INPUT ISOLATION WHILE PAUSED
    // =========================================================================

    private void DisableGameplayInteractionsForPause()
    {
        disabledBehavioursWhilePaused.Clear();

        void DisableBehaviour(Behaviour b)
        {
            if (b == null) return;
            disabledBehavioursWhilePaused.Add(new DisabledBehaviourState
            {
                behaviour = b,
                wasEnabled = b.enabled
            });
            b.enabled = false;
        }

        if (playerController != null)
        {
            if (playerController.crosshairContainer != null)
            {
                crosshairWasActive = playerController.crosshairContainer.gameObject.activeSelf;
                playerController.crosshairContainer.gameObject.SetActive(false);
            }
            if (playerController.interactTextUI != null)
            {
                playerController.interactTextUI.transform.localScale = Vector3.zero;
            }
            DisableBehaviour(playerController);
        }

        if (cameraFollow != null)
        {
            DisableBehaviour(cameraFollow);
        }

        InteractableObject[] interactables = FindObjectsOfType<InteractableObject>(false);
        for (int i = 0; i < interactables.Length; i++)
        {
            DisableBehaviour(interactables[i]);
        }
    }

    private void RestoreGameplayInteractionsAfterPauseImmediate()
    {
        for (int i = 0; i < disabledBehavioursWhilePaused.Count; i++)
        {
            DisabledBehaviourState state = disabledBehavioursWhilePaused[i];
            if (state.behaviour != null)
            {
                state.behaviour.enabled = state.wasEnabled;
            }
        }
        disabledBehavioursWhilePaused.Clear();

        if (playerController != null && playerController.crosshairContainer != null)
        {
            playerController.crosshairContainer.gameObject.SetActive(crosshairWasActive);
        }
    }

    private void EnsureEventSystemActive()
    {
        if (sceneEventSystem == null)
        {
            sceneEventSystem = EventSystem.current != null
                ? EventSystem.current
                : FindObjectOfType<EventSystem>(true);
        }

        if (sceneEventSystem == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            sceneEventSystem = esObj.AddComponent<EventSystem>();
            esObj.AddComponent<InputSystemUIInputModule>();
        }

        if (!sceneEventSystem.gameObject.activeSelf)
        {
            sceneEventSystem.gameObject.SetActive(true);
        }
        if (!sceneEventSystem.enabled)
        {
            sceneEventSystem.enabled = true;
        }

        BaseInputModule inputModule = sceneEventSystem.GetComponent<BaseInputModule>();
        if (inputModule != null && !inputModule.enabled)
        {
            inputModule.enabled = true;
        }
    }

    private void ConfigurePauseCanvasForClicking()
    {
        if (pauseCanvas == null) return;

        if (pauseCanvas.renderMode == RenderMode.ScreenSpaceCamera && pauseCanvas.worldCamera == null)
        {
            pauseCanvas.worldCamera = Camera.main;
        }

        // Ensure sortingOrder is above fader (999) and endingPopupUI (2000) so nothing blocks clicks
        if (pauseCanvas.sortingOrder < 5000)
        {
            pauseCanvas.sortingOrder = 5000;
        }

        GraphicRaycaster raycaster = pauseCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            raycaster = pauseCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }
        raycaster.enabled = true;
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
    }

    // =========================================================================
    // CRT FULLSCREEN PASS MATERIAL SWITCHING
    // =========================================================================

    private void SetCrtMaterial(Material targetMaterial)
    {
        if (targetMaterial == null) return;

        if (crtRendererFeature == null)
        {
            FindCrtRendererFeature();
        }

        if (crtRendererFeature != null)
        {
            crtRendererFeature.passMaterial = targetMaterial;
        }
    }

    private void FindCrtRendererFeature()
    {
        // 1. Check assigned ScriptableRendererData (e.g. mainRenderer.asset)
        if (rendererData != null && TryFindCrtFeatureInRendererData(rendererData, out FullScreenPassRendererFeature found))
        {
            crtRendererFeature = found;
            return;
        }

        // 2. Search active UniversalRenderPipelineAsset renderer data list via reflection
        RenderPipelineAsset pipeline = QualitySettings.renderPipeline != null
            ? QualitySettings.renderPipeline
            : GraphicsSettings.currentRenderPipeline;

        if (pipeline != null)
        {
            FieldInfo listField = pipeline.GetType().GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (listField != null && listField.GetValue(pipeline) is ScriptableRendererData[] dataList)
            {
                for (int i = 0; i < dataList.Length; i++)
                {
                    if (dataList[i] != null && TryFindCrtFeatureInRendererData(dataList[i], out FullScreenPassRendererFeature feature))
                    {
                        crtRendererFeature = feature;
                        return;
                    }
                }
            }
        }

        // 3. Fallback: search all loaded FullScreenPassRendererFeature instances in memory
        FullScreenPassRendererFeature[] loadedFeatures = Resources.FindObjectsOfTypeAll<FullScreenPassRendererFeature>();
        for (int i = 0; i < loadedFeatures.Length; i++)
        {
            FullScreenPassRendererFeature f = loadedFeatures[i];
            if (f == null) continue;
            if (IsMatchingCrtFeature(f))
            {
                crtRendererFeature = f;
                return;
            }
        }
    }

    private bool TryFindCrtFeatureInRendererData(ScriptableRendererData data, out FullScreenPassRendererFeature result)
    {
        result = null;
        if (data == null || data.rendererFeatures == null) return false;

        for (int i = 0; i < data.rendererFeatures.Count; i++)
        {
            if (data.rendererFeatures[i] is FullScreenPassRendererFeature f && IsMatchingCrtFeature(f))
            {
                result = f;
                return true;
            }
        }
        return false;
    }

    private bool IsMatchingCrtFeature(FullScreenPassRendererFeature feature)
    {
        if (feature == null) return false;

        if (duringGameplayMaterial != null && feature.passMaterial == duringGameplayMaterial)
            return true;
        if (duringPausedMaterial != null && feature.passMaterial == duringPausedMaterial)
            return true;

        if (feature.passMaterial != null)
        {
            string matName = feature.passMaterial.name.ToLowerInvariant();
            if (matName.Contains("duringgameplay") || matName.Contains("duringpaused") || matName.Contains("crt"))
                return true;

            if (feature.passMaterial.shader != null && feature.passMaterial.shader.name.Contains("URP_CRT"))
                return true;
        }

        if (feature.name.Contains("FullScreenPassRendererFeatureOldGUID"))
            return true;

        return false;
    }

    // =========================================================================
    // AUTO-WIRING & UI EVENT BINDING
    // =========================================================================

    private void AutoFindReferences()
    {
        if (musicManager == null) musicManager = FindObjectOfType<DynamicMusicManager>(true);
        if (barManager == null) barManager = FindObjectOfType<BarManager>(true);
        if (depressionBreakdown == null) depressionBreakdown = FindObjectOfType<DepressionBreakdownSequence>(true);
        if (playerController == null) playerController = FindObjectOfType<GridCrawlerController>(true);
        if (cameraFollow == null) cameraFollow = FindObjectOfType<SmoothCameraFollow>(true);
        if (crtRendererFeature == null) FindCrtRendererFeature();

        EnsureHudPauseButtonSetup();
    }

    public void EnsureHudPauseButtonSetup()
    {
        if (hudPauseButton == null)
        {
            hudPauseButton = FindObjectOfType<PauseButtonUI>(true);
        }

        if (hudPauseButton == null)
        {
            Transform pauseImgTransform = FindHudPauseImageTransform();
            if (pauseImgTransform != null)
            {
                hudPauseButton = pauseImgTransform.GetComponent<PauseButtonUI>();
                if (hudPauseButton == null)
                {
                    hudPauseButton = pauseImgTransform.gameObject.AddComponent<PauseButtonUI>();
                }
            }
        }

        if (hudPauseButton != null)
        {
            if (hudPauseButton.controller == null) hudPauseButton.controller = playerController;
            if (hudPauseButton.pauseManager == null) hudPauseButton.pauseManager = this;

            // If the user also added a UnityEngine.UI.Button component to 'pause', wire it too
            Button optionalBtn = hudPauseButton.GetComponent<Button>();
            if (optionalBtn != null)
            {
                optionalBtn.onClick.RemoveListener(PauseGame);
                optionalBtn.onClick.AddListener(PauseGame);
            }
        }
    }

    private Transform FindHudPauseImageTransform()
    {
        // 1. Search under playerController.cropPanel ("croper") first
        if (playerController != null && playerController.cropPanel != null)
        {
            Transform found = FindPauseChildRecursive(playerController.cropPanel);
            if (found != null) return found;
        }

        // 2. Search any RectTransform named "croper" in the scene
        RectTransform[] allRects = FindObjectsOfType<RectTransform>(true);
        for (int i = 0; i < allRects.Length; i++)
        {
            RectTransform rt = allRects[i];
            if (rt == null) continue;
            if (pauseCanvas != null && rt.IsChildOf(pauseCanvas.transform)) continue;

            if (string.Equals(rt.name.Trim(), "croper", StringComparison.OrdinalIgnoreCase))
            {
                Transform found = FindPauseChildRecursive(rt);
                if (found != null) return found;
            }
        }

        // 3. Fallback: any UI Image named "pause" outside PauseCanvas
        for (int i = 0; i < allRects.Length; i++)
        {
            RectTransform rt = allRects[i];
            if (rt == null) continue;
            if (pauseCanvas != null && rt.IsChildOf(pauseCanvas.transform)) continue;
            if (rt.gameObject == gameObject) continue;

            if (string.Equals(rt.name.Trim(), "pause", StringComparison.OrdinalIgnoreCase) && rt.GetComponent<Image>() != null)
            {
                return rt;
            }
        }

        return null;
    }

    private Transform FindPauseChildRecursive(Transform parent)
    {
        if (parent == null) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null) continue;
            if (pauseCanvas != null && child.IsChildOf(pauseCanvas.transform)) continue;

            string trimmedName = child.name.Trim();
            if (string.Equals(trimmedName, "pause", StringComparison.OrdinalIgnoreCase)
                || trimmedName.IndexOf("pause", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (child.GetComponent<Image>() != null)
                {
                    return child;
                }
            }

            Transform nested = FindPauseChildRecursive(child);
            if (nested != null) return nested;
        }

        return null;
    }

    private void BindUIEvents()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveListener(ResumeGame);
            resumeButton.onClick.AddListener(ResumeGame);
        }
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(OpenSettings);
            settingsButton.onClick.AddListener(OpenSettings);
        }
        if (endRunButton != null)
        {
            endRunButton.onClick.RemoveListener(EndRun);
            endRunButton.onClick.AddListener(EndRun);
        }
        if (backFromSettingsButton != null)
        {
            backFromSettingsButton.onClick.RemoveListener(CloseSettings);
            backFromSettingsButton.onClick.AddListener(CloseSettings);
        }
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
        }
    }

    private void EnsureRuntimeFallbackUIIfMissing()
    {
        if (pauseCanvas != null) return;

        Canvas existingChildCanvas = GetComponentInChildren<Canvas>(true);
        if (existingChildCanvas != null)
        {
            pauseCanvas = existingChildCanvas;
            return;
        }

        TMP_FontAsset fontAsset = TMP_Settings.defaultFontAsset;

        GameObject canvasObj = new GameObject("PauseCanvas");
        canvasObj.layer = 5;
        canvasObj.transform.SetParent(transform, false);

        pauseCanvas = canvasObj.AddComponent<Canvas>();
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            pauseCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            pauseCanvas.worldCamera = mainCam;
            pauseCanvas.planeDistance = 0.08f;
        }
        else
        {
            pauseCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        pauseCanvas.sortingOrder = 5000;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject blockerObj = new GameObject("PauseBackground_RaycastBlocker", typeof(RectTransform));
        blockerObj.layer = 5;
        blockerObj.transform.SetParent(canvasObj.transform, false);
        RectTransform blockerRT = blockerObj.GetComponent<RectTransform>();
        blockerRT.anchorMin = Vector2.zero;
        blockerRT.anchorMax = Vector2.one;
        blockerRT.offsetMin = Vector2.zero;
        blockerRT.offsetMax = Vector2.zero;
        Image blockerImg = blockerObj.AddComponent<Image>();
        blockerImg.color = new Color(0.02f, 0.02f, 0.03f, 0.78f);
        blockerImg.raycastTarget = true;

        mainButtonsPanel = new GameObject("PauseMainMenuPanel", typeof(RectTransform));
        mainButtonsPanel.layer = 5;
        mainButtonsPanel.transform.SetParent(canvasObj.transform, false);
        RectTransform mainRT = mainButtonsPanel.GetComponent<RectTransform>();
        mainRT.sizeDelta = new Vector2(620f, 540f);
        Image mainBg = mainButtonsPanel.AddComponent<Image>();
        mainBg.color = new Color(0.08f, 0.08f, 0.10f, 0.92f);

        CreateRuntimeText("PausedTitle", mainButtonsPanel.transform, "PAUSED", fontAsset, 54f, new Vector2(0f, 185f), new Vector2(500f, 80f), Color.white);
        resumeButton = CreateRuntimeButton("ResumeButton", mainButtonsPanel.transform, "RESUME", fontAsset, new Vector2(0f, 65f), new Vector2(360f, 68f), new Color(0.92f, 0.92f, 0.92f, 1f));
        settingsButton = CreateRuntimeButton("SettingsButton", mainButtonsPanel.transform, "SETTINGS", fontAsset, new Vector2(0f, -25f), new Vector2(360f, 68f), new Color(0.92f, 0.92f, 0.92f, 1f));
        endRunButton = CreateRuntimeButton("EndRunButton", mainButtonsPanel.transform, "END RUN", fontAsset, new Vector2(0f, -115f), new Vector2(360f, 68f), new Color(0.85f, 0.25f, 0.25f, 1f));

        settingsPanel = new GameObject("SettingsPanel", typeof(RectTransform));
        settingsPanel.layer = 5;
        settingsPanel.transform.SetParent(canvasObj.transform, false);
        RectTransform settingsRT = settingsPanel.GetComponent<RectTransform>();
        settingsRT.sizeDelta = new Vector2(660f, 540f);
        Image settingsBg = settingsPanel.AddComponent<Image>();
        settingsBg.color = new Color(0.08f, 0.08f, 0.10f, 0.92f);

        CreateRuntimeText("SettingsTitle", settingsPanel.transform, "SETTINGS", fontAsset, 50f, new Vector2(0f, 190f), new Vector2(500f, 75f), Color.white);
        CreateRuntimeText("MusicLabel", settingsPanel.transform, "MUSIC", fontAsset, 30f, new Vector2(0f, 100f), new Vector2(460f, 45f), Color.white);
        musicVolumeSlider = CreateRuntimeSlider("MusicVolumeSlider", settingsPanel.transform, new Vector2(0f, 50f), new Vector2(440f, 36f));

        CreateRuntimeText("SfxLabel", settingsPanel.transform, "SFX / OTHER SOUNDS", fontAsset, 30f, new Vector2(0f, -25f), new Vector2(460f, 45f), Color.white);
        sfxVolumeSlider = CreateRuntimeSlider("SfxVolumeSlider", settingsPanel.transform, new Vector2(0f, -75f), new Vector2(440f, 36f));

        backFromSettingsButton = CreateRuntimeButton("BackButton", settingsPanel.transform, "BACK", fontAsset, new Vector2(0f, -180f), new Vector2(280f, 62f), new Color(0.92f, 0.92f, 0.92f, 1f));
    }

    private static TextMeshProUGUI CreateRuntimeText(string name, Transform parent, string text, TMP_FontAsset font, float size, Vector2 pos, Vector2 sizeDelta, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateRuntimeButton(string name, Transform parent, string label, TMP_FontAsset font, Vector2 pos, Vector2 sizeDelta, Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;

        Image img = go.AddComponent<Image>();
        img.color = bgColor;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        GameObject txtGo = new GameObject("Text (TMP)", typeof(RectTransform));
        txtGo.layer = 5;
        txtGo.transform.SetParent(go.transform, false);
        RectTransform txtRT = txtGo.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        if (font != null) tmp.font = font;
        tmp.fontSize = 32f;
        tmp.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return btn;
    }

    private static Slider CreateRuntimeSlider(string name, Transform parent, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject sliderObj = new GameObject(name, typeof(RectTransform));
        sliderObj.layer = 5;
        sliderObj.transform.SetParent(parent, false);
        RectTransform sliderRT = sliderObj.GetComponent<RectTransform>();
        sliderRT.anchoredPosition = pos;
        sliderRT.sizeDelta = sizeDelta;

        GameObject bgObj = new GameObject("Background", typeof(RectTransform));
        bgObj.layer = 5;
        bgObj.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRT = bgObj.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.25f);
        bgRT.anchorMax = new Vector2(1f, 0.75f);
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.22f, 0.22f, 0.25f, 1f);

        GameObject fillAreaObj = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaObj.layer = 5;
        fillAreaObj.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRT = fillAreaObj.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRT.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRT.offsetMin = new Vector2(5f, 0f);
        fillAreaRT.offsetMax = new Vector2(-15f, 0f);

        GameObject fillObj = new GameObject("Fill", typeof(RectTransform));
        fillObj.layer = 5;
        fillObj.transform.SetParent(fillAreaObj.transform, false);
        RectTransform fillRT = fillObj.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = new Vector2(10f, 0f);
        Image fillImg = fillObj.AddComponent<Image>();
        fillImg.color = new Color(0.85f, 0.85f, 0.85f, 1f);

        GameObject handleAreaObj = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaObj.layer = 5;
        handleAreaObj.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRT = handleAreaObj.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = Vector2.zero;
        handleAreaRT.anchorMax = Vector2.one;
        handleAreaRT.offsetMin = new Vector2(10f, 0f);
        handleAreaRT.offsetMax = new Vector2(-10f, 0f);

        GameObject handleObj = new GameObject("Handle", typeof(RectTransform));
        handleObj.layer = 5;
        handleObj.transform.SetParent(handleAreaObj.transform, false);
        RectTransform handleRT = handleObj.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0f, 0f);
        handleRT.anchorMax = new Vector2(0f, 1f);
        handleRT.sizeDelta = new Vector2(26f, 0f);
        Image handleImg = handleObj.AddComponent<Image>();
        handleImg.color = Color.white;

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 1f;

        return slider;
    }
}
