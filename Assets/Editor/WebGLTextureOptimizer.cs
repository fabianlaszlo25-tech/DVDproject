using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class WebGLTextureOptimizer : EditorWindow
{
    private const string BackupFilePath = "Library/TextureBackupState.json"; //[cite: 4]
    public int maxStandardSize = 256;
    public int maxTinySize = 64;
    public int tinyThreshold = 512; //[cite: 4]

    [System.Serializable]
    private class TextureBackup
    {
        public string assetPath; //[cite: 4]
        public int originalMaxSize; //[cite: 4]
        public TextureImporterFormat originalFormat; //[cite: 4]
        public int originalCompressionQuality; //[cite: 4]
        public bool originalCrunched; //[cite: 4]
    }

    [System.Serializable]
    private class BackupList
    {
        public List<TextureBackup> backups = new List<TextureBackup>(); //[cite: 4]
    }

    [MenuItem("Tools/WebGL Texture Optimizer")]
    public static void ShowWindow()
    {
        GetWindow<WebGLTextureOptimizer>("Texture Optimizer"); //[cite: 4]
    }

    private void OnGUI()
    {
        GUILayout.Label("WebGL Texture Optimizer", EditorStyles.boldLabel); //[cite: 4]
        EditorGUILayout.Space(); //[cite: 4]

        // Added 128 and 256 to standard sizes
        maxStandardSize = EditorGUILayout.IntPopup("Standard Max Size", maxStandardSize,
            new string[] { "128", "256", "512", "1024", "2048" }, new int[] { 128, 256, 512, 1024, 2048 });

        // Added 32 and 64 to tiny sizes
        maxTinySize = EditorGUILayout.IntPopup("Tiny Object Max Size", maxTinySize,
            new string[] { "32", "64", "128", "256", "512" }, new int[] { 32, 64, 128, 256, 512 });

        EditorGUILayout.HelpBox("Textures with an original resolution below the threshold will be capped at the Tiny size to save maximum RAM.", MessageType.Info); //[cite: 4]
        EditorGUILayout.Space(); //[cite: 4]

        if (GUILayout.Button("Optimize For WebGL (Creates Backup)", GUILayout.Height(40))) //[cite: 4]
        {
            OptimizeTextures(); //[cite: 4]
        }

        EditorGUILayout.Space(); //[cite: 4]

        GUI.backgroundColor = Color.red; //[cite: 4]
        if (GUILayout.Button("Revert to Previous State", GUILayout.Height(40))) //[cite: 4]
        {
            RevertTextures(); //[cite: 4]
        }
        GUI.backgroundColor = Color.white; //[cite: 4]
    }

    private void OptimizeTextures()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D"); //[cite: 4]
        BackupList backupList = new BackupList(); //[cite: 4]

        float total = guids.Length; //[cite: 4]
        int processed = 0; //[cite: 4]

        AssetDatabase.StartAssetEditing(); //[cite: 4]

        try
        {
            foreach (string guid in guids) //[cite: 4]
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); //[cite: 4]

                if (path.Contains("Packages/") || path.Contains("Editor/")) continue; //[cite: 4]

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter; //[cite: 4]
                if (importer == null || importer.textureType == TextureImporterType.Sprite) continue; //[cite: 4]

                TextureImporterPlatformSettings webglSettings = importer.GetPlatformTextureSettings("WebGL"); //[cite: 4]

                TextureBackup backup = new TextureBackup
                {
                    assetPath = path, //[cite: 4]
                    originalMaxSize = webglSettings.overridden ? webglSettings.maxTextureSize : importer.maxTextureSize, //[cite: 4]
                    originalFormat = webglSettings.overridden ? webglSettings.format : importer.textureCompression == TextureImporterCompression.Uncompressed ? TextureImporterFormat.RGBA32 : TextureImporterFormat.DXT5, //[cite: 4]
                    originalCompressionQuality = webglSettings.compressionQuality, //[cite: 4]
                    originalCrunched = webglSettings.crunchedCompression //[cite: 4]
                };
                backupList.backups.Add(backup); //[cite: 4]

                Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(path); //[cite: 4]
                int originalWidth = texture != null ? texture.width : 2048; //[cite: 4]

                int targetSize = (originalWidth <= tinyThreshold) ? maxTinySize : maxStandardSize; //[cite: 4]

                webglSettings.overridden = true; //[cite: 4]
                webglSettings.maxTextureSize = targetSize; //[cite: 4]
                webglSettings.format = TextureImporterFormat.DXT5Crunched; //[cite: 4]
                webglSettings.crunchedCompression = true; //[cite: 4]
                webglSettings.compressionQuality = 50; //[cite: 4]

                importer.SetPlatformTextureSettings(webglSettings); //[cite: 4]
                importer.SaveAndReimport(); //[cite: 4]

                processed++; //[cite: 4]
                EditorUtility.DisplayProgressBar("Optimizing Textures", $"Processing {path}", processed / total); //[cite: 4]
            }

            string json = JsonUtility.ToJson(backupList, true); //[cite: 4]
            File.WriteAllText(BackupFilePath, json); //[cite: 4]

            Debug.Log($"Optimized {processed} textures. Backup saved to {BackupFilePath}."); //[cite: 4]
        }
        finally
        {
            EditorUtility.ClearProgressBar(); //[cite: 4]
            AssetDatabase.StopAssetEditing(); //[cite: 4]
        }
    }

    private void RevertTextures()
    {
        if (!File.Exists(BackupFilePath)) //[cite: 4]
        {
            Debug.LogError("No backup file found. Cannot revert."); //[cite: 4]
            return; //[cite: 4]
        }

        string json = File.ReadAllText(BackupFilePath); //[cite: 4]
        BackupList backupList = JsonUtility.FromJson<BackupList>(json); //[cite: 4]

        float total = backupList.backups.Count; //[cite: 4]
        int processed = 0; //[cite: 4]

        AssetDatabase.StartAssetEditing(); //[cite: 4]

        try
        {
            foreach (var backup in backupList.backups) //[cite: 4]
            {
                TextureImporter importer = AssetImporter.GetAtPath(backup.assetPath) as TextureImporter; //[cite: 4]
                if (importer == null) continue; //[cite: 4]

                TextureImporterPlatformSettings webglSettings = importer.GetPlatformTextureSettings("WebGL"); //[cite: 4]

                webglSettings.overridden = true; //[cite: 4]
                webglSettings.maxTextureSize = backup.originalMaxSize; //[cite: 4]
                webglSettings.format = backup.originalFormat; //[cite: 4]
                webglSettings.compressionQuality = backup.originalCompressionQuality; //[cite: 4]
                webglSettings.crunchedCompression = backup.originalCrunched; //[cite: 4]

                importer.SetPlatformTextureSettings(webglSettings); //[cite: 4]
                importer.SaveAndReimport(); //[cite: 4]

                processed++; //[cite: 4]
                EditorUtility.DisplayProgressBar("Reverting Textures", $"Restoring {backup.assetPath}", processed / total); //[cite: 4]
            }

            Debug.Log($"Successfully reverted {processed} textures."); //[cite: 4]
        }
        finally
        {
            EditorUtility.ClearProgressBar(); //[cite: 4]
            AssetDatabase.StopAssetEditing(); //[cite: 4]
        }
    }
}