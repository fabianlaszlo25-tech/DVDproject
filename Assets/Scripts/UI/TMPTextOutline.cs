using UnityEngine;
using TMPro;

/// <summary>
/// Provides a clean, perfectly aligned semi-transparent outline for TextMeshPro text
/// by utilizing TMP's native Signed Distance Field (SDF) shader per-glyph distance calculation.
/// This prevents letter-drift or misalignment caused by geometry duplication or quad scaling.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class TMPTextOutline : MonoBehaviour
{
    [Range(0f, 1f)]
    [Tooltip("Thickness of the glyph outline.")]
    public float outlineWidth = 0.22f;

    [Tooltip("Semi-transparent outline color.")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.72f);

    [Range(0f, 1f)]
    [Tooltip("Softness/anti-aliasing of the outline edge.")]
    public float outlineSoftness = 0.05f;

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
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, outlineSoftness);
            tmpText.UpdateMeshPadding();
        }
    }
}
