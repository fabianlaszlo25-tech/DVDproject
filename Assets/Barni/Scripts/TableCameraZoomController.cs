using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Provides an automatic, smooth "table zoom" camera mode for 3D bar mini-games (Blackjack & Roulette).
/// Gently tilts the camera toward the tabletop with a comfortable FOV so the entire table and all 3D text
/// are clearly readable without over-zooming, and automatically zooms back out when the player clicks
/// LEAVE TABLE or moves/turns away.
/// </summary>
public class TableCameraZoomController : MonoBehaviour
{
    [Header("Scene Camera References")]
    public SmoothCameraFollow smoothCamera;
    public Camera targetCamera;
    public GridCrawlerController playerController;
    public Transform zoomAnchor;

    [Header("Zoom Framing Settings (Table-Local Space)")]
    [Tooltip("Local position relative to the table root where the camera target moves when zoomed in.")]
    public Vector3 localZoomPosition = new Vector3(0f, 1.24f, -1.08f);
    [Tooltip("Downward pitch angle (degrees) looking at the tabletop when zoomed in.")]
    public float zoomPitchAngle = 18f;
    [Tooltip("Camera Field of View while zoomed in on the table (default 65 in bar, 59 for full-table view).")]
    public float zoomedFieldOfView = 59f;
    [Tooltip("Allow mouse scroll wheel to fine-tune zoom FOV while seated at the table.")]
    public bool allowScrollWheelZoom = true;
    public float minScrollFieldOfView = 46f;
    public float maxScrollFieldOfView = 65f;
    [Tooltip("Reduced horizontal mouse-edge look angle while zoomed so the table stays framed.")]
    public float zoomedMaxHorizontalLook = 18f;
    [Tooltip("Reduced vertical mouse-edge look angle while zoomed so the table stays framed.")]
    public float zoomedMaxVerticalLook = 12f;
    [Tooltip("Interpolation speed for FOV and look-limit transitions.")]
    public float fovTransitionSpeed = 7.5f;

    [Header("Auto-Zoom Behavior")]
    [Tooltip("Automatically tilts/frames the table when the player stands on the tile directly in front of the table and faces it.")]
    public bool enableAutoZoomOnApproach = true;
    public float triggerHorizontalDistance = 1.42f;
    public float triggerFacingAngle = 35f;
    [Tooltip("If true, hides screen-edge MovementArrowUI hitboxes while zoomed in so edge clicks hit table buttons.")]
    public bool suppressTurnArrowsWhileZoomed = true;
    [Tooltip("Distance threshold beyond which the camera automatically zooms back out.")]
    public float maxSessionDistance = 2.85f;

    public bool IsZoomedIn { get; private set; }

    private Transform originalCameraTarget;
    private float originalFieldOfView = 65f;
    private float originalMaxHorizontalLook = 45f;
    private float originalMaxVerticalLook = 45f;
    private bool hasCapturedOriginals;
    private float currentTargetZoomFov;
    private bool suppressAutoZoomUntilPlayerMoves;
    private Vector3 suppressedAtPlayerPos;

    private Vector3 seatedPlayerForward;
    private bool hasRecordedSeatedForward;

    private static TableCameraZoomController activeZoomController;

    private class SavedArrowState
    {
        public MovementArrowUI arrow;
        public Vector2 originalPadding;
        public bool wasActive;
    }

    private readonly List<SavedArrowState> suppressedArrows = new List<SavedArrowState>();

    private void Awake()
    {
        // Enforce comfortable full-table framing if an older serialized instance had aggressive zoom values
        if (zoomedFieldOfView < 52f)
        {
            zoomedFieldOfView = 59f;
        }
        if (localZoomPosition.z > -0.95f)
        {
            localZoomPosition = new Vector3(0f, 1.24f, -1.08f);
        }
        if (zoomPitchAngle > 22f)
        {
            zoomPitchAngle = 18f;
        }

        currentTargetZoomFov = zoomedFieldOfView;
        EnsureReferences();
        EnsureZoomAnchor();
    }

    private void EnsureReferences()
    {
        if (playerController == null)
        {
            playerController = FindFirstObjectByType<GridCrawlerController>();
        }

        if (smoothCamera == null)
        {
            if (playerController != null && playerController.cameraScript != null)
            {
                smoothCamera = playerController.cameraScript;
            }
            else
            {
                smoothCamera = FindFirstObjectByType<SmoothCameraFollow>();
            }
        }

        if (targetCamera == null)
        {
            if (smoothCamera != null)
            {
                targetCamera = smoothCamera.GetComponent<Camera>();
                if (targetCamera == null)
                {
                    targetCamera = smoothCamera.GetComponentInChildren<Camera>();
                }
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }
    }

    private void EnsureZoomAnchor()
    {
        if (zoomAnchor == null)
        {
            Transform existing = transform.Find("CameraZoomAnchor");
            if (existing != null)
            {
                zoomAnchor = existing;
            }
            else
            {
                GameObject anchorObj = new GameObject("CameraZoomAnchor");
                anchorObj.transform.SetParent(transform, false);
                zoomAnchor = anchorObj.transform;
            }
        }

        zoomAnchor.localPosition = localZoomPosition;
        zoomAnchor.localRotation = Quaternion.Euler(zoomPitchAngle, 0f, 0f);
        zoomAnchor.localScale = Vector3.one;
    }

    private void CaptureOriginalCameraSettingsIfNeeded()
    {
        EnsureReferences();
        if (hasCapturedOriginals) return;

        if (smoothCamera != null && smoothCamera.target != null)
        {
            originalCameraTarget = smoothCamera.target;
            originalMaxHorizontalLook = smoothCamera.maxHorizontalLook;
            originalMaxVerticalLook = smoothCamera.maxVerticalLook;
        }

        if (targetCamera != null)
        {
            originalFieldOfView = targetCamera.fieldOfView;
        }

        hasCapturedOriginals = true;
    }

    public void RequestSessionZoomIn()
    {
        suppressAutoZoomUntilPlayerMoves = false;
        ZoomIn();
    }

    public void RequestSessionZoomOutAndSuppressAuto()
    {
        suppressAutoZoomUntilPlayerMoves = true;
        if (playerController != null)
        {
            suppressedAtPlayerPos = playerController.transform.position;
            seatedPlayerForward = playerController.transform.forward;
            hasRecordedSeatedForward = true;
        }
        ZoomOut();
    }

    public void ZoomIn()
    {
        EnsureReferences();
        EnsureZoomAnchor();

        if (activeZoomController != null && activeZoomController != this && activeZoomController.IsZoomedIn)
        {
            activeZoomController.ZoomOut();
        }

        CaptureOriginalCameraSettingsIfNeeded();

        zoomAnchor.localPosition = localZoomPosition;
        zoomAnchor.localRotation = Quaternion.Euler(zoomPitchAngle, 0f, 0f);
        currentTargetZoomFov = zoomedFieldOfView;

        if (smoothCamera != null && zoomAnchor != null)
        {
            if (smoothCamera.target != zoomAnchor && smoothCamera.target != null)
            {
                originalCameraTarget = smoothCamera.target;
            }
            smoothCamera.target = zoomAnchor;
        }

        if (playerController != null)
        {
            seatedPlayerForward = playerController.transform.forward;
            hasRecordedSeatedForward = true;
        }

        if (!IsZoomedIn)
        {
            IsZoomedIn = true;
            activeZoomController = this;
            if (suppressTurnArrowsWhileZoomed)
            {
                SuppressNavigationArrows();
            }
        }
    }

    public void ZoomOut()
    {
        if (!IsZoomedIn) return;
        IsZoomedIn = false;

        if (activeZoomController == this)
        {
            activeZoomController = null;
        }

        RestoreOriginalCameraTarget();
        RestoreNavigationArrows();
    }

    private void RestoreOriginalCameraTarget()
    {
        if (smoothCamera != null)
        {
            if (originalCameraTarget != null)
            {
                smoothCamera.target = originalCameraTarget;
            }
            smoothCamera.maxHorizontalLook = originalMaxHorizontalLook;
            smoothCamera.maxVerticalLook = originalMaxVerticalLook;
        }
    }

    private void Update()
    {
        EnsureReferences();

        if (IsZoomedIn)
        {
            if (zoomAnchor != null)
            {
                zoomAnchor.localPosition = localZoomPosition;
                zoomAnchor.localRotation = Quaternion.Euler(zoomPitchAngle, 0f, 0f);
            }

            if (allowScrollWheelZoom)
            {
                float scrollDelta = GetMouseScrollDeltaY();
                if (Mathf.Abs(scrollDelta) > 0.01f)
                {
                    currentTargetZoomFov = Mathf.Clamp(
                        currentTargetZoomFov - scrollDelta * 2.2f,
                        minScrollFieldOfView,
                        maxScrollFieldOfView);
                }
            }

            // Automatically zoom out if the player walks away or rotates away from the table
            if (playerController != null)
            {
                Vector3 playerFlat = new Vector3(playerController.transform.position.x, 0f, playerController.transform.position.z);
                Vector3 tableFlat = new Vector3(transform.position.x, 0f, transform.position.z);
                float flatDist = Vector3.Distance(playerFlat, tableFlat);

                bool movedTooFar = flatDist > maxSessionDistance;
                bool turnedAway = false;
                if (hasRecordedSeatedForward)
                {
                    float angle = Vector3.Angle(playerController.transform.forward, seatedPlayerForward);
                    if (angle > 55f)
                    {
                        turnedAway = true;
                    }
                }

                if (movedTooFar || turnedAway)
                {
                    ZoomOut();
                    return;
                }
            }

            if (targetCamera != null)
            {
                targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, currentTargetZoomFov, Time.deltaTime * fovTransitionSpeed);
            }

            if (smoothCamera != null)
            {
                smoothCamera.maxHorizontalLook = Mathf.Lerp(smoothCamera.maxHorizontalLook, zoomedMaxHorizontalLook, Time.deltaTime * fovTransitionSpeed);
                smoothCamera.maxVerticalLook = Mathf.Lerp(smoothCamera.maxVerticalLook, zoomedMaxVerticalLook, Time.deltaTime * fovTransitionSpeed);
            }
        }
        else
        {
            if (enableAutoZoomOnApproach && playerController != null)
            {
                Vector3 playerFlat = new Vector3(playerController.transform.position.x, 0f, playerController.transform.position.z);
                Vector3 tableFlat = new Vector3(transform.position.x, 0f, transform.position.z);
                float flatDist = Vector3.Distance(playerFlat, tableFlat);

                if (suppressAutoZoomUntilPlayerMoves)
                {
                    float movedSinceSuppress = Vector3.Distance(playerController.transform.position, suppressedAtPlayerPos);
                    float turnedSinceSuppress = hasRecordedSeatedForward
                        ? Vector3.Angle(playerController.transform.forward, seatedPlayerForward)
                        : 0f;
                    if (movedSinceSuppress > 0.35f || turnedSinceSuppress > 45f)
                    {
                        suppressAutoZoomUntilPlayerMoves = false;
                    }
                }

                if (!suppressAutoZoomUntilPlayerMoves && flatDist <= triggerHorizontalDistance)
                {
                    Vector3 toTableDir = (tableFlat - playerFlat).normalized;
                    float facingAngle = Vector3.Angle(playerController.transform.forward, toTableDir);
                    if (facingAngle <= triggerFacingAngle && (activeZoomController == null || !activeZoomController.IsZoomedIn))
                    {
                        ZoomIn();
                        return;
                    }
                }
            }

            if (hasCapturedOriginals && targetCamera != null && Mathf.Abs(targetCamera.fieldOfView - originalFieldOfView) > 0.05f)
            {
                targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, originalFieldOfView, Time.deltaTime * fovTransitionSpeed);
                if (Mathf.Abs(targetCamera.fieldOfView - originalFieldOfView) <= 0.08f)
                {
                    targetCamera.fieldOfView = originalFieldOfView;
                }
            }
        }
    }

    private float GetMouseScrollDeltaY()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            float val = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(val) > 10f) val /= 120f;
            return val;
        }
#endif
        return Input.mouseScrollDelta.y;
    }

    private void OnDisable()
    {
        if (IsZoomedIn)
        {
            IsZoomedIn = false;
            if (activeZoomController == this)
            {
                activeZoomController = null;
            }
            RestoreOriginalCameraTarget();
            if (targetCamera != null && hasCapturedOriginals)
            {
                targetCamera.fieldOfView = originalFieldOfView;
            }
            RestoreNavigationArrows();
        }
    }

    private void SuppressNavigationArrows()
    {
        suppressedArrows.Clear();
        MovementArrowUI[] arrows = FindObjectsByType<MovementArrowUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < arrows.Length; i++)
        {
            MovementArrowUI arrow = arrows[i];
            if (arrow == null) continue;

            suppressedArrows.Add(new SavedArrowState
            {
                arrow = arrow,
                originalPadding = arrow.hitboxPadding,
                wasActive = arrow.gameObject.activeSelf
            });

            arrow.hitboxPadding = new Vector2(-10000f, -10000f);
            arrow.SetHoverState(false);
            arrow.gameObject.SetActive(false);
        }
    }

    private void RestoreNavigationArrows()
    {
        for (int i = 0; i < suppressedArrows.Count; i++)
        {
            SavedArrowState saved = suppressedArrows[i];
            if (saved != null && saved.arrow != null)
            {
                saved.arrow.hitboxPadding = saved.originalPadding;
                saved.arrow.gameObject.SetActive(saved.wasActive);
                saved.arrow.SetHoverState(false);
            }
        }
        suppressedArrows.Clear();
    }
}
