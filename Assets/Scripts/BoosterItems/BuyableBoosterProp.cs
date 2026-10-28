using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(InteractableObject))]
public class BuyableBoosterProp : MonoBehaviour
{
    private enum State { Idle, Holding, Returning, Purchased }

    [Header("Booster Info & UI")]
    public string propName = "New Prop";
    public float price = 25f;
    [TextArea(2, 4)]
    public string effectDescription = "Increases Money per Bounce by 25%";
    public string holdPrompt = "Hold Click to Buy";
    public string notEnoughMoneyPrompt = "Not enough money!";

    [Header("Target Replacement")]
    [Tooltip("Drag the hidden (SetActive false) prop with the real materials & BoosterObject script here.")]
    public GameObject unlockedPropObject;
    [Tooltip("Drag your DVDLogoBouncer here (or leave empty to auto-find).")]
    public DVDLogoBouncer economyBouncer;

    [Header("Hold Settings")]
    [Tooltip("Seconds the player must hold left click to buy this prop.")]
    public float holdDuration = 1.5f;

    [Header("Levitate Settings")]
    [Tooltip("How high the prop rises while held (set to 0 if you want heavy furniture to stay grounded).")]
    public float levitateHeight = 0.25f;
    [Tooltip("How fast it swooshes back to its spot when released early.")]
    public float returnDuration = 0.2f;

    [Header("Shake Settings")]
    public float shakeIntensity = 0.04f;
    public float shakeSpeed = 40f;

    [Header("Audio")]
    [Tooltip("Loops while the prop is being held.")]
    public AudioClip shakeSound;
    [Range(0f, 1f)] public float shakeVolume = 1f;
    [Tooltip("Played when the purchase completes.")]
    public AudioClip purchasePoofSound;
    [Range(0f, 1f)] public float purchasePoofVolume = 1f;
    [Tooltip("Optional sound played if clicked without enough money.")]
    public AudioClip cantAffordSound;

    [Header("Poof Effect")]
    [Tooltip("Smoke/sparkle particle prefab spawned when bought.")]
    public ParticleSystem poofParticlePrefab;
    public Vector3 poofPositionOffset = Vector3.zero;
    public Vector3 poofRotationOffset = Vector3.zero;
    public Vector3 poofScale = Vector3.one;
    public bool offsetsRelativeToProp = true;

    private InteractableObject interactable;
    private InteractVisualizer visualizer;
    private AudioSource audioSource;
    private AudioSource shakeSource;
    private Collider[] colliders;
    private Renderer[] renderers;

    private State state = State.Idle;
    private float holdProgress;
    private Vector3 shakeAnchor;
    private Vector3 heldTarget;
    private Vector3 returnStart;
    private float returnElapsed;
    private Coroutine denyMessageRoutine;

    public bool IsPurchased => state == State.Purchased;

    private void Awake()
    {
        if (RunSaveManager.HasInstance && (RunSaveManager.Instance.IsPermanentBoosterBroken(gameObject)
            || (unlockedPropObject != null && RunSaveManager.Instance.IsPermanentBoosterBroken(unlockedPropObject))))
        {
            if (unlockedPropObject != null) unlockedPropObject.SetActive(false);
            gameObject.SetActive(false);
            return;
        }

        interactable = GetComponent<InteractableObject>();
        visualizer = GetComponent<InteractVisualizer>();
        colliders = GetComponentsInChildren<Collider>();
        renderers = GetComponentsInChildren<Renderer>();

        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();

        // Ensure the replacement object starts hidden if assigned (unless already restored by RunSaveManager)
        if (unlockedPropObject != null && unlockedPropObject.activeSelf && state != State.Purchased)
        {
            unlockedPropObject.SetActive(false);
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        shakeSource = gameObject.AddComponent<AudioSource>();
        shakeSource.playOnAwake = false;
        shakeSource.loop = true;
        shakeSource.spatialBlend = 0f;
        shakeSource.clip = shakeSound;
        shakeSource.volume = shakeVolume;

        UpdateHoverText();
        interactable.onClick.AddListener(BeginHold);
    }

    private void OnDestroy()
    {
        if (interactable != null) interactable.onClick.RemoveListener(BeginHold);
    }

    private void OnValidate()
    {
        // Updates the text in the Inspector automatically when you tweak values
        InteractableObject io = GetComponent<InteractableObject>();
        if (io != null)
        {
            io.hoverText = FormatHoverText();
        }
    }

    public void UpdateHoverText()
    {
        if (interactable != null)
        {
            interactable.hoverText = FormatHoverText();
        }
    }

    /// <summary>
    /// Restores a permanent buyable prop as already purchased when returning to a scene via <see cref="RunSaveManager"/>.
    /// </summary>
    public void RestoreAsPurchasedFromSave(bool applyBoosterStats = false)
    {
        if (RunSaveManager.HasInstance && (RunSaveManager.Instance.IsPermanentBoosterBroken(gameObject)
            || (unlockedPropObject != null && RunSaveManager.Instance.IsPermanentBoosterBroken(unlockedPropObject))))
        {
            if (unlockedPropObject != null) unlockedPropObject.SetActive(false);
            gameObject.SetActive(false);
            return;
        }

        state = State.Purchased;
        holdProgress = 0f;

        if (unlockedPropObject != null)
        {
            if (unlockedPropObject.transform.IsChildOf(transform))
            {
                unlockedPropObject.transform.SetParent(transform.parent, true);
            }

            if (!applyBoosterStats)
            {
                BoosterObject booster = unlockedPropObject.GetComponent<BoosterObject>();
                if (booster != null)
                {
                    booster.MarkAsAlreadyAppliedFromSave();
                }
            }

            unlockedPropObject.SetActive(true);
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Called by <see cref="TimedConsumableBooster"/> when a 1-minute consumable finishes
    /// so the unused buyable prop reappears and can be purchased again.
    /// </summary>
    public void ResetForRepurchase()
    {
        gameObject.SetActive(true);
        state = State.Idle;
        holdProgress = 0f;

        if (colliders == null || colliders.Length == 0) colliders = GetComponentsInChildren<Collider>();
        if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            if (r != null && !r.name.StartsWith("Glow_"))
            {
                r.enabled = true;
            }
        }
        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = true;
        }

        if (interactable != null)
        {
            interactable.hideHoverText = false;
            UpdateHoverText();
        }

        if (visualizer != null)
        {
            visualizer.enablePeriodicGlow = true;
            visualizer.SetIntensity(0f);
        }
    }

    private string FormatHoverText()
    {
        string text = $"{propName} - ${price:0.00}";
        if (!string.IsNullOrWhiteSpace(effectDescription))
        {
            text += $"\n{effectDescription}";
        }
        if (!string.IsNullOrWhiteSpace(holdPrompt))
        {
            text += $"\n({holdPrompt})";
        }
        return text;
    }

    private void Update()
    {
        switch (state)
        {
            case State.Holding:
                if (!IsStillHeld())
                {
                    BeginReturn();
                    return;
                }

                holdProgress += Time.deltaTime / Mathf.Max(0.01f, holdDuration);
                ApplyHoldMotion(holdProgress);

                if (holdProgress >= 1f)
                {
                    TryCompletePurchase();
                }
                break;

            case State.Returning:
                UpdateReturn();
                break;
        }
    }

    private void BeginHold()
    {
        if (state != State.Idle) return;

        // Check if the player has enough money before starting the hold
        if (economyBouncer != null && economyBouncer.currentMoney < (decimal)price)
        {
            if (cantAffordSound != null) audioSource.PlayOneShot(cantAffordSound);
            if (denyMessageRoutine != null) StopCoroutine(denyMessageRoutine);
            denyMessageRoutine = StartCoroutine(ShowNotEnoughMoneyFeedback());
            return;
        }

        state = State.Holding;
        holdProgress = 0f;
        shakeAnchor = transform.position;
        heldTarget = shakeAnchor + Vector3.up * levitateHeight;

        interactable.hideHoverText = true;
        if (shakeSound != null) shakeSource.Play();
    }

    private IEnumerator ShowNotEnoughMoneyFeedback()
    {
        interactable.hoverText = $"{propName} - ${price:0.00}\n{notEnoughMoneyPrompt}";
        yield return new WaitForSeconds(1.2f);
        UpdateHoverText();
        denyMessageRoutine = null;
    }

    private bool IsStillHeld()
    {
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    private void ApplyHoldMotion(float progress)
    {
        float clamped = Mathf.Clamp01(progress);
        float ease = Mathf.SmoothStep(0f, 1f, clamped);
        Vector3 basePosition = Vector3.Lerp(shakeAnchor, heldTarget, ease);

        float strength = shakeIntensity * Mathf.Lerp(0.4f, 1f, clamped);
        float t = Time.time * shakeSpeed;
        Vector3 shake = new Vector3(
            Mathf.PerlinNoise(t, 0f) * 2f - 1f,
            Mathf.PerlinNoise(0f, t) * 2f - 1f,
            Mathf.PerlinNoise(t, t) * 2f - 1f) * strength;

        transform.position = basePosition + shake;

        if (visualizer != null) visualizer.SetIntensity(clamped);
    }

    private void BeginReturn()
    {
        state = State.Returning;
        holdProgress = 0f;
        returnElapsed = 0f;
        returnStart = transform.position;

        if (visualizer != null) visualizer.SetIntensity(0f);

        interactable.hideHoverText = false;
        shakeSource.Stop();
    }

    private void UpdateReturn()
    {
        returnElapsed += Time.deltaTime;
        float t = Mathf.SmoothStep(0f, 1f, returnElapsed / Mathf.Max(0.01f, returnDuration));
        transform.position = Vector3.Lerp(returnStart, shakeAnchor, t);

        if (returnElapsed >= returnDuration)
        {
            transform.position = shakeAnchor;
            state = State.Idle;
        }
    }

    private void TryCompletePurchase()
    {
        decimal cost = (decimal)price;

        // Spend the money through DVDLogoBouncer
        if (economyBouncer != null && !economyBouncer.TrySpendMoney(cost))
        {
            BeginReturn();
            if (denyMessageRoutine != null) StopCoroutine(denyMessageRoutine);
            denyMessageRoutine = StartCoroutine(ShowNotEnoughMoneyFeedback());
            return;
        }

        state = State.Purchased;
        transform.position = shakeAnchor;

        if (visualizer != null) visualizer.HideImmediate();
        shakeSource.Stop();

        // Enable the real colored object with the BoosterObject modifier script
        if (unlockedPropObject != null)
        {
            // Safeguard: if the unlocked prop was parented under the grayed-out prop, unparent it first
            if (unlockedPropObject.transform.IsChildOf(transform))
            {
                unlockedPropObject.transform.SetParent(transform.parent, true);
            }

            // Record substance consumed if this buyable prop is a consumable substance
            TimedConsumableBooster consumable = unlockedPropObject.GetComponent<TimedConsumableBooster>();
            if (consumable != null && RunSaveManager.HasInstance)
            {
                RunSaveManager.Instance.RecordSubstanceConsumed(consumable.effectType);
            }

            unlockedPropObject.SetActive(true);
        }

        StartCoroutine(PurchasePoofRoutine());
    }

    private ParticleSystem SpawnPoofEffect()
    {
        if (poofParticlePrefab == null) return null;

        Vector3 position;
        Quaternion rotation;

        if (offsetsRelativeToProp)
        {
            position = transform.TransformPoint(poofPositionOffset);
            rotation = transform.rotation * Quaternion.Euler(poofRotationOffset);
        }
        else
        {
            position = transform.position + poofPositionOffset;
            rotation = Quaternion.Euler(poofRotationOffset);
        }

        ParticleSystem smoke = Instantiate(poofParticlePrefab, position, rotation);
        smoke.transform.localScale = Vector3.Scale(poofParticlePrefab.transform.localScale, poofScale);
        smoke.Play();
        return smoke;
    }

    private IEnumerator PurchasePoofRoutine()
    {
        // Hide the grayed-out mesh and disable colliders immediately so the colored prop is revealed
        foreach (Renderer r in renderers)
        {
            if (r != null) r.enabled = false;
        }
        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = false;
        }

        ParticleSystem smoke = SpawnPoofEffect();

        if (purchasePoofSound != null)
        {
            audioSource.PlayOneShot(purchasePoofSound, purchasePoofVolume);
        }

        float soundEnd = purchasePoofSound != null ? Time.time + purchasePoofSound.length : 0f;
        while ((smoke != null && smoke.IsAlive(true)) || Time.time < soundEnd)
        {
            yield return null;
        }

        if (smoke != null) Destroy(smoke.gameObject);
        gameObject.SetActive(false);
    }
}
