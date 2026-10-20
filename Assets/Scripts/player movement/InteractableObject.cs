using UnityEngine;
using UnityEngine.Events;

public class InteractableObject : MonoBehaviour
{
    [TextArea]
    public string hoverText = "Interact";

    [Tooltip("Drag the script/functions you want to trigger here.")]
    public UnityEvent onClick;

    /// <summary>Set by other scripts (e.g. while holding a click) to hide the hover text.</summary>
    [HideInInspector] public bool hideHoverText = false;
}