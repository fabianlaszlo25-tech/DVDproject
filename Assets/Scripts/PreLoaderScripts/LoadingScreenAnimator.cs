using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class LetterAnimation
{
    [Tooltip("The active letter you want to animate.")]
    public RectTransform letterRect;

    [Tooltip("The inactive duplicate placed at the final target position.")]
    public RectTransform endReferenceRect;

    [HideInInspector] public Vector2 startPosition;
    [HideInInspector] public Vector3 startScale;
    [HideInInspector] public Vector2 endPosition;
    [HideInInspector] public Vector3 endScale;
}

public class LoadingScreenAnimator : MonoBehaviour
{
    [Header("Warning Screen")]
    public CanvasGroup warningGroup;
    public float warningFadeSpeed = 2f;
    public float warningHoldTime = 2f;

    [Header("Theme Logo")]
    public CanvasGroup themeGroup;
    public float themeFadeSpeed = 2f;
    public float letterMoveDuration = 1.5f;
    public float themeHoldTime = 1f;
    public List<LetterAnimation> letters;

    [Header("DVD Logo")]
    public CanvasGroup dvdGroup;
    public RectTransform dvdLogoRect;
    public float dvdSpeed = 300f;
    private Vector2 dvdDirection = new Vector2(1f, 1f).normalized;
    private bool isDvdBouncing = false;
    private RectTransform canvasRect;

    [HideInInspector] public bool minimumAnimationFinished = false;

    void Start()
    {
        canvasRect = GetComponentInParent<Canvas>().GetComponent<RectTransform>();

        if (warningGroup) warningGroup.alpha = 0f;
        if (themeGroup) themeGroup.alpha = 0f;
        if (dvdGroup) dvdGroup.alpha = 0f;

        // Automatically capture the start and end states from the transforms in the scene
        foreach (var l in letters)
        {
            if (l.letterRect != null)
            {
                l.startPosition = l.letterRect.anchoredPosition;
                l.startScale = l.letterRect.localScale;
            }

            if (l.endReferenceRect != null)
            {
                l.endPosition = l.endReferenceRect.anchoredPosition;
                l.endScale = l.endReferenceRect.localScale;
            }
        }

        StartCoroutine(RunAnimationSequence());
    }

    void Update()
    {
        if (isDvdBouncing && dvdLogoRect != null && canvasRect != null)
        {
            dvdLogoRect.anchoredPosition += dvdDirection * dvdSpeed * Time.deltaTime;

            float halfWidth = dvdLogoRect.rect.width * dvdLogoRect.localScale.x / 2f;
            float halfHeight = dvdLogoRect.rect.height * dvdLogoRect.localScale.y / 2f;

            float canvasHalfWidth = canvasRect.rect.width / 2f;
            float canvasHalfHeight = canvasRect.rect.height / 2f;

            if (dvdLogoRect.anchoredPosition.x + halfWidth >= canvasHalfWidth || dvdLogoRect.anchoredPosition.x - halfWidth <= -canvasHalfWidth)
            {
                dvdDirection.x *= -1f;
                dvdLogoRect.anchoredPosition = new Vector2(Mathf.Clamp(dvdLogoRect.anchoredPosition.x, -canvasHalfWidth + halfWidth, canvasHalfWidth - halfWidth), dvdLogoRect.anchoredPosition.y);
            }

            if (dvdLogoRect.anchoredPosition.y + halfHeight >= canvasHalfHeight || dvdLogoRect.anchoredPosition.y - halfHeight <= -canvasHalfHeight)
            {
                dvdDirection.y *= -1f;
                dvdLogoRect.anchoredPosition = new Vector2(dvdLogoRect.anchoredPosition.x, Mathf.Clamp(dvdLogoRect.anchoredPosition.y, -canvasHalfHeight + halfHeight, canvasHalfHeight - halfHeight));
            }
        }
    }

    private IEnumerator RunAnimationSequence()
    {
        if (warningGroup)
        {
            yield return FadeGroup(warningGroup, 0f, 1f, warningFadeSpeed);
            yield return new WaitForSeconds(warningHoldTime);
            yield return FadeGroup(warningGroup, 1f, 0f, warningFadeSpeed);
        }

        if (themeGroup)
        {
            yield return FadeGroup(themeGroup, 0f, 1f, themeFadeSpeed);

            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime / letterMoveDuration;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                foreach (var l in letters)
                {
                    if (l.letterRect != null && l.endReferenceRect != null)
                    {
                        l.letterRect.anchoredPosition = Vector2.Lerp(l.startPosition, l.endPosition, smoothT);
                        l.letterRect.localScale = Vector3.Lerp(l.startScale, l.endScale, smoothT);
                    }
                }
                yield return null;
            }

            yield return new WaitForSeconds(themeHoldTime);
            yield return FadeGroup(themeGroup, 1f, 0f, themeFadeSpeed);
        }

        if (dvdGroup)
        {
            isDvdBouncing = true;
            yield return FadeGroup(dvdGroup, 0f, 1f, themeFadeSpeed);
        }

        minimumAnimationFinished = true;
    }

    private IEnumerator FadeGroup(CanvasGroup group, float start, float end, float speed)
    {
        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime * speed;
            group.alpha = Mathf.Lerp(start, end, t);
            yield return null;
        }
        group.alpha = end;
    }
}