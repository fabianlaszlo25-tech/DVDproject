using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Controls the Main Menu in 06Menu.
/// - Manages the 3 main buttons: START RUN (loads 01Day), SETTINGS, and QUIT (Windows & WebGL).
/// - Provides a smooth crossfade transition between the Main Buttons and the Settings Panel.
/// - Hosts the same global audio sliders (Music & SFX) as the in-game Pause Menu via <see cref="GlobalAudioSettings"/>.
/// - Supports both Unity UI EventSystem and direct regular-cursor hit-testing/slider dragging
///   for 100% reliability on world-space TV canvases in Editor, Standalone, and WebGL.
/// </summary>
[DefaultExecutionOrder(10)]
public class MainMenuController : MonoBehaviour
{
    private const string DefaultFontGuid = "6d795282a2f39f94ab1e799f42073e3f";

    [Header("Scenes & Transitions")]
    public string mainSceneName = "01Day";
    public SceneLoaderFade sceneLoaderFade;

    [Header("Panels & Containers")]
    [Tooltip("Container holding the 3 main menu buttons.")]
    public GameObject mainButtonsPanel;
    [Tooltip("Panel containing the Music & SFX audio sliders and Back button.")]
    public GameObject settingsPanel;

    [Header("Main Menu Buttons")]
    public Button startRunButton;
    public Button settingsButton;
    public Button quitButton;

    [Header("Settings Panel UI")]
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public TextMeshProUGUI musicValueText;
    public TextMeshProUGUI sfxValueText;
    public Button backFromSettingsButton;

    [Header("Visual & Transition Settings")]
    public TMP_FontAsset fontAsset;
    [Range(0.15f, 0.6f)] public float transitionDuration = 0.28f;
    public float transitionHorizontalDrift = 25f;

    [Header("Audio Feedback (Optional)")]
    public AudioSource menuAudioSource;
    public AudioClip buttonClickSound;
    [Range(0f, 1f)] public float buttonClickVolume = 0.85f;

    private CanvasGroup mainButtonsCanvasGroup;
    private CanvasGroup settingsCanvasGroup;
    private Coroutine transitionRoutine;
    private bool isTransitioning = false;
    private bool isUpdatingSlidersInternally = false;
    private Slider activeDraggedSlider = null;

    private Vector2 mainButtonsBasePos;
    private Vector2 settingsBasePos;
    private bool hasRecordedBasePositions = false;

    private void Awake()
    {
        EnsureCursorUnlocked();
        EnsureReferencesAndUI();
        BindUIEvents();
    }

    private void Start()
    {
        EnsureCursorUnlocked();
        GlobalAudioSettings.ApplyLoadedSettings();
        SyncSlidersToGlobalAudio();
    }

    private void OnEnable()
    {
        EnsureCursorUnlocked();
        SyncSlidersToGlobalAudio();
    }

    private void Update()
    {
        EnsureCursorUnlocked();
        HandleDirectRegularCursorMenuUI();
    }

    private void EnsureCursorUnlocked()
    {
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
        }
        if (!Cursor.visible)
        {
            Cursor.visible = true;
        }
        if (Time.timeScale < 0.99f)
        {
            Time.timeScale = 1f;
        }
        if (AudioListener.pause)
        {
            AudioListener.pause = false;
        }
    }

    // =========================================================================
    // BUTTON ACTIONS
    // =========================================================================

    public void OnStartRunClicked()
    {
        if (isTransitioning) return;
        PlayClickSound();

        // 1. Reset run statistics for a fresh game
        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.ResetRunForNewGame();
        }

        // 2. Load the main room scene cleanly
        if (sceneLoaderFade == null) sceneLoaderFade = FindObjectOfType<SceneLoaderFade>();
        if (sceneLoaderFade != null)
        {
            sceneLoaderFade.LoadSceneWithFade(mainSceneName);
        }
        else
        {
            SceneManager.LoadScene(mainSceneName);
        }
    }

    public void OnSettingsClicked()
    {
        if (isTransitioning) return;
        PlayClickSound();
        SwitchToSettings(true);
    }

    public void OnBackFromSettingsClicked()
    {
        if (isTransitioning) return;
        PlayClickSound();
        SwitchToSettings(false);
    }

    public void OnQuitClicked()
    {
        PlayClickSound();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        try
        {
            Application.ExternalEval("window.open('', '_self', ''); window.close(); if(!window.closed) { window.location.href = 'about:blank'; }");
        }
        catch { }
        Application.Quit();
#else
        Application.Quit();
#endif
    }

    private void PlayClickSound()
    {
        if (buttonClickSound == null || menuAudioSource == null) return;
        menuAudioSource.PlayOneShot(buttonClickSound, buttonClickVolume);
    }

    // =========================================================================
    // SMOOTH MENU <-> SETTINGS TRANSITIONS
    // =========================================================================

    public void SwitchToSettings(bool toSettings)
    {
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
        }
        transitionRoutine = StartCoroutine(TransitionRoutine(toSettings));
    }

    private IEnumerator TransitionRoutine(bool toSettings)
    {
        isTransitioning = true;
        activeDraggedSlider = null;

        if (toSettings)
        {
            SyncSlidersToGlobalAudio();
        }

        EnsureCanvasGroups();
        RecordBasePositions();

        CanvasGroup outgoing = toSettings ? mainButtonsCanvasGroup : settingsCanvasGroup;
        CanvasGroup incoming = toSettings ? settingsCanvasGroup : mainButtonsCanvasGroup;

        RectTransform outgoingRT = outgoing.GetComponent<RectTransform>();
        RectTransform incomingRT = incoming.GetComponent<RectTransform>();

        Vector2 outBase = toSettings ? mainButtonsBasePos : settingsBasePos;
        Vector2 inBase = toSettings ? settingsBasePos : mainButtonsBasePos;

        Vector2 outTargetPos = outBase + (toSettings ? new Vector2(-transitionHorizontalDrift, 0f) : new Vector2(transitionHorizontalDrift, 0f));
        Vector2 inStartPos = inBase + (toSettings ? new Vector2(transitionHorizontalDrift, 0f) : new Vector2(-transitionHorizontalDrift, 0f));

        incoming.gameObject.SetActive(true);
        incoming.alpha = 0f;
        incoming.interactable = false;
        incoming.blocksRaycasts = false;
        incomingRT.anchoredPosition = inStartPos;

        outgoing.interactable = false;
        outgoing.blocksRaycasts = false;

        float elapsed = 0f;
        float dur = Mathf.Max(0.05f, transitionDuration);

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            outgoing.alpha = 1f - smoothT;
            incoming.alpha = smoothT;

            outgoingRT.anchoredPosition = Vector2.Lerp(outBase, outTargetPos, smoothT);
            incomingRT.anchoredPosition = Vector2.Lerp(inStartPos, inBase, smoothT);

            yield return null;
        }

        outgoing.alpha = 0f;
        outgoing.gameObject.SetActive(false);
        outgoingRT.anchoredPosition = outBase;

        incoming.alpha = 1f;
        incomingRT.anchoredPosition = inBase;
        incoming.interactable = true;
        incoming.blocksRaycasts = true;

        isTransitioning = false;
        transitionRoutine = null;
    }

    private void EnsureCanvasGroups()
    {
        if (mainButtonsPanel != null && mainButtonsCanvasGroup == null)
        {
            mainButtonsCanvasGroup = mainButtonsPanel.GetComponent<CanvasGroup>();
            if (mainButtonsCanvasGroup == null) mainButtonsCanvasGroup = mainButtonsPanel.AddComponent<CanvasGroup>();
        }

        if (settingsPanel != null && settingsCanvasGroup == null)
        {
            settingsCanvasGroup = settingsPanel.GetComponent<CanvasGroup>();
            if (settingsCanvasGroup == null) settingsCanvasGroup = settingsPanel.AddComponent<CanvasGroup>();
        }
    }

    private void RecordBasePositions()
    {
        if (hasRecordedBasePositions) return;

        if (mainButtonsPanel != null)
        {
            RectTransform rt = mainButtonsPanel.GetComponent<RectTransform>();
            if (rt != null) mainButtonsBasePos = rt.anchoredPosition;
        }

        if (settingsPanel != null)
        {
            RectTransform rt = settingsPanel.GetComponent<RectTransform>();
            if (rt != null) settingsBasePos = rt.anchoredPosition;
        }

        hasRecordedBasePositions = true;
    }

    // =========================================================================
    // AUDIO SETTINGS & SLIDERS
    // =========================================================================

    public void OnMusicSliderChanged(float value)
    {
        if (isUpdatingSlidersInternally) return;

        float clamped = Mathf.Clamp01(value);
        GlobalAudioSettings.SetMusicVolume(clamped);
        UpdateSliderLabels();
    }

    public void OnSfxSliderChanged(float value)
    {
        if (isUpdatingSlidersInternally) return;

        float clamped = Mathf.Clamp01(value);
        GlobalAudioSettings.SetSfxVolume(clamped);
        UpdateSliderLabels();
    }

    private void SyncSlidersToGlobalAudio()
    {
        isUpdatingSlidersInternally = true;
        try
        {
            if (musicVolumeSlider != null)
            {
                musicVolumeSlider.SetValueWithoutNotify(GlobalAudioSettings.MusicVolume);
            }
            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.SetValueWithoutNotify(GlobalAudioSettings.SfxVolume);
            }
            UpdateSliderLabels();
        }
        finally
        {
            isUpdatingSlidersInternally = false;
        }
    }

    private void UpdateSliderLabels()
    {
        if (musicValueText != null && musicVolumeSlider != null)
        {
            musicValueText.text = Mathf.RoundToInt(musicVolumeSlider.value * 100f) + "%";
        }
        if (sfxValueText != null && sfxVolumeSlider != null)
        {
            sfxValueText.text = Mathf.RoundToInt(sfxVolumeSlider.value * 100f) + "%";
        }
    }

    // =========================================================================
    // UI INITIALIZATION & FALLBACK CONSTRUCTION
    // =========================================================================

    private void EnsureReferencesAndUI()
    {
        if (sceneLoaderFade == null) sceneLoaderFade = FindObjectOfType<SceneLoaderFade>();

        Transform holderTransform = (transform.name == "holder") ? transform : transform.Find("holder");
        if (holderTransform == null)
        {
            GameObject holderObj = GameObject.Find("holder");
            if (holderObj != null) holderTransform = holderObj.transform;
        }

        if (fontAsset == null && holderTransform != null)
        {
            TextMeshProUGUI existingTmp = holderTransform.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existingTmp != null && existingTmp.font != null)
            {
                fontAsset = existingTmp.font;
            }
        }

        // 1. Resolve Main Buttons Panel from Inspector or Scene Hierarchy
        if (mainButtonsPanel == null && holderTransform != null)
        {
            Transform existingMain = holderTransform.Find("MainButtonsPanel");
            if (existingMain != null)
            {
                mainButtonsPanel = existingMain.gameObject;
            }
        }

        // Locate buttons inside mainButtonsPanel or holder
        Transform buttonsContainer = (mainButtonsPanel != null) ? mainButtonsPanel.transform : holderTransform;
        if (buttonsContainer != null)
        {
            if (startRunButton == null)
            {
                startRunButton = FindButtonByNameOrIndex(buttonsContainer, "Story", "StartRun", 0);
            }
            if (settingsButton == null)
            {
                settingsButton = FindButtonByNameOrIndex(buttonsContainer, "Settings", "Settings", 1);
            }
            if (quitButton == null)
            {
                quitButton = FindButtonByNameOrIndex(buttonsContainer, "Quit", "Quit", 2);
            }
        }

        // Relabel Start Run button if it still says Story
        if (startRunButton != null)
        {
            TextMeshProUGUI tmp = startRunButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null && !tmp.text.Equals("START RUN", StringComparison.OrdinalIgnoreCase))
            {
                tmp.text = "START RUN";
            }
        }

        // 2. Resolve Settings Panel from Inspector or Scene Hierarchy (No dynamic runtime creation)
        if (settingsPanel == null && holderTransform != null)
        {
            Transform existingSettings = holderTransform.Find("SettingsPanel");
            if (existingSettings != null)
            {
                settingsPanel = existingSettings.gameObject;
            }
        }

        // Locate Settings Panel controls if not already assigned
        if (settingsPanel != null)
        {
            if (musicVolumeSlider == null)
            {
                Slider[] sliders = settingsPanel.GetComponentsInChildren<Slider>(true);
                foreach (Slider s in sliders)
                {
                    if (s.name.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        musicVolumeSlider = s;
                        break;
                    }
                }
                if (musicVolumeSlider == null && sliders.Length > 0) musicVolumeSlider = sliders[0];
            }

            if (sfxVolumeSlider == null)
            {
                Slider[] sliders = settingsPanel.GetComponentsInChildren<Slider>(true);
                foreach (Slider s in sliders)
                {
                    if (s.name.IndexOf("Sfx", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sfxVolumeSlider = s;
                        break;
                    }
                }
                if (sfxVolumeSlider == null && sliders.Length > 1) sfxVolumeSlider = sliders[1];
            }

            if (backFromSettingsButton == null)
            {
                Button[] buttons = settingsPanel.GetComponentsInChildren<Button>(true);
                foreach (Button b in buttons)
                {
                    if (b.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        backFromSettingsButton = b;
                        break;
                    }
                }
                if (backFromSettingsButton == null && buttons.Length > 0) backFromSettingsButton = buttons[0];
            }

            if (musicValueText == null)
            {
                TextMeshProUGUI[] tmps = settingsPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (TextMeshProUGUI t in tmps)
                {
                    if (t.name.IndexOf("MusicValue", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        musicValueText = t;
                        break;
                    }
                }
            }

            if (sfxValueText == null)
            {
                TextMeshProUGUI[] tmps = settingsPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (TextMeshProUGUI t in tmps)
                {
                    if (t.name.IndexOf("SfxValue", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sfxValueText = t;
                        break;
                    }
                }
            }
        }

        EnsureCanvasGroups();
        RecordBasePositions();

        // Settings panel starts hidden at runtime
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            if (settingsCanvasGroup != null)
            {
                settingsCanvasGroup.alpha = 0f;
                settingsCanvasGroup.interactable = false;
                settingsCanvasGroup.blocksRaycasts = false;
            }
        }

        // Main buttons start visible and interactable at runtime
        if (mainButtonsPanel != null)
        {
            mainButtonsPanel.SetActive(true);
            if (mainButtonsCanvasGroup != null)
            {
                mainButtonsCanvasGroup.alpha = 1f;
                mainButtonsCanvasGroup.interactable = true;
                mainButtonsCanvasGroup.blocksRaycasts = true;
            }
        }
    }

    private Button FindButtonByNameOrIndex(Transform parent, string match1, string match2, int fallbackIdx)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null) continue;
            string name = child.name;
            if (name.IndexOf(match1, StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf(match2, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Button b = child.GetComponent<Button>();
                if (b != null) return b;
            }
        }

        Button[] buttons = parent.GetComponentsInChildren<Button>(true);
        if (buttons != null && fallbackIdx >= 0 && fallbackIdx < buttons.Length)
        {
            return buttons[fallbackIdx];
        }

        return null;
    }

    private void BindUIEvents()
    {
        if (startRunButton != null)
        {
            startRunButton.onClick.RemoveListener(OnStartRunClicked);
            startRunButton.onClick.AddListener(OnStartRunClicked);
        }
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(OnSettingsClicked);
            settingsButton.onClick.AddListener(OnSettingsClicked);
        }
        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(OnQuitClicked);
            quitButton.onClick.AddListener(OnQuitClicked);
        }
        if (backFromSettingsButton != null)
        {
            backFromSettingsButton.onClick.RemoveListener(OnBackFromSettingsClicked);
            backFromSettingsButton.onClick.AddListener(OnBackFromSettingsClicked);
        }
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicSliderChanged);
            musicVolumeSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        }
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxSliderChanged);
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxSliderChanged);
        }
    }

    // =========================================================================
    // DIRECT REGULAR-CURSOR HIT-TESTING & SLIDER DRAGGING FALLBACK
    // =========================================================================

    private void HandleDirectRegularCursorMenuUI()
    {
        if (isTransitioning) return;

        Vector2 mouseScreenPos = GetMousePosition();
        Camera uiCam = GetMenuCamera();

        bool settingsActive = settingsPanel != null && settingsPanel.activeInHierarchy;
        bool mainActive = mainButtonsPanel != null && mainButtonsPanel.activeInHierarchy && !settingsActive;

        bool leftPressedThisFrame = WasLeftMousePressedThisFrame();
        bool leftHeld = IsLeftMousePressed();

        if (settingsActive)
        {
            if (leftPressedThisFrame)
            {
                if (IsScreenPointNearSlider(musicVolumeSlider, mouseScreenPos, uiCam, 16f))
                {
                    activeDraggedSlider = musicVolumeSlider;
                }
                else if (IsScreenPointNearSlider(sfxVolumeSlider, mouseScreenPos, uiCam, 16f))
                {
                    activeDraggedSlider = sfxVolumeSlider;
                }
                else if (IsScreenPointOverButton(backFromSettingsButton, mouseScreenPos, uiCam, 6f))
                {
                    OnBackFromSettingsClicked();
                    return;
                }
            }

            if (activeDraggedSlider != null)
            {
                if (leftHeld)
                {
                    UpdateSliderValueFromScreenPoint(activeDraggedSlider, mouseScreenPos, uiCam);
                }
                else
                {
                    activeDraggedSlider = null;
                }
            }
        }
        else if (mainActive && leftPressedThisFrame)
        {
            if (IsScreenPointOverButton(startRunButton, mouseScreenPos, uiCam, 6f))
            {
                OnStartRunClicked();
            }
            else if (IsScreenPointOverButton(settingsButton, mouseScreenPos, uiCam, 6f))
            {
                OnSettingsClicked();
            }
            else if (IsScreenPointOverButton(quitButton, mouseScreenPos, uiCam, 6f))
            {
                OnQuitClicked();
            }
        }
    }

    private Camera GetMenuCamera()
    {
        Canvas c = GetComponentInParent<Canvas>();
        if (c != null && c.worldCamera != null) return c.worldCamera;
        return Camera.main;
    }

    private Vector2 GetMousePosition()
    {
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
        return Vector2.zero;
    }

    private bool WasLeftMousePressedThisFrame()
    {
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    private bool IsLeftMousePressed()
    {
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    private bool IsScreenPointOverButton(Button btn, Vector2 screenPos, Camera uiCam, float padding)
    {
        if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) return false;
        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt == null) return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, uiCam, out Vector2 localPoint))
        {
            return false;
        }

        Rect r = rt.rect;
        r.xMin -= padding;
        r.xMax += padding;
        r.yMin -= padding;
        r.yMax += padding;
        return r.Contains(localPoint);
    }

    private bool IsScreenPointNearSlider(Slider slider, Vector2 screenPos, Camera uiCam, float padding)
    {
        if (slider == null || !slider.gameObject.activeInHierarchy || !slider.interactable) return false;
        RectTransform rt = slider.GetComponent<RectTransform>();
        if (rt == null) return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, uiCam, out Vector2 localPoint))
        {
            return false;
        }

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
        RectTransform rt = slider.GetComponent<RectTransform>();
        if (rt == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, uiCam, out Vector2 localPoint))
        {
            float width = rt.rect.width;
            if (width > 0.01f)
            {
                float normalized = Mathf.Clamp01((localPoint.x - rt.rect.xMin) / width);
                slider.value = Mathf.Lerp(slider.minValue, slider.maxValue, normalized);
            }
        }
    }
}
