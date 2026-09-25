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

    [HideInInspector] public bool isAutoCentering = false;

    private float mouseX = 0f;
    private float mouseY = 0f;
    private Quaternion currentBodyRotation;

    void Awake()
    {
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;
    }

    void Start()
    {
        if (target != null)
        {
            transform.position = target.position + offset;
            currentBodyRotation = target.rotation;
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

            mouseX = Mathf.Lerp(mouseX, targetX, Time.deltaTime * 10f);
            mouseY = Mathf.Lerp(mouseY, targetY, Time.deltaTime * 10f);
        }

        // 3. Combine body rotation with mapped local look
        currentBodyRotation = Quaternion.Slerp(currentBodyRotation, target.rotation, Time.deltaTime * bodyRotationLerpSpeed);
        Quaternion localLookRotation = Quaternion.Euler(-mouseY, mouseX, 0f);
        transform.rotation = currentBodyRotation * localLookRotation;
    }
}