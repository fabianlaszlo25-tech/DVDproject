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
    public float hungerFillRate = 2f;
    public float sleepFillRate = 1.5f;
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
    public float trashDirtinessIncrease = 1f;
    public float trashDepressionReduction = 1f;

    [Header("Action Impacts: Depression Reductions")]
    public float eatDepressionReduction = 5f;
    public float toiletDepressionReduction = 2f;
    public float sleepDepressionReduction = 10f;
    public float cleanDepressionReduction = 15f;

    [Header("Economy & Inflation")]
    public float baseFoodCost = 10f;
    [Tooltip("Rate at which prices inflate per dollar of all-time money earned.")]
    public float inflationRatePerDollar = 0.00025f;
    private decimal currentFoodCost = 0m;
    private float currentInflationMultiplier = 1f;

    [Header("Depression From Money (Tuning & Testing)")]
    [Tooltip("Rate at which money increases depression fill speed. Set to 0 in Inspector to completely disable money scaling during testing! (e.g. 0.00005 = +5% depression rate per $1,000; 0 = disabled).")]
    public float moneyDepressionRatePerDollar = 0.00005f;
    [Tooltip("Maximum multiplier money can scale depression speed to. Prevents instant runaway depression when holding large funds.")]
    public float maxMoneyDepressionMultiplier = 2.5f;
    [Tooltip("Current active depression multiplier driven by money (visible in real-time while testing).")]
    public float currentMoneyDepressionMultiplier = 1f;

    [Header("Penalty Settings")]
    public float penaltyMultiplier = 2f;

    private bool hasTriggeredDepressionEnding = false;
    private float lastDisplayedInflationPercent = -1f;
    private decimal lastDisplayedFoodCost = -1m;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            // Update reference if a new instance loaded
            Instance = this;
        }
    }

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

        UpdateUI();
    }

    void Update()
    {
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

        currentHunger = Mathf.Clamp(currentHunger + (hungerFillRate * Time.deltaTime), 0, maxHunger);
        currentSleep = Mathf.Clamp(currentSleep + (sleepFillRate * Time.deltaTime), 0, maxSleep);

        bool isHungerFull = currentHunger >= maxHunger;
        bool isNeedsFull = currentNeeds >= maxNeeds;
        bool isSleepFull = currentSleep >= maxSleep;
        bool isDirtinessFull = currentDirtiness >= maxDirtiness;

        float moneyFloat = economyBouncer != null ? (float)economyBouncer.currentMoney : 0f;
        currentMoneyDepressionMultiplier = Mathf.Clamp(1f + (moneyFloat * moneyDepressionRatePerDollar), 1f, maxMoneyDepressionMultiplier);
        float depressionSpeed = baseDepressionFillRate * currentMoneyDepressionMultiplier;

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
            foodCostText.text = $"Food Cost: ${currentFoodCost:0.00}";
        }
    }

    public decimal GetCurrentFoodCost()
    {
        return currentFoodCost;
    }

    public float CurrentInflationMultiplier => currentInflationMultiplier;

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
        currentNeeds = Mathf.Clamp(currentNeeds + needsIncrease, 0, maxNeeds);
        currentDirtiness = Mathf.Clamp(currentDirtiness + dirtinessIncrease, 0, maxDirtiness);
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
        currentDirtiness = Mathf.Clamp(currentDirtiness + toiletDirtinessIncrease, 0, maxDirtiness);
        currentDepression = Mathf.Clamp(currentDepression - toiletDepressionReduction, 0, maxDepression);
    }

    public void GoToSleep()
    {
        currentSleep = Mathf.Clamp(currentSleep - sleepReduction, 0, maxSleep);
        currentDirtiness = Mathf.Clamp(currentDirtiness + sleepDirtinessIncrease, 0, maxDirtiness);
        currentDepression = Mathf.Clamp(currentDepression - sleepDepressionReduction, 0, maxDepression);
    }

    public void CleanTrashItem()
    {
        currentDirtiness = Mathf.Clamp(currentDirtiness + trashDirtinessIncrease, 0, maxDirtiness);
        currentDepression = Mathf.Clamp(currentDepression - trashDepressionReduction, 0, maxDepression);
    }

    public void CleanSelf()
    {
        currentDirtiness = Mathf.Clamp(currentDirtiness - cleanDirtinessReduction, 0, maxDirtiness);
        currentDepression = Mathf.Clamp(currentDepression - cleanDepressionReduction, 0, maxDepression);
    }
}
