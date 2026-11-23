using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;
using Story;

/// <summary>
/// Self-contained interactive tutorial sequence manager on the 'tutorialManager' GameObject.
/// - If 'tutorialManager' GameObject is set active false (either in the scene hierarchy or via the Main Menu choice),
///   the tutorial does not play at all and the game drops straight into normal gameplay.
/// - If active, guides the player through:
///   1. Watching the News video -> Static -> Unpaid Bills Disclaimer video -> Static on the TV (all bars, money, and inflation UI hidden and inactive).
///   2. Phone starts ringing + Controls UI popup with "I UNDERSTAND" button (player movement locked until dismissed).
///   3. Answering the Boss phone call (both swipe sides are ACCEPT) -> Severance transferred -> Money UI activates with $2,000.
///   4. Tip to pay the random starting bill on the wall beside the front door.
///   5. Tip to Eat (Hunger bar activates & works).
///   6. Tip to Use the Toilet (Needs bar activates & works).
///   7. Tip to Shower (Dirtiness bar activates & works).
///   8. Tip to Sleep (Tiredness/Sleep bar activates; sleeping performs an in-room tutorial sleep transition without the Dream/Sleep Stats canvas).
///   9. Waking up (Depression bar activates) -> Unknown Number phone call (both swipe sides are ACCEPT) offering a DVD Player & Disc for $1,000.
///  10. Upgrade Store pops up with only the two starter items (Starter DVD Disc $500 + Starter DVD Player $500 = $1,000 total).
///  11. After buying both starter items and closing the store, tip to go to the TV and pick up the Remote, click the logo, and put the remote down.
///  12. Final prompt to look at the DVD Player on the desk and check out what else can be bought -> Tutorial Complete!
/// </summary>
[DefaultExecutionOrder(-100)]
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    /// <summary>True when the tutorial manager GameObject is active and the tutorial sequence is currently running.</summary>
    public static bool IsTutorialActive => Instance != null && Instance.gameObject.activeInHierarchy && Instance.enabled && !Instance.isTutorialFinished;

    /// <summary>True while the tutorial is on the Sleep step (so sleeping does not load the Dream/Stats scene).</summary>
    public static bool IsTutorialSleepStepActive => IsTutorialActive && Instance.currentStage == TutorialStage.Sleep;

    /// <summary>True while the tutorial starter store is open (restricting the shop to the first two starter items costing $1,000 combined).</summary>
    public static bool IsTutorialStarterShopActive => IsTutorialActive && !Instance.hasCompletedStarterShop;

    // Cross-scene Main Menu choice tracking
    public static bool HasExplicitMenuChoice { get; private set; } = false;
    public static bool MenuChoicePlayTutorial { get; private set; } = true;
    public static bool TutorialCompletedInCurrentRun { get; set; } = false;

    public static void SetMenuTutorialChoice(bool playTutorial)
    {
        HasExplicitMenuChoice = true;
        MenuChoicePlayTutorial = playTutorial;
        TutorialCompletedInCurrentRun = !playTutorial;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoadHook()
    {
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
    }

    private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "01Day", StringComparison.OrdinalIgnoreCase)) return;

        TutorialManager[] managers = Resources.FindObjectsOfTypeAll<TutorialManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            TutorialManager tm = managers[i];
            if (tm == null || tm.gameObject.scene.name != scene.name) continue;

            if (TutorialCompletedInCurrentRun)
            {
                if (tm.gameObject.activeSelf) tm.gameObject.SetActive(false);
            }
            else if (HasExplicitMenuChoice)
            {
                if (tm.gameObject.activeSelf != MenuChoicePlayTutorial)
                {
                    tm.gameObject.SetActive(MenuChoicePlayTutorial);
                }
            }
        }
    }

    public enum TutorialStage
    {
        None = 0,
        OpeningVideos = 1,
        ControlsPopup = 2,
        WaitForBossCall = 3,
        PayBill = 4,
        EatFood = 5,
        UseToilet = 6,
        TakeShower = 7,
        Sleep = 8,
        SleepingTransition = 9,
        WaitForUnknownCall = 10,
        StarterShopOpen = 11,
        PickUpAndUseRemote = 12,
        FinalPromptCheckPlayer = 13,
        Completed = 14
    }

    [Header("Current Tutorial Progress (Read-Only)")]
    public TutorialStage currentStage = TutorialStage.None;
    public bool isTutorialFinished = false;

    [Header("1. Opening TV Video Sequence Slots")]
    [Tooltip("Slot 1: The News video clip that plays on the TV when the tutorial launches.")]
    public VideoClip newsVideoClip;
    [Tooltip("Optional StreamingAssets file name if News video is stored in StreamingAssets (e.g. 'news.mp4').")]
    public string newsVideoFileName = "";
    [Tooltip("Optional separate audio clip for the News video.")]
    public AudioClip newsAudioClip;
    [Tooltip("Fallback duration (seconds) for the News segment if no video clip is assigned yet.")]
    public float newsFallbackDuration = 4.5f;

    [Tooltip("Duration (seconds) of the TV static transition between the News video and the Unpaid Bills Disclaimer video.")]
    public float staticBetweenVideosDuration = 1.8f;

    [Tooltip("Slot 2: The Unpaid Bills Disclaimer video clip that plays after the first static burst.")]
    public VideoClip disclaimerVideoClip;
    [Tooltip("Optional StreamingAssets file name if Disclaimer video is stored in StreamingAssets (e.g. 'disclaimer.mp4').")]
    public string disclaimerVideoFileName = "";
    [Tooltip("Optional separate audio clip for the Disclaimer video.")]
    public AudioClip disclaimerAudioClip;
    [Tooltip("Fallback duration (seconds) for the Disclaimer segment if no video clip is assigned yet.")]
    public float disclaimerFallbackDuration = 4.5f;

    [Tooltip("Optional static VideoClip override (defaults to 'static.mp4' in StreamingAssets).")]
    public VideoClip staticVideoClip;
    [Tooltip("StreamingAssets file name for the static loop video.")]
    public string staticVideoFileName = "static.mp4";
    [Tooltip("Looping static noise audio clip played while static is on the TV.")]
    public AudioClip staticNoiseClip;

    [Header("2. Tutorial Economy & Starter Kit Costs")]
    [Tooltip("Starting money transferred by the Boss during the first call.")]
    public float bossSeveranceMoney = 2000f;
    [Tooltip("Cost of the starter DVD Disc in the tutorial upgrade store ($500 + $500 = $1,000 total).")]
    public float starterDvdDiscCost = 500f;
    [Tooltip("Cost of the starter DVD Player in the tutorial upgrade store ($500 + $500 = $1,000 total).")]
    public float starterDvdPlayerCost = 500f;

    [Header("3. Tutorial Phone Call Cards (Both Sides Accept)")]
    public StoryCardData bossCallCard = new StoryCardData
    {
        id = "tutorial_boss_call",
        contactName = "YOUR BOSS",
        contactRole = "Severance Notice",
        narrativeText = "I'm really sorry I have to do this. I know how bad the accident was, but we have to let you go and stop paying sick leave—because it's not really a leave anymore. I transferred $2,000 in severance to your bank account. Take care of yourself.",
        leftLabel = "ACCEPT",
        leftChoicePrompt = "I understand, boss...",
        leftOutcomeText = "He hangs up quietly. Your severance of $2,000 has arrived in your bank account.",
        leftDepressionDelta = 0f,
        leftTirednessDelta = 0f,
        leftMoneyDelta = 2000f,
        leftIsPositive = true,
        rightLabel = "ACCEPT",
        rightChoicePrompt = "Thanks for the severance...",
        rightOutcomeText = "He hangs up quietly. Your severance of $2,000 has arrived in your bank account.",
        rightDepressionDelta = 0f,
        rightTirednessDelta = 0f,
        rightMoneyDelta = 2000f,
        rightIsPositive = true
    };

    public StoryCardData unknownCallerCard = new StoryCardData
    {
        id = "tutorial_unknown_call",
        contactName = "UNKNOWN NUMBER",
        contactRole = "Restricted Caller",
        narrativeText = "Heard you got laid off. Listen closely—I'm giving you a chance to buy a special DVD Player and a DVD Disc for $1,000 total that lets you make real money every time it bounces. Don't waste this opportunity.",
        leftLabel = "ACCEPT",
        leftChoicePrompt = "Show me what you've got",
        leftOutcomeText = "The line clicks dead. A hardware catalog pops up on your screen.",
        leftDepressionDelta = 0f,
        leftTirednessDelta = 0f,
        leftMoneyDelta = 0f,
        leftIsPositive = true,
        rightLabel = "ACCEPT",
        rightChoicePrompt = "I'll take the deal",
        rightOutcomeText = "The line clicks dead. A hardware catalog pops up on your screen.",
        rightDepressionDelta = 0f,
        rightTirednessDelta = 0f,
        rightMoneyDelta = 0f,
        rightIsPositive = true
    };

    [Header("4. Scene References (Auto-Resolved if Empty)")]
    public BarManager barManager;
    public DVDLogoBouncer bouncer;
    public UpgradeManager upgradeManager;
    public WorldSpaceUpgradeMenu upgradeMenu;
    public BillManager billManager;
    public TVFocusController tvFocusController;
    public TVScreenHazard tvScreenHazard;
    public GridCrawlerController playerController;
    public SmoothCameraFollow smoothCamera;
    public VideoPlayer bgVideoPlayer;
    public Renderer bgVideoRenderer;
    public AudioSource tvAudioSource;

    [Header("5. Tutorial Typography & Opening Focus")]
    [Tooltip("Impact TMP font asset used for the subtle tutorial text overlay.")]
    public TMP_FontAsset impactFont;
    [Tooltip("Duration (seconds) of the smooth camera zoom-out from the TV to normal view when the opening broadcast stops.")]
    public float openingZoomOutDuration = 0.85f;

    // Runtime state tracking
    private bool hasCompletedStarterShop = false;
    private bool boughtStarterDisc = false;
    private bool boughtStarterPlayer = false;
    private bool hasPickedUpRemoteInTutorial = false;
    private bool hasClickedRemoteInFocus = false;
    private bool isWaitingForActionZoomOut = false;
    private SurvivalAction.ActionType pendingCompletedActionType;
    private float finalPromptTimer = 0f;
    private float starterShopAutoCloseTimer = -1f;

    // Saved TV bgvideo state
    private Vector3 savedBgVideoLocalPos;
    private bool hasSavedBgVideoPos = false;
    private int savedBgRenderQueue = -1;
    private float savedNormalBgAlpha = 0.27450982f;

    // Tutorial UI Overlay (Simple Impact Tip Text + Soft Shadow Box + Controls Popup + Virtual Cursor)
    private Canvas tutorialCanvas;
    private RectTransform tipBannerRect;
    private CanvasGroup tipBannerGroup;
    private Image tipShadowBoxImage;
    private TextMeshProUGUI tipBodyText;
    private Sprite softShadowBoxSprite;
    private GameObject cachedCursorHolderObj;

    private GameObject controlsPopupBackdrop;
    private RectTransform controlsPopupWindow;
    private Button controlsUnderstandButton;
    private Image controlsUnderstandBtnImg;
    private RectTransform tutorialVirtualCursorRect;
    private Image tutorialVirtualCursorImg;
    private Sprite cachedDefaultCursorSprite;
    private Sprite cachedInteractCursorSprite;
    private Vector2 cachedDefaultCursorSize = new Vector2(20.7f, 31.8f);
    private Vector2 cachedInteractCursorSize = new Vector2(51f, 51f);
    private TMP_FontAsset sharedFont;
    private AudioSource uiAudioSource;

    // In-TV broadcast fallback text when VideoClip slots are left empty in Inspector
    private GameObject inTvBroadcastOverlayObj;
    private TextMeshPro inTvBroadcastTitleTmp;
    private TextMeshPro inTvBroadcastBodyTmp;

    public bool AreBothStarterItemsPurchased => boughtStarterDisc && boughtStarterPlayer;

    private void OnValidate()
    {
        EnsureDefaultCallCardsConfigured();
        if (!Application.isPlaying && gameObject.scene.IsValid())
        {
            ResolveReferencesInEditor();
        }
    }

    [ContextMenu("Auto-Wire Scene References")]
    public void ResolveReferencesInEditor()
    {
        if (barManager == null) barManager = FindObjectOfType<BarManager>(true);
        if (bouncer == null) bouncer = FindObjectOfType<DVDLogoBouncer>(true);
        if (upgradeManager == null) upgradeManager = FindObjectOfType<UpgradeManager>(true);
        if (billManager == null) billManager = FindObjectOfType<BillManager>(true);
        if (tvFocusController == null) tvFocusController = FindObjectOfType<TVFocusController>(true);
        if (tvScreenHazard == null) tvScreenHazard = FindObjectOfType<TVScreenHazard>(true);
        if (playerController == null) playerController = FindObjectOfType<GridCrawlerController>(true);
        if (smoothCamera == null) smoothCamera = FindObjectOfType<SmoothCameraFollow>(true);

        if (upgradeMenu == null)
        {
            WorldSpaceUpgradeMenu[] menus = FindObjectsOfType<WorldSpaceUpgradeMenu>(true);
            for (int i = 0; i < menus.Length; i++)
            {
                if (menus[i] != null && menus[i].isPlayerMenu)
                {
                    upgradeMenu = menus[i];
                    break;
                }
            }
            if (upgradeMenu == null && menus.Length > 0) upgradeMenu = menus[0];
        }

        if (tvScreenHazard != null)
        {
            if (bgVideoPlayer == null) bgVideoPlayer = tvScreenHazard.bgVideoPlayer;
            if (bgVideoRenderer == null) bgVideoRenderer = tvScreenHazard.bgVideoRenderer;
            if (staticNoiseClip == null) staticNoiseClip = tvScreenHazard.staticNoiseSound;
            if (tvAudioSource == null) tvAudioSource = tvScreenHazard.staticLoopAudioSource;
        }

#if UNITY_EDITOR
        if (impactFont == null)
        {
            impactFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Scripts/DVD BOUNCER/IMPACT SDF.asset");
        }
#endif
    }

    private void Awake()
    {
        // Check if tutorial should be disabled based on Main Menu choice or already completed in this run
        if (TutorialCompletedInCurrentRun || (HasExplicitMenuChoice && !MenuChoicePlayTutorial))
        {
            gameObject.SetActive(false);
            return;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        ResolveReferences();
        EnsureDefaultCallCardsConfigured();
    }

    private void OnEnable()
    {
        if (Instance == null) Instance = this;
    }

    private void OnDisable()
    {
        UnhookEvents();
        if (smoothCamera != null)
        {
            smoothCamera.lockCameraLook = false;
        }
        if (currentStage == TutorialStage.OpeningVideos || currentStage == TutorialStage.ControlsPopup)
        {
            GridCrawlerController.InputFrozen = false;
            SetOpeningCursorAndHudVisible(true);
        }
        if (tutorialCanvas != null)
        {
            tutorialCanvas.gameObject.SetActive(false);
        }
        if (inTvBroadcastOverlayObj != null)
        {
            inTvBroadcastOverlayObj.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        UnhookEvents();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (!gameObject.activeInHierarchy || isTutorialFinished) return;

        ResolveReferences();
        HookEvents();
        BuildTutorialUI();

        // Initialize tutorial Day 1 starting state:
        // - All 5 survival bars hidden AND functionally inactive
        // - Money and Inflation UI hidden
        // - No DVD player or disc owned yet; DVD logo hidden and paused
        SetupInitialTutorialState();

        // Start opening sequence: News video -> Static -> Unpaid Bills Disclaimer video -> Static -> Phone Ring + Controls UI
        StartCoroutine(OpeningSequenceRoutine());
    }

    private void ResolveReferences()
    {
        if (barManager == null) barManager = FindObjectOfType<BarManager>(true);
        if (bouncer == null) bouncer = FindObjectOfType<DVDLogoBouncer>(true);
        if (upgradeManager == null) upgradeManager = FindObjectOfType<UpgradeManager>(true);
        if (billManager == null) billManager = FindObjectOfType<BillManager>(true);
        if (tvFocusController == null) tvFocusController = FindObjectOfType<TVFocusController>(true);
        if (tvScreenHazard == null) tvScreenHazard = FindObjectOfType<TVScreenHazard>(true);
        if (playerController == null) playerController = FindObjectOfType<GridCrawlerController>(true);
        if (smoothCamera == null) smoothCamera = FindObjectOfType<SmoothCameraFollow>(true);

        if (upgradeMenu == null)
        {
            WorldSpaceUpgradeMenu[] menus = FindObjectsOfType<WorldSpaceUpgradeMenu>(true);
            for (int i = 0; i < menus.Length; i++)
            {
                if (menus[i] != null && menus[i].isPlayerMenu)
                {
                    upgradeMenu = menus[i];
                    break;
                }
            }
            if (upgradeMenu == null && menus.Length > 0) upgradeMenu = menus[0];
        }

        if (bgVideoPlayer == null)
        {
            if (tvScreenHazard != null && tvScreenHazard.bgVideoPlayer != null)
            {
                bgVideoPlayer = tvScreenHazard.bgVideoPlayer;
                bgVideoRenderer = tvScreenHazard.bgVideoRenderer;
            }
            else
            {
                GameObject bgObj = GameObject.Find("DVDSetup/Camera/bgvideo");
                if (bgObj != null)
                {
                    bgVideoPlayer = bgObj.GetComponent<VideoPlayer>();
                    bgVideoRenderer = bgObj.GetComponent<Renderer>();
                }
            }
        }
        if (bgVideoRenderer == null && bgVideoPlayer != null)
        {
            bgVideoRenderer = bgVideoPlayer.GetComponent<Renderer>();
        }

        if (tvAudioSource == null)
        {
            GameObject screenObj = GameObject.Find("DVDSetup/Televisor_80/screen");
            if (screenObj != null) tvAudioSource = screenObj.GetComponent<AudioSource>();
            if (tvAudioSource == null && tvScreenHazard != null && tvScreenHazard.staticLoopAudioSource != null)
            {
                tvAudioSource = tvScreenHazard.staticLoopAudioSource;
            }
        }

        if (staticNoiseClip == null && tvScreenHazard != null && tvScreenHazard.staticNoiseSound != null)
        {
            staticNoiseClip = tvScreenHazard.staticNoiseSound;
        }

        if (impactFont != null)
        {
            sharedFont = impactFont;
        }
        else if (sharedFont == null)
        {
            if (bouncer != null && bouncer.totalMoneyText != null && bouncer.totalMoneyText.font != null)
            {
                sharedFont = bouncer.totalMoneyText.font;
            }
            else if (barManager != null && barManager.inflationText != null && barManager.inflationText.font != null)
            {
                sharedFont = barManager.inflationText.font;
            }
        }

        if (cachedCursorHolderObj == null && playerController != null && playerController.crosshairContainer != null)
        {
            cachedCursorHolderObj = playerController.crosshairContainer.gameObject;
        }

        if (uiAudioSource == null)
        {
            uiAudioSource = GetComponent<AudioSource>();
            if (uiAudioSource == null) uiAudioSource = gameObject.AddComponent<AudioSource>();
            uiAudioSource.playOnAwake = false;
            uiAudioSource.spatialBlend = 0f;
        }
    }

    private void EnsureDefaultCallCardsConfigured()
    {
        if (bossCallCard == null || string.IsNullOrEmpty(bossCallCard.contactName))
        {
            bossCallCard = new StoryCardData();
        }
        bossCallCard.id = "tutorial_boss_call";
        bossCallCard.leftLabel = "ACCEPT";
        bossCallCard.rightLabel = "ACCEPT";
        bossCallCard.leftMoneyDelta = bossSeveranceMoney;
        bossCallCard.rightMoneyDelta = bossSeveranceMoney;
        bossCallCard.leftDepressionDelta = 0f;
        bossCallCard.rightDepressionDelta = 0f;
        bossCallCard.leftTirednessDelta = 0f;
        bossCallCard.rightTirednessDelta = 0f;

        if (unknownCallerCard == null || string.IsNullOrEmpty(unknownCallerCard.contactName))
        {
            unknownCallerCard = new StoryCardData();
        }
        unknownCallerCard.id = "tutorial_unknown_call";
        unknownCallerCard.leftLabel = "ACCEPT";
        unknownCallerCard.rightLabel = "ACCEPT";
        unknownCallerCard.leftMoneyDelta = 0f;
        unknownCallerCard.rightMoneyDelta = 0f;
        unknownCallerCard.leftDepressionDelta = 0f;
        unknownCallerCard.rightDepressionDelta = 0f;
        unknownCallerCard.leftTirednessDelta = 0f;
        unknownCallerCard.rightTirednessDelta = 0f;
    }

    private void HookEvents()
    {
        UnhookEvents();
        if (barManager != null)
        {
            barManager.OnFoodEatenEvent += HandleFoodEaten;
            barManager.OnToiletUsedEvent += HandleToiletUsed;
            barManager.OnShoweredEvent += HandleShowered;
        }
    }

    private void UnhookEvents()
    {
        if (barManager != null)
        {
            barManager.OnFoodEatenEvent -= HandleFoodEaten;
            barManager.OnToiletUsedEvent -= HandleToiletUsed;
            barManager.OnShoweredEvent -= HandleShowered;
        }
    }

    // =========================================================================
    // INITIAL TUTORIAL SETUP (ALL BARS OFF, MONEY & INFLATION HIDDEN, NO DVD)
    // =========================================================================

    private void SetupInitialTutorialState()
    {
        hasCompletedStarterShop = false;
        boughtStarterDisc = false;
        boughtStarterPlayer = false;
        hasPickedUpRemoteInTutorial = false;
        hasClickedRemoteInFocus = false;

        // 1. Deactivate all 5 survival bars (both UI and functionality) and hide Inflation & Food Cost UI
        if (barManager != null)
        {
            barManager.SetBarsActiveState(
                hunger: false,
                needs: false,
                dirtiness: false,
                sleep: false,
                depression: false);

            if (barManager.inflationText != null)
            {
                barManager.inflationText.gameObject.SetActive(false);
            }
            if (barManager.foodCostText != null)
            {
                barManager.foodCostText.gameObject.SetActive(false);
            }
        }

        // 2. Hide Money UI and start with $0 until the Boss transfers $2,000 severance
        if (bouncer != null)
        {
            bouncer.currentMoney = 0m;
            bouncer.allTimeMoneyEarned = 0m;
            bouncer.RefreshMoneyUIFromSave();
            if (bouncer.totalMoneyText != null)
            {
                bouncer.totalMoneyText.gameObject.SetActive(false);
            }

            // Pause and hide the bouncing DVD logo until the player buys the DVD player & disc on Day 2
            DVDLogoBouncer.GlobalStaticHalt = true;
            SetDvdLogoVisualsVisible(false);
        }

        // 3. Remove starter DVD Player & DVD Disc ownership until purchased in the Day 2 Starter Store
        if (upgradeManager != null)
        {
            UpgradePath discPath = upgradeManager.GetPathByCategory(UpgradeCategory.MoneyMultiplier);
            if (discPath != null)
            {
                discPath.unlockedIndices.Clear();
                discPath.equippedIndex = -1;
            }

            UpgradePath playerPath = upgradeManager.GetPathByCategory(UpgradeCategory.SpeedMultiplier);
            if (playerPath != null)
            {
                playerPath.unlockedIndices.Clear();
                playerPath.equippedIndex = -1;
                if (playerPath.tiers != null)
                {
                    for (int i = 0; i < playerPath.tiers.Count; i++)
                    {
                        if (playerPath.tiers[i] != null && playerPath.tiers[i].worldGameObject != null)
                        {
                            playerPath.tiers[i].worldGameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        // 4. Disable clicking the DVD Player UpgradeTrigger and Remote/Towel until their tutorial steps
        SetUpgradeStationInteractable(false);
        SetRemoteAndTowelInteractable(false);

        // 5. Disable random TV static hazard timer during the tutorial
        if (tvScreenHazard != null)
        {
            tvScreenHazard.enabled = false;
            if (tvScreenHazard.tvCasingInteractable != null)
            {
                tvScreenHazard.tvCasingInteractable.enabled = false;
            }
        }
    }

    private void SetDvdLogoVisualsVisible(bool visible)
    {
        if (bouncer == null) return;
        Renderer[] rends = bouncer.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            string n = rends[i].gameObject.name.ToLowerInvariant();
            if (n == "edge" || n == "corner") continue;
            rends[i].enabled = visible;
        }
    }

    private void SetUpgradeStationInteractable(bool interactable)
    {
        UpgradeTrigger[] triggers = FindObjectsOfType<UpgradeTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            if (triggers[i] == null) continue;
            if (triggers[i].gameObject.name.ToLowerInvariant().Contains("dvds") &&
                !triggers[i].gameObject.name.ToLowerInvariant().Contains("player"))
            {
                continue;
            }
            InteractableObject io = triggers[i].GetComponent<InteractableObject>();
            if (io != null) io.enabled = interactable;
            Collider col = triggers[i].GetComponent<Collider>();
            if (col != null) col.enabled = interactable;
        }
    }

    private void SetRemoteAndTowelInteractable(bool interactable)
    {
        if (tvFocusController != null)
        {
            if (tvFocusController.remoteInteractable != null)
            {
                tvFocusController.remoteInteractable.enabled = interactable;
                Collider c = tvFocusController.remoteInteractable.GetComponent<Collider>();
                if (c != null) c.enabled = interactable;
            }
            if (tvFocusController.towelInteractable != null)
            {
                tvFocusController.towelInteractable.enabled = interactable;
                Collider c = tvFocusController.towelInteractable.GetComponent<Collider>();
                if (c != null) c.enabled = interactable;
            }
        }
    }

    // =========================================================================
    // STAGE 1 & 2: OPENING TV VIDEO SEQUENCE -> ZOOM OUT -> CONTROLS POPUP
    // =========================================================================

    private void EnterOpeningTvFocusImmediate()
    {
        if (smoothCamera == null) smoothCamera = FindObjectOfType<SmoothCameraFollow>(true);
        if (smoothCamera == null) return;

        Renderer tvRend = tvFocusController != null ? tvFocusController.tvScreenRenderer : null;
        if (tvRend == null)
        {
            GameObject screenObj = GameObject.Find("DVDSetup/Televisor_80/screen");
            if (screenObj != null) tvRend = screenObj.GetComponent<Renderer>();
        }
        if (tvRend == null) return;

        Bounds b = tvRend.bounds;
        Vector3 screenCenter = b.center;
        float dist = tvFocusController != null ? Mathf.Max(0.8f, tvFocusController.focusDistanceFromScreen) : 1.45f;

        Vector3 focusCamPos = screenCenter + new Vector3(0f, 0f, -dist);
        Quaternion focusCamRot = Quaternion.LookRotation((screenCenter - focusCamPos).normalized, Vector3.up);

        float targetFill = tvFocusController != null ? tvFocusController.targetViewportFill : 0.88f;
        float activeViewportFraction = 0.85f;
        float desiredVisibleHeightAtScreen = b.size.y / (targetFill * activeViewportFraction);
        float calculatedFOV = 2f * Mathf.Atan(desiredVisibleHeightAtScreen / (2f * dist)) * Mathf.Rad2Deg;
        calculatedFOV = Mathf.Clamp(calculatedFOV, 22f, 58f);

        smoothCamera.EnterFocusModeImmediate(focusCamPos, focusCamRot, calculatedFOV);
    }

    private void SetOpeningCursorAndHudVisible(bool visible)
    {
        if (playerController != null)
        {
            playerController.SetHudCropPanelVisible(visible);
            playerController.SetFocusCursorVisuals(visible, false, "");
            if (playerController.crosshairContainer != null)
            {
                cachedCursorHolderObj = playerController.crosshairContainer.gameObject;
            }
        }
        if (cachedCursorHolderObj != null && cachedCursorHolderObj.activeSelf != visible)
        {
            cachedCursorHolderObj.SetActive(visible);
        }
        if (!visible)
        {
            if (tutorialVirtualCursorRect != null && tutorialVirtualCursorRect.gameObject.activeSelf)
            {
                tutorialVirtualCursorRect.gameObject.SetActive(false);
            }
            if (!PauseManager.IsGamePaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    private IEnumerator OpeningSequenceRoutine()
    {
        currentStage = TutorialStage.OpeningVideos;

        // Lock player movement, camera look, and hide cursor completely while watching the TV in focus
        GridCrawlerController.InputFrozen = true;
        if (smoothCamera != null)
        {
            smoothCamera.lockCameraLook = true;
        }
        SetOpeningCursorAndHudVisible(false);
        SetTutorialBannerVisible(false);

        // Launch the game already focused tightly on the TV screen
        EnterOpeningTvFocusImmediate();

        // Ensure bgvideo is at full opacity in front of the TV camera
        SetTvFullScreenVideoMode(true);

        // Wait one frame for scene & VideoPlayer initialization
        yield return null;

        // 1. Play News Video (Slot 1)
        yield return StartCoroutine(PlayTutorialVideoSegmentRoutine(
            newsVideoClip,
            newsVideoFileName,
            newsAudioClip,
            newsFallbackDuration,
            "EVENING NEWS BROADCAST",
            "\"...local warehouse pile-up leaves one worker permanently paralyzed.\nCorporate spokespeople declined to comment on long-term disability coverage...\""));

        // 2. First Static Transition
        StartTvStaticLoop(playAudio: true);
        yield return new WaitForSeconds(Mathf.Max(0.5f, staticBetweenVideosDuration));

        // 3. Play Unpaid Bills Disclaimer Video (Slot 2)
        StopTvStaticAudio();
        yield return StartCoroutine(PlayTutorialVideoSegmentRoutine(
            disclaimerVideoClip,
            disclaimerVideoFileName,
            disclaimerAudioClip,
            disclaimerFallbackDuration,
            "NOTICE: UNPAID BILLS & CABLE TERMINATION",
            "\"DUE TO UNPAID BILLS AND OVERDUE ACCOUNT BALANCES,\nYOUR TELEVISION & UTILITY SERVICES HAVE BEEN RESTRICTED.\nSETTLE DAILY NOTICES ON YOUR DOOR IMMEDIATELY.\""));

        // 4. Static plays from then on
        HideInTvBroadcastFallback();
        StartTvStaticLoop(playAudio: true);
        yield return new WaitForSeconds(0.45f);

        // Lower static audio slightly so phone ringing is prominent
        if (tvScreenHazard != null && tvScreenHazard.staticLoopAudioSource != null)
        {
            tvScreenHazard.staticLoopAudioSource.volume = 0.14f;
        }

        // Start ringing the kitchen phone as the camera pulls back to regular view
        KitchenPhone phone = KitchenPhone.EnsureInstanceInScene();
        if (phone != null)
        {
            phone.StartTutorialCall(bossCallCard, OnBossCallCompleted);
        }

        // 5. Smoothly zoom out from the TV to the regular player view (camera look & cursor still locked)
        float zoomDur = Mathf.Max(0.25f, openingZoomOutDuration);
        if (smoothCamera != null)
        {
            smoothCamera.ExitFocusMode(zoomDur);
        }
        yield return new WaitForSeconds(zoomDur + 0.08f);

        // 6. Pop up Controls UI and activate the cursor so the player can click "I UNDERSTAND"
        currentStage = TutorialStage.ControlsPopup;
        if (playerController != null)
        {
            playerController.virtualMousePos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }
        ShowControlsPopup(true);
    }

    private IEnumerator PlayTutorialVideoSegmentRoutine(
        VideoClip clipSlot,
        string fileNameSlot,
        AudioClip separateAudio,
        float fallbackDuration,
        string fallbackTitle,
        string fallbackSubtitle)
    {
        HideInTvBroadcastFallback();

        if (separateAudio != null && tvAudioSource != null)
        {
            tvAudioSource.Stop();
            tvAudioSource.clip = separateAudio;
            tvAudioSource.loop = false;
            tvAudioSource.Play();
        }

        bool startedVideo = false;
        double clipDuration = fallbackDuration;

        if (bgVideoPlayer != null)
        {
            if (clipSlot != null)
            {
                bgVideoPlayer.Stop();
                bgVideoPlayer.source = VideoSource.VideoClip;
                bgVideoPlayer.clip = clipSlot;
                bgVideoPlayer.isLooping = false;
                bgVideoPlayer.Play();
                startedVideo = true;
                if (clipSlot.length > 0.2) clipDuration = clipSlot.length;
            }
            else if (!string.IsNullOrEmpty(fileNameSlot))
            {
                string fullPath = Path.Combine(Application.streamingAssetsPath, fileNameSlot);
#if UNITY_WEBGL && !UNITY_EDITOR
                bool exists = true;
#else
                bool exists = File.Exists(fullPath);
#endif
                if (exists)
                {
                    bgVideoPlayer.Stop();
                    bgVideoPlayer.source = VideoSource.Url;
                    bgVideoPlayer.url = fullPath;
                    bgVideoPlayer.isLooping = false;
                    bgVideoPlayer.Play();
                    startedVideo = true;
                }
            }
        }

        if (startedVideo && bgVideoPlayer != null)
        {
            // Wait up to 1.5s for video preparation
            float prepWait = 0f;
            while (!bgVideoPlayer.isPrepared && prepWait < 1.5f)
            {
                prepWait += Time.deltaTime;
                yield return null;
            }

            if (bgVideoPlayer.isPrepared && bgVideoPlayer.length > 0.2)
            {
                clipDuration = bgVideoPlayer.length;
            }

            float elapsed = 0f;
            float maxWait = Mathf.Max(0.5f, (float)clipDuration);
            while (elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                if (elapsed > 0.4f && bgVideoPlayer.isPrepared && !bgVideoPlayer.isPlaying)
                {
                    break;
                }
                yield return null;
            }
        }
        else
        {
            // If user hasn't dragged a VideoClip into the slot yet, display an in-TV broadcast notice so the sequence still plays clearly
            ShowInTvBroadcastFallback(fallbackTitle, fallbackSubtitle);
            yield return new WaitForSeconds(Mathf.Max(1.0f, fallbackDuration));
            HideInTvBroadcastFallback();
        }

        if (separateAudio != null && tvAudioSource != null && tvAudioSource.clip == separateAudio)
        {
            tvAudioSource.Stop();
        }
    }

    private void StartTvStaticLoop(bool playAudio)
    {
        SetTvFullScreenVideoMode(true);

        if (bgVideoPlayer != null)
        {
            if (staticVideoClip != null)
            {
                bgVideoPlayer.source = VideoSource.VideoClip;
                bgVideoPlayer.clip = staticVideoClip;
                bgVideoPlayer.isLooping = true;
                bgVideoPlayer.Play();
            }
            else if (!string.IsNullOrEmpty(staticVideoFileName))
            {
                string fullPath = Path.Combine(Application.streamingAssetsPath, staticVideoFileName);
                bgVideoPlayer.source = VideoSource.Url;
                bgVideoPlayer.url = fullPath;
                bgVideoPlayer.isLooping = true;
                bgVideoPlayer.Play();
            }
        }

        if (playAudio && tvScreenHazard != null && tvScreenHazard.staticLoopAudioSource != null && staticNoiseClip != null)
        {
            AudioSource src = tvScreenHazard.staticLoopAudioSource;
            src.clip = staticNoiseClip;
            src.loop = true;
            src.volume = 0.35f;
            if (!src.isPlaying) src.Play();
        }
    }

    private void StopTvStaticAudio()
    {
        if (tvScreenHazard != null && tvScreenHazard.staticLoopAudioSource != null && tvScreenHazard.staticLoopAudioSource.isPlaying)
        {
            tvScreenHazard.staticLoopAudioSource.Stop();
        }
    }

    private void SetTvFullScreenVideoMode(bool fullScreen)
    {
        if (bgVideoPlayer != null)
        {
            if (!hasSavedBgVideoPos)
            {
                savedBgVideoLocalPos = bgVideoPlayer.transform.localPosition;
                hasSavedBgVideoPos = true;
            }

            if (fullScreen)
            {
                Camera tvCam = bgVideoPlayer.GetComponentInParent<Camera>();
                float frontZ = (tvCam != null ? tvCam.nearClipPlane : 0.3f) + 0.02f;
                bgVideoPlayer.transform.localPosition = new Vector3(
                    savedBgVideoLocalPos.x,
                    savedBgVideoLocalPos.y,
                    frontZ);
            }
            else
            {
                bgVideoPlayer.transform.localPosition = savedBgVideoLocalPos;
            }
        }

        if (bgVideoRenderer != null)
        {
            Material mat = Application.isPlaying ? bgVideoRenderer.material : bgVideoRenderer.sharedMaterial;
            if (mat != null)
            {
                if (savedBgRenderQueue < 0)
                {
                    savedBgRenderQueue = mat.renderQueue;
                    if (mat.HasProperty("_BaseColor")) savedNormalBgAlpha = mat.GetColor("_BaseColor").a;
                    else if (mat.HasProperty("_Color")) savedNormalBgAlpha = mat.GetColor("_Color").a;
                }

                mat.renderQueue = fullScreen ? 4500 : savedBgRenderQueue;
                float targetAlpha = fullScreen ? 1.0f : savedNormalBgAlpha;

                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = targetAlpha;
                    mat.SetColor("_BaseColor", c);
                }
                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = targetAlpha;
                    mat.SetColor("_Color", c);
                }
            }
        }
    }

    private void ShowInTvBroadcastFallback(string title, string body)
    {
        if (bouncer == null || bouncer.RenderCamera == null) return;

        if (inTvBroadcastOverlayObj == null)
        {
            Camera orthoCam = bouncer.RenderCamera;
            inTvBroadcastOverlayObj = new GameObject("TutorialInTvBroadcastFallback");
            inTvBroadcastOverlayObj.transform.SetParent(orthoCam.transform, false);
            inTvBroadcastOverlayObj.transform.localPosition = new Vector3(0f, 0f, (orthoCam.nearClipPlane + 0.01f));
            inTvBroadcastOverlayObj.layer = bouncer.gameObject.layer;

            GameObject bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgQuad.name = "BroadcastBg";
            Destroy(bgQuad.GetComponent<Collider>());
            bgQuad.transform.SetParent(inTvBroadcastOverlayObj.transform, false);
            float h = orthoCam.orthographicSize * 2.1f;
            float w = h * Mathf.Max(1.33f, orthoCam.aspect);
            bgQuad.transform.localScale = new Vector3(w, h, 1f);
            Renderer r = bgQuad.GetComponent<Renderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (r != null && sh != null)
            {
                Material m = new Material(sh);
                Color darkBlue = new Color(0.05f, 0.09f, 0.16f, 1f);
                m.color = darkBlue;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", darkBlue);
                r.material = m;
            }

            GameObject titleGO = new GameObject("BroadcastTitle");
            titleGO.layer = bouncer.gameObject.layer;
            titleGO.transform.SetParent(inTvBroadcastOverlayObj.transform, false);
            titleGO.transform.localPosition = new Vector3(0f, orthoCam.orthographicSize * 0.42f, -0.005f);
            inTvBroadcastTitleTmp = titleGO.AddComponent<TextMeshPro>();
            if (sharedFont != null) inTvBroadcastTitleTmp.font = sharedFont;
            inTvBroadcastTitleTmp.fontSize = 5.2f;
            inTvBroadcastTitleTmp.fontStyle = FontStyles.Bold;
            inTvBroadcastTitleTmp.alignment = TextAlignmentOptions.Center;
            inTvBroadcastTitleTmp.color = new Color(1f, 0.35f, 0.32f, 1f);
            inTvBroadcastTitleTmp.rectTransform.sizeDelta = new Vector2(w * 0.88f, 2.2f);

            GameObject bodyGO = new GameObject("BroadcastBody");
            bodyGO.layer = bouncer.gameObject.layer;
            bodyGO.transform.SetParent(inTvBroadcastOverlayObj.transform, false);
            bodyGO.transform.localPosition = new Vector3(0f, -orthoCam.orthographicSize * 0.10f, -0.005f);
            inTvBroadcastBodyTmp = bodyGO.AddComponent<TextMeshPro>();
            if (sharedFont != null) inTvBroadcastBodyTmp.font = sharedFont;
            inTvBroadcastBodyTmp.fontSize = 3.8f;
            inTvBroadcastBodyTmp.alignment = TextAlignmentOptions.Center;
            inTvBroadcastBodyTmp.color = new Color(0.92f, 0.96f, 1f, 1f);
            inTvBroadcastBodyTmp.rectTransform.sizeDelta = new Vector2(w * 0.86f, h * 0.65f);
        }

        if (inTvBroadcastTitleTmp != null) inTvBroadcastTitleTmp.text = title;
        if (inTvBroadcastBodyTmp != null) inTvBroadcastBodyTmp.text = body;
        inTvBroadcastOverlayObj.SetActive(true);
    }

    private void HideInTvBroadcastFallback()
    {
        if (inTvBroadcastOverlayObj != null && inTvBroadcastOverlayObj.activeSelf)
        {
            inTvBroadcastOverlayObj.SetActive(false);
        }
    }

    // =========================================================================
    // CONTROLS UI POPUP ("I UNDERSTAND") -> STAGE 3: BOSS PHONE CALL
    // =========================================================================

    public void OnControlsUnderstandClicked()
    {
        if (currentStage != TutorialStage.ControlsPopup) return;

        PlayUiBeep(640f, 0.08f);
        ShowControlsPopup(false);

        // Unlock camera look, movement, HUD, and regular game cursor!
        if (smoothCamera != null)
        {
            smoothCamera.lockCameraLook = false;
        }
        SetOpeningCursorAndHudVisible(true);
        GridCrawlerController.InputFrozen = false;

        currentStage = TutorialStage.WaitForBossCall;
        ShowTipBanner("The phone is ringing in the kitchen — go answer it");
    }

    private void OnBossCallCompleted()
    {
        if (!IsTutorialActive) return;

        // Boss transferred $2,000 severance: set money to $2,000 without inflating prices, and activate the Money UI!
        if (bouncer != null)
        {
            bouncer.currentMoney = (decimal)bossSeveranceMoney;
            bouncer.allTimeMoneyEarned = 0m;
            if (bouncer.totalMoneyText != null)
            {
                bouncer.totalMoneyText.gameObject.SetActive(true);
            }
            bouncer.RefreshMoneyUIFromSave();
        }

        if (RunSaveManager.HasInstance)
        {
            // Keep all-time money earned at 0 so the severance transfer doesn't cause starting inflation
            typeof(RunSaveManager)
                .GetField("totalMoneyEarnedDecimal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(RunSaveManager.Instance, 0m);
        }

        PlayUiBeep(720f, 0.10f);
        AdvanceToPayBillStage();
    }

    // =========================================================================
    // STAGE 4: PAY THE STARTING BILL
    // =========================================================================

    private void AdvanceToPayBillStage()
    {
        currentStage = TutorialStage.PayBill;

        // Ensure there is an unpaid bill on the front door wall
        if (billManager != null && !billManager.HasUnpaidBill && billManager.billObjects != null && billManager.billObjects.Length > 0)
        {
            billManager.activeBillIndex = 0;
            billManager.isPaid = false;
            billManager.ApplyBillStateToWorld();
        }

        ShowTipBanner("You received $2,000 severance — pay the overdue bill by the front door");
    }

    // =========================================================================
    // STAGE 5: EAT FOOD (ACTIVATES HUNGER BAR)
    // =========================================================================

    private void AdvanceToEatStage()
    {
        currentStage = TutorialStage.EatFood;

        if (barManager != null)
        {
            barManager.SetBarsActiveState(
                hunger: true,
                needs: false,
                dirtiness: false,
                sleep: false,
                depression: false);

            // Start hunger at 50% so the player sees the bar active and watches it go down when they eat
            barManager.currentHunger = barManager.maxHunger * 0.50f;
        }

        PlayUiBeep(660f, 0.09f);
        ShowTipBanner("Hunger bar active — eat some food in the kitchen");
    }

    private void HandleFoodEaten()
    {
        if (!IsTutorialActive || currentStage != TutorialStage.EatFood) return;
        AdvanceToToiletStage();
    }

    // =========================================================================
    // STAGE 6: USE THE TOILET (ACTIVATES NEEDS BAR)
    // =========================================================================

    private void AdvanceToToiletStage()
    {
        currentStage = TutorialStage.UseToilet;

        if (barManager != null)
        {
            barManager.SetBarsActiveState(
                hunger: true,
                needs: true,
                dirtiness: false,
                sleep: false,
                depression: false);

            barManager.currentNeeds = Mathf.Max(barManager.currentNeeds, barManager.maxNeeds * 0.65f);
        }

        PlayUiBeep(660f, 0.09f);
        ShowTipBanner("Needs bar active — use the toilet in the bathroom");
    }

    private void HandleToiletUsed()
    {
        if (!IsTutorialActive || currentStage != TutorialStage.UseToilet) return;
        isWaitingForActionZoomOut = true;
        pendingCompletedActionType = SurvivalAction.ActionType.Toilet;
    }

    // =========================================================================
    // STAGE 7: TAKE A SHOWER (ACTIVATES DIRTINESS BAR)
    // =========================================================================

    private void AdvanceToShowerStage()
    {
        currentStage = TutorialStage.TakeShower;

        if (barManager != null)
        {
            barManager.SetBarsActiveState(
                hunger: true,
                needs: true,
                dirtiness: true,
                sleep: false,
                depression: false);

            barManager.currentDirtiness = Mathf.Max(barManager.currentDirtiness, barManager.maxDirtiness * 0.60f);
        }

        PlayUiBeep(660f, 0.09f);
        ShowTipBanner("Dirtiness bar active — take a shower in the bathroom");
    }

    private void HandleShowered()
    {
        if (!IsTutorialActive || currentStage != TutorialStage.TakeShower) return;
        isWaitingForActionZoomOut = true;
        pendingCompletedActionType = SurvivalAction.ActionType.Clean;
    }

    public void NotifySurvivalActionAnimationFinished(SurvivalAction.ActionType completedType)
    {
        if (!IsTutorialActive || !isWaitingForActionZoomOut) return;
        if (completedType != pendingCompletedActionType) return;

        isWaitingForActionZoomOut = false;
        if (completedType == SurvivalAction.ActionType.Toilet && currentStage == TutorialStage.UseToilet)
        {
            AdvanceToShowerStage();
        }
        else if (completedType == SurvivalAction.ActionType.Clean && currentStage == TutorialStage.TakeShower)
        {
            AdvanceToSleepStage();
        }
    }

    // =========================================================================
    // STAGE 8 & 9: SLEEP (ACTIVATES SLEEP BAR; NO SLEEP STATS CANVAS)
    // =========================================================================

    private void AdvanceToSleepStage()
    {
        currentStage = TutorialStage.Sleep;

        if (barManager != null)
        {
            barManager.SetBarsActiveState(
                hunger: true,
                needs: true,
                dirtiness: true,
                sleep: true,
                depression: false);

            // Fill tiredness to 95% so the player sees the Sleep bar ready for bed
            barManager.currentSleep = barManager.maxSleep * 0.95f;
        }

        PlayUiBeep(660f, 0.09f);
        ShowTipBanner("Tiredness bar active — go to bed and get some sleep");
    }

    public bool CanPerformSurvivalAction(SurvivalAction.ActionType type, GameObject sourceObj)
    {
        if (!IsTutorialActive) return true;

        switch (type)
        {
            case SurvivalAction.ActionType.Eat:
                if (currentStage < TutorialStage.EatFood)
                {
                    if (billManager != null) billManager.ShowCustomDenyFeedback(sourceObj, "Complete your current tutorial objective first!");
                    return false;
                }
                return true;

            case SurvivalAction.ActionType.Toilet:
                if (currentStage < TutorialStage.UseToilet)
                {
                    if (billManager != null) billManager.ShowCustomDenyFeedback(sourceObj, "Complete your current tutorial objective first!");
                    return false;
                }
                return true;

            case SurvivalAction.ActionType.Clean:
                if (currentStage < TutorialStage.TakeShower)
                {
                    if (billManager != null) billManager.ShowCustomDenyFeedback(sourceObj, "Complete your current tutorial objective first!");
                    return false;
                }
                return true;

            case SurvivalAction.ActionType.Sleep:
                if (currentStage != TutorialStage.Sleep)
                {
                    if (billManager != null) billManager.ShowCustomDenyFeedback(sourceObj, "Complete your current tutorial objective before sleeping!");
                    return false;
                }
                return true;
        }
        return true;
    }

    /// <summary>
    /// Performs the tutorial sleep transition right inside 01Day without opening the Dream/Sleep Stats canvas,
    /// since the first tutorial day is not a real run day.
    /// </summary>
    public void TriggerTutorialSleep(GameObject bedObject)
    {
        if (!IsTutorialActive || currentStage != TutorialStage.Sleep) return;
        StartCoroutine(TutorialSleepTransitionRoutine(bedObject));
    }

    private IEnumerator TutorialSleepTransitionRoutine(GameObject bedObject)
    {
        currentStage = TutorialStage.SleepingTransition;
        SetTutorialBannerVisible(false);

        SceneTransitionOut sceneOut = bedObject != null ? bedObject.GetComponent<SceneTransitionOut>() : null;
        SurvivalAction sleepAction = bedObject != null ? bedObject.GetComponent<SurvivalAction>() : null;

        Camera cam = (sceneOut != null && sceneOut.targetCamera != null)
            ? sceneOut.targetCamera
            : (Camera.main != null ? Camera.main : FindObjectOfType<Camera>());
        Image fadeImg = (sceneOut != null && sceneOut.fadeImage != null)
            ? sceneOut.fadeImage
            : (sleepAction != null ? sleepAction.fadeImage : null);

        float origFov = cam != null ? cam.fieldOfView : 65f;
        float targetFov = sceneOut != null ? sceneOut.zoomTargetFOV : 30f;
        float zoomDur = sceneOut != null ? Mathf.Max(0.45f, sceneOut.zoomInTime) : 0.6f;

        GridCrawlerController.InputFrozen = true;
        if (playerController != null) playerController.SetHudCropPanelVisible(false);

        Color fadeCol = fadeImg != null ? fadeImg.color : Color.black;
        if (fadeImg != null) fadeImg.raycastTarget = true;

        // 1. Zoom in & fade to black
        float elapsed = 0f;
        while (elapsed < zoomDur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / zoomDur));
            if (cam != null) cam.fieldOfView = Mathf.Lerp(origFov, targetFov, t);
            if (fadeImg != null)
            {
                fadeCol.a = t;
                fadeImg.color = fadeCol;
            }
            yield return null;
        }

        if (cam != null) cam.fieldOfView = targetFov;
        if (fadeImg != null)
        {
            fadeCol.a = 1f;
            fadeImg.color = fadeCol;
        }

        // 2. Apply sleep recovery & turn player 180 degrees to face into the room upon waking up
        if (barManager != null)
        {
            barManager.GoToSleep();
            barManager.currentSleep = 0f;
        }

        if (playerController != null)
        {
            playerController.transform.rotation = playerController.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            if (smoothCamera != null)
            {
                smoothCamera.SnapToTargetImmediate();
            }
        }

        // Activate the Depression bar for when the player wakes up!
        if (barManager != null)
        {
            barManager.SetBarsActiveState(
                hunger: true,
                needs: true,
                dirtiness: true,
                sleep: true,
                depression: true);
            barManager.currentDepression = barManager.maxDepression * 0.18f;
        }

        yield return new WaitForSecondsRealtime(1.2f);

        // 3. Zoom out & fade back in (Waking up!)
        elapsed = 0f;
        while (elapsed < zoomDur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / zoomDur));
            if (cam != null) cam.fieldOfView = Mathf.Lerp(targetFov, origFov, t);
            if (fadeImg != null)
            {
                fadeCol.a = 1f - t;
                fadeImg.color = fadeCol;
            }
            yield return null;
        }

        if (cam != null) cam.fieldOfView = origFov;
        if (fadeImg != null)
        {
            fadeCol.a = 0f;
            fadeImg.color = fadeCol;
            fadeImg.raycastTarget = false;
        }

        if (playerController != null) playerController.SetHudCropPanelVisible(true);
        GridCrawlerController.InputFrozen = false;

        // 4. Advance to Unknown Number Phone Call
        AdvanceToUnknownCallerStage();
    }

    // =========================================================================
    // STAGE 10: WAKING UP (DEPRESSION BAR ACTIVE) -> UNKNOWN NUMBER PHONE CALL
    // =========================================================================

    private void AdvanceToUnknownCallerStage()
    {
        currentStage = TutorialStage.WaitForUnknownCall;

        KitchenPhone phone = KitchenPhone.EnsureInstanceInScene();
        if (phone != null)
        {
            phone.StartTutorialCall(unknownCallerCard, OnUnknownCallerCompleted);
        }

        PlayUiBeep(720f, 0.10f);
        ShowTipBanner("Depression bar active — answer the ringing phone in the kitchen");
    }

    private void OnUnknownCallerCompleted()
    {
        if (!IsTutorialActive) return;
        StartCoroutine(OpenStarterShopAfterCallRoutine());
    }

    // =========================================================================
    // STAGE 11: STARTER UPGRADE STORE (ONLY FIRST TWO STARTER ITEMS = $1,000)
    // =========================================================================

    private IEnumerator OpenStarterShopAfterCallRoutine()
    {
        currentStage = TutorialStage.StarterShopOpen;
        boughtStarterDisc = false;
        boughtStarterPlayer = false;
        starterShopAutoCloseTimer = -1f;

        // Wait one frame for phone card canvas to finish closing and restoring camera isolation
        yield return null;

        if (upgradeManager != null && upgradeMenu != null)
        {
            UpgradePath dvdPath = upgradeManager.GetPathByCategory(UpgradeCategory.MoneyMultiplier);
            if (dvdPath != null)
            {
                upgradeMenu.OpenMenu(dvdPath);
            }
        }

        UpdateStarterShopTipText();
    }

    public void NotifyUpgradePurchasedOrEquipped(UpgradePath path, int tierIndex)
    {
        if (!IsTutorialActive || currentStage != TutorialStage.StarterShopOpen || path == null) return;

        if (path.category == UpgradeCategory.MoneyMultiplier && tierIndex == 0 && path.unlockedIndices.Contains(0))
        {
            boughtStarterDisc = true;
        }
        else if (path.category == UpgradeCategory.SpeedMultiplier && tierIndex == 0 && path.unlockedIndices.Contains(0))
        {
            boughtStarterPlayer = true;
        }

        if (AreBothStarterItemsPurchased && !hasCompletedStarterShop)
        {
            ActivateStarterDvdAndPlayerInWorld();
            starterShopAutoCloseTimer = 1.15f;
        }

        UpdateStarterShopTipText();
    }

    private void UpdateStarterShopTipText()
    {
        if (currentStage != TutorialStage.StarterShopOpen) return;

        if (!boughtStarterDisc && !boughtStarterPlayer)
        {
            ShowTipBanner("Buy the Starter DVD Disc ($500) and Starter DVD Player ($500)");
        }
        else if (boughtStarterDisc && !boughtStarterPlayer)
        {
            ShowTipBanner("Switch to [2. DVD PLAYERS] and buy the Starter DVD Player ($500)");
        }
        else if (!boughtStarterDisc && boughtStarterPlayer)
        {
            ShowTipBanner("Switch to [1. DVD DISCS] and buy the Starter DVD Disc ($500)");
        }
        else
        {
            ShowTipBanner("Starter kit bought! Closing store...");
        }
    }

    private void ActivateStarterDvdAndPlayerInWorld()
    {
        // Stop tutorial TV static and turn on the bouncing DVD logo + equipped starter DVD background video!
        StopTvStaticAudio();
        SetTvFullScreenVideoMode(false);

        if (bouncer != null)
        {
            DVDLogoBouncer.GlobalStaticHalt = false;
            SetDvdLogoVisualsVisible(true);
        }

        if (upgradeManager != null)
        {
            upgradeManager.ResumeCurrentDvdVideoAndMusic();
        }
    }

    // =========================================================================
    // STAGE 12: PICK UP REMOTE, CLICK LOGO, AND CLOSE REMOTE
    // =========================================================================

    private void AdvanceToRemoteStage()
    {
        hasCompletedStarterShop = true;
        currentStage = TutorialStage.PickUpAndUseRemote;
        hasPickedUpRemoteInTutorial = false;
        hasClickedRemoteInFocus = false;

        SetRemoteAndTowelInteractable(true);

        PlayUiBeep(680f, 0.09f);
        ShowTipBanner("Go to the TV and pick up the Remote");
    }

    public void NotifyRemotePickedUp()
    {
        if (!IsTutorialActive || currentStage != TutorialStage.PickUpAndUseRemote) return;
        hasPickedUpRemoteInTutorial = true;

        ShowTipBanner("Click the bouncing DVD logo — click outside the TV to put the remote down");
    }

    public void NotifyRemoteClickedInFocus()
    {
        if (!IsTutorialActive || currentStage != TutorialStage.PickUpAndUseRemote) return;
        if (!hasClickedRemoteInFocus)
        {
            hasClickedRemoteInFocus = true;
            ShowTipBanner("Click outside the TV screen whenever you're ready to put the remote down");
        }
    }

    public void NotifyRemotePutDown()
    {
        if (!IsTutorialActive || currentStage != TutorialStage.PickUpAndUseRemote) return;
        if (hasPickedUpRemoteInTutorial)
        {
            AdvanceToFinalPromptStage();
        }
    }

    // =========================================================================
    // STAGE 13: FINAL PROMPT — CHECK OUT THE DVD PLAYER UPGRADE STATION
    // =========================================================================

    private void AdvanceToFinalPromptStage()
    {
        currentStage = TutorialStage.FinalPromptCheckPlayer;
        finalPromptTimer = 14f;

        // Enable the DVD Player upgrade trigger on the desk so the player can open the full store!
        SetUpgradeStationInteractable(true);

        // Enable normal TV static hazard system now that the core tutorial is complete
        if (tvScreenHazard != null)
        {
            tvScreenHazard.enabled = true;
            if (tvScreenHazard.tvCasingInteractable != null)
            {
                tvScreenHazard.tvCasingInteractable.enabled = true;
            }
        }

        PlayUiBeep(780f, 0.12f);
        ShowTipBanner("Look at the DVD Player on your desk to check out what else you can buy");
    }

    private void CompleteTutorial()
    {
        if (isTutorialFinished) return;
        isTutorialFinished = true;
        currentStage = TutorialStage.Completed;
        TutorialCompletedInCurrentRun = true;

        SetUpgradeStationInteractable(true);
        SetRemoteAndTowelInteractable(true);

        if (barManager != null)
        {
            barManager.SetBarsActiveState(true, true, true, true, true);
        }
        if (bouncer != null)
        {
            DVDLogoBouncer.GlobalStaticHalt = false;
            SetDvdLogoVisualsVisible(true);
            if (bouncer.totalMoneyText != null) bouncer.totalMoneyText.gameObject.SetActive(true);
        }
        if (tvScreenHazard != null)
        {
            tvScreenHazard.enabled = true;
            if (tvScreenHazard.tvCasingInteractable != null)
            {
                tvScreenHazard.tvCasingInteractable.enabled = true;
            }
        }

        SetTutorialBannerVisible(false);
    }

    // =========================================================================
    // FRAME UPDATE LOOP
    // =========================================================================

    private void Update()
    {
        if (!IsTutorialActive) return;

        EnsureTutorialCanvasCameraBound();

        switch (currentStage)
        {
            case TutorialStage.OpeningVideos:
                GridCrawlerController.InputFrozen = true;
                if (smoothCamera != null) smoothCamera.lockCameraLook = true;
                SetOpeningCursorAndHudVisible(false);
                break;

            case TutorialStage.ControlsPopup:
                GridCrawlerController.InputFrozen = true;
                if (smoothCamera != null) smoothCamera.lockCameraLook = true;
                UpdateControlsPopupInput();
                break;

            case TutorialStage.PayBill:
                if (billManager != null && !billManager.HasUnpaidBill)
                {
                    AdvanceToEatStage();
                }
                break;

            case TutorialStage.StarterShopOpen:
                // Keep tutorial banner visible on top of the fullscreen shop canvas
                if (tutorialCanvas != null && tutorialCanvas.sortingOrder < 32765)
                {
                    tutorialCanvas.sortingOrder = 32765;
                }
                if (tipBannerRect != null)
                {
                    tipBannerRect.anchoredPosition = new Vector2(0f, -940f);
                }

                // Check if both starter items were bought
                if (!AreBothStarterItemsPurchased && upgradeManager != null)
                {
                    UpgradePath discPath = upgradeManager.GetPathByCategory(UpgradeCategory.MoneyMultiplier);
                    UpgradePath playerPath = upgradeManager.GetPathByCategory(UpgradeCategory.SpeedMultiplier);
                    if (discPath != null && discPath.unlockedIndices.Contains(0)) boughtStarterDisc = true;
                    if (playerPath != null && playerPath.unlockedIndices.Contains(0)) boughtStarterPlayer = true;
                    if (AreBothStarterItemsPurchased)
                    {
                        ActivateStarterDvdAndPlayerInWorld();
                        if (starterShopAutoCloseTimer < 0f) starterShopAutoCloseTimer = 1.15f;
                        UpdateStarterShopTipText();
                    }
                }

                if (AreBothStarterItemsPurchased)
                {
                    if (starterShopAutoCloseTimer > 0f)
                    {
                        starterShopAutoCloseTimer -= Time.unscaledDeltaTime;
                        if (starterShopAutoCloseTimer <= 0f && upgradeMenu != null && upgradeMenu.IsMenuOpen)
                        {
                            upgradeMenu.CloseMenu();
                        }
                    }

                    if (upgradeMenu == null || !upgradeMenu.IsMenuOpen)
                    {
                        if (tipBannerRect != null) tipBannerRect.anchoredPosition = new Vector2(0f, -74f);
                        AdvanceToRemoteStage();
                    }
                }
                break;

            case TutorialStage.PickUpAndUseRemote:
                if (tvFocusController != null)
                {
                    if (tvFocusController.IsHoldingRemote && !hasPickedUpRemoteInTutorial)
                    {
                        NotifyRemotePickedUp();
                    }
                    if (tvFocusController.IsHoldingRemote && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        NotifyRemoteClickedInFocus();
                    }
                    if (hasPickedUpRemoteInTutorial && !tvFocusController.IsInFocusMode)
                    {
                        NotifyRemotePutDown();
                    }
                }
                break;

            case TutorialStage.FinalPromptCheckPlayer:
                finalPromptTimer -= Time.unscaledDeltaTime;
                if (WorldSpaceUpgradeMenu.IsFullscreenShopOpen || finalPromptTimer <= 0f)
                {
                    CompleteTutorial();
                }
                break;
        }
    }

    private void UpdateControlsPopupInput()
    {
        if (controlsPopupBackdrop == null || !controlsPopupBackdrop.activeSelf) return;

        // Keep OS cursor locked inside the Game View and update the virtual CRT cursor
        if (!PauseManager.IsGamePaused)
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
        }

        Vector2 vPos = GridCrawlerController.GetActiveVirtualScreenPos();
        Camera uiCam = (tutorialCanvas != null && tutorialCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            ? tutorialCanvas.worldCamera
            : null;

        if (tutorialVirtualCursorRect != null)
        {
            tutorialVirtualCursorRect.SetAsLastSibling();
            if (!tutorialVirtualCursorRect.gameObject.activeSelf)
                tutorialVirtualCursorRect.gameObject.SetActive(true);

            RectTransform canvasRT = tutorialCanvas.GetComponent<RectTransform>();
            if (canvasRT != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, vPos, uiCam, out Vector2 localPt))
            {
                tutorialVirtualCursorRect.anchoredPosition = localPt;
            }
        }

        bool hoveringUnderstand = false;
        if (controlsUnderstandButton != null)
        {
            RectTransform btnRT = controlsUnderstandButton.GetComponent<RectTransform>();
            if (btnRT != null && RectTransformUtility.RectangleContainsScreenPoint(btnRT, vPos, uiCam))
            {
                hoveringUnderstand = true;
            }
        }

        if (controlsUnderstandBtnImg != null)
        {
            controlsUnderstandBtnImg.color = hoveringUnderstand
                ? new Color(0.24f, 0.88f, 0.48f, 1f)
                : new Color(0.16f, 0.68f, 0.36f, 1f);
        }

        if (tutorialVirtualCursorImg != null && tutorialVirtualCursorRect != null)
        {
            CacheCursorSpritesFromPlayer();
            Sprite targetSprite = hoveringUnderstand && cachedInteractCursorSprite != null
                ? cachedInteractCursorSprite
                : cachedDefaultCursorSprite;
            tutorialVirtualCursorRect.pivot = hoveringUnderstand ? new Vector2(0.5f, 0.5f) : new Vector2(0.28f, 0.82f);
            if (targetSprite != null)
            {
                tutorialVirtualCursorImg.sprite = targetSprite;
                tutorialVirtualCursorRect.sizeDelta = hoveringUnderstand ? cachedInteractCursorSize : cachedDefaultCursorSize;
            }
        }

        bool leftClicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool submitKey = Keyboard.current != null &&
                         (Keyboard.current.spaceKey.wasPressedThisFrame ||
                          Keyboard.current.enterKey.wasPressedThisFrame ||
                          Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                          Keyboard.current.eKey.wasPressedThisFrame);

        if ((leftClicked && hoveringUnderstand) || submitKey)
        {
            OnControlsUnderstandClicked();
        }
    }

    private void CacheCursorSpritesFromPlayer()
    {
        if (cachedDefaultCursorSprite != null && cachedInteractCursorSprite != null) return;
        if (playerController == null) playerController = FindObjectOfType<GridCrawlerController>();
        if (playerController == null) return;

        if (playerController.defaultCrosshairImg != null && playerController.defaultCrosshairImg.sprite != null)
        {
            cachedDefaultCursorSprite = playerController.defaultCrosshairImg.sprite;
            RectTransform rt = playerController.defaultCrosshairImg.rectTransform;
            cachedDefaultCursorSize = new Vector2(
                rt.sizeDelta.x * Mathf.Abs(rt.localScale.x) * 1.061f,
                rt.sizeDelta.y * Mathf.Abs(rt.localScale.y) * 1.061f);
            if (cachedDefaultCursorSize.sqrMagnitude < 16f) cachedDefaultCursorSize = new Vector2(20.7f, 31.8f);
        }
        if (playerController.interactCrosshairImg != null && playerController.interactCrosshairImg.sprite != null)
        {
            cachedInteractCursorSprite = playerController.interactCrosshairImg.sprite;
            RectTransform rt = playerController.interactCrosshairImg.rectTransform;
            cachedInteractCursorSize = new Vector2(
                rt.sizeDelta.x * Mathf.Abs(rt.localScale.x) * 1.061f,
                rt.sizeDelta.y * Mathf.Abs(rt.localScale.y) * 1.061f);
            if (cachedInteractCursorSize.sqrMagnitude < 16f) cachedInteractCursorSize = new Vector2(51f, 51f);
        }
    }

    // =========================================================================
    // TUTORIAL UI CONSTRUCTION (TIP BANNER + CONTROLS POPUP)
    // =========================================================================

    private Camera ResolveMainPlayerCamera()
    {
        if (smoothCamera != null)
        {
            Camera c = smoothCamera.GetComponent<Camera>();
            if (c != null && c.targetTexture == null) return c;
        }
        if (Camera.main != null && Camera.main.targetTexture == null) return Camera.main;
        Camera[] allCams = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < allCams.Length; i++)
        {
            if (allCams[i] != null && allCams[i].targetTexture == null) return allCams[i];
        }
        return null;
    }

    private void EnsureTutorialCanvasCameraBound()
    {
        if (tutorialCanvas == null) return;
        Camera cam = ResolveMainPlayerCamera();
        if (cam != null)
        {
            tutorialCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            tutorialCanvas.worldCamera = cam;
            tutorialCanvas.planeDistance = 0.21f;
        }
    }

    private void BuildTutorialUI()
    {
        if (tutorialCanvas != null) return;

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 5;

        GameObject canvasObj = new GameObject("TutorialOverlayCanvas");
        canvasObj.layer = uiLayer;
        canvasObj.transform.SetParent(transform, false);

        tutorialCanvas = canvasObj.AddComponent<Canvas>();
        EnsureTutorialCanvasCameraBound();
        tutorialCanvas.sortingOrder = 32765;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // ---------------------------------------------------------------------
        // 1. SUBTLE TOP TUTORIAL TEXT (IMPACT FONT + SOFT FEATHERED SHADOW BOX)
        // ---------------------------------------------------------------------
        softShadowBoxSprite = CreateSoftShadowBoxSprite(128, 32);

        GameObject tipShadowObj = new GameObject("TutorialTipShadowBox", typeof(RectTransform), typeof(Image));
        tipShadowObj.layer = uiLayer;
        tipShadowObj.transform.SetParent(canvasObj.transform, false);

        tipShadowBoxImage = tipShadowObj.GetComponent<Image>();
        tipShadowBoxImage.sprite = softShadowBoxSprite;
        tipShadowBoxImage.type = Image.Type.Sliced;
        tipShadowBoxImage.color = new Color(0f, 0f, 0f, 0.62f);
        tipShadowBoxImage.raycastTarget = false;

        tipBannerRect = tipShadowObj.GetComponent<RectTransform>();
        tipBannerRect.anchorMin = new Vector2(0.5f, 1f);
        tipBannerRect.anchorMax = new Vector2(0.5f, 1f);
        tipBannerRect.pivot = new Vector2(0.5f, 1f);
        tipBannerRect.sizeDelta = new Vector2(780f, 92f);
        tipBannerRect.anchoredPosition = new Vector2(0f, -74f);

        tipBannerGroup = tipShadowObj.AddComponent<CanvasGroup>();
        tipBannerGroup.blocksRaycasts = false;

        tipBodyText = CreateText(
            "TipText",
            tipShadowObj.transform,
            "",
            31f,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            new Color(0.98f, 0.97f, 0.94f, 1f));
        tipBodyText.enableWordWrapping = true;
        ApplyImpactTextShadow(tipBodyText);

        RectTransform tbRT = tipBodyText.GetComponent<RectTransform>();
        tbRT.anchorMin = Vector2.zero;
        tbRT.anchorMax = Vector2.one;
        tbRT.offsetMin = new Vector2(46f, 22f);
        tbRT.offsetMax = new Vector2(-46f, -22f);

        tipShadowObj.SetActive(false);

        // ---------------------------------------------------------------------
        // 2. CONTROLS POPUP WINDOW ("I UNDERSTAND")
        // ---------------------------------------------------------------------
        controlsPopupBackdrop = CreatePanel("ControlsPopupBackdrop", canvasObj.transform, new Color(0f, 0f, 0f, 0.72f));
        RectTransform backRT = controlsPopupBackdrop.GetComponent<RectTransform>();
        backRT.anchorMin = Vector2.zero;
        backRT.anchorMax = Vector2.one;
        backRT.offsetMin = Vector2.zero;
        backRT.offsetMax = Vector2.zero;

        // Soft outer shadow behind the Controls card
        GameObject winShadow = new GameObject("ControlsWindowShadow", typeof(RectTransform), typeof(Image));
        winShadow.layer = uiLayer;
        winShadow.transform.SetParent(controlsPopupBackdrop.transform, false);
        Image winShadowImg = winShadow.GetComponent<Image>();
        winShadowImg.sprite = softShadowBoxSprite;
        winShadowImg.type = Image.Type.Sliced;
        winShadowImg.color = new Color(0f, 0f, 0f, 0.85f);
        winShadowImg.raycastTarget = false;
        controlsPopupWindow = winShadow.GetComponent<RectTransform>();
        controlsPopupWindow.anchorMin = new Vector2(0.5f, 0.5f);
        controlsPopupWindow.anchorMax = new Vector2(0.5f, 0.5f);
        controlsPopupWindow.pivot = new Vector2(0.5f, 0.5f);
        controlsPopupWindow.sizeDelta = new Vector2(1020f, 700f);
        controlsPopupWindow.anchoredPosition = Vector2.zero;

        GameObject winInner = CreatePanel("ControlsWindowInner", winShadow.transform, new Color(0.08f, 0.09f, 0.11f, 0.95f));
        RectTransform winInnerRT = winInner.GetComponent<RectTransform>();
        winInnerRT.anchorMin = Vector2.zero;
        winInnerRT.anchorMax = Vector2.one;
        winInnerRT.offsetMin = new Vector2(28f, 28f);
        winInnerRT.offsetMax = new Vector2(-28f, -28f);

        GameObject headerStrip = CreatePanel("HeaderStrip", winInner.transform, new Color(0.13f, 0.14f, 0.17f, 1f));
        RectTransform hsRT = headerStrip.GetComponent<RectTransform>();
        hsRT.anchorMin = new Vector2(0f, 1f);
        hsRT.anchorMax = new Vector2(1f, 1f);
        hsRT.pivot = new Vector2(0.5f, 1f);
        hsRT.sizeDelta = new Vector2(0f, 76f);
        hsRT.anchoredPosition = Vector2.zero;

        TextMeshProUGUI ctrlTitle = CreateText(
            "ControlsTitle",
            headerStrip.transform,
            "CONTROLS",
            34f,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            new Color(0.98f, 0.96f, 0.92f, 1f));
        ApplyImpactTextShadow(ctrlTitle);
        StretchFull(ctrlTitle.GetComponent<RectTransform>());

        string controlsList =
            "<color=#FFD966>MOVE FORWARD:</color>   [W], [UP ARROW], or [SCROLL WHEEL UP]\n\n" +
            "<color=#FFD966>TURN LEFT / RIGHT:</color>   [A] / [D], [LEFT / RIGHT ARROWS], or Screen Edge Arrows\n\n" +
            "<color=#FFD966>TURN AROUND (180°):</color>   [S], [DOWN ARROW], or [SCROLL WHEEL DOWN]\n\n" +
            "<color=#FFD966>INTERACT:</color>   Aim cursor & [LEFT-CLICK] (Hold on Bills & Food)\n\n" +
            "<color=#FFD966>CLEAN TRASH:</color>   Hold [LEFT-CLICK] and swipe over trash on the floor\n\n" +
            "<color=#FFD966>PAUSE:</color>   Press [ESC] or click the Pause icon";

        TextMeshProUGUI ctrlBody = CreateText(
            "ControlsBody",
            winInner.transform,
            controlsList,
            24f,
            FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft,
            new Color(0.93f, 0.94f, 0.96f, 1f));
        ctrlBody.enableWordWrapping = true;
        RectTransform cbRT = ctrlBody.GetComponent<RectTransform>();
        cbRT.anchorMin = new Vector2(0.06f, 0.22f);
        cbRT.anchorMax = new Vector2(0.94f, 0.86f);
        cbRT.offsetMin = Vector2.zero;
        cbRT.offsetMax = Vector2.zero;

        // "I UNDERSTAND" Button
        GameObject btnObj = CreatePanel("UnderstandButton", winInner.transform, new Color(0.16f, 0.68f, 0.36f, 1f));
        controlsUnderstandBtnImg = btnObj.GetComponent<Image>();
        controlsUnderstandBtnImg.raycastTarget = true;
        controlsUnderstandButton = btnObj.AddComponent<Button>();
        controlsUnderstandButton.targetGraphic = controlsUnderstandBtnImg;
        controlsUnderstandButton.onClick.AddListener(OnControlsUnderstandClicked);

        RectTransform btnRT = btnObj.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.27f, 0.05f);
        btnRT.anchorMax = new Vector2(0.73f, 0.16f);
        btnRT.offsetMin = Vector2.zero;
        btnRT.offsetMax = Vector2.zero;

        TextMeshProUGUI btnTxt = CreateText(
            "Label",
            btnObj.transform,
            "I UNDERSTAND",
            30f,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            Color.white);
        ApplyImpactTextShadow(btnTxt);
        StretchFull(btnTxt.GetComponent<RectTransform>());

        // Virtual Cursor inside Tutorial Canvas (for Controls Popup)
        GameObject curObj = new GameObject("TutorialVirtualCursor", typeof(RectTransform), typeof(Image));
        curObj.layer = uiLayer;
        curObj.transform.SetParent(canvasObj.transform, false);
        tutorialVirtualCursorRect = curObj.GetComponent<RectTransform>();
        tutorialVirtualCursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        tutorialVirtualCursorRect.anchorMax = new Vector2(0.5f, 0.5f);
        tutorialVirtualCursorRect.sizeDelta = new Vector2(21.2f, 31.8f);
        tutorialVirtualCursorImg = curObj.GetComponent<Image>();
        tutorialVirtualCursorImg.raycastTarget = false;
        tutorialVirtualCursorImg.preserveAspect = true;
        curObj.SetActive(false);

        controlsPopupBackdrop.SetActive(false);
    }

    private void ShowControlsPopup(bool show)
    {
        if (tutorialCanvas == null) BuildTutorialUI();
        if (controlsPopupBackdrop != null) controlsPopupBackdrop.SetActive(show);
        if (tutorialVirtualCursorRect != null) tutorialVirtualCursorRect.gameObject.SetActive(show);

        if (show)
        {
            // Keep gameplay HUD & gameplay cursor hidden while the Controls Popup and its own cursor are active
            if (playerController != null)
            {
                playerController.SetHudCropPanelVisible(false);
                playerController.SetFocusCursorVisuals(false, false, "");
            }
            if (cachedCursorHolderObj != null)
            {
                cachedCursorHolderObj.SetActive(false);
            }
        }
    }

    private void ShowTipBanner(string message)
    {
        if (tutorialCanvas == null) BuildTutorialUI();
        if (tipBodyText != null)
        {
            tipBodyText.text = message;
            if (tipBannerRect != null)
            {
                const float maxTextWidth = 1040f;
                Vector2 preferred = tipBodyText.GetPreferredValues(message, maxTextWidth, 0f);
                float boxW = Mathf.Clamp(preferred.x + 116f, 320f, 1160f);
                float boxH = Mathf.Clamp(preferred.y + 66f, 84f, 220f);
                tipBannerRect.sizeDelta = new Vector2(boxW, boxH);
            }
        }
        SetTutorialBannerVisible(true);
    }

    private static Sprite CreateSoftShadowBoxSprite(int size, int featherBorder)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        int innerMin = featherBorder;
        int innerMax = size - 1 - featherBorder;
        float invBorder = 1f / Mathf.Max(1f, featherBorder);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float dy = 0f;
            if (y < innerMin) dy = (innerMin - y) * invBorder;
            else if (y > innerMax) dy = (y - innerMax) * invBorder;

            for (int x = 0; x < size; x++)
            {
                float dx = 0f;
                if (x < innerMin) dx = (innerMin - x) * invBorder;
                else if (x > innerMax) dx = (x - innerMax) * invBorder;

                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01(1f - dist);
                float smooth = t * t * (3f - 2f * t);
                float alpha = smooth * smooth;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);

        Vector4 border = new Vector4(featherBorder, featherBorder, featherBorder, featherBorder);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }

    private static void ApplyImpactTextShadow(TextMeshProUGUI tmp)
    {
        if (tmp == null) return;
        Material mat = tmp.fontMaterial;
        if (mat == null) return;

        mat.EnableKeyword("OUTLINE_ON");
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.08f);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.85f));
        mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.06f);

        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.78f));
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.25f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.12f);
        mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.42f);

        tmp.UpdateMeshPadding();
    }

    private void SetTutorialBannerVisible(bool visible)
    {
        if (tipBannerRect != null && tipBannerRect.gameObject.activeSelf != visible)
        {
            tipBannerRect.gameObject.SetActive(visible);
        }
    }

    private static GameObject CreatePanel(string name, Transform parent, Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = false;
        return go;
    }

    private TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string content,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions align,
        Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (sharedFont != null) tmp.font = sharedFont;
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void PlayUiBeep(float freq, float duration)
    {
        if (uiAudioSource == null) return;
        int sampleRate = 44100;
        int samples = Mathf.Max(1, (int)(sampleRate * duration));
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = 1f - (t / duration);
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.22f;
        }
        AudioClip clip = AudioClip.Create("TutorialBeep", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        uiAudioSource.PlayOneShot(clip, 0.7f);
    }
}
