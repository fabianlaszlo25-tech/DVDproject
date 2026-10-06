using UnityEngine;

public class FloatingPreview : MonoBehaviour
{
    public float rotateSpeed = 45f;
    public float bobSpeed = 2f;
    public float bobHeight = 0.2f;
    private float startY;

    void Start() => startY = transform.localPosition.y;

    void Update()
    {
        transform.Rotate(Vector3.up * rotateSpeed * Time.deltaTime, Space.Self);
        float newY = startY + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.localPosition = new Vector3(transform.localPosition.x, newY, transform.localPosition.z);
    }
}