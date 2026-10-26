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

    private float mouseX = 0f;
    private float mouseY = 0f;
    private Quaternion currentBodyRotation;

    void Awake()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SnapToTargetImmediate();
    }

    void OnEnable()
    {
        mouseSensitivity = GlobalAudioSettings.MouseSensitivity;
        SnapToTargetImmediate();
    }

    void Start()
    {
        mouseSensitivity = GlobalAudioSettings.MouseSensitivity;
        SnapToTargetImmediate();
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

        if (target != null)
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

        // 1. Tightly follow the player.
        transform.position = Vector3.Lerp(transform.position, target.position + offset, Time.deltaTime * positionLerpSpeed);

        // 2. Camera Rotation Logic
        if (isAutoCentering)
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

        // 3. Combine body rotation with mapped local look
        currentBodyRotation = Quaternion.Slerp(currentBodyRotation, target.rotation, Time.deltaTime * bodyRotationLerpSpeed);
        Quaternion localLookRotation = Quaternion.Euler(-mouseY, mouseX, 0f);
        transform.rotation = currentBodyRotation * localLookRotation;
    }
}
