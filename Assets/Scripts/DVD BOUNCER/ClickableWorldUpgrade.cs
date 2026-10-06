using UnityEngine;

public class UpgradeTrigger : MonoBehaviour
{
    [Tooltip("0 for Pile, 1 for Player (matching the UpgradeManager list)")]
    public int pathIndex;

    [Tooltip("Drag the specific WorldSpaceUpgradeMenu Canvas for this object here.")]
    public WorldSpaceUpgradeMenu targetMenu;

    // Call this exact method from your existing interaction system's UnityEvent
    public void OpenUpgradeMenu()
    {
        if (targetMenu.IsMenuOpen) return;

        var path = UpgradeManager.Instance.upgradePaths[pathIndex];
        targetMenu.OpenMenu(path);
    }
}