with open('Assets/Scripts/BarSystems/SurvivalAction.cs', 'r', encoding='utf-8') as f:
    text = f.read()

# Add needs full check to PerformAction
target1 = """        // If this is the Eat action, verify the player actually has enough money BEFORE action or hold starts!

        if (actionType == ActionType.Eat)

        {"""

replacement1 = """        // If this is the Eat action, verify the player actually has enough money BEFORE action or hold starts!

        if (actionType == ActionType.Eat)

        {

            if (barManager != null && barManager.currentNeeds >= barManager.maxNeeds - 0.01f)
            {
                if (audioSource != null && cantAffordSound != null)
                {
                    audioSource.PlayOneShot(cantAffordSound);
                }
                if (foodPriceDisplay != null)
                {
                    foodPriceDisplay.ShowCustomFeedback("Needs full! Use toilet first.", 1.5f);
                }
                return;
            }"""

if target1 in text:
    text = text.replace(target1, replacement1)
    print("Replaced in PerformAction!")
else:
    print("target1 not found")

target2 = """                if (foodPriceDisplay != null)

                {

                    foodPriceDisplay.ShowNotEnoughMoneyFeedback(1.2f);

                }"""

replacement2 = """                if (foodPriceDisplay != null)

                {

                    if (barManager != null && barManager.currentNeeds >= barManager.maxNeeds - 0.01f)
                    {
                        foodPriceDisplay.ShowCustomFeedback("Needs full! Use toilet first.", 1.5f);
                    }
                    else
                    {
                        foodPriceDisplay.ShowNotEnoughMoneyFeedback(1.2f);
                    }

                }"""

if target2 in text:
    text = text.replace(target2, replacement2, 1)
    print("Replaced in CompleteEat!")
else:
    print("target2 not found")

with open('Assets/Scripts/BarSystems/SurvivalAction.cs', 'w', encoding='utf-8') as f:
    f.write(text)

print("SurvivalAction.cs successfully updated!")
