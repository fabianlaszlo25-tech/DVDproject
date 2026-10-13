using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class WebGLPreloader : MonoBehaviour
{
    [Header("UI Elements")]
    public Slider loadingBar;

    [Header("Scene Routing")]
    public string nextSceneName = "MainMenu";

    IEnumerator Start()
    {
        if (loadingBar != null) loadingBar.value = 0f;

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

        // Stream the big scene natively in the background without a preload holder
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(nextSceneName);
        asyncLoad.allowSceneActivation = false;

        while (!asyncLoad.isDone)
        {
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);

            if (loadingBar != null)
            {
                loadingBar.value = progress;
            }

            if (asyncLoad.progress >= 0.9f)
            {
                asyncLoad.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}