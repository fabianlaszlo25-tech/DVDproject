using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum BoosterStat
{
    // --- Economy (DVDLogoBouncer) ---
    MoneyPerEdgeBounce,
    MoneyPerCornerBounce,
    MoneyAllBounces,
    BounceSpeed,

    // --- Bar Fill Rates (BarManager) ---
    HungerFillRate,
    SleepFillRate,
    BaseDepressionFillRate,

    // --- Bar Maximums (BarManager) ---
    MaxHunger,
    MaxNeeds,
    MaxSleep,
    MaxDirtiness,
    MaxDepression,

    // --- Action Reductions (BarManager) ---
    FoodHungerReduction,
    ToiletNeedsReduction,
    SleepReduction,
    CleanDirtinessReduction,

    // --- Action Dirtiness / Needs Increases (BarManager) ---
    EatDirtinessIncrease,
    ToiletDirtinessIncrease,
    SleepDirtinessIncrease,
    FoodNeedsIncrease,
    TrashDirtinessIncrease,

    // --- Action Depression Reductions (BarManager) ---
    EatDepressionReduction,
    ToiletDepressionReduction,
    SleepDepressionReduction,
    CleanDepressionReduction,
    TrashDepressionReduction,

    // --- Inflation & Penalties (BarManager) ---
    BaseFoodCost,
    InflationRatePerDollar,
    PenaltyMultiplier
}

public enum ModifierMode
{
    [Tooltip("Adds the value directly (use negative numbers to reduce a stat).")]
    FlatAdd,
    [Tooltip("Changes the stat by a percentage (e.g., 25 = +25%, -20 = -20%).")]
    PercentageChange,
    [Tooltip("Multiplies the stat directly (e.g., 1.5 = 1.5x, 0.5 = half).")]
    Multiply
}

[System.Serializable]
public class StatModifier
{
    public BoosterStat stat;
    public ModifierMode mode = ModifierMode.FlatAdd;
    public float value = 0f;
}

public class BoosterObject : MonoBehaviour
{
    [Header("References (Auto-filled if left empty)")]
    public DVDLogoBouncer economyBouncer;
    public BarManager barManager;

    [Header("Stat Modifiers")]
    [Tooltip("Add as many stat changes as you want this prop to grant when unlocked.")]
    public List<StatModifier> modifiers = new List<StatModifier>();

    [Header("Instant Bar Changes (Optional)")]
    [Tooltip("Immediate one-time change to current Depression when bought (negative reduces depression).")]
    public float instantDepressionChange = 0f;
    [Tooltip("Immediate one-time change to current Dirtiness when bought (negative cleans).")]
    public float instantDirtinessChange = 0f;

    [Header("Future Special Boosts")]
    [Tooltip("Hook custom scripts or special boost methods here later.")]
    public UnityEvent onBoosterActivated;

    private bool hasApplied = false;

    private void OnEnable()
    {
        // Because the colored prop starts SetActive(false) and is enabled when bought,
        // OnEnable automatically triggers and applies the boost once.
        if (!hasApplied)
        {
            ApplyBooster();
        }
    }

    public void ApplyBooster()
    {
        if (hasApplied) return;
        hasApplied = true;

        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();
        if (barManager == null) barManager = FindObjectOfType<BarManager>();

        foreach (StatModifier mod in modifiers)
        {
            ApplySingleModifier(mod);
        }

        if (barManager != null)
        {
            if (Mathf.Abs(instantDepressionChange) > 0.001f)
            {
                barManager.currentDepression = Mathf.Clamp(
                    barManager.currentDepression + instantDepressionChange,
                    0f,
                    barManager.maxDepression);
            }

            if (Mathf.Abs(instantDirtinessChange) > 0.001f)
            {
                barManager.currentDirtiness = Mathf.Clamp(
                    barManager.currentDirtiness + instantDirtinessChange,
                    0f,
                    barManager.maxDirtiness);
            }
        }

        onBoosterActivated?.Invoke();
    }

    private void ApplySingleModifier(StatModifier mod)
    {
        switch (mod.stat)
        {
            // --- DVDLogoBouncer Stats ---
            case BoosterStat.MoneyPerEdgeBounce:
                if (economyBouncer != null)
                    economyBouncer.edgeReward = CalculateDecimal(economyBouncer.edgeReward, mod);
                break;

            case BoosterStat.MoneyPerCornerBounce:
                if (economyBouncer != null)
                    economyBouncer.cornerReward = CalculateDecimal(economyBouncer.cornerReward, mod);
                break;

            case BoosterStat.MoneyAllBounces:
                if (economyBouncer != null)
                {
                    economyBouncer.edgeReward = CalculateDecimal(economyBouncer.edgeReward, mod);
                    economyBouncer.cornerReward = CalculateDecimal(economyBouncer.cornerReward, mod);
                }
                break;

            case BoosterStat.BounceSpeed:
                if (economyBouncer != null)
                    economyBouncer.baseSpeed = Mathf.Max(0.01f, CalculateFloat(economyBouncer.baseSpeed, mod));
                break;

            // --- BarManager Fill Rates ---
            case BoosterStat.HungerFillRate:
                if (barManager != null)
                    barManager.hungerFillRate = Mathf.Max(0f, CalculateFloat(barManager.hungerFillRate, mod));
                break;

            case BoosterStat.SleepFillRate:
                if (barManager != null)
                    barManager.sleepFillRate = Mathf.Max(0f, CalculateFloat(barManager.sleepFillRate, mod));
                break;

            case BoosterStat.BaseDepressionFillRate:
                if (barManager != null)
                    barManager.baseDepressionFillRate = Mathf.Max(0f, CalculateFloat(barManager.baseDepressionFillRate, mod));
                break;

            // --- BarManager Maximums ---
            case BoosterStat.MaxHunger:
                if (barManager != null)
                    barManager.maxHunger = Mathf.Max(1f, CalculateFloat(barManager.maxHunger, mod));
                break;

            case BoosterStat.MaxNeeds:
                if (barManager != null)
                    barManager.maxNeeds = Mathf.Max(1f, CalculateFloat(barManager.maxNeeds, mod));
                break;

            case BoosterStat.MaxSleep:
                if (barManager != null)
                    barManager.maxSleep = Mathf.Max(1f, CalculateFloat(barManager.maxSleep, mod));
                break;

            case BoosterStat.MaxDirtiness:
                if (barManager != null)
                    barManager.maxDirtiness = Mathf.Max(1f, CalculateFloat(barManager.maxDirtiness, mod));
                break;

            case BoosterStat.MaxDepression:
                if (barManager != null)
                    barManager.maxDepression = Mathf.Max(1f, CalculateFloat(barManager.maxDepression, mod));
                break;

            // --- BarManager Action Reductions ---
            case BoosterStat.FoodHungerReduction:
                if (barManager != null)
                    barManager.foodHungerReduction = Mathf.Max(0f, CalculateFloat(barManager.foodHungerReduction, mod));
                break;

            case BoosterStat.ToiletNeedsReduction:
                if (barManager != null)
                    barManager.toiletNeedsReduction = Mathf.Max(0f, CalculateFloat(barManager.toiletNeedsReduction, mod));
                break;

            case BoosterStat.SleepReduction:
                if (barManager != null)
                    barManager.sleepReduction = Mathf.Max(0f, CalculateFloat(barManager.sleepReduction, mod));
                break;

            case BoosterStat.CleanDirtinessReduction:
                if (barManager != null)
                    barManager.cleanDirtinessReduction = Mathf.Max(0f, CalculateFloat(barManager.cleanDirtinessReduction, mod));
                break;

            // --- BarManager Dirtiness / Needs Increases ---
            case BoosterStat.EatDirtinessIncrease:
                if (barManager != null)
                    barManager.eatDirtinessIncrease = Mathf.Max(0f, CalculateFloat(barManager.eatDirtinessIncrease, mod));
                break;

            case BoosterStat.ToiletDirtinessIncrease:
                if (barManager != null)
                    barManager.toiletDirtinessIncrease = Mathf.Max(0f, CalculateFloat(barManager.toiletDirtinessIncrease, mod));
                break;

            case BoosterStat.SleepDirtinessIncrease:
                if (barManager != null)
                    barManager.sleepDirtinessIncrease = Mathf.Max(0f, CalculateFloat(barManager.sleepDirtinessIncrease, mod));
                break;

            case BoosterStat.FoodNeedsIncrease:
                if (barManager != null)
                    barManager.foodNeedsIncrease = Mathf.Max(0f, CalculateFloat(barManager.foodNeedsIncrease, mod));
                break;

            case BoosterStat.TrashDirtinessIncrease:
                if (barManager != null)
                    barManager.trashDirtinessIncrease = Mathf.Max(0f, CalculateFloat(barManager.trashDirtinessIncrease, mod));
                break;

            // --- BarManager Depression Reductions ---
            case BoosterStat.EatDepressionReduction:
                if (barManager != null)
                    barManager.eatDepressionReduction = Mathf.Max(0f, CalculateFloat(barManager.eatDepressionReduction, mod));
                break;

            case BoosterStat.ToiletDepressionReduction:
                if (barManager != null)
                    barManager.toiletDepressionReduction = Mathf.Max(0f, CalculateFloat(barManager.toiletDepressionReduction, mod));
                break;

            case BoosterStat.SleepDepressionReduction:
                if (barManager != null)
                    barManager.sleepDepressionReduction = Mathf.Max(0f, CalculateFloat(barManager.sleepDepressionReduction, mod));
                break;

            case BoosterStat.CleanDepressionReduction:
                if (barManager != null)
                    barManager.cleanDepressionReduction = Mathf.Max(0f, CalculateFloat(barManager.cleanDepressionReduction, mod));
                break;

            case BoosterStat.TrashDepressionReduction:
                if (barManager != null)
                    barManager.trashDepressionReduction = Mathf.Max(0f, CalculateFloat(barManager.trashDepressionReduction, mod));
                break;

            // --- Inflation & Penalties ---
            case BoosterStat.BaseFoodCost:
                if (barManager != null)
                    barManager.baseFoodCost = Mathf.Max(0f, CalculateFloat(barManager.baseFoodCost, mod));
                break;

            case BoosterStat.InflationRatePerDollar:
                if (barManager != null)
                    barManager.inflationRatePerDollar = Mathf.Max(0f, CalculateFloat(barManager.inflationRatePerDollar, mod));
                break;

            case BoosterStat.PenaltyMultiplier:
                if (barManager != null)
                    barManager.penaltyMultiplier = Mathf.Max(1f, CalculateFloat(barManager.penaltyMultiplier, mod));
                break;
        }
    }

    private float CalculateFloat(float current, StatModifier mod)
    {
        switch (mod.mode)
        {
            case ModifierMode.FlatAdd:
                return current + mod.value;
            case ModifierMode.PercentageChange:
                return current * (1f + (mod.value / 100f));
            case ModifierMode.Multiply:
                return current * mod.value;
            default:
                return current;
        }
    }

    private decimal CalculateDecimal(decimal current, StatModifier mod)
    {
        switch (mod.mode)
        {
            case ModifierMode.FlatAdd:
                return System.Math.Max(0m, current + (decimal)mod.value);
            case ModifierMode.PercentageChange:
                return System.Math.Max(0m, current * (decimal)(1f + (mod.value / 100f)));
            case ModifierMode.Multiply:
                return System.Math.Max(0m, current * (decimal)mod.value);
            default:
                return current;
        }
    }
}