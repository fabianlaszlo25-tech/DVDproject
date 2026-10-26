using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// HUD Pause button under the player controller's overlay canvas ("croper").
/// Uses the exact same hover scale/alpha animation and mathematical hitbox logic as MovementArrowUI,
/// driven by GridCrawlerController's raycast/virtual cursor (virtualMousePos).
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Image), typeof(RectTransform))]
public class PauseButtonUI : MonoBehaviour
{
    public GridCrawlerController controller;
    public PauseManager pauseManager;

    [Header("Hover Settings")]
    public float defaultAlpha = 0.2f;
    public float hoverAlpha = 0.8f;
    public float hoverScale = 1.2f;

    [Header("Hitbox Tuning")]
    [Tooltip("Expands the clickable area outward (in local pixels).")]
    public Vector2 hitboxPadding = new Vector2(30f, 30f);
    [Tooltip("Shifts the center of the clickable area if it feels offset.")]
    public Vector2 hitboxOffset = Vector2.zero;

    public RectTransform Rect { get; private set; }
    private Image img;
    private Vector3 targetScale = Vector3.one;
    private float targetAlpha;
    private bool isCurrentlyHovered = false;

    private void Awake()
    {
        Rect = GetComponent<RectTransform>();
        img = GetComponent<Image>();

        if (controller == null) controller = FindObjectOfType<GridCrawlerController>(true);
        if (pauseManager == null) pauseManager = FindObjectOfType<PauseManager>(true);

        targetAlpha = defaultAlpha;
        SetAlpha(targetAlpha);
    }

    private void Update()
    {
        if (Rect == null) Rect = GetComponent<RectTransform>();
        if (img == null) img = GetComponent<Image>();
        if (controller == null) controller = FindObjectOfType<GridCrawlerController>(true);
        if (pauseManager == null) pauseManager = FindObjectOfType<PauseManager>(true);

        isCurrentlyHovered = false;

        if ((pauseManager == null || !pauseManager.IsPaused) && controller != null && controller.enabled)
        {
            Vector2 virtualCursorPos = GetCurrentVirtualMousePos();
            Camera uiCam = GetUICamera();

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, virtualCursorPos, uiCam, out Vector2 localPoint))
            {
                if (CheckHitbox(localPoint))
                {
                    isCurrentlyHovered = true;
                }
            }
        }

        SetHoverState(isCurrentlyHovered);

        // Exact same hover scale & alpha lerp animation as MovementArrowUI
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * 12f);

        if (img != null)
        {
            Color c = img.color;
            c.a = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * 12f);
            img.color = c;
        }

        if (isCurrentlyHovered && WasLeftMousePressedThisFrame())
        {
            isCurrentlyHovered = false;
            SetHoverState(false);
            TriggerAction();
        }
    }

    private void LateUpdate()
    {
        // Hide the in-game crosshair dot/interact sprite while hovering the pause button,
        // matching GridCrawlerController's behavior when hovering the left/right MovementArrowUI buttons.
        if (isCurrentlyHovered && (pauseManager == null || !pauseManager.IsPaused) && controller != null && controller.enabled)
        {
            if (controller.defaultCrosshairImg != null) controller.defaultCrosshairImg.enabled = false;
            if (controller.interactCrosshairImg != null) controller.interactCrosshairImg.enabled = false;
            if (controller.interactTextUI != null) controller.interactTextUI.transform.localScale = Vector3.zero;
        }
    }

    // Custom mathematical hitbox that applies padding and offsets (identical to MovementArrowUI)
    public bool CheckHitbox(Vector2 localPoint)
    {
        if (Rect == null) Rect = GetComponent<RectTransform>();
        Rect r = Rect.rect;

        r.position += hitboxOffset;

        r.xMin -= hitboxPadding.x;
        r.xMax += hitboxPadding.x;
        r.yMin -= hitboxPadding.y;
        r.yMax += hitboxPadding.y;

        return r.Contains(localPoint);
    }

    public void SetHoverState(bool isHovering)
    {
        targetScale = isHovering ? Vector3.one * hoverScale : Vector3.one;
        targetAlpha = isHovering ? hoverAlpha : defaultAlpha;
    }

    public void TriggerAction()
    {
        if (pauseManager == null) pauseManager = FindObjectOfType<PauseManager>(true);
        if (pauseManager != null)
        {
            pauseManager.PauseGame();
        }
    }

    private Vector2 GetCurrentVirtualMousePos()
    {
        Vector2 rawMouse = Vector2.zero;
        if (Mouse.current != null)
        {
            rawMouse = Mouse.current.position.ReadValue();
        }
        else
        {
            try
            {
                rawMouse = Input.mousePosition;
            }
            catch
            {
                rawMouse = Vector2.zero;
            }
        }

        if (controller != null && controller.cropPanel != null)
        {
            Canvas cropCanvas = controller.cropPanel.GetComponentInParent<Canvas>();
            Camera uiCam = cropCanvas != null ? cropCanvas.worldCamera : null;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(controller.cropPanel, rawMouse, uiCam, out Vector2 localPoint);

            Rect rect = controller.cropPanel.rect;
            localPoint.x = Mathf.Clamp(localPoint.x, rect.xMin, rect.xMax);
            localPoint.y = Mathf.Clamp(localPoint.y, rect.yMin, rect.yMax);

            Vector3 clampedWorld = controller.cropPanel.TransformPoint(localPoint);
            return RectTransformUtility.WorldToScreenPoint(uiCam, clampedWorld);
        }

        return controller != null ? controller.virtualMousePos : rawMouse;
    }

    private Camera GetUICamera()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            return parentCanvas.worldCamera != null ? parentCanvas.worldCamera : Camera.main;
        }
        return null;
    }

    private static bool WasLeftMousePressedThisFrame()
    {
        if (Mouse.current != null)
        {
            return Mouse.current.leftButton.wasPressedThisFrame;
        }

        try
        {
            return Input.GetMouseButtonDown(0);
        }
        catch
        {
            return false;
        }
    }

    private void SetAlpha(float alpha)
    {
        if (img == null) img = GetComponent<Image>();
        if (img == null) return;
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }
}
