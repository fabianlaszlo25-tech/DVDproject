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
    public int firstDayTrashCount = 220;
    [Tooltip("Subsequent days base trash count: Day 2 and each following day starts with this separate number instead of the Day 1 base, then adds dirtiness/depression and subtracts cleaned trash.")]
    public int subsequentDaysBaseTrash = 110;

    // Legacy fields preserved for backward compatibility
    [HideInInspector] public int defaultTrashCount = 220;
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
        // Enforce high-density trash quotas
        if (firstDayTrashCount < 180) firstDayTrashCount = 220;
        if (subsequentDaysBaseTrash < 90) subsequentDaysBaseTrash = 110;

        if (RunSaveManager.HasInstance)
        {
            if (RunSaveManager.Instance.daysSurvived == 0)
            {
                RunSaveManager.Instance.baseTrashCount = firstDayTrashCount;
            }
            RunSaveManager.Instance.subsequentDaysBaseTrash = subsequentDaysBaseTrash;
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

            if (GetRandomSpawnPosition(out Vector3 spawnPos, out Transform chosenTransform, out BoxCollider chosenBox))
            {
                Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                TrashItem item = Instantiate(prefab, spawnPos, rot);
                item.SetSpawnAreaConstraint(chosenTransform, chosenBox);
                item.Cleared += HandleTrashCleared;
                activeTrash.Add(item);
            }
        }
    }

    public bool GetRandomSpawnPosition(out Vector3 result)
    {
        return GetRandomSpawnPosition(out result, out _, out _);
    }

    public bool GetRandomSpawnPosition(out Vector3 result, out Transform chosenTransform, out BoxCollider chosenBox)
    {
        chosenTransform = null;
        chosenBox = null;

        List<Transform> validAreas = new List<Transform>();
        if (spawnAreaCubes != null)
        {
            for (int i = 0; i < spawnAreaCubes.Count; i++)
            {
                if (spawnAreaCubes[i] != null && !validAreas.Contains(spawnAreaCubes[i]))
                {
                    validAreas.Add(spawnAreaCubes[i]);
                }
            }
        }
        if (spawnAreaColliders != null)
        {
            for (int i = 0; i < spawnAreaColliders.Count; i++)
            {
                if (spawnAreaColliders[i] != null && !validAreas.Contains(spawnAreaColliders[i].transform))
                {
                    validAreas.Add(spawnAreaColliders[i].transform);
                }
            }
        }

        if (validAreas.Count == 0)
        {
            validAreas.Add(transform);
        }

        // Weight selection by horizontal footprint so larger floor areas get significantly more trash
        float totalWeight = 0f;
        float[] weights = new float[validAreas.Count];
        for (int i = 0; i < validAreas.Count; i++)
        {
            Transform t = validAreas[i];
            BoxCollider b = t.GetComponent<BoxCollider>();
            Vector3 size = b != null ? b.size : Vector3.one;
            Vector3 lossy = t.lossyScale;
            float worldWidth = Mathf.Abs(size.x * lossy.x);
            float worldDepth = Mathf.Abs(size.z * lossy.z);
            float footprint = worldWidth * worldDepth;
            float w = Mathf.Max(0.2f, Mathf.Sqrt(footprint) * 2f + footprint * 3f);
            weights[i] = w;
            totalWeight += w;
        }

        float randomWeight = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        Transform targetArea = validAreas[0];
        for (int i = 0; i < validAreas.Count; i++)
        {
            cumulative += weights[i];
            if (randomWeight <= cumulative)
            {
                targetArea = validAreas[i];
                break;
            }
        }

        BoxCollider box = targetArea.GetComponent<BoxCollider>();
        chosenTransform = targetArea;
        chosenBox = box;

        Vector3 randomPoint;
        Vector3 scale = targetArea.lossyScale;

        if (box != null)
        {
            Vector3 size = box.size;
            Vector3 center = box.center;
            float worldWidth = Mathf.Abs(size.x * scale.x);
            float worldDepth = Mathf.Abs(size.z * scale.z);

            float padX = cubeEdgePadding / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float padZ = cubeEdgePadding / Mathf.Max(0.001f, Mathf.Abs(scale.z));
            padX = Mathf.Min(padX, size.x * 0.4f);
            padZ = Mathf.Min(padZ, size.z * 0.4f);

            float rx, rz;

            // In large areas, cluster trash into tight piles/hotspots rather than spreading them far apart
            if (worldWidth > 0.5f || worldDepth > 0.5f)
            {
                // Select a cluster center within the area
                int numClusters = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(worldWidth, worldDepth) / 0.45f), 1, 3);
                int clusterIdx = Random.Range(0, numClusters);
                float clusterFraction = numClusters > 1 ? (float)clusterIdx / (numClusters - 1) : 0.5f;

                float clusterCenterX = Mathf.Lerp(-size.x * 0.5f + padX * 1.5f, size.x * 0.5f - padX * 1.5f, clusterFraction);
                float clusterCenterZ = 0f;

                // Tightly cluster around the hotspot (0.12 - 0.22m world radius)
                float clusterRadiusWorld = Random.Range(0.02f, 0.20f);
                float angle = Random.Range(0f, Mathf.PI * 2f);

                rx = clusterCenterX + (Mathf.Cos(angle) * clusterRadiusWorld) / Mathf.Max(0.001f, Mathf.Abs(scale.x));
                rz = clusterCenterZ + (Mathf.Sin(angle) * clusterRadiusWorld) / Mathf.Max(0.001f, Mathf.Abs(scale.z));
            }
            else
            {
                rx = Random.Range(-size.x * 0.5f + padX, size.x * 0.5f - padX);
                rz = Random.Range(-size.z * 0.5f + padZ, size.z * 0.5f - padZ);
            }

            float ry = Random.Range(-size.y * 0.5f, size.y * 0.5f);
            randomPoint = targetArea.TransformPoint(center + new Vector3(rx, ry, rz));
        }
        else
        {
            float padX = cubeEdgePadding / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float padZ = cubeEdgePadding / Mathf.Max(0.001f, Mathf.Abs(scale.z));
            padX = Mathf.Min(padX, 0.4f);
            padZ = Mathf.Min(padZ, 0.4f);

            float rx = Random.Range(-0.5f + padX, 0.5f - padX);
            float ry = Random.Range(-0.5f, 0.5f);
            float rz = Random.Range(-0.5f + padZ, 0.5f - padZ);

            randomPoint = targetArea.TransformPoint(new Vector3(rx, ry, rz));
        }

        if (snapToFloor)
        {
            // Cast down from inside the cube volume so the item lands on the surface
            Vector3 rayStart = randomPoint + Vector3.up * 1f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxFloorRayDistance, floorMask, QueryTriggerInteraction.Ignore))
            {
                // Small vertical stagger so dense trash stacks naturally under physics gravity
                randomPoint = hit.point + Vector3.up * (floorLift + Random.Range(0.01f, 0.25f));
            }
        }

        // Strictly clamp inside the designated cube so trash piles up tightly and never scatters into walking paths
        Vector3 local = targetArea.InverseTransformPoint(randomPoint);
        if (box != null)
        {
            Vector3 size = box.size;
            Vector3 center = box.center;
            float padX = cubeEdgePadding / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float padZ = cubeEdgePadding / Mathf.Max(0.001f, Mathf.Abs(scale.z));
            padX = Mathf.Min(padX, size.x * 0.4f);
            padZ = Mathf.Min(padZ, size.z * 0.4f);

            local.x = Mathf.Clamp(local.x, center.x - size.x * 0.5f + padX, center.x + size.x * 0.5f - padX);
            local.y = Mathf.Clamp(local.y, center.y - size.y * 0.5f, center.y + size.y * 0.5f);
            local.z = Mathf.Clamp(local.z, center.z - size.z * 0.5f + padZ, center.z + size.z * 0.5f - padZ);
        }
        else
        {
            float padX = cubeEdgePadding / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float padZ = cubeEdgePadding / Mathf.Max(0.001f, Mathf.Abs(scale.z));
            padX = Mathf.Min(padX, 0.4f);
            padZ = Mathf.Min(padZ, 0.4f);

            local.x = Mathf.Clamp(local.x, -0.5f + padX, 0.5f - padX);
            local.y = Mathf.Clamp(local.y, -0.5f, 0.5f);
            local.z = Mathf.Clamp(local.z, -0.5f + padZ, 0.5f - padZ);
        }

        result = targetArea.TransformPoint(local);
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
            item.SetSettledImmediate();
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
