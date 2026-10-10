using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneLoaderFade : MonoBehaviour
{
    [Header("Fade Settings")]
    [Tooltip("A UI Image covering the whole screen, set to black with 0 alpha")]
    public Image fadeImage;
    public float fadeDuration = 1.0f;

    private void Start()
    {
        // Ensure the screen is clear when the scene first loads
        if (fadeImage != null)
        {
            Color c = fadeImage.color;
            c.a = 0f;
            fadeImage.color = c;
            fadeImage.raycastTarget = false;
        }
    }

    public void LoadSceneWithFade(string sceneName)
    {
        StartCoroutine(FadeAndLoadRoutine(sceneName));
    }

    private IEnumerator FadeAndLoadRoutine(string sceneName)
    {
        if (fadeImage != null)
        {
            // Block UI interactions during the fade
            fadeImage.raycastTarget = true;

            Color startColor = fadeImage.color;
            Color targetColor = new Color(startColor.r, startColor.g, startColor.b, 1f);

            float time = 0;
            while (time < fadeDuration)
            {
                fadeImage.color = Color.Lerp(startColor, targetColor, time / fadeDuration);
                time += Time.deltaTime;
                yield return null;
            }
            fadeImage.color = targetColor;
        }

        SceneManager.LoadScene(sceneName);
    }
}