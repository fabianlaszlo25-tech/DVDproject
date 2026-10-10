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
        startLocalPos = transform.localPosition;
        baseRotation = transform.localEulerAngles;
    }

    void Update()
    {
        currentSpin += rotateSpeed * Time.deltaTime;

        transform.localRotation = Quaternion.Euler(0f, currentSpin, 0f) * Quaternion.Euler(baseRotation);

        float newY = startLocalPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.localPosition = new Vector3(startLocalPos.x, newY, startLocalPos.z);
    }
}