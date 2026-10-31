using UnityEngine;

public class UpgradeTrigger : MonoBehaviour
{
    [Tooltip("0 for Pile, 1 for Player, 2 for Remote (matching the UpgradeManager list)")]
    public int pathIndex;

    [Tooltip("Drag the specific WorldSpaceUpgradeMenu Canvas for this object here.")]
    public WorldSpaceUpgradeMenu targetMenu;

    void Start()
    {
        // System 3: Clicking the physical DVD Player is now the ONLY trigger to open the upgrade menu.
        // Disable standalone DVD pile triggers so only the DVD player opens the unified tabbed canvas.
        if (gameObject.name.ToLowerInvariant().Contains("dvds") && !gameObject.name.ToLowerInvariant().Contains("player"))
        {
            InteractableObject io = GetComponent<InteractableObject>();
            if (io != null) io.enabled = false;
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            enabled = false;
            return;
        }

        InteractableObject playerIo = GetComponent<InteractableObject>();
        if (playerIo != null && (playerIo.hoverText == "Buy Next DVD Player" || string.IsNullOrEmpty(playerIo.hoverText)))
        {
            playerIo.hoverText = "Upgrade Station (DVD / Player / Remote)";
        }
    }

    // Call this exact method from your existing interaction system's UnityEvent
    public void OpenUpgradeMenu()
    {
        if (targetMenu == null || targetMenu.IsMenuOpen) return;

        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.EnsureRemoteUpgradePath();
            int safeIndex = Mathf.Clamp(pathIndex, 0, Mathf.Max(0, UpgradeManager.Instance.upgradePaths.Count - 1));
            var path = UpgradeManager.Instance.upgradePaths[safeIndex];
            targetMenu.OpenMenu(path);
        }
    }

    public void TriggerUpgradeMenu()
    {
        OpenUpgradeMenu();
    }
}
