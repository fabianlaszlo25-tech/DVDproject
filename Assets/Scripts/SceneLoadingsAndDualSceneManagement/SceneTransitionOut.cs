using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneTransitionOut : MonoBehaviour
{
    [Header("Transition Settings")]
    public string sceneToLoad;
    public float zoomInTime = 1.0f;

    [Header("Camera & Visuals")]
    public Camera targetCamera;
    public Image fadeImage;
    public float zoomTargetFOV = 30f;

    [Header("UI Workarounds")]
    public RectTransform[] uiToScale;
    [Range(0.1f, 0.9f)] public float scaleSpeedFraction = 0.4f;
    public float uiExitScale = 3.0f;
    public GameObject[] uiToHide;

    [Header("Input Blocking")]
    public MonoBehaviour[] scriptsToDisable;

    private bool isTransitioning = false;
    private float originalFOV;

    private void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null) originalFOV = targetCamera.fieldOfView;
    }

    public void TransitionToScene()
    {
        if (!isTransitioning)
        {
            StartCoroutine(TransitionRoutine());
        }
    }

    public void TransitionToScene(string sceneName)
    {
        sceneToLoad = sceneName;
        TransitionToScene();
    }

    private IEnumerator TransitionRoutine()
    {
        isTransitioning = true;
        ToggleInput(false);
        ToggleHiddenUI(false);

        Color fadeColor = fadeImage != null ? fadeImage.color : Color.black;

        float elapsedTime = 0f;
        while (elapsedTime < zoomInTime)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / zoomInTime;

            if (targetCamera != null) targetCamera.fieldOfView = Mathf.Lerp(originalFOV, zoomTargetFOV, t);
            if (fadeImage != null)
            {
                fadeColor.a = Mathf.Lerp(0f, 1f, t);
                fadeImage.color = fadeColor;
            }

            float scaleT = Mathf.Clamp01(elapsedTime / (zoomInTime * scaleSpeedFraction));
            Vector3 currentScale = Vector3.Lerp(Vector3.one, Vector3.one * uiExitScale, scaleT);
            foreach (RectTransform ui in uiToScale)
            {
                if (ui != null) ui.localScale = currentScale;
            }

            yield return null;
        }

        // Load the next scene once the screen is fully black
        SceneManager.LoadScene(sceneToLoad);
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