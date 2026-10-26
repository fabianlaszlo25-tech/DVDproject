using System.Collections;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(InteractableObject))]
[RequireComponent(typeof(Collider))]
public class RouletteInteractiveElement : MonoBehaviour
{
    public enum RouletteActionType
    {
        SelectChipDenomination = 0,
        PlaceBoardBet = 1,
        SpinOrNewRound = 2,
        ClearBets = 3,
        LeaveTable = 4
    }

    [Header("Action Configuration")]
    public RouletteGameController gameController;
    public RouletteActionType actionType = RouletteActionType.PlaceBoardBet;
    [Tooltip("Used when actionType is SelectChipDenomination.")]
    public float chipDenomination = 5f;
    [Tooltip("Used when actionType is PlaceBoardBet.")]
    public RouletteBetType betType = RouletteBetType.StraightNumber;
    [Tooltip("0..36 when betType is StraightNumber.")]
    public int betNumber = 0;

    [Header("Labels & Hover Prompt")]
    public string activeHoverPrompt = "Interact";
    public TMP_Text labelText;

    [Header("Visual Feedback")]
    public MeshRenderer primaryRenderer;
    public Color activeColor = Color.white;
    public Color disabledColor = new Color(0.28f, 0.28f, 0.31f, 1f);
    public float hoverScaleMultiplier = 1.10f;
    public float hoverLiftOffset = 0.012f;
    public float clickPressDepth = 0.010f;
    public float transitionSpeed = 15f;
    public float clickCooldown = 0.16f;

    private InteractableObject interactable;
    private Collider elementCollider;
    private InteractVisualizer interactVisualizer;
    private GridCrawlerController crawlerController;

    private Vector3 initialLocalPos;
    private Vector3 initialLocalScale;
    private Vector3 initialLabelLocalPos;
    private bool hasExternalLabel;
    private bool isElementEnabled = true;
    private bool isSelectedHighlight;
    private bool isWinningSpotHighlight;
    private bool isPressAnimating;
    private float lastTriggerTime = -10f;
    private MaterialPropertyBlock propBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public RouletteBetKey GetBetKey()
    {
        return new RouletteBetKey(betType, betNumber);
    }

    private void Awake()
    {
        interactable = GetComponent<InteractableObject>();
        elementCollider = GetComponent<Collider>();
        interactVisualizer = GetComponent<InteractVisualizer>();
        if (primaryRenderer == null) primaryRenderer = GetComponent<MeshRenderer>();

        initialLocalPos = transform.localPosition;
        initialLocalScale = transform.localScale;
        if (labelText != null && labelText.transform.parent != transform)
        {
            hasExternalLabel = true;
            initialLabelLocalPos = labelText.transform.localPosition;
        }
        propBlock = new MaterialPropertyBlock();

        if (interactable != null)
        {
            if (!string.IsNullOrEmpty(activeHoverPrompt))
            {
                interactable.hoverText = activeHoverPrompt;
            }
            interactable.onClick.RemoveListener(TriggerElement);
            interactable.onClick.AddListener(TriggerElement);
        }
    }

    private void Start()
    {
        if (gameController == null)
        {
            gameController = GetComponentInParent<RouletteGameController>();
        }
        if (crawlerController == null)
        {
            crawlerController = FindFirstObjectByType<GridCrawlerController>();
        }

        ApplyColorState(false, false);
    }

    private void Update()
    {
        bool targeted = IsCurrentlyTargetedByPointer();

        if (interactVisualizer != null)
        {
            interactVisualizer.SetIntensity(targeted && isElementEnabled ? 1f : 0f);
        }

        if (!isPressAnimating)
        {
            float lift = 0f;
            if (targeted && isElementEnabled) lift = hoverLiftOffset;
            else if (isSelectedHighlight && isElementEnabled) lift = hoverLiftOffset * 0.75f;

            float scaleMult = 1f;
            if (targeted && isElementEnabled) scaleMult = hoverScaleMultiplier;
            else if (isSelectedHighlight && isElementEnabled) scaleMult = 1.07f;

            Vector3 targetPos = initialLocalPos + new Vector3(0f, lift, 0f);
            Vector3 targetScale = initialLocalScale * scaleMult;

            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.unscaledDeltaTime * transitionSpeed);
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * transitionSpeed);
            SyncExternalLabelPosition();
            ApplyColorState(targeted && isElementEnabled, false);
        }
    }

    private void SyncExternalLabelPosition()
    {
        if (hasExternalLabel && labelText != null)
        {
            Vector3 delta = transform.localPosition - initialLocalPos;
            labelText.transform.localPosition = initialLabelLocalPos + delta;
        }
    }

    private bool IsCurrentlyTargetedByPointer()
    {
        if (!isElementEnabled || elementCollider == null || !elementCollider.enabled || interactable == null)
        {
            return false;
        }

        if (crawlerController == null)
        {
            crawlerController = FindFirstObjectByType<GridCrawlerController>();
            if (crawlerController == null) return false;
        }

        bool crosshairActive = crawlerController.interactCrosshairImg != null && crawlerController.interactCrosshairImg.enabled;
        if (!crosshairActive || crawlerController.interactTextUI == null)
        {
            return false;
        }

        return string.Equals(crawlerController.interactTextUI.text, interactable.hoverText, System.StringComparison.Ordinal);
    }

    public void SetElementEnabled(bool enabledState, string updatedHoverPrompt = null, string updatedLabel = null)
    {
        isElementEnabled = enabledState;

        if (elementCollider != null)
        {
            elementCollider.enabled = enabledState;
        }

        if (interactable != null && !string.IsNullOrEmpty(updatedHoverPrompt))
        {
            activeHoverPrompt = updatedHoverPrompt;
            interactable.hoverText = updatedHoverPrompt;
        }

        if (labelText != null && !string.IsNullOrEmpty(updatedLabel))
        {
            labelText.text = updatedLabel;
        }

        if (!enabledState && interactVisualizer != null)
        {
            interactVisualizer.SetIntensity(0f);
        }

        ApplyColorState(false, false);
    }

    public void SetSelectedHighlight(bool selected)
    {
        isSelectedHighlight = selected;
        ApplyColorState(false, false);
    }

    public void SetWinningSpotHighlight(bool winning)
    {
        isWinningSpotHighlight = winning;
        ApplyColorState(false, false);
    }

    public void TriggerElement()
    {
        if (!isElementEnabled) return;

        float now = Time.unscaledTime;
        if (now - lastTriggerTime < clickCooldown)
        {
            return;
        }
        lastTriggerTime = now;

        if (gameObject.activeInHierarchy)
        {
            StopAllCoroutines();
            StartCoroutine(ClickFeedbackRoutine());
        }

        if (gameController == null)
        {
            gameController = GetComponentInParent<RouletteGameController>();
        }

        if (gameController != null)
        {
            gameController.OnInteractiveElementClicked(this);
        }
    }

    private IEnumerator ClickFeedbackRoutine()
    {
        isPressAnimating = true;
        Vector3 pressedPos = initialLocalPos - new Vector3(0f, clickPressDepth, 0f);
        Vector3 pressedScale = initialLocalScale * 0.94f;

        ApplyColorState(true, true);

        float elapsed = 0f;
        const float halfDuration = 0.055f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            transform.localPosition = Vector3.Lerp(initialLocalPos, pressedPos, t);
            transform.localScale = Vector3.Lerp(initialLocalScale, pressedScale, t);
            SyncExternalLabelPosition();
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            transform.localPosition = Vector3.Lerp(pressedPos, initialLocalPos, t);
            transform.localScale = Vector3.Lerp(pressedScale, initialLocalScale, t);
            SyncExternalLabelPosition();
            yield return null;
        }

        transform.localPosition = initialLocalPos;
        transform.localScale = initialLocalScale;
        SyncExternalLabelPosition();
        isPressAnimating = false;
    }

    private void ApplyColorState(bool isHovered, bool isPressed)
    {
        if (primaryRenderer == null) return;
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        Color baseCol = isElementEnabled ? activeColor : disabledColor;
        if (isPressed)
        {
            baseCol = Color.Lerp(baseCol, Color.white, 0.55f);
        }
        else if (isHovered)
        {
            baseCol = Color.Lerp(baseCol, Color.white, 0.30f);
        }
        else if (isWinningSpotHighlight)
        {
            baseCol = Color.Lerp(baseCol, new Color(1f, 0.88f, 0.25f, 1f), 0.50f);
        }
        else if (isSelectedHighlight)
        {
            baseCol = Color.Lerp(baseCol, Color.white, 0.22f);
        }

        Color emission = Color.black;
        if (isElementEnabled)
        {
            if (isPressed) emission = baseCol * 0.70f;
            else if (isHovered) emission = baseCol * 0.38f;
            else if (isWinningSpotHighlight) emission = new Color(0.75f, 0.62f, 0.12f, 1f);
            else if (isSelectedHighlight) emission = baseCol * 0.42f;
            else emission = baseCol * 0.08f;
        }

        primaryRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(BaseColorId, baseCol);
        propBlock.SetColor(LegacyColorId, baseCol);
        propBlock.SetColor(EmissionColorId, emission);
        primaryRenderer.SetPropertyBlock(propBlock);

        if (labelText != null)
        {
            labelText.alpha = isElementEnabled ? 1f : 0.42f;
        }
    }
}
