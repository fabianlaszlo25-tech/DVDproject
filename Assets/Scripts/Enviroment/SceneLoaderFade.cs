using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneLoaderFade : MonoBehaviour
{
    [Header("Fade Settings")]
    [Tooltip("A UI Image covering the whole screen, set to black with 0 alpha")]
    public Image fadeImage; //
    public float fadeDuration = 1.0f; //[cite: 5]

    private void Start()
    {
        // Ensure the screen is clear when the scene first loads
        if (fadeImage != null)
        {
            Color c = fadeImage.color;
            c.a = 0f;
            fadeImage.color = c;
            fadeImage.raycastTarget = false;
        } //[cite: 5]
    }

    public void LoadSceneWithFade(string sceneName)
    {
        StartCoroutine(FadeAndLoadRoutine(sceneName)); //[cite: 5]
    }

    private IEnumerator FadeAndLoadRoutine(string sceneName)
    {
        if (fadeImage != null)
        {
            // Block UI interactions during the fade
            fadeImage.raycastTarget = true; //[cite: 5]

            Color startColor = fadeImage.color; //[cite: 5]
            Color targetColor = new Color(startColor.r, startColor.g, startColor.b, 1f); //[cite: 5]

            float time = 0;
            while (time < fadeDuration)
            {
                fadeImage.color = Color.Lerp(startColor, targetColor, time / fadeDuration);
                time += Time.deltaTime;
                yield return null;
            } //[cite: 5]

            fadeImage.color = targetColor; //[cite: 5]
        }

        // --- SAFER LOADING IMPLEMENTATION ---

        // 1. Force a garbage collection cycle while the screen is black. 
        // This clears out temporary RAM from the current scene before the massive load starts.
        yield return Resources.UnloadUnusedAssets();
        System.GC.Collect();

        // 2. Load the scene asynchronously in the background. 
        // This prevents the browser tab from locking up and crashing the WebGL player.
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        // 3. Keep the coroutine running (and the screen black) until Unity finishes loading the new scene.
        while (!asyncLoad.isDone)
        {
            yield return null;
        }
    }
}