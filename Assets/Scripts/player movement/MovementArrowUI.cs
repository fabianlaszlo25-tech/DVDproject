using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image), typeof(RectTransform))]
public class MovementArrowUI : MonoBehaviour
{
    public enum ActionType { TurnLeft, TurnRight }
    public ActionType action;
    public GridCrawlerController controller;

    [Header("Hover Settings")]
    public float defaultAlpha = 0.2f;
    public float hoverAlpha = 0.8f;
    public float hoverScale = 1.2f;

    [Header("Hitbox Tuning")]
    [Tooltip("Expands the clickable area outward (in local pixels).")]
    public Vector2 hitboxPadding = new Vector2(30f, 30f);
    [Tooltip("Shifts the center of the clickable area if it feels offset.")]
    public Vector2 hitboxOffset = Vector2.zero;

    public RectTransform Rect { get; private set; }
    private Image img;
    private Vector3 targetScale = Vector3.one;
    private float targetAlpha;

    void Awake()
    {
        Rect = GetComponent<RectTransform>();
        img = GetComponent<Image>();
        targetAlpha = defaultAlpha;
        SetAlpha(targetAlpha);
    }

    void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * 12f);

        Color c = img.color;
        c.a = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * 12f);
        img.color = c;
    }

    // Custom mathematical hitbox that applies your padding and offsets
    public bool CheckHitbox(Vector2 localPoint)
    {
        Rect r = Rect.rect;

        r.position += hitboxOffset;

        r.xMin -= hitboxPadding.x;
        r.xMax += hitboxPadding.x;
        r.yMin -= hitboxPadding.y;
        r.yMax += hitboxPadding.y;

        return r.Contains(localPoint);
    }

    public void SetHoverState(bool isHovering)
    {
        targetScale = isHovering ? Vector3.one * hoverScale : Vector3.one;
        targetAlpha = isHovering ? hoverAlpha : defaultAlpha;
    }

    public void TriggerAction()
    {
        if (controller == null) return;

        switch (action)
        {
            case ActionType.TurnLeft: controller.UI_TurnLeft(); break;
            case ActionType.TurnRight: controller.UI_TurnRight(); break;
        }
    }

    private void SetAlpha(float alpha)
    {
        if (img == null) img = GetComponent<Image>();
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }
}