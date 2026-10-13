using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public class WebGLRuntimeOptimizer : MonoBehaviour
{
    private static WebGLRuntimeOptimizer instance;

    [Header("Testing")]
    [Tooltip("If true, applies these extreme WebGL limits in the Editor for testing.")]
    public bool enforceInEditor = false;

    [Header("Performance Limits")]
    public int targetFrameRate = 30;

    [Tooltip("0=Full, 1=Half, 2=Quarter, 3=Eighth. Try 2 if the textures still look too good.")]
    public int globalTextureMipmapLimit = 2;

    [Tooltip("Drops the actual screen resolution pixel count. 0.5 = 50% resolution (massive GPU savings).")]
    [Range(0.1f, 1f)]
    public float screenResolutionScale = 0.5f;

    private bool resolutionScaled = false;

    void Awake()
    {
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

        Application.targetFrameRate = targetFrameRate;
        QualitySettings.vSyncCount = 0;

        QualitySettings.globalTextureMipmapLimit = globalTextureMipmapLimit;

        QualitySettings.shadowCascades = 0;
        QualitySettings.shadowDistance = 0;
        QualitySettings.lodBias = 0.5f;

        if (!resolutionScaled)
        {
            int newWidth = Mathf.RoundToInt(Screen.width * screenResolutionScale);
            int newHeight = Mathf.RoundToInt(Screen.height * screenResolutionScale);
            Screen.SetResolution(newWidth, newHeight, Screen.fullScreen);
            resolutionScaled = true;
        }

        Debug.Log("WebGL Runtime Optimizer: Applied global RAM/VRAM restrictions and dropped resolution.");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer && !enforceInEditor) return;

        OptimizeLights();
        OptimizeCameras();

        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }

    private void OptimizeCameras()
    {
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        foreach (Camera cam in cameras)
        {
            cam.allowMSAA = false;
            cam.allowHDR = false;

            // Target URP specifically to override the pipeline asset's post-processing
            UniversalAdditionalCameraData urpCamData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (urpCamData != null)
            {
                urpCamData.renderPostProcessing = false;
                urpCamData.antialiasing = AntialiasingMode.None;
            }
        }

        Debug.Log($"WebGL Runtime Optimizer: Stripped URP Post-Processing on {cameras.Length} cameras.");
    }

    private void OptimizeLights()
    {
        Light[] allLights = FindObjectsOfType<Light>(true);

        foreach (Light light in allLights)
        {
            light.shadows = LightShadows.None;

            if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                light.renderMode = LightRenderMode.ForceVertex;
                light.range *= 0.8f;
            }
        }
    }
}