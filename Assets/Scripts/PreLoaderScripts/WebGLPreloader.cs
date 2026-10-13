using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class WebGLPreloader : MonoBehaviour
{
    [Header("UI Elements")]
    public Slider loadingBar;

    [Header("Scene Routing")]
    public string nextSceneName = "MainMenu";

    [Header("Preload Setup")]
    [Tooltip("Assign the Holder GameObject containing the copied scene here.")]
    public GameObject preloadHolder;

    [Header("Automated Data (Do Not Touch)")]
    public List<GameObject> objectsToLoad = new List<GameObject>();

    IEnumerator Start()
    {
        if (loadingBar != null) loadingBar.value = 0f;

        int totalObjects = objectsToLoad.Count;

        for (int i = 0; i < totalObjects; i++)
        {
            if (objectsToLoad[i] != null)
            {
                // Turn on ONLY the current object
                objectsToLoad[i].SetActive(true);

                if (loadingBar != null)
                {
                    loadingBar.value = (float)i / totalObjects;
                }

                // Wait for the GPU to process the meshes/shaders
                yield return null;
                yield return null;

                // Turn the object BACK OFF before the next loop. 
                // This guarantees VRAM never accumulates all props at once.
                objectsToLoad[i].SetActive(false);
            }
        }

        if (loadingBar != null) loadingBar.value = 1f;

        // Wait until the warning and theme are done, and the DVD logo has faded in.
        LoadingScreenAnimator animator = FindObjectOfType<LoadingScreenAnimator>();
        if (animator != null)
        {
            while (!animator.minimumAnimationFinished)
            {
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(1f);
        }

        SceneManager.LoadScene(nextSceneName);
    }
}