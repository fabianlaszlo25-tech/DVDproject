using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Attach this to a Volume GameObject that only has the ACES Tonemapping override.
/// Because Unity's Tonemapping override only has a Mode dropdown (None / Neutral / ACES) and no strength slider,
/// this script locks the mode to ACES and drives the Volume's master blend weight (0..1) so you can smoothly
/// dial in the exact amount of ACES tonemapping in the Inspector or via script/UI!
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Volume))]
public class ACESToneMappingSlider : MonoBehaviour
{
    [Header("ACES Tonemapping Strength")]
    [Tooltip("0 = No ACES (ungraded), 1 = 100% full ACES Tonemapping.")]
    [Range(0f, 1f)]
    public float acesAmount = 1.0f;

    [Header("Optional Smooth Transition")]
    [Tooltip("If > 0, smoothly interpolates toward targetAmount when changed via SetTargetACESAmount().")]
    public float smoothSpeed = 0f;

    private Volume volume;
    private Tonemapping tonemapping;
    private float targetAmount = 1.0f;

    private void OnEnable()
    {
        EnsureVolumeAndACES();
        targetAmount = acesAmount;
        ApplyWeight();
    }

    private void OnValidate()
    {
        EnsureVolumeAndACES();
        targetAmount = acesAmount;
        ApplyWeight();
    }

    private void Update()
    {
        if (volume == null) EnsureVolumeAndACES();

        if (Application.isPlaying && smoothSpeed > 0.001f)
        {
            acesAmount = Mathf.MoveTowards(acesAmount, targetAmount, smoothSpeed * Time.unscaledDeltaTime);
        }

        ApplyWeight();
    }

    /// <summary>
    /// Call this from a UI Slider, UnityEvent, or script to set the ACES amount (0..1) immediately.
    /// </summary>
    public void SetACESAmount(float amount)
    {
        acesAmount = Mathf.Clamp01(amount);
        targetAmount = acesAmount;
        ApplyWeight();
    }

    /// <summary>
    /// Call this to smoothly fade the ACES amount to a new target value using <see cref="smoothSpeed"/>.
    /// </summary>
    public void SetTargetACESAmount(float amount)
    {
        targetAmount = Mathf.Clamp01(amount);
        if (smoothSpeed <= 0.001f)
        {
            acesAmount = targetAmount;
            ApplyWeight();
        }
    }

    private void EnsureVolumeAndACES()
    {
        if (volume == null) volume = GetComponent<Volume>();
        if (volume == null || volume.sharedProfile == null) return;

        if (volume.sharedProfile.TryGet(out tonemapping) && tonemapping != null)
        {
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;
        }
    }

    private void ApplyWeight()
    {
        if (volume == null) return;

        float clamped = Mathf.Clamp01(acesAmount);
        volume.weight = clamped;
    }
}