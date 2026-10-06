using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ClickableWorldUpgrade : MonoBehaviour
{
    [Tooltip("0 for Pile, 1 for Player (matching the UpgradeManager list)")]
    public int pathIndex;

    private void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        var path = UpgradeManager.Instance.upgradePaths[pathIndex];
        WorldSpaceUpgradeMenu.Instance.OpenMenu(path, transform);
    }
}