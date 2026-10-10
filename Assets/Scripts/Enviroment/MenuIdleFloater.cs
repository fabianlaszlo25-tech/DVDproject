using UnityEngine;

public class MenuIdleFloater : MonoBehaviour
{
    [Header("Float")]
    public float bobSpeed = 2f;
    public float bobHeight = 0.1f;

    [Header("Blob")]
    public float blobSpeed = 3f;
    public float blobAmount = 0.05f;

    [Header("Rotation")]
    public Vector3 rotationAxis = new Vector3(0f, 1f, 0f);
    public float rotationSpeed = 2f;
    public float rotationAngle = 15f;

    private Vector3 startPos;
    private Vector3 baseScale;
    private Quaternion baseRotation;

    void Start()
    {
        startPos = transform.localPosition;
        baseScale = transform.localScale;
        baseRotation = transform.localRotation;
    }

    void Update()
    {
        float newY = startPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.localPosition = new Vector3(startPos.x, newY, startPos.z);

        float scaleOffset = Mathf.Sin(Time.time * blobSpeed) * blobAmount;
        transform.localScale = baseScale * (1f + scaleOffset);

        float angle = Mathf.Sin(Time.time * rotationSpeed) * rotationAngle;
        transform.localRotation = baseRotation * Quaternion.AngleAxis(angle, rotationAxis);
    }
}