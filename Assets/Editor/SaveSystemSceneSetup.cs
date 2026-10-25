#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

// Manual setup utility for Save System - only executes when manually clicked via Tools menu.
public static class SaveSystemSceneSetup
{
    private const string RoomScenePath = "Assets/Scenes/01Day.unity";
    private const string BarScenePath = "Assets/Barni/BarGameBarni.unity";
    private const string DreamScenePath = "Assets/Kristof/KristofDreamGames.unity";
    private const string ProjectFontGuid = "6d795282a2f39f94ab1e799f42073e3f";

    private static AudioClip cachedGunCockSound;
    private static AudioClip cachedGunshotSound;
    private static AudioClip cachedHeartbeatLoop;
    private static Shader cachedBreakdownShader;

    [MenuItem("Tools/Setup Run Save & Summary System Across All Scenes")]
    public static void SetupAllScenes()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[SaveSystemSceneSetup] Please exit Play Mode before running scene setup.");
            return;
        }

        EnsureBuildSettingsIncludeDreamAndBarScenes();

        TMP_FontAsset fontAsset = LoadProjectFont();

        // 1. Setup 01Day.unity
        SetupRoomScene(fontAsset);

        // 2. Setup BarGameBarni.unity
        SetupBarScene(fontAsset);

        // 3. Setup KristofDreamGames.unity
        SetupDreamScene(fontAsset);

        // Return to 01Day.unity
        EditorSceneManager.OpenScene(RoomScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        Debug.Log("[SaveSystemSceneSetup] Successfully set up RunSaveManager and RunStatsSummaryUI across 01Day, BarGameBarni, and KristofDreamGames!");
    }

    private static void EnsureBuildSettingsIncludeDreamAndBarScenes()
    {
        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
        List<EditorBuildSettingsScene> updated = new List<EditorBuildSettingsScene>(existing);

        bool changed = false;
        void EnsureSceneEnabled(string path)
        {
            for (int i = 0; i < updated.Count; i++)
            {
                if (string.Equals(updated[i].path, path, StringComparison.OrdinalIgnoreCase))
                {
                    if (!updated[i].enabled)
                    {
                        updated[i].enabled = true;
                        changed = true;
                    }
                    return;
                }
            }

            updated.Add(new EditorBuildSettingsScene(path, true));
            changed = true;
        }

        EnsureSceneEnabled(RoomScenePath);
        EnsureSceneEnabled(BarScenePath);
        EnsureSceneEnabled(DreamScenePath);
        EnsureSceneEnabled("Assets/Scenes/06Menu.unity");

        if (changed)
        {
            EditorBuildSettings.scenes = updated.ToArray();
            Debug.Log("[SaveSystemSceneSetup] Enabled KristofDreamGames, BarGameBarni, 01Day, and 06Menu in EditorBuildSettings.");
        }
    }

    private static void SetupRoomScene(TMP_FontAsset fontAsset)
    {
        Scene scene = EditorSceneManager.OpenScene(RoomScenePath, OpenSceneMode.Single);

        // 1. Ensure RunSaveManager root object
        EnsureRunSaveManagerInScene();

        // 2. Ensure UpgradeManager in 01Day has playerbasic assigned to upgradePaths[0].tiers[0].worldGameObject
        UpgradeManager upgradeMgr = UnityEngine.Object.FindObjectOfType<UpgradeManager>(true);
        if (upgradeMgr != null && upgradeMgr.upgradePaths != null)
        {
            GameObject playerBasicObj = FindGameObjectInSceneByName(scene, "playerbasic");
            foreach (UpgradePath path in upgradeMgr.upgradePaths)
            {
                if (path != null && path.category == UpgradeCategory.SpeedMultiplier && path.tiers != null && path.tiers.Count > 0)
                {
                    if (path.tiers[0].worldGameObject == null && playerBasicObj != null)
                    {
                        path.tiers[0].worldGameObject = playerBasicObj;
                        EditorUtility.SetDirty(upgradeMgr);
                        Debug.Log("[SaveSystemSceneSetup] Wired 'playerbasic' to UpgradeManager SpeedMultiplier tier 0 in 01Day.");
                    }
                }
            }
        }

        // 3. Configure DeathSeq & DeathSeq/Canvas with RunStatsSummaryUI (DeathToMenu mode)
        DepressionBreakdownSequence breakdown = UnityEngine.Object.FindObjectOfType<DepressionBreakdownSequence>(true);
        if (breakdown != null)
        {
            cachedGunCockSound = breakdown.gunCockingSound;
            cachedGunshotSound = breakdown.gunshotSound;
            cachedHeartbeatLoop = breakdown.optionalHeartbeatLoop;
            cachedBreakdownShader = breakdown.breakdownShader;

            GameObject canvasObj = breakdown.endingPopupUI;
            if (canvasObj == null)
            {
                Transform childCanvas = breakdown.transform.Find("Canvas");
                if (childCanvas != null) canvasObj = childCanvas.gameObject;
            }
            if (canvasObj == null)
            {
                canvasObj = new GameObject("Canvas");
                canvasObj.transform.SetParent(breakdown.transform, false);
                breakdown.endingPopupUI = canvasObj;
            }

            ConfigureSummaryCanvasObject(canvasObj, RunStatsSummaryUI.SummaryMode.DeathToMenu, fontAsset, startActive: false);
            breakdown.endingPopupUI = canvasObj;
            EditorUtility.SetDirty(breakdown);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetupBarScene(TMP_FontAsset fontAsset)
    {
        Scene scene = EditorSceneManager.OpenScene(BarScenePath, OpenSceneMode.Single);

        // 1. Ensure RunSaveManager root object
        EnsureRunSaveManagerInScene();

        // 2. Fix door SceneTransitionOut in BarGameBarni so it transitions cleanly to 01Day and disables player input
        GridCrawlerController playerController = UnityEngine.Object.FindObjectOfType<GridCrawlerController>(true);
        BarManager barManager = UnityEngine.Object.FindObjectOfType<BarManager>(true);

        SceneTransitionOut[] transitionsOut = UnityEngine.Object.FindObjectsOfType<SceneTransitionOut>(true);
        foreach (SceneTransitionOut trans in transitionsOut)
        {
            if (trans == null) continue;
            if (trans.gameObject.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                trans.sceneToLoad = "01Day";
                List<MonoBehaviour> disableList = new List<MonoBehaviour>();
                if (playerController != null) disableList.Add(playerController);
                if (barManager != null) disableList.Add(barManager);
                trans.scriptsToDisable = disableList.ToArray();
                EditorUtility.SetDirty(trans);
            }
        }

        // 3. Ensure DeathSeq & DepressionBreakdownSequence exist in BarGameBarni so dying or ending run in the Bar works identically
        DepressionBreakdownSequence breakdown = UnityEngine.Object.FindObjectOfType<DepressionBreakdownSequence>(true);
        if (breakdown == null)
        {
            GameObject deathSeqObj = new GameObject("DeathSeq");
            breakdown = deathSeqObj.AddComponent<DepressionBreakdownSequence>();
        }

        breakdown.barManager = barManager;
        breakdown.targetCamera = Camera.main;
        if (playerController != null)
        {
            breakdown.playerRootObject = playerController.gameObject;
            breakdown.playerAudioListener = playerController.GetComponentInChildren<AudioListener>(true);
            breakdown.scriptsToDisableDuringRampUp = new MonoBehaviour[] { playerController };
        }
        if (breakdown.gunCockingSound == null) breakdown.gunCockingSound = cachedGunCockSound;
        if (breakdown.gunshotSound == null) breakdown.gunshotSound = cachedGunshotSound;
        if (breakdown.optionalHeartbeatLoop == null) breakdown.optionalHeartbeatLoop = cachedHeartbeatLoop;
        if (breakdown.breakdownShader == null)
        {
            breakdown.breakdownShader = cachedBreakdownShader != null
                ? cachedBreakdownShader
                : Shader.Find("Custom/DepressionBreakdownShader");
        }

        GameObject deathCanvasObj = breakdown.endingPopupUI;
        if (deathCanvasObj == null)
        {
            Transform childCanvas = breakdown.transform.Find("Canvas");
            if (childCanvas != null) deathCanvasObj = childCanvas.gameObject;
        }
        if (deathCanvasObj == null)
        {
            deathCanvasObj = new GameObject("Canvas");
            deathCanvasObj.transform.SetParent(breakdown.transform, false);
        }

        ConfigureSummaryCanvasObject(deathCanvasObj, RunStatsSummaryUI.SummaryMode.DeathToMenu, fontAsset, startActive: false);
        breakdown.endingPopupUI = deathCanvasObj;
        EditorUtility.SetDirty(breakdown);

        if (barManager != null)
        {
            barManager.depressionBreakdown = breakdown;
            EditorUtility.SetDirty(barManager);
        }

        // 4. Ensure PauseManager is also set up in BarGameBarni
        PauseManagerSceneSetup.SetupPauseManagerInActiveScene();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetupDreamScene(TMP_FontAsset fontAsset)
    {
        Scene scene = EditorSceneManager.OpenScene(DreamScenePath, OpenSceneMode.Single);

        // 1. Ensure RunSaveManager root object
        EnsureRunSaveManagerInScene();

        // 2. Configure Main Camera for clean dark CRT background
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            EditorUtility.SetDirty(mainCam);
        }

        // 3. Ensure EventSystem with InputSystemUIInputModule exists
        EventSystem es = UnityEngine.Object.FindObjectOfType<EventSystem>(true);
        if (es == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            es = esObj.AddComponent<EventSystem>();
            esObj.AddComponent<InputSystemUIInputModule>();
            EditorUtility.SetDirty(esObj);
        }
        else
        {
            es.gameObject.SetActive(true);
            es.enabled = true;
            if (es.GetComponent<BaseInputModule>() == null)
            {
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            EditorUtility.SetDirty(es.gameObject);
        }

        // 4. Create or update DreamSequenceCanvas with RunStatsSummaryUI (DreamWakeUp mode -> "WAKE UP" button)
        RunStatsSummaryUI existingSummary = UnityEngine.Object.FindObjectOfType<RunStatsSummaryUI>(true);
        GameObject dreamCanvasObj = existingSummary != null ? existingSummary.gameObject : FindGameObjectInSceneByName(scene, "DreamSequenceCanvas");
        if (dreamCanvasObj == null)
        {
            dreamCanvasObj = new GameObject("DreamSequenceCanvas");
        }

        ConfigureSummaryCanvasObject(dreamCanvasObj, RunStatsSummaryUI.SummaryMode.DreamWakeUp, fontAsset, startActive: true);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static RunSaveManager EnsureRunSaveManagerInScene()
    {
        RunSaveManager existing = UnityEngine.Object.FindObjectOfType<RunSaveManager>(true);
        if (existing != null)
        {
            if (existing.transform.parent != null)
            {
                existing.transform.SetParent(null, false);
            }
            existing.gameObject.name = "RunSaveManager";
            existing.gameObject.SetActive(true);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        GameObject go = new GameObject("RunSaveManager");
        RunSaveManager mgr = go.AddComponent<RunSaveManager>();
        EditorUtility.SetDirty(go);
        return mgr;
    }

    private static void ConfigureSummaryCanvasObject(
        GameObject canvasObj,
        RunStatsSummaryUI.SummaryMode mode,
        TMP_FontAsset fontAsset,
        bool startActive)
    {
        canvasObj.layer = 5; // UI Layer

        RunStatsSummaryUI summaryUI = canvasObj.GetComponent<RunStatsSummaryUI>();
        if (summaryUI == null)
        {
            summaryUI = canvasObj.AddComponent<RunStatsSummaryUI>();
        }

        summaryUI.mode = mode;
        if (summaryUI.customFont == null) summaryUI.customFont = fontAsset;

        // NEVER rebuild or destroy existing hierarchy if canvas already has children!
        if (canvasObj.transform.childCount == 0)
        {
            summaryUI.RebuildSummaryUIHierarchy();
        }

        if (summaryUI.actionButton != null)
        {
            // Ensure persistent Inspector listener is also visible & wired
            int existingCount = summaryUI.actionButton.onClick.GetPersistentEventCount();
            for (int i = existingCount - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(summaryUI.actionButton.onClick, i);
            }
            UnityEventTools.AddVoidPersistentListener(summaryUI.actionButton.onClick, summaryUI.OnActionButtonClicked);
            EditorUtility.SetDirty(summaryUI.actionButton);
        }

        canvasObj.SetActive(startActive);
        EditorUtility.SetDirty(summaryUI);
        EditorUtility.SetDirty(canvasObj);
    }

    private static TMP_FontAsset LoadProjectFont()
    {
        string path = AssetDatabase.GUIDToAssetPath(ProjectFontGuid);
        if (!string.IsNullOrEmpty(path))
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null) return font;
        }
        return TMP_Settings.defaultFontAsset;
    }

    private static GameObject FindGameObjectInSceneByName(Scene scene, string exactName)
    {
        if (!scene.IsValid()) return null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindTransformRecursive(roots[i].transform, exactName);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static Transform FindTransformRecursive(Transform current, string exactName)
    {
        if (current == null) return null;
        if (string.Equals(current.name, exactName, StringComparison.Ordinal)) return current;

        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindTransformRecursive(current.GetChild(i), exactName);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
