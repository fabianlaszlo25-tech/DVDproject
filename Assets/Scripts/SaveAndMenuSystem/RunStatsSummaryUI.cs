using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Animated Run Data Summary Canvas used in:
/// 1. DeathSeq/Canvas (Mode = DeathToMenu, bottom button = "GO TO MENU")
/// 2. KristofDreamGames Canvas (Mode = DreamWakeUp, bottom button = "WAKE UP")
///
/// Sequentially writes out all 8 run statistics with a typewriter + rolling counter + impact pulse animation,
/// and reveals the action button underneath once all data has been written down.
/// </summary>
public class RunStatsSummaryUI : MonoBehaviour
{
    public enum SummaryMode
    {
        DeathToMenu = 0,
        DreamWakeUp = 1
    }

    [Serializable]
    public class StatRowReferences
    {
        public string statKey;
        public RectTransform rowRoot;
        public CanvasGroup rowCanvasGroup;
        public TMP_Text labelText;
        public TMP_Text valueText;
        public Image rowHighlightBar;
    }

    [Header("Mode & Scene Targets")]
    public SummaryMode mode = SummaryMode.DeathToMenu;
    public string menuSceneName = "06Menu";
    public string roomSceneName = "01Day";

    [Header("UI References (Auto-Built if Missing)")]
    public Canvas targetCanvas;
    public CanvasGroup rootCanvasGroup;
    public RectTransform cardRect;
    public TMP_Text headerTitleText;
    public TMP_Text headerSubtitleText;
    public RectTransform dividerLineRect;
    public List<StatRowReferences> statRows = new List<StatRowReferences>();
    public Button actionButton;
    public CanvasGroup actionButtonCanvasGroup;
    public RectTransform actionButtonRect;
    public TMP_Text actionButtonText;
    public Image screenFadeOverlay;
    public TMP_FontAsset customFont;

    [Header("Animation Timings (Unscaled Seconds)")]
    public float initialFadeInDuration = 0.35f;
    public float characterTypeInterval = 0.014f;
    public float numberCountDuration = 0.32f;
    public float finalScoreCountDuration = 0.65f;
    public float delayBetweenRows = 0.08f;
    public float buttonRevealDuration = 0.35f;

    [Header("Audio Feedback (Procedural CRT Ticks if Clips Left Empty)")]
    public AudioSource uiAudioSource;
    public AudioClip typeTickClip;
    public AudioClip rowLockClip;
    public AudioClip scoreFinishClip;
    public AudioClip buttonClickClip;
    [Range(0f, 1f)] public float sfxVolume = 0.45f;

    [Header("CRT Post-Processing")]
    [Tooltip("If true, switches CRT to duringpaused.mat. If false, uses duringgameplay.mat for the stats summary.")]
    public bool usePausedCrt = false;

    private Coroutine animationRoutine;
    private bool isButtonInteractive;
    private bool hasTriggeredAction;
    private float speedMultiplier = 1f;
    private int lastTickSampleFrame = -10;

    private void Awake()
    {
        if (targetCanvas == null) targetCanvas = GetComponent<Canvas>();
        if (targetCanvas == null) targetCanvas = GetComponentInParent<Canvas>(true);

        EnsureProceduralAudioClips();
        EnsureUIBound();
        BindButtonEvents();
    }

    private void OnEnable()
    {
        hasTriggeredAction = false;
        isButtonInteractive = false;
        speedMultiplier = 1f;

        EnsureProceduralAudioClips();
        EnsureUIBound();
        ConfigureCanvasAndCursor();

        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
        }
        animationRoutine = StartCoroutine(PlaySummarySequenceRoutine());
    }

    private void OnDisable()
    {
        CrtPostProcessManager.SetPausedCrtActive(false);
        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }
    }

    private void OnDestroy()
    {
        CrtPostProcessManager.SetPausedCrtActive(false);
    }

    private void Update()
    {
        // Keep cursor unlocked & visible while summary screen is active
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;

        EnsureEventSystemActive();

        // Allow holding or clicking left mouse / Space / Enter to fast-forward the writing animation
        if (!isButtonInteractive)
        {
            if (WasFastForwardPressed())
            {
                speedMultiplier = 3.5f;
            }
            return;
        }

        // Direct regular-cursor hover & click support for 100% reliability in Editor and WebGL
        HandleDirectButtonInteraction();
    }

    private bool WasFastForwardPressed()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
        if (Keyboard.current != null &&
            (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame))
            return true;
        return false;
    }

    // =========================================================================
    // SEQUENTIAL DATA WRITING ANIMATION
    // =========================================================================

    private struct RunMetricDisplayData
    {
        public string label;
        public bool isCurrency;
        public float numericValue;
        public bool isFinalScore;
        public Color valueColor;
    }

    private IEnumerator PlaySummarySequenceRoutine()
    {
        // Gather current run data from RunSaveManager
        RunSaveManager saveMgr = RunSaveManager.Instance;
        float moneyEarned = saveMgr != null ? (float)saveMgr.TotalMoneyEarned : 0f;
        int days = saveMgr != null ? saveMgr.daysSurvived : 0;
        int substances = saveMgr != null ? saveMgr.substancesConsumed : 0;
        float gamblingWon = saveMgr != null ? (float)saveMgr.MoneyEarnedByGambling : 0f;
        float gamblingLost = saveMgr != null ? (float)saveMgr.MoneyLostByGambling : 0f;
        int trash = saveMgr != null ? saveMgr.trashCleaned : 0;
        int story = saveMgr != null ? saveMgr.storyElementsEncountered : 0;
        int finalScore = saveMgr != null ? saveMgr.CalculateFinalScore() : 0;

        RunMetricDisplayData[] metrics = new RunMetricDisplayData[]
        {
            new RunMetricDisplayData
            {
                label = "MONEY EARNED",
                isCurrency = true,
                numericValue = moneyEarned,
                isFinalScore = false,
                valueColor = new Color(0.55f, 0.95f, 0.62f, 1f)
            },
            new RunMetricDisplayData
            {
                label = "DAYS SURVIVED",
                isCurrency = false,
                numericValue = days,
                isFinalScore = false,
                valueColor = new Color(0.92f, 0.90f, 0.84f, 1f)
            },
            new RunMetricDisplayData
            {
                label = "SUBSTANCES CONSUMED",
                isCurrency = false,
                numericValue = substances,
                isFinalScore = false,
                valueColor = new Color(0.75f, 0.86f, 0.98f, 1f)
            },
            new RunMetricDisplayData
            {
                label = "MONEY EARNED BY GAMBLING",
                isCurrency = true,
                numericValue = gamblingWon,
                isFinalScore = false,
                valueColor = new Color(0.55f, 0.95f, 0.62f, 1f)
            },
            new RunMetricDisplayData
            {
                label = "MONEY LOST BY GAMBLING",
                isCurrency = true,
                numericValue = gamblingLost,
                isFinalScore = false,
                valueColor = new Color(0.96f, 0.48f, 0.45f, 1f)
            },
            new RunMetricDisplayData
            {
                label = "TRASH CLEANED",
                isCurrency = false,
                numericValue = trash,
                isFinalScore = false,
                valueColor = new Color(0.92f, 0.90f, 0.84f, 1f)
            },
            new RunMetricDisplayData
            {
                label = "STORY ELEMENTS ENCOUNTERED",
                isCurrency = false,
                numericValue = story,
                isFinalScore = false,
                valueColor = new Color(0.88f, 0.76f, 0.98f, 1f)
            },
            new RunMetricDisplayData
            {
                label = "FINAL SCORE",
                isCurrency = false,
                numericValue = finalScore,
                isFinalScore = true,
                valueColor = new Color(1.0f, 0.84f, 0.28f, 1f)
            }
        };

        // Configure header & button text for current mode
        ApplyModeTitles();

        // Reset initial visual state of rows, divider, and button
        if (rootCanvasGroup != null) rootCanvasGroup.alpha = 0f;
        if (cardRect != null) cardRect.localScale = new Vector3(0.96f, 0.96f, 1f);
        if (dividerLineRect != null) dividerLineRect.localScale = new Vector3(0f, 1f, 1f);

        for (int i = 0; i < statRows.Count; i++)
        {
            StatRowReferences row = statRows[i];
            if (row == null) continue;
            if (row.rowCanvasGroup != null) row.rowCanvasGroup.alpha = 0f;
            if (row.labelText != null) row.labelText.text = string.Empty;
            if (row.valueText != null) row.valueText.text = string.Empty;
            if (row.rowRoot != null) row.rowRoot.localScale = Vector3.one;
            // Keep rowHighlightBar color and alpha preserved from the scene design!
        }

        if (actionButton != null)
        {
            actionButton.interactable = false;
            actionButton.gameObject.SetActive(false);
        }
        if (actionButtonCanvasGroup != null)
        {
            actionButtonCanvasGroup.alpha = 0f;
        }
        if (screenFadeOverlay != null)
        {
            screenFadeOverlay.gameObject.SetActive(false);
        }

        // 1. Fade in card & header
        float elapsed = 0f;
        float fadeDur = Mathf.Max(0.05f, initialFadeInDuration);
        while (elapsed < fadeDur)
        {
            elapsed += GetStepDeltaTime();
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeDur));
            if (rootCanvasGroup != null) rootCanvasGroup.alpha = t;
            if (cardRect != null) cardRect.localScale = Vector3.Lerp(new Vector3(0.96f, 0.96f, 1f), Vector3.one, t);
            yield return null;
        }
        if (rootCanvasGroup != null) rootCanvasGroup.alpha = 1f;
        if (cardRect != null) cardRect.localScale = Vector3.one;

        // 2. Write out each of the 8 data rows sequentially
        int count = Mathf.Min(metrics.Length, statRows.Count);
        for (int i = 0; i < count; i++)
        {
            RunMetricDisplayData data = metrics[i];
            StatRowReferences row = statRows[i];
            if (row == null) continue;

            // Expand divider line right before FINAL SCORE
            if (data.isFinalScore && dividerLineRect != null)
            {
                float divElapsed = 0f;
                const float divDur = 0.22f;
                while (divElapsed < divDur)
                {
                    divElapsed += GetStepDeltaTime();
                    float dtNorm = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(divElapsed / divDur));
                    dividerLineRect.localScale = new Vector3(dtNorm, 1f, 1f);
                    yield return null;
                }
                dividerLineRect.localScale = Vector3.one;
            }

            if (row.rowCanvasGroup != null) row.rowCanvasGroup.alpha = 1f;
            // Value text color preserved from scene hierarchy rather than clobbering

            // 2a. Typewriter label animation
            string fullLabel = data.label;
            if (row.labelText != null)
            {
                float charWait = Mathf.Max(0.004f, characterTypeInterval);
                for (int c = 1; c <= fullLabel.Length; c++)
                {
                    row.labelText.text = fullLabel.Substring(0, c);
                    if (c % 2 == 1) PlayTickSound(0.95f + (c * 0.008f));
                    yield return WaitScaledUnscaled(charWait);
                }
                row.labelText.text = fullLabel;
            }

            // 2b. Rolling number counter animation
            float countDur = data.isFinalScore
                ? Mathf.Max(0.15f, finalScoreCountDuration)
                : Mathf.Max(0.10f, numberCountDuration);

            float rollElapsed = 0f;
            int lastDisplayedInt = -1;
            while (rollElapsed < countDur)
            {
                rollElapsed += GetStepDeltaTime();
                float t = Mathf.Clamp01(rollElapsed / countDur);
                float eased = 1f - Mathf.Pow(1f - t, 3f); // cubic ease-out

                float currentVal = Mathf.Lerp(0f, data.numericValue, eased);
                if (row.valueText != null)
                {
                    row.valueText.text = FormatMetricValue(currentVal, data.isCurrency);
                }

                int stepInt = Mathf.RoundToInt(t * 10f);
                if (stepInt != lastDisplayedInt && data.numericValue > 0.001f)
                {
                    lastDisplayedInt = stepInt;
                    PlayTickSound(Mathf.Lerp(0.85f, 1.35f, t));
                }

                yield return null;
            }

            if (row.valueText != null)
            {
                row.valueText.text = FormatMetricValue(data.numericValue, data.isCurrency);
            }

            // 2c. Impact lock-in pulse & brightness flash
            PlaySoundClip(data.isFinalScore ? scoreFinishClip : rowLockClip, data.isFinalScore ? 0.85f : 0.55f, 1f);

            // Cache original configured colors before the flash
            Color baseBarColor = row.rowHighlightBar != null ? row.rowHighlightBar.color : Color.clear;
            Color baseValColor = row.valueText != null ? row.valueText.color : Color.white;
            Color baseLblColor = row.labelText != null ? row.labelText.color : Color.white;

            // Compute gentle bright flash colors (tasteful, not overpowering)
            Color flashBarColor;
            if (baseBarColor.a > 0.4f)
            {
                // For opaque/colored bars (e.g. dark red in death screen)
                flashBarColor = new Color(
                    Mathf.Clamp01(baseBarColor.r * 1.45f + 0.10f),
                    Mathf.Clamp01(baseBarColor.g * 1.45f + 0.10f),
                    Mathf.Clamp01(baseBarColor.b * 1.45f + 0.10f),
                    baseBarColor.a
                );
            }
            else
            {
                // For subtle translucent bars (e.g. dream sequence where alpha is ~0.03-0.07)
                flashBarColor = new Color(
                    Mathf.Clamp01(baseBarColor.r + 0.12f),
                    Mathf.Clamp01(baseBarColor.g + 0.12f),
                    Mathf.Clamp01(baseBarColor.b + 0.12f),
                    data.isFinalScore ? 0.18f : 0.11f
                );
            }

            Color flashValColor = Color.Lerp(baseValColor, Color.white, 0.40f);
            Color flashLblColor = Color.Lerp(baseLblColor, Color.white, 0.28f);

            float pulseDur = data.isFinalScore ? 0.22f : 0.14f;
            float pulsePeak = data.isFinalScore ? 1.08f : 1.035f;
            float pulseElapsed = 0f;
            while (pulseElapsed < pulseDur)
            {
                pulseElapsed += GetStepDeltaTime();
                float pt = Mathf.Clamp01(pulseElapsed / pulseDur);
                float scale = Mathf.Lerp(pulsePeak, 1f, pt);
                if (row.rowRoot != null) row.rowRoot.localScale = new Vector3(scale, scale, 1f);

                // Brightness flash relaxes smoothly back to original configured colors
                if (row.rowHighlightBar != null)
                {
                    row.rowHighlightBar.color = Color.Lerp(flashBarColor, baseBarColor, pt);
                }
                if (row.valueText != null)
                {
                    row.valueText.color = Color.Lerp(flashValColor, baseValColor, pt);
                }
                if (row.labelText != null)
                {
                    row.labelText.color = Color.Lerp(flashLblColor, baseLblColor, pt);
                }

                yield return null;
            }

            // Guarantee exact original colors and scale are restored
            if (row.rowRoot != null) row.rowRoot.localScale = Vector3.one;
            if (row.rowHighlightBar != null) row.rowHighlightBar.color = baseBarColor;
            if (row.valueText != null) row.valueText.color = baseValColor;
            if (row.labelText != null) row.labelText.color = baseLblColor;

            yield return WaitScaledUnscaled(delayBetweenRows);
        }

        // 3. Once ALL data is written down, reveal the action button underneath!
        if (actionButton != null)
        {
            actionButton.gameObject.SetActive(true);
            actionButton.interactable = true;
        }

        float btnElapsed = 0f;
        float btnDur = Mathf.Max(0.1f, buttonRevealDuration);
        while (btnElapsed < btnDur)
        {
            btnElapsed += Time.unscaledDeltaTime;
            float bt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(btnElapsed / btnDur));
            if (actionButtonCanvasGroup != null) actionButtonCanvasGroup.alpha = bt;
            if (actionButtonRect != null)
            {
                float s = Mathf.Lerp(0.88f, 1f, bt);
                actionButtonRect.localScale = new Vector3(s, s, 1f);
            }
            yield return null;
        }

        if (actionButtonCanvasGroup != null) actionButtonCanvasGroup.alpha = 1f;
        if (actionButtonRect != null) actionButtonRect.localScale = Vector3.one;

        isButtonInteractive = true;
        animationRoutine = null;
    }

    private string FormatMetricValue(float value, bool isCurrency)
    {
        if (isCurrency)
        {
            return $"${value:0.00}";
        }
        return Mathf.RoundToInt(value).ToString("N0");
    }

    private float GetStepDeltaTime()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f || dt > 0.25f || float.IsNaN(dt)) dt = 0.016f;
        return dt * speedMultiplier;
    }

    private IEnumerator WaitScaledUnscaled(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += GetStepDeltaTime();
            yield return null;
        }
    }

    // =========================================================================
    // BUTTON ACTION HANDLERS (GO TO MENU / WAKE UP)
    // =========================================================================

    public void OnActionButtonClicked()
    {
        if (!isButtonInteractive || hasTriggeredAction) return;
        hasTriggeredAction = true;

        PlaySoundClip(buttonClickClip, 0.85f, 1f);
        StartCoroutine(ExecuteTransitionActionRoutine());
    }

    private IEnumerator ExecuteTransitionActionRoutine()
    {
        if (actionButton != null) actionButton.interactable = false;

        if (screenFadeOverlay != null)
        {
            screenFadeOverlay.gameObject.SetActive(true);
            screenFadeOverlay.raycastTarget = true;
            Color c = Color.black;
            c.a = 0f;
            screenFadeOverlay.color = c;

            float fadeElapsed = 0f;
            const float fadeDur = 0.4f;
            while (fadeElapsed < fadeDur)
            {
                fadeElapsed += Time.unscaledDeltaTime;
                c.a = Mathf.Clamp01(fadeElapsed / fadeDur);
                screenFadeOverlay.color = c;
                yield return null;
            }
            c.a = 1f;
            screenFadeOverlay.color = c;
        }
        else
        {
            yield return new WaitForSecondsRealtime(0.15f);
        }

        CrtPostProcessManager.SetPausedCrtActive(false);
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (mode == SummaryMode.DreamWakeUp)
        {
            if (RunSaveManager.HasInstance)
            {
                RunSaveManager.Instance.WakeUpFromDreamAndReturnToRoom();
            }
            else
            {
                SceneManager.LoadScene(roomSceneName);
            }
        }
        else
        {
            if (RunSaveManager.HasInstance)
            {
                RunSaveManager.Instance.ReturnToMainMenuFromDeath();
            }
            else
            {
                SceneManager.LoadScene(menuSceneName);
            }
        }
    }

    // =========================================================================
    // DIRECT REGULAR-CURSOR INTERACTION & HOVER FEEDBACK
    // =========================================================================

    private void HandleDirectButtonInteraction()
    {
        if (actionButton == null || !actionButton.gameObject.activeInHierarchy || hasTriggeredAction) return;
        if (actionButtonRect == null) actionButtonRect = actionButton.GetComponent<RectTransform>();
        if (actionButtonRect == null) return;

        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Camera uiCam = GetCanvasEventCamera();

        bool hovered = IsScreenPointOverRect(actionButtonRect, mousePos, uiCam, 8f);
        Vector3 targetScale = hovered ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;
        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);
        actionButtonRect.localScale = Vector3.Lerp(actionButtonRect.localScale, targetScale, dt * 14f);

        bool leftClicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        if (hovered && leftClicked)
        {
            OnActionButtonClicked();
        }
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

    private Camera GetCanvasEventCamera()
    {
        if (targetCanvas == null) return null;
        if (targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return targetCanvas.worldCamera != null ? targetCanvas.worldCamera : Camera.main;
    }

    // =========================================================================
    // UI CONSTRUCTION & BINDING (EDITOR & RUNTIME)
    // =========================================================================

    public void ApplyModeTitles()
    {
        if (headerTitleText != null)
        {
            headerTitleText.text = mode == SummaryMode.DreamWakeUp
                ? "DREAM SEQUENCE"
                : "RUN OVER!";
        }

        if (headerSubtitleText != null)
        {
            headerSubtitleText.text = mode == SummaryMode.DreamWakeUp
                ? "STATS SO FAR"
                : "FINAL STATS REPORT";
        }

        if (actionButtonText != null)
        {
            actionButtonText.text = mode == SummaryMode.DreamWakeUp
                ? "WAKE UP"
                : "GO TO MENU";
        }
    }

    private void BindButtonEvents()
    {
        if (actionButton != null)
        {
            actionButton.onClick.RemoveListener(OnActionButtonClicked);
            actionButton.onClick.AddListener(OnActionButtonClicked);
        }
    }

    private void ConfigureCanvasAndCursor()
    {
        if (targetCanvas == null) targetCanvas = GetComponent<Canvas>();
        if (targetCanvas == null) targetCanvas = GetComponentInParent<Canvas>(true);

        if (targetCanvas != null)
        {
            Camera cam = targetCanvas.worldCamera != null && targetCanvas.worldCamera.gameObject.activeInHierarchy
                ? targetCanvas.worldCamera
                : (Camera.main != null ? Camera.main : FindObjectOfType<Camera>(false));

            if (cam != null)
            {
                targetCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                targetCanvas.worldCamera = cam;
                targetCanvas.planeDistance = Mathf.Max(0.5f, cam.nearClipPlane + 0.25f);
                targetCanvas.sortingOrder = 32767;

                int canvasLayer = targetCanvas.gameObject.layer;
                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer < 0) uiLayer = 5;
                cam.cullingMask |= (1 << canvasLayer) | (1 << uiLayer);

                UniversalAdditionalCameraData camData = cam.GetComponent<UniversalAdditionalCameraData>();
                if (camData == null)
                {
                    camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }
                if (camData != null)
                {
                    camData.renderPostProcessing = true;
                }
            }
            else
            {
                targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                targetCanvas.sortingOrder = 32767;
            }

            targetCanvas.transform.localScale = Vector3.one;

            EnsureHierarchyValid(targetCanvas.transform, targetCanvas.gameObject.layer);

            GraphicRaycaster raycaster = targetCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                raycaster = targetCanvas.gameObject.AddComponent<GraphicRaycaster>();
            }
            raycaster.enabled = true;
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
        }

        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.transform.localScale = Vector3.one;
            rootCanvasGroup.alpha = 1f;
        }

        if (cardRect != null)
        {
            cardRect.localScale = Vector3.one;
        }

        CrtPostProcessManager.SetPausedCrtActive(usePausedCrt);

        EnsureEventSystemActive();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void EnsureHierarchyValid(Transform root, int layer)
    {
        if (root == null) return;
        if (root.localScale == Vector3.zero)
        {
            root.localScale = Vector3.one;
        }
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            EnsureHierarchyValid(root.GetChild(i), layer);
        }
    }

    private void EnsureEventSystemActive()
    {
        EventSystem es = EventSystem.current;
        if (es == null)
        {
            es = FindObjectOfType<EventSystem>(true);
        }

        if (es == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            es = esObj.AddComponent<EventSystem>();
            esObj.AddComponent<InputSystemUIInputModule>();
        }
        else
        {
            if (!es.gameObject.activeSelf) es.gameObject.SetActive(true);
            if (!es.enabled) es.enabled = true;
        }
    }

    /// <summary>
    /// Builds or repairs the full animated stats UI hierarchy inside this Canvas.
    /// Can be called both from Editor scene setup and at runtime.
    /// </summary>
    [ContextMenu("Rebuild Summary UI Hierarchy")]
    public void RebuildSummaryUIHierarchy()
    {
        if (targetCanvas == null) targetCanvas = GetComponent<Canvas>();
        if (targetCanvas == null) targetCanvas = gameObject.AddComponent<Canvas>();

        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 2500;

        CanvasScaler scaler = targetCanvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = targetCanvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (targetCanvas.GetComponent<GraphicRaycaster>() == null)
        {
            targetCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        // Remove old placeholder children
        for (int i = targetCanvas.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = targetCanvas.transform.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        statRows.Clear();

        // Root full-screen backdrop
        GameObject backdropObj = new GameObject("SummaryBackdrop", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        backdropObj.transform.SetParent(targetCanvas.transform, false);
        RectTransform backdropRt = backdropObj.GetComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;

        rootCanvasGroup = backdropObj.GetComponent<CanvasGroup>();
        Image backdropImg = backdropObj.GetComponent<Image>();
        backdropImg.color = new Color(0.02f, 0.02f, 0.03f, 0.96f);
        backdropImg.raycastTarget = true;

        // Terminal Card Container
        GameObject cardObj = new GameObject("TerminalSummaryCard", typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(backdropObj.transform, false);
        cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(960f, 840f);
        cardRect.anchoredPosition = Vector2.zero;

        Image cardBg = cardObj.GetComponent<Image>();
        cardBg.color = new Color(0.055f, 0.06f, 0.075f, 0.98f);

        // Outer border accent
        Outline cardOutline = cardObj.AddComponent<Outline>();
        cardOutline.effectColor = mode == SummaryMode.DreamWakeUp
            ? new Color(0.38f, 0.72f, 0.95f, 0.45f)
            : new Color(0.85f, 0.25f, 0.22f, 0.45f);
        cardOutline.effectDistance = new Vector2(2f, -2f);

        // Top Accent Bar
        GameObject topBarObj = new GameObject("TopAccentBar", typeof(RectTransform), typeof(Image));
        topBarObj.transform.SetParent(cardObj.transform, false);
        RectTransform topBarRt = topBarObj.GetComponent<RectTransform>();
        topBarRt.anchorMin = new Vector2(0f, 1f);
        topBarRt.anchorMax = new Vector2(1f, 1f);
        topBarRt.pivot = new Vector2(0.5f, 1f);
        topBarRt.sizeDelta = new Vector2(0f, 6f);
        topBarRt.anchoredPosition = Vector2.zero;
        Image topBarImg = topBarObj.GetComponent<Image>();
        topBarImg.color = mode == SummaryMode.DreamWakeUp
            ? new Color(0.38f, 0.78f, 1.0f, 0.9f)
            : new Color(0.92f, 0.26f, 0.22f, 0.9f);

        // Header Title
        headerTitleText = CreateTMPText(
            "HeaderTitle",
            cardObj.transform,
            new Vector2(0f, 355f),
            new Vector2(860f, 54f),
            80f,
            TextAlignmentOptions.Center,
            new Color(0.96f, 0.94f, 0.88f, 1f));
        headerTitleText.fontStyle = FontStyles.Bold;

        // Header Subtitle
        headerSubtitleText = CreateTMPText(
            "HeaderSubtitle",
            cardObj.transform,
            new Vector2(0f, 312f),
            new Vector2(860f, 32f),
            24f,
            TextAlignmentOptions.Center,
            new Color(0.62f, 0.66f, 0.72f, 0.9f));

        // Header Separator Line
        GameObject headerLineObj = new GameObject("HeaderLine", typeof(RectTransform), typeof(Image));
        headerLineObj.transform.SetParent(cardObj.transform, false);
        RectTransform headerLineRt = headerLineObj.GetComponent<RectTransform>();
        headerLineRt.anchorMin = new Vector2(0.5f, 0.5f);
        headerLineRt.anchorMax = new Vector2(0.5f, 0.5f);
        headerLineRt.sizeDelta = new Vector2(840f, 2f);
        headerLineRt.anchoredPosition = new Vector2(0f, 282f);
        headerLineObj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);

        // 8 Stat Rows
        string[] rowKeys = new string[]
        {
            "MoneyEarned",
            "DaysSurvived",
            "SubstancesConsumed",
            "GamblingEarned",
            "GamblingLost",
            "TrashCleaned",
            "StoryEncountered",
            "FinalScore"
        };

        string[] defaultLabels = new string[]
        {
            "MONEY EARNED",
            "DAYS SURVIVED",
            "SUBSTANCES CONSUMED",
            "MONEY EARNED BY GAMBLING",
            "MONEY LOST BY GAMBLING",
            "TRASH CLEANED",
            "STORY ELEMENTS ENCOUNTERED",
            "FINAL SCORE"
        };

        float startY = 232f;
        float rowStepY = 56f;

        for (int i = 0; i < rowKeys.Length; i++)
        {
            bool isFinalScore = (i == rowKeys.Length - 1);

            if (isFinalScore)
            {
                // Divider line above Final Score
                GameObject divObj = new GameObject("ScoreDividerLine", typeof(RectTransform), typeof(Image));
                divObj.transform.SetParent(cardObj.transform, false);
                dividerLineRect = divObj.GetComponent<RectTransform>();
                dividerLineRect.anchorMin = new Vector2(0.5f, 0.5f);
                dividerLineRect.anchorMax = new Vector2(0.5f, 0.5f);
                dividerLineRect.sizeDelta = new Vector2(840f, 3f);
                dividerLineRect.anchoredPosition = new Vector2(0f, startY - (i * rowStepY) + 28f);
                divObj.GetComponent<Image>().color = new Color(1.0f, 0.84f, 0.28f, 0.55f);
            }

            float rowY = isFinalScore
                ? (startY - (i * rowStepY) - 12f)
                : (startY - (i * rowStepY));

            float rowHeight = isFinalScore ? 58f : 44f;

            GameObject rowObj = new GameObject($"Row_{i + 1}_{rowKeys[i]}", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            rowObj.transform.SetParent(cardObj.transform, false);
            RectTransform rowRt = rowObj.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0.5f, 0.5f);
            rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.sizeDelta = new Vector2(840f, rowHeight);
            rowRt.anchoredPosition = new Vector2(0f, rowY);

            Image rowBg = rowObj.GetComponent<Image>();
            rowBg.color = new Color(1f, 1f, 1f, isFinalScore ? 0.07f : 0.03f);
            rowBg.raycastTarget = false;

            float fontSize = isFinalScore ? 55f : 36f;

            TMP_Text lbl = CreateTMPText(
                "Label",
                rowObj.transform,
                new Vector2(-20f, 0f),
                new Vector2(560f, rowHeight),
                fontSize,
                TextAlignmentOptions.MidlineLeft,
                isFinalScore ? new Color(1f, 0.88f, 0.42f, 1f) : new Color(0.84f, 0.86f, 0.90f, 1f));

            RectTransform lblRt = lbl.rectTransform;
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(0.7f, 1f);
            lblRt.offsetMin = new Vector2(22f, 0f);
            lblRt.offsetMax = new Vector2(0f, 0f);
            lbl.text = defaultLabels[i];
            if (isFinalScore) lbl.fontStyle = FontStyles.Bold;

            TMP_Text val = CreateTMPText(
                "Value",
                rowObj.transform,
                new Vector2(20f, 0f),
                new Vector2(260f, rowHeight),
                fontSize,
                TextAlignmentOptions.MidlineRight,
                isFinalScore ? new Color(1f, 0.84f, 0.28f, 1f) : new Color(0.95f, 0.95f, 0.95f, 1f));

            RectTransform valRt = val.rectTransform;
            valRt.anchorMin = new Vector2(0.65f, 0f);
            valRt.anchorMax = new Vector2(1f, 1f);
            valRt.offsetMin = new Vector2(0f, 0f);
            valRt.offsetMax = new Vector2(-22f, 0f);
            val.text = (i == 0 || i == 3 || i == 4) ? "$0.00" : "0";
            val.fontStyle = FontStyles.Bold;

            statRows.Add(new StatRowReferences
            {
                statKey = rowKeys[i],
                rowRoot = rowRt,
                rowCanvasGroup = rowObj.GetComponent<CanvasGroup>(),
                labelText = lbl,
                valueText = val,
                rowHighlightBar = rowBg
            });
        }

        // Bottom Action Button ("GO TO MENU" or "WAKE UP")
        GameObject btnObj = new GameObject("ActionButton", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(cardObj.transform, false);
        actionButtonRect = btnObj.GetComponent<RectTransform>();
        actionButtonRect.anchorMin = new Vector2(0.5f, 0.5f);
        actionButtonRect.anchorMax = new Vector2(0.5f, 0.5f);
        actionButtonRect.pivot = new Vector2(0.5f, 0.5f);
        actionButtonRect.sizeDelta = new Vector2(380f, 68f);
        actionButtonRect.anchoredPosition = new Vector2(0f, -335f);

        actionButtonCanvasGroup = btnObj.GetComponent<CanvasGroup>();
        Image btnImg = btnObj.GetComponent<Image>();
        btnImg.color = mode == SummaryMode.DreamWakeUp
            ? new Color(0.16f, 0.36f, 0.54f, 0.98f)
            : new Color(0.55f, 0.14f, 0.14f, 0.98f);
        btnImg.raycastTarget = true;

        Outline btnOutline = btnObj.AddComponent<Outline>();
        btnOutline.effectColor = new Color(1f, 1f, 1f, 0.35f);
        btnOutline.effectDistance = new Vector2(2f, -2f);

        actionButton = btnObj.GetComponent<Button>();
        actionButton.targetGraphic = btnImg;
        ColorBlock cb = actionButton.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        actionButton.colors = cb;

        actionButtonText = CreateTMPText(
            "ButtonLabel",
            btnObj.transform,
            Vector2.zero,
            new Vector2(360f, 60f),
            45f,
            TextAlignmentOptions.Center,
            new Color(0.98f, 0.96f, 0.92f, 1f));
        actionButtonText.fontStyle = FontStyles.Bold;
        actionButtonText.raycastTarget = false;
        RectTransform btnTxtRt = actionButtonText.rectTransform;
        btnTxtRt.anchorMin = Vector2.zero;
        btnTxtRt.anchorMax = Vector2.one;
        btnTxtRt.offsetMin = Vector2.zero;
        btnTxtRt.offsetMax = Vector2.zero;

        // Screen Transition Fade Overlay on top
        GameObject fadeObj = new GameObject("ScreenTransitionFade", typeof(RectTransform), typeof(Image));
        fadeObj.transform.SetParent(targetCanvas.transform, false);
        RectTransform fadeRt = fadeObj.GetComponent<RectTransform>();
        fadeRt.anchorMin = Vector2.zero;
        fadeRt.anchorMax = Vector2.one;
        fadeRt.offsetMin = Vector2.zero;
        fadeRt.offsetMax = Vector2.zero;
        screenFadeOverlay = fadeObj.GetComponent<Image>();
        screenFadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        screenFadeOverlay.raycastTarget = false;
        fadeObj.SetActive(false);

        ApplyModeTitles();
        BindButtonEvents();
    }

    private void EnsureUIBound()
    {
        if (targetCanvas == null) targetCanvas = GetComponent<Canvas>();
        if (targetCanvas == null) targetCanvas = GetComponentInParent<Canvas>(true);
        if (targetCanvas == null) return;

        // If targetCanvas has children, bind references dynamically rather than destroying them
        if (rootCanvasGroup == null)
        {
            Transform backdrop = targetCanvas.transform.Find("SummaryBackdrop");
            if (backdrop != null) rootCanvasGroup = backdrop.GetComponent<CanvasGroup>();
        }

        Transform card = targetCanvas.transform.Find("SummaryBackdrop/TerminalSummaryCard");
        if (card != null)
        {
            if (cardRect == null) cardRect = card.GetComponent<RectTransform>();
            if (headerTitleText == null)
            {
                Transform t = card.Find("HeaderTitle");
                if (t != null) headerTitleText = t.GetComponent<TMP_Text>();
            }
            if (headerSubtitleText == null)
            {
                Transform t = card.Find("HeaderSubtitle");
                if (t != null) headerSubtitleText = t.GetComponent<TMP_Text>();
            }
            if (dividerLineRect == null)
            {
                Transform t = card.Find("ScoreDividerLine");
                if (t != null) dividerLineRect = t.GetComponent<RectTransform>();
            }
            if (actionButton == null)
            {
                Transform t = card.Find("ActionButton");
                if (t != null)
                {
                    actionButton = t.GetComponent<Button>();
                    actionButtonRect = t.GetComponent<RectTransform>();
                    actionButtonCanvasGroup = t.GetComponent<CanvasGroup>();
                    Transform lbl = t.Find("ButtonLabel");
                    if (lbl == null) lbl = t.Find("Text (TMP)");
                    if (lbl != null) actionButtonText = lbl.GetComponent<TMP_Text>();
                }
            }

            if (statRows == null || statRows.Count < 8 || statRows[0].labelText == null)
            {
                statRows = new List<StatRowReferences>();
                string[] rowKeys = new string[]
                {
                    "MoneyEarned",
                    "DaysSurvived",
                    "SubstancesConsumed",
                    "GamblingEarned",
                    "GamblingLost",
                    "TrashCleaned",
                    "StoryEncountered",
                    "FinalScore"
                };

                for (int i = 0; i < rowKeys.Length; i++)
                {
                    Transform rowTrans = card.Find($"Row_{i + 1}_{rowKeys[i]}");
                    if (rowTrans != null)
                    {
                        Transform lbl = rowTrans.Find("Label");
                        Transform val = rowTrans.Find("Value");
                        statRows.Add(new StatRowReferences
                        {
                            statKey = rowKeys[i],
                            rowRoot = rowTrans.GetComponent<RectTransform>(),
                            rowCanvasGroup = rowTrans.GetComponent<CanvasGroup>(),
                            labelText = lbl != null ? lbl.GetComponent<TMP_Text>() : null,
                            valueText = val != null ? val.GetComponent<TMP_Text>() : null,
                            rowHighlightBar = rowTrans.GetComponent<Image>()
                        });
                    }
                }
            }
        }

        if (screenFadeOverlay == null)
        {
            Transform fade = targetCanvas.transform.Find("ScreenTransitionFade");
            if (fade != null) screenFadeOverlay = fade.GetComponent<Image>();
        }

        // Only build from scratch if canvas is completely empty!
        if (targetCanvas.transform.childCount == 0)
        {
            RebuildSummaryUIHierarchy();
        }
    }

    private TMP_Text CreateTMPText(
        string name,
        Transform parent,
        Vector2 anchoredPos,
        Vector2 sizeDelta,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (customFont != null)
        {
            tmp.font = customFont;
        }
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    // =========================================================================
    // PROCEDURAL RETRO CRT AUDIO FEEDBACK
    // =========================================================================

    private void EnsureProceduralAudioClips()
    {
        if (uiAudioSource == null)
        {
            uiAudioSource = GetComponent<AudioSource>();
            if (uiAudioSource == null)
            {
                uiAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        uiAudioSource.enabled = true;
        uiAudioSource.mute = false;
        uiAudioSource.playOnAwake = false;
        uiAudioSource.loop = false;
        uiAudioSource.spatialBlend = 0f;
        uiAudioSource.ignoreListenerPause = true;
        uiAudioSource.ignoreListenerVolume = false;

        if (typeTickClip == null)
        {
            typeTickClip = CreateSynthToneClip("CRT_TypeTick", 0.018f, 920f, 540f, 0.35f);
        }
        if (rowLockClip == null)
        {
            rowLockClip = CreateSynthToneClip("CRT_RowLock", 0.055f, 520f, 780f, 0.55f);
        }
        if (scoreFinishClip == null)
        {
            scoreFinishClip = CreateSynthToneClip("CRT_ScoreFinish", 0.16f, 440f, 880f, 0.7f);
        }
        if (buttonClickClip == null)
        {
            buttonClickClip = CreateSynthToneClip("CRT_ButtonClick", 0.07f, 660f, 330f, 0.65f);
        }
    }

    private static AudioClip CreateSynthToneClip(string clipName, float duration, float startFreq, float endFreq, float amplitude)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.Max(64, Mathf.RoundToInt(duration * sampleRate));
        float[] samples = new float[sampleCount];

        float phase = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += (freq * 2f * Mathf.PI) / sampleRate;

            float env = Mathf.Sin(t * Mathf.PI);
            // Blend sine + soft square harmonic for retro CRT character
            float wave = Mathf.Sin(phase) * 0.7f + Mathf.Sign(Mathf.Sin(phase)) * 0.3f;
            samples[i] = wave * env * amplitude;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlayTickSound(float pitch)
    {
        if (Time.frameCount - lastTickSampleFrame < 2) return;
        lastTickSampleFrame = Time.frameCount;
        PlaySoundClip(typeTickClip, 0.35f, pitch);
    }

    private void PlaySoundClip(AudioClip clip, float volumeMultiplier, float pitch)
    {
        if (clip == null || uiAudioSource == null) return;
        if (!uiAudioSource.enabled) uiAudioSource.enabled = true;
        uiAudioSource.mute = false;
        uiAudioSource.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        uiAudioSource.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * volumeMultiplier));
    }
}
