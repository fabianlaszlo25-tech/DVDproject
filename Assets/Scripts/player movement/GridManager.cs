using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [Header("Grid Nodes")]
    public List<Transform> gridNodes = new List<Transform>();

    [Header("Obstacles")]
    [Tooltip("Add nodes here that represent walls or blocked spaces. If the player tries to move to one, they will bump.")]
    public List<Transform> obstacleNodes = new List<Transform>();

    [Header("Settings")]
    public float playerHeightOffset = 1.0f;

    public Vector3? GetValidMovePosition(Vector3 currentPos, Vector3 inputDirection)
    {
        Transform closestNode = null;
        float closestDistance = float.MaxValue;
        Vector3 flatCurrent = new Vector3(currentPos.x, 0, currentPos.z);

        // Combine both lists to find the absolute closest spatial point in the chosen direction
        List<Transform> allNodes = new List<Transform>();
        allNodes.AddRange(gridNodes);
        allNodes.AddRange(obstacleNodes);

        foreach (Transform node in allNodes)
        {
            if (node == null) continue;

            Vector3 flatNode = new Vector3(node.position.x, 0, node.position.z);
            Vector3 vectorToNode = flatNode - flatCurrent;
            float distance = vectorToNode.magnitude;

            if (distance < 0.1f) continue; // Ignore the spot we are standing on

            // Check if this node is in the direction we are pushing
            float alignment = Vector3.Dot(vectorToNode.normalized, inputDirection);
            if (alignment > 0.9f && distance < closestDistance)
            {
                closestDistance = distance;
                closestNode = node;
            }
        }

        // If the closest node we found is an obstacle, return null to trigger the bump animation
        if (closestNode != null && obstacleNodes.Contains(closestNode))
        {
            return null;
        }

        // If it's a valid floor node, return its position with the player height added
        if (closestNode != null && gridNodes.Contains(closestNode))
        {
            return new Vector3(closestNode.position.x, closestNode.position.y + playerHeightOffset, closestNode.position.z);
        }

        // If absolutely nothing is in that direction, also bump
        return null;
    }

    public Vector3 GetClosestNodePosition(Vector3 startPos)
    {
        if (gridNodes.Count == 0) return startPos;

        Transform closest = gridNodes[0];
        float minDist = float.MaxValue;

        foreach (Transform node in gridNodes)
        {
            if (node == null) continue;
            float dist = Vector3.Distance(new Vector3(startPos.x, 0, startPos.z), new Vector3(node.position.x, 0, node.position.z));
            if (dist < minDist)
            {
                minDist = dist;
                closest = node;
            }
        }
        return new Vector3(closest.position.x, closest.position.y + playerHeightOffset, closest.position.z);
    }
}