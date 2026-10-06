using UnityEngine;

public class FloatingPreview : MonoBehaviour
{
    public float rotateSpeed = 45f;
    public float bobSpeed = 2f;
    public float bobHeight = 0.2f;

    private Vector3 startLocalPos;
    private Vector3 baseRotation;
    private float currentSpin = 0f;

    void Start()
    {
        // Record the exact starting offset and tilt set by the Menu script
        startLocalPos = transform.localPosition;
        baseRotation = transform.localEulerAngles;
    }

    void Update()
    {
        currentSpin += rotateSpeed * Time.deltaTime;

        // Quaternion multiplication order matters: 
        // This tilts the object FIRST (your custom rotation), 
        // and THEN spins it around the UI's vertical Y-axis (left-to-right).
        transform.localRotation = Quaternion.Euler(0f, currentSpin, 0f) * Quaternion.Euler(baseRotation);

        // Strictly lock the X and Z position to the offset, only bobbing on the Y axis
        float newY = startLocalPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.localPosition = new Vector3(startLocalPos.x, newY, startLocalPos.z);
    }
}