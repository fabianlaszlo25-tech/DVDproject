using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BarMaterialSetup
{
    private static readonly Dictionary<string, string> MatToTex = new Dictionary<string, string>
    {
        { "TextureMaterial", "Image_0.png" },
        { "TextureMaterial_1", "Image_1.png" },
        { "TextureMaterial_10", "Image_2.png" },
        { "TextureMaterial_11", "Image_3.png" },
        { "TextureMaterial_12", "Image_4.png" },
        { "TextureMaterial_13", "Image_5.png" },
        { "TextureMaterial_14", "Image_6.png" },
        { "TextureMaterial_15", "Image_7.png" },
        { "TextureMaterial_16", "Image_8.png" },
        { "TextureMaterial_17", "Image_9.png" },
        { "TextureMaterial_18", "Image_10.png" },
        { "TextureMaterial_19", "Image_11.png" },
        { "TextureMaterial_20", "Image_13.png" },
        { "TextureMaterial_21", "Image_14.png" },
        { "TextureMaterial_22", "Image_15.png" },
        { "TextureMaterial_23", "Image_16.png" },
        { "TextureMaterial_24", "Image_17.png" },
        { "TextureMaterial_25", "Image_18.png" },
        { "TextureMaterial_26", "Image_19.png" },
        { "TextureMaterial_27", "Image_20.png" },
        { "TextureMaterial_28", "Image_21.png" },
        { "TextureMaterial_29", "Image_22.png" },
        { "TextureMaterial_3", "Image_23.png" },
        { "TextureMaterial_30", "Image_24.png" },
        { "TextureMaterial_31", "Image_25.png" },
        { "TextureMaterial_32", "Image_26.png" },
        { "TextureMaterial_34", "Image_28.png" },
        { "TextureMaterial_36", "Image_30.png" },
        { "TextureMaterial_38", "Image_32.png" },
        { "TextureMaterial_39", "Image_33.png" },
        { "TextureMaterial_4", "Image_34.png" },
        { "TextureMaterial_40", "Image_35.png" },
        { "TextureMaterial_41", "Image_36.png" },
        { "TextureMaterial_43", "Image_38.png" },
        { "TextureMaterial_44", "Image_39.png" },
        { "TextureMaterial_45", "Image_40.png" },
        { "TextureMaterial_47", "Image_42.png" },
        { "TextureMaterial_48", "Image_43.png" },
        { "TextureMaterial_49", "Image_44.png" },
        { "TextureMaterial_50", "Image_46.png" },
        { "TextureMaterial_52", "Image_48.png" },
        { "TextureMaterial_53", "Image_49.png" },
        { "TextureMaterial_54", "Image_50.png" },
        { "TextureMaterial_56", "Image_52.png" },
        { "TextureMaterial_57", "Image_53.png" },
        { "TextureMaterial_58", "Image_54.png" },
        { "TextureMaterial_59", "Image_55.png" },
        { "TextureMaterial_6", "Image_56.png" },
        { "TextureMaterial_60", "Image_57.png" },
        { "TextureMaterial_61", "Image_58.png" },
        { "TextureMaterial_62", "Image_59.png" },
        { "TextureMaterial_64", "Image_61.png" },
        { "TextureMaterial_65", "Image_62.png" },
        { "TextureMaterial_66", "Image_63.png" },
        { "TextureMaterial_67", "Image_67.png" },
        { "TextureMaterial_7", "Image_64.png" },
        { "TextureMaterial_8", "Image_65.png" },
        { "TextureMaterial_9", "Image_66.png" },
    };

    [MenuItem("Tools/Generate and Assign Bar Materials")]
    public static void GenerateAndAssignMaterials()
    {
        string matsFolder = "Assets/Barni/EverythingElse/BarModels/Materials";
        string fbmFolder = "Assets/Barni/EverythingElse/BarModels/bar2.fbm";
        string fbxPath = "Assets/Barni/EverythingElse/BarModels/bar2.fbx";

        if (!AssetDatabase.IsValidFolder(matsFolder))
        {
            AssetDatabase.CreateFolder("Assets/Barni/EverythingElse/BarModels", "Materials");
        }

        Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLitShader == null)
        {
            Debug.LogError("[BarMaterialSetup] Universal Render Pipeline/Lit shader not found!");
            return;
        }

        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[BarMaterialSetup] ModelImporter not found at {fbxPath}");
            return;
        }

        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.External;

        int createdCount = 0;
        int updatedCount = 0;

        foreach (var kvp in MatToTex)
        {
            string matName = kvp.Key;
            string texFile = kvp.Value;
            string matPath = $"{matsFolder}/{matName}.mat";
            string texPath = $"{fbmFolder}/{texFile}";

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                Debug.LogWarning($"[BarMaterialSetup] Texture not found at {texPath}");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(urpLitShader);
                mat.name = matName;
                if (tex != null)
                {
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetTexture("_MainTex", tex);
                }
                mat.SetFloat("_Smoothness", 0.5f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetColor("_BaseColor", Color.white);
                AssetDatabase.CreateAsset(mat, matPath);
                createdCount++;
            }
            else
            {
                mat.shader = urpLitShader;
                if (tex != null)
                {
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetTexture("_MainTex", tex);
                }
                EditorUtility.SetDirty(mat);
                updatedCount++;
            }

            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), matName), mat);
        }

        AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
        AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[BarMaterialSetup] Complete! Created: {createdCount}, Updated: {updatedCount}, Assigned {MatToTex.Count} URP Lit materials to {fbxPath}!");
    }
}
