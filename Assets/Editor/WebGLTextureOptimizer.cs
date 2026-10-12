using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class WebGLTextureOptimizer : EditorWindow
{
    private const string BackupFilePath = "Library/TextureBackupState.json";
    public int maxStandardSize = 1024;
    public int maxTinySize = 256;
    public int tinyThreshold = 512; // Textures originally smaller than this are considered "tiny"

    [System.Serializable]
    private class TextureBackup
    {
        public string assetPath;
        public int originalMaxSize;
        public TextureImporterFormat originalFormat;
        public int originalCompressionQuality;
        public bool originalCrunched;
    }

    [System.Serializable]
    private class BackupList
    {
        public List<TextureBackup> backups = new List<TextureBackup>();
    }

    [MenuItem("Tools/WebGL Texture Optimizer")]
    public static void ShowWindow()
    {
        GetWindow<WebGLTextureOptimizer>("Texture Optimizer");
    }

    private void OnGUI()
    {
        GUILayout.Label("WebGL Texture Optimizer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        maxStandardSize = EditorGUILayout.IntPopup("Standard Max Size", maxStandardSize,
            new string[] { "512", "1024", "2048" }, new int[] { 512, 1024, 2048 });

        maxTinySize = EditorGUILayout.IntPopup("Tiny Object Max Size", maxTinySize,
            new string[] { "128", "256", "512" }, new int[] { 128, 256, 512 });

        EditorGUILayout.HelpBox("Textures with an original resolution below the threshold will be capped at the Tiny size to save maximum RAM.", MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Optimize For WebGL (Creates Backup)", GUILayout.Height(40)))
        {
            OptimizeTextures();
        }

        EditorGUILayout.Space();

        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Revert to Previous State", GUILayout.Height(40)))
        {
            RevertTextures();
        }
        GUI.backgroundColor = Color.white;
    }

    private void OptimizeTextures()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");
        BackupList backupList = new BackupList();

        float total = guids.Length;
        int processed = 0;

        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Skip UI, Editor, and Package textures to prevent breaking the engine UI
                if (path.Contains("Packages/") || path.Contains("Editor/")) continue;

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType == TextureImporterType.Sprite) continue;

                TextureImporterPlatformSettings webglSettings = importer.GetPlatformTextureSettings("WebGL");

                // Save Backup
                TextureBackup backup = new TextureBackup
                {
                    assetPath = path,
                    originalMaxSize = webglSettings.overridden ? webglSettings.maxTextureSize : importer.maxTextureSize,
                    originalFormat = webglSettings.overridden ? webglSettings.format : importer.textureCompression == TextureImporterCompression.Uncompressed ? TextureImporterFormat.RGBA32 : TextureImporterFormat.DXT5,
                    originalCompressionQuality = webglSettings.compressionQuality,
                    originalCrunched = webglSettings.crunchedCompression
                };
                backupList.backups.Add(backup);

                // Determine new size based on original source texture dimensions
                Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                int originalWidth = texture != null ? texture.width : 2048;

                int targetSize = (originalWidth <= tinyThreshold) ? maxTinySize : maxStandardSize;

                // Apply new settings
                webglSettings.overridden = true;
                webglSettings.maxTextureSize = targetSize;
                webglSettings.format = TextureImporterFormat.DXT5Crunched; // Safe, highly compressed format for WebGL
                webglSettings.crunchedCompression = true;
                webglSettings.compressionQuality = 50;

                importer.SetPlatformTextureSettings(webglSettings);
                importer.SaveAndReimport();

                processed++;
                EditorUtility.DisplayProgressBar("Optimizing Textures", $"Processing {path}", processed / total);
            }

            // Write backup to disk
            string json = JsonUtility.ToJson(backupList, true);
            File.WriteAllText(BackupFilePath, json);

            Debug.Log($"Optimized {processed} textures. Backup saved to {BackupFilePath}.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
        }
    }

    private void RevertTextures()
    {
        if (!File.Exists(BackupFilePath))
        {
            Debug.LogError("No backup file found. Cannot revert.");
            return;
        }

        string json = File.ReadAllText(BackupFilePath);
        BackupList backupList = JsonUtility.FromJson<BackupList>(json);

        float total = backupList.backups.Count;
        int processed = 0;

        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (var backup in backupList.backups)
            {
                TextureImporter importer = AssetImporter.GetAtPath(backup.assetPath) as TextureImporter;
                if (importer == null) continue;

                TextureImporterPlatformSettings webglSettings = importer.GetPlatformTextureSettings("WebGL");

                webglSettings.overridden = true; // Keep overridden but restore original values
                webglSettings.maxTextureSize = backup.originalMaxSize;
                webglSettings.format = backup.originalFormat;
                webglSettings.compressionQuality = backup.originalCompressionQuality;
                webglSettings.crunchedCompression = backup.originalCrunched;

                importer.SetPlatformTextureSettings(webglSettings);
                importer.SaveAndReimport();

                processed++;
                EditorUtility.DisplayProgressBar("Reverting Textures", $"Restoring {backup.assetPath}", processed / total);
            }

            Debug.Log($"Successfully reverted {processed} textures.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
        }
    }
}