using UnityEngine;
using UnityEngine.InputSystem;

public class SmoothCameraFollow : MonoBehaviour
{
    [Header("Follow Settings")]
    public Transform target;
    public Vector3 offset = new Vector3(0, 0.6f, 0);

    public float positionLerpSpeed = 35f;
    public float bodyRotationLerpSpeed = 15f;

    [Header("Screen Look Settings")]
    public GridCrawlerController controller;
    public float maxHorizontalLook = 25f;
    public float maxVerticalLook = 20f;
    public float recenterSpeed = 25f;

    [Header("Mouse Sensitivity")]
    [Tooltip("Mouse sensitivity multiplier for camera rotation (governed globally by settings).")]
    public float mouseSensitivity = 1.0f;

    [HideInInspector] public bool isAutoCentering = false;
    [HideInInspector] public bool lockCameraLook = false;

    [Header("TV Focus Mode Settings")]
    [Tooltip("Interpolation speed when zooming into or out of TV Focus Mode.")]
    public float focusLerpSpeed = 9f;
    [Tooltip("Default camera field of view in normal exploration mode.")]
    public float defaultFOV = 65f;

    private float mouseX = 0f;
    private float mouseY = 0f;
    private Quaternion currentBodyRotation;
    private Camera cam;

    private bool isFocusMode = false;
    private Vector3 focusWorldPos;
    private Quaternion focusWorldRot;
    private float focusTargetFOV = 36f;

    private bool isExitingFocusTransition = false;
    private float exitTransitionElapsed = 0f;
    private float exitTransitionDuration = 0f;
    private Vector3 exitStartPos;
    private Quaternion exitStartRot;
    private float exitStartFOV;

    public bool IsInFocusMode => isFocusMode;
    public bool IsExitingFocusTransition => isExitingFocusTransition;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam != null && cam.fieldOfView > 1f)
        {
            defaultFOV = cam.fieldOfView;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SnapToTargetImmediate();
    }

    void OnEnable()
    {
        if (cam == null) cam = GetComponent<Camera>();
        mouseSensitivity = GlobalAudioSettings.MouseSensitivity;
        SnapToTargetImmediate();
    }

    void Start()
    {
        if (cam == null) cam = GetComponent<Camera>();
        mouseSensitivity = GlobalAudioSettings.MouseSensitivity;
        SnapToTargetImmediate();
    }

    /// <summary>
    /// Enters TV Focus Mode, locking camera rotation and smoothly zooming/positioning
    /// the camera so the TV screen fills ~85-90% of the viewport.
    /// </summary>
    public void EnterFocusMode(Vector3 worldPos, Quaternion worldRot, float targetFOV)
    {
        if (cam == null) cam = GetComponent<Camera>();
        isFocusMode = true;
        isExitingFocusTransition = false;
        focusWorldPos = worldPos;
        focusWorldRot = worldRot;
        focusTargetFOV = targetFOV;
        mouseX = 0f;
        mouseY = 0f;
    }

    /// <summary>
    /// Enters TV Focus Mode immediately on frame 1 without lerping from the player position.
    /// Used at the start of the tutorial so the game launches already focused on the TV.
    /// </summary>
    public void EnterFocusModeImmediate(Vector3 worldPos, Quaternion worldRot, float targetFOV)
    {
        if (cam == null) cam = GetComponent<Camera>();
        isFocusMode = true;
        isExitingFocusTransition = false;
        focusWorldPos = worldPos;
        focusWorldRot = worldRot;
        focusTargetFOV = targetFOV;
        mouseX = 0f;
        mouseY = 0f;

        transform.position = worldPos;
        transform.rotation = worldRot;
        if (cam != null)
        {
            cam.fieldOfView = targetFOV;
        }
    }

    /// <summary>
    /// Exits TV Focus Mode and smoothly returns the camera FOV, position, and rotation to normal.
    /// When smoothDuration > 0, position, rotation, and FOV all interpolate smoothly in sync over that duration.
    /// </summary>
    public void ExitFocusMode(float smoothDuration = 0f)
    {
        if (cam == null) cam = GetComponent<Camera>();
        isFocusMode = false;
        mouseX = 0f;
        mouseY = 0f;

        if (smoothDuration > 0.01f)
        {
            isExitingFocusTransition = true;
            exitTransitionElapsed = 0f;
            exitTransitionDuration = smoothDuration;
            exitStartPos = transform.position;
            exitStartRot = transform.rotation;
            exitStartFOV = cam != null ? cam.fieldOfView : defaultFOV;
        }
        else
        {
            isExitingFocusTransition = false;
        }
    }

    /// <summary>
    /// Snaps the camera transform and rotation immediately to match the target and body rotation.
    /// Resets mouse look deltas so no interpolation lag or sudden camera teleports occur.
    /// </summary>
    public void SnapToTargetImmediate()
    {
        if (target == null)
        {
            GridCrawlerController player = FindObjectOfType<GridCrawlerController>();
            if (player != null)
            {
                target = player.transform;
                if (controller == null) controller = player;
            }
        }

        if (target != null && !isFocusMode)
        {
            transform.position = target.position + offset;
            currentBodyRotation = target.rotation;
            transform.rotation = target.rotation;
            mouseX = 0f;
            mouseY = 0f;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (isFocusMode)
        {
            transform.position = Vector3.Lerp(transform.position, focusWorldPos, Time.deltaTime * focusLerpSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, focusWorldRot, Time.deltaTime * focusLerpSpeed);
            if (cam != null)
            {
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, focusTargetFOV, Time.deltaTime * focusLerpSpeed);
            }
            return;
        }

        if (isExitingFocusTransition)
        {
            exitTransitionElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(exitTransitionElapsed / Mathf.Max(0.01f, exitTransitionDuration));
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            Vector3 targetPos = target.position + offset;
            Quaternion targetRot = target.rotation;

            transform.position = Vector3.Lerp(exitStartPos, targetPos, smoothT);
            transform.rotation = Quaternion.Slerp(exitStartRot, targetRot, smoothT);
            currentBodyRotation = targetRot;
            mouseX = 0f;
            mouseY = 0f;

            if (cam != null)
            {
                cam.fieldOfView = Mathf.Lerp(exitStartFOV, defaultFOV, smoothT);
            }

            if (t >= 1f)
            {
                isExitingFocusTransition = false;
                transform.position = targetPos;
                transform.rotation = targetRot;
                if (cam != null) cam.fieldOfView = defaultFOV;
            }
            return;
        }

        // 1. Tightly follow the player.
        transform.position = Vector3.Lerp(transform.position, target.position + offset, Time.deltaTime * positionLerpSpeed);

        // 2. Smoothly restore default FOV when returning from TV Focus Mode.
        if (cam != null && Mathf.Abs(cam.fieldOfView - defaultFOV) > 0.01f)
        {
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, defaultFOV, Time.deltaTime * focusLerpSpeed);
            if (Mathf.Abs(cam.fieldOfView - defaultFOV) <= 0.01f)
            {
                cam.fieldOfView = defaultFOV;
            }
        }

        // 3. Camera Rotation Logic
        if (isAutoCentering || lockCameraLook || GridCrawlerController.InputFrozen)
        {
            mouseX = Mathf.Lerp(mouseX, 0f, Time.deltaTime * recenterSpeed);
            mouseY = Mathf.Lerp(mouseY, 0f, Time.deltaTime * recenterSpeed);
        }
        else if (controller != null)
        {
            // Read the clamped virtual mouse position so camera respects crop bounds
            Vector2 mousePos = controller.virtualMousePos;

            float screenX = Mathf.Clamp((mousePos.x / Screen.width) * 2f - 1f, -1f, 1f);
            float screenY = Mathf.Clamp((mousePos.y / Screen.height) * 2f - 1f, -1f, 1f);

            float targetX = screenX * maxHorizontalLook;
            float targetY = screenY * maxVerticalLook;

            float lerpSpeed = 10f * Mathf.Clamp(mouseSensitivity, 0.2f, 3.0f);
            mouseX = Mathf.Lerp(mouseX, targetX, Time.deltaTime * lerpSpeed);
            mouseY = Mathf.Lerp(mouseY, targetY, Time.deltaTime * lerpSpeed);
        }

        // 4. Combine body rotation with mapped local look
        currentBodyRotation = Quaternion.Slerp(currentBodyRotation, target.rotation, Time.deltaTime * bodyRotationLerpSpeed);
        Quaternion localLookRotation = Quaternion.Euler(-mouseY, mouseX, 0f);
        transform.rotation = currentBodyRotation * localLookRotation;
    }
}
