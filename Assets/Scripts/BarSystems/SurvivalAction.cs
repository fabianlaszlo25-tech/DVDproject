using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class SurvivalAction : MonoBehaviour
{
    [Header("References")]
    public BarManager barManager;
    [Tooltip("Auto-filled if left empty. Used to verify the player has enough money before playing the Eat animation.")]
    public DVDLogoBouncer economyBouncer;
    [Tooltip("Optional reference to FoodPriceDisplay to show 'Not enough money!' when clicked broke.")]
    public FoodPriceDisplay foodPriceDisplay;

    public enum ActionType { Eat, Toilet, Sleep, Clean }
    public ActionType actionType;

    [Header("Timings")]
    public float zoomInTime = 1.0f;
    public float holdTime = 1.5f;
    public float zoomOutTime = 1.0f;

    [Header("Camera & Visuals")]
    public Camera targetCamera;
    public Image fadeImage;
    public float zoomTargetFOV = 30f;

    [Header("UI Scale Workaround")]
    [Tooltip("Drag the UI elements here. They will scale up to leave the screen before the glitch happens.")]
    public RectTransform[] uiToScale;

    [Tooltip("How fast they scale relative to the zoom time. 0.4 means they fully scale by the time the zoom is 40% complete.")]
    [Range(0.1f, 0.9f)]
    public float scaleSpeedFraction = 0.4f;

    [Tooltip("How large the UI grows to push itself off the edges of the screen.")]
    public float uiExitScale = 3.0f;

    [Header("UI To Hide")]
    [Tooltip("Drag UI GameObjects here that should just turn off completely during the animation.")]
    public GameObject[] uiToHide;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip actionSound;
    [Tooltip("Optional sound played if the player tries to eat without enough money.")]
    public AudioClip cantAffordSound;

    [Header("Input Blocking")]
    public MonoBehaviour[] scriptsToDisable;

    private bool isActing = false;
    private float originalFOV;

    private void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (barManager == null) barManager = FindObjectOfType<BarManager>();
        if (economyBouncer == null) if (barManager != null) economyBouncer = barManager.economyBouncer;
        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();
        if (foodPriceDisplay == null) foodPriceDisplay = GetComponent<FoodPriceDisplay>();
    }

    public void PerformAction()
    {
        if (isActing || barManager == null) return;

        // If this is the Eat action, verify the player actually has enough money BEFORE zooming in or playing audio!
        if (actionType == ActionType.Eat && !CanAffordFood())
        {
            if (audioSource != null && cantAffordSound != null)
            {
                audioSource.PlayOneShot(cantAffordSound);
            }
            if (foodPriceDisplay != null)
            {
                foodPriceDisplay.ShowNotEnoughMoneyFeedback(1.2f);
            }
            return;
        }

        if (targetCamera != null) originalFOV = targetCamera.fieldOfView;

        StartCoroutine(ActionRoutine());
    }

    private bool CanAffordFood()
    {
        if (barManager == null) return false;
        if (economyBouncer == null) economyBouncer = barManager.economyBouncer;
        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();

        decimal foodCost = barManager.GetCurrentFoodCost();
        if (economyBouncer != null && economyBouncer.currentMoney < foodCost)
        {
            return false;
        }
        return true;
    }

    private IEnumerator ActionRoutine()
    {
        isActing = true;
        ToggleInput(false);
        ToggleHiddenUI(false);

        Color fadeColor = fadeImage != null ? fadeImage.color : Color.black;

        // Phase 1: Zoom In, Fade to Black, and Grow UI early
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

        if (targetCamera != null) targetCamera.fieldOfView = zoomTargetFOV;
        if (fadeImage != null) { fadeColor.a = 1f; fadeImage.color = fadeColor; }
        foreach (RectTransform ui in uiToScale) { if (ui != null) ui.localScale = Vector3.one * uiExitScale; }

        // Phase 2: Hold
        ApplyActionStats();

        if (audioSource != null && actionSound != null)
        {
            audioSource.PlayOneShot(actionSound);
        }

        yield return new WaitForSeconds(holdTime);

        // Phase 3: Zoom Out, Fade to Transparent, and Shrink UI back to normal late
        elapsedTime = 0f;
        float returnStartTime = zoomOutTime * (1f - scaleSpeedFraction);

        while (elapsedTime < zoomOutTime)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / zoomOutTime;

            if (targetCamera != null) targetCamera.fieldOfView = Mathf.Lerp(zoomTargetFOV, originalFOV, t);
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
        isActing = false;
    }

    private void ApplyActionStats()
    {
        switch (actionType)
        {
            case ActionType.Eat: barManager.TryEatFood(); break;
            case ActionType.Toilet: barManager.UseToilet(); break;
            case ActionType.Sleep: barManager.GoToSleep(); break;
            case ActionType.Clean: barManager.CleanSelf(); break;
        }
    }

    private void ToggleInput(bool enable)
    {
        foreach (MonoBehaviour script in scriptsToDisable)
        {
            if (script != null) script.enabled = enable;
        }

        if (EventSystem.current != null) EventSystem.current.enabled = enable;
        if (fadeImage != null) fadeImage.raycastTarget = !enable;
    }

    private void ToggleHiddenUI(bool enable)
    {
        foreach (GameObject ui in uiToHide)
        {
            if (ui != null) ui.SetActive(enable);
        }
    }
}