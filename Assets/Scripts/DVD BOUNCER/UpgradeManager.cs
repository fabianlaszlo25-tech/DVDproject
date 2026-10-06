using UnityEngine;
using System.Collections.Generic;

public enum UpgradeCategory { MoneyMultiplier, SpeedMultiplier }

[System.Serializable]
public class UpgradeTier
{
    public string tierName;
    public float cost;
    public float multiplierValue;

    [Tooltip("The 3D model spawned in the UI menu")]
    public GameObject previewPrefab;

    [Tooltip("Shift the model if it doesn't spawn perfectly centered in your UI")]
    public Vector3 previewPositionOffset = Vector3.zero;

    [Tooltip("The exact scale for the UI preview. World Space UIs usually require large numbers (e.g., 50, 50, 50)")]
    public Vector3 previewScale = new Vector3(50f, 50f, 50f);

    [Tooltip("The base starting rotation (Euler Angles) for the UI preview")]
    public Vector3 previewRotation = Vector3.zero;

    [Tooltip("The objects to enable in the world when purchased (e.g., next DVD in stack, or new Player model)")]
    public GameObject[] worldVisuals;
}

[System.Serializable]
public class UpgradePath
{
    public string pathName;
    public UpgradeCategory category;

    [Tooltip("Where should the preview model fly to when bought?")]
    public Transform animationTargetPoint;

    public List<UpgradeTier> tiers;
    public int CurrentLevel { get; private set; } = 0;

    public void InitializeWorldVisuals()
    {
        for (int i = 0; i < tiers.Count; i++)
        {
            foreach (var visual in tiers[i].worldVisuals)
            {
                if (visual != null) visual.SetActive(i == CurrentLevel);
            }
        }
    }

    public void SetLevel(int level)
    {
        CurrentLevel = level;
        InitializeWorldVisuals();
    }
}

public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    public DVDLogoBouncer bouncer;
    public List<UpgradePath> upgradePaths;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        foreach (var path in upgradePaths)
        {
            path.InitializeWorldVisuals();
        }
    }

    public bool TrySpendForUpgrade(UpgradePath path, int targetIndex)
    {
        if (targetIndex >= path.tiers.Count || targetIndex <= path.CurrentLevel) return false;

        decimal cost = (decimal)path.tiers[targetIndex].cost;
        return bouncer.TrySpendMoney(cost);
    }

    public void FinalizeUpgrade(UpgradePath path, int targetIndex)
    {
        path.SetLevel(targetIndex);

        float newMultiplier = path.tiers[targetIndex].multiplierValue;
        if (path.category == UpgradeCategory.MoneyMultiplier)
            bouncer.SetMoneyMultiplier(newMultiplier);
        else if (path.category == UpgradeCategory.SpeedMultiplier)
            bouncer.SetSpeedMultiplier(newMultiplier);
    }
}