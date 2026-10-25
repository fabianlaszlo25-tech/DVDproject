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

        // 1. Resolve or wrap Main Buttons
        if (mainButtonsPanel == null && holderTransform != null)
        {
            Transform existingMain = holderTransform.Find("MainButtonsPanel");
            if (existingMain != null)
            {
                mainButtonsPanel = existingMain.gameObject;
            }
            else
            {
                // Create a sub-panel inside holder for the 3 main buttons
                GameObject mbp = new GameObject("MainButtonsPanel", typeof(RectTransform));
                mbp.layer = 5;
                mbp.transform.SetParent(holderTransform, false);
                RectTransform mbpRT = mbp.GetComponent<RectTransform>();
                mbpRT.anchorMin = new Vector2(0f, 0.5f);
                mbpRT.anchorMax = new Vector2(0f, 0.5f);
                mbpRT.pivot = new Vector2(0.5f, 0.5f);
                mbpRT.anchoredPosition = new Vector2(-529.6f, -140f);
                mbpRT.sizeDelta = new Vector2(200f, 300f);
                mainButtonsPanel = mbp;

                // Reparent existing Story, Settings, Quit buttons into mbp
                ReparentButtonIfFound(holderTransform, "Story", mbp.transform, new Vector2(0f, 115f), "START RUN");
                ReparentButtonIfFound(holderTransform, "Settings", mbp.transform, new Vector2(0f, 0f), "SETTINGS");
                ReparentButtonIfFound(holderTransform, "Quit", mbp.transform, new Vector2(0f, -115f), "QUIT");

                // Remove or deactivate temporary Endless button
                Transform endlessBtn = holderTransform.Find("Endless");
                if (endlessBtn != null)
                {
                    endlessBtn.gameObject.SetActive(false);
                }
            }
        }

        // Locate buttons inside mainButtonsPanel or scene
        if (startRunButton == null && mainButtonsPanel != null)
        {
            startRunButton = FindButtonByNameOrIndex(mainButtonsPanel.transform, "Story", "StartRun", 0);
        }
        if (settingsButton == null && mainButtonsPanel != null)
        {
            settingsButton = FindButtonByNameOrIndex(mainButtonsPanel.transform, "Settings", "Settings", 1);
        }
        if (quitButton == null && mainButtonsPanel != null)
        {
            quitButton = FindButtonByNameOrIndex(mainButtonsPanel.transform, "Quit", "Quit", 2);
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

        // 2. Resolve or build Settings Panel
        if (settingsPanel == null && holderTransform != null)
        {
            Transform existingSettings = holderTransform.Find("SettingsPanel");
            if (existingSettings != null)
            {
                settingsPanel = existingSettings.gameObject;
            }
            else
            {
                BuildSettingsPanelRuntime(holderTransform);
            }
        }

        EnsureCanvasGroups();
        RecordBasePositions();

        // Settings panel starts hidden
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

        if (mainButtonsPanel != null && mainButtonsCanvasGroup != null)
        {
            mainButtonsCanvasGroup.alpha = 1f;
            mainButtonsCanvasGroup.interactable = true;
            mainButtonsCanvasGroup.blocksRaycasts = true;
        }
    }

    private void ReparentButtonIfFound(Transform holder, string btnName, Transform newParent, Vector2 localPos, string newLabel)
    {
        Transform btnTrans = holder.Find(btnName);
        if (btnTrans != null)
        {
            btnTrans.SetParent(newParent, false);
            RectTransform rt = btnTrans.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = localPos;
                rt.sizeDelta = new Vector2(180f, 32f);
                rt.localScale = Vector3.one * 2.5f;
            }

            TextMeshProUGUI txt = btnTrans.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null && !string.IsNullOrEmpty(newLabel))
            {
                txt.text = newLabel;
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

    private void BuildSettingsPanelRuntime(Transform parent)
    {
        GameObject sp = new GameObject("SettingsPanel", typeof(RectTransform));
        sp.layer = 5;
        sp.transform.SetParent(parent, false);
        RectTransform spRT = sp.GetComponent<RectTransform>();
        spRT.anchorMin = new Vector2(0f, 0.5f);
        spRT.anchorMax = new Vector2(0f, 0.5f);
        spRT.pivot = new Vector2(0.5f, 0.5f);
        spRT.anchoredPosition = new Vector2(-529.6f, -140f);
        spRT.sizeDelta = new Vector2(190f, 130f);
        spRT.localScale = Vector3.one * 2.5f;
        settingsPanel = sp;

        Image bg = sp.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.10f, 0.90f);

        // Title
        CreateText("SettingsTitle", sp.transform, "SETTINGS", 18f, new Vector2(0f, 48f), new Vector2(180f, 22f), Color.white, TextAlignmentOptions.Center);

        // Music Section
        CreateText("MusicLabel", sp.transform, "MUSIC", 11f, new Vector2(-48f, 22f), new Vector2(80f, 16f), new Color(0.85f, 0.85f, 0.85f, 1f), TextAlignmentOptions.Left);
        musicValueText = CreateText("MusicValue", sp.transform, "100%", 11f, new Vector2(48f, 22f), new Vector2(60f, 16f), new Color(0.75f, 0.75f, 0.75f, 1f), TextAlignmentOptions.Right);
        musicVolumeSlider = CreateSlider("MusicSlider", sp.transform, new Vector2(0f, 8f), new Vector2(165f, 12f));

        // SFX Section
        CreateText("SfxLabel", sp.transform, "SFX", 11f, new Vector2(-48f, -12f), new Vector2(80f, 16f), new Color(0.85f, 0.85f, 0.85f, 1f), TextAlignmentOptions.Left);
        sfxValueText = CreateText("SfxValue", sp.transform, "100%", 11f, new Vector2(48f, -12f), new Vector2(60f, 16f), new Color(0.75f, 0.75f, 0.75f, 1f), TextAlignmentOptions.Right);
        sfxVolumeSlider = CreateSlider("SfxSlider", sp.transform, new Vector2(0f, -26f), new Vector2(165f, 12f));

        // Back Button
        backFromSettingsButton = CreateButton("BackButton", sp.transform, "BACK", new Vector2(0f, -48f), new Vector2(95f, 20f));
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Vector2 pos, Vector2 sizeDelta, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (fontAsset != null) tmp.font = fontAsset;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private Button CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.92f, 0.92f, 0.92f, 1f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        cb.selectedColor = Color.white;
        btn.colors = cb;

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
        if (fontAsset != null) tmp.font = fontAsset;
        tmp.fontSize = 13f;
        tmp.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return btn;
    }

    private Slider CreateSlider(string name, Transform parent, Vector2 pos, Vector2 sizeDelta)
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
        bgImg.color = new Color(0.24f, 0.24f, 0.28f, 1f);

        GameObject fillAreaObj = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaObj.layer = 5;
        fillAreaObj.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRT = fillAreaObj.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRT.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRT.offsetMin = new Vector2(3f, 0f);
        fillAreaRT.offsetMax = new Vector2(-10f, 0f);

        GameObject fillObj = new GameObject("Fill", typeof(RectTransform));
        fillObj.layer = 5;
        fillObj.transform.SetParent(fillAreaObj.transform, false);
        RectTransform fillRT = fillObj.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = new Vector2(6f, 0f);
        Image fillImg = fillObj.AddComponent<Image>();
        fillImg.color = new Color(0.88f, 0.88f, 0.90f, 1f);

        GameObject handleAreaObj = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaObj.layer = 5;
        handleAreaObj.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRT = handleAreaObj.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = Vector2.zero;
        handleAreaRT.anchorMax = Vector2.one;
        handleAreaRT.offsetMin = new Vector2(6f, 0f);
        handleAreaRT.offsetMax = new Vector2(-6f, 0f);

        GameObject handleObj = new GameObject("Handle", typeof(RectTransform));
        handleObj.layer = 5;
        handleObj.transform.SetParent(handleAreaObj.transform, false);
        RectTransform handleRT = handleObj.GetComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(14f, 14f);
        Image handleImg = handleObj.AddComponent<Image>();
        handleImg.color = Color.white;

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;

        return slider;
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
