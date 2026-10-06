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

        // Capture the camera's normal FOV from the scene, then immediately snap it to the zoomed-in state
        if (targetCamera != null)
        {
            originalFOV = targetCamera.fieldOfView;
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

    private IEnumerator TransitionRoutine()
    {
        ToggleInput(false);
        Color fadeColor = fadeImage != null ? fadeImage.color : Color.black;

        float elapsedTime = 0f;
        float returnStartTime = zoomOutTime * (1f - scaleSpeedFraction);

        while (elapsedTime < zoomOutTime)
        {
            elapsedTime += Time.deltaTime;
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