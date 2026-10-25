#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Manual setup utility for Pause Manager - only executes when manually clicked via Tools menu.
public static class PauseManagerSceneSetup
{
    private const string GameplayMatPath = "Assets/Shaders/duringgameplay.mat";
    private const string PausedMatPath = "Assets/Shaders/duringpaused.mat";
    private const string MainRendererPath = "Assets/Shaders/mainRenderer.asset";
    private const string DefaultPausedSoundPath = "Assets/Audio/static.mp3";
    private const string ProjectFontGuid = "6d795282a2f39f94ab1e799f42073e3f";

    private static void UpgradeExistingPauseManagerInScene(PauseManager pauseMgr, DynamicMusicManager musicMgr, Scene activeScene)
    {
        bool modified = false;

        if (pauseMgr.pauseCanvas != null && pauseMgr.pauseCanvas.sortingOrder < 5000)
        {
            pauseMgr.pauseCanvas.sortingOrder = 5000;
            EditorUtility.SetDirty(pauseMgr.pauseCanvas);
            modified = true;
        }

        if (EnsureHudPauseButtonWiredInScene(pauseMgr))
        {
            modified = true;
        }

        if (pauseMgr.playerController == null || pauseMgr.musicManager == null || pauseMgr.crtRendererFeature == null)
        {
            AssignCoreReferences(pauseMgr, musicMgr);
            modified = true;
        }

        if (modified)
        {
            EditorUtility.SetDirty(pauseMgr);
            EditorSceneManager.MarkSceneDirty(activeScene);
        }
    }

    [MenuItem("Tools/Setup Pause Manager in Active Scene")]
    public static void SetupPauseManagerInActiveScene()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Please exit Play Mode before running Setup Pause Manager.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        bool wasDirtyBefore = activeScene.isDirty;

        // Normalize tracks on DynamicMusicManager if present
        DynamicMusicManager musicMgr = UnityEngine.Object.FindObjectOfType<DynamicMusicManager>(true);
        if (musicMgr != null)
        {
            Undo.RecordObject(musicMgr, "Normalize Music Tracks");
            musicMgr.RecalculateTrackNormalizations();
            EditorUtility.SetDirty(musicMgr);
        }

        PauseManager pauseMgr = UnityEngine.Object.FindObjectOfType<PauseManager>(true);
        if (pauseMgr != null)
        {
            Debug.Log("[PauseManagerSceneSetup] PauseManager already exists in the active scene. Refreshing references and wiring HUD pause button.");
            AssignCoreReferences(pauseMgr, musicMgr);
            if (pauseMgr.pauseCanvas != null && pauseMgr.pauseCanvas.sortingOrder < 5000)
            {
                pauseMgr.pauseCanvas.sortingOrder = 5000;
                EditorUtility.SetDirty(pauseMgr.pauseCanvas);
            }
            EnsureHudPauseButtonWiredInScene(pauseMgr);
            EditorUtility.SetDirty(pauseMgr);
            EditorSceneManager.MarkSceneDirty(activeScene);
            return;
        }

        // Load project assets
        TMP_FontAsset fontAsset = LoadProjectFont();
        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        Sprite backgroundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        Sprite knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        // 1. Create single root PauseManager GameObject
        GameObject rootObj = new GameObject("PauseManager");
        Undo.RegisterCreatedObjectUndo(rootObj, "Create PauseManager");

        AudioSource pauseAudio = rootObj.AddComponent<AudioSource>();
        pauseAudio.playOnAwake = false;
        pauseAudio.loop = true;
        pauseAudio.spatialBlend = 0f;
        pauseAudio.ignoreListenerPause = true;
        pauseAudio.ignoreListenerVolume = false;

        pauseMgr = rootObj.AddComponent<PauseManager>();
        pauseMgr.pausedAudioSource = pauseAudio;

        // 2. Create PauseCanvas under PauseManager
        GameObject canvasObj = new GameObject("PauseCanvas");
        canvasObj.layer = 5; // UI layer
        canvasObj.transform.SetParent(rootObj.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            // ScreenSpaceCamera on Main Camera so the URP FullScreenPassRendererFeature (duringpaused.mat) curves & scanlines the Pause Canvas too
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = mainCam;
            canvas.planeDistance = 0.08f;
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 3. Fullscreen Dim Background & Raycast Blocker (ensures only PauseCanvas is clickable)
        GameObject blockerObj = CreateUIObject("PauseBackground_RaycastBlocker", canvasObj.transform);
        StretchFullScreen(blockerObj.GetComponent<RectTransform>());
        Image blockerImg = blockerObj.AddComponent<Image>();
        blockerImg.color = new Color(0.02f, 0.02f, 0.03f, 0.78f);
        blockerImg.raycastTarget = true;

        // 4. Main Buttons Panel (Resume, Settings, End Run)
        GameObject mainPanel = CreateUIObject("PauseMainMenuPanel", canvasObj.transform);
        RectTransform mainPanelRT = mainPanel.GetComponent<RectTransform>();
        mainPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
        mainPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
        mainPanelRT.pivot = new Vector2(0.5f, 0.5f);
        mainPanelRT.anchoredPosition = Vector2.zero;
        mainPanelRT.sizeDelta = new Vector2(620f, 540f);

        Image mainPanelBg = mainPanel.AddComponent<Image>();
        mainPanelBg.sprite = backgroundSprite;
        mainPanelBg.type = Image.Type.Sliced;
        mainPanelBg.color = new Color(0.08f, 0.08f, 0.10f, 0.92f);

        CreateTMPText("PausedTitle", mainPanel.transform, "PAUSED", fontAsset, 54f, new Vector2(0f, 185f), new Vector2(500f, 80f), Color.white);

        Button resumeBtn = CreateMenuButton("ResumeButton", mainPanel.transform, "RESUME", fontAsset, uiSprite, new Vector2(0f, 65f), new Vector2(360f, 68f));
        Button settingsBtn = CreateMenuButton("SettingsButton", mainPanel.transform, "SETTINGS", fontAsset, uiSprite, new Vector2(0f, -25f), new Vector2(360f, 68f));
        Button endRunBtn = CreateMenuButton("EndRunButton", mainPanel.transform, "END RUN", fontAsset, uiSprite, new Vector2(0f, -115f), new Vector2(360f, 68f));

        // Subtle red tint on END RUN button
        Image endRunImg = endRunBtn.GetComponent<Image>();
        if (endRunImg != null)
        {
            endRunImg.color = new Color(0.85f, 0.25f, 0.25f, 1f);
        }

        // 5. Settings Panel (2 Audio Sliders: Music & Every Other Sound / SFX + Back Button)
        GameObject settingsPanel = CreateUIObject("SettingsPanel", canvasObj.transform);
        RectTransform settingsPanelRT = settingsPanel.GetComponent<RectTransform>();
        settingsPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
        settingsPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
        settingsPanelRT.pivot = new Vector2(0.5f, 0.5f);
        settingsPanelRT.anchoredPosition = Vector2.zero;
        settingsPanelRT.sizeDelta = new Vector2(660f, 540f);

        Image settingsPanelBg = settingsPanel.AddComponent<Image>();
        settingsPanelBg.sprite = backgroundSprite;
        settingsPanelBg.type = Image.Type.Sliced;
        settingsPanelBg.color = new Color(0.08f, 0.08f, 0.10f, 0.92f);

        CreateTMPText("SettingsTitle", settingsPanel.transform, "SETTINGS", fontAsset, 50f, new Vector2(0f, 190f), new Vector2(500f, 75f), Color.white);

        CreateTMPText("MusicLabel", settingsPanel.transform, "MUSIC", fontAsset, 30f, new Vector2(0f, 100f), new Vector2(460f, 45f), Color.white);
        Slider musicSlider = CreateMenuSlider("MusicVolumeSlider", settingsPanel.transform, backgroundSprite, uiSprite, knobSprite, new Vector2(0f, 50f), new Vector2(440f, 36f));

        CreateTMPText("SfxLabel", settingsPanel.transform, "SFX / OTHER SOUNDS", fontAsset, 30f, new Vector2(0f, -25f), new Vector2(460f, 45f), Color.white);
        Slider sfxSlider = CreateMenuSlider("SfxVolumeSlider", settingsPanel.transform, backgroundSprite, uiSprite, knobSprite, new Vector2(0f, -75f), new Vector2(440f, 36f));

        Button backBtn = CreateMenuButton("BackButton", settingsPanel.transform, "BACK", fontAsset, uiSprite, new Vector2(0f, -180f), new Vector2(280f, 62f));

        // 6. Wire UI references & persistent UnityEvents on PauseManager
        pauseMgr.pauseCanvas = canvas;
        pauseMgr.mainButtonsPanel = mainPanel;
        pauseMgr.settingsPanel = settingsPanel;
        pauseMgr.resumeButton = resumeBtn;
        pauseMgr.settingsButton = settingsBtn;
        pauseMgr.endRunButton = endRunBtn;
        pauseMgr.backFromSettingsButton = backBtn;
        pauseMgr.musicVolumeSlider = musicSlider;
        pauseMgr.sfxVolumeSlider = sfxSlider;

        UnityEventTools.AddVoidPersistentListener(resumeBtn.onClick, pauseMgr.ResumeGame);
        UnityEventTools.AddVoidPersistentListener(settingsBtn.onClick, pauseMgr.OpenSettings);
        UnityEventTools.AddVoidPersistentListener(endRunBtn.onClick, pauseMgr.EndRun);
        UnityEventTools.AddVoidPersistentListener(backBtn.onClick, pauseMgr.CloseSettings);
        UnityEventTools.AddPersistentListener(musicSlider.onValueChanged, pauseMgr.OnMusicVolumeChanged);
        UnityEventTools.AddPersistentListener(sfxSlider.onValueChanged, pauseMgr.OnSfxVolumeChanged);

        // Start with SettingsPanel & PauseCanvas inactive so gameplay view is unobstructed until ESC is pressed
        settingsPanel.SetActive(false);
        mainPanel.SetActive(true);
        canvasObj.SetActive(false);

        AssignCoreReferences(pauseMgr, musicMgr);
        EnsureHudPauseButtonWiredInScene(pauseMgr);

        EditorUtility.SetDirty(rootObj);
        EditorUtility.SetDirty(pauseMgr);
        EditorSceneManager.MarkSceneDirty(activeScene);

        if (!wasDirtyBefore && !string.IsNullOrEmpty(activeScene.path))
        {
            EditorSceneManager.SaveScene(activeScene);
        }

        Debug.Log("[PauseManagerSceneSetup] Created PauseManager and PauseCanvas hierarchy in scene '" + activeScene.name + "'.");
    }

    private static bool EnsureHudPauseButtonWiredInScene(PauseManager pauseMgr)
    {
        if (pauseMgr == null) return false;

        bool changed = false;

        if (pauseMgr.playerController == null)
        {
            pauseMgr.playerController = UnityEngine.Object.FindObjectOfType<GridCrawlerController>(true);
        }

        Transform pauseImgTransform = FindHudPauseTransformInScene(pauseMgr);
        if (pauseImgTransform != null)
        {
            PauseButtonUI btnUI = pauseImgTransform.GetComponent<PauseButtonUI>();
            if (btnUI == null)
            {
                btnUI = Undo.AddComponent<PauseButtonUI>(pauseImgTransform.gameObject);
                changed = true;
                Debug.Log("[PauseManagerSceneSetup] Attached PauseButtonUI to '" + pauseImgTransform.name + "' under croper.");
            }

            if (btnUI.controller != pauseMgr.playerController)
            {
                btnUI.controller = pauseMgr.playerController;
                EditorUtility.SetDirty(btnUI);
                changed = true;
            }

            if (btnUI.pauseManager != pauseMgr)
            {
                btnUI.pauseManager = pauseMgr;
                EditorUtility.SetDirty(btnUI);
                changed = true;
            }

            if (pauseMgr.hudPauseButton != btnUI)
            {
                pauseMgr.hudPauseButton = btnUI;
                EditorUtility.SetDirty(pauseMgr);
                changed = true;
            }
        }

        return changed;
    }

    private static Transform FindHudPauseTransformInScene(PauseManager pauseMgr)
    {
        if (pauseMgr.playerController != null && pauseMgr.playerController.cropPanel != null)
        {
            Transform found = FindPauseChildRecursive(pauseMgr.playerController.cropPanel, pauseMgr.pauseCanvas);
            if (found != null) return found;
        }

        RectTransform[] allRects = UnityEngine.Object.FindObjectsOfType<RectTransform>(true);
        for (int i = 0; i < allRects.Length; i++)
        {
            RectTransform rt = allRects[i];
            if (rt == null) continue;
            if (pauseMgr.pauseCanvas != null && rt.IsChildOf(pauseMgr.pauseCanvas.transform)) continue;

            if (string.Equals(rt.name.Trim(), "croper", StringComparison.OrdinalIgnoreCase))
            {
                Transform found = FindPauseChildRecursive(rt, pauseMgr.pauseCanvas);
                if (found != null) return found;
            }
        }

        for (int i = 0; i < allRects.Length; i++)
        {
            RectTransform rt = allRects[i];
            if (rt == null) continue;
            if (pauseMgr.pauseCanvas != null && rt.IsChildOf(pauseMgr.pauseCanvas.transform)) continue;
            if (rt.gameObject == pauseMgr.gameObject) continue;

            if (string.Equals(rt.name.Trim(), "pause", StringComparison.OrdinalIgnoreCase) && rt.GetComponent<Image>() != null)
            {
                return rt;
            }
        }

        return null;
    }

    private static Transform FindPauseChildRecursive(Transform parent, Canvas pauseCanvas)
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

            Transform nested = FindPauseChildRecursive(child, pauseCanvas);
            if (nested != null) return nested;
        }

        return null;
    }

    private static void AssignCoreReferences(PauseManager pauseMgr, DynamicMusicManager musicMgr)
    {
        if (pauseMgr == null) return;

        if (musicMgr == null) musicMgr = UnityEngine.Object.FindObjectOfType<DynamicMusicManager>(true);
        pauseMgr.musicManager = musicMgr;
        pauseMgr.barManager = UnityEngine.Object.FindObjectOfType<BarManager>(true);
        pauseMgr.depressionBreakdown = UnityEngine.Object.FindObjectOfType<DepressionBreakdownSequence>(true);
        pauseMgr.playerController = UnityEngine.Object.FindObjectOfType<GridCrawlerController>(true);
        pauseMgr.cameraFollow = UnityEngine.Object.FindObjectOfType<SmoothCameraFollow>(true);

        pauseMgr.duringGameplayMaterial = AssetDatabase.LoadAssetAtPath<Material>(GameplayMatPath);
        pauseMgr.duringPausedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PausedMatPath);
        pauseMgr.rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(MainRendererPath);

        if (pauseMgr.rendererData != null && pauseMgr.rendererData.rendererFeatures != null)
        {
            foreach (ScriptableRendererFeature feature in pauseMgr.rendererData.rendererFeatures)
            {
                if (feature is FullScreenPassRendererFeature fsp)
                {
                    if (fsp.passMaterial == pauseMgr.duringGameplayMaterial
                        || fsp.passMaterial == pauseMgr.duringPausedMaterial
                        || fsp.name.Contains("FullScreenPassRendererFeatureOldGUID"))
                    {
                        pauseMgr.crtRendererFeature = fsp;
                        break;
                    }
                }
            }
        }

        if (pauseMgr.pausedSound == null)
        {
            pauseMgr.pausedSound = AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultPausedSoundPath);
        }

        if (pauseMgr.pausedAudioSource != null && pauseMgr.pausedSound != null)
        {
            pauseMgr.pausedAudioSource.clip = pauseMgr.pausedSound;
        }
    }

    private static TMP_FontAsset LoadProjectFont()
    {
        string path = AssetDatabase.GUIDToAssetPath(ProjectFontGuid);
        if (!string.IsNullOrEmpty(path))
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null) return font;
        }
        return TMP_Settings.defaultFontAsset;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFullScreen(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateTMPText(
        string name,
        Transform parent,
        string content,
        TMP_FontAsset font,
        float fontSize,
        Vector2 anchoredPos,
        Vector2 sizeDelta,
        Color color)
    {
        GameObject textObj = CreateUIObject(name, parent);
        RectTransform rt = textObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = content;
        if (font != null) tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateMenuButton(
        string name,
        Transform parent,
        string label,
        TMP_FontAsset font,
        Sprite buttonSprite,
        Vector2 anchoredPos,
        Vector2 sizeDelta)
    {
        GameObject btnObj = CreateUIObject(name, parent);
        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        Image img = btnObj.AddComponent<Image>();
        img.sprite = buttonSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.92f, 0.92f, 0.92f, 1f);

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.85f, 0.95f, 1f, 1f);
        colors.pressedColor = new Color(0.70f, 0.70f, 0.70f, 1f);
        btn.colors = colors;

        GameObject txtObj = CreateUIObject("Text (TMP)", btnObj.transform);
        StretchFullScreen(txtObj.GetComponent<RectTransform>());
        TextMeshProUGUI tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        if (font != null) tmp.font = font;
        tmp.fontSize = 32f;
        tmp.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return btn;
    }

    private static Slider CreateMenuSlider(
        string name,
        Transform parent,
        Sprite bgSprite,
        Sprite fillSprite,
        Sprite knobSprite,
        Vector2 anchoredPos,
        Vector2 sizeDelta)
    {
        GameObject sliderObj = CreateUIObject(name, parent);
        RectTransform sliderRT = sliderObj.GetComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRT.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = anchoredPos;
        sliderRT.sizeDelta = sizeDelta;

        // Background
        GameObject bgObj = CreateUIObject("Background", sliderObj.transform);
        RectTransform bgRT = bgObj.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.25f);
        bgRT.anchorMax = new Vector2(1f, 0.75f);
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = bgSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(0.22f, 0.22f, 0.25f, 1f);

        // Fill Area
        GameObject fillAreaObj = CreateUIObject("Fill Area", sliderObj.transform);
        RectTransform fillAreaRT = fillAreaObj.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRT.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRT.offsetMin = new Vector2(5f, 0f);
        fillAreaRT.offsetMax = new Vector2(-15f, 0f);

        GameObject fillObj = CreateUIObject("Fill", fillAreaObj.transform);
        RectTransform fillRT = fillObj.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = new Vector2(10f, 0f);
        Image fillImg = fillObj.AddComponent<Image>();
        fillImg.sprite = fillSprite;
        fillImg.type = Image.Type.Sliced;
        fillImg.color = new Color(0.85f, 0.85f, 0.85f, 1f);

        // Handle Slide Area
        GameObject handleAreaObj = CreateUIObject("Handle Slide Area", sliderObj.transform);
        RectTransform handleAreaRT = handleAreaObj.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = Vector2.zero;
        handleAreaRT.anchorMax = Vector2.one;
        handleAreaRT.offsetMin = new Vector2(10f, 0f);
        handleAreaRT.offsetMax = new Vector2(-10f, 0f);

        GameObject handleObj = CreateUIObject("Handle", handleAreaObj.transform);
        RectTransform handleRT = handleObj.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0f, 0f);
        handleRT.anchorMax = new Vector2(0f, 1f);
        handleRT.sizeDelta = new Vector2(28f, 0f);
        Image handleImg = handleObj.AddComponent<Image>();
        handleImg.sprite = knobSprite;
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
#endif
