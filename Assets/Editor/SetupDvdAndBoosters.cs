using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupDvdAndBoosters
{
    private const string ScenePath = "Assets/Scenes/01Day.unity";
    private const string SmokePrefabPath = "Assets/Scripts/BoosterItems/msVFX_Stylized Smoke 4.prefab";
    private const string BuildupSoundPath = "Assets/Audio/buildup.mp3";
    private const string BuySoundPath = "Assets/Audio/buy.mp3";
    private const string CantAffordSoundPath = "Assets/Audio/poof.mp3";
    private const string GrayedOutMaterialPath = "Assets/materials/M_UnboughtGrayedOut.mat";

    private struct TierColorInfo
    {
        public string tierName;
        public Color baseColor;
        public Color emissionColor;
        public string baseMatName;
        public string logoMatName;
    }

    private static readonly TierColorInfo[] Colors = new TierColorInfo[]
    {
        new TierColorInfo { tierName = "Silver Standard", baseColor = new Color(0.748f, 0.748f, 0.748f, 1f), emissionColor = new Color(0.08f, 0.0f, 1.0f, 1f), baseMatName = "Base 2", logoMatName = "EMMISSIVES 1" },
        new TierColorInfo { tierName = "Crimson Red", baseColor = new Color(0.877f, 0.608f, 0.608f, 1f), emissionColor = new Color(1.0f, 0.09f, 0.27f, 1f), baseMatName = "Base 3", logoMatName = "EMMISSIVES_1_Crimson" },
        new TierColorInfo { tierName = "Cobalt Blue", baseColor = new Color(0.23f, 0.38f, 0.71f, 1f), emissionColor = new Color(0.08f, 0.53f, 1.0f, 1f), baseMatName = "Base_2_Cobalt", logoMatName = "EMMISSIVES_2_Cobalt" },
        new TierColorInfo { tierName = "Emerald Green", baseColor = new Color(0.18f, 0.49f, 0.20f, 1f), emissionColor = new Color(0.0f, 1.2f, 0.4f, 1f), baseMatName = "Base_3_Emerald", logoMatName = "EMMISSIVES_3_Emerald" },
        new TierColorInfo { tierName = "Amber Gold", baseColor = new Color(0.90f, 0.66f, 0.14f, 1f), emissionColor = new Color(1.2f, 1.0f, 0.0f, 1f), baseMatName = "Base_4_Amber", logoMatName = "EMMISSIVES_4_Amber" },
        new TierColorInfo { tierName = "Amethyst Purple", baseColor = new Color(0.48f, 0.12f, 0.64f, 1f), emissionColor = new Color(1.0f, 0.0f, 1.2f, 1f), baseMatName = "Base_5_Amethyst", logoMatName = "EMMISSIVES_5_Amethyst" },
        new TierColorInfo { tierName = "Vibrant Cyan", baseColor = new Color(0.0f, 0.67f, 0.76f, 1f), emissionColor = new Color(0.0f, 1.1f, 1.2f, 1f), baseMatName = "Base_6_Cyan", logoMatName = "EMMISSIVES_6_Cyan" },
        new TierColorInfo { tierName = "Tangerine Orange", baseColor = new Color(0.90f, 0.32f, 0.0f, 1f), emissionColor = new Color(1.2f, 0.45f, 0.0f, 1f), baseMatName = "Base_7_Orange", logoMatName = "EMMISSIVES_7_Orange" },
        new TierColorInfo { tierName = "Magenta Rose", baseColor = new Color(0.76f, 0.09f, 0.36f, 1f), emissionColor = new Color(1.2f, 0.1f, 0.6f, 1f), baseMatName = "Base_8_Magenta", logoMatName = "EMMISSIVES_8_Magenta" },
        new TierColorInfo { tierName = "Obsidian Glow", baseColor = new Color(0.12f, 0.12f, 0.14f, 1f), emissionColor = new Color(1.5f, 1.5f, 1.5f, 1f), baseMatName = "Base_9_Obsidian", logoMatName = "EMMISSIVES_9_Obsidian" }
    };

    [MenuItem("Tools/Run Complete Setup")]
    public static void Run()
    {
        Debug.Log(">>> Starting Complete DVD, Player, and Permanent Booster Setup...");

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath);
        }

        // 1. Setup Materials
        Material[] baseMaterials = SetupBaseMaterials();
        Material[] logoMaterials = SetupLogoMaterials();

        // 2. Setup Prefabs
        GameObject[] playerPrefabs = SetupPlayerPrefabs(baseMaterials);
        GameObject[] dvdPrefabs = SetupDvdPrefabs(baseMaterials);

        // 3. Setup World GameObjects & Boosters in Scene
        SetupWorldPlayersAndDvdBoosters(playerPrefabs, dvdPrefabs, logoMaterials);

        // 4. Setup Permanent Boosters on Shelves
        SetupPermanentBoosters();

        // 5. Setup Player Movement Speed
        SetupPlayerMovementSpeed();

        // 6. Mark Scene Dirty and Save
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(">>> Complete DVD, Player, and Permanent Booster Setup FINISHED SUCCESSFULLY!");
    }

    private static Material[] SetupBaseMaterials()
    {
        Material[] mats = new Material[10];
        Material templateBase = AssetDatabase.LoadAssetAtPath<Material>("Assets/3DAssets/DVDPlayer/Base 3.mat");

        for (int i = 0; i < 10; i++)
        {
            string path = $"Assets/3DAssets/DVDPlayer/{Colors[i].baseMatName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(templateBase);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", Colors[i].baseColor);
            mat.SetColor("_Color", Colors[i].baseColor);
            EditorUtility.SetDirty(mat);
            mats[i] = mat;
        }

        return mats;
    }

    private static Material[] SetupLogoMaterials()
    {
        Material[] mats = new Material[10];
        Material templateLogo = AssetDatabase.LoadAssetAtPath<Material>("Assets/materials/EMMISSIVES 1.mat");

        for (int i = 0; i < 10; i++)
        {
            string path = $"Assets/materials/{Colors[i].logoMatName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(templateLogo);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Colors[i].emissionColor);
            EditorUtility.SetDirty(mat);
            mats[i] = mat;
        }

        return mats;
    }

    private static GameObject[] SetupPlayerPrefabs(Material[] baseMaterials)
    {
        GameObject[] prefabs = new GameObject[10];
        prefabs[0] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/playerbasic.prefab");
        prefabs[1] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/playerbasic 1.prefab");

        for (int i = 2; i < 10; i++)
        {
            string targetPath = $"Assets/Prefabs/playerbasic {i}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (existing == null)
            {
                AssetDatabase.CopyAsset("Assets/Prefabs/playerbasic 1.prefab", targetPath);
                AssetDatabase.SaveAssets();
                existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            }

            // Edit prefab contents to use baseMaterials[i]
            GameObject root = PrefabUtility.LoadPrefabContents(targetPath);
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in renderers)
            {
                Material[] shared = mr.sharedMaterials;
                bool changed = false;
                for (int m = 0; m < shared.Length; m++)
                {
                    if (shared[m] != null && shared[m].name.StartsWith("Base"))
                    {
                        shared[m] = baseMaterials[i];
                        changed = true;
                    }
                }
                if (changed) mr.sharedMaterials = shared;
            }
            PrefabUtility.SaveAsPrefabAsset(root, targetPath);
            PrefabUtility.UnloadPrefabContents(root);

            prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
        }

        return prefabs;
    }

    private static GameObject[] SetupDvdPrefabs(Material[] baseMaterials)
    {
        GameObject[] prefabs = new GameObject[10];
        prefabs[0] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/dvdv.prefab");
        prefabs[1] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/dvdv 1.prefab");

        for (int i = 2; i < 10; i++)
        {
            string targetPath = $"Assets/Prefabs/dvdv {i}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (existing == null)
            {
                AssetDatabase.CopyAsset("Assets/Prefabs/dvdv 1.prefab", targetPath);
                AssetDatabase.SaveAssets();
                existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            }

            // Edit prefab contents to use baseMaterials[i]
            GameObject root = PrefabUtility.LoadPrefabContents(targetPath);
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in renderers)
            {
                Material[] shared = mr.sharedMaterials;
                bool changed = false;
                for (int m = 0; m < shared.Length; m++)
                {
                    if (shared[m] != null && shared[m].name.StartsWith("Base"))
                    {
                        shared[m] = baseMaterials[i];
                        changed = true;
                    }
                }
                if (changed) mr.sharedMaterials = shared;
            }
            PrefabUtility.SaveAsPrefabAsset(root, targetPath);
            PrefabUtility.UnloadPrefabContents(root);

            prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
        }

        return prefabs;
    }

    private static GameObject FindSceneObject(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                if (t.name == name) return t.gameObject;
            }
        }
        return null;
    }

    private static void SetupWorldPlayersAndDvdBoosters(GameObject[] playerPrefabs, GameObject[] dvdPrefabs, Material[] logoMaterials)
    {
        UpgradeManager manager = Object.FindObjectOfType<UpgradeManager>();
        if (manager == null)
        {
            Debug.LogError("SetupDvdAndBoosters: UpgradeManager not found in scene!");
            return;
        }

        Transform dvdSetup = manager.transform;
        Transform dvdLogoObj = dvdSetup.Find("Camera/DVDLogo/dvd");
        Renderer dvdLogoRenderer = dvdLogoObj != null ? dvdLogoObj.GetComponent<Renderer>() : null;

        // --- Setup Player World GameObjects ---
        GameObject[] worldPlayers = new GameObject[10];
        Vector3 playerPos = new Vector3(1.08f, -0.34733f, -2.822f);
        Quaternion playerRot = Quaternion.Euler(0f, -90f, 0f);

        for (int i = 0; i < 10; i++)
        {
            string pName = i == 0 ? "playerbasic" : $"playerbasic {i}";
            GameObject go = FindSceneObject(pName);
            if (go == null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefabs[i]);
                go.name = pName;
                go.transform.position = playerPos;
                go.transform.rotation = playerRot;
            }
            if (i > 0) go.SetActive(false);
            else go.SetActive(true);
            worldPlayers[i] = go;
        }

        // Attach negative boosters to Players after the 3rd one (index 3 to 9)
        BoosterObject[] playerBoosters = new BoosterObject[10];
        playerBoosters[3] = AddOrUpdateModifier(worldPlayers[3], BoosterStat.HungerFillRate, ModifierMode.PercentageChange, 45f);
        playerBoosters[4] = AddOrUpdateModifier(worldPlayers[4], BoosterStat.SleepFillRate, ModifierMode.PercentageChange, 55f);
        playerBoosters[5] = AddOrUpdateModifier(worldPlayers[5], BoosterStat.BaseDepressionFillRate, ModifierMode.PercentageChange, 60f);
        playerBoosters[6] = AddOrUpdateModifier(worldPlayers[6], BoosterStat.ToiletDirtinessIncrease, ModifierMode.PercentageChange, 70f);
        playerBoosters[7] = AddOrUpdateModifier(worldPlayers[7], BoosterStat.EatDirtinessIncrease, ModifierMode.PercentageChange, 65f);
        playerBoosters[8] = AddOrUpdateModifier(worldPlayers[8], BoosterStat.InflationRatePerDollar, ModifierMode.PercentageChange, 80f);
        playerBoosters[9] = AddOrUpdateModifier(worldPlayers[9], BoosterStat.PenaltyMultiplier, ModifierMode.PercentageChange, 100f);

        // --- Setup DVD Boosters under DVDSetup ---
        Transform dvdBoostersParent = dvdSetup.Find("DVDBoosters");
        if (dvdBoostersParent == null)
        {
            GameObject parentGo = new GameObject("DVDBoosters");
            parentGo.transform.SetParent(dvdSetup, false);
            dvdBoostersParent = parentGo.transform;
        }

        BoosterObject[] dvdBoosters = new BoosterObject[10];
        dvdBoosters[3] = CreateChildBooster(dvdBoostersParent, "DVDBooster_3", BoosterStat.SleepFillRate, ModifierMode.PercentageChange, 45f);
        dvdBoosters[4] = CreateChildBooster(dvdBoostersParent, "DVDBooster_4", BoosterStat.HungerFillRate, ModifierMode.PercentageChange, 55f);
        dvdBoosters[5] = CreateChildBooster(dvdBoostersParent, "DVDBooster_5", BoosterStat.BaseDepressionFillRate, ModifierMode.PercentageChange, 65f);
        dvdBoosters[6] = CreateChildBooster(dvdBoostersParent, "DVDBooster_6", BoosterStat.TrashDirtinessIncrease, ModifierMode.PercentageChange, 75f);
        dvdBoosters[7] = CreateChildBooster(dvdBoostersParent, "DVDBooster_7", BoosterStat.BaseFoodCost, ModifierMode.PercentageChange, 60f);
        dvdBoosters[8] = CreateChildBooster(dvdBoostersParent, "DVDBooster_8", BoosterStat.SleepReduction, ModifierMode.PercentageChange, -50f);
        dvdBoosters[9] = CreateChildBooster(dvdBoostersParent, "DVDBooster_9", BoosterStat.PenaltyMultiplier, ModifierMode.PercentageChange, 100f);

        // --- Populate UpgradeManager Player Path (Index 0) ---
        UpgradePath playerPath = manager.upgradePaths[0];
        playerPath.targetRenderer = dvdLogoRenderer;
        if (playerPath.tiers == null) playerPath.tiers = new List<UpgradeTier>();

        string[] playerNames = new string[]
        {
            "Dumpster Sanyo",
            "Pawnshop Apex",
            "Taped Magnavox",
            "Overclocked Deck",
            "Repo'd Trinitron",
            "Smoking Turbo",
            "Landlord Bribe",
            "Gold Panasonic",
            "Bootleg Monolith",
            "Doom Accelerator"
        };
        float[] playerCosts = new float[] { 0f, 25f, 75f, 180f, 380f, 750f, 1400f, 2500f, 4500f, 8000f };
        float[] playerMults = new float[] { 1.00f, 1.40f, 1.90f, 2.60f, 3.50f, 4.70f, 6.20f, 8.00f, 10.50f, 14.00f };

        while (playerPath.tiers.Count < 10) playerPath.tiers.Add(new UpgradeTier());
        while (playerPath.tiers.Count > 10) playerPath.tiers.RemoveAt(playerPath.tiers.Count - 1);

        for (int i = 0; i < 10; i++)
        {
            var tier = playerPath.tiers[i];
            tier.tierName = playerNames[i];
            tier.cost = playerCosts[i];
            tier.multiplierValue = playerMults[i];
            tier.previewPrefab = playerPrefabs[i];
            tier.previewPositionOffset = new Vector3(0f, -150f, 0f);
            tier.previewScale = new Vector3(75f, 75f, 75f);
            tier.previewRotation = Vector3.zero;
            tier.worldGameObject = worldPlayers[i];
            tier.logoMaterial = logoMaterials[i];
            tier.booster = playerBoosters[i];
        }

        // --- Populate UpgradeManager DVD Path (Index 1) ---
        UpgradePath dvdPath = manager.upgradePaths[1];
        dvdPath.targetRenderer = dvdLogoRenderer;
        if (dvdPath.tiers == null) dvdPath.tiers = new List<UpgradeTier>();

        string[] dvdNames = new string[]
        {
            "Sharpie CDR",
            "Bargain Kung-Fu",
            "Mortgage Seminar",
            "Psychic Hotline",
            "Dashcam Cheaters",
            "Depressed Fishing",
            "Banned Commercials",
            "Foreclosure Tour",
            "Warehouse Bootleg",
            "Executive Severance"
        };
        float[] dvdCosts = new float[] { 0f, 30f, 90f, 220f, 450f, 900f, 1700f, 3200f, 5800f, 10000f };
        float[] dvdMults = new float[] { 1f, 5f, 15f, 40f, 100f, 250f, 600f, 1500f, 3500f, 10000f };

        while (dvdPath.tiers.Count < 10) dvdPath.tiers.Add(new UpgradeTier());
        while (dvdPath.tiers.Count > 10) dvdPath.tiers.RemoveAt(dvdPath.tiers.Count - 1);

        for (int i = 0; i < 10; i++)
        {
            var tier = dvdPath.tiers[i];
            tier.tierName = dvdNames[i];
            tier.cost = dvdCosts[i];
            tier.multiplierValue = dvdMults[i];
            tier.previewPrefab = dvdPrefabs[i];
            tier.previewPositionOffset = Vector3.zero;
            tier.previewScale = new Vector3(20f, 20f, 20f);
            tier.previewRotation = new Vector3(90f, 0f, 0f);
            tier.dvdVideoFileName = (i == 0) ? "1.mp4" : (i == 1 ? "2.mp4" : "");
            tier.logoMaterial = logoMaterials[i];
            tier.booster = dvdBoosters[i];
        }

        EditorUtility.SetDirty(manager);
    }

    private static BoosterObject AddOrUpdateModifier(GameObject go, BoosterStat stat, ModifierMode mode, float val)
    {
        if (go == null) return null;
        BoosterObject booster = go.GetComponent<BoosterObject>();
        if (booster == null) booster = go.AddComponent<BoosterObject>();

        booster.modifiers.Clear();
        booster.modifiers.Add(new StatModifier { stat = stat, mode = mode, value = val });
        EditorUtility.SetDirty(booster);
        return booster;
    }

    private static BoosterObject CreateChildBooster(Transform parent, string name, BoosterStat stat, ModifierMode mode, float val)
    {
        Transform t = parent.Find(name);
        GameObject go;
        if (t == null)
        {
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
        }
        else
        {
            go = t.gameObject;
        }

        return AddOrUpdateModifier(go, stat, mode, val);
    }

    private static void SetupPermanentBoosters()
    {
        Debug.Log("--- Setting up all 19 Permanent Booster Objects on Shelves ---");
        GameObject pbo = GameObject.Find("permanentBoosterObjects");
        if (pbo == null)
        {
            Debug.LogError("permanentBoosterObjects parent not found in scene!");
            return;
        }

        ParticleSystem smokePrefab = AssetDatabase.LoadAssetAtPath<ParticleSystem>(SmokePrefabPath);
        AudioClip buildupClip = AssetDatabase.LoadAssetAtPath<AudioClip>(BuildupSoundPath);
        AudioClip buyClip = AssetDatabase.LoadAssetAtPath<AudioClip>(BuySoundPath);
        AudioClip cantAffordClip = AssetDatabase.LoadAssetAtPath<AudioClip>(CantAffordSoundPath);

        // 1. Setup the 5 existing scene objects (Rebalanced and renamed for dark bachelor depression vibes)
        SetupExistingPermanentItem(pbo, "SM_Book01", "Overdue Final Tax Notices", 50f,
            "+80% Money per Edge Bounce\n+45% Depression Fill Rate",
            BoosterStat.MoneyPerEdgeBounce, ModifierMode.PercentageChange, 80f,
            BoosterStat.BaseDepressionFillRate, ModifierMode.PercentageChange, 45f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupExistingPermanentItem(pbo, "SM_Book02", "Ex's Left-Behind Diary", 110f,
            "+120% Money per Corner Bounce\n+60% Sleep Fill Rate",
            BoosterStat.MoneyPerCornerBounce, ModifierMode.PercentageChange, 120f,
            BoosterStat.SleepFillRate, ModifierMode.PercentageChange, 60f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupExistingPermanentItem(pbo, "SM_Book03", "Self-Help Book (Still in Shrinkwrap)", 150f,
            "-50% Depression Fill Rate\n+50% Base Food Cost",
            BoosterStat.BaseDepressionFillRate, ModifierMode.PercentageChange, -50f,
            BoosterStat.BaseFoodCost, ModifierMode.PercentageChange, 50f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupExistingPermanentItem(pbo, "SM_Book04", "Gross Anatomy Textbook", 200f,
            "+60% Bounce Speed\n+75% Toilet Dirtiness",
            BoosterStat.BounceSpeed, ModifierMode.PercentageChange, 60f,
            BoosterStat.ToiletDirtinessIncrease, ModifierMode.PercentageChange, 75f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupExistingPermanentItem(pbo, "LanternV1", "Stale Scented Mood Candle", 180f,
            "-50% Sleep Fill Rate\n+60% Sleep Dirtiness",
            BoosterStat.SleepFillRate, ModifierMode.PercentageChange, -50f,
            BoosterStat.SleepDirtinessIncrease, ModifierMode.PercentageChange, 60f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        // 2. Setup Existing 5 Cubes
        SetupCubePermanentItem(pbo, "Cube_Shelf_Lower_1", new Vector3(-0.42f, 1.322f, -3.87f),
            "Half-Empty Anti-Depressant Bottle", 250f,
            "+120% Sleep Depression Reduction\n+50% Hunger Fill Rate",
            BoosterStat.SleepDepressionReduction, ModifierMode.PercentageChange, 120f,
            BoosterStat.HungerFillRate, ModifierMode.PercentageChange, 50f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_Upper_1", new Vector3(-0.22f, 1.322f, -3.87f),
            "Crusty Gas Station Scratch-Off", 320f,
            "+70% Money on All Bounces\n+80% Inflation Rate",
            BoosterStat.MoneyAllBounces, ModifierMode.PercentageChange, 70f,
            BoosterStat.InflationRatePerDollar, ModifierMode.PercentageChange, 80f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_Upper_2", new Vector3(-0.42f, 1.95f, -3.87f),
            "Emotional Support Bong", 380f,
            "+120% Food Hunger Reduction\n+50% Depression Fill Rate",
            BoosterStat.FoodHungerReduction, ModifierMode.PercentageChange, 120f,
            BoosterStat.BaseDepressionFillRate, ModifierMode.PercentageChange, 50f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_Upper_3", new Vector3(-0.22f, 1.95f, -3.87f),
            "Moldy Takeout Styrofoam", 440f,
            "+100% Cleaning Dirt Reduction\n+50% Sleep Fill Rate",
            BoosterStat.CleanDirtinessReduction, ModifierMode.PercentageChange, 100f,
            BoosterStat.SleepFillRate, ModifierMode.PercentageChange, 50f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_Upper_4", new Vector3(-0.02f, 1.95f, -3.87f),
            "Flask of Warm Cooking Wine", 520f,
            "+150% Money per Corner Bounce\n+75% Penalty Multiplier",
            BoosterStat.MoneyPerCornerBounce, ModifierMode.PercentageChange, 150f,
            BoosterStat.PenaltyMultiplier, ModifierMode.PercentageChange, 75f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        // 3. Setup New 9 Cubes (Items 11 through 19)
        SetupCubePermanentItem(pbo, "Cube_Shelf_11", new Vector3(-0.02f, 1.322f, -3.87f),
            "Expired Xanax in a Pez Dispenser", 220f,
            "-60% Depression Fill Rate\n+70% Toilet Dirtiness",
            BoosterStat.BaseDepressionFillRate, ModifierMode.PercentageChange, -60f,
            BoosterStat.ToiletDirtinessIncrease, ModifierMode.PercentageChange, 70f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_12", new Vector3(0.18f, 1.322f, -3.87f),
            "Shoddy Lithium Battery Charger", 270f,
            "+50% Bounce Speed\n+40% Depression Fill Rate",
            BoosterStat.BounceSpeed, ModifierMode.PercentageChange, 50f,
            BoosterStat.BaseDepressionFillRate, ModifierMode.PercentageChange, 40f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_13", new Vector3(0.34f, 1.322f, -3.87f),
            "Overflowing Ceramic Ashtray", 340f,
            "-70% Toilet Dirtiness\n+60% Hunger Fill Rate",
            BoosterStat.ToiletDirtinessIncrease, ModifierMode.PercentageChange, -70f,
            BoosterStat.HungerFillRate, ModifierMode.PercentageChange, 60f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_14", new Vector3(0.18f, 1.95f, -3.87f),
            "Sticky Magnifying Glass", 390f,
            "+100% Money per Edge Bounce\n+60% Base Food Cost",
            BoosterStat.MoneyPerEdgeBounce, ModifierMode.PercentageChange, 100f,
            BoosterStat.BaseFoodCost, ModifierMode.PercentageChange, 60f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_15", new Vector3(0.38f, 1.95f, -3.87f),
            "Pirated Cable Descrambler", 460f,
            "+120% Sleep Depression Reduction\n+50% Base Food Cost",
            BoosterStat.SleepDepressionReduction, ModifierMode.PercentageChange, 120f,
            BoosterStat.BaseFoodCost, ModifierMode.PercentageChange, 50f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_16", new Vector3(0.58f, 1.95f, -3.87f),
            "Questionable USB Thumbdrive", 500f,
            "+75% Money on All Bounces\n+60% Sleep Dirtiness",
            BoosterStat.MoneyAllBounces, ModifierMode.PercentageChange, 75f,
            BoosterStat.SleepDirtinessIncrease, ModifierMode.PercentageChange, 60f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_17", new Vector3(-0.42f, 0.825f, -3.87f),
            "Pure Regret Paperweight", 560f,
            "+110% Food Hunger Reduction\n+65% Penalty Multiplier",
            BoosterStat.FoodHungerReduction, ModifierMode.PercentageChange, 110f,
            BoosterStat.PenaltyMultiplier, ModifierMode.PercentageChange, 65f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_18", new Vector3(-0.32f, 0.825f, -3.87f),
            "Knockoff Golden Buddha Statue", 620f,
            "+120% Cleaning Dirt Reduction\n+70% Hunger Fill Rate",
            BoosterStat.CleanDirtinessReduction, ModifierMode.PercentageChange, 120f,
            BoosterStat.HungerFillRate, ModifierMode.PercentageChange, 70f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);

        SetupCubePermanentItem(pbo, "Cube_Shelf_19", new Vector3(0.48f, 0.825f, -3.87f),
            "Broken Nostalgia Music Box", 700f,
            "-75% Depression Fill Rate\n+80% Sleep Fill Rate",
            BoosterStat.BaseDepressionFillRate, ModifierMode.PercentageChange, -75f,
            BoosterStat.SleepFillRate, ModifierMode.PercentageChange, 80f,
            smokePrefab, buildupClip, buyClip, cantAffordClip);
    }

    private static void SetupExistingPermanentItem(GameObject parent, string childName, string displayName, float price,
        string effectDesc, BoosterStat posStat, ModifierMode posMode, float posVal,
        BoosterStat negStat, ModifierMode negMode, float negVal,
        ParticleSystem smokePrefab, AudioClip buildupClip, AudioClip buyClip, AudioClip cantAffordClip)
    {
        Transform existing = parent.transform.Find(childName);
        if (existing == null)
        {
            Debug.LogWarning($"Existing item {childName} not found under permanentBoosterObjects!");
            return;
        }

        // Check if already split into Buyable and Active
        Transform existingBuyable = parent.transform.Find(childName + "_Buyable");
        Transform existingActive = parent.transform.Find(childName + "_Active");

        GameObject buyableObj = existingBuyable != null ? existingBuyable.gameObject : null;
        GameObject activeObj = existingActive != null ? existingActive.gameObject : null;

        if (buyableObj == null && activeObj == null)
        {
            // Convert existing GameObject to the Active prop
            activeObj = existing.gameObject;
            activeObj.name = childName + "_Active";

            // Duplicate it to become the Buyable prop
            buyableObj = Object.Instantiate(activeObj, parent.transform);
            buyableObj.name = childName + "_Buyable";
            buyableObj.transform.localPosition = activeObj.transform.localPosition;
            buyableObj.transform.localRotation = activeObj.transform.localRotation;
            buyableObj.transform.localScale = activeObj.transform.localScale;
        }

        ConfigurePermanentPair(buyableObj, activeObj, displayName, price, effectDesc,
            posStat, posMode, posVal, negStat, negMode, negVal,
            smokePrefab, buildupClip, buyClip, cantAffordClip);
    }

    private static void SetupCubePermanentItem(GameObject parent, string name, Vector3 localPos, string displayName, float price,
        string effectDesc, BoosterStat posStat, ModifierMode posMode, float posVal,
        BoosterStat negStat, ModifierMode negMode, float negVal,
        ParticleSystem smokePrefab, AudioClip buildupClip, AudioClip buyClip, AudioClip cantAffordClip)
    {
        Transform existingBuyable = parent.transform.Find(name + "_Buyable");
        Transform existingActive = parent.transform.Find(name + "_Active");

        GameObject buyableObj = existingBuyable != null ? existingBuyable.gameObject : null;
        GameObject activeObj = existingActive != null ? existingActive.gameObject : null;

        if (buyableObj == null)
        {
            buyableObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            buyableObj.name = name + "_Buyable";
            buyableObj.transform.SetParent(parent.transform, false);
            buyableObj.transform.localPosition = localPos;
            buyableObj.transform.localRotation = Quaternion.identity;
            buyableObj.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        }

        if (activeObj == null)
        {
            activeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            activeObj.name = name + "_Active";
            activeObj.transform.SetParent(parent.transform, false);
            activeObj.transform.localPosition = localPos;
            activeObj.transform.localRotation = Quaternion.identity;
            activeObj.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        }

        ConfigurePermanentPair(buyableObj, activeObj, displayName, price, effectDesc,
            posStat, posMode, posVal, negStat, negMode, negVal,
            smokePrefab, buildupClip, buyClip, cantAffordClip);
    }

    private static void ConfigurePermanentPair(GameObject buyableObj, GameObject activeObj, string displayName, float price,
        string effectDesc, BoosterStat posStat, ModifierMode posMode, float posVal,
        BoosterStat negStat, ModifierMode negMode, float negVal,
        ParticleSystem smokePrefab, AudioClip buildupClip, AudioClip buyClip, AudioClip cantAffordClip)
    {
        DVDLogoBouncer bouncer = Object.FindObjectOfType<DVDLogoBouncer>();

        // 1. Setup Active Object (Starts SetActive(false))
        activeObj.SetActive(false);

        // Ensure colliders exist
        if (activeObj.GetComponentInChildren<Collider>() == null)
        {
            activeObj.AddComponent<BoxCollider>();
        }

        // BoosterObject
        BoosterObject booster = activeObj.GetComponent<BoosterObject>();
        if (booster == null) booster = activeObj.AddComponent<BoosterObject>();
        booster.modifiers.Clear();
        booster.modifiers.Add(new StatModifier { stat = posStat, mode = posMode, value = posVal });
        booster.modifiers.Add(new StatModifier { stat = negStat, mode = negMode, value = negVal });
        EditorUtility.SetDirty(booster);

        // InteractableObject
        InteractableObject activeInteract = activeObj.GetComponent<InteractableObject>();
        if (activeInteract == null) activeInteract = activeObj.AddComponent<InteractableObject>();
        activeInteract.hoverText = $"{displayName}\n(Hold Click to Break)";
        EditorUtility.SetDirty(activeInteract);

        // InteractVisualizer
        InteractVisualizer activeVis = activeObj.GetComponent<InteractVisualizer>();
        if (activeVis == null) activeVis = activeObj.AddComponent<InteractVisualizer>();
        activeVis.enablePeriodicGlow = false; // Unlocked object doesn't need to pulsate periodically
        EditorUtility.SetDirty(activeVis);

        // BreakablePermanentBooster
        BreakablePermanentBooster breakable = activeObj.GetComponent<BreakablePermanentBooster>();
        if (breakable == null) breakable = activeObj.AddComponent<BreakablePermanentBooster>();
        breakable.holdDuration = 1.5f;
        breakable.levitateHeight = 0f;
        breakable.hoverMessage = $"{displayName}\n(Hold Click to Break)";
        breakable.shakeSound = buildupClip;
        breakable.shakeVolume = 1f;
        breakable.poofSound = buyClip;
        breakable.poofVolume = 1f;
        breakable.poofParticlePrefab = smokePrefab;
        breakable.boosterObject = booster;
        EditorUtility.SetDirty(breakable);

        // 2. Setup Buyable Object (Starts SetActive(true))
        buyableObj.SetActive(true);

        // Ensure colliders exist
        if (buyableObj.GetComponentInChildren<Collider>() == null)
        {
            buyableObj.AddComponent<BoxCollider>();
        }

        // InteractableObject
        InteractableObject buyInteract = buyableObj.GetComponent<InteractableObject>();
        if (buyInteract == null) buyInteract = buyableObj.AddComponent<InteractableObject>();

        // InteractVisualizer
        InteractVisualizer buyVis = buyableObj.GetComponent<InteractVisualizer>();
        if (buyVis == null) buyVis = buyableObj.AddComponent<InteractVisualizer>();
        buyVis.enablePeriodicGlow = true;
        EditorUtility.SetDirty(buyVis);

        // Apply Grayed-Out Material to all renderers of the unbought prop
        Material grayedOutMat = AssetDatabase.LoadAssetAtPath<Material>(GrayedOutMaterialPath);
        if (grayedOutMat != null)
        {
            foreach (Renderer r in buyableObj.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = grayedOutMat;
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);
            }
        }

        // BuyableBoosterProp
        BuyableBoosterProp buyable = buyableObj.GetComponent<BuyableBoosterProp>();
        if (buyable == null) buyable = buyableObj.AddComponent<BuyableBoosterProp>();
        buyable.propName = displayName;
        buyable.price = price;
        buyable.effectDescription = effectDesc;
        buyable.holdPrompt = "Hold Click to Buy";
        buyable.notEnoughMoneyPrompt = "Not enough money!";
        buyable.unlockedPropObject = activeObj;
        buyable.economyBouncer = bouncer;
        buyable.holdDuration = 1.5f;
        buyable.levitateHeight = 0f;
        buyable.shakeSound = buildupClip;
        buyable.purchasePoofSound = buyClip;
        buyable.cantAffordSound = cantAffordClip;
        buyable.poofParticlePrefab = smokePrefab;
        buyable.UpdateHoverText();
        EditorUtility.SetDirty(buyable);

        EditorUtility.SetDirty(activeObj);
        EditorUtility.SetDirty(buyableObj);
    }

    private static void SetupPlayerMovementSpeed()
    {
        GridCrawlerController controller = Object.FindObjectOfType<GridCrawlerController>();
        if (controller != null)
        {
            controller.moveDuration = 0.3f;
            controller.rotateDuration = 0.3f;
            controller.bumpDuration = 0.2f;
            controller.bumpDistance = 0.3f;
            controller.postActionCooldown = 0.2f;
            EditorUtility.SetDirty(controller);
            Debug.Log("Updated GridCrawlerController movement speed settings.");
        }
    }
}
