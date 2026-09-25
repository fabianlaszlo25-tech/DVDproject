using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class SliderMaxAlert : MonoBehaviour
{
    private Slider slider;
    private RectTransform rectTransform;
    private Vector2 originalPosition;

    [Header("Alert Settings")]
    public AudioSource audioSource;
    public AudioClip alertSound;

    [Header("Shake Settings")]
    [Tooltip("How far the UI element moves while shaking")]
    public float shakeIntensity = 5f;
    [Tooltip("How fast the UI element vibrates")]
    public float shakeSpeed = 30f;

    private bool isMaxed = false;

    void Start()
    {
        slider = GetComponent<Slider>();
        rectTransform = GetComponent<RectTransform>();
        originalPosition = rectTransform.anchoredPosition;

        // Auto-add an AudioSource if you forgot to attach one
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }
    }

    void Update()
    {
        // BarManager sets slider value from 0 to 1
        if (slider.value >= 1f)
        {
            if (!isMaxed)
            {
                isMaxed = true;
                if (alertSound != null && audioSource != null)
                {
                    audioSource.PlayOneShot(alertSound);
                }
            }

            // Generate random shake using Perlin Noise for smooth but erratic movement
            float offsetX = (Mathf.PerlinNoise(Time.time * shakeSpeed, 0f) * 2f - 1f) * shakeIntensity;
            float offsetY = (Mathf.PerlinNoise(0f, Time.time * shakeSpeed) * 2f - 1f) * shakeIntensity;

            rectTransform.anchoredPosition = originalPosition + new Vector2(offsetX, offsetY);
        }
        else
        {
            if (isMaxed)
            {
                // Reset state and lock position back to normal once the bar lowers
                isMaxed = false;
                rectTransform.anchoredPosition = originalPosition;
            }
        }
    }
}