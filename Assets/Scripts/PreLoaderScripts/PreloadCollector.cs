#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class PreloadCollector : EditorWindow
{
    [MenuItem("Tools/Auto-Collect Preload Objects")]
    public static void CollectObjects()
    {
        WebGLPreloader preloader = FindObjectOfType<WebGLPreloader>();
        if (preloader == null)
        {
            Debug.LogError("Could not find the WebGLPreloader script. Open your Loading Scene first!");
            return;
        }

        if (preloader.preloadHolder == null)
        {
            Debug.LogError("Assign a Preload Holder GameObject in the WebGLPreloader script first.");
            return;
        }

        List<GameObject> queuedObjects = new List<GameObject>();
        Transform holder = preloader.preloadHolder.transform;

        holder.gameObject.SetActive(true);

        // 1. Process all cameras to keep them active, untagged, and silent
        Camera[] cameras = holder.GetComponentsInChildren<Camera>(true);
        foreach (Camera cam in cameras)
        {
            if (cam.gameObject.CompareTag("MainCamera"))
            {
                cam.gameObject.tag = "Untagged";
            }
            cam.enabled = true;
            cam.gameObject.SetActive(true);

            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
        }

        // 2. Flatten the hierarchy and process all other transforms
        // We grab the array first so modifying parents doesn't break the iteration
        Transform[] allTransforms = holder.GetComponentsInChildren<Transform>(true);

        foreach (Transform t in allTransforms)
        {
            if (t == holder) continue;

            // Destroy the nested hierarchy of the copied scene.
            // By making every object a direct sibling under the holder, we can turn 
            // them on/off individually without breaking parent-child visibility chains.
            t.SetParent(holder, true);

            if (t.GetComponent<Camera>() == null)
            {
                t.gameObject.SetActive(false);
                queuedObjects.Add(t.gameObject);
            }
        }

        preloader.objectsToLoad = queuedObjects;
        EditorUtility.SetDirty(preloader);

        Debug.Log($"Flattened hierarchy, queued {queuedObjects.Count} objects, and processed {cameras.Length} cameras for strict 1-by-1 preloading!");
    }
}
#endif