using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class SceneTransitionIn : MonoBehaviour
{
    [Header("Transition Settings")]
    public float zoomOutTime = 1.0f;

    [Header("Camera & Visuals")]
    public Camera targetCamera;
    public Image fadeImage;
    public float startingZoomFOV = 30f;

    [Header("UI Workarounds")]
    public RectTransform[] uiToScale;
    [Range(0.1f, 0.9f)] public float scaleSpeedFraction = 0.4f;
    public float uiExitScale = 3.0f;
    public GameObject[] uiToHide;

    [Header("Input Blocking")]
    public MonoBehaviour[] scriptsToDisable;

    private float originalFOV;

    private void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        // During the interactive tutorial opening, TutorialManager starts the camera locked onto the TV
        // with cursor and movement hidden. Perform a clean black fade-in only without overriding FOV or
        // re-enabling the cursor / movement scripts prematurely.
        if (TutorialManager.IsTutorialActive)
        {
            foreach (RectTransform ui in uiToScale) { if (ui != null) ui.localScale = Vector3.one; }
            ToggleHiddenUI(false);

            if (fadeImage != null)
            {
                Color c = fadeImage.color;
                c.a = 1f;
                fadeImage.color = c;
                fadeImage.raycastTarget = false;
            }

            StartCoroutine(TutorialFadeOnlyRoutine());
            return;
        }

        // Snap camera immediately to the player target at the restored location
        // BEFORE beginning the transition routine so the animation never renders at the wrong spawn point
        SnapCameraImmediate();

        // Capture the camera's normal FOV from the scene, then immediately snap it to the zoomed-in state
        if (targetCamera != null)
        {
            originalFOV = targetCamera.fieldOfView > startingZoomFOV + 1f ? targetCamera.fieldOfView : 65f;
            targetCamera.fieldOfView = startingZoomFOV;
        }

        // Snap fade image to fully black before the first frame renders
        if (fadeImage != null)
        {
            Color c = fadeImage.color;
            c.a = 1f;
            fadeImage.color = c;
            fadeImage.raycastTarget = true;
        }

        // Snap UI to scaled/hidden states
        foreach (RectTransform ui in uiToScale) { if (ui != null) ui.localScale = Vector3.one * uiExitScale; }
        ToggleHiddenUI(false);

        StartCoroutine(TransitionRoutine());
    }

    private IEnumerator TutorialFadeOnlyRoutine()
    {
        Color fadeColor = fadeImage != null ? fadeImage.color : Color.black;

        for (int settleFrames = 0; settleFrames < 2; settleFrames++)
        {
            yield return null;
        }

        float elapsedTime = 0f;
        float duration = Mathf.Max(0.25f, zoomOutTime);
        while (elapsedTime < duration)
        {
            elapsedTime += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float t = Mathf.Clamp01(elapsedTime / duration);

            if (fadeImage != null)
            {
                fadeColor.a = Mathf.Lerp(1f, 0f, t);
                fadeImage.color = fadeColor;
            }
            yield return null;
        }

        if (fadeImage != null)
        {
            fadeColor.a = 0f;
            fadeImage.color = fadeColor;
            fadeImage.raycastTarget = false;
        }
    }

    private void SnapCameraImmediate()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null)
        {
            SmoothCameraFollow camFollow = targetCamera.GetComponent<SmoothCameraFollow>();
            if (camFollow == null) camFollow = FindObjectOfType<SmoothCameraFollow>();
            if (camFollow != null)
            {
                camFollow.SnapToTargetImmediate();
            }
        }
    }

    private IEnumerator TransitionRoutine()
    {
        ToggleInput(false);
        Color fadeColor = fadeImage != null ? fadeImage.color : Color.black;

        // When returning from another scene (Bar / Dream) the freshly loaded scene restores saved state,
        // spawns objects and starts videos, which causes big frame hitches on its first frames.
        // Hold the black/zoomed state for a few settled frames so the animation is never skipped by those hitches,
        // and continually ensure the camera remains snapped to the restored target.
        for (int settleFrames = 0; settleFrames < 3; settleFrames++)
        {
            SnapCameraImmediate();
            yield return null;
        }

        float elapsedTime = 0f;
        float returnStartTime = zoomOutTime * (1f - scaleSpeedFraction);

        while (elapsedTime < zoomOutTime)
        {
            // Unscaled + clamped delta so a leftover timeScale or a load hitch can't swallow the animation.
            elapsedTime += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float t = elapsedTime / zoomOutTime;

            if (targetCamera != null) targetCamera.fieldOfView = Mathf.Lerp(startingZoomFOV, originalFOV, t);
            if (fadeImage != null)
            {
                fadeColor.a = Mathf.Lerp(1f, 0f, t);
                fadeImage.color = fadeColor;
            }

            float returnT = Mathf.Clamp01((elapsedTime - returnStartTime) / (zoomOutTime * scaleSpeedFraction));
            Vector3 returnScale = Vector3.Lerp(Vector3.one * uiExitScale, Vector3.one, returnT);
            foreach (RectTransform ui in uiToScale)
            {
                if (ui != null) ui.localScale = returnScale;
            }

            yield return null;
        }

        if (targetCamera != null) targetCamera.fieldOfView = originalFOV;
        if (fadeImage != null) { fadeColor.a = 0f; fadeImage.color = fadeColor; }
        foreach (RectTransform ui in uiToScale) { if (ui != null) ui.localScale = Vector3.one; }

        ToggleHiddenUI(true);
        ToggleInput(true);
        SnapCameraImmediate();
    }

    private void ToggleInput(bool enable)
    {
        foreach (MonoBehaviour script in scriptsToDisable) { if (script != null) script.enabled = enable; }
        if (EventSystem.current != null) EventSystem.current.enabled = enable;
        if (fadeImage != null) fadeImage.raycastTarget = !enable;
    }

    private void ToggleHiddenUI(bool enable)
    {
        foreach (GameObject ui in uiToHide) { if (ui != null) ui.SetActive(enable); }
    }
}
