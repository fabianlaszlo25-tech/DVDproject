using UnityEngine;
using UnityEngine.Profiling;

public class WebGLMemoryProfiler : MonoBehaviour
{
    [Header("Settings")]
    public bool showInEditor = true;
    public int fontSize = 24;
    public Color textColor = Color.green;

    private static WebGLMemoryProfiler instance;

    void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnGUI()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer && !showInEditor) return;

        // Convert bytes to Megabytes
        long ramUsed = Profiler.GetTotalAllocatedMemoryLong() / 1048576;
        long ramReserved = Profiler.GetTotalReservedMemoryLong() / 1048576;
        long vramUsed = Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576;

        int maxRam = SystemInfo.systemMemorySize;
        int maxVram = SystemInfo.graphicsMemorySize;

        GUIStyle style = new GUIStyle();
        style.fontSize = fontSize;
        style.normal.textColor = textColor;
        style.fontStyle = FontStyle.Bold;

        string info = $"RAM Used: {ramUsed} MB (Reserved: {ramReserved} MB)\n" +
                      $"VRAM Used: {vramUsed} MB\n" +
                      $"Combined Footprint: {ramUsed + vramUsed} MB\n" +
                      $"Hardware Max: {maxRam} MB RAM | {maxVram} MB VRAM";

        // Draw a subtle background box for readability
        GUI.Box(new Rect(10, 10, 450, 120), "");
        GUI.Label(new Rect(20, 20, 450, 120), info, style);
    }
}