using UnityEngine;
using TMPro;

/// <summary>
/// Provides a solid black, crisp outline for TextMeshPro text that expands strictly OUTWARD
/// from the letters without eating into or thinning the font face, and without washed-out gray softness.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class TMPTextOutline : MonoBehaviour
{
    [Range(0f, 1f)]
    [Tooltip("Thickness of the glyph outline expanding outward.")]
    public float outlineWidth = 0.28f;

    [Tooltip("Solid outline color (pure black by default).")]
    public Color outlineColor = Color.black;

    [Range(0f, 1f)]
    [Tooltip("Edge softness (keep near 0 - 0.02 for deep crisp black without fuzzy gray bleed).")]
    public float outlineSoftness = 0.02f;

    [Range(0f, 1f)]
    [Tooltip("Protects the letter face thickness so the outline does not eat into the characters.")]
    public float faceDilate = 0.05f;

    private TMP_Text tmpText;

    private void Awake()
    {
        ApplyOutline();
    }

    private void OnEnable()
    {
        ApplyOutline();
    }

    private void OnValidate()
    {
        ApplyOutline();
    }

    public void ApplyOutline()
    {
        if (tmpText == null) tmpText = GetComponent<TMP_Text>();
        if (tmpText == null) return;

        Material mat = tmpText.fontMaterial;
        if (mat != null)
        {
            // Face dilation keeps the inner letter thick and crisp
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, faceDilate);

            // 1. Crisp solid black Outline
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, outlineSoftness);

            // 2. Underlay with offset (0, 0) renders strictly behind the glyph face,
            // expanding purely outward into empty space without any inward encroachment.
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, outlineColor);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, outlineWidth);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, outlineSoftness);

            tmpText.UpdateMeshPadding();
        }
    }
}
