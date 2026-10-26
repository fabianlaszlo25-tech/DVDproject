using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns trash inside cube areas placed in the scene.
/// Daily count is determined by RunSaveManager based on yesterday's cleaned trash,
/// bedtime dirtiness, and bedtime depression.
/// </summary>
public class TrashManager : MonoBehaviour
{
    [Header("References")]
    public BarManager barManager;
    [Tooltip("Legacy placer reference kept for scene compatibility.")]
    public TrashPlacer placer;

    [Header("Trash Prefabs")]
    [Tooltip("Each prefab needs a TrashItem, InteractableObject and a Collider.")]
    public List<TrashItem> trashPrefabs = new List<TrashItem>();

    [Header("Cube Spawn Areas")]
    [Tooltip("Cubes (Transforms) placed in the scene where trash can spawn (position = center, localScale = area size).")]
    public List<Transform> spawnAreaCubes = new List<Transform>();

    [Tooltip("Optional BoxColliders placed in the scene where trash can spawn (uses collider center & size).")]
    public List<BoxCollider> spawnAreaColliders = new List<BoxCollider>();

    [Header("Floor Alignment")]
    [Tooltip("If true, raycasts down from within the cube volume to place trash flush against the floor.")]
    public bool snapToFloor = true;

    [Tooltip("Layers treated as floor for downward raycasts.")]
    public LayerMask floorMask = ~0;

    [Tooltip("Max downward raycast distance.")]
    public float maxFloorRayDistance = 10f;

    [Tooltip("Padding inside the cube area so trash doesn't spawn right on the border edges.")]
    public float cubeEdgePadding = 0.05f;

    [Tooltip("Vertical lift so trash doesn't intersect floor collider planes.")]
    public float floorLift = 0.01f;

    [Header("Gizmo Visualization")]
    [Tooltip("Draw cube spawn zones in the Unity Scene view.")]
    public bool drawGizmos = true;
    public Color gizmoWireColor = new Color(0.2f, 0.9f, 0.4f, 0.9f);
    public Color gizmoFillColor = new Color(0.2f, 0.9f, 0.4f, 0.15f);

    [Header("Day 1 vs Subsequent Days Spawning")]
    [Tooltip("Spawn trash automatically on Start if not skipping.")]
    public bool spawnOnStart = true;
    [Tooltip("Day 1 base trash count: exactly this amount of trash spawns on the very first day when a run starts.")]
    public int firstDayTrashCount = 25;
    [Tooltip("Subsequent days base trash count: Day 2 and each following day starts with this separate number instead of the Day 1 base, then adds dirtiness/depression and subtracts cleaned trash.")]
    public int subsequentDaysBaseTrash = 5;

    // Legacy fields preserved for backward compatibility
    [HideInInspector] public int defaultTrashCount = 25;
    [HideInInspector] public Transform spawnPoint;
    [HideInInspector] public float dirtiness = 30f;
    [HideInInspector] public float dirtinessPerTrash = 10f;

    private readonly List<TrashItem> activeTrash = new List<TrashItem>();

    public int RemainingTrash => activeTrash.Count;
    public IReadOnlyList<TrashItem> GetActiveTrashItems() => activeTrash;

    private void OnValidate()
    {
        defaultTrashCount = firstDayTrashCount;
    }

    private void Start()
    {
        if (RunSaveManager.HasInstance)
        {
            // Sync with RunSaveManager so setting values in either Inspector works seamlessly
            if (RunSaveManager.Instance.daysSurvived == 0)
            {
                if (firstDayTrashCount != 25 && RunSaveManager.Instance.baseTrashCount == 25)
                {
                    RunSaveManager.Instance.baseTrashCount = firstDayTrashCount;
                }
                else if (RunSaveManager.Instance.baseTrashCount > 0)
                {
                    firstDayTrashCount = RunSaveManager.Instance.baseTrashCount;
                }
            }

            if (subsequentDaysBaseTrash != 5 && RunSaveManager.Instance.subsequentDaysBaseTrash == 5)
            {
                RunSaveManager.Instance.subsequentDaysBaseTrash = subsequentDaysBaseTrash;
            }
            else if (RunSaveManager.Instance.subsequentDaysBaseTrash > 0)
            {
                subsequentDaysBaseTrash = RunSaveManager.Instance.subsequentDaysBaseTrash;
            }
        }

        if (spawnOnStart)
        {
            if (RunSaveManager.HasInstance && RunSaveManager.Instance.ShouldTrashManagerSkipInitialSpawn(this))
            {
                return;
            }
            StartDay();
        }
    }

    /// <summary>Spawns this day's trash quota. Queries RunSaveManager for today's calculated count.</summary>
    public void StartDay()
    {
        int amount;
        if (RunSaveManager.HasInstance)
        {
            amount = RunSaveManager.Instance.GetMorningTrashCountForManager(this);
        }
        else
        {
            amount = firstDayTrashCount;
        }
        SpawnBatch(amount);
    }

    /// <summary>Spawns a specific amount of trash.</summary>
    public void StartDay(int amount)
    {
        SpawnBatch(amount);
    }

    /// <summary>Legacy overload from older systems.</summary>
    public void StartDay(float legacyValue)
    {
        StartDay();
    }

    public void SpawnBatch(int amount)
    {
        if (trashPrefabs == null || trashPrefabs.Count == 0)
        {
            Debug.LogWarning("TrashManager: assign trash prefabs.", this);
            return;
        }

        for (int i = 0; i < amount; i++)
        {
            TrashItem prefab = trashPrefabs[Random.Range(0, trashPrefabs.Count)];
            if (prefab == null) continue;

            if (GetRandomSpawnPosition(out Vector3 spawnPos))
            {
                Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                TrashItem item = Instantiate(prefab, spawnPos, rot);
                item.Cleared += HandleTrashCleared;
                activeTrash.Add(item);
            }
        }
    }

    public bool GetRandomSpawnPosition(out Vector3 result)
    {
        List<Transform> validTransforms = new List<Transform>();
        List<BoxCollider> validColliders = new List<BoxCollider>();

        if (spawnAreaCubes != null)
        {
            for (int i = 0; i < spawnAreaCubes.Count; i++)
            {
                if (spawnAreaCubes[i] != null) validTransforms.Add(spawnAreaCubes[i]);
            }
        }

        if (spawnAreaColliders != null)
        {
            for (int i = 0; i < spawnAreaColliders.Count; i++)
            {
                if (spawnAreaColliders[i] != null) validColliders.Add(spawnAreaColliders[i]);
            }
        }

        // Fallback: if no areas assigned, check own BoxCollider or transform
        if (validTransforms.Count == 0 && validColliders.Count == 0)
        {
            BoxCollider selfBox = GetComponent<BoxCollider>();
            if (selfBox != null)
            {
                validColliders.Add(selfBox);
            }
            else
            {
                validTransforms.Add(transform);
            }
        }

        int totalAreas = validTransforms.Count + validColliders.Count;
        if (totalAreas == 0)
        {
            result = transform.position;
            return false;
        }

        int chosenIndex = Random.Range(0, totalAreas);
        Vector3 randomPoint;

        if (chosenIndex < validTransforms.Count)
        {
            Transform t = validTransforms[chosenIndex];
            Vector3 scale = t.lossyScale;
            float hx = Mathf.Max(0.01f, scale.x * 0.5f - cubeEdgePadding);
            float hy = Mathf.Max(0.01f, scale.y * 0.5f);
            float hz = Mathf.Max(0.01f, scale.z * 0.5f - cubeEdgePadding);

            Vector3 localOffset = new Vector3(
                Random.Range(-hx, hx),
                Random.Range(-hy, hy),
                Random.Range(-hz, hz)
            );
            randomPoint = t.position + t.rotation * localOffset;
        }
        else
        {
            BoxCollider box = validColliders[chosenIndex - validTransforms.Count];
            Vector3 size = box.size;
            float hx = Mathf.Max(0.01f, size.x * 0.5f - cubeEdgePadding);
            float hy = Mathf.Max(0.01f, size.y * 0.5f);
            float hz = Mathf.Max(0.01f, size.z * 0.5f - cubeEdgePadding);

            Vector3 localOffset = box.center + new Vector3(
                Random.Range(-hx, hx),
                Random.Range(-hy, hy),
                Random.Range(-hz, hz)
            );
            randomPoint = box.transform.TransformPoint(localOffset);
        }

        if (snapToFloor)
        {
            Vector3 rayStart = randomPoint + Vector3.up * 1f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxFloorRayDistance, floorMask, QueryTriggerInteraction.Ignore))
            {
                result = hit.point + Vector3.up * floorLift;
                return true;
            }
        }

        result = randomPoint;
        return true;
    }

    public int FindPrefabIndexForInstance(TrashItem item)
    {
        if (item == null || trashPrefabs == null) return 0;
        string rawName = item.gameObject.name;
        if (rawName.EndsWith("(Clone)"))
        {
            rawName = rawName.Substring(0, rawName.Length - 7).Trim();
        }

        for (int i = 0; i < trashPrefabs.Count; i++)
        {
            if (trashPrefabs[i] != null && trashPrefabs[i].name == rawName)
            {
                return i;
            }
        }
        return 0;
    }

    public void RestoreTrashFromSave(
        List<RunSaveManager.SavedSpawnedTrashEntry> savedEntries,
        HashSet<TrashItem> restoredItemsSet)
    {
        for (int i = activeTrash.Count - 1; i >= 0; i--)
        {
            if (activeTrash[i] != null)
            {
                activeTrash[i].Cleared -= HandleTrashCleared;
                Destroy(activeTrash[i].gameObject);
            }
        }
        activeTrash.Clear();

        if (savedEntries == null || trashPrefabs == null || trashPrefabs.Count == 0) return;

        for (int i = 0; i < savedEntries.Count; i++)
        {
            RunSaveManager.SavedSpawnedTrashEntry entry = savedEntries[i];
            if (entry == null) continue;

            int idx = Mathf.Clamp(entry.prefabIndex, 0, trashPrefabs.Count - 1);
            TrashItem prefab = trashPrefabs[idx];
            if (prefab == null) continue;

            TrashItem item = Instantiate(prefab, entry.position, entry.rotation);
            item.Cleared += HandleTrashCleared;
            activeTrash.Add(item);
            if (restoredItemsSet != null)
            {
                restoredItemsSet.Add(item);
            }
        }
    }

    private void HandleTrashCleared(TrashItem item)
    {
        if (item != null) item.Cleared -= HandleTrashCleared;
        activeTrash.Remove(item);

        if (barManager != null) barManager.CleanTrashItem();
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        if (spawnAreaCubes != null)
        {
            for (int i = 0; i < spawnAreaCubes.Count; i++)
            {
                Transform t = spawnAreaCubes[i];
                if (t == null) continue;

                Matrix4x4 oldMat = Gizmos.matrix;
                Gizmos.matrix = t.localToWorldMatrix;

                Gizmos.color = gizmoFillColor;
                Gizmos.DrawCube(Vector3.zero, Vector3.one);

                Gizmos.color = gizmoWireColor;
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

                Gizmos.matrix = oldMat;
            }
        }

        if (spawnAreaColliders != null)
        {
            for (int i = 0; i < spawnAreaColliders.Count; i++)
            {
                BoxCollider box = spawnAreaColliders[i];
                if (box == null) continue;

                Matrix4x4 oldMat = Gizmos.matrix;
                Gizmos.matrix = box.transform.localToWorldMatrix;

                Gizmos.color = gizmoFillColor;
                Gizmos.DrawCube(box.center, box.size);

                Gizmos.color = gizmoWireColor;
                Gizmos.DrawWireCube(box.center, box.size);

                Gizmos.matrix = oldMat;
            }
        }
    }
}
