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
/// Coordinates automatically with <see cref="SubstanceCombinationManager"/> for 2-effect and 3-effect combos!
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
    [Tooltip("Choose Drunk, Caffeinated, High, or Nicotined for this prop.")]
    public ConsumableVisualEffectType effectType = ConsumableVisualEffectType.Drunk;
    [Range(0f, 1f)]
    [Tooltip("Peak screen effect intensity reached after fading in.")]
    public float peakIntensity = 1.0f;

    [Header("Timing (Seconds)")]
    [Tooltip("How many seconds it takes to slowly fade the fullscreen effect IN.")]
    public float fadeInDuration = 4.0f;
    [Tooltip("How long the effect stays at full strength (60 = 1 minute).")]
    public float activeHoldDuration = 60.0f;
    [Tooltip("How many seconds it takes to slowly fade the fullscreen effect OUT before resetting.")]
    public float fadeOutDuration = 5.0f;
    [Tooltip("If true, uses real-world unscaled time so GameSpeed changes don't shorten the 1-minute duration.")]
    public bool useUnscaledTime = true;

    [Header("Prop Reset References (Auto-Found if left empty)")]
    [Tooltip("The unused grayed-out BuyableBoosterProp that spawns this object.")]
    public BuyableBoosterProp unusedBuyableProp;
    [Tooltip("The BoosterObject on this active prop (auto-found on this GameObject).")]
    public BoosterObject activeBoosterObject;

    [Header("Screen Effect Controllers (Auto-Created if not in scene)")]
    public DrunkScreenEffect drunkEffectController;
    public CaffeineScreenEffect caffeineEffectController;
    public HighScreenEffect highEffectController;
    public NicotineScreenEffect nicotineEffectController;

    [Header("Auto-Trigger")]
    [Tooltip("If true, starts the 1-minute cycle automatically whenever this unlocked prop is enabled.")]
    public bool startAutomaticallyOnEnable = true;

    private Coroutine cycleRoutine;
    private float lastInternalIntensity;

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
        SetEffectIntensityImmediate(0f);

        if (SubstanceCombinationManager.Instance != null)
        {
            SubstanceCombinationManager.Instance.UnregisterConsumable(this);
        }
    }

    [ContextMenu("Start Consumable Cycle")]
    public void StartConsumableCycle()
    {
        FindUnusedBuyablePropIfNeeded();
        EnsureEffectControllerExists();

        if (activeBoosterObject != null)
        {
            activeBoosterObject.ApplyBooster();
        }

        SubstanceCombinationManager.Instance.RegisterConsumable(this);

        if (SubstanceCombinationManager.Instance.IsOverdoseSequenceRunning)
        {
            return;
        }

        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
        }
        cycleRoutine = StartCoroutine(ConsumableLifecycleRoutine());
    }

    public void ForceTerminateAndResetToUnused()
    {
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

    private IEnumerator ConsumableLifecycleRoutine()
    {
        float fadeIn = Mathf.Max(0.01f, fadeInDuration);
        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += GetDeltaTime();
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeIn));
            SetEffectIntensityImmediate(t * peakIntensity);
            yield return null;
        }

        SetEffectIntensityImmediate(peakIntensity);

        float hold = Mathf.Max(0f, activeHoldDuration);
        float holdElapsed = 0f;
        while (holdElapsed < hold)
        {
            holdElapsed += GetDeltaTime();
            SetEffectIntensityImmediate(peakIntensity);
            yield return null;
        }

        float fadeOut = Mathf.Max(0.01f, fadeOutDuration);
        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += GetDeltaTime();
            float t = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(elapsed / fadeOut));
            SetEffectIntensityImmediate(t * peakIntensity);
            yield return null;
        }

        SetEffectIntensityImmediate(0f);
        cycleRoutine = null;

        if (SubstanceCombinationManager.Instance != null)
        {
            SubstanceCombinationManager.Instance.UnregisterConsumable(this);
        }
        ResetToUnusedProp();
    }

    private void SetEffectIntensityImmediate(float intensity)
    {
        lastInternalIntensity = intensity;
        ActiveEffectIntensities[effectType] = intensity;

        bool suppressSingle = SubstanceCombinationManager.Instance != null
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