using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Controls the Main Menu in 06Menu.
/// - Manages the 4 main buttons: START RUN (loads 01Day), ARCADE (loads 07Arcade), SETTINGS, and QUIT (Windows & WebGL).
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
    public string arcadeSceneName = "07Arcade";
    public SceneLoaderFade sceneLoaderFade;

    [Header("Panels & Containers")]
    [Tooltip("Container holding the main menu buttons.")]
    public GameObject mainButtonsPanel;
    [Tooltip("Panel containing the Music & SFX audio sliders and Back button.")]
    public GameObject settingsPanel;
    [Tooltip("Panel asking whether the player wants to play With Tutorial or Without Tutorial.")]
    public GameObject tutorialPromptPanel;

    [Header("Main Menu Buttons")]
    public Button startRunButton;
    public Button arcadeButton;
    public Button settingsButton;
    public Button quitButton;

    [Header("Tutorial Prompt Buttons")]
    public Button playWithTutorialButton;
    public Button playWithoutTutorialButton;
    public Button backFromTutorialButton;

    [Header("Settings Panel UI")]
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public Slider sensitivitySlider;
    public TextMeshProUGUI musicValueText;
    public TextMeshProUGUI sfxValueText;
    public TextMeshProUGUI sensitivityValueText;
    public Button backFromSettingsButton;

    [Header("Visual & Transition Settings")]
    public TMP_FontAsset fontAsset;
    [Range(0.15f, 0.6f)] public float transitionDuration = 0.28f;
    public float transitionHorizontalDrift = 25f;

    [Header("Audio Feedback (Optional)")]
    public AudioSource menuAudioSource;
    public AudioClip buttonClickSound;
    [Range(0f, 1f)] public float buttonClickVolume = 0.85f;

    [Header("Menu Music")]
    [Tooltip("AudioSource for the main menu background music (e.g. on GameObject 'mainmenumusic'). Governed by Music Volume setting.")]
    public AudioSource mainMenuMusicSource;

    private CanvasGroup mainButtonsCanvasGroup;
    private CanvasGroup settingsCanvasGroup;
    private CanvasGroup tutorialPromptCanvasGroup;
    private Coroutine transitionRoutine;
    private bool isTransitioning = false;
    private bool isUpdatingSlidersInternally = false;
    private Slider activeDraggedSlider = null;

    private Vector2 mainButtonsBasePos;
    private Vector2 settingsBasePos;
    private Vector2 tutorialPromptBasePos;
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
        EnsureMainMenuMusic();
        GlobalAudioSettings.ApplyLoadedSettings();
        SyncSlidersToGlobalAudio();
    }

    public void EnsureMainMenuMusic()
    {
        if (mainMenuMusicSource == null)
        {
            GameObject go = GameObject.Find("mainmenumusic");
            if (go != null)
            {
                mainMenuMusicSource = go.GetComponent<AudioSource>();
            }
        }

        if (mainMenuMusicSource != null)
        {
            mainMenuMusicSource.ignoreListenerVolume = true;
            mainMenuMusicSource.volume = GlobalAudioSettings.MusicVolume;
        }
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
        SwitchToTutorialPrompt(true);
    }

    public void OnPlayWithTutorialClicked()
    {
        if (isTransitioning) return;
        PlayClickSound();
        LaunchGameWithTutorialChoice(true);
    }

    public void OnPlayWithoutTutorialClicked()
    {
        if (isTransitioning) return;
        PlayClickSound();
        LaunchGameWithTutorialChoice(false);
    }

    public void OnBackFromTutorialPromptClicked()
    {
        if (isTransitioning) return;
        PlayClickSound();
        SwitchToTutorialPrompt(false);
    }

    private void LaunchGameWithTutorialChoice(bool withTutorial)
    {
        isTransitioning = true;

        // 1. Reset run statistics for a fresh game
        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.ResetRunForNewGame();
        }

        // 2. Record whether to play with or without the tutorial
        TutorialManager.SetMenuTutorialChoice(withTutorial);

        // 3. Load the main room scene cleanly (includeInactive=true because SceneLoaderFade sits on 'Story' inside MainButtonsPanel)
        if (sceneLoaderFade == null) sceneLoaderFade = FindObjectOfType<SceneLoaderFade>(true);
        if (sceneLoaderFade != null)
        {
            sceneLoaderFade.LoadSceneWithFade(mainSceneName);
        }
        else
        {
            SceneManager.LoadScene(mainSceneName);
        }
    }

    public void OnArcadeClicked()
    {
        if (isTransitioning) return;
        isTransitioning = true;
        PlayClickSound();

        if (sceneLoaderFade == null) sceneLoaderFade = FindObjectOfType<SceneLoaderFade>(true);
        if (sceneLoaderFade != null)
        {
            sceneLoaderFade.LoadSceneWithFade(arcadeSceneName);
        }
        else
        {
            SceneManager.LoadScene(arcadeSceneName);
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
        EnsureCanvasGroups();
        RecordBasePositions();
        transitionRoutine = StartCoroutine(TransitionPanelsRoutine(
            toSettings ? mainButtonsCanvasGroup : settingsCanvasGroup,
            toSettings ? settingsCanvasGroup : mainButtonsCanvasGroup,
            toSettings ? mainButtonsBasePos : settingsBasePos,
            toSettings ? settingsBasePos : mainButtonsBasePos,
            toSettings));
    }

    public void SwitchToTutorialPrompt(bool toPrompt)
    {
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
        }
        EnsureTutorialPromptPanel();
        EnsureCanvasGroups();
        RecordBasePositions();
        transitionRoutine = StartCoroutine(TransitionPanelsRoutine(
            toPrompt ? mainButtonsCanvasGroup : tutorialPromptCanvasGroup,
            toPrompt ? tutorialPromptCanvasGroup : mainButtonsCanvasGroup,
            toPrompt ? mainButtonsBasePos : tutorialPromptBasePos,
            toPrompt ? tutorialPromptBasePos : mainButtonsBasePos,
            toPrompt));
    }

    private IEnumerator TransitionPanelsRoutine(
        CanvasGroup outgoing,
        CanvasGroup incoming,
        Vector2 outBase,
        Vector2 inBase,
        bool forward)
    {
        if (outgoing == null || incoming == null) yield break;

        isTransitioning = true;
        activeDraggedSlider = null;

        if (incoming == settingsCanvasGroup)
        {
            SyncSlidersToGlobalAudio();
        }

        RectTransform outgoingRT = outgoing.GetComponent<RectTransform>();
        RectTransform incomingRT = incoming.GetComponent<RectTransform>();

        Vector2 outTargetPos = outBase + (forward ? new Vector2(-transitionHorizontalDrift, 0f) : new Vector2(transitionHorizontalDrift, 0f));
        Vector2 inStartPos = inBase + (forward ? new Vector2(transitionHorizontalDrift, 0f) : new Vector2(-transitionHorizontalDrift, 0f));

        incoming.gameObject.SetActive(true);
        incoming.alpha = 0f;
        incoming.interactable = false;
        incoming.blocksRaycasts = false;
        if (incomingRT != null) incomingRT.anchoredPosition = inStartPos;

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

            if (outgoingRT != null) outgoingRT.anchoredPosition = Vector2.Lerp(outBase, outTargetPos, smoothT);
            if (incomingRT != null) incomingRT.anchoredPosition = Vector2.Lerp(inStartPos, inBase, smoothT);

            yield return null;
        }

        outgoing.alpha = 0f;
        outgoing.gameObject.SetActive(false);
        if (outgoingRT != null) outgoingRT.anchoredPosition = outBase;

        incoming.alpha = 1f;
        if (incomingRT != null) incomingRT.anchoredPosition = inBase;
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

        if (tutorialPromptPanel != null && tutorialPromptCanvasGroup == null)
        {
            tutorialPromptCanvasGroup = tutorialPromptPanel.GetComponent<CanvasGroup>();
            if (tutorialPromptCanvasGroup == null) tutorialPromptCanvasGroup = tutorialPromptPanel.AddComponent<CanvasGroup>();
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

        if (tutorialPromptPanel != null)
        {
            RectTransform rt = tutorialPromptPanel.GetComponent<RectTransform>();
            if (rt != null) tutorialPromptBasePos = rt.anchoredPosition;
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
        if (mainMenuMusicSource != null)
        {
            mainMenuMusicSource.ignoreListenerVolume = true;
            mainMenuMusicSource.volume = clamped;
        }
        UpdateSliderLabels();
    }

    public void OnSfxSliderChanged(float value)
    {
        if (isUpdatingSlidersInternally) return;

        float clamped = Mathf.Clamp01(value);
        GlobalAudioSettings.SetSfxVolume(clamped);
        UpdateSliderLabels();
    }

    public void OnSensitivitySliderChanged(float value)
    {
        if (isUpdatingSlidersInternally) return;

        float clamped = Mathf.Clamp(value, GlobalAudioSettings.MinMouseSensitivity, GlobalAudioSettings.MaxMouseSensitivity);
        GlobalAudioSettings.SetMouseSensitivity(clamped);
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
            if (sensitivitySlider != null)
            {
                sensitivitySlider.minValue = GlobalAudioSettings.MinMouseSensitivity;
                sensitivitySlider.maxValue = GlobalAudioSettings.MaxMouseSensitivity;
                sensitivitySlider.SetValueWithoutNotify(GlobalAudioSettings.MouseSensitivity);
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
        if (sensitivityValueText != null && sensitivitySlider != null)
        {
            sensitivityValueText.text = Mathf.RoundToInt(sensitivitySlider.value * 100f) + "%";
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
            if (arcadeButton == null)
            {
                arcadeButton = FindButtonByNameOrIndex(buttonsContainer, "Arcade", "Arcade", -1);
            }
            if (settingsButton == null)
            {
                settingsButton = FindButtonByNameOrIndex(buttonsContainer, "Settings", "Settings", 2);
            }
            if (quitButton == null)
            {
                quitButton = FindButtonByNameOrIndex(buttonsContainer, "Quit", "Quit", 3);
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

            if (sensitivitySlider == null)
            {
                Slider[] sliders = settingsPanel.GetComponentsInChildren<Slider>(true);
                foreach (Slider s in sliders)
                {
                    if (s.name.IndexOf("Sens", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        s.name.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sensitivitySlider = s;
                        break;
                    }
                }
                if (sensitivitySlider == null && sliders.Length > 2) sensitivitySlider = sliders[2];
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

            if (sensitivityValueText == null)
            {
                TextMeshProUGUI[] tmps = settingsPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (TextMeshProUGUI t in tmps)
                {
                    if (t.name.IndexOf("SensValue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.name.IndexOf("SensitivityValue", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sensitivityValueText = t;
                        break;
                    }
                }
            }
        }

        EnsureTutorialPromptPanel();
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

        // Tutorial prompt panel starts hidden at runtime
        if (tutorialPromptPanel != null)
        {
            tutorialPromptPanel.SetActive(false);
            if (tutorialPromptCanvasGroup != null)
            {
                tutorialPromptCanvasGroup.alpha = 0f;
                tutorialPromptCanvasGroup.interactable = false;
                tutorialPromptCanvasGroup.blocksRaycasts = false;
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

    private void EnsureTutorialPromptPanel()
    {
        if (tutorialPromptPanel != null) return;
        if (mainButtonsPanel == null || mainButtonsPanel.transform.parent == null) return;

        Transform parent = mainButtonsPanel.transform.parent;
        Transform existing = parent.Find("TutorialPromptPanel");
        if (existing != null)
        {
            tutorialPromptPanel = existing.gameObject;
            playWithTutorialButton = FindButtonByNameOrIndex(existing, "WithTutorial", "Yes", 0);
            playWithoutTutorialButton = FindButtonByNameOrIndex(existing, "WithoutTutorial", "No", 1);
            backFromTutorialButton = FindButtonByNameOrIndex(existing, "Back", "Back", 2);
            return;
        }

        // Clone MainButtonsPanel layout so button styling, sprites, font, and scale match the Main Menu 1:1
        GameObject panelObj = new GameObject("TutorialPromptPanel", typeof(RectTransform), typeof(CanvasGroup));
        panelObj.layer = mainButtonsPanel.layer;
        panelObj.transform.SetParent(parent, false);

        RectTransform srcRT = mainButtonsPanel.GetComponent<RectTransform>();
        RectTransform dstRT = panelObj.GetComponent<RectTransform>();
        if (srcRT != null && dstRT != null)
        {
            dstRT.anchorMin = srcRT.anchorMin;
            dstRT.anchorMax = srcRT.anchorMax;
            dstRT.pivot = srcRT.pivot;
            dstRT.anchoredPosition = srcRT.anchoredPosition;
            dstRT.sizeDelta = srcRT.sizeDelta;
            dstRT.localScale = srcRT.localScale;
        }

        // Prompt Question Title above buttons
        GameObject titleObj = new GameObject("PromptTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleObj.layer = panelObj.layer;
        titleObj.transform.SetParent(panelObj.transform, false);
        RectTransform titleRT = titleObj.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.5f, 0.5f);
        titleRT.anchorMax = new Vector2(0.5f, 0.5f);
        titleRT.pivot = new Vector2(0.5f, 0.5f);
        titleRT.anchoredPosition = new Vector2(0f, 172f);
        titleRT.sizeDelta = new Vector2(540f, 46f);

        TextMeshProUGUI titleTmp = titleObj.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) titleTmp.font = fontAsset;
        titleTmp.text = "PLAY WITH TUTORIAL?";
        titleTmp.fontSize = 26f;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = new Color(0.70f, 0.98f, 0.78f, 1f);
        titleTmp.raycastTarget = false;

        Button templateBtn = startRunButton != null ? startRunButton : (settingsButton != null ? settingsButton : quitButton);
        if (templateBtn != null)
        {
            playWithTutorialButton = CloneMenuButton(templateBtn, panelObj.transform, "WithTutorialButton", "WITH TUTORIAL", new Vector2(0f, 88f));
            playWithoutTutorialButton = CloneMenuButton(templateBtn, panelObj.transform, "WithoutTutorialButton", "WITHOUT TUTORIAL", new Vector2(0f, -8f));
            backFromTutorialButton = CloneMenuButton(templateBtn, panelObj.transform, "BackFromTutorialButton", "BACK", new Vector2(0f, -104f));
        }

        tutorialPromptPanel = panelObj;
        tutorialPromptCanvasGroup = panelObj.GetComponent<CanvasGroup>();
        tutorialPromptBasePos = dstRT != null ? dstRT.anchoredPosition : Vector2.zero;
    }

    private Button CloneMenuButton(Button template, Transform parent, string name, string label, Vector2 anchoredPos)
    {
        GameObject clone = Instantiate(template.gameObject, parent);
        clone.name = name;
        clone.SetActive(true);

        // Remove any SceneLoaderFade or non-UI helper scripts copied from startRunButton
        SceneLoaderFade fadeComp = clone.GetComponent<SceneLoaderFade>();
        if (fadeComp != null) Destroy(fadeComp);

        RectTransform rt = clone.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchoredPosition = anchoredPos;
        }

        Button btn = clone.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick = new Button.ButtonClickedEvent();
        }

        TextMeshProUGUI tmp = clone.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            tmp.text = label;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 8f;
            tmp.fontSizeMax = 18f;
        }

        return btn;
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
        if (arcadeButton != null)
        {
            arcadeButton.onClick.RemoveListener(OnArcadeClicked);
            arcadeButton.onClick.AddListener(OnArcadeClicked);
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
        if (playWithTutorialButton != null)
        {
            playWithTutorialButton.onClick.RemoveListener(OnPlayWithTutorialClicked);
            playWithTutorialButton.onClick.AddListener(OnPlayWithTutorialClicked);
        }
        if (playWithoutTutorialButton != null)
        {
            playWithoutTutorialButton.onClick.RemoveListener(OnPlayWithoutTutorialClicked);
            playWithoutTutorialButton.onClick.AddListener(OnPlayWithoutTutorialClicked);
        }
        if (backFromTutorialButton != null)
        {
            backFromTutorialButton.onClick.RemoveListener(OnBackFromTutorialPromptClicked);
            backFromTutorialButton.onClick.AddListener(OnBackFromTutorialPromptClicked);
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
        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = GlobalAudioSettings.MinMouseSensitivity;
            sensitivitySlider.maxValue = GlobalAudioSettings.MaxMouseSensitivity;
            sensitivitySlider.onValueChanged.RemoveListener(OnSensitivitySliderChanged);
            sensitivitySlider.onValueChanged.AddListener(OnSensitivitySliderChanged);
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
        bool tutorialPromptActive = tutorialPromptPanel != null && tutorialPromptPanel.activeInHierarchy;
        bool mainActive = mainButtonsPanel != null && mainButtonsPanel.activeInHierarchy && !settingsActive && !tutorialPromptActive;

        bool leftPressedThisFrame = WasLeftMousePressedThisFrame();
        bool leftHeld = IsLeftMousePressed();

        if (tutorialPromptActive && leftPressedThisFrame)
        {
            if (IsScreenPointOverButton(playWithTutorialButton, mouseScreenPos, uiCam, 6f))
            {
                OnPlayWithTutorialClicked();
                return;
            }
            if (IsScreenPointOverButton(playWithoutTutorialButton, mouseScreenPos, uiCam, 6f))
            {
                OnPlayWithoutTutorialClicked();
                return;
            }
            if (IsScreenPointOverButton(backFromTutorialButton, mouseScreenPos, uiCam, 6f))
            {
                OnBackFromTutorialPromptClicked();
                return;
            }
        }
        else if (settingsActive)
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
                else if (IsScreenPointNearSlider(sensitivitySlider, mouseScreenPos, uiCam, 16f))
                {
                    activeDraggedSlider = sensitivitySlider;
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
            else if (IsScreenPointOverButton(arcadeButton, mouseScreenPos, uiCam, 6f))
            {
                OnArcadeClicked();
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
