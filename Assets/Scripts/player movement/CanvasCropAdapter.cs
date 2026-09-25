using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class CanvasCropAdapter : MonoBehaviour
{
    [Header("Crop Margins (0.0 to 1.0)")]
    [Tooltip("0.1 means 10% of the screen is cropped on that edge.")]
    public float topCrop = 0.1f;
    public float bottomCrop = 0.1f;
    public float leftCrop = 0f;
    public float rightCrop = 0f;

    void Start()
    {
        ApplyCrop();
    }

    void OnValidate()
    {
        ApplyCrop();
    }

    private void ApplyCrop()
    {
        RectTransform rt = GetComponent<RectTransform>();

        // Adjust the anchors based on the crop percentages
        rt.anchorMin = new Vector2(leftCrop, bottomCrop);
        rt.anchorMax = new Vector2(1f - rightCrop, 1f - topCrop);

        // Zero out the offsets so the Panel perfectly matches the new anchors
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}