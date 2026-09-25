using UnityEngine;

public class LightFlicker : MonoBehaviour
{
    public Light lightSource;

    public float flickerRate = 10f;
    [Range(0f, 1f)]
    public float intensity = 0.3f;

    private float originalIntensity;
    private float timer;

    void Start()
    {
        if (lightSource == null)
            lightSource = GetComponent<Light>();

        originalIntensity = lightSource.intensity;
    }

    void Update()
    {
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            lightSource.intensity = Random.Range(
                originalIntensity * (1f - intensity),
                originalIntensity
            );

            timer = 1f / flickerRate;
        }
    }
}