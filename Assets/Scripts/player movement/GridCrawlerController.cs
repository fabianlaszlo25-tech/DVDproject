using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class GridCrawlerController : MonoBehaviour
{
    [Header("Dependencies")]
    public GridManager gridManager;
    public SmoothCameraFollow cameraScript;

    [Header("Movement Settings")]
    public float moveDuration = 0.3f;
    public float rotateDuration = 0.25f;
    public float bumpDuration = 0.2f;
    public float bumpDistance = 0.3f;
    public float postActionCooldown = 0.05f;

    [Header("UI Constraints")]
    public RectTransform cropPanel;

    [Header("Crosshair Settings")]
    public RectTransform crosshairContainer;
    public Image defaultCrosshairImg;
    public Image interactCrosshairImg;

    [Header("Interactable Text Settings")]
    public TextMeshProUGUI interactTextUI;
    [Tooltip("Underlay shadow text for crisp readability against any background.")]
    public TextMeshProUGUI interactTextShadowUI;
    public float interactReach = 3f;

    [Header("HUD Pause Button")]
    [Tooltip("The pause button in the HUD overlay (auto-found if not assigned).")]
    public PauseButtonUI hudPauseButton;

    [Header("Mouse Sensitivity")]
    [Tooltip("Mouse sensitivity multiplier for cursor movement (governed globally by settings).")]
    public float mouseSensitivity = 1.0f;

    [HideInInspector] public Vector2 virtualMousePos;

    private bool isActing = false;
    private InputAction moveAction;
    private Vector3 textTargetScale = Vector3.zero;
    private MovementArrowUI[] allArrows;

    private Vector2 virtualCursorLocalPos;
    private bool hasInitializedVirtualMouse = false;

    private enum CrosshairMode { Default, Interact, Hidden }

    void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("Dpad")
            .With("Up", "<Keyboard>/w").With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/s").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/a").With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d").With("Right", "<Keyboard>/rightArrow");

        if (interactTextUI != null) interactTextUI.transform.localScale = Vector3.zero;
        SetCrosshairState(CrosshairMode.Default);

        allArrows = FindObjectsOfType<MovementArrowUI>();
        if (hudPauseButton == null) hudPauseButton = FindObjectOfType<PauseButtonUI>(true);

        SetupInteractTextOutline();
    }

    void Start()
    {
        mouseSensitivity = GlobalAudioSettings.MouseSensitivity;

        if (gridManager != null)
        {
            transform.position = gridManager.GetClosestNodePosition(transform.position);
        }
        if (cameraScript == null) cameraScript = FindObjectOfType<SmoothCameraFollow>();
        if (cameraScript != null)
        {
            cameraScript.SnapToTargetImmediate();
        }
    }

    void OnEnable()
    {
        moveAction.Enable();
        mouseSensitivity = GlobalAudioSettings.MouseSensitivity;
    }

    void OnDisable() => moveAction.Disable();

    void Update()
    {
        CalculateVirtualMousePosition();
        HandleCursorAndInteractions();

        if (isActing) return;
        HandleMovementInputs();
    }

    private void CalculateVirtualMousePosition()
    {
        if (PauseManager.IsGamePaused) return;

        if (cropPanel != null)
        {
            Camera uiCam = cropPanel.GetComponentInParent<Canvas>().worldCamera;
            Rect rect = cropPanel.rect;

            if (!hasInitializedVirtualMouse)
            {
                Vector2 initialMouse = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(cropPanel, initialMouse, uiCam, out virtualCursorLocalPos);
                virtualCursorLocalPos.x = Mathf.Clamp(virtualCursorLocalPos.x, rect.xMin, rect.xMax);
                virtualCursorLocalPos.y = Mathf.Clamp(virtualCursorLocalPos.y, rect.yMin, rect.yMax);
                hasInitializedVirtualMouse = true;
            }

            Vector2 delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            virtualCursorLocalPos += delta * mouseSensitivity;

            virtualCursorLocalPos.x = Mathf.Clamp(virtualCursorLocalPos.x, rect.xMin, rect.xMax);
            virtualCursorLocalPos.y = Mathf.Clamp(virtualCursorLocalPos.y, rect.yMin, rect.yMax);

            Vector3 clampedWorld = cropPanel.TransformPoint(virtualCursorLocalPos);
            virtualMousePos = RectTransformUtility.WorldToScreenPoint(uiCam, clampedWorld);
        }
        else
        {
            if (!hasInitializedVirtualMouse)
            {
                virtualMousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                hasInitializedVirtualMouse = true;
            }

            Vector2 delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            virtualMousePos += delta * mouseSensitivity;
            virtualMousePos.x = Mathf.Clamp(virtualMousePos.x, 0f, Screen.width);
            virtualMousePos.y = Mathf.Clamp(virtualMousePos.y, 0f, Screen.height);
        }
    }

    private void HandleCursorAndInteractions()
    {
        Camera cam = cameraScript != null ? cameraScript.GetComponent<Camera>() : Camera.main;
        if (cam == null) return;
        Camera uiCam = crosshairContainer.GetComponentInParent<Canvas>().worldCamera;

        if (crosshairContainer != null && crosshairContainer.parent != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                crosshairContainer.parent as RectTransform,
                virtualMousePos,
                uiCam,
                out Vector2 localPoint);
            crosshairContainer.localPosition = localPoint;
        }

        if (interactTextUI != null)
        {
            interactTextUI.transform.localScale = Vector3.Lerp(interactTextUI.transform.localScale, textTargetScale, Time.deltaTime * 15f);
        }

        ResetAllArrows();

        if (hudPauseButton == null) hudPauseButton = FindObjectOfType<PauseButtonUI>(true);

        if (hudPauseButton != null && hudPauseButton.gameObject.activeInHierarchy)
        {
            RectTransform pauseRect = hudPauseButton.Rect;
            if (pauseRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(pauseRect, virtualMousePos, uiCam, out Vector2 pauseLocalPoint))
            {
                if (hudPauseButton.CheckHitbox(pauseLocalPoint))
                {
                    textTargetScale = Vector3.zero;
                    SetCrosshairModeState(CrosshairMode.Hidden); // Hide entirely while on pause button
                    hudPauseButton.SetHoverState(true);

                    if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        hudPauseButton.TriggerAction();
                    }
                    return;
                }
            }
            hudPauseButton.SetHoverState(false);
        }

        MovementArrowUI hoveredArrow = null;
        foreach (var arrow in allArrows)
        {
            if (arrow == null || arrow.Rect == null) continue;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(arrow.Rect, virtualMousePos, uiCam, out Vector2 arrowLocalPoint))
            {
                if (arrow.CheckHitbox(arrowLocalPoint))
                {
                    hoveredArrow = arrow;
                    break;
                }
            }
        }

        if (hoveredArrow != null)
        {
            textTargetScale = Vector3.zero;
            SetCrosshairModeState(CrosshairMode.Hidden); // Hide entirely while on arrows
            hoveredArrow.SetHoverState(true);

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                hoveredArrow.TriggerAction();
            }
            return;
        }

        Ray ray = cam.ScreenPointToRay(virtualMousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, interactReach))
        {
            InteractableObject interactObj = hit.collider.GetComponentInParent<InteractableObject>();
            if (interactObj != null)
            {
                SetCrosshairModeState(CrosshairMode.Interact); // Show interact sprite for 3D objects

                if (interactTextUI != null)
                {
                    interactTextUI.text = interactObj.hoverText;
                    textTargetScale = interactObj.hideHoverText ? Vector3.zero : Vector3.one;
                }

                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    interactObj.onClick.Invoke();
                }
                return;
            }
        }

        SetCrosshairModeState(CrosshairMode.Default); // Revert to standard dot
        textTargetScale = Vector3.zero;
    }

    private void SetCrosshairState(CrosshairMode mode)
    {
        SetCrosshairModeState(mode);
    }

    private void SetCrosshairModeState(CrosshairMode mode)
    {
        if (defaultCrosshairImg != null) defaultCrosshairImg.enabled = (mode == CrosshairMode.Default);
        if (interactCrosshairImg != null) interactCrosshairImg.enabled = (mode == CrosshairMode.Interact);
    }

    private void ResetAllArrows()
    {
        if (allArrows == null) return;
        foreach (var arrow in allArrows)
        {
            if (arrow != null) arrow.SetHoverState(false);
        }
    }

    private void HandleMovementInputs()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        if (input.sqrMagnitude > 0 || Mathf.Abs(scroll) > 0.01f)
        {
            if (input.y > 0.5f || scroll > 0f) Action_MoveForward();
            else if (input.y < -0.5f || scroll < 0f) Action_TurnAround();
            else if (input.x > 0.5f) UI_TurnRight();
            else if (input.x < -0.5f) UI_TurnLeft();
        }
    }

    public void Action_MoveForward() { if (!isActing && gridManager != null) StartCoroutine(PerformMove(transform.forward, gridManager.GetValidMovePosition(transform.position, transform.forward))); }
    public void Action_TurnAround() { if (!isActing) StartCoroutine(PerformTurn(180f)); }
    public void UI_TurnRight() { if (!isActing) StartCoroutine(PerformTurn(90f)); }
    public void UI_TurnLeft() { if (!isActing) StartCoroutine(PerformTurn(-90f)); }

    private IEnumerator PerformMove(Vector3 targetDirection, Vector3? targetPos)
    {
        isActing = true;

        if (targetPos.HasValue)
        {
            Vector3 startPos = transform.position;
            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / moveDuration);
                transform.position = Vector3.Lerp(startPos, targetPos.Value, t);
                elapsed += Time.deltaTime;
                yield return null;
            }
            transform.position = targetPos.Value;
        }
        else
        {
            Vector3 startPos = transform.position;
            Vector3 bumpPos = startPos + (targetDirection * bumpDistance);
            float elapsed = 0f;
            float halfBump = bumpDuration / 2f;

            while (elapsed < halfBump)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / halfBump);
                transform.position = Vector3.Lerp(startPos, bumpPos, t);
                elapsed += Time.deltaTime;
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < halfBump)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / halfBump);
                transform.position = Vector3.Lerp(bumpPos, startPos, t);
                elapsed += Time.deltaTime;
                yield return null;
            }
            transform.position = startPos;
        }

        yield return new WaitForSeconds(postActionCooldown);
        isActing = false;
    }

    private IEnumerator PerformTurn(float angle)
    {
        isActing = true;
        if (cameraScript != null) cameraScript.isAutoCentering = true;

        Quaternion startRot = transform.rotation;
        Quaternion targetRot = startRot * Quaternion.Euler(0f, angle, 0f);
        float elapsed = 0f;

        while (elapsed < rotateDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / rotateDuration);
            transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.rotation = targetRot;

        if (cameraScript != null) cameraScript.isAutoCentering = false;
        yield return new WaitForSeconds(postActionCooldown);
        isActing = false;
    }
    private void SetupInteractTextOutline()
    {
        if (interactTextUI == null) return;

        // Clean up any old duplicate shadow child if it existed from previous versions
        Transform oldShadow = interactTextUI.transform.Find("Text_Shadow");
        if (oldShadow != null)
        {
            Destroy(oldShadow.gameObject);
        }

        // Apply true Signed Distance Field (SDF) per-glyph solid black outward outline
        Material mat = interactTextUI.fontMaterial;
        if (mat != null)
        {
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.05f);

            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.28f);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.02f);

            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, Color.black);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.28f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.02f);

            interactTextUI.UpdateMeshPadding();
        }
    }
}