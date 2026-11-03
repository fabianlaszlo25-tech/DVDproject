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
    [Tooltip("Seconds the clearing animation plays out before poofing.")]
    public float holdDuration = 1.5f;
    public string hoverMessage = "Click to clear trash";

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

    private Transform constraintTransform;
    private BoxCollider constraintBox;
    private bool hasAreaConstraint = false;
    private Rigidbody rb;
    private float settleTimer = 0f;
    private bool isSettled = false;

    public bool IsClearedOrPoofing => state == State.Holding || state == State.Poofing;

    public void SetSpawnAreaConstraint(Transform areaTrans, BoxCollider areaBox)
    {
        constraintTransform = areaTrans;
        constraintBox = areaBox;
        hasAreaConstraint = (constraintTransform != null || constraintBox != null);
    }

    public void SetSettledImmediate()
    {
        isSettled = true;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

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

        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearDamping = 3.5f;
            rb.angularDamping = 4.0f;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.maxDepenetrationVelocity = 0.8f;
        }
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

    private void FixedUpdate()
    {
        if (state == State.Holding || state == State.Poofing) return;

        // Once settled, lock kinematic to completely eliminate physics vibration and jitter
        if (isSettled)
        {
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            return;
        }

        settleTimer += Time.fixedDeltaTime;

        // Settle window: allow dynamic falling for ~1.0s. Once velocity drops low, lock early.
        if (settleTimer >= 1.0f || (settleTimer >= 0.35f && rb != null && rb.linearVelocity.sqrMagnitude < 0.02f))
        {
            isSettled = true;
            if (hasAreaConstraint && (constraintTransform != null || constraintBox != null))
            {
                transform.position = ClampPositionToArea(transform.position);
            }
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            return;
        }

        if (hasAreaConstraint && (constraintTransform != null || constraintBox != null))
        {
            Vector3 currentPos = transform.position;
            Vector3 clampedPos = ClampPositionToArea(currentPos);

            if ((clampedPos - currentPos).sqrMagnitude > 0.0001f)
            {
                transform.position = clampedPos;

                if (rb != null)
                {
                    Vector3 vel = rb.linearVelocity;
                    vel.x *= 0.1f;
                    vel.z *= 0.1f;
                    rb.linearVelocity = vel;
                }
            }

            // Suppress physics explosion speed between dense items while settling
            if (rb != null)
            {
                if (rb.linearVelocity.sqrMagnitude > 4f)
                {
                    rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, 2.0f);
                }
                if (rb.angularVelocity.sqrMagnitude > 16f)
                {
                    rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, 4.0f);
                }
            }
        }
    }

    private Vector3 ClampPositionToArea(Vector3 worldPos)
    {
        Transform t = constraintTransform != null ? constraintTransform : (constraintBox != null ? constraintBox.transform : null);
        if (t == null) return worldPos;

        Vector3 local = t.InverseTransformPoint(worldPos);
        Vector3 scale = t.lossyScale;

        if (constraintBox != null)
        {
            Vector3 center = constraintBox.center;
            Vector3 half = constraintBox.size * 0.5f;
            float padX = 0.04f / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float padZ = 0.04f / Mathf.Max(0.001f, Mathf.Abs(scale.z));
            padX = Mathf.Min(padX, half.x * 0.4f);
            padZ = Mathf.Min(padZ, half.z * 0.4f);

            local.x = Mathf.Clamp(local.x, center.x - half.x + padX, center.x + half.x - padX);
            local.z = Mathf.Clamp(local.z, center.z - half.z + padZ, center.z + half.z - padZ);
            if (local.y < center.y - half.y) local.y = center.y - half.y;
            if (local.y > center.y + half.y) local.y = center.y + half.y;
        }
        else
        {
            float padX = 0.04f / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float padZ = 0.04f / Mathf.Max(0.001f, Mathf.Abs(scale.z));
            padX = Mathf.Min(padX, 0.2f);
            padZ = Mathf.Min(padZ, 0.2f);

            local.x = Mathf.Clamp(local.x, -0.5f + padX, 0.5f - padX);
            local.z = Mathf.Clamp(local.z, -0.5f + padZ, 0.5f - padZ);
            if (local.y < -0.5f) local.y = -0.5f;
            if (local.y > 0.5f) local.y = 0.5f;
        }

        return t.TransformPoint(local);
    }

    private void Update()
    {
        switch (state)
        {
            case State.Holding:
                // Once started, the 1.5s clearing animation plays out to completion without requiring hold
                holdProgress += Time.deltaTime / Mathf.Max(0.01f, holdDuration);
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

        // Cannot clean trash if at max dirtiness
        if (BarManager.Instance != null && !BarManager.Instance.CanCleanTrash) return;

        state = State.Holding;
        holdProgress = 0f;
        shakeAnchor = transform.position;
        heldTarget = shakeAnchor + Vector3.up * levitateHeight;

        interactable.hideHoverText = true;
        if (interactable != null) interactable.enabled = false;
        SetCollidersEnabled(false); // disable colliders so raycasts pass through to trash behind/underneath

        if (rb != null)
        {
            rb.isKinematic = true; // disable physics during levitation
        }

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

    /// <summary>
    /// Starts clearing this trash (used when the player clicks or swipes over it while holding left click).
    /// Starts the 1.5s levitate & shake animation, which automatically completes with a poof effect.
    /// </summary>
    public void SwipeClear()
    {
        if (state != State.Idle) return;
        if (BarManager.Instance != null && !BarManager.Instance.CanCleanTrash) return;
        BeginHold();
    }

    private void Clear()
    {
        state = State.Poofing;
        if (visualizer != null) visualizer.HideImmediate();
        shakeSource.Stop();

        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.RecordTrashCleaned();
        }

        bool hadSubscriber = Cleared != null;
        Cleared?.Invoke(this);

        // If this is a pre-placed TrashItem in the scene (not managed by a TrashManager),
        // also notify BarManager directly so cleaning it affects dirtiness/depression.
        if (!hadSubscriber)
        {
            BarManager bm = FindObjectOfType<BarManager>();
            if (bm != null) bm.CleanTrashItem();
        }

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
