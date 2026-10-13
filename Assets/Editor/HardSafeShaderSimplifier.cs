using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class HardSafeShaderSimplifier : EditorWindow
{
    private const string BACKUP_FOLDER = "Assets/matsaver";
    private const string SIMPLE_SHADER = "Universal Render Pipeline/Simple Lit";

    [MenuItem("Tools/Hard-Safe Shader Simplifier")]
    public static void ShowWindow()
    {
        GetWindow<HardSafeShaderSimplifier>("Shader Simplifier");
    }

    private void OnGUI()
    {
        GUILayout.Label("Physical Asset Backup & Conversion", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("This creates literal copies of your .mat files in Assets/matsaver before altering the originals.", MessageType.Info);

        if (GUILayout.Button("1. Backup to 'matsaver' & Simplify"))
        {
            BackupAndSimplify();
        }

        if (GUILayout.Button("2. Restore from 'matsaver' (Overwrite Current)"))
        {
            RestoreFromBackups();
        }
    }

    private void BackupAndSimplify()
    {
        if (!AssetDatabase.IsValidFolder(BACKUP_FOLDER))
        {
            AssetDatabase.CreateFolder("Assets", "matsaver");
        }

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        Shader simpleShader = Shader.Find(SIMPLE_SHADER);

        if (simpleShader == null)
        {
            Debug.LogError("Could not find Simple Lit shader.");
            return;
        }

        HashSet<Material> processedMaterials = new HashSet<Material>();

        foreach (Renderer r in renderers)
        {
            foreach (Material mat in r.sharedMaterials)
            {
                if (mat == null || mat.shader.name == SIMPLE_SHADER || processedMaterials.Contains(mat))
                    continue;

                string originalPath = AssetDatabase.GetAssetPath(mat);

                // Ignore built-in Unity default materials
                if (string.IsNullOrEmpty(originalPath) || originalPath.StartsWith("Packages") || originalPath.StartsWith("Resources"))
                    continue;

                // 1. Create a hard file copy in the matsaver folder
                string backupPath = $"{BACKUP_FOLDER}/{mat.name}_backup.mat";

                // If a backup already exists for some reason, delete it to ensure a fresh copy
                if (AssetDatabase.LoadAssetAtPath<Material>(backupPath) != null)
                {
                    AssetDatabase.DeleteAsset(backupPath);
                }

                AssetDatabase.CopyAsset(originalPath, backupPath);
                processedMaterials.Add(mat);

                // 2. Dynamically scan for the Base Texture and Color
                Texture foundTexture = ScanForTexture(mat);
                Color foundColor = ScanForColor(mat);

                // 3. Swap the shader and apply the found properties
                Undo.RecordObject(mat, "Simplify Material");
                mat.shader = simpleShader;

                if (foundTexture != null) mat.SetTexture("_BaseMap", foundTexture);
                mat.SetColor("_BaseColor", foundColor);

                EditorUtility.SetDirty(mat);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Backed up and simplified {processedMaterials.Count} materials. Check Assets/matsaver for backups.");
    }

    private Texture ScanForTexture(Material mat)
    {
        Texture bestMatch = null;
        int propCount = mat.shader.GetPropertyCount();

        for (int i = 0; i < propCount; i++)
        {
            if (mat.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
            {
                string propName = mat.shader.GetPropertyName(i).ToLower();
                string propDesc = mat.shader.GetPropertyDescription(i).ToLower();
                Texture tex = mat.GetTexture(mat.shader.GetPropertyName(i));

                if (tex != null)
                {
                    // Prioritize properties that sound like a base map
                    if (propName.Contains("base") || propDesc.Contains("base") ||
                        propName.Contains("albedo") || propDesc.Contains("albedo") ||
                        propName.Contains("main") || propDesc.Contains("main"))
                    {
                        return tex;
                    }

                    // Fallback: hold onto the first valid texture we find just in case
                    if (bestMatch == null)
                    {
                        bestMatch = tex;
                    }
                }
            }
        }
        return bestMatch;
    }

    private Color ScanForColor(Material mat)
    {
        Color bestMatch = Color.white;
        int propCount = mat.shader.GetPropertyCount();

        for (int i = 0; i < propCount; i++)
        {
            if (mat.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
            {
                string propName = mat.shader.GetPropertyName(i).ToLower();
                string propDesc = mat.shader.GetPropertyDescription(i).ToLower();

                // Shader Graph often hides variables in the background, we only want exposed ones
                Color col = mat.GetColor(mat.shader.GetPropertyName(i));

                if (propName.Contains("base") || propDesc.Contains("base") ||
                    propName.Contains("color") || propDesc.Contains("color") ||
                    propName.Contains("tint"))
                {
                    // Avoid selecting pure black or completely transparent colors if possible
                    if (col.a > 0.1f) return col;
                }
            }
        }
        return bestMatch;
    }

    private void RestoreFromBackups()
    {
        if (!AssetDatabase.IsValidFolder(BACKUP_FOLDER))
        {
            Debug.LogWarning("No 'matsaver' folder found.");
            return;
        }

        string[] backupGUIDs = AssetDatabase.FindAssets("t:Material", new[] { BACKUP_FOLDER });
        int restoredCount = 0;

        foreach (string guid in backupGUIDs)
        {
            string backupPath = AssetDatabase.GUIDToAssetPath(guid);
            Material backupMat = AssetDatabase.LoadAssetAtPath<Material>(backupPath);

            if (backupMat != null)
            {
                // Reconstruct the original path by stripping out "Assets/matsaver/" and "_backup.mat"
                string originalName = backupMat.name.Replace("_backup", "");

                // Find where the broken material currently lives in the project
                string[] originalGUIDs = AssetDatabase.FindAssets($"{originalName} t:Material");

                foreach (string origGuid in originalGUIDs)
                {
                    string origPath = AssetDatabase.GUIDToAssetPath(origGuid);

                    // Ensure we are overwriting the actual material, not another backup
                    if (!origPath.StartsWith(BACKUP_FOLDER) && Path.GetFileNameWithoutExtension(origPath) == originalName)
                    {
                        // Overwrite the broken asset with the perfect backup from disk
                        File.Copy(Path.GetFullPath(backupPath), Path.GetFullPath(origPath), true);
                        restoredCount++;
                    }
                }
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Restored {restoredCount} materials from the matsaver backups.");
    }
}