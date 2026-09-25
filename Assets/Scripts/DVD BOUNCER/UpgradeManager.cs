using UnityEngine;

[System.Serializable]
public struct DVDPileTier
{
    public string tierName;
    public float cost;
    public float multiplierValue;

    [Tooltip("The GameObject to enable when this tier is purchased (e.g., the next DVD in the stack)")]
    public GameObject dvdVisual;
}

[System.Serializable]
public struct DVDPlayerTier
{
    public string tierName;
    public float cost;
    public float multiplierValue;

    [Tooltip("The 3D model of the DVD player for this tier")]
    public GameObject playerModel;

    [Tooltip("The parent GameObject containing the 2 logo meshes for this tier")]
    public GameObject logoVisualParent;
}

public class UpgradeManager : MonoBehaviour
{
    public DVDLogoBouncer bouncer;

    [Header("DVD Pile (Money Upgrades)")]
    public DVDPileTier[] dvdPileTiers;
    private int currentDvdIndex = 0;

    [Header("DVD Player (Speed Upgrades)")]
    public DVDPlayerTier[] dvdPlayerTiers;
    private int currentPlayerIndex = 0;

    void Start()
    {
        InitializeVisuals();
    }

    private void InitializeVisuals()
    {
        // Set up the DVD Pile: Only the first tier's visual is active
        for (int i = 0; i < dvdPileTiers.Length; i++)
        {
            if (dvdPileTiers[i].dvdVisual != null)
            {
                dvdPileTiers[i].dvdVisual.SetActive(i == currentDvdIndex);
            }
        }

        // Set up the DVD Player and Logo: Only the first tier's visuals are active
        for (int i = 0; i < dvdPlayerTiers.Length; i++)
        {
            if (dvdPlayerTiers[i].playerModel != null)
            {
                dvdPlayerTiers[i].playerModel.SetActive(i == currentPlayerIndex);
            }

            if (dvdPlayerTiers[i].logoVisualParent != null)
            {
                dvdPlayerTiers[i].logoVisualParent.SetActive(i == currentPlayerIndex);
            }
        }
    }

    public void TryUpgradeDVD()
    {
        // Check if maxed out
        if (currentDvdIndex >= dvdPileTiers.Length - 1)
        {
            Debug.Log("DVD Pile maxed out.");
            return;
        }

        // We check the cost of the *next* tier
        int nextIndex = currentDvdIndex + 1;
        decimal cost = (decimal)dvdPileTiers[nextIndex].cost;

        if (bouncer.TrySpendMoney(cost))
        {
            // Activate the new DVD visual
            if (dvdPileTiers[nextIndex].dvdVisual != null)
            {
                dvdPileTiers[nextIndex].dvdVisual.SetActive(true);
            }

            // If you want the old one to turn OFF instead of stacking, uncomment the line below:
            // if (dvdPileTiers[currentDvdIndex].dvdVisual != null) dvdPileTiers[currentDvdIndex].dvdVisual.SetActive(false);

            currentDvdIndex = nextIndex;
            bouncer.SetMoneyMultiplier(dvdPileTiers[currentDvdIndex].multiplierValue);
        }
    }

    public void TryUpgradePlayer()
    {
        // Check if maxed out
        if (currentPlayerIndex >= dvdPlayerTiers.Length - 1)
        {
            Debug.Log("DVD Player maxed out.");
            return;
        }

        int nextIndex = currentPlayerIndex + 1;
        decimal cost = (decimal)dvdPlayerTiers[nextIndex].cost;

        if (bouncer.TrySpendMoney(cost))
        {
            // Deactivate old visuals
            if (dvdPlayerTiers[currentPlayerIndex].playerModel != null)
                dvdPlayerTiers[currentPlayerIndex].playerModel.SetActive(false);

            if (dvdPlayerTiers[currentPlayerIndex].logoVisualParent != null)
                dvdPlayerTiers[currentPlayerIndex].logoVisualParent.SetActive(false);

            // Activate new visuals
            if (dvdPlayerTiers[nextIndex].playerModel != null)
                dvdPlayerTiers[nextIndex].playerModel.SetActive(true);

            if (dvdPlayerTiers[nextIndex].logoVisualParent != null)
                dvdPlayerTiers[nextIndex].logoVisualParent.SetActive(true);

            currentPlayerIndex = nextIndex;
            bouncer.SetSpeedMultiplier(dvdPlayerTiers[currentPlayerIndex].multiplierValue);
        }
    }
}