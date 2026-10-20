using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns one batch of trash at the start of a day, asks the TrashPlacer for a floor
/// spot for each item, and reports cleared trash to the BarManager.
/// </summary>
public class TrashManager : MonoBehaviour
{
    [Header("References")]
    public BarManager barManager;
    public TrashPlacer placer;

    [Header("Trash Prefabs")]
    [Tooltip("Each prefab needs a TrashItem, InteractableObject and a Collider.")]
    public List<TrashItem> trashPrefabs = new List<TrashItem>();

    [Header("Spawn Location")]
    [Tooltip("Rays are shot out from here. Place it in the middle of the room, above the floor.")]
    public Transform spawnPoint;

    [Header("Dirtiness")]
    [Tooltip("Set from outside later (end-of-day depression/dirtiness calculation).")]
    public float dirtiness = 30f;
    [Tooltip("How much dirtiness is worth one piece of trash.")]
    public float dirtinessPerTrash = 10f;
    public bool spawnOnStart = true;

    private readonly List<TrashItem> activeTrash = new List<TrashItem>();

    public int RemainingTrash => activeTrash.Count;

    private void Start()
    {
        if (spawnOnStart) StartDay();
    }

    /// <summary>Spawns this day's trash from the current dirtiness value. Call once per day start.</summary>
    public void StartDay()
    {
        int amount = Mathf.RoundToInt(dirtiness / Mathf.Max(0.01f, dirtinessPerTrash));
        SpawnBatch(amount);
    }

    /// <summary>Hook for the future day system: set the value, then start the day.</summary>
    public void StartDay(float newDirtiness)
    {
        dirtiness = newDirtiness;
        StartDay();
    }

    private void SpawnBatch(int amount)
    {
        if (trashPrefabs.Count == 0 || spawnPoint == null || placer == null)
        {
            Debug.LogWarning("TrashManager: assign trash prefabs, a spawn point and a TrashPlacer.", this);
            return;
        }

        int failed = 0;

        for (int i = 0; i < amount; i++)
        {
            TrashItem prefab = trashPrefabs[Random.Range(0, trashPrefabs.Count)];
            if (prefab == null) continue;

            TrashItem item = Instantiate(prefab, spawnPoint.position, prefab.transform.rotation);

            if (!placer.TryPlace(item, spawnPoint.position, activeTrash))
            {
                Destroy(item.gameObject);
                failed++;
                continue;
            }

            item.Cleared += HandleTrashCleared;
            activeTrash.Add(item);
        }

        if (failed > 0)
            Debug.LogWarning($"TrashManager: {failed} trash item(s) found no free floor spot (room may be too full).", this);
    }

    private void HandleTrashCleared(TrashItem item)
    {
        item.Cleared -= HandleTrashCleared;
        activeTrash.Remove(item);

        if (barManager != null) barManager.CleanTrashItem();
    }

    private void OnDrawGizmosSelected()
    {
        if (spawnPoint == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(spawnPoint.position, 0.25f);
    }
}