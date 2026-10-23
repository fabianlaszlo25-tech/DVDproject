using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ConsumableVisualEffectType
{
    Drunk,
    Caffeinated,
    High,
    Nicotined
}

/// <summary>
/// Attach this to any consumable unlocked BoosterObject (Beer, Energy Drink, Weed/Joint, Cigarette/Vape).
/// - When used alone: Ramps in and unwinds at the exact same smooth rate as the combined effects!
/// - When combined: <see cref="SubstanceCombinationManager"/> locks both consumables together for 15 seconds,
///   unwinds the combined effect to 0, and resets both consumables together.
/// </summary>
public class TimedConsumableBooster : MonoBehaviour
{
    private static readonly Dictionary<ConsumableVisualEffectType, float> ActiveEffectIntensities =
        new Dictionary<ConsumableVisualEffectType, float>
        {
            { ConsumableVisualEffectType.Drunk, 0f },
            { ConsumableVisualEffectType.Caffeinated, 0f },
            { ConsumableVisualEffectType.High, 0f },
            { ConsumableVisualEffectType.Nicotined, 0f }
        };

    public static bool IsEffectActive(ConsumableVisualEffectType type, float minThreshold = 0.05f)
    {
        return ActiveEffectIntensities.TryGetValue(type, out float val) && val >= minThreshold;
    }

    public static float GetEffectIntensity(ConsumableVisualEffectType type)
    {
        return ActiveEffectIntensities.TryGetValue(type, out float val) ? val : 0f;
    }

    [Header("Effect Selection")]
    public ConsumableVisualEffectType effectType = ConsumableVisualEffectType.Drunk;
    [Range(0f, 1f)]
    public float peakIntensity = 1.0f;

    [Header("Timing & Smooth Unwind Speed")]
    [Tooltip("Speed at which the effect ramps in and unwinds when ending (0.35 matches the combined effects' ~3s unwind!).")]
    public float fadeSpeed = 0.35f;
    [Tooltip("How long the standalone effect stays active before unwinding (60 = 1 minute).")]
    public float activeHoldDuration = 60.0f;
    public bool useUnscaledTime = true;

    [Header("Prop Reset References (Auto-Found if left empty)")]
    public BuyableBoosterProp unusedBuyableProp;
    public BoosterObject activeBoosterObject;

    [Header("Screen Effect Controllers (Auto-Created if not in scene)")]
    public DrunkScreenEffect drunkEffectController;
    public CaffeineScreenEffect caffeineEffectController;
    public HighScreenEffect highEffectController;
    public NicotineScreenEffect nicotineEffectController;

    [Header("Auto-Trigger")]
    public bool startAutomaticallyOnEnable = true;

    private Coroutine cycleRoutine;
    private float lastInternalIntensity;
    private bool isLockedByCombination;

    public bool IsLockedByCombination => isLockedByCombination;

    private void Awake()
    {
        if (activeBoosterObject == null)
        {
            activeBoosterObject = GetComponent<BoosterObject>();
        }

        FindUnusedBuyablePropIfNeeded();
        EnsureEffectControllerExists();
    }

    private void OnEnable()
    {
        isLockedByCombination = false;
        if (startAutomaticallyOnEnable)
        {
            StartConsumableCycle();
        }
    }

    private void OnDisable()
    {
        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
            cycleRoutine = null;
        }
        isLockedByCombination = false;
        SetEffectIntensityImmediate(0f);

        if (SubstanceCombinationManager.HasInstance)
        {
            SubstanceCombinationManager.Instance.UnregisterConsumable(this);
        }
    }

    [ContextMenu("Start Consumable Cycle")]
    public void StartConsumableCycle()
    {
        FindUnusedBuyablePropIfNeeded();
        EnsureEffectControllerExists();
        isLockedByCombination = false;

        if (activeBoosterObject != null)
        {
            activeBoosterObject.ApplyBooster();
        }

        SubstanceCombinationManager.Instance.RegisterConsumable(this);

        if (isLockedByCombination || SubstanceCombinationManager.Instance.IsOverdoseSequenceRunning)
        {
            return;
        }

        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
        }
        cycleRoutine = StartCoroutine(StandaloneLifecycleRoutine());
    }

    public void LockIntoCombinedCycle()
    {
        isLockedByCombination = true;
        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
            cycleRoutine = null;
        }
        SetEffectIntensityImmediate(peakIntensity);
    }

    public void ForceTerminateAndResetToUnused()
    {
        isLockedByCombination = false;
        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
            cycleRoutine = null;
        }

        SetEffectIntensityImmediate(0f);
        ResetToUnusedProp();
    }

    public void RefreshVisualSuppression()
    {
        SetEffectIntensityImmediate(lastInternalIntensity);
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }

    private IEnumerator StandaloneLifecycleRoutine()
    {
        float spd = Mathf.Max(0.05f, fadeSpeed);
        float current = 0f;

        // 1. Smoothly ramp IN using MoveTowards (same curve as SubstanceCombinationManager)
        while (current < peakIntensity - 0.001f)
        {
            if (isLockedByCombination) yield break;
            current = Mathf.MoveTowards(current, peakIntensity, spd * GetDeltaTime());
            SetEffectIntensityImmediate(current);
            yield return null;
        }
        SetEffectIntensityImmediate(peakIntensity);

        // 2. Hold for activeHoldDuration
        float hold = Mathf.Max(0f, activeHoldDuration);
        float holdElapsed = 0f;
        while (holdElapsed < hold)
        {
            if (isLockedByCombination) yield break;
            holdElapsed += GetDeltaTime();
            SetEffectIntensityImmediate(peakIntensity);
            yield return null;
        }

        // 3. Smoothly unwind to 0 using MoveTowards (exact same ending feel as the combined effects!)
        while (current > 0.001f)
        {
            if (isLockedByCombination) yield break;
            current = Mathf.MoveTowards(current, 0f, spd * GetDeltaTime());
            SetEffectIntensityImmediate(current);
            yield return null;
        }

        SetEffectIntensityImmediate(0f);
        cycleRoutine = null;

        if (SubstanceCombinationManager.HasInstance)
        {
            SubstanceCombinationManager.Instance.UnregisterConsumable(this);
        }
        ResetToUnusedProp();
    }

    private void SetEffectIntensityImmediate(float intensity)
    {
        lastInternalIntensity = intensity;
        ActiveEffectIntensities[effectType] = intensity;

        bool suppressSingle = SubstanceCombinationManager.HasInstance
                           && SubstanceCombinationManager.Instance.ShouldSuppressSingleScreenEffects();
        float visualIntensity = suppressSingle ? 0f : intensity;

        switch (effectType)
        {
            case ConsumableVisualEffectType.Drunk:
                if (drunkEffectController != null) drunkEffectController.SetExternalIntensity(visualIntensity);
                break;

            case ConsumableVisualEffectType.Caffeinated:
                if (caffeineEffectController != null) caffeineEffectController.SetExternalIntensity(visualIntensity);
                break;

            case ConsumableVisualEffectType.High:
                if (highEffectController != null) highEffectController.SetExternalIntensity(visualIntensity);
                break;

            case ConsumableVisualEffectType.Nicotined:
                if (nicotineEffectController != null) nicotineEffectController.SetExternalIntensity(visualIntensity);
                break;
        }
    }

    private void ResetToUnusedProp()
    {
        if (activeBoosterObject != null)
        {
            activeBoosterObject.RevertBooster();
        }

        if (unusedBuyableProp != null)
        {
            unusedBuyableProp.ResetForRepurchase();
        }

        gameObject.SetActive(false);
    }

    private void FindUnusedBuyablePropIfNeeded()
    {
        if (unusedBuyableProp != null) return;

        BuyableBoosterProp[] allBuyables = FindObjectsOfType<BuyableBoosterProp>(true);
        foreach (BuyableBoosterProp buyable in allBuyables)
        {
            if (buyable != null && buyable.unlockedPropObject == gameObject)
            {
                unusedBuyableProp = buyable;
                break;
            }
        }
    }

    private void EnsureEffectControllerExists()
    {
        switch (effectType)
        {
            case ConsumableVisualEffectType.Drunk:
                if (drunkEffectController == null) drunkEffectController = GetComponent<DrunkScreenEffect>();
                if (drunkEffectController == null) drunkEffectController = FindObjectOfType<DrunkScreenEffect>();
                if (drunkEffectController == null) drunkEffectController = gameObject.AddComponent<DrunkScreenEffect>();
                break;

            case ConsumableVisualEffectType.Caffeinated:
                if (caffeineEffectController == null) caffeineEffectController = GetComponent<CaffeineScreenEffect>();
                if (caffeineEffectController == null) caffeineEffectController = FindObjectOfType<CaffeineScreenEffect>();
                if (caffeineEffectController == null) caffeineEffectController = gameObject.AddComponent<CaffeineScreenEffect>();
                break;

            case ConsumableVisualEffectType.High:
                if (highEffectController == null) highEffectController = GetComponent<HighScreenEffect>();
                if (highEffectController == null) highEffectController = FindObjectOfType<HighScreenEffect>();
                if (highEffectController == null) highEffectController = gameObject.AddComponent<HighScreenEffect>();
                break;

            case ConsumableVisualEffectType.Nicotined:
                if (nicotineEffectController == null) nicotineEffectController = GetComponent<NicotineScreenEffect>();
                if (nicotineEffectController == null) nicotineEffectController = FindObjectOfType<NicotineScreenEffect>();
                if (nicotineEffectController == null) nicotineEffectController = gameObject.AddComponent<NicotineScreenEffect>();
                break;
        }
    }
}