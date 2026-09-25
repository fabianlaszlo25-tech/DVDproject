using UnityEngine;

[RequireComponent(typeof(InteractableObject))]
public class FoodPriceDisplay : MonoBehaviour
{
    public BarManager barManager;
    private InteractableObject interactable;

    void Start()
    {
        interactable = GetComponent<InteractableObject>();
    }

    void Update()
    {
        if (barManager != null)
        {
            // Dynamically rewrites the hover text string on your custom script
            interactable.hoverText = $"Eat Food (${barManager.GetCurrentFoodCost():0.00})";
        }
    }
}