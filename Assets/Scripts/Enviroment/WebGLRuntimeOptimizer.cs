using UnityEngine;
using UnityEngine.SceneManagement;

public class WebGLRuntimeOptimizer : MonoBehaviour
{
    private static WebGLRuntimeOptimizer instance;

    [Header("Testing")]
    [Tooltip("If true, applies these extreme WebGL limits in the Editor for testing.")]
    public bool enforceInEditor = false;

    [Header("Performance Limits")]
    public int targetFrameRate = 30;

    [Tooltip("0 = Full Res, 1 = Half Res, 2 = Quarter Res. Applies globally to all textures.")]
    public int globalTextureMipmapLimit = 1;

    void Awake()
    {
        // Singleton pattern to ensure only one optimizer exists across scene loads
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        ApplyGlobalSettings();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void ApplyGlobalSettings()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer && !enforceInEditor) return;

        // Force a lower framerate to reduce thermal throttling and CPU load on browsers
        Application.targetFrameRate = targetFrameRate;

        // VSync is often forced by the browser, but we disable Unity's side of it
        QualitySettings.vSyncCount = 0;

        // Force Unity to load lower-resolution mipmaps globally (saves massive amounts of VRAM)
        QualitySettings.globalTextureMipmapLimit = globalTextureMipmapLimit;

        // Disable global shadow cascades
        QualitySettings.shadowCascades = 0;
        QualitySettings.shadowDistance = 0;
        QualitySettings.shadows = ShadowQuality.Disable;

        // Reduce Level of Detail bias so meshes swap to lower poly versions sooner
        QualitySettings.lodBias = 0.5f;

        Debug.Log("WebGL Runtime Optimizer: Applied global RAM/VRAM restrictions.");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer && !enforceInEditor) return;

        OptimizeLights();

        // Force garbage collection and unload unused assets from the previous scene
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }

    private void OptimizeLights()
    {
        Light[] allLights = FindObjectsOfType<Light>();

        foreach (Light light in allLights)
        {
            // Disable all shadows on all light types
            light.shadows = LightShadows.None;

            if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                // Force point/spot lights to vertex rendering rather than pixel rendering.
                // This makes them significantly cheaper on the GPU.
                light.renderMode = LightRenderMode.ForceVertex;

                // Slightly reduce the range of point lights to cull them faster
                //light.range *= 0.8f;
            }
        }

        Debug.Log($"WebGL Runtime Optimizer: Stripped shadows and forced vertex rendering on {allLights.Length} lights.");
    }
}