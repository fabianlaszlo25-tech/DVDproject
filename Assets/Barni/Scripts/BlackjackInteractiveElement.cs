using System.Collections;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(InteractableObject))]
[RequireComponent(typeof(Collider))]
public class BlackjackInteractiveElement : MonoBehaviour
{
    public enum ElementActionType
    {
        BetChip = 0,
        DealOrNewRound = 1,
        Hit = 2,
        Stand = 3,
        ClearBet = 4,
        LeaveTable = 5,
        JoinTable = 6
    }

    [Header("Action Configuration")]
    public BlackjackGameController gameController;
    public ElementActionType actionType = ElementActionType.BetChip;
    [Tooltip("Used when actionType is BetChip.")]
    public float chipDenomination = 5f;

    [Header("Labels & Hover Prompt")]
    public string activeHoverPrompt = "Interact";
    public TMP_Text labelText;

    [Header("Visual Feedback")]
    public MeshRenderer primaryRenderer;
    public MeshRenderer accentRenderer;
    public Color activeColor = Color.white;
    public Color disabledColor = new Color(0.32f, 0.32f, 0.35f, 1f);
    public float hoverScaleMultiplier = 1.10f;
    public float hoverLiftOffset = 0.012f;
    public float clickPressDepth = 0.012f;
    public float transitionSpeed = 14f;
    public float clickCooldown = 0.20f;

    private InteractableObject interactable;
    private Collider elementCollider;
    private InteractVisualizer interactVisualizer;
    private GridCrawlerController crawlerController;

    private Vector3 initialLocalPos;
    private Vector3 initialLocalScale;
    private Vector3 initialLabelLocalPos;
    private bool hasExternalLabel;
    private bool isElementEnabled = true;
    private bool isPressAnimating;
    private float lastTriggerTime = -10f;
    private MaterialPropertyBlock propBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        interactable = GetComponent<InteractableObject>();
        elementCollider = GetComponent<Collider>();
        interactVisualizer = GetComponent<InteractVisualizer>();
        if (primaryRenderer == null) primaryRenderer = GetComponent<MeshRenderer>();

        initialLocalPos = transform.localPosition;
        initialLocalScale = transform.localScale;
        EnsureUniformLabelHierarchyAndCaptureState();
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
            gameController = GetComponentInParent<BlackjackGameController>();
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
            Vector3 targetPos = initialLocalPos + (targeted && isElementEnabled ? new Vector3(0f, hoverLiftOffset, 0f) : Vector3.zero);
            Vector3 targetScale = initialLocalScale * (targeted && isElementEnabled ? hoverScaleMultiplier : 1f);

            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.unscaledDeltaTime * transitionSpeed);
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * transitionSpeed);
            SyncExternalLabelPosition();
            ApplyColorState(targeted && isElementEnabled, false);
        }
    }

    public void EnsureUniformLabelHierarchyAndCaptureState()
    {
        initialLocalPos = transform.localPosition;
        initialLocalScale = transform.localScale;

        if (labelText == null)
        {
            labelText = GetComponentInChildren<TMP_Text>(true);
        }

        if (labelText != null)
        {
            if (labelText.transform.parent == transform && transform.parent != null)
            {
                labelText.transform.SetParent(transform.parent, true);
            }

            if (labelText.transform.parent != transform)
            {
                hasExternalLabel = true;
                labelText.transform.localScale = Vector3.one;
                labelText.transform.localPosition = initialLocalPos + new Vector3(0f, initialLocalScale.y * 0.5f + 0.008f, 0f);
                labelText.transform.localRotation = Quaternion.Euler(75f, 0f, 0f);
                initialLabelLocalPos = labelText.transform.localPosition;

                float targetFontSize = actionType == ElementActionType.BetChip ? 0.23f : 0.21f;
                if (labelText.fontSize < targetFontSize || labelText.fontSize > 1.0f)
                {
                    labelText.fontSize = targetFontSize;
                }
                labelText.fontStyle = FontStyles.Bold;
                labelText.textWrappingMode = TextWrappingModes.NoWrap;
                labelText.overflowMode = TextOverflowModes.Overflow;
                RectTransform rt = labelText.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.sizeDelta = new Vector2(0.45f, 0.25f);
                }
            }
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
            gameController = GetComponentInParent<BlackjackGameController>();
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
        const float halfDuration = 0.065f;
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
            baseCol = Color.Lerp(baseCol, Color.white, 0.28f);
        }

        Color emission = isElementEnabled
            ? (isPressed ? baseCol * 0.65f : (isHovered ? baseCol * 0.35f : baseCol * 0.08f))
            : Color.black;

        primaryRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(BaseColorId, baseCol);
        propBlock.SetColor(LegacyColorId, baseCol);
        propBlock.SetColor(EmissionColorId, emission);
        primaryRenderer.SetPropertyBlock(propBlock);

        if (labelText != null)
        {
            labelText.alpha = isElementEnabled ? 1f : 0.4f;
        }
    }
}
