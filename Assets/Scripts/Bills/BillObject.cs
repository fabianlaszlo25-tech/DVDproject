using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum BillType
{
    FoodMoreExpensive,  // Food costs x2
    DvdBouncesSlower,   // DVD 50% speed
    ToiletBroken,       // Toilet unusable
    ShowerBroken,       // Shower unusable
    CarPayment          // Impounded car: cannot go to the Bar
}

[RequireComponent(typeof(InteractableObject))]
public class BillObject : MonoBehaviour
{
    private enum State { Idle, Holding, Returning, Paid }

    [Header("Bill Configuration")]
    public BillType billType;
    public string billTitle = "Overdue Bill";
    public float basePrice = 20f;
    [TextArea(2, 3)]
    public string penaltyDescription = "Doubles food costs until paid!";
    public string holdPrompt = "Hold Click to Pay";
    public string notEnoughMoneyPrompt = "Not enough money!";

    [Header("Hold Settings")]
    [Tooltip("Seconds required to hold click to pay this bill.")]
    public float holdDuration = 1.2f;
    public float levitateHeight = 0.06f;
    public float returnDuration = 0.2f;

    [Header("Shake Settings")]
    public float shakeIntensity = 0.025f;
    public float shakeSpeed = 35f;

    [Header("Audio")]
    public AudioClip shakeSound;
    [Range(0f, 1f)] public float shakeVolume = 0.8f;
    public AudioClip paidSound;
    [Range(0f, 1f)] public float paidVolume = 1.0f;
    public AudioClip cantAffordSound;

    [Header("Visual Effects")]
    public ParticleSystem poofParticlePrefab;
    public DVDLogoBouncer economyBouncer;

    private InteractableObject interactable;
    private InteractVisualizer visualizer;
    private AudioSource audioSource;
    private AudioSource shakeSource;

    private State state = State.Idle;
    private float holdProgress = 0f;
    private Vector3 shakeAnchor;
    private Vector3 heldTarget;
    private Vector3 returnStart;
    private float returnElapsed = 0f;
    private Coroutine denyRoutine;

    public bool IsPaid => state == State.Paid;

    private void Awake()
    {
        shakeAnchor = transform.position;
        interactable = GetComponent<InteractableObject>();
        visualizer = GetComponent<InteractVisualizer>();

        if (visualizer == null)
        {
            visualizer = gameObject.AddComponent<InteractVisualizer>();
        }

        // Flashes bright red periodically
        visualizer.glowColor = new Color(1.8f, 0.15f, 0.15f, 1f);
        visualizer.enablePeriodicGlow = true;
        visualizer.periodicWait = 1.5f;
        visualizer.periodicDuration = 1.2f;
        visualizer.thickness = 0.035f;

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

        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();

        UpdateHoverText();
        interactable.onClick.AddListener(BeginHold);
    }

    private void OnDestroy()
    {
        if (interactable != null) interactable.onClick.RemoveListener(BeginHold);
    }

    private void OnEnable()
    {
        state = State.Idle;
        holdProgress = 0f;
        transform.position = shakeAnchor;
        if (visualizer != null)
        {
            visualizer.enablePeriodicGlow = true;
            visualizer.SetIntensity(0f);
        }
        UpdateHoverText();
    }

    public decimal GetInflatedPrice()
    {
        float mult = BarManager.Instance != null ? BarManager.Instance.CurrentInflationMultiplier : 1f;
        return (decimal)(basePrice * mult);
    }

    public void UpdateHoverText()
    {
        if (interactable != null && state == State.Idle && denyRoutine == null)
        {
            decimal cost = GetInflatedPrice();
            interactable.hoverText = $"{billTitle} - ${cost:0.00}\n{penaltyDescription}\n({holdPrompt})";
        }
    }

    private void Update()
    {
        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();

        switch (state)
        {
            case State.Idle:
                if (interactable != null && !interactable.hideHoverText && denyRoutine == null)
                {
                    UpdateHoverText();
                }
                break;

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
                    CompletePayment();
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

        decimal cost = GetInflatedPrice();
        if (economyBouncer != null && economyBouncer.currentMoney < cost)
        {
            if (cantAffordSound != null) audioSource.PlayOneShot(cantAffordSound);
            if (denyRoutine != null) StopCoroutine(denyRoutine);
            denyRoutine = StartCoroutine(ShowNotEnoughMoneyFeedback());
            return;
        }

        state = State.Holding;
        holdProgress = 0f;
        shakeAnchor = transform.position;
        heldTarget = shakeAnchor + Vector3.up * levitateHeight;

        interactable.hideHoverText = true;
        if (shakeSound != null && shakeSource != null)
        {
            shakeSource.clip = shakeSound;
            shakeSource.volume = shakeVolume;
            shakeSource.Play();
        }
    }

    private IEnumerator ShowNotEnoughMoneyFeedback()
    {
        interactable.hoverText = $"{billTitle} - ${GetInflatedPrice():0.00}\n{notEnoughMoneyPrompt}";
        yield return new WaitForSeconds(1.5f);
        denyRoutine = null;
        UpdateHoverText();
    }

    private bool IsStillHeld()
    {
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    private void ApplyHoldMotion(float progress)
    {
        float clamped = Mathf.Clamp01(progress);
        float ease = Mathf.SmoothStep(0f, 1f, clamped);
        Vector3 basePos = Vector3.Lerp(shakeAnchor, heldTarget, ease);

        float strength = shakeIntensity * Mathf.Lerp(0.4f, 1f, clamped);
        float t = Time.time * shakeSpeed;
        Vector3 shake = new Vector3(
            Mathf.PerlinNoise(t, 0f) * 2f - 1f,
            Mathf.PerlinNoise(0f, t) * 2f - 1f,
            Mathf.PerlinNoise(t, t) * 2f - 1f) * strength;

        transform.position = basePos + shake;

        if (visualizer != null)
        {
            visualizer.SetIntensity(clamped);
        }
    }

    private void BeginReturn()
    {
        state = State.Returning;
        holdProgress = 0f;
        returnElapsed = 0f;
        returnStart = transform.position;

        if (visualizer != null)
        {
            visualizer.SetIntensity(0f);
            visualizer.enablePeriodicGlow = true;
        }

        interactable.hideHoverText = false;
        if (shakeSource != null) shakeSource.Stop();
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
            if (visualizer != null)
            {
                visualizer.ResetGlow(keepPeriodicGlow: true);
                visualizer.enablePeriodicGlow = true;
            }
        }
    }

    private void CompletePayment()
    {
        decimal cost = GetInflatedPrice();
        if (economyBouncer != null && !economyBouncer.TrySpendMoney(cost))
        {
            BeginReturn();
            if (denyRoutine != null) StopCoroutine(denyRoutine);
            denyRoutine = StartCoroutine(ShowNotEnoughMoneyFeedback());
            return;
        }

        state = State.Paid;
        transform.position = shakeAnchor;

        if (visualizer != null) visualizer.HideImmediate();
        if (shakeSource != null) shakeSource.Stop();

        if (poofParticlePrefab != null)
        {
            ParticleSystem ps = Instantiate(poofParticlePrefab, transform.position, Quaternion.identity);
            Destroy(ps.gameObject, 3f);
        }

        if (audioSource != null && paidSound != null)
        {
            AudioSource.PlayClipAtPoint(paidSound, transform.position, paidVolume);
        }

        if (BillManager.Instance != null)
        {
            BillManager.Instance.MarkBillPaid(billType);
        }

        gameObject.SetActive(false);
    }
}
