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

    void Awake()
    {
        ApplyCrop();
        IsolateSubCanvases();
    }

    void Start()
    {
        ApplyCrop();
        IsolateSubCanvases();
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

    /// <summary>
    /// Isolates frequently animated or dirtying UI elements into independent sub-canvases.
    /// This prevents TextMeshPro rebuilds, cursorholder position updates, and hover scaling
    /// from dirtying the parent canvas and causing UV coordinate dropouts on icon images.
    /// </summary>
    private void IsolateSubCanvases()
    {
        // 1. Isolate dynamic cursor and text (moves and scales every frame)
        Transform cursor = transform.Find("cursorholder");
        if (cursor != null) EnsureSubCanvas(cursor.gameObject);

        // 2. Isolate pause button (animates scale/alpha on hover)
        Transform pause = transform.Find("Pause");
        if (pause == null) pause = transform.Find("pause");
        if (pause != null) EnsureSubCanvas(pause.gameObject);

        // 3. Isolate each slider so value changes only redraw that specific slider
        string[] sliders = new string[] { "Sleep", "DIrt", "Hungerr", "NEEDS", "Depressions" };
        foreach (string sName in sliders)
        {
            Transform s = transform.Find(sName);
            if (s != null) EnsureSubCanvas(s.gameObject);
        }
    }

    private static void EnsureSubCanvas(GameObject go)
    {
        if (go.GetComponent<Canvas>() == null)
        {
            Canvas sub = go.AddComponent<Canvas>();
            sub.overrideSorting = false;
        }
    }
}
