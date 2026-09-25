using UnityEngine;
using TMPro;
using System.Collections;

[RequireComponent(typeof(TextMeshProUGUI))]
public class MoneyPopup : MonoBehaviour
{
    private TextMeshProUGUI textMesh;
    private RectTransform rectTransform;

    [Header("Movement Settings")]
    public float totalDuration = 1.0f;
    public Vector2 floatOffset = new Vector2(0f, 100f);

    [Header("Scale Values")]
    [Tooltip("How big it gets at the peak of the initial pop")]
    public float peakScale = 1.2f;
    [Tooltip("The normal size it settles to after the pop")]
    public float normalScale = 1.0f;

    [Header("Timing Phases (Fractions of Total Duration)")]
    [Tooltip("Fraction of time spent scaling up from 0 to Peak (e.g. 0.15 = 15%)")]
    public float popInFraction = 0.15f;
    [Tooltip("Fraction of time spent dropping from Peak to Normal Scale")]
    public float settleFraction = 0.15f;
    [Tooltip("Fraction of time spent shrinking to 0 and fading out at the very end")]
    public float fadeOutFraction = 0.30f;

    private void Awake()
    {
        textMesh = GetComponent<TextMeshProUGUI>();
        rectTransform = GetComponent<RectTransform>();

        // Lock starting positions to the parent canvas anchor point
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.zero;
    }

    public void AnimatePopup(decimal amount)
    {
        textMesh.text = $"+${amount:0.00}";
        StartCoroutine(AnimateRoutine());
    }

    private IEnumerator AnimateRoutine()
    {
        float timer = 0f;
        Vector2 startPos = rectTransform.anchoredPosition;
        Vector2 endPos = startPos + floatOffset;
        Color textColor = textMesh.color;

        // Calculate absolute times in seconds based on your fractions
        float popInTime = totalDuration * popInFraction;
        float settleTime = totalDuration * settleFraction;
        float fadeTime = totalDuration * fadeOutFraction;

        // The remaining time is how long it hangs on screen at normal scale
        float hangTime = totalDuration - (popInTime + settleTime + fadeTime);

        while (timer < totalDuration)
        {
            timer += Time.deltaTime;

            // 1. Float upwards constantly over the entire duration
            float normalizedTime = timer / totalDuration;
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, normalizedTime);

            // 2. Scale & Alpha Phase Logic
            float currentScale = 0f;
            float currentAlpha = 1f;

            if (timer < popInTime)
            {
                // Phase 1: Pop In (Scale 0 -> Peak, Alpha 0 -> 1)
                float t = timer / popInTime;
                currentScale = Mathf.Lerp(0f, peakScale, t);
                currentAlpha = Mathf.Lerp(0f, 1f, t);
            }
            else if (timer < popInTime + settleTime)
            {
                // Phase 2: Settle (Scale Peak -> Normal, Alpha 1)
                float t = (timer - popInTime) / settleTime;
                currentScale = Mathf.Lerp(peakScale, normalScale, t);
                currentAlpha = 1f;
            }
            else if (timer < popInTime + settleTime + hangTime)
            {
                // Phase 3: Hang (Scale Normal, Alpha 1)
                currentScale = normalScale;
                currentAlpha = 1f;
            }
            else
            {
                // Phase 4: Fade Out (Scale Normal -> 0, Alpha 1 -> 0)
                float t = (timer - (popInTime + settleTime + hangTime)) / fadeTime;
                currentScale = Mathf.Lerp(normalScale, 0f, t);
                currentAlpha = Mathf.Lerp(1f, 0f, t);
            }

            // Apply calculated values
            rectTransform.localScale = Vector3.one * currentScale;
            textColor.a = currentAlpha;
            textMesh.color = textColor;

            yield return null;
        }

        Destroy(gameObject);
    }
}