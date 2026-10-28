using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(InteractableObject))]
public class BreakablePermanentBooster : MonoBehaviour
{
    private enum State { Idle, Holding, Returning, Breaking }

    [Header("Hold Settings")]
    [Tooltip("Seconds the player must hold left click to break this object.")]
    public float holdDuration = 1.0f;
    public string hoverMessage = "Hold to break";

    [Header("Levitate Settings (0 = no floating)")]
    public float levitateHeight = 0f;
    public float returnDuration = 0.2f;

    [Header("Shake Settings")]
    public float shakeIntensity = 0.04f;
    public float shakeSpeed = 40f;

    [Header("Audio")]
    public AudioClip shakeSound;
    [Range(0f, 1f)] public float shakeVolume = 1f;
    public AudioClip poofSound;
    [Range(0f, 1f)] public float poofVolume = 1f;

    [Header("Poof Effect")]
    public ParticleSystem poofParticlePrefab;
    public Vector3 poofPositionOffset = Vector3.zero;
    public Vector3 poofRotationOffset = Vector3.zero;
    public Vector3 poofScale = Vector3.one;
    public bool offsetsRelativeToProp = true;

    [Header("Booster to Revert")]
    public BoosterObject boosterObject;

    private InteractableObject interactable;
    private AudioSource audioSource;
    private AudioSource shakeSource;
    private Collider[] colliders;
    private Renderer[] renderers;
    private InteractVisualizer visualizer;

    private State state = State.Idle;
    private float holdProgress;
    private Vector3 shakeAnchor;
    private Vector3 heldTarget;
    private Vector3 returnStart;
    private float returnElapsed;

    public bool IsBroken => state == State.Breaking;

    private void Awake()
    {
        if (RunSaveManager.HasInstance && RunSaveManager.Instance.IsPermanentBoosterBroken(gameObject))
        {
            gameObject.SetActive(false);
            return;
        }

        interactable = GetComponent<InteractableObject>();
        colliders = GetComponentsInChildren<Collider>();
        renderers = GetComponentsInChildren<Renderer>();
        visualizer = GetComponent<InteractVisualizer>();

        if (boosterObject == null) boosterObject = GetComponent<BoosterObject>();

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
                    Break();
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

    private void Break()
    {
        state = State.Breaking;
        transform.position = shakeAnchor;

        if (visualizer != null) visualizer.HideImmediate();
        shakeSource.Stop();

        // Record in RunSaveManager that this booster was permanently broken for the rest of the run!
        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.RecordPermanentBoosterBroken(gameObject);
        }

        // Revert the booster so the stats return to normal!
        if (boosterObject != null)
        {
            boosterObject.RevertBooster();
        }

        StartCoroutine(BreakPoofRoutine());
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

    private IEnumerator BreakPoofRoutine()
    {
        foreach (Renderer r in renderers)
        {
            if (r != null) r.enabled = false;
        }
        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = false;
        }

        ParticleSystem smoke = SpawnPoofEffect();

        if (poofSound != null)
        {
            audioSource.PlayOneShot(poofSound, poofVolume);
        }

        float soundEnd = poofSound != null ? Time.time + poofSound.length : 0f;
        while ((smoke != null && smoke.IsAlive(true)) || Time.time < soundEnd)
        {
            yield return null;
        }

        if (smoke != null) Destroy(smoke.gameObject);
        Destroy(gameObject);
    }
}
