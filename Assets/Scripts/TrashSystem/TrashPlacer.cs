using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds a valid spot on the floor for a trash item. From an origin it shoots a ray in a
/// random direction to see how far the room reaches (stopping short of walls by the
/// trash's own size), then drops a ray down to find the real floor and places the item on it.
/// No physics simulation is involved.
/// </summary>
public class TrashPlacer : MonoBehaviour
{
    [Header("Layers")]
    [Tooltip("Layer(s) of the walkable floor. Trash is only ever placed on these.")]
    public LayerMask floorMask;
    [Tooltip("Walls, furniture and anything trash must not clip into. Do NOT include the floor or trash here.")]
    public LayerMask obstacleMask;

    [Header("Blocked Zones")]
    [Tooltip("Cubes marking areas where trash must NOT land (e.g. where the player can walk). " +
             "Rays pass straight through them, only the landing spot is rejected. " +
             "Each cube's transform defines the zone, so turn off its Mesh Renderer but keep the object active.")]
    public List<Transform> blockedZones = new List<Transform>();

    [Header("Ray Spread")]
    [Tooltip("Furthest distance from the spawn point trash may land.")]
    public float maxSpreadDistance = 10f;
    public float minSpreadDistance = 0f;
    [Tooltip("1 = evenly spread along the ray. Higher values push trash toward the walls.")]
    public float edgeBias = 1.5f;
    public int maxAttemptsPerItem = 40;

    [Header("Clearance")]
    [Tooltip("Extra gap kept between trash and walls/furniture.")]
    public float wallPadding = 0.05f;
    [Tooltip("Extra gap kept between two pieces of trash.")]
    public float trashSpacing = 0.05f;

    [Header("Floor Checks")]
    [Tooltip("Height above the floor the downward rays start from.")]
    public float floorRayHeight = 1f;
    [Tooltip("How flat the floor must be (1 = perfectly flat, lower allows slopes).")]
    [Range(0f, 1f)] public float minFloorNormalY = 0.85f;
    [Tooltip("Max height difference allowed under the item's footprint corners.")]
    public float floorTolerance = 0.1f;

    private const float FootLift = 0.02f;

    /// <summary>
    /// Tries to move the item onto a free floor spot. Returns false if none was found.
    /// <paramref name="others"/> are items already placed, so they don't overlap.
    /// </summary>
    public bool TryPlace(TrashItem item, Vector3 origin, IReadOnlyList<TrashItem> others)
    {
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit originFloor, 200f, floorMask, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning("TrashPlacer: no floor found below the spawn point (check the floor layer mask).", this);
            return false;
        }

        float floorY = originFloor.point.y;

        item.SetCollidersEnabled(false);
        item.transform.Rotate(0f, Random.value * 360f, 0f, Space.World);

        Bounds bounds = item.GetWorldBounds();
        Vector3 pivotToCenter = bounds.center - item.transform.position;
        float pivotToBottom = item.transform.position.y - bounds.min.y;
        Vector3 extents = bounds.extents;

        float castRadius = new Vector2(extents.x, extents.z).magnitude + wallPadding;
        Vector3 castOrigin = new Vector3(origin.x, floorY + castRadius + FootLift, origin.z);

        for (int attempt = 0; attempt < maxAttemptsPerItem; attempt++)
        {
            // 1. Shoot a ray out in a random direction to see how far the room goes.
            Vector3 dir = Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward;
            float available = maxSpreadDistance;
            if (Physics.SphereCast(castOrigin, castRadius, dir, out RaycastHit wallHit,
                    maxSpreadDistance, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                available = wallHit.distance;
            }
            if (available <= minSpreadDistance) continue;

            float t = Mathf.Pow(Random.value, 1f / Mathf.Max(0.1f, edgeBias));
            float distance = Mathf.Lerp(minSpreadDistance, available, t);
            Vector3 flatPoint = origin + dir * distance;

            // 2. Find the actual floor under that point.
            if (!Physics.Raycast(new Vector3(flatPoint.x, floorY + floorRayHeight, flatPoint.z), Vector3.down,
                    out RaycastHit floorHit, floorRayHeight * 2f, floorMask, QueryTriggerInteraction.Ignore))
                continue;
            if (floorHit.normal.y < minFloorNormalY) continue;

            // 3. Work out where the item would sit so its bottom touches the floor.
            Vector3 pivot = new Vector3(
                floorHit.point.x - pivotToCenter.x,
                floorHit.point.y + pivotToBottom,
                floorHit.point.z - pivotToCenter.z);
            Bounds candidate = new Bounds(pivot + pivotToCenter, bounds.size);

            if (!IsFootprintOnFloor(candidate, floorHit.point.y)) continue;
            if (IntersectsObstacle(candidate)) continue;
            if (IntersectsBlockedZone(candidate)) continue;
            if (OverlapsOtherTrash(candidate, others)) continue;

            item.transform.position = pivot;
            item.SetCollidersEnabled(true);
            return true;
        }

        item.SetCollidersEnabled(true);
        return false;
    }

    /// <summary>Checks the four footprint corners also have floor at the same height (no hanging over edges).</summary>
    private bool IsFootprintOnFloor(Bounds candidate, float floorHeight)
    {
        Vector3 c = candidate.center;
        Vector3 e = candidate.extents;

        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 start = new Vector3(c.x + sx * e.x, floorHeight + floorRayHeight, c.z + sz * e.z);
                if (!Physics.Raycast(start, Vector3.down, out RaycastHit hit, floorRayHeight * 2f, floorMask, QueryTriggerInteraction.Ignore))
                    return false;
                if (Mathf.Abs(hit.point.y - floorHeight) > floorTolerance) return false;
            }
        }
        return true;
    }

    private bool IntersectsObstacle(Bounds candidate)
    {
        // Lifted slightly so the item resting on the floor doesn't count as touching it.
        Vector3 center = candidate.center + Vector3.up * FootLift;
        Vector3 halfExtents = new Vector3(
            candidate.extents.x + wallPadding,
            Mathf.Max(0.01f, candidate.extents.y - FootLift),
            candidate.extents.z + wallPadding);

        return Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    private bool IntersectsBlockedZone(Bounds candidate)
    {
        if (blockedZones == null) return false;

        candidate.Expand(wallPadding * 2f);

        foreach (Transform zone in blockedZones)
        {
            if (zone == null) continue;
            if (BoxOverlapsZone(candidate, zone)) return true;
        }
        return false;
    }

    /// <summary>
    /// Overlap test between the candidate's axis-aligned box and a zone cube
    /// (the zone transform is treated as a 1x1x1 box, so rotation and scale are respected).
    /// Uses the separating axis theorem.
    /// </summary>
    private static bool BoxOverlapsZone(Bounds box, Transform zone)
    {
        Vector3[] boxAxes = { Vector3.right, Vector3.up, Vector3.forward };
        Vector3[] zoneAxes = { zone.right, zone.up, zone.forward };

        Vector3 boxHalf = box.extents;
        Vector3 lossy = zone.lossyScale;
        Vector3 zoneHalf = new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)) * 0.5f;
        Vector3 delta = zone.position - box.center;

        for (int i = 0; i < 3; i++)
        {
            if (IsSeparatingAxis(boxAxes[i], delta, boxAxes, boxHalf, zoneAxes, zoneHalf)) return false;
            if (IsSeparatingAxis(zoneAxes[i], delta, boxAxes, boxHalf, zoneAxes, zoneHalf)) return false;

            for (int j = 0; j < 3; j++)
            {
                Vector3 cross = Vector3.Cross(boxAxes[i], zoneAxes[j]);
                if (IsSeparatingAxis(cross, delta, boxAxes, boxHalf, zoneAxes, zoneHalf)) return false;
            }
        }
        return true;
    }

    private static bool IsSeparatingAxis(Vector3 axis, Vector3 delta,
        Vector3[] boxAxes, Vector3 boxHalf, Vector3[] zoneAxes, Vector3 zoneHalf)
    {
        if (axis.sqrMagnitude < 1e-6f) return false; // parallel edges, no information
        axis.Normalize();

        float boxRadius = 0f;
        float zoneRadius = 0f;
        for (int k = 0; k < 3; k++)
        {
            boxRadius += boxHalf[k] * Mathf.Abs(Vector3.Dot(boxAxes[k], axis));
            zoneRadius += zoneHalf[k] * Mathf.Abs(Vector3.Dot(zoneAxes[k], axis));
        }

        return Mathf.Abs(Vector3.Dot(delta, axis)) > boxRadius + zoneRadius;
    }

#if UNITY_EDITOR
    // Zone boxes are only drawn while the placer, a zone cube, or any of their parents is selected.
    private void OnDrawGizmos()
    {
        if (blockedZones == null) return;

        bool placerSelected = IsSelectedOrParentSelected(transform);

        foreach (Transform zone in blockedZones)
        {
            if (zone == null) continue;
            if (!placerSelected && !IsSelectedOrParentSelected(zone)) continue;

            Gizmos.matrix = zone.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    private static bool IsSelectedOrParentSelected(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            if (UnityEditor.Selection.Contains(t.gameObject)) return true;
        }
        return false;
    }
#endif

    private bool OverlapsOtherTrash(Bounds candidate, IReadOnlyList<TrashItem> others)
    {
        if (others == null) return false;

        foreach (TrashItem other in others)
        {
            if (other == null) continue;
            Bounds otherBounds = other.GetWorldBounds();
            otherBounds.Expand(trashSpacing * 2f);
            if (otherBounds.Intersects(candidate)) return true;
        }
        return false;
    }
}