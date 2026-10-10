using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class WorldSpaceCanvasFitter : MonoBehaviour
{
    public Camera targetCamera;

    [Tooltip("How far in front of the camera the UI sits")]
    public float planeDistance = 2f;

    [Tooltip("The reference height of your UI (Standard is 1080)")]
    public float referenceHeight = 1080f;

    private RectTransform rectTransform;

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    void LateUpdate()
    {
        if (targetCamera == null || rectTransform == null) return;

        // 1. Snap position and rotation to the Camera lens
        Transform camT = targetCamera.transform;
        transform.position = camT.position + camT.forward * planeDistance;
        transform.rotation = camT.rotation;

        // 2. Adjust internal resolution to match the screen's Aspect Ratio
        float currentAspect = targetCamera.aspect;
        rectTransform.sizeDelta = new Vector2(referenceHeight * currentAspect, referenceHeight);

        // 3. Physically scale the Canvas to fit the Camera's field of view bounds
        if (targetCamera.orthographic)
        {
            float frustumHeight = targetCamera.orthographicSize * 2f;
            float scale = frustumHeight / referenceHeight;
            rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            float frustumHeight = 2.0f * planeDistance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float scale = frustumHeight / referenceHeight;
            rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}