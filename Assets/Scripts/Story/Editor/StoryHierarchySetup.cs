using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using TMPro;

namespace Story.Editor
{
    [InitializeOnLoad]
    public static class StoryHierarchySetup
    {
        private const string SceneName = "01Day";
        public const string TemplatePrefabPath = "Assets/Resources/StoryCardCanvasTemplate.prefab";

        static StoryHierarchySetup()
        {
            EditorApplication.delayCall += EnsureHierarchyOnLoad;
            EditorSceneManager.sceneOpened += (scene, mode) =>
            {
                if (scene.name == SceneName) EnsureHierarchyOnLoad();
            };
        }

        private static void EnsureHierarchyOnLoad()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.isLoaded && (activeScene.name == SceneName || activeScene.path.Contains(SceneName)))
            {
                StoryManager manager = UnityEngine.Object.FindAnyObjectByType<StoryManager>(FindObjectsInactive.Include);
                StoryCardController controller = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);

                if (manager == null || controller == null)
                {
                    SetupStoryHierarchy(true);
                }
                else if (controller != null && PrefabUtility.IsPartOfAnyPrefab(controller.gameObject))
                {
                    // Permanently unpack prefab instance into scene hierarchy so user modifications are permanent
                    PrefabUtility.UnpackPrefabInstance(controller.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    EditorUtility.SetDirty(controller.gameObject);
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    EditorSceneManager.SaveScene(activeScene);
                    Debug.Log("[StoryHierarchySetup] Unpacked StoryCardCanvas permanently into scene hierarchy!");
                }
            }
        }

        [MenuItem("Tools/Story/Unpack Canvas Templates Permanently Into Hierarchy")]
        public static void UnpackCanvasPermanently()
        {
            StoryCardController controller = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                if (PrefabUtility.IsPartOfAnyPrefab(controller.gameObject))
                {
                    PrefabUtility.UnpackPrefabInstance(controller.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                EditorUtility.SetDirty(controller.gameObject);
                var activeScene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
                Debug.Log("[StoryHierarchySetup] Successfully unpacked StoryCardCanvas permanently into scene hierarchy!");
                EditorUtility.DisplayDialog("Templates Unpacked Permanently",
                    "StoryCardCanvas has been completely unpacked into the scene hierarchy!\n\n" +
                    "All templates (CardContainer, ConsequencePanel, ReckoningPanel) are now permanent scene GameObjects.\n\n" +
                    "Any visual tweaks (sizes, positions, fonts, colors) are now saved directly in the scene and will never be overwritten.",
                    "OK");
            }
            else
            {
                SetupStoryHierarchy(true);
            }
        }

        [MenuItem("Tools/Story/Setup Story Hierarchy in Scene")]
        public static void SetupStoryHierarchyManual()
        {
            SetupStoryHierarchy(true);
        }

        [MenuItem("Tools/Story/Save Current Canvas As Template Prefab")]
        public static void SaveCurrentCanvasAsTemplatePrefab()
        {
            StoryCardController canvasController = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            if (canvasController == null)
            {
                EditorUtility.DisplayDialog("Save Failed", "Could not find StoryCardCanvas in the active scene to save!", "OK");
                return;
            }

            // Ensure directory exists
            string dir = Path.GetDirectoryName(TemplatePrefabPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Canvas canvas = canvasController.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvasController.SetupScreenSpaceCamera(canvas);
            }

            // Ensure references are wired before saving
            canvasController.EnsureUIHierarchy();

            // Save as standalone prefab asset backup without connecting scene instance
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(canvasController.gameObject, TemplatePrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AssetDatabase.Refresh();

            Debug.Log($"[StoryHierarchySetup] Successfully saved StoryCardCanvas as template prefab at '{TemplatePrefabPath}'!");
            EditorUtility.DisplayDialog("Template Saved",
                "StoryCardCanvas has been saved as the default template prefab!\n\n" +
                "Any visual tweaks (sizes, positions, fonts, colors) will now be preserved and restored if you ever delete and respawn it.",
                "OK");
        }

        [MenuItem("Tools/Story/Toggle Story Canvas Visible")]
        public static void ToggleStoryCanvasVisible()
        {
            StoryCardController controller = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                bool newState = !controller.gameObject.activeSelf;
                controller.gameObject.SetActive(newState);
                Selection.activeGameObject = controller.gameObject;
                Debug.Log($"[StoryHierarchySetup] StoryCardCanvas is now {(newState ? "ACTIVE (Visible in Scene view for editing)" : "INACTIVE (Hidden)")}");
            }
            else
            {
                Debug.LogWarning("[StoryHierarchySetup] StoryCardCanvas not found. Running Setup first...");
                SetupStoryHierarchy(true);
            }
        }

        [MenuItem("Tools/Story/Templates/Show Card Swipe Template Only")]
        public static void ShowCardTemplateOnly()
        {
            StoryCardController controller = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                controller.gameObject.SetActive(true);
                if (controller.cardContainer != null) controller.cardContainer.gameObject.SetActive(true);
                if (controller.consequencePanel != null) controller.consequencePanel.SetActive(false);
                if (controller.reckoningPanel != null) controller.reckoningPanel.SetActive(false);
                Selection.activeGameObject = controller.cardContainer != null ? controller.cardContainer.gameObject : controller.gameObject;
                Debug.Log("[StoryHierarchySetup] CardContainer template is now visible for visual editing.");
            }
        }

        [MenuItem("Tools/Story/Templates/Show Consequence Template Only")]
        public static void ShowConsequenceTemplateOnly()
        {
            StoryCardController controller = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                controller.gameObject.SetActive(true);
                if (controller.cardContainer != null) controller.cardContainer.gameObject.SetActive(false);
                if (controller.consequencePanel != null) controller.consequencePanel.SetActive(true);
                if (controller.reckoningPanel != null) controller.reckoningPanel.SetActive(false);
                Selection.activeGameObject = controller.consequencePanel != null ? controller.consequencePanel : controller.gameObject;
                Debug.Log("[StoryHierarchySetup] ConsequencePanel template is now visible for visual editing.");
            }
        }

        [MenuItem("Tools/Story/Templates/Show Reckoning Stats Template Only")]
        public static void ShowReckoningTemplateOnly()
        {
            StoryCardController controller = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                controller.gameObject.SetActive(true);
                if (controller.cardContainer != null) controller.cardContainer.gameObject.SetActive(false);
                if (controller.consequencePanel != null) controller.consequencePanel.SetActive(false);
                if (controller.reckoningPanel != null) controller.reckoningPanel.SetActive(true);
                Selection.activeGameObject = controller.reckoningPanel != null ? controller.reckoningPanel : controller.gameObject;
                Debug.Log("[StoryHierarchySetup] ReckoningPanel template is now visible for visual editing.");
            }
        }

        [MenuItem("Tools/Story/Templates/Hide Canvas (Ready for Gameplay)")]
        public static void HideCanvasForGameplay()
        {
            StoryCardController controller = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                if (controller.cardContainer != null) controller.cardContainer.gameObject.SetActive(true);
                if (controller.consequencePanel != null) controller.consequencePanel.SetActive(false);
                if (controller.reckoningPanel != null) controller.reckoningPanel.SetActive(false);
                controller.gameObject.SetActive(false);
                Debug.Log("[StoryHierarchySetup] StoryCardCanvas is now hidden (ready for gameplay).");
            }
        }

        [MenuItem("Tools/Story/Force Ring Phone Now (Editor Test)")]
        public static void ForceRingPhoneMenu()
        {
            KitchenPhone phone = UnityEngine.Object.FindAnyObjectByType<KitchenPhone>(FindObjectsInactive.Include);
            if (phone == null) phone = KitchenPhone.EnsureInstanceInScene();
            if (phone != null)
            {
                phone.ForceRingPhoneInEditor();
                Debug.Log("[StoryHierarchySetup] Kitchen telephone started ringing! Walk over to it in the kitchen to answer.");
            }
        }

        [MenuItem("Tools/Story/Force Trigger Day 3 Finale Call (Test)")]
        public static void ForceTriggerDay3FinaleCall()
        {
            if (RunSaveManager.HasInstance)
            {
                RunSaveManager.Instance.postMaxDaysSurvived = 3;
                RunSaveManager.Instance.finalPhoneEncounterDone = false;
            }

            KitchenPhone phone = UnityEngine.Object.FindAnyObjectByType<KitchenPhone>(FindObjectsInactive.Include);
            if (phone == null) phone = KitchenPhone.EnsureInstanceInScene();
            if (phone != null)
            {
                phone.ForceRingPhoneInEditor();
                Debug.Log("[StoryHierarchySetup] Day 3 Climactic Call triggered! Telephone is ringing. Answer it to see the weighed stats reckoning screen.");
            }
        }

        public static void SetupStoryHierarchy(bool forceSave)
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.isLoaded) return;

            bool modified = false;

            // 1. Ensure StoryManager in scene
            StoryManager manager = UnityEngine.Object.FindAnyObjectByType<StoryManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                GameObject mgrObj = new GameObject("StoryManager");
                Undo.RegisterCreatedObjectUndo(mgrObj, "Create StoryManager");
                manager = mgrObj.AddComponent<StoryManager>();
                manager.PopulateDefaultDeck();
                modified = true;
                Debug.Log("[StoryHierarchySetup] Created 'StoryManager' in scene hierarchy with default encounter deck.");
            }
            else if (manager.calls == null || manager.calls.Count == 0)
            {
                manager.PopulateDefaultDeck();
                EditorUtility.SetDirty(manager);
                modified = true;
            }

            // 2. Ensure StoryCardCanvas in scene (instantiating from saved template prefab if available)
            StoryCardController canvasController = UnityEngine.Object.FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            GameObject canvasObj;
            if (canvasController == null)
            {
                GameObject templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
                if (templatePrefab != null)
                {
                    canvasObj = (GameObject)PrefabUtility.InstantiatePrefab(templatePrefab);
                    canvasObj.name = "StoryCardCanvas";
                    Undo.RegisterCreatedObjectUndo(canvasObj, "Instantiate StoryCardCanvas from Template");
                    PrefabUtility.UnpackPrefabInstance(canvasObj, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    canvasController = canvasObj.GetComponent<StoryCardController>();
                    Debug.Log("[StoryHierarchySetup] Restored 'StoryCardCanvas' permanently into scene hierarchy!");
                }
                else
                {
                    canvasObj = new GameObject("StoryCardCanvas");
                    Undo.RegisterCreatedObjectUndo(canvasObj, "Create StoryCardCanvas");
                    canvasController = canvasObj.AddComponent<StoryCardController>();
                    Debug.Log("[StoryHierarchySetup] Created fresh 'StoryCardCanvas' in scene hierarchy.");
                }
                modified = true;
            }
            else
            {
                canvasObj = canvasController.gameObject;
                if (PrefabUtility.IsPartOfAnyPrefab(canvasObj))
                {
                    PrefabUtility.UnpackPrefabInstance(canvasObj, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    modified = true;
                    Debug.Log("[StoryHierarchySetup] Unpacked existing 'StoryCardCanvas' permanently into scene hierarchy!");
                }
            }

            // 3. Ensure Screen Space Camera configuration and Hierarchy elements
            Canvas canvas = canvasObj.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasObj.AddComponent<Canvas>();

            // Resolve player camera (with SmoothCameraFollow and no render texture)
            Camera playerCam = null;
            SmoothCameraFollow scf = UnityEngine.Object.FindAnyObjectByType<SmoothCameraFollow>(FindObjectsInactive.Include);
            if (scf != null) playerCam = scf.GetComponent<Camera>();
            if (playerCam == null)
            {
                Camera[] allCams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var c in allCams)
                {
                    if (c != null && c.CompareTag("MainCamera") && c.targetTexture == null)
                    {
                        playerCam = c;
                        break;
                    }
                }
            }

            if (playerCam != null)
            {
                if (canvasController.targetCamera == null || canvasController.targetCamera.targetTexture != null)
                {
                    canvasController.targetCamera = playerCam;
                    modified = true;
                }
                if (canvas.worldCamera == null || canvas.worldCamera.targetTexture != null)
                {
                    canvas.worldCamera = playerCam;
                    modified = true;
                }
                if (manager.targetCamera == null || manager.targetCamera.targetTexture != null)
                {
                    manager.targetCamera = playerCam;
                    EditorUtility.SetDirty(manager);
                    modified = true;
                }
            }

            canvasController.SetupScreenSpaceCamera(canvas);
            canvasController.EnsureUIHierarchy();

            // Link StoryManager to canvas controller
            if (manager.storyCardCanvas != canvasController)
            {
                manager.storyCardCanvas = canvasController;
                EditorUtility.SetDirty(manager);
                modified = true;
            }

            // 4. By default, ensure StoryCardCanvas is SetActive(false) as requested by user
            if (canvasObj.activeSelf)
            {
                canvasObj.SetActive(false);
                modified = true;
            }

            // Ensure consequence and reckoning panels are inactive inside canvas by default
            if (canvasController.consequencePanel != null && canvasController.consequencePanel.activeSelf)
            {
                canvasController.consequencePanel.SetActive(false);
                modified = true;
            }
            if (canvasController.reckoningPanel != null && canvasController.reckoningPanel.activeSelf)
            {
                canvasController.reckoningPanel.SetActive(false);
                modified = true;
            }
            if (canvasController.cardContainer != null && !canvasController.cardContainer.gameObject.activeSelf)
            {
                canvasController.cardContainer.gameObject.SetActive(true);
                modified = true;
            }

            EditorUtility.SetDirty(canvasController);

            // 5. Ensure KitchenPhone object exists in the scene
            KitchenPhone phone = UnityEngine.Object.FindAnyObjectByType<KitchenPhone>(FindObjectsInactive.Include);
            if (phone == null)
            {
                GameObject phoneObj = new GameObject("KitchenPhone");
                Undo.RegisterCreatedObjectUndo(phoneObj, "Create KitchenPhone");
                phoneObj.transform.position = new Vector3(-5.298f, 2.277f, -1.953f);
                phoneObj.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

                // Try to instantiate phone model if available
                GameObject phonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetSlop/danthaigames/DS UK Public Telephone/Prefabs/UK Phone Distressed.prefab");
                if (phonePrefab != null)
                {
                    GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(phonePrefab, phoneObj.transform);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    model.transform.localScale = Vector3.one * 0.8f;
                }

                phone = phoneObj.AddComponent<KitchenPhone>();
                modified = true;
                Debug.Log("[StoryHierarchySetup] Created 'KitchenPhone' in kitchen wall position.");
            }

            if (modified || forceSave)
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
                if (forceSave)
                {
                    EditorSceneManager.SaveScene(activeScene);
                    Debug.Log($"[StoryHierarchySetup] Scene '{activeScene.name}' saved with Story hierarchy in ScreenSpaceCamera mode!");
                }
            }
        }
    }
}
