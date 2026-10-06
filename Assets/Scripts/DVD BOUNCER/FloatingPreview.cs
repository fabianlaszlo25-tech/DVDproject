using UnityEngine;

public class FloatingPreview : MonoBehaviour
{
    public float rotateSpeed = 45f;
    public float bobSpeed = 2f;
    public float bobHeight = 0.2f;

    private Vector3 startLocalPos;

    void Start()
    {
        // Record the position after the offset from the Tier settings has been applied
        startLocalPos = transform.localPosition;
    }

    void Update()
    {
        // Space.Self ensures it rotates around its own tilted axis (e.g., spinning like a wheel)
        transform.Rotate(Vector3.up * rotateSpeed * Time.deltaTime, Space.Self);

        // Lock X and Z to the exact starting offset, only modifying the Y axis for the bobbing
        float newY = startLocalPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.localPosition = new Vector3(startLocalPos.x, newY, startLocalPos.z);
    }
}