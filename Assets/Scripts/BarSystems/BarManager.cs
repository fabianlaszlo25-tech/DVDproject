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
    public float hungerFillRate = 1f;
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

    [Header("Economy & Inflation")]
    public float baseFoodCost = 10f;
    [Tooltip("Rate at which prices inflate per dollar of all-time money earned (e.g. 0.0001 = 0.01% per $1, 0.00025 = 0.025% per $1, 0.001 = 0.1% per $1).")]
    public float inflationRatePerDollar = 0.0001f;
    [Tooltip("Inflation increase (in percent) can never be more than this fraction of the money gained (0.5 = 50%).")]
    [Range(0.05f, 1f)]
    public float maxInflationPercentOfMoneyGained = 0.5f;
    private decimal currentFoodCost = 0m;
    private float currentInflationMultiplier = 1f;

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

    private bool hasTriggeredDepressionEnding = false;
    private float lastDisplayedInflationPercent = -1f;
    private decimal lastDisplayedFoodCost = -1m;

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
    private void ApplyBalanceTuning()
    {
        sleepFillRate = Mathf.Min(sleepFillRate, 0.05f);
        trashDepressionReduction = 2.5f;
        trashDirtinessIncrease = 1.17f;
        sleepDepressionReduction = 3f;
        cleanDepressionReduction = 25f;
        eatDepressionReduction = 10f;
        currentMoneyDepressionMultiplier = 1f;
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
        if (amount <= 0f || hasTriggeredDepressionEnding) return;
        currentSleep = Mathf.Clamp(currentSleep + amount * TirednessChunkMultiplier, 0, maxSleep);
    }

    public void ReduceTiredness(float amount)
    {
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

        UpdateUI();
    }

    void Update()
    {
        if (RunSaveManager.HasInstance && RunSaveManager.Instance.isGoodDayActive) return;

        ProcessInflation();
        ProcessBars();
        UpdateUI();
    }

    private void ProcessInflation()
    {
        if (economyBouncer == null) return;
        // Monotonic inflation: based strictly on all-time money earned so spending money never lowers inflation
        float allTimeMoneyFloat = (float)economyBouncer.AllTimeMoneyEarned;
        currentInflationMultiplier = 1f + (allTimeMoneyFloat * inflationRatePerDollar);

        // Safety cap: the inflation increase (shown as "+X% Inflation") can never exceed
        // maxInflationPercentOfMoneyGained (50%) of the money gained, so stacked permanent boosters
        // can never make prices run away from what the player earns.
        float maxInflationPercent = Mathf.Max(0f, allTimeMoneyFloat * maxInflationPercentOfMoneyGained);
        float maxMultiplier = 1f + (maxInflationPercent / 100f);
        if (currentInflationMultiplier > maxMultiplier) currentInflationMultiplier = maxMultiplier;

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

        currentHunger = Mathf.Clamp(currentHunger + (hungerFillRate * HungerSpeedMultiplier * Time.deltaTime), 0, maxHunger);
        currentSleep = Mathf.Clamp(currentSleep + (sleepFillRate * Time.deltaTime), 0, maxSleep);

        bool isHungerFull = currentHunger >= maxHunger;
        bool isNeedsFull = currentNeeds >= maxNeeds;
        bool isSleepFull = currentSleep >= maxSleep;
        bool isDirtinessFull = currentDirtiness >= maxDirtiness;

        currentMoneyDepressionMultiplier = 1f;
        float depressionSpeed = baseDepressionFillRate;

        if (isHungerFull) depressionSpeed *= penaltyMultiplier;
        if (isNeedsFull) depressionSpeed *= penaltyMultiplier;
        if (isSleepFull) depressionSpeed *= penaltyMultiplier;
        if (isDirtinessFull) depressionSpeed *= penaltyMultiplier;

        currentDepression = Mathf.Clamp(currentDepression + (depressionSpeed * Time.deltaTime), 0, maxDepression);

        // When Depression reaches 100%, lock it at max and trigger the Heart-Attack Breakdown -> Cut to Black -> Gun Cocking -> UI Popup!
        if (currentDepression >= maxDepression)
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

        float percentOverBase = (currentInflationMultiplier - 1f) * 100f;
        if (inflationText && (Mathf.Abs(percentOverBase - lastDisplayedInflationPercent) > 0.005f || lastDisplayedInflationPercent < 0f))
        {
            lastDisplayedInflationPercent = percentOverBase;
            inflationText.text = $"+{percentOverBase:0.00}% Inflation";
        }

        if (foodCostText && currentFoodCost != lastDisplayedFoodCost)
        {
            lastDisplayedFoodCost = currentFoodCost;
            foodCostText.text = $"Food Cost: {CurrencyFormatter.Format(currentFoodCost)}";
        }
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
        if (currentHunger >= maxHunger - 0.01f) barName = "hungry";
        else if (currentNeeds >= maxNeeds - 0.01f) barName = "desperate for the toilet";
        else if (currentDirtiness >= maxDirtiness - 0.01f) barName = "filthy";
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
        if (currentNeeds >= maxNeeds - 0.01f)
        {
            return false;
        }

        if (economyBouncer != null && !economyBouncer.TrySpendMoney(cost))
        {
            return false;
        }

        currentHunger = Mathf.Clamp(currentHunger - hungerReduction, 0, maxHunger);
        currentNeeds = Mathf.Clamp(currentNeeds + needsIncrease * NeedsGainMultiplier, 0, maxNeeds);
        currentDirtiness = Mathf.Clamp(currentDirtiness + dirtinessIncrease * DirtinessGainMultiplier, 0, maxDirtiness);
        AddTiredness(4f); // eating
        currentDepression = Mathf.Clamp(currentDepression - depressionReduction, 0, maxDepression);
        return true;
    }

    public void TryEatFood()
    {
        TryEatFood(currentFoodCost, foodHungerReduction, foodNeedsIncrease, eatDirtinessIncrease, eatDepressionReduction);
    }

    public void UseToilet()
    {
        currentNeeds = Mathf.Clamp(currentNeeds - toiletNeedsReduction, 0, maxNeeds);
        currentDirtiness = Mathf.Clamp(currentDirtiness + toiletDirtinessIncrease * DirtinessGainMultiplier, 0, maxDirtiness);
        AddTiredness(3f); // toilet
        currentDepression = Mathf.Clamp(currentDepression - toiletDepressionReduction, 0, maxDepression);
    }

    public void GoToSleep()
    {
        currentSleep = Mathf.Clamp(currentSleep - sleepReduction, 0, maxSleep);
        currentDirtiness = Mathf.Clamp(currentDirtiness + sleepDirtinessIncrease * DirtinessGainMultiplier, 0, maxDirtiness);
        currentDepression = Mathf.Clamp(currentDepression - sleepDepressionReduction, 0, maxDepression);
    }

    public bool CanCleanTrash => currentDirtiness < maxDirtiness;

    public void CleanTrashItem()
    {
        if (!CanCleanTrash) return;

        currentDirtiness = Mathf.Clamp(currentDirtiness + trashDirtinessIncrease * DirtinessGainMultiplier, 0, maxDirtiness);
        currentDepression = Mathf.Clamp(currentDepression - trashDepressionReduction, 0, maxDepression);
    }

    public void CleanSelf()
    {
        currentDirtiness = Mathf.Clamp(currentDirtiness - cleanDirtinessReduction, 0, maxDirtiness);
        currentDepression = Mathf.Clamp(currentDepression - cleanDepressionReduction, 0, maxDepression);
        AddTiredness(8f); // shower / clean
    }
}
