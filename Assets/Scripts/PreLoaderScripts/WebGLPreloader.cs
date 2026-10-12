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

    [Header("Automated Data (Do Not Touch)")]
    public List<Material> allGameMaterials = new List<Material>();

    private GameObject dummyQuad;

    IEnumerator Start()
    {
        if (loadingBar != null) loadingBar.value = 0f;

        dummyQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        dummyQuad.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 2f;
        MeshRenderer quadRenderer = dummyQuad.GetComponent<MeshRenderer>();

        int totalMaterials = allGameMaterials.Count;

        for (int i = 0; i < totalMaterials; i++)
        {
            if (allGameMaterials[i] != null)
            {
                quadRenderer.material = allGameMaterials[i];

                if (loadingBar != null)
                {
                    loadingBar.value = (float)i / totalMaterials;
                }

                yield return null;
                yield return null;
            }
        }

        Destroy(dummyQuad);

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