using UnityEngine;

[RequireComponent(typeof(InteractableObject))]
public class FoodPriceDisplay : MonoBehaviour
{
    public BarManager barManager;

    [Header("Food Configuration")]
    [Tooltip("Name of this food / eating method shown on hover.")]
    public string foodName = "Eat Food";
    [Tooltip("Base price of this food before inflation multiplier.")]
    public float basePrice = 10f;
    [Tooltip("Prompt displayed under the price. E.g. 'Hold to Eat'. Leave empty to omit.")]
    public string holdPrompt = "Hold to Eat";
    [Tooltip("Message displayed when clicked without enough money.")]
    public string notEnoughMoneyMessage = "Not enough money!";

    private InteractableObject interactable;
    private SurvivalAction survivalAction;
    private float denyMessageTimer = 0f;

    private void Start()
    {
        interactable = GetComponent<InteractableObject>();
        survivalAction = GetComponent<SurvivalAction>();
        if (barManager == null) barManager = FindObjectOfType<BarManager>();

        if (survivalAction != null && survivalAction.actionType == SurvivalAction.ActionType.Eat)
        {
            if (basePrice > 0f)
            {
                survivalAction.baseFoodCost = basePrice;
            }
            else if (survivalAction.baseFoodCost > 0f)
            {
                basePrice = survivalAction.baseFoodCost;
            }

            if (!string.IsNullOrEmpty(survivalAction.foodName))
            {
                foodName = survivalAction.foodName;
            }
            else
            {
                survivalAction.foodName = foodName;
            }
        }
    }

    private void Update()
    {
        if (interactable == null) return;

        decimal currentCost = GetCurrentCost();

        if (denyMessageTimer > 0f)
        {
            denyMessageTimer -= Time.unscaledDeltaTime;
            interactable.hoverText = $"{foodName} (${currentCost:0.00})\n{notEnoughMoneyMessage}";
        }
        else
        {
            if (!string.IsNullOrEmpty(holdPrompt))
            {
                interactable.hoverText = $"{foodName} (${currentCost:0.00})\n({holdPrompt})";
            }
            else
            {
                interactable.hoverText = $"{foodName} (${currentCost:0.00})";
            }
        }
    }

    public decimal GetCurrentCost()
    {
        if (barManager != null)
        {
            return barManager.GetFoodCost(basePrice);
        }
        return (decimal)basePrice;
    }

    public void ShowNotEnoughMoneyFeedback(float duration = 1.2f)
    {
        denyMessageTimer = Mathf.Max(0.2f, duration);
    }
}
