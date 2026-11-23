using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BarManager : MonoBehaviour
{
    public static BarManager Instance { get; private set; }

    [Header("References")]
    public DVDLogoBouncer economyBouncer;
    [Tooltip("Auto-found or auto-created if left empty. Triggered when Depression reaches 100%.")]
    public DepressionBreakdownSequence depressionBreakdown;

    [Header("UI Sliders")]
    public Slider hungerSlider;
    public Slider needsSlider;
    public Slider sleepSlider;
    public Slider dirtinessSlider;
    public Slider depressionSlider;

    [Header("UI Text")]
    public TMP_Text inflationText;
    public TMP_Text foodCostText;

    [Header("Hunger Bar Visual Customization")]
    [Tooltip("If checked: 0 hunger shows a FULL bar, max hunger shows an EMPTY bar, and the bar shakes when empty. If unchecked: standard fill-up.")]
    public bool invertHungerVisuals = true;

    [Header("Bar Maximums")]
    public float maxHunger = 100f;
    public float maxNeeds = 100f;
    public float maxSleep = 100f;
    public float maxDirtiness = 100f;
    public float maxDepression = 100f;

    [Header("Current Values")]
    public float currentHunger = 0f;
    public float currentNeeds = 0f;
    public float currentSleep = 0f;
    public float currentDirtiness = 0f;
    public float currentDepression = 0f;

    [Header("Automatic Fill Rates (Per Second)")]
    public float hungerFillRate = 0.5f;
    public float sleepFillRate = 0.05f;
    public float baseDepressionFillRate = 1f;

    [Header("Action Impacts: Stat Reductions")]
    public float foodHungerReduction = 50f;
    public float toiletNeedsReduction = 100f;
    public float sleepReduction = 100f;
    public float cleanDirtinessReduction = 100f;

    [Header("Action Impacts: Dirtiness Increases")]
    public float eatDirtinessIncrease = 10f;
    public float toiletDirtinessIncrease = 15f;
    public float sleepDirtinessIncrease = 5f;

    public float foodNeedsIncrease = 33.4f;

    [Header("Action Impacts: Trash Clearing")]
    public float trashDirtinessIncrease = 1.17f;
    public float trashDepressionReduction = 2.5f;

    [Header("Action Impacts: Depression Reductions")]
    public float eatDepressionReduction = 10f;
    public float toiletDepressionReduction = 2f;
    public float sleepDepressionReduction = 3f;
    public float cleanDepressionReduction = 25f;

    [Header("Economy & Daily Inflation")]
    public float baseFoodCost = 10f;
    [Tooltip("Rate at which prices inflate each morning per dollar of all-time money earned (e.g. 0.0001 = 0.01% per $1).")]
    public float inflationRatePerDollar = 0.0001f;
    [Tooltip("Inflation increase (in percent) can never be more than this fraction of the money gained (0.5 = 50%).")]
    [Range(0.05f, 1f)]
    public float maxInflationPercentOfMoneyGained = 0.5f;
    [Tooltip("Impact TMP font asset used for the subtle morning inflation notification.")]
    public TMP_FontAsset morningBannerFont;
    [Tooltip("Delay after waking up before the morning inflation notice fades in.")]
    public float morningBannerDelay = 1.05f;
    [Tooltip("How long the morning inflation notice stays visible on screen.")]
    public float morningBannerHold = 5.5f;
    private decimal currentFoodCost = 0m;
    private float currentInflationMultiplier = 1f;

    // Runtime morning inflation notification UI (matches tutorial Impact text + soft shadow box)
    private Canvas morningNoticeCanvas;
    private RectTransform morningNoticeRect;
    private CanvasGroup morningNoticeGroup;
    private TextMeshProUGUI morningNoticeText;
    private Sprite morningSoftShadowSprite;
    private Coroutine morningNoticeRoutine;

    [Header("Tiredness & Endgame Pressure (runtime, read-only)")]
    [Tooltip("Hunger/dirtiness gain per unlocked DVD Player / DVD Disc tier.")]
    public float perTierSurvivalScaling = 0.05f;
    public float endgameHungerMultiplier = 2.5f;
    public float endgameNeedsMultiplier = 2.0f;
    public float endgameTirednessMultiplier = 1.8f;
    [Tooltip("Extra multiplier added for every day survived after both upgrade paths hit max tier (+20% each).")]
    public float endgamePerDayBonus = 0.2f;
    [Tooltip("Sleeping requires the tiredness bar to be at least this fraction of max.")]
    [Range(0f, 1f)] public float sleepRequiredFraction = 0.90f;
    public float currentMoneyDepressionMultiplier = 1f; // always 1: money no longer accelerates depression

    [Header("Penalty Settings")]
    public float penaltyMultiplier = 2f;

    // Tutorial & runtime individual bar active states (when false, bar UI is hidden AND bar does not fill or function)
    [HideInInspector] public bool hungerBarActive = true;
    [HideInInspector] public bool needsBarActive = true;
    [HideInInspector] public bool dirtinessBarActive = true;
    [HideInInspector] public bool sleepBarActive = true;
    [HideInInspector] public bool depressionBarActive = true;

    public event System.Action OnFoodEatenEvent;
    public event System.Action OnToiletUsedEvent;
    public event System.Action OnShoweredEvent;
    public event System.Action OnSleptEvent;

    private bool hasTriggeredDepressionEnding = false;
    private decimal lastDisplayedFoodCost = -1m;

    public void SetBarsActiveState(bool hunger, bool needs, bool dirtiness, bool sleep, bool depression)
    {
        hungerBarActive = hunger;
        needsBarActive = needs;
        dirtinessBarActive = dirtiness;
        sleepBarActive = sleep;
        depressionBarActive = depression;

        if (!hungerBarActive) currentHunger = 0f;
        if (!needsBarActive) currentNeeds = 0f;
        if (!dirtinessBarActive) currentDirtiness = 0f;
        if (!sleepBarActive) currentSleep = 0f;
        if (!depressionBarActive) currentDepression = 0f;

        if (hungerSlider != null) hungerSlider.gameObject.SetActive(hungerBarActive);
        if (needsSlider != null) needsSlider.gameObject.SetActive(needsBarActive);
        if (dirtinessSlider != null) dirtinessSlider.gameObject.SetActive(dirtinessBarActive);
        if (sleepSlider != null) sleepSlider.gameObject.SetActive(sleepBarActive);
        if (depressionSlider != null) depressionSlider.gameObject.SetActive(depressionBarActive);

        UpdateUI();
    }

    void Awake()
    {
        ApplyBalanceTuning();
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            // Update reference if a new instance loaded
            Instance = this;
        }
    }

    /// <summary>
    /// Forces the tuned survival numbers even if the scene instance still has older serialized values.
    /// (Runs once on load; RunSaveManager / boosters can modify them afterwards.)
    /// </summary>
    private void OnValidate()
    {
#if UNITY_EDITOR
        if (morningBannerFont == null)
        {
            morningBannerFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Scripts/DVD BOUNCER/IMPACT SDF.asset");
        }
#endif
    }

    private void ApplyBalanceTuning()
    {
        hungerFillRate = Mathf.Min(hungerFillRate, 0.5f);
        sleepFillRate = Mathf.Min(sleepFillRate, 0.05f);
        trashDepressionReduction = 2.5f;
        trashDirtinessIncrease = 1.17f;
        sleepDepressionReduction = 3f;
        cleanDepressionReduction = 25f;
        eatDepressionReduction = 10f;
        currentMoneyDepressionMultiplier = 1f;
    }

    /// <summary>
    /// Calculates the inflation multiplier for a new morning based on all-time money circulating in the economy.
    /// </summary>
    public static float ComputeInflationMultiplier(float allTimeMoneyFloat, float ratePerDollar, float maxPercentOfMoneyGained)
    {
        float safeMoney = Mathf.Max(0f, allTimeMoneyFloat);
        float mult = 1f + (safeMoney * Mathf.Max(0f, ratePerDollar));

        float maxInflationPercent = safeMoney * Mathf.Max(0f, maxPercentOfMoneyGained);
        float maxMultiplier = 1f + (maxInflationPercent / 100f);
        if (mult > maxMultiplier) mult = maxMultiplier;

        return Mathf.Max(1f, mult);
    }

    /// <summary>
    /// Sets today's locked-in inflation multiplier (calculated once per morning, stays fixed all day).
    /// </summary>
    public void SetDailyInflationMultiplier(float multiplier)
    {
        currentInflationMultiplier = Mathf.Max(1f, multiplier);
        RefreshDailyFoodCost();
    }

    // ---------------- Tier scaling & endgame pressure ----------------

    private float TierSurvivalMultiplier
    {
        get
        {
            int tiers = UpgradeManager.Instance != null ? UpgradeManager.Instance.TotalUnlockedTierCount : 0;
            return 1f + perTierSurvivalScaling * tiers;
        }
    }

    public bool IsEndgameActive => UpgradeManager.Instance != null && UpgradeManager.Instance.IsEndgameMaxed;

    private float EndgameDayFactor
    {
        get
        {
            int days = RunSaveManager.HasInstance ? RunSaveManager.Instance.postMaxDaysSurvived : 0;
            return 1f + endgamePerDayBonus * days;
        }
    }

    public float HungerSpeedMultiplier => TierSurvivalMultiplier * (IsEndgameActive ? endgameHungerMultiplier * EndgameDayFactor : 1f);
    public float NeedsGainMultiplier => IsEndgameActive ? endgameNeedsMultiplier * EndgameDayFactor : 1f;
    public float TirednessChunkMultiplier => IsEndgameActive ? endgameTirednessMultiplier * EndgameDayFactor : 1f;
    public float DirtinessGainMultiplier => TierSurvivalMultiplier;

    /// <summary>Adds tiredness (the sleep bar) from an action. Scaled up during the endgame.</summary>
    public void AddTiredness(float amount)
    {
        if (!sleepBarActive || amount <= 0f || hasTriggeredDepressionEnding) return;
        currentSleep = Mathf.Clamp(currentSleep + amount * TirednessChunkMultiplier, 0, maxSleep);
    }

    public void ReduceTiredness(float amount)
    {
        if (!sleepBarActive) return;
        currentSleep = Mathf.Clamp(currentSleep - Mathf.Abs(amount), 0, maxSleep);
    }

    /// <summary>Energy drink (Caffeinated) -35 tiredness, Zyn (Nicotined) -20 tiredness.</summary>
    public void ApplySubstanceTirednessRelief(ConsumableVisualEffectType type)
    {
        if (type == ConsumableVisualEffectType.Caffeinated) ReduceTiredness(35f);
        else if (type == ConsumableVisualEffectType.Nicotined) ReduceTiredness(20f);
    }

    /// <summary>True when the tiredness bar is high enough to be allowed to sleep.</summary>
    public bool IsTiredEnoughToSleep => currentSleep >= maxSleep * sleepRequiredFraction - 0.001f;

    void Start()
    {
        if (depressionBreakdown == null)
        {
            depressionBreakdown = GetComponent<DepressionBreakdownSequence>();
            if (depressionBreakdown == null)
            {
                depressionBreakdown = FindObjectOfType<DepressionBreakdownSequence>();
            }
        }

        // Permanent inflation HUD UI is hidden; inflation is announced once each morning via a subtle Impact banner
        if (inflationText != null) inflationText.gameObject.SetActive(false);

        // Sync today's locked-in daily inflation multiplier from RunSaveManager
        if (RunSaveManager.HasInstance)
        {
            currentInflationMultiplier = Mathf.Max(1f, RunSaveManager.Instance.currentDayInflationMultiplier);
        }
        else
        {
            currentInflationMultiplier = 1f;
        }
        RefreshDailyFoodCost();

        // Secret Good Day: Hide all survival bars, inflation, and food cost UI
        if (RunSaveManager.HasInstance && RunSaveManager.Instance.isGoodDayActive)
        {
            if (hungerSlider != null) hungerSlider.gameObject.SetActive(false);
            if (needsSlider != null) needsSlider.gameObject.SetActive(false);
            if (sleepSlider != null) sleepSlider.gameObject.SetActive(false);
            if (dirtinessSlider != null) dirtinessSlider.gameObject.SetActive(false);
            if (depressionSlider != null) depressionSlider.gameObject.SetActive(false);
            if (inflationText != null) inflationText.gameObject.SetActive(false);
            if (foodCostText != null) foodCostText.gameObject.SetActive(false);
        }
        else if (RunSaveManager.HasInstance &&
                 RunSaveManager.Instance.pendingMorningInflationAnnouncement &&
                 !TutorialManager.IsTutorialActive)
        {
            float pct = RunSaveManager.Instance.pendingMorningInflationPercent;
            RunSaveManager.Instance.pendingMorningInflationAnnouncement = false;
            ShowMorningInflationNotification(pct);
        }

        UpdateUI();
    }

    void Update()
    {
        if (RunSaveManager.HasInstance && RunSaveManager.Instance.isGoodDayActive) return;

        RefreshDailyFoodCost();
        ProcessBars();
        UpdateUI();
    }

    /// <summary>
    /// Inflation stays fixed for the entire day (only recalculated each morning).
    /// This only keeps food cost synced with any active Food Bill penalty.
    /// </summary>
    private void RefreshDailyFoodCost()
    {
        float foodMultiplier = currentInflationMultiplier;
        if (BillManager.Instance != null && BillManager.Instance.IsFoodMoreExpensive)
        {
            foodMultiplier *= BillManager.Instance.FoodBillMultiplier;
        }
        currentFoodCost = (decimal)(baseFoodCost * foodMultiplier);
    }

    private void ProcessBars()
    {
        if (hasTriggeredDepressionEnding)
        {
            currentDepression = maxDepression;
            return;
        }

        if (hungerBarActive)
        {
            currentHunger = Mathf.Clamp(currentHunger + (hungerFillRate * HungerSpeedMultiplier * Time.deltaTime), 0, maxHunger);
        }
        else
        {
            currentHunger = 0f;
        }

        if (sleepBarActive)
        {
            currentSleep = Mathf.Clamp(currentSleep + (sleepFillRate * Time.deltaTime), 0, maxSleep);
        }
        else
        {
            currentSleep = 0f;
        }

        if (!needsBarActive) currentNeeds = 0f;
        if (!dirtinessBarActive) currentDirtiness = 0f;

        if (!depressionBarActive)
        {
            currentDepression = 0f;
            return;
        }

        bool isHungerFull = hungerBarActive && currentHunger >= maxHunger;
        bool isNeedsFull = needsBarActive && currentNeeds >= maxNeeds;
        bool isSleepFull = sleepBarActive && currentSleep >= maxSleep;
        bool isDirtinessFull = dirtinessBarActive && currentDirtiness >= maxDirtiness;

        currentMoneyDepressionMultiplier = 1f;
        float depressionSpeed = baseDepressionFillRate;

        if (isHungerFull) depressionSpeed *= penaltyMultiplier;
        if (isNeedsFull) depressionSpeed *= penaltyMultiplier;
        if (isSleepFull) depressionSpeed *= penaltyMultiplier;
        if (isDirtinessFull) depressionSpeed *= penaltyMultiplier;

        float cap = TutorialManager.IsTutorialActive ? (maxDepression * 0.85f) : maxDepression;
        currentDepression = Mathf.Clamp(currentDepression + (depressionSpeed * Time.deltaTime), 0, cap);

        // When Depression reaches 100%, lock it at max and trigger the Heart-Attack Breakdown -> Cut to Black -> Gun Cocking -> UI Popup!
        if (!TutorialManager.IsTutorialActive && currentDepression >= maxDepression)
        {
            currentDepression = maxDepression;
            hasTriggeredDepressionEnding = true;

            if (depressionBreakdown == null)
            {
                depressionBreakdown = FindObjectOfType<DepressionBreakdownSequence>();
                if (depressionBreakdown == null)
                {
                    depressionBreakdown = gameObject.AddComponent<DepressionBreakdownSequence>();
                }
            }

            depressionBreakdown.TriggerDepressionBreakdown();
        }
    }

    private void UpdateUI()
    {
        if (hungerSlider)
        {
            float hungerNorm = currentHunger / maxHunger;
            hungerSlider.value = invertHungerVisuals ? (1f - hungerNorm) : hungerNorm;

            SliderMaxAlert alert = hungerSlider.GetComponent<SliderMaxAlert>();
            if (alert != null)
            {
                alert.alertOnEmpty = invertHungerVisuals;
            }
        }
        if (needsSlider) needsSlider.value = currentNeeds / maxNeeds;
        if (sleepSlider) sleepSlider.value = currentSleep / maxSleep;
        if (dirtinessSlider) dirtinessSlider.value = currentDirtiness / maxDirtiness;
        if (depressionSlider) depressionSlider.value = currentDepression / maxDepression;

        if (inflationText != null && inflationText.gameObject.activeSelf)
        {
            inflationText.gameObject.SetActive(false);
        }

        if (foodCostText && currentFoodCost != lastDisplayedFoodCost)
        {
            lastDisplayedFoodCost = currentFoodCost;
            foodCostText.text = $"Food Cost: {CurrencyFormatter.Format(currentFoodCost)}";
        }
    }

    // =========================================================================
    // MORNING INFLATION NOTIFICATION (IMPACT FONT + SOFT SHADOW BOX)
    // =========================================================================

    public void ShowMorningInflationNotification(float percentUp)
    {
        if (percentUp < 0.05f) return;

        if (morningNoticeRoutine != null)
        {
            StopCoroutine(morningNoticeRoutine);
        }
        morningNoticeRoutine = StartCoroutine(MorningInflationNoticeRoutine(percentUp));
    }

    private IEnumerator MorningInflationNoticeRoutine(float percentUp)
    {
        BuildMorningInflationUI();

        string pctStr = percentUp < 0.1f
            ? "0.1"
            : (percentUp < 10f ? percentUp.ToString("0.#") : Mathf.RoundToInt(percentUp).ToString());
        string message = $"Due to unexplained money circulating in the economy, inflation is up {pctStr}%";

        if (morningNoticeText != null)
        {
            morningNoticeText.text = message;
            if (morningNoticeRect != null)
            {
                const float maxTextWidth = 1100f;
                Vector2 preferred = morningNoticeText.GetPreferredValues(message, maxTextWidth, 0f);
                float boxW = Mathf.Clamp(preferred.x + 116f, 360f, 1220f);
                float boxH = Mathf.Clamp(preferred.y + 66f, 84f, 220f);
                morningNoticeRect.sizeDelta = new Vector2(boxW, boxH);
            }
        }

        if (morningNoticeGroup != null) morningNoticeGroup.alpha = 0f;
        if (morningNoticeRect != null) morningNoticeRect.gameObject.SetActive(false);

        // Wait for wake-up fade-in to clear
        float delay = Mathf.Max(0f, morningBannerDelay);
        if (delay > 0f) yield return new WaitForSeconds(delay);

        EnsureMorningNoticeCameraBound();
        if (morningNoticeRect != null) morningNoticeRect.gameObject.SetActive(true);

        // Fade in
        float fadeInDur = 0.45f;
        float t = 0f;
        while (t < fadeInDur)
        {
            t += Time.deltaTime;
            if (morningNoticeGroup != null) morningNoticeGroup.alpha = Mathf.Clamp01(t / fadeInDur);
            yield return null;
        }
        if (morningNoticeGroup != null) morningNoticeGroup.alpha = 1f;

        // Hold visible
        yield return new WaitForSeconds(Mathf.Max(1.5f, morningBannerHold));

        // Fade out
        float fadeOutDur = 0.9f;
        t = 0f;
        while (t < fadeOutDur)
        {
            t += Time.deltaTime;
            if (morningNoticeGroup != null) morningNoticeGroup.alpha = 1f - Mathf.Clamp01(t / fadeOutDur);
            yield return null;
        }
        if (morningNoticeGroup != null) morningNoticeGroup.alpha = 0f;
        if (morningNoticeRect != null) morningNoticeRect.gameObject.SetActive(false);

        morningNoticeRoutine = null;
    }

    private void EnsureMorningNoticeCameraBound()
    {
        if (morningNoticeCanvas == null) return;
        Camera cam = Camera.main;
        if (cam == null || cam.targetTexture != null)
        {
            Camera[] cams = FindObjectsOfType<Camera>(true);
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != null && cams[i].targetTexture == null)
                {
                    cam = cams[i];
                    break;
                }
            }
        }
        if (cam != null)
        {
            morningNoticeCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            morningNoticeCanvas.worldCamera = cam;
            morningNoticeCanvas.planeDistance = 0.21f;
        }
    }

    private void BuildMorningInflationUI()
    {
        if (morningNoticeCanvas != null) return;

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 5;

        GameObject canvasObj = new GameObject("MorningInflationNoticeCanvas");
        canvasObj.layer = uiLayer;
        canvasObj.transform.SetParent(transform, false);

        morningNoticeCanvas = canvasObj.AddComponent<Canvas>();
        EnsureMorningNoticeCameraBound();
        morningNoticeCanvas.sortingOrder = 32760;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        morningSoftShadowSprite = CreateSoftShadowBoxSprite(128, 32);

        GameObject shadowObj = new GameObject("MorningInflationShadowBox", typeof(RectTransform), typeof(Image));
        shadowObj.layer = uiLayer;
        shadowObj.transform.SetParent(canvasObj.transform, false);

        Image shadowImg = shadowObj.GetComponent<Image>();
        shadowImg.sprite = morningSoftShadowSprite;
        shadowImg.type = Image.Type.Sliced;
        shadowImg.color = new Color(0f, 0f, 0f, 0.62f);
        shadowImg.raycastTarget = false;

        morningNoticeRect = shadowObj.GetComponent<RectTransform>();
        morningNoticeRect.anchorMin = new Vector2(0.5f, 1f);
        morningNoticeRect.anchorMax = new Vector2(0.5f, 1f);
        morningNoticeRect.pivot = new Vector2(0.5f, 1f);
        morningNoticeRect.sizeDelta = new Vector2(960f, 92f);
        morningNoticeRect.anchoredPosition = new Vector2(0f, -74f);

        morningNoticeGroup = shadowObj.AddComponent<CanvasGroup>();
        morningNoticeGroup.blocksRaycasts = false;
        morningNoticeGroup.alpha = 0f;

        GameObject textObj = new GameObject("MorningInflationText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.layer = uiLayer;
        textObj.transform.SetParent(shadowObj.transform, false);

        morningNoticeText = textObj.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset fontToUse = morningBannerFont;
        if (fontToUse == null && inflationText != null) fontToUse = inflationText.font;
        if (fontToUse == null && economyBouncer != null && economyBouncer.totalMoneyText != null) fontToUse = economyBouncer.totalMoneyText.font;
        if (fontToUse != null) morningNoticeText.font = fontToUse;

        morningNoticeText.fontSize = 31f;
        morningNoticeText.fontStyle = FontStyles.Normal;
        morningNoticeText.alignment = TextAlignmentOptions.Center;
        morningNoticeText.color = new Color(0.98f, 0.97f, 0.94f, 1f);
        morningNoticeText.enableWordWrapping = true;
        morningNoticeText.raycastTarget = false;

        Material mat = morningNoticeText.fontMaterial;
        if (mat != null)
        {
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.08f);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.85f));
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.06f);

            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.78f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.25f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.12f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.42f);

            morningNoticeText.UpdateMeshPadding();
        }

        RectTransform textRT = morningNoticeText.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(46f, 22f);
        textRT.offsetMax = new Vector2(-46f, -22f);

        shadowObj.SetActive(false);
    }

    private static Sprite CreateSoftShadowBoxSprite(int size, int featherBorder)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        int innerMin = featherBorder;
        int innerMax = size - 1 - featherBorder;
        float invBorder = 1f / Mathf.Max(1f, featherBorder);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float dy = 0f;
            if (y < innerMin) dy = (innerMin - y) * invBorder;
            else if (y > innerMax) dy = (y - innerMax) * invBorder;

            for (int x = 0; x < size; x++)
            {
                float dx = 0f;
                if (x < innerMin) dx = (innerMin - x) * invBorder;
                else if (x > innerMax) dx = (x - innerMax) * invBorder;

                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01(1f - dist);
                float smooth = t * t * (3f - 2f * t);
                float alpha = smooth * smooth;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);

        Vector4 border = new Vector4(featherBorder, featherBorder, featherBorder, featherBorder);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }

    public decimal GetCurrentFoodCost()
    {
        return currentFoodCost;
    }

    public float CurrentInflationMultiplier => currentInflationMultiplier;

    /// <summary>
    /// True when hunger, needs/bladder or dirtiness is at max (critical). While true the player cannot sleep.
    /// The sleep bar itself is excluded, since a full sleep bar is exactly why you want to sleep.
    /// </summary>
    public bool IsCriticalBarMaxed(out string barName)
    {
        barName = null;
        if (hungerBarActive && currentHunger >= maxHunger - 0.01f) barName = "hungry";
        else if (needsBarActive && currentNeeds >= maxNeeds - 0.01f) barName = "desperate for the toilet";
        else if (dirtinessBarActive && currentDirtiness >= maxDirtiness - 0.01f) barName = "filthy";
        return barName != null;
    }

    public decimal GetFoodCost(float basePrice)
    {
        float foodMultiplier = currentInflationMultiplier;
        if (BillManager.Instance != null && BillManager.Instance.IsFoodMoreExpensive)
        {
            foodMultiplier *= BillManager.Instance.FoodBillMultiplier;
        }
        return (decimal)(basePrice * foodMultiplier);
    }

    // --- INTERACTION METHODS ---

    public bool TryEatFood(decimal cost, float hungerReduction, float needsIncrease, float dirtinessIncrease, float depressionReduction)
    {
        // Cannot eat if needs/bladder are already full
        if (needsBarActive && currentNeeds >= maxNeeds - 0.01f)
        {
            return false;
        }

        if (economyBouncer != null && !economyBouncer.TrySpendMoney(cost))
        {
            return false;
        }

        if (hungerBarActive) currentHunger = Mathf.Clamp(currentHunger - hungerReduction, 0, maxHunger);
        if (needsBarActive) currentNeeds = Mathf.Clamp(currentNeeds + needsIncrease * NeedsGainMultiplier, 0, maxNeeds);
        if (dirtinessBarActive) currentDirtiness = Mathf.Clamp(currentDirtiness + dirtinessIncrease * DirtinessGainMultiplier, 0, maxDirtiness);
        if (sleepBarActive) AddTiredness(4f); // eating
        if (depressionBarActive) currentDepression = Mathf.Clamp(currentDepression - depressionReduction, 0, maxDepression);
        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.RecordFoodEaten();
        }
        OnFoodEatenEvent?.Invoke();
        return true;
    }

    public void TryEatFood()
    {
        TryEatFood(currentFoodCost, foodHungerReduction, foodNeedsIncrease, eatDirtinessIncrease, eatDepressionReduction);
    }

    public void UseToilet()
    {
        if (needsBarActive) currentNeeds = Mathf.Clamp(currentNeeds - toiletNeedsReduction, 0, maxNeeds);
        if (dirtinessBarActive) currentDirtiness = Mathf.Clamp(currentDirtiness + toiletDirtinessIncrease * DirtinessGainMultiplier, 0, maxDirtiness);
        if (sleepBarActive) AddTiredness(3f); // toilet
        if (depressionBarActive) currentDepression = Mathf.Clamp(currentDepression - toiletDepressionReduction, 0, maxDepression);
        OnToiletUsedEvent?.Invoke();
    }

    public void GoToSleep()
    {
        if (sleepBarActive) currentSleep = Mathf.Clamp(currentSleep - sleepReduction, 0, maxSleep);
        if (dirtinessBarActive) currentDirtiness = Mathf.Clamp(currentDirtiness + sleepDirtinessIncrease * DirtinessGainMultiplier, 0, maxDirtiness);
        if (depressionBarActive) currentDepression = Mathf.Clamp(currentDepression - sleepDepressionReduction, 0, maxDepression);
        OnSleptEvent?.Invoke();
    }

    public bool CanCleanTrash => !dirtinessBarActive || currentDirtiness < maxDirtiness;

    public void CleanTrashItem()
    {
        if (!CanCleanTrash) return;

        if (dirtinessBarActive) currentDirtiness = Mathf.Clamp(currentDirtiness + trashDirtinessIncrease * DirtinessGainMultiplier, 0, maxDirtiness);
        if (depressionBarActive) currentDepression = Mathf.Clamp(currentDepression - trashDepressionReduction, 0, maxDepression);
        if (sleepBarActive) currentSleep = Mathf.Clamp(currentSleep + 0.5f, 0f, maxSleep); // +0.5 tiredness per trash cleaned
    }

    public void CleanSelf()
    {
        if (dirtinessBarActive) currentDirtiness = Mathf.Clamp(currentDirtiness - cleanDirtinessReduction, 0, maxDirtiness);
        if (depressionBarActive) currentDepression = Mathf.Clamp(currentDepression - cleanDepressionReduction, 0, maxDepression);
        if (sleepBarActive) AddTiredness(8f); // shower / clean
        OnShoweredEvent?.Invoke();
    }
}
