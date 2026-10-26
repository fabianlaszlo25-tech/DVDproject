using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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

    [Header("Food / Eat Settings (When ActionType = Eat)")]
    [Tooltip("Name of the meal or snack.")]
    public string foodName = "Snack";
    [Tooltip("Base price of this food before inflation.")]
    public float baseFoodCost = 5f;
    [Tooltip("Amount of hunger reduced when eaten.")]
    public float hungerReduction = 25f;
    [Tooltip("Amount of needs increased when eaten.")]
    public float needsIncrease = 15f;
    [Tooltip("Amount of dirtiness increased when eaten.")]
    public float dirtinessIncrease = 5f;
    [Tooltip("Amount of depression reduced when eaten.")]
    public float depressionReduction = 3f;

    [Header("Eat Visualizer & Hold Animation")]
    [Tooltip("If true, eating uses the InteractVisualizer hold/glow animation instead of zooming the camera.")]
    public bool useVisualizerForEat = true;
    [Tooltip("How long (in seconds) the player must hold click to eat. Set to 0 for instant click-to-eat.")]
    public float holdDuration = 1.0f;
    [Tooltip("How high the object rises while held (0 for heavy appliances like fridge/stove).")]
    public float levitateHeight = 0f;
    [Tooltip("Shake amplitude while held.")]
    public float shakeIntensity = 0.03f;
    [Tooltip("Shake oscillation speed.")]
    public float shakeSpeed = 35f;
    [Tooltip("How fast the object returns to its resting position if released early.")]
    public float returnDuration = 0.2f;

    [Header("Hold Audio")]
    [Tooltip("Looping buildup sound played while holding to eat.")]
    public AudioClip shakeSound;
    [Range(0f, 1f)] public float shakeVolume = 0.8f;
    public InteractVisualizer visualizer;

    [Header("Glow & Max Hunger Alert")]
    [Tooltip("Color of the periodic & hold outline under normal hunger.")]
    public Color normalGlowColor = new Color(1f, 0.82f, 0.25f, 1f);
    [Tooltip("Color of the periodic & hold outline when player hunger is at maximum (starving).")]
    public Color maxHungerGlowColor = Color.red;

    [Header("Camera Zoom Timings (Toilet, Sleep, Clean)")]
    public float zoomInTime = 1.0f;
    public float holdTime = 1.5f;
    public float zoomOutTime = 1.0f;

    [Header("Camera & Visuals (Toilet, Sleep, Clean)")]
    public Camera targetCamera;
    public Image fadeImage;
    public float zoomTargetFOV = 30f;

    [Header("UI Scale Workaround (Toilet, Sleep, Clean)")]
    [Tooltip("Drag the UI elements here. They will scale up to leave the screen before the glitch happens.")]
    public RectTransform[] uiToScale;

    [Tooltip("How fast they scale relative to the zoom time. 0.4 means they fully scale by the time the zoom is 40% complete.")]
    [Range(0.1f, 0.9f)]
    public float scaleSpeedFraction = 0.4f;

    [Tooltip("How large the UI grows to push itself off the edges of the screen.")]
    public float uiExitScale = 3.0f;

    [Header("UI To Hide (Toilet, Sleep, Clean)")]
    [Tooltip("Drag UI GameObjects here that should just turn off completely during the animation.")]
    public GameObject[] uiToHide;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip actionSound;
    [Tooltip("Optional sound played if the player tries to eat without enough money.")]
    public AudioClip cantAffordSound;

    [Header("Input Blocking (Toilet, Sleep, Clean)")]
    public MonoBehaviour[] scriptsToDisable;

    private bool isActing = false;
    private float originalFOV;

    // Hold state for Eat action
    private bool isHolding = false;
    private bool isReturning = false;
    private float holdProgress = 0f;
    private Vector3 shakeAnchor;
    private Vector3 heldTarget;
    private Vector3 returnStart;
    private float returnElapsed = 0f;
    private AudioSource shakeSource;
    private InteractableObject interactable;
    private int lastActionClickFrame = -1;

    private void Awake()
    {
        shakeAnchor = transform.position;
        interactable = GetComponent<InteractableObject>();
    }

    private void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (barManager == null) barManager = FindObjectOfType<BarManager>();
        if (economyBouncer == null && barManager != null) economyBouncer = barManager.economyBouncer;
        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();
        if (foodPriceDisplay == null) foodPriceDisplay = GetComponent<FoodPriceDisplay>();
        if (interactable == null) interactable = GetComponent<InteractableObject>();

        if (actionType == ActionType.Eat)
        {
            if (visualizer == null) visualizer = GetComponent<InteractVisualizer>();
            if (visualizer == null && useVisualizerForEat) visualizer = gameObject.AddComponent<InteractVisualizer>();

            if (visualizer != null)
            {
                visualizer.enablePeriodicGlow = true;
                if (visualizer.glowColor != Color.clear && visualizer.glowColor != Color.black && normalGlowColor == new Color(1f, 0.82f, 0.25f, 1f))
                {
                    normalGlowColor = visualizer.glowColor;
                }
            }

            if (foodPriceDisplay != null)
            {
                foodPriceDisplay.foodName = foodName;
                foodPriceDisplay.basePrice = baseFoodCost;
            }

            if (shakeSound != null)
            {
                shakeSource = gameObject.AddComponent<AudioSource>();
                shakeSource.playOnAwake = false;
                shakeSource.loop = true;
                shakeSource.spatialBlend = 0f;
                shakeSource.clip = shakeSound;
                shakeSource.volume = shakeVolume;
            }
        }

        if (interactable != null)
        {
            interactable.onClick.AddListener(PerformAction);
        }
    }

    private void OnDestroy()
    {
        if (isHolding || isReturning)
        {
            transform.position = shakeAnchor;
            if (visualizer != null) visualizer.ResetGlow(keepPeriodicGlow: true);
            if (shakeSource != null) shakeSource.Stop();
        }
        if (interactable != null)
        {
            interactable.onClick.RemoveListener(PerformAction);
        }
    }

    private void OnDisable()
    {
        if (isHolding || isReturning)
        {
            transform.position = shakeAnchor;
            if (visualizer != null) visualizer.SetIntensity(0f);
            if (shakeSource != null) shakeSource.Stop();
            if (interactable != null) interactable.hideHoverText = false;
            isHolding = false;
            isReturning = false;
        }
    }

    public decimal GetFoodCost()
    {
        if (barManager != null)
        {
            return barManager.GetFoodCost(baseFoodCost);
        }
        return (decimal)baseFoodCost;
    }

    public bool CanAffordFood()
    {
        if (barManager == null) return false;
        if (economyBouncer == null) economyBouncer = barManager.economyBouncer;
        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();

        decimal foodCost = GetFoodCost();
        if (economyBouncer != null && economyBouncer.currentMoney < foodCost)
        {
            return false;
        }
        return true;
    }

    public void PerformAction()
    {
        // Guard against duplicate event invocations in the exact same frame
        if (Time.frameCount == lastActionClickFrame) return;
        lastActionClickFrame = Time.frameCount;

        if (isActing || isHolding || barManager == null) return;

        // If this is the Sleep action and a SceneTransitionOut component is present (e.g. Bed transitioning to dream sequence),
        // let SceneTransitionOut drive the scene transition so two competing camera/fade routines don't fight each other!
        if (actionType == ActionType.Sleep)
        {
            SceneTransitionOut sceneOut = GetComponent<SceneTransitionOut>();
            if (sceneOut != null)
            {
                sceneOut.TransitionToScene();
                return;
            }
        }

        // If this is the Eat action, verify the player actually has enough money BEFORE action or hold starts!
        if (actionType == ActionType.Eat)
        {
            if (!CanAffordFood())
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

            // Redo eating: instead of the camera zoom-in/fade animation, use the visualize object hold animation!
            if (useVisualizerForEat)
            {
                if (holdDuration <= 0f)
                {
                    CompleteEat();
                }
                else
                {
                    BeginHold();
                }
                return;
            }
        }

        if (targetCamera != null)
        {
            originalFOV = targetCamera.fieldOfView > zoomTargetFOV + 1f ? targetCamera.fieldOfView : 65f;
        }

        StartCoroutine(ActionRoutine());
    }

    private void BeginHold()
    {
        isHolding = true;
        isReturning = false;
        holdProgress = 0f;
        shakeAnchor = transform.position;
        heldTarget = shakeAnchor + Vector3.up * levitateHeight;

        if (interactable != null) interactable.hideHoverText = true;
        if (shakeSource != null && shakeSound != null) shakeSource.Play();
    }

    private void Update()
    {
        if (actionType != ActionType.Eat) return;

        // Periodic highlight & hold glow: switch to red if the player is at maximum hunger!
        if (visualizer != null)
        {
            bool isMaxHunger = barManager != null && barManager.currentHunger >= barManager.maxHunger - 0.01f;
            visualizer.glowColor = isMaxHunger ? maxHungerGlowColor : normalGlowColor;
        }

        if (!useVisualizerForEat) return;

        if (isHolding)
        {
            bool stillHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (!stillHeld)
            {
                BeginReturn();
                return;
            }

            holdProgress += Time.deltaTime / Mathf.Max(0.01f, holdDuration);
            ApplyHoldMotion(holdProgress);

            if (holdProgress >= 1f)
            {
                CompleteEat();
            }
        }
        else if (isReturning)
        {
            UpdateReturn();
        }
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
        isHolding = false;
        isReturning = true;
        holdProgress = 0f;
        returnElapsed = 0f;
        returnStart = transform.position;

        if (visualizer != null) visualizer.SetIntensity(0f);
        if (interactable != null) interactable.hideHoverText = false;
        if (shakeSource != null) shakeSource.Stop();
    }

    private void UpdateReturn()
    {
        returnElapsed += Time.deltaTime;
        float t = Mathf.SmoothStep(0f, 1f, returnElapsed / Mathf.Max(0.01f, returnDuration));
        transform.position = Vector3.Lerp(returnStart, shakeAnchor, t);

        if (returnElapsed >= returnDuration)
        {
            transform.position = shakeAnchor;
            isReturning = false;
        }
    }

    private void CompleteEat()
    {
        isHolding = false;
        isReturning = false;
        transform.position = shakeAnchor;

        if (visualizer != null) visualizer.HideImmediate();
        if (shakeSource != null) shakeSource.Stop();
        if (interactable != null) interactable.hideHoverText = false;

        decimal cost = GetFoodCost();
        if (barManager != null)
        {
            bool ate = barManager.TryEatFood(cost, hungerReduction, needsIncrease, dirtinessIncrease, depressionReduction);
            if (!ate)
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
        }

        if (audioSource != null && actionSound != null)
        {
            audioSource.PlayOneShot(actionSound);
        }
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
            case ActionType.Eat:
                barManager.TryEatFood(GetFoodCost(), hungerReduction, needsIncrease, dirtinessIncrease, depressionReduction);
                break;
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
