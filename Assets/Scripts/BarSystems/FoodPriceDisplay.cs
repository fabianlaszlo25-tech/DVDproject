using UnityEngine;

[RequireComponent(typeof(InteractableObject))]
public class FoodPriceDisplay : MonoBehaviour
{
    public BarManager barManager;
    public string notEnoughMoneyMessage = "Not enough money!";

    private InteractableObject interactable;
    private float denyMessageTimer = 0f;

    private void Start()
    {
        interactable = GetComponent<InteractableObject>();
        if (barManager == null) barManager = FindObjectOfType<BarManager>();
    }

    private void Update()
    {
        if (interactable == null || barManager == null) return;

        decimal currentCost = barManager.GetCurrentFoodCost();

        if (denyMessageTimer > 0f)
        {
            denyMessageTimer -= Time.unscaledDeltaTime;
            interactable.hoverText = $"Eat Food (${currentCost:0.00})\n{notEnoughMoneyMessage}";
        }
        else
        {
            interactable.hoverText = $"Eat Food (${currentCost:0.00})";
        }
    }

    public void ShowNotEnoughMoneyFeedback(float duration = 1.2f)
    {
        denyMessageTimer = Mathf.Max(0.2f, duration);
    }
}