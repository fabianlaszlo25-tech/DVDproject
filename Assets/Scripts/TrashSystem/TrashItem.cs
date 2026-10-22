using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lives on every trash prefab. Handles its own hold-to-clear behaviour:
/// shake while held, then poof (particles + sound) and finally destroy itself.
/// It knows nothing about bars or spawning; it just announces <see cref="Cleared"/>.
/// </summary>
[RequireComponent(typeof(InteractableObject))]
public class TrashItem : MonoBehaviour
{
    private enum State { Idle, Holding, Returning, Poofing }

    [Header("Hold Settings")]
    [Tooltip("Seconds the player must hold left click to clear this trash.")]
    public float holdDuration = 1f;
    public string hoverMessage = "Hold to clear trash";

    [Header("Levitate Settings")]
    [Tooltip("How high the trash rises while held.")]
    public float levitateHeight = 0.4f;
    [Tooltip("How fast it swooshes back to its spot when released.")]
    public float returnDuration = 0.2f;

    [Header("Shake Settings")]
    public float shakeIntensity = 0.04f;
    public float shakeSpeed = 40f;

    [Header("Shake Sound")]
    [Tooltip("Loops while the trash is being held, stops on release or clear.")]
    public AudioClip shakeSound;
    [Range(0f, 1f)] public float shakeVolume = 1f;

    [Header("Poof Effect")]
    [Tooltip("Smoke particle prefab. It is spawned at the trash position when cleared.")]
    public ParticleSystem poofParticlePrefab;
    public AudioClip poofSound;
    [Range(0f, 1f)] public float poofVolume = 1f;

    [Header("Poof Effect Offsets")]
    [Tooltip("Position offset of the smoke from the trash pivot.")]
    public Vector3 poofPositionOffset = Vector3.zero;
    [Tooltip("Rotation offset of the smoke, in degrees (euler angles).")]
    public Vector3 poofRotationOffset = Vector3.zero;
    [Tooltip("Multiplies the smoke prefab's own scale.")]
    public Vector3 poofScale = Vector3.one;
    [Tooltip("On: offsets are in the trash's local space (they turn with the trash). Off: offsets are in world space.")]
    public bool offsetsRelativeToTrash = true;

    /// <summary>Raised once, the moment the trash is cleared by the player.</summary>
    public event Action<TrashItem> Cleared;

    private InteractableObject interactable;
    private AudioSource audioSource;      // one-shot poof
    private AudioSource shakeSource;      // looping shake
    private Collider[] colliders;
    private InteractVisualizer visualizer; // optional glow, drives from hold progress
    private Renderer[] renderers;

    private State state = State.Idle;
    private float holdProgress;
    private Vector3 shakeAnchor;   // where the trash rests on the floor
    private Vector3 heldTarget;    // where it ends up when fully held
    private Vector3 returnStart;
    private float returnElapsed;

    private void Awake()
    {
        interactable = GetComponent<InteractableObject>();
        colliders = GetComponentsInChildren<Collider>();
        renderers = GetComponentsInChildren<Renderer>();
        visualizer = GetComponent<InteractVisualizer>();

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

        interactable.hoverText = hoverMessage;
        interactable.onClick.AddListener(BeginHold);
    }

    private void OnDestroy()
    {
        if (interactable != null) interactable.onClick.RemoveListener(BeginHold);
    }

    /// <summary>World-space bounds of the visible trash (falls back to colliders). Used for placement.</summary>
    public Bounds GetWorldBounds()
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);

        foreach (Renderer r in renderers)
        {
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!hasBounds)
        {
            foreach (Collider c in colliders)
            {
                if (!hasBounds) { bounds = c.bounds; hasBounds = true; }
                else bounds.Encapsulate(c.bounds);
            }
        }

        return bounds;
    }

    /// <summary>Lets the placer switch colliders off while it searches for a spot, so the item can't block its own rays.</summary>
    public void SetCollidersEnabled(bool enabled)
    {
        foreach (Collider c in colliders) c.enabled = enabled;
    }

    private void Update()
    {
        switch (state)
        {
            case State.Holding:
                // Only letting go of the button releases it, the cursor may wander off freely.
                if (!IsStillHeld())
                {
                    BeginReturn();
                    return;
                }

                holdProgress += Time.deltaTime / holdDuration;
                ApplyHoldMotion(holdProgress);

                if (holdProgress >= 1f) Clear();
                break;

            case State.Returning:
                UpdateReturn();
                break;
        }
    }

    // --- Hold handling ---

    private void BeginHold()
    {
        if (state != State.Idle) return;

        state = State.Holding;
        holdProgress = 0f;
        shakeAnchor = transform.position;
        heldTarget = shakeAnchor + Vector3.up * levitateHeight;

        interactable.hideHoverText = true;
        if (shakeSound != null) shakeSource.Play();
    }

    private bool IsStillHeld()
    {
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    private void ApplyHoldMotion(float progress)
    {
        // Slow, smooth levitation upward...
        float ease = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        Vector3 basePosition = Vector3.Lerp(shakeAnchor, heldTarget, ease);

        // ...with a shake that grows as the hold completes.
        float strength = shakeIntensity * Mathf.Lerp(0.4f, 1f, progress);
        float t = Time.time * shakeSpeed;
        Vector3 shake = new Vector3(
            Mathf.PerlinNoise(t, 0f) * 2f - 1f,
            Mathf.PerlinNoise(0f, t) * 2f - 1f,
            Mathf.PerlinNoise(t, t) * 2f - 1f) * strength;

        transform.position = basePosition + shake;

        if (visualizer != null) visualizer.SetIntensity(progress);
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

    // --- Clearing ---

    private void Clear()
    {
        state = State.Poofing;
        if (visualizer != null) visualizer.HideImmediate();
        shakeSource.Stop();
        Cleared?.Invoke(this);
        StartCoroutine(PoofRoutine());
    }

    private ParticleSystem SpawnPoofEffect()
    {
        if (poofParticlePrefab == null) return null;

        Vector3 position;
        Quaternion rotation;

        if (offsetsRelativeToTrash)
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

    private IEnumerator PoofRoutine()
    {
        // Hide and disable the trash immediately, it only lingers to finish effects.
        foreach (Renderer r in renderers) r.enabled = false;
        foreach (Collider c in colliders) c.enabled = false;

        ParticleSystem smoke = SpawnPoofEffect();

        if (poofSound != null) audioSource.PlayOneShot(poofSound, poofVolume);

        // Wait for both the smoke and the sound to finish.
        float soundEnd = poofSound != null ? Time.time + poofSound.length : 0f;
        while ((smoke != null && smoke.IsAlive(true)) || Time.time < soundEnd)
        {
            yield return null;
        }

        if (smoke != null) Destroy(smoke.gameObject);
        Destroy(gameObject);
    }
}