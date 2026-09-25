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
    public float interactReach = 3f;

    [HideInInspector] public Vector2 virtualMousePos;

    private bool isActing = false;
    private InputAction moveAction;
    private Vector3 textTargetScale = Vector3.zero;
    private MovementArrowUI[] allArrows;

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
    }

    void Start()
    {
        if (gridManager != null)
        {
            transform.position = gridManager.GetClosestNodePosition(transform.position);
        }
    }

    void OnEnable() => moveAction.Enable();
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
        Vector2 rawMouse = Mouse.current.position.ReadValue();

        if (cropPanel != null)
        {
            Camera uiCam = cropPanel.GetComponentInParent<Canvas>().worldCamera;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(cropPanel, rawMouse, uiCam, out Vector2 localPoint);

            Rect rect = cropPanel.rect;
            localPoint.x = Mathf.Clamp(localPoint.x, rect.xMin, rect.xMax);
            localPoint.y = Mathf.Clamp(localPoint.y, rect.yMin, rect.yMax);

            Vector3 clampedWorld = cropPanel.TransformPoint(localPoint);
            virtualMousePos = RectTransformUtility.WorldToScreenPoint(uiCam, clampedWorld);
        }
        else
        {
            virtualMousePos = rawMouse;
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
            SetCrosshairState(CrosshairMode.Hidden); // Hide entirely while on arrows
            hoveredArrow.SetHoverState(true);

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                hoveredArrow.TriggerAction();
            }
            return;
        }

        Ray ray = cam.ScreenPointToRay(virtualMousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, interactReach))
        {
            InteractableObject interactObj = hit.collider.GetComponent<InteractableObject>();
            if (interactObj != null)
            {
                SetCrosshairState(CrosshairMode.Interact); // Show interact sprite for 3D objects

                if (interactTextUI != null)
                {
                    interactTextUI.text = interactObj.hoverText;
                    textTargetScale = Vector3.one;
                }

                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    interactObj.onClick.Invoke();
                }
                return;
            }
        }

        SetCrosshairState(CrosshairMode.Default); // Revert to standard dot
        textTargetScale = Vector3.zero;
    }

    private void SetCrosshairState(CrosshairMode mode)
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
        float scroll = Mouse.current.scroll.ReadValue().y;

        if (input.sqrMagnitude > 0 || Mathf.Abs(scroll) > 0.01f)
        {
            if (input.y > 0.5f || scroll > 0f) Action_MoveForward();
            else if (input.y < -0.5f || scroll < 0f) Action_TurnAround();
            else if (input.x > 0.5f) UI_TurnRight();
            else if (input.x < -0.5f) UI_TurnLeft();
        }
    }

    public void Action_MoveForward() { if (!isActing) StartCoroutine(PerformMove(transform.forward, gridManager.GetValidMovePosition(transform.position, transform.forward))); }
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
}