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
/// Slowly fades the chosen fullscreen effect IN, holds it for 1 minute (60s), slowly fades it OUT,
/// reverts the temporary stat modifiers on BoosterObject, and switches back to the unused BuyableBoosterProp!
/// Also exposes active state queries so <see cref="DynamicMusicManager"/> knows which effects are currently active.
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

    /// <summary>Returns true if the specified consumable effect is currently active above a threshold.</summary>
    public static bool IsEffectActive(ConsumableVisualEffectType type, float minThreshold = 0.05f)
    {
        return ActiveEffectIntensities.TryGetValue(type, out float val) && val >= minThreshold;
    }

    /// <summary>Returns the current 0..1 intensity of the specified consumable effect.</summary>
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

        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
        }
        cycleRoutine = StartCoroutine(ConsumableLifecycleRoutine());
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }

    private IEnumerator ConsumableLifecycleRoutine()
    {
        // 1. Slowly fade IN
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

        // 2. Hold for 1 minute (60 seconds)
        float hold = Mathf.Max(0f, activeHoldDuration);
        float holdElapsed = 0f;
        while (holdElapsed < hold)
        {
            holdElapsed += GetDeltaTime();
            SetEffectIntensityImmediate(peakIntensity);
            yield return null;
        }

        // 3. Slowly fade OUT
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

        // 4. Revert stats & switch back to unused BuyableBoosterProp
        ResetToUnusedProp();
    }

    private void SetEffectIntensityImmediate(float intensity)
    {
        ActiveEffectIntensities[effectType] = intensity;

        switch (effectType)
        {
            case ConsumableVisualEffectType.Drunk:
                if (drunkEffectController != null) drunkEffectController.SetExternalIntensity(intensity);
                break;

            case ConsumableVisualEffectType.Caffeinated:
                if (caffeineEffectController != null) caffeineEffectController.SetExternalIntensity(intensity);
                break;

            case ConsumableVisualEffectType.High:
                if (highEffectController != null) highEffectController.SetExternalIntensity(intensity);
                break;

            case ConsumableVisualEffectType.Nicotined:
                if (nicotineEffectController != null) nicotineEffectController.SetExternalIntensity(intensity);
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