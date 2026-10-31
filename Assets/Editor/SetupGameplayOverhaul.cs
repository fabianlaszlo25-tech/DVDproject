using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

[InitializeOnLoad]
public static class SetupGameplayOverhaul
{
    private const string ScenePath = "Assets/Scenes/01Day.unity";

    static SetupGameplayOverhaul()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SceneManager.GetActiveScene().path != ScenePath) return;

            GameObject interactablesRoot = GameObject.Find("interactables");
            Transform remoteRoot = interactablesRoot != null ? interactablesRoot.transform.Find("remote") : null;
            if (remoteRoot != null && remoteRoot.Find("RemoteControlTV_Tier8") == null)
            {
                RunSetup();
            }
        };
    }

    [MenuItem("Tools/Setup Gameplay Overhaul")]
    [MenuItem("Tools/DVD Project/Apply Gameplay Overhaul Setup")]
    public static void RunSetup()
    {
        Debug.Log(">>> Starting Gameplay & Visual Overhaul Setup (Systems 1-6 + Refinements)...");

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath);
        }

        // 1. Ensure TV FBX & DVDLogo FBX meshes are readable for planar UV generation & directional vertex squash/stretch
        EnsureMeshReadable("Assets/3DAssets/reTVfbx.fbx");
        EnsureMeshReadable("Assets/3DAssets/DVDLogo.fbx");

        // 2. Load Audio Clips
        AudioClip clipOpen = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/open.mp3");
        AudioClip clipClose = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/close.mp3");
        AudioClip clipBuy = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/buy.mp3");
        AudioClip clipPoof = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/poof.mp3");
        AudioClip clipStatic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/static.mp3");
        AudioClip clipHit2 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/2.mp3");
        AudioClip clipHit5 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/5.mp3");
        AudioClip clipHit6 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/6.mp3");
        AudioClip clipHit7 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/7.mp3");
        AudioClip clipHit9 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/9.mp3");

        // 3. Find Core Scene Objects
        DVDLogoBouncer bouncer = Object.FindObjectOfType<DVDLogoBouncer>();
        UpgradeManager upgradeManager = Object.FindObjectOfType<UpgradeManager>();
        GridCrawlerController playerController = Object.FindObjectOfType<GridCrawlerController>();
        SmoothCameraFollow smoothCamera = Object.FindObjectOfType<SmoothCameraFollow>();

        if (bouncer == null || upgradeManager == null)
        {
            Debug.LogError("SetupGameplayOverhaul: Could not find DVDLogoBouncer or UpgradeManager in 01Day!");
            return;
        }

        // Configure bouncer audio clips & subtle, fluid directional squash/stretch defaults
        // (Preserves the user's BoxCollider, manualOffset, and startingMoney on DVDLogo!)
        if (bouncer.directHitSound == null) bouncer.directHitSound = clipHit5 != null ? clipHit5 : bouncer.edgeHitSound;
        if (bouncer.recombinePopSound == null) bouncer.recombinePopSound = clipHit7 != null ? clipHit7 : bouncer.cornerHitSound;
        bouncer.squashStretchDuration = 0.55f;
        bouncer.maxSquashAmount = 0.24f;
        bouncer.squashOscillations = 2.0f;
        bouncer.squashDamping = 2.6f;
        bouncer.wallBounceSquashMultiplier = 0.78f;
        EditorUtility.SetDirty(bouncer);

        Transform dvdSetup = upgradeManager.transform;
        Transform tvCasing = dvdSetup.Find("Televisor_80");
        Transform tvScreen = tvCasing != null ? tvCasing.Find("screen") : null;
        MeshRenderer tvScreenRenderer = tvScreen != null ? tvScreen.GetComponent<MeshRenderer>() : null;

        // Clean up any old procedural static overlay if present
        if (tvCasing != null)
        {
            Transform oldStaticOverlay = tvCasing.Find("TVStaticMalfunctionOverlay");
            if (oldStaticOverlay != null)
            {
                Object.DestroyImmediate(oldStaticOverlay.gameObject);
            }
        }

        // Remove any old external 3D reticle if present so TVFocusController creates the in-screen one inside DVDSetup/Camera
        Transform oldExternalReticle = dvdSetup.Find("TVScreen3DReticle");
        if (oldExternalReticle != null)
        {
            Object.DestroyImmediate(oldExternalReticle.gameObject);
        }

        // 4. Setup All 9 Remote 3D Models & Prefabs as Clean Color Variants (No Huge Attachments!)
        GameObject interactablesRoot = GameObject.Find("interactables");
        Transform remoteRoot = interactablesRoot != null ? interactablesRoot.transform.Find("remote") : null;
        GameObject[] remoteWorldModels = new GameObject[9];
        GameObject[] remotePreviewPrefabs = new GameObject[9];

        if (remoteRoot != null)
        {
            SetupRemoteModelsAndPrefabs(remoteRoot, remoteWorldModels, remotePreviewPrefabs);
        }

        // 5. Setup Towel Interactable in the Room (System 5)
        InteractableObject towelInteractable = SetupTowelInteractable(interactablesRoot != null ? interactablesRoot.transform : null);

        // 6. Setup UpgradeManager Remote Path with 9 Tiers (System 3 & System 4)
        SetupRemotePathInUpgradeManager(upgradeManager, remoteWorldModels, remotePreviewPrefabs);

        // 7. Setup Unified Tabbed Upgrade Menu on DVDPLAYERMANAGER/Canvas (System 3)
        SetupUnifiedUpgradeMenu(upgradeManager);

        // 8. Setup TV Screen MeshCollider & TV Casing "Hit TV" Interactable (System 1 & System 6)
        Collider screenCollider = null;
        if (tvScreen != null)
        {
            MeshCollider mc = tvScreen.GetComponent<MeshCollider>();
            if (mc == null) mc = tvScreen.gameObject.AddComponent<MeshCollider>();
            MeshFilter mf = tvScreen.GetComponent<MeshFilter>();
            if (mf != null) mc.sharedMesh = mf.sharedMesh;
            mc.enabled = false; // Enabled dynamically when in TV Focus Mode
            screenCollider = mc;
            EditorUtility.SetDirty(tvScreen.gameObject);
        }

        InteractableObject tvCasingInteractable = null;
        if (tvCasing != null)
        {
            BoxCollider tvBox = tvCasing.GetComponent<BoxCollider>();
            if (tvBox == null) tvBox = tvCasing.gameObject.AddComponent<BoxCollider>();
            MeshFilter tvMf = tvCasing.GetComponent<MeshFilter>();
            if (tvMf != null && tvMf.sharedMesh != null)
            {
                Bounds localB = tvMf.sharedMesh.bounds;
                tvBox.center = localB.center;
                tvBox.size = localB.size;
            }

            tvCasingInteractable = tvCasing.GetComponent<InteractableObject>();
            if (tvCasingInteractable == null) tvCasingInteractable = tvCasing.gameObject.AddComponent<InteractableObject>();
            tvCasingInteractable.hoverText = "Hit TV";
            tvCasingInteractable.hideHoverText = false;
            EditorUtility.SetDirty(tvCasing.gameObject);
        }

        // 9. Attach & Wire Modular Controllers on DVDSetup (TVFocusController, TVScreenCleaner, TVScreenHazard)
        AudioSource sharedAudio = bouncer.audioSource;
        if (sharedAudio == null)
        {
            sharedAudio = dvdSetup.GetComponent<AudioSource>();
            if (sharedAudio == null) sharedAudio = dvdSetup.gameObject.AddComponent<AudioSource>();
        }

        // TVScreenCleaner (System 5: High-Res 512x512 Dust on Duplicate Screen Quad)
        TVScreenCleaner cleaner = dvdSetup.GetComponent<TVScreenCleaner>();
        if (cleaner == null) cleaner = dvdSetup.gameObject.AddComponent<TVScreenCleaner>();
        cleaner.tvScreenRenderer = tvScreenRenderer;
        cleaner.dustTextureResolution = 512;
        cleaner.overlayFrontOffset = 0.0012f;
        cleaner.wipeAudioSource = sharedAudio;
        if (cleaner.wipeRubSound == null) cleaner.wipeRubSound = clipPoof != null ? clipPoof : clipOpen;
        if (cleaner.cleanChimeSound == null) cleaner.cleanChimeSound = clipBuy != null ? clipBuy : clipHit9;
        EditorUtility.SetDirty(cleaner);

        // TVScreenHazard (System 6: Uses existing static.mp4 on DVDSetup/Camera/bgvideo + Centered Slap Squish)
        TVScreenHazard hazard = dvdSetup.GetComponent<TVScreenHazard>();
        if (hazard == null) hazard = dvdSetup.gameObject.AddComponent<TVScreenHazard>();
        hazard.tvCasingTransform = tvCasing;
        hazard.tvScreenRenderer = tvScreenRenderer;
        hazard.bouncer = bouncer;
        hazard.tvCasingInteractable = tvCasingInteractable;
        hazard.centerSquishOnScreen = true;
        hazard.staticVideoFileName = "static.mp4";
        hazard.staticVideoFullAlpha = 1.0f;
        hazard.slapAudioSource = sharedAudio;
        if (hazard.slapImpactSound == null) hazard.slapImpactSound = clipHit6 != null ? clipHit6 : clipPoof;
        if (hazard.staticNoiseSound == null) hazard.staticNoiseSound = clipStatic;

        Transform bgVideoTr = dvdSetup.Find("Camera/bgvideo");
        if (bgVideoTr != null)
        {
            hazard.bgVideoPlayer = bgVideoTr.GetComponent<VideoPlayer>();
            hazard.bgVideoRenderer = bgVideoTr.GetComponent<Renderer>();
        }

        // Create a dedicated looping AudioSource for static noise so it doesn't cut off one-shot SFX
        Transform staticAudioChild = dvdSetup.Find("StaticNoiseAudio");
        if (staticAudioChild == null)
        {
            GameObject saObj = new GameObject("StaticNoiseAudio");
            saObj.transform.SetParent(dvdSetup, false);
            if (tvScreen != null) saObj.transform.position = tvScreen.position;
            staticAudioChild = saObj.transform;
        }
        AudioSource staticLoopSrc = staticAudioChild.GetComponent<AudioSource>();
        if (staticLoopSrc == null) staticLoopSrc = staticAudioChild.gameObject.AddComponent<AudioSource>();
        staticLoopSrc.playOnAwake = false;
        staticLoopSrc.loop = true;
        staticLoopSrc.spatialBlend = 0.5f;
        hazard.staticLoopAudioSource = staticLoopSrc;

        if (tvCasingInteractable != null)
        {
            tvCasingInteractable.onClick = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddPersistentListener(tvCasingInteractable.onClick, hazard.SlapTV);
            EditorUtility.SetDirty(tvCasingInteractable);
        }
        EditorUtility.SetDirty(hazard);

        // TVFocusController (System 1, System 2, System 4, System 5)
        TVFocusController focusCtrl = dvdSetup.GetComponent<TVFocusController>();
        if (focusCtrl == null) focusCtrl = dvdSetup.gameObject.AddComponent<TVFocusController>();
        focusCtrl.playerController = playerController;
        focusCtrl.smoothCamera = smoothCamera;
        focusCtrl.bouncer = bouncer;
        focusCtrl.upgradeManager = upgradeManager;
        focusCtrl.screenCleaner = cleaner;
        focusCtrl.tvScreenRenderer = tvScreenRenderer;
        focusCtrl.tvScreenCollider = screenCollider;
        focusCtrl.screenReticleTransform = null; // Let it auto-create inside DVDSetup/Camera (in-screen)
        focusCtrl.towelHoverRotationOffset = Vector3.zero; // Rotated 90 degrees upright against the TV glass
        focusCtrl.focusAudioSource = sharedAudio;
        if (focusCtrl.pickUpSound == null) focusCtrl.pickUpSound = clipOpen;
        if (focusCtrl.putDownSound == null) focusCtrl.putDownSound = clipClose;
        if (focusCtrl.bumperSpawnSound == null) focusCtrl.bumperSpawnSound = clipHit2 != null ? clipHit2 : clipOpen;
        if (focusCtrl.bumperPingSound == null) focusCtrl.bumperPingSound = clipHit9 != null ? clipHit9 : clipBuy;
        if (focusCtrl.grabStartSound == null) focusCtrl.grabStartSound = clipOpen;
        if (focusCtrl.grabShakeTickSound == null) focusCtrl.grabShakeTickSound = clipHit5;
        if (focusCtrl.grabReleaseSound == null) focusCtrl.grabReleaseSound = clipClose;

        // TVMinigameController (Dino Runner, One-Sided Pong, Suika Merger & Tower Stack Remotes)
        TVMinigameController minigameCtrl = dvdSetup.GetComponent<TVMinigameController>();
        if (minigameCtrl == null) minigameCtrl = dvdSetup.gameObject.AddComponent<TVMinigameController>();
        minigameCtrl.mainBouncer = bouncer;
        minigameCtrl.orthoCamera = bouncer.renderCamera;
        minigameCtrl.audioSource = sharedAudio;
        minigameCtrl.dinoJumpSquashIntensity = 0.12f;
        minigameCtrl.dinoLandSquashIntensity = 0.10f;
        minigameCtrl.dinoDamageSquashIntensity = 0.24f;
        if (minigameCtrl.jumpSound == null) minigameCtrl.jumpSound = clipHit2 != null ? clipHit2 : bouncer.edgeHitSound;
        if (minigameCtrl.enemyPassSound == null) minigameCtrl.enemyPassSound = clipHit9 != null ? clipHit9 : bouncer.cornerHitSound;
        if (minigameCtrl.damageHitSound == null) minigameCtrl.damageHitSound = clipHit6 != null ? clipHit6 : bouncer.directHitSound;
        if (minigameCtrl.pongPaddleHitSound == null) minigameCtrl.pongPaddleHitSound = clipHit7 != null ? clipHit7 : bouncer.cornerHitSound;
        if (minigameCtrl.suikaMergeSound == null) minigameCtrl.suikaMergeSound = clipHit7 != null ? clipHit7 : bouncer.recombinePopSound;
        if (minigameCtrl.towerLandSound == null) minigameCtrl.towerLandSound = clipHit5 != null ? clipHit5 : bouncer.edgeHitSound;
        EditorUtility.SetDirty(minigameCtrl);

        // Normalize PauseMenu Sliders in the scene so their track & handle align cleanly
        PauseManager pauseMgr = Object.FindObjectOfType<PauseManager>(true);
        if (pauseMgr != null)
        {
            PauseManager.NormalizeSliderVisualAlignment(pauseMgr.musicVolumeSlider);
            PauseManager.NormalizeSliderVisualAlignment(pauseMgr.sfxVolumeSlider);
            PauseManager.NormalizeSliderVisualAlignment(pauseMgr.sensitivitySlider);
            if (pauseMgr.musicVolumeSlider != null) EditorUtility.SetDirty(pauseMgr.musicVolumeSlider.gameObject);
            if (pauseMgr.sfxVolumeSlider != null) EditorUtility.SetDirty(pauseMgr.sfxVolumeSlider.gameObject);
            if (pauseMgr.sensitivitySlider != null) EditorUtility.SetDirty(pauseMgr.sensitivitySlider.gameObject);
        }

        if (remoteRoot != null)
        {
            InteractableObject remIo = remoteRoot.GetComponent<InteractableObject>();
            if (remIo == null) remIo = remoteRoot.gameObject.AddComponent<InteractableObject>();
            remIo.hoverText = "Pick Up Remote";
            remIo.hideHoverText = false;
            remIo.onClick = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddPersistentListener(remIo.onClick, focusCtrl.PickUpRemote);
            focusCtrl.remoteInteractable = remIo;
            EditorUtility.SetDirty(remIo);
        }

        if (towelInteractable != null)
        {
            towelInteractable.hoverText = "Pick Up Towel";
            towelInteractable.hideHoverText = false;
            towelInteractable.onClick = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddPersistentListener(towelInteractable.onClick, focusCtrl.PickUpTowel);
            focusCtrl.towelInteractable = towelInteractable;
            EditorUtility.SetDirty(towelInteractable);
        }

        EditorUtility.SetDirty(focusCtrl);

        // Save Scene and Assets
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(">>> Gameplay & Visual Overhaul Setup (Systems 1-6 + All 5 Remotes & Refinements) COMPLETED SUCCESSFULLY!");
    }

    private static void EnsureMeshReadable(string fbxPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
    }

    private static void SetupRemoteModelsAndPrefabs(Transform remoteRoot, GameObject[] worldModels, GameObject[] previewPrefabs)
    {
        Material baseRemoteMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/3DAssets/remote/remote.mat");
        if (baseRemoteMat == null)
        {
            baseRemoteMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }

        Color[] tierColors = new Color[]
        {
            Color.white,                                // Tier 0: Stock Clicker
            new Color(0.25f, 0.95f, 0.55f, 1f),         // Tier 1: Mitosis Splitter (Emerald Green)
            new Color(0.85f, 0.35f, 1.0f, 1f),          // Tier 2: Telekinetic Grabber (Violet-Magenta)
            new Color(0.25f, 0.85f, 1.0f, 1f),          // Tier 3: Pinball Bumper Pro (Cyan-Blue)
            new Color(1.0f, 0.55f, 0.18f, 1f),          // Tier 4: Dino Jump Remote (Sunset Orange)
            new Color(1.0f, 0.25f, 0.52f, 1f),          // Tier 5: Pong Paddle Remote (Neon Coral-Pink)
            new Color(0.95f, 0.22f, 0.28f, 1f),         // Tier 6: Suika Merger Remote (Watermelon Red)
            new Color(0.38f, 0.95f, 0.22f, 1f),         // Tier 7: Tower Stack Remote (Electric Lime)
            new Color(1.0f, 0.82f, 0.22f, 1f)           // Tier 8: Omni-Remote 9000 (Golden Ultimate)
        };

        Color[] emissionColors = new Color[]
        {
            Color.black,
            new Color(0.05f, 0.35f, 0.15f, 1f),
            new Color(0.35f, 0.08f, 0.48f, 1f),
            new Color(0.08f, 0.35f, 0.52f, 1f),
            new Color(0.45f, 0.20f, 0.04f, 1f),
            new Color(0.45f, 0.06f, 0.20f, 1f),
            new Color(0.45f, 0.05f, 0.08f, 1f),
            new Color(0.12f, 0.42f, 0.05f, 1f),
            new Color(0.55f, 0.38f, 0.06f, 1f)
        };

        Material[] tierMats = new Material[9];
        for (int i = 0; i < 9; i++)
        {
            string matPath = $"Assets/3DAssets/remote/Remote_Tier{i}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null)
            {
                m = new Material(baseRemoteMat);
                AssetDatabase.CreateAsset(m, matPath);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tierColors[i]);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tierColors[i]);
            if (i > 0)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emissionColors[i]);
            }
            EditorUtility.SetDirty(m);
            tierMats[i] = m;
        }

        Transform baseModelChild = remoteRoot.Find("RemoteControlTV");
        if (baseModelChild != null)
        {
            Transform oldBaseAttach = baseModelChild.Find("TierAttachment");
            if (oldBaseAttach != null) Object.DestroyImmediate(oldBaseAttach.gameObject);
        }
        GameObject remoteFbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DAssets/remote/RemoteControlTV.fbx");

        for (int i = 0; i < 9; i++)
        {
            string childName = i == 0 ? "RemoteControlTV" : $"RemoteControlTV_Tier{i}";
            Transform t = remoteRoot.Find(childName);
            GameObject modelObj;
            if (t == null)
            {
                if (i == 0 && baseModelChild != null)
                {
                    modelObj = baseModelChild.gameObject;
                }
                else if (baseModelChild != null)
                {
                    modelObj = Object.Instantiate(baseModelChild.gameObject, remoteRoot);
                    modelObj.name = childName;
                }
                else
                {
                    modelObj = (GameObject)PrefabUtility.InstantiatePrefab(remoteFbxPrefab, remoteRoot);
                    modelObj.name = childName;
                }
            }
            else
            {
                modelObj = t.gameObject;
            }

            // Remove any bulky primitive TierAttachment from previous iteration so each remote is a clean color prefab variant!
            Transform oldAttachment = modelObj.transform.Find("TierAttachment");
            if (oldAttachment != null)
            {
                Object.DestroyImmediate(oldAttachment.gameObject);
            }

            Renderer[] rends = modelObj.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                r.sharedMaterial = tierMats[i];
            }

            modelObj.SetActive(i == 0);
            worldModels[i] = modelObj;

            string prefabPath = $"Assets/Prefabs/remote_tier{i}.prefab";
            bool wasActive = modelObj.activeSelf;
            modelObj.SetActive(true);
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(modelObj, prefabPath);
            modelObj.SetActive(wasActive);
            previewPrefabs[i] = savedPrefab;
        }
    }

    private static InteractableObject SetupTowelInteractable(Transform interactablesParent)
    {
        Transform existingTowel = interactablesParent != null ? interactablesParent.Find("towel") : null;
        GameObject towelObj;

        if (existingTowel != null)
        {
            towelObj = existingTowel.gameObject;
        }
        else
        {
            towelObj = new GameObject("towel");
            if (interactablesParent != null)
            {
                towelObj.transform.SetParent(interactablesParent, false);
            }
            towelObj.transform.position = new Vector3(0.55f, 1.072f, -3.45f);
            towelObj.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
            towelObj.transform.localScale = new Vector3(1.15f, 1.15f, 1.15f);
        }

        Transform visualChild = towelObj.transform.Find("TowelVisual");
        if (visualChild == null)
        {
            GameObject sourceTowel = GameObject.Find("Props/Curtain_Towels_Towel_B_11/SM_Curtain_Towels_Towel_B_LOD0");
            if (sourceTowel != null)
            {
                GameObject copy = Object.Instantiate(sourceTowel, towelObj.transform);
                copy.name = "TowelVisual";
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;
            }
            else
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fallback.name = "TowelVisual";
                Collider fc = fallback.GetComponent<Collider>();
                if (fc != null) Object.DestroyImmediate(fc);
                fallback.transform.SetParent(towelObj.transform, false);
                fallback.transform.localScale = new Vector3(0.24f, 0.04f, 0.18f);
            }
        }

        Transform[] towelTransforms = towelObj.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < towelTransforms.Length; i++)
        {
            if (towelTransforms[i] != null)
            {
                towelTransforms[i].gameObject.isStatic = false;
                GameObjectUtility.SetStaticEditorFlags(towelTransforms[i].gameObject, 0);
            }
        }

        BoxCollider box = towelObj.GetComponent<BoxCollider>();
        if (box == null) box = towelObj.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.03f, 0f);
        box.size = new Vector3(0.32f, 0.12f, 0.28f);

        InteractableObject io = towelObj.GetComponent<InteractableObject>();
        if (io == null) io = towelObj.AddComponent<InteractableObject>();
        io.hoverText = "Pick Up Towel";
        io.hideHoverText = false;

        EditorUtility.SetDirty(towelObj);
        return io;
    }

    private static void SetupRemotePathInUpgradeManager(
        UpgradeManager upgradeManager,
        GameObject[] remoteWorldModels,
        GameObject[] remotePreviewPrefabs)
    {
        upgradeManager.EnsureRemoteUpgradePath();
        UpgradePath remotePath = upgradeManager.GetPathByCategory(UpgradeCategory.RemoteUpgrade);
        if (remotePath == null) return;

        string[] tierNames = new string[]
        {
            "Stock Clicker",
            "Mitosis Splitter",
            "Telekinetic Grabber",
            "Pinball Bumper Pro",
            "Dino Jump Remote",
            "Pong Paddle Remote",
            "Suika Merger Remote",
            "Tower Stack Remote",
            "Omni-Remote 9000"
        };
        float[] costs = new float[] { 0f, 85f, 190f, 320f, 420f, 520f, 640f, 760f, 950f };
        float[] mults = new float[] { 1f, 2f, 2.5f, 3f, 3.5f, 4f, 4.5f, 5f, 6f };
        string[] descriptions = new string[]
        {
            "Direct Impulse Hit + Bonus Cash",
            "Splits Logo into 2x Children + Recombines",
            "Hold Logo: Drag Into Walls & Corners to Bounce!",
            "Click Empty Screen: Spawn up to 5 Bumpers",
            "Hold Remote: No-Internet Dino & Bird Jumper!",
            "Hold Remote: 1-Sided Pong Bar Defender!",
            "Hold Remote: Suika Logo Drop & Exponential Merger!",
            "Hold Remote: Deformed Logo Tower Stacker!",
            "ULTIMATE: Direct Hit + Mitosis + Bumpers!"
        };

        while (remotePath.tiers.Count < 9) remotePath.tiers.Add(new UpgradeTier());
        while (remotePath.tiers.Count > 9) remotePath.tiers.RemoveAt(remotePath.tiers.Count - 1);

        for (int i = 0; i < 9; i++)
        {
            UpgradeTier t = remotePath.tiers[i];
            t.tierName = tierNames[i];
            t.cost = costs[i];
            t.multiplierValue = mults[i];
            t.customDescription = descriptions[i];
            t.previewPrefab = remotePreviewPrefabs[i];
            t.previewPositionOffset = Vector3.zero;
            t.previewScale = new Vector3(380f, 380f, 380f);
            t.previewRotation = new Vector3(-35f, 145f, -20f);
            t.worldGameObject = remoteWorldModels[i];
            if (remoteWorldModels[i] != null)
            {
                t.initialWorldScale = remoteWorldModels[i].transform.localScale;
            }
        }

        EditorUtility.SetDirty(upgradeManager);
    }

    private static void SetupUnifiedUpgradeMenu(UpgradeManager upgradeManager)
    {
        GameObject dvdPileTriggerObj = GameObject.Find("dvds");
        if (dvdPileTriggerObj != null)
        {
            InteractableObject io = dvdPileTriggerObj.GetComponent<InteractableObject>();
            if (io != null) io.enabled = false;
            Collider col = dvdPileTriggerObj.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            UpgradeTrigger ut = dvdPileTriggerObj.GetComponent<UpgradeTrigger>();
            if (ut != null) ut.enabled = false;
            EditorUtility.SetDirty(dvdPileTriggerObj);
        }

        GameObject dvdPileManagerObj = GameObject.Find("DVDPILEMANAGER");
        if (dvdPileManagerObj != null)
        {
            Transform pileCanvas = dvdPileManagerObj.transform.Find("Canvas");
            if (pileCanvas != null)
            {
                pileCanvas.gameObject.SetActive(false);
                EditorUtility.SetDirty(pileCanvas.gameObject);
            }
        }

        GameObject dvdPlayersTriggerObj = GameObject.Find("dvdplayers");
        if (dvdPlayersTriggerObj != null)
        {
            InteractableObject io = dvdPlayersTriggerObj.GetComponent<InteractableObject>();
            if (io != null)
            {
                io.hoverText = "Upgrade Station (DVD / Player / Remote)";
                EditorUtility.SetDirty(io);
            }
        }

        GameObject playerManagerObj = GameObject.Find("DVDPLAYERMANAGER");
        if (playerManagerObj == null) return;

        Transform canvasTransform = playerManagerObj.transform.Find("Canvas");
        if (canvasTransform == null) return;

        WorldSpaceUpgradeMenu menu = canvasTransform.GetComponent<WorldSpaceUpgradeMenu>();
        if (menu == null) return;

        menu.EnsureUnifiedTabsExist();

        if (menu.dvdTabInteractable != null)
        {
            menu.dvdTabInteractable.onClick = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddPersistentListener(menu.dvdTabInteractable.onClick, menu.SelectDvdTab);
            EditorUtility.SetDirty(menu.dvdTabInteractable);
        }
        if (menu.playerTabInteractable != null)
        {
            menu.playerTabInteractable.onClick = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddPersistentListener(menu.playerTabInteractable.onClick, menu.SelectPlayerTab);
            EditorUtility.SetDirty(menu.playerTabInteractable);
        }
        if (menu.remoteTabInteractable != null)
        {
            menu.remoteTabInteractable.onClick = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddPersistentListener(menu.remoteTabInteractable.onClick, menu.SelectRemoteTab);
            EditorUtility.SetDirty(menu.remoteTabInteractable);
        }

        EditorUtility.SetDirty(menu);
        EditorUtility.SetDirty(canvasTransform.gameObject);
    }
}
