using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BarManager : MonoBehaviour
{
    [Header("References")]
    public DVDLogoBouncer economyBouncer;

    [Header("UI Sliders")]
    public Slider hungerSlider;
    public Slider needsSlider;
    public Slider sleepSlider;
    public Slider dirtinessSlider;
    public Slider depressionSlider;

    [Header("UI Text")]
    public TMP_Text inflationText;
    public TMP_Text foodCostText;

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

    [Header("Action Impacts: Depression Reductions")]
    public float eatDepressionReduction = 5f;
    public float toiletDepressionReduction = 2f;
    public float sleepDepressionReduction = 10f;
    public float cleanDepressionReduction = 15f;

    [Header("Economy & Inflation")]
    public float baseFoodCost = 10f;
    public float inflationRatePerDollar = 0.01f;
    private decimal currentFoodCost = 0m;
    private float currentInflationMultiplier = 1f;

    [Header("Penalty Settings")]
    public float penaltyMultiplier = 2f;

    void Start()
    {
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
        float moneyFloat = (float)economyBouncer.currentMoney;
        currentInflationMultiplier = 1f + (moneyFloat * inflationRatePerDollar);
        currentFoodCost = (decimal)(baseFoodCost * currentInflationMultiplier);
    }

    private void ProcessBars()
    {
        currentHunger = Mathf.Clamp(currentHunger + (hungerFillRate * Time.deltaTime), 0, maxHunger);
        currentSleep = Mathf.Clamp(currentSleep + (sleepFillRate * Time.deltaTime), 0, maxSleep);

        bool isHungerFull = currentHunger >= maxHunger;
        bool isNeedsFull = currentNeeds >= maxNeeds;
        bool isSleepFull = currentSleep >= maxSleep;
        bool isDirtinessFull = currentDirtiness >= maxDirtiness;

        float depressionSpeed = baseDepressionFillRate * currentInflationMultiplier;

        if (isHungerFull) depressionSpeed *= penaltyMultiplier;
        if (isNeedsFull) depressionSpeed *= penaltyMultiplier;
        if (isSleepFull) depressionSpeed *= penaltyMultiplier;
        if (isDirtinessFull) depressionSpeed *= penaltyMultiplier;

        currentDepression = Mathf.Clamp(currentDepression + (depressionSpeed * Time.deltaTime), 0, maxDepression);

        if (currentDepression >= maxDepression)
        {
            currentDepression = maxDepression - 0.01f;
        }
    }

    private void UpdateUI()
    {
        if (hungerSlider) hungerSlider.value = currentHunger / maxHunger;
        if (needsSlider) needsSlider.value = currentNeeds / maxNeeds;
        if (sleepSlider) sleepSlider.value = currentSleep / maxSleep;
        if (dirtinessSlider) dirtinessSlider.value = currentDirtiness / maxDirtiness;
        if (depressionSlider) depressionSlider.value = currentDepression / maxDepression;

        if (inflationText) inflationText.text = $"Inflation: {(currentInflationMultiplier * 100f):0}%";
        if (foodCostText) foodCostText.text = $"Food Cost: ${currentFoodCost:0.00}";
    }

    public decimal GetCurrentFoodCost()
    {
        return currentFoodCost;
    }

    // --- INTERACTION METHODS ---

    public void TryEatFood()
    {
        if (economyBouncer.TrySpendMoney(currentFoodCost))
        {
            currentHunger = Mathf.Clamp(currentHunger - foodHungerReduction, 0, maxHunger);
            currentNeeds = Mathf.Clamp(currentNeeds + foodNeedsIncrease, 0, maxNeeds); // Restored!
            currentDirtiness = Mathf.Clamp(currentDirtiness + eatDirtinessIncrease, 0, maxDirtiness);
            currentDepression = Mathf.Clamp(currentDepression - eatDepressionReduction, 0, maxDepression);
        }
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

    public void CleanSelf()
    {
        currentDirtiness = Mathf.Clamp(currentDirtiness - cleanDirtinessReduction, 0, maxDirtiness);
        currentDepression = Mathf.Clamp(currentDepression - cleanDepressionReduction, 0, maxDepression);
    }
}