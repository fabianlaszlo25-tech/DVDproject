using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Cross-scene run persistence and run statistics manager.
/// Lives on a DontDestroyOnLoad GameObject ("RunSaveManager") across:
/// - Main Room ("01Day")
/// - Bar ("BarGameBarni")
/// - Dream Sequence ("KristofDreamGames")
///
/// Responsibilities:
/// 1. Migrates player bars, money, DVD bouncer stats, and DVD Player / DVD upgrades between scenes.
/// 2. Preserves all scene changes during an active run (remaining trash in 01Day when visiting the Bar,
///    purchased permanent booster props in both 01Day and BarGameBarni, active timed consumables & combos,
///    player position/rotation, and audio volume settings).
/// 3. Tracks the 8 run statistics for the Dream Sequence generator and the animated Death / Dream summary canvases:
///    - Money Earned
///    - Days Survived
///    - Substances Consumed (total + per substance type for procedural dream generation)
///    - Money Earned by Gambling
///    - Money Lost by Gambling
///    - Trash Cleaned
///    - Story Elements Encountered This Run
///    - Final Score
/// 4. Handles waking up from the Dream Sequence (resets trash for the new day, sets Depression to 0,
///    and keeps all other scene changes, upgrades, and money intact).
/// </summary>
[DefaultExecutionOrder(-50)]
public class RunSaveManager : MonoBehaviour
{
    public static RunSaveManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;

    [Header("Scene Names")]
    public string roomSceneName = "01Day";
    public string barSceneName = "BarGameBarni";
    public string dreamSceneName = "KristofDreamGames";
    public string menuSceneName = "06Menu";

    [Header("Run Data Collection (Live Run Metrics)")]
    [Tooltip("Total money earned during this run (stored as double/float for Inspector visibility; backed by decimal).")]
    [SerializeField] private double totalMoneyEarnedInspector;
    public int daysSurvived;
    public int substancesConsumed;
    [SerializeField] private double moneyEarnedByGamblingInspector;
    [SerializeField] private double moneyLostByGamblingInspector;
    public int trashCleaned;
    public int storyElementsEncountered;
    public List<string> encounteredStoryElementIds = new List<string>();

    [Header("Daily Trash Spawn Calculation Settings")]
    [Tooltip("Day 1 base trash count: exactly this amount of trash spawns on the very first day when a run starts.")]
    public int baseTrashCount = 25;

    [Tooltip("Subsequent days base trash count: Day 2 and each following day starts with this separate number instead of the Day 1 base, then adds dirtiness/depression and subtracts cleaned trash.")]
    public int subsequentDaysBaseTrash = 5;

    [Tooltip("Additional trash per point of bedtime dirtiness (e.g. 0.08 = +8 trash at 100 dirtiness).")]
    public float dirtinessTrashMultiplier = 0.08f;

    [Tooltip("Additional trash per point of bedtime depression (e.g. 0.06 = +6 trash at 100 depression).")]
    public float depressionTrashMultiplier = 0.06f;

    [Tooltip("Trash spawn reduction per piece of trash cleaned yesterday (e.g. 0.5 = 2 cleaned trash reduces spawn by 1).")]
    public float cleanedTrashReductionFactor = 0.5f;

    [Tooltip("Minimum trash items that can spawn in a morning.")]
    public int minTrashSpawn = 2;

    [Tooltip("Maximum trash items that can spawn in a morning.")]
    public int maxTrashSpawn = 25;

    [Header("Morning Depression Calculation Settings")]
    [Tooltip("Base morning depression when waking up.")]
    public float baseMorningDepression = 0f;

    [Tooltip("Fraction of bedtime depression carried over into the morning (e.g. 0.35 = 35% carryover).")]
    public float depressionSleepCarryoverFactor = 0.35f;

    [Tooltip("Morning depression increase per point of bedtime dirtiness (e.g. 0.15 = +15 depression at 100 dirtiness).")]
    public float dirtinessMorningDepressionFactor = 0.15f;

    [Tooltip("Morning depression increase per trash item spawned in the morning (e.g. 1.2 = +12 depression for 10 trash items).")]
    public float trashMorningDepressionFactor = 1.2f;

    [Tooltip("Hangover depression penalty per drug/substance consumed yesterday.")]
    public float drugMorningDepressionPenalty = 6f;

    [Tooltip("Depression reduction per story interaction experienced yesterday.")]
    public float storyInteractionDepressionReduction = 12f;

    [Tooltip("Minimum morning depression (clamped).")]
    public float minMorningDepression = 0f;

    [Tooltip("Maximum morning depression (clamped, recommended < 100 to avoid instant morning breakdown).")]
    public float maxMorningDepression = 85f;

    [Header("Daily Tracking State (Yesterday & Today)")]
    [SerializeField] private int trashCleanedThisDay;
    [SerializeField] private int trashCleanedYesterday;
    [SerializeField] private int substancesConsumedThisDay;
    [SerializeField] private int substancesConsumedYesterday;
    [SerializeField] private int storyElementsThisDay;
    [SerializeField] private int storyElementsYesterday;
    [SerializeField] private float bedtimeDirtiness;
    [SerializeField] private float bedtimeDepression;
    [SerializeField] private int pendingMorningTrashCount = 5;
    [SerializeField] private float pendingMorningDepression = 0f;

    [Header("Per-Substance Breakdown (For Future Dream World Generation)")]
    public int drunkConsumedCount;
    public int caffeinatedConsumedCount;
    public int highConsumedCount;
    public int nicotinedConsumedCount;

    [Header("Final Score Weights")]
    public float scorePerDollarEarned = 10f;
    public int scorePerDaySurvived = 250;
    public int scorePerSubstanceConsumed = 50;
    public float scorePerGamblingDollarWon = 15f;
    public float scorePerGamblingDollarLost = 5f;
    public int scorePerTrashCleaned = 25;
    public int scorePerStoryElement = 300;

    private decimal totalMoneyEarnedDecimal;
    private decimal moneyEarnedByGamblingDecimal;
    private decimal moneyLostByGamblingDecimal;

    public decimal TotalMoneyEarned => totalMoneyEarnedDecimal;
    public decimal MoneyEarnedByGambling => moneyEarnedByGamblingDecimal;
    public decimal MoneyLostByGambling => moneyLostByGamblingDecimal;

    // =========================================================================
    // SAVED CROSS-SCENE PLAYER, ECONOMY & UPGRADE STATE
    // =========================================================================

    [Serializable]
    public class SavedUpgradePathState
    {
        public string pathName;
        public UpgradeCategory category;
        public int equippedIndex;
        public List<int> unlockedIndices = new List<int>();
    }

    [Serializable]
    public class SavedActiveConsumableState
    {
        public ConsumableVisualEffectType effectType;
        public float remainingHoldDuration;
        public float currentIntensity;
        public bool wasLockedByCombination;
        public string sourceSceneName;
        public string sourceBuyableHierarchyPath;
    }

    [Serializable]
    public class SavedSpawnedTrashEntry
    {
        public int prefabIndex;
        public string prefabName;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public class SavedTrashManagerState
    {
        public string managerHierarchyPath;
        public List<SavedSpawnedTrashEntry> remainingItems = new List<SavedSpawnedTrashEntry>();
    }

    private class SceneSpecificSavedState
    {
        public bool hasSavedPlayerTransform;
        public Vector3 playerPosition;
        public Quaternion playerRotation;

        public readonly HashSet<string> purchasedPermanentBuyablePaths = new HashSet<string>();
        public readonly HashSet<string> consumedActiveBuyablePaths = new HashSet<string>();
        public readonly HashSet<string> brokenPermanentBoosterPaths = new HashSet<string>();

        public bool hasSavedTrashState;
        public readonly HashSet<string> remainingPreplacedTrashPaths = new HashSet<string>();
        public readonly Dictionary<string, SavedTrashManagerState> trashManagerStates = new Dictionary<string, SavedTrashManagerState>();
    }

    // Run-wide tracking of broken permanent booster props so they stay gone for the rest of the run
    public readonly HashSet<string> runWideBrokenPermanentBoosterPaths = new HashSet<string>();

    private bool hasSavedPlayerBars;
    private float savedHunger;
    private float savedNeeds;
    private float savedSleep;
    private float savedDirtiness;
    private float savedDepression;

    // BarManager stat configuration (reflects permanent BoosterObject modifications)
    private float savedMaxHunger = 100f;
    private float savedMaxNeeds = 100f;
    private float savedMaxSleep = 100f;
    private float savedMaxDirtiness = 100f;
    private float savedMaxDepression = 100f;
    private float savedHungerFillRate = 2f;
    private float savedSleepFillRate = 1.5f;
    private float savedBaseDepressionFillRate = 1f;
    private float savedFoodHungerReduction = 50f;
    private float savedToiletNeedsReduction = 100f;
    private float savedSleepReduction = 100f;
    private float savedCleanDirtinessReduction = 100f;
    private float savedEatDirtinessIncrease = 10f;
    private float savedToiletDirtinessIncrease = 15f;
    private float savedSleepDirtinessIncrease = 5f;
    private float savedFoodNeedsIncrease = 33.4f;
    private float savedTrashDirtinessIncrease = 1f;
    private float savedTrashDepressionReduction = 1f;
    private float savedEatDepressionReduction = 5f;
    private float savedToiletDepressionReduction = 2f;
    private float savedSleepDepressionReduction = 10f;
    private float savedCleanDepressionReduction = 15f;
    private float savedBaseFoodCost = 10f;
    private float savedInflationRatePerDollar = 0.0001f;
    private float savedPenaltyMultiplier = 2f;

    // DVDLogoBouncer economy & speed state
    private bool hasSavedBouncerState;
    private decimal savedCurrentMoney;
    private decimal savedEdgeReward = 0.01m;
    private decimal savedCornerReward = 0.05m;
    private float savedBaseSpeed = 2.5f;
    private float savedStateMultiplier = 1f;
    private float savedMoneyMultiplier = 1f;
    private Vector2 savedBouncerDirection = Vector2.zero;

    [Header("Daily Bill State")]
    public int currentActiveBillIndex = -1;
    public bool isCurrentBillPaid = false;
    public int lastBillDay = -1;
    public List<int> billHistory = new List<int>();

    // UpgradeManager state
    private bool hasSavedUpgrades;
    private readonly List<SavedUpgradePathState> savedUpgradePaths = new List<SavedUpgradePathState>();

    // Active TimedConsumableBooster & SubstanceCombinationManager state across scenes
    private readonly List<SavedActiveConsumableState> savedActiveConsumables = new List<SavedActiveConsumableState>();
    private ActiveSubstanceComboState savedComboState = ActiveSubstanceComboState.None;
    private bool savedComboWasLocked;
    private float savedComboRemainingTimer;
    private float savedComboIntensity;

    // Audio settings state
    public bool hasSavedAudioSettings;
    public float savedMusicSliderVolume = 1f;
    public float savedSfxListenerVolume = 1f;

    // Per-scene state (01Day, BarGameBarni)
    private readonly Dictionary<string, SceneSpecificSavedState> sceneStates = new Dictionary<string, SceneSpecificSavedState>();

    // Wake-up flag: when true, the next load of 01Day resets trash for the new day and sets depression to 0
    private bool resetTrashOnNextRoomLoad;
    private string lastActiveGameplayScene = string.Empty;
    private bool isRestoringSceneState;
    [HideInInspector] public bool isReturningFromDream = false;

    public bool IsRestoringSceneState => isRestoringSceneState;
    public bool HasSavedUpgrades => hasSavedUpgrades;
    public bool HasSavedBouncerState => hasSavedBouncerState;

    // =========================================================================
    // LIFECYCLE & SINGLETON
    // =========================================================================

    public static RunSaveManager EnsureExists()
    {
        if (Instance != null) return Instance;

        RunSaveManager existing = FindObjectOfType<RunSaveManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject go = new GameObject("RunSaveManager");
        Instance = go.AddComponent<RunSaveManager>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (transform.parent != null)
        {
            transform.SetParent(null, false);
        }
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (daysSurvived == 0)
        {
            pendingMorningTrashCount = baseTrashCount;
        }
    }

    private void OnValidate()
    {
        if (daysSurvived == 0)
        {
            pendingMorningTrashCount = baseTrashCount;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    // =========================================================================
    // DATA COLLECTION API
    // =========================================================================

    public void RecordMoneyEarned(decimal amount)
    {
        if (amount <= 0m) return;
        totalMoneyEarnedDecimal += amount;
        totalMoneyEarnedInspector = (double)totalMoneyEarnedDecimal;
    }

    public void RecordMoneyEarned(float amount)
    {
        if (amount <= 0f) return;
        RecordMoneyEarned((decimal)amount);
    }

    public void RecordDaySurvived()
    {
        daysSurvived++;
    }

    public void RecordSubstanceConsumed(ConsumableVisualEffectType effectType)
    {
        substancesConsumed++;
        substancesConsumedThisDay++;
        switch (effectType)
        {
            case ConsumableVisualEffectType.Drunk:
                drunkConsumedCount++;
                break;
            case ConsumableVisualEffectType.Caffeinated:
                caffeinatedConsumedCount++;
                break;
            case ConsumableVisualEffectType.High:
                highConsumedCount++;
                break;
            case ConsumableVisualEffectType.Nicotined:
                nicotinedConsumedCount++;
                break;
        }
    }

    public void RecordGamblingWin(decimal amount, bool addToCurrentMoney = false)
    {
        if (amount <= 0m) return;
        moneyEarnedByGamblingDecimal += amount;
        moneyEarnedByGamblingInspector = (double)moneyEarnedByGamblingDecimal;

        totalMoneyEarnedDecimal += amount;
        totalMoneyEarnedInspector = (double)totalMoneyEarnedDecimal;

        if (addToCurrentMoney)
        {
            DVDLogoBouncer bouncer = FindObjectOfType<DVDLogoBouncer>();
            if (bouncer != null)
            {
                bouncer.currentMoney += amount;
                bouncer.RefreshMoneyUIFromSave();
            }
            else if (hasSavedBouncerState)
            {
                savedCurrentMoney += amount;
            }
        }
    }

    public void RecordGamblingWin(float amount, bool addToCurrentMoney = false)
    {
        if (amount <= 0f) return;
        RecordGamblingWin((decimal)amount, addToCurrentMoney);
    }

    public void RecordGamblingLoss(decimal amount, bool deductFromCurrentMoney = false)
    {
        if (amount <= 0m) return;
        moneyLostByGamblingDecimal += amount;
        moneyLostByGamblingInspector = (double)moneyLostByGamblingDecimal;

        if (deductFromCurrentMoney)
        {
            DVDLogoBouncer bouncer = FindObjectOfType<DVDLogoBouncer>();
            if (bouncer != null)
            {
                bouncer.TrySpendMoney(amount);
            }
            else if (hasSavedBouncerState)
            {
                savedCurrentMoney = Math.Max(0m, savedCurrentMoney - amount);
            }
        }
    }

    public void RecordGamblingLoss(float amount, bool deductFromCurrentMoney = false)
    {
        if (amount <= 0f) return;
        RecordGamblingLoss((decimal)amount, deductFromCurrentMoney);
    }

    public void RecordTrashCleaned()
    {
        trashCleaned++;
        trashCleanedThisDay++;
    }

    public void RecordStoryElementEncountered(string storyElementId = "")
    {
        if (!string.IsNullOrWhiteSpace(storyElementId))
        {
            if (encounteredStoryElementIds.Contains(storyElementId))
            {
                return;
            }
            encounteredStoryElementIds.Add(storyElementId);
        }

        storyElementsEncountered++;
        storyElementsThisDay++;
    }

    public int GetSubstanceCount(ConsumableVisualEffectType effectType)
    {
        switch (effectType)
        {
            case ConsumableVisualEffectType.Drunk: return drunkConsumedCount;
            case ConsumableVisualEffectType.Caffeinated: return caffeinatedConsumedCount;
            case ConsumableVisualEffectType.High: return highConsumedCount;
            case ConsumableVisualEffectType.Nicotined: return nicotinedConsumedCount;
            default: return 0;
        }
    }

    public int CalculateFinalScore()
    {
        float score = 0f;
        score += (float)totalMoneyEarnedDecimal * scorePerDollarEarned;
        score += daysSurvived * scorePerDaySurvived;
        score += substancesConsumed * scorePerSubstanceConsumed;
        score += (float)moneyEarnedByGamblingDecimal * scorePerGamblingDollarWon;
        score -= (float)moneyLostByGamblingDecimal * scorePerGamblingDollarLost;
        score += trashCleaned * scorePerTrashCleaned;
        score += storyElementsEncountered * scorePerStoryElement;

        return Mathf.Max(0, Mathf.RoundToInt(score));
    }

    // =========================================================================
    // SCENE TRANSITION & WAKE UP / RESET FLOW
    // =========================================================================

    /// <summary>
    /// Called automatically by <see cref="SceneTransitionOut"/> right before loading the next scene.
    /// Captures all current scene state so nothing is lost.
    /// </summary>
    public void OnBeforeSceneTransition(string targetSceneName)
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (IsGameplayScene(currentScene))
        {
            CaptureCurrentSceneState(currentScene);
        }

        // Going to sleep -> entering the Dream Sequence
        if (string.Equals(targetSceneName, dreamSceneName, StringComparison.OrdinalIgnoreCase))
        {
            RecordDaySurvived();

            // Capture bedtime stats before resetting bars
            BarManager barManager = FindObjectOfType<BarManager>();
            if (barManager != null)
            {
                bedtimeDirtiness = barManager.currentDirtiness;
                bedtimeDepression = barManager.currentDepression;
            }
            else if (hasSavedPlayerBars)
            {
                bedtimeDirtiness = savedDirtiness;
                bedtimeDepression = savedDepression;
            }

            // Snapshot daily metrics to yesterday counters and reset today's counters
            trashCleanedYesterday = trashCleanedThisDay;
            trashCleanedThisDay = 0;

            substancesConsumedYesterday = substancesConsumedThisDay;
            substancesConsumedThisDay = 0;

            storyElementsYesterday = storyElementsThisDay;
            storyElementsThisDay = 0;

            // Calculate morning trash count and morning depression
            pendingMorningTrashCount = CalculateMorningTrashCount();
            pendingMorningDepression = CalculateMorningDepression(pendingMorningTrashCount);

            // Waking up after the dream will reset trash and restore the calculated morning depression
            resetTrashOnNextRoomLoad = true;
            if (hasSavedPlayerBars)
            {
                savedSleep = 0f;
                savedDepression = pendingMorningDepression;
                savedDirtiness = Mathf.Clamp(bedtimeDirtiness + savedSleepDirtinessIncrease, 0f, savedMaxDirtiness);
            }

            // Clear temporary substance effects when sleeping through the night
            savedActiveConsumables.Clear();
            savedComboState = ActiveSubstanceComboState.None;
            savedComboWasLocked = false;
            savedComboRemainingTimer = 0f;
            savedComboIntensity = 0f;

            foreach (var kvp in sceneStates)
            {
                kvp.Value.consumedActiveBuyablePaths.Clear();
            }
        }
        else if (string.Equals(targetSceneName, menuSceneName, StringComparison.OrdinalIgnoreCase))
        {
            try
        {
            ResetRunForNewGame();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RunSaveManager] Error in ResetRunForNewGame during death return: {ex}");
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        }
    }

    /// <summary>
    /// Called by the "WAKE UP" button on the Dream Sequence canvas in KristofDreamGames.
    /// Resets trash for the new day, sets Depression (and Sleep) to 0, keeps all other state intact,
    /// and loads the main room (01Day).
    /// </summary>
    public void WakeUpFromDreamAndReturnToRoom()
    {
        resetTrashOnNextRoomLoad = true;
        isReturningFromDream = true;

        if (hasSavedPlayerBars)
        {
            savedDepression = pendingMorningDepression;
            savedSleep = 0f;
        }

        if (sceneStates.TryGetValue(roomSceneName, out SceneSpecificSavedState roomState))
        {
            roomState.hasSavedTrashState = false;
            roomState.remainingPreplacedTrashPaths.Clear();
            roomState.trashManagerStates.Clear();
            roomState.consumedActiveBuyablePaths.Clear();
        }

        savedActiveConsumables.Clear();
        savedComboState = ActiveSubstanceComboState.None;
        savedComboWasLocked = false;
        savedComboRemainingTimer = 0f;
        savedComboIntensity = 0f;

        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(roomSceneName);
    }

    /// <summary>
    /// Called by the "GO TO MENU" button on the DeathSeq canvas (or when starting a fresh run).
    /// Clears all run statistics and saved scene states, then loads the menu if requested.
    /// </summary>
    public void ResetRunForNewGame()
    {
        totalMoneyEarnedDecimal = 0m;
        totalMoneyEarnedInspector = 0.0;
        daysSurvived = 0;
        substancesConsumed = 0;
        moneyEarnedByGamblingDecimal = 0m;
        moneyEarnedByGamblingInspector = 0.0;
        moneyLostByGamblingDecimal = 0m;
        moneyLostByGamblingInspector = 0.0;
        trashCleaned = 0;
        storyElementsEncountered = 0;
        encounteredStoryElementIds.Clear();

        trashCleanedThisDay = 0;
        trashCleanedYesterday = 0;
        substancesConsumedThisDay = 0;
        substancesConsumedYesterday = 0;
        storyElementsThisDay = 0;
        storyElementsYesterday = 0;
        bedtimeDirtiness = 0f;
        bedtimeDepression = 0f;
        pendingMorningTrashCount = baseTrashCount;
        pendingMorningDepression = baseMorningDepression;

        drunkConsumedCount = 0;
        caffeinatedConsumedCount = 0;
        highConsumedCount = 0;
        nicotinedConsumedCount = 0;

        hasSavedPlayerBars = false;
        hasSavedBouncerState = false;
        hasSavedUpgrades = false;
        savedUpgradePaths.Clear();

        savedActiveConsumables.Clear();
        savedComboState = ActiveSubstanceComboState.None;
        savedComboWasLocked = false;
        savedComboRemainingTimer = 0f;
        savedComboIntensity = 0f;

        sceneStates.Clear();
        runWideBrokenPermanentBoosterPaths.Clear();
        resetTrashOnNextRoomLoad = false;
        lastActiveGameplayScene = string.Empty;

        currentActiveBillIndex = -1;
        isCurrentBillPaid = false;
        lastBillDay = -1;
        billHistory.Clear();
    }

    public void ReturnToMainMenuFromDeath()
    {
        try
        {
            ResetRunForNewGame();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RunSaveManager] Error in ResetRunForNewGame during death return: {ex}");
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(menuSceneName);
    }


    // =========================================================================
    // DAILY TRASH & MORNING DEPRESSION CALCULATIONS
    // =========================================================================

    /// <summary>
    /// Calculates the number of trash items that will spawn for Day 2 and subsequent days.
    /// Uses subsequentDaysBaseTrash (a separate tunable number), plus bedtime dirtiness and depression,
    /// minus trash cleaned yesterday.
    /// </summary>
    public int CalculateMorningTrashCount()
    {
        float rawTrash = subsequentDaysBaseTrash
            + (bedtimeDirtiness * dirtinessTrashMultiplier)
            + (bedtimeDepression * depressionTrashMultiplier)
            - (trashCleanedYesterday * cleanedTrashReductionFactor);

        return Mathf.Clamp(Mathf.RoundToInt(rawTrash), minTrashSpawn, maxTrashSpawn);
    }

    /// <summary>
    /// Calculates starting depression for the morning after sleep.
    /// Based on:
    /// - baseMorningDepression
    /// - Bedtime depression carryover (+)
    /// - Bedtime dirtiness (+)
    /// - Morning trash count (+)
    /// - Yesterday drugs/substances consumed (+)
    /// - Yesterday story interactions (-)
    /// </summary>
    public float CalculateMorningDepression(int trashCount)
    {
        float rawDepression = baseMorningDepression
            + (bedtimeDepression * depressionSleepCarryoverFactor)
            + (bedtimeDirtiness * dirtinessMorningDepressionFactor)
            + (trashCount * trashMorningDepressionFactor)
            + (substancesConsumedYesterday * drugMorningDepressionPenalty)
            - (storyElementsYesterday * storyInteractionDepressionReduction);

        return Mathf.Clamp(rawDepression, minMorningDepression, maxMorningDepression);
    }

    /// <summary>
    /// Distributes this morning's trash quota to a requesting TrashManager.
    /// On Day 1 (run start), returns Day 1 base trash count directly.
    /// On Day 2+, distributes pendingMorningTrashCount across active managers.
    /// </summary>
    public int GetMorningTrashCountForManager(TrashManager manager)
    {
        // On Day 1 (first day of a run), spawn the Day 1 base trash count directly
        if (daysSurvived == 0)
        {
            int count = baseTrashCount;
            if (manager != null && manager.firstDayTrashCount > 0 && manager.firstDayTrashCount != count)
            {
                count = manager.firstDayTrashCount;
                baseTrashCount = count;
            }
            pendingMorningTrashCount = count;
            return count;
        }

        TrashManager[] managers = FindObjectsOfType<TrashManager>(false);
        int mgrCount = managers != null && managers.Length > 0 ? managers.Length : 1;

        if (mgrCount <= 1)
        {
            return pendingMorningTrashCount;
        }

        int share = pendingMorningTrashCount / mgrCount;
        int remainder = pendingMorningTrashCount % mgrCount;
        int index = Array.IndexOf(managers, manager);
        if (index >= 0 && index < remainder)
        {
            share += 1;
        }
        return Mathf.Max(1, share);
    }

    private bool IsGameplayScene(string sceneName)
    {
        return string.Equals(sceneName, roomSceneName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sceneName, barSceneName, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // CAPTURING CURRENT SCENE STATE BEFORE SCENE UNLOAD
    // =========================================================================

    public void CaptureCurrentSceneState(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || !IsGameplayScene(sceneName)) return;

        SceneSpecificSavedState sceneState = GetOrCreateSceneState(sceneName);

        // 1. Capture active timed consumables & combinations first, and temporarily revert their
        //    temporary stat modifiers before reading BarManager and DVDLogoBouncer base stats
        //    so temporary modifiers are never baked into permanent stats across scene loads!
        CaptureAndTemporarilyRevertActiveConsumables(sceneName, sceneState);

        // 2. Capture BarManager values and stat configuration
        BarManager barManager = FindObjectOfType<BarManager>();
        if (barManager != null)
        {
            hasSavedPlayerBars = true;
            savedHunger = barManager.currentHunger;
            savedNeeds = barManager.currentNeeds;
            savedSleep = barManager.currentSleep;
            savedDirtiness = barManager.currentDirtiness;
            savedDepression = barManager.currentDepression;

            savedMaxHunger = barManager.maxHunger;
            savedMaxNeeds = barManager.maxNeeds;
            savedMaxSleep = barManager.maxSleep;
            savedMaxDirtiness = barManager.maxDirtiness;
            savedMaxDepression = barManager.maxDepression;

            savedHungerFillRate = barManager.hungerFillRate;
            savedSleepFillRate = barManager.sleepFillRate;
            savedBaseDepressionFillRate = barManager.baseDepressionFillRate;

            savedFoodHungerReduction = barManager.foodHungerReduction;
            savedToiletNeedsReduction = barManager.toiletNeedsReduction;
            savedSleepReduction = barManager.sleepReduction;
            savedCleanDirtinessReduction = barManager.cleanDirtinessReduction;

            savedEatDirtinessIncrease = barManager.eatDirtinessIncrease;
            savedToiletDirtinessIncrease = barManager.toiletDirtinessIncrease;
            savedSleepDirtinessIncrease = barManager.sleepDirtinessIncrease;
            savedFoodNeedsIncrease = barManager.foodNeedsIncrease;

            savedTrashDirtinessIncrease = barManager.trashDirtinessIncrease;
            savedTrashDepressionReduction = barManager.trashDepressionReduction;

            savedEatDepressionReduction = barManager.eatDepressionReduction;
            savedToiletDepressionReduction = barManager.toiletDepressionReduction;
            savedSleepDepressionReduction = barManager.sleepDepressionReduction;
            savedCleanDepressionReduction = barManager.cleanDepressionReduction;

            savedBaseFoodCost = barManager.baseFoodCost;
            savedInflationRatePerDollar = barManager.inflationRatePerDollar;
            savedPenaltyMultiplier = barManager.penaltyMultiplier;
        }

        // 3. Capture DVDLogoBouncer money and economy/speed settings
        DVDLogoBouncer bouncer = FindObjectOfType<DVDLogoBouncer>();
        if (bouncer != null)
        {
            hasSavedBouncerState = true;
            savedCurrentMoney = bouncer.currentMoney;
            savedEdgeReward = bouncer.edgeReward;
            savedCornerReward = bouncer.cornerReward;
            savedBaseSpeed = bouncer.baseSpeed;
            savedStateMultiplier = bouncer.stateMultiplier;
            savedMoneyMultiplier = bouncer.moneyMultiplier;
            savedBouncerDirection = bouncer.GetDirection();
        }

        // 4. Capture UpgradeManager unlocked & equipped tiers
        UpgradeManager upgradeManager = FindObjectOfType<UpgradeManager>();
        if (upgradeManager != null && upgradeManager.upgradePaths != null && upgradeManager.upgradePaths.Count > 0)
        {
            hasSavedUpgrades = true;
            savedUpgradePaths.Clear();
            foreach (UpgradePath path in upgradeManager.upgradePaths)
            {
                if (path == null) continue;
                SavedUpgradePathState pathState = new SavedUpgradePathState
                {
                    pathName = path.pathName,
                    category = path.category,
                    equippedIndex = path.equippedIndex,
                    unlockedIndices = new List<int>(path.unlockedIndices)
                };
                if (!pathState.unlockedIndices.Contains(0))
                {
                    pathState.unlockedIndices.Add(0);
                }
                savedUpgradePaths.Add(pathState);
            }
        }

        // 5. Capture Permanent BuyableBoosterProps in this scene
        CaptureSceneBuyableBoosters(sceneState);

        // 6. Capture Player Grid Position & Rotation in this scene
        GridCrawlerController player = FindObjectOfType<GridCrawlerController>();
        if (player != null)
        {
            sceneState.hasSavedPlayerTransform = true;
            if (player.gridManager != null)
            {
                sceneState.playerPosition = player.gridManager.GetClosestNodePosition(player.transform.position);
            }
            else
            {
                sceneState.playerPosition = player.transform.position;
            }
            sceneState.playerRotation = player.transform.rotation;
        }

        // 7. Capture Trash state if in the Room scene (01Day)
        if (string.Equals(sceneName, roomSceneName, StringComparison.OrdinalIgnoreCase))
        {
            CaptureRoomTrashState(sceneState);
        }

        // 8. Capture Audio settings
        DynamicMusicManager musicMgr = FindObjectOfType<DynamicMusicManager>();
        if (musicMgr != null)
        {
            hasSavedAudioSettings = true;
            savedMusicSliderVolume = musicMgr.musicSliderVolume;
            savedSfxListenerVolume = AudioListener.volume;
        }

        lastActiveGameplayScene = sceneName;
    }

    private void CaptureAndTemporarilyRevertActiveConsumables(string sceneName, SceneSpecificSavedState sceneState)
    {
        savedActiveConsumables.Clear();
        sceneState.consumedActiveBuyablePaths.Clear();

        savedComboState = ActiveSubstanceComboState.None;
        savedComboWasLocked = false;
        savedComboRemainingTimer = 0f;
        savedComboIntensity = 0f;

        if (SubstanceCombinationManager.HasInstance)
        {
            SubstanceCombinationManager comboMgr = SubstanceCombinationManager.Instance;
            savedComboState = comboMgr.GetRuntimeComboStateForSave(
                out savedComboWasLocked,
                out savedComboRemainingTimer,
                out savedComboIntensity);

            // Revert active combo booster before snapshotting BarManager/DVDLogoBouncer stats
            comboMgr.RevertComboBoostersForSceneTransition();
        }

        TimedConsumableBooster[] allConsumables = FindObjectsOfType<TimedConsumableBooster>(false);
        foreach (TimedConsumableBooster consumable in allConsumables)
        {
            if (consumable == null || !consumable.gameObject.activeInHierarchy) continue;

            string buyablePath = string.Empty;
            if (consumable.unusedBuyableProp != null)
            {
                buyablePath = GetHierarchyPath(consumable.unusedBuyableProp.transform);
                if (!string.IsNullOrEmpty(buyablePath))
                {
                    sceneState.consumedActiveBuyablePaths.Add(buyablePath);
                }
            }

            savedActiveConsumables.Add(new SavedActiveConsumableState
            {
                effectType = consumable.effectType,
                remainingHoldDuration = consumable.GetRemainingHoldDurationForSave(),
                currentIntensity = TimedConsumableBooster.GetEffectIntensity(consumable.effectType),
                wasLockedByCombination = consumable.IsLockedByCombination,
                sourceSceneName = sceneName,
                sourceBuyableHierarchyPath = buyablePath
            });

            // Revert the temporary booster modifier on the outgoing scene's BarManager/DVDLogoBouncer
            // so the saved stats represent the clean base + permanent booster values
            if (consumable.activeBoosterObject != null)
            {
                consumable.activeBoosterObject.RevertBooster();
            }
        }
    }

    private void CaptureSceneBuyableBoosters(SceneSpecificSavedState sceneState)
    {
        BuyableBoosterProp[] allBuyables = FindObjectsOfType<BuyableBoosterProp>(true);
        foreach (BuyableBoosterProp buyable in allBuyables)
        {
            if (buyable == null) continue;

            bool isConsumable = buyable.unlockedPropObject != null
                && buyable.unlockedPropObject.GetComponent<TimedConsumableBooster>() != null;

            if (isConsumable) continue;

            // If broken, NEVER record as purchased!
            if (IsPermanentBoosterBroken(buyable.gameObject)
                || (buyable.unlockedPropObject != null && IsPermanentBoosterBroken(buyable.unlockedPropObject)))
            {
                continue;
            }

            // A permanent prop is purchased if it is in Purchased state or its unlockedPropObject is active
            bool isPurchased = buyable.IsPurchased
                || (buyable.unlockedPropObject != null && buyable.unlockedPropObject.activeSelf && !buyable.gameObject.activeSelf);

            if (isPurchased)
            {
                string path = GetHierarchyPath(buyable.transform);
                if (!string.IsNullOrEmpty(path))
                {
                    sceneState.purchasedPermanentBuyablePaths.Add(path);
                }
            }
        }
    }

    private void CaptureRoomTrashState(SceneSpecificSavedState sceneState)
    {
        sceneState.hasSavedTrashState = true;
        sceneState.remainingPreplacedTrashPaths.Clear();
        sceneState.trashManagerStates.Clear();

        HashSet<TrashItem> managerOwnedItems = new HashSet<TrashItem>();
        TrashManager[] managers = FindObjectsOfType<TrashManager>(true);

        foreach (TrashManager mgr in managers)
        {
            if (mgr == null) continue;
            string mgrPath = GetHierarchyPath(mgr.transform);

            SavedTrashManagerState mgrSaved = new SavedTrashManagerState
            {
                managerHierarchyPath = mgrPath
            };

            IReadOnlyList<TrashItem> activeItems = mgr.GetActiveTrashItems();
            for (int i = 0; i < activeItems.Count; i++)
            {
                TrashItem item = activeItems[i];
                if (item == null || item.IsClearedOrPoofing) continue;

                managerOwnedItems.Add(item);

                int prefabIdx = mgr.FindPrefabIndexForInstance(item);
                mgrSaved.remainingItems.Add(new SavedSpawnedTrashEntry
                {
                    prefabIndex = prefabIdx,
                    prefabName = item.gameObject.name,
                    position = item.transform.position,
                    rotation = item.transform.rotation
                });
            }

            sceneState.trashManagerStates[mgrPath] = mgrSaved;
        }

        TrashItem[] allTrashItems = FindObjectsOfType<TrashItem>(false);
        foreach (TrashItem item in allTrashItems)
        {
            if (item == null || item.IsClearedOrPoofing) continue;
            if (managerOwnedItems.Contains(item)) continue;

            string itemPath = GetHierarchyPath(item.transform);
            if (!string.IsNullOrEmpty(itemPath))
            {
                sceneState.remainingPreplacedTrashPaths.Add(itemPath);
            }
        }
    }

    // =========================================================================
    // RESTORING SCENE STATE AFTER SCENE LOAD
    // =========================================================================

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (string.Equals(scene.name, menuSceneName, StringComparison.OrdinalIgnoreCase))
        {
            try
        {
            ResetRunForNewGame();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RunSaveManager] Error in ResetRunForNewGame during death return: {ex}");
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
            return;
        }

        if (!IsGameplayScene(scene.name))
        {
            return;
        }

        RestoreSceneStateImmediate(scene.name);
    }

    public void RestoreSceneStateImmediate(string sceneName)
    {
        if (!IsGameplayScene(sceneName)) return;

        isRestoringSceneState = true;
        try
        {
            SceneSpecificSavedState sceneState = GetOrCreateSceneState(sceneName);

            // 1. Restore Permanent BuyableBoosterProps FIRST and mark their BoosterObjects as already applied
            //    so their OnEnable does not double-apply stat modifiers on top of our saved BarManager/Bouncer stats!
            RestorePermanentBuyableBoosters(sceneState);

            // 2. Restore BarManager current values & modified stat parameters
            BarManager barManager = FindObjectOfType<BarManager>();
            if (barManager != null && hasSavedPlayerBars)
            {
                barManager.maxHunger = savedMaxHunger;
                barManager.maxNeeds = savedMaxNeeds;
                barManager.maxSleep = savedMaxSleep;
                barManager.maxDirtiness = savedMaxDirtiness;
                barManager.maxDepression = savedMaxDepression;

                barManager.hungerFillRate = savedHungerFillRate;
                barManager.sleepFillRate = savedSleepFillRate;
                barManager.baseDepressionFillRate = savedBaseDepressionFillRate;

                barManager.foodHungerReduction = savedFoodHungerReduction;
                barManager.toiletNeedsReduction = savedToiletNeedsReduction;
                barManager.sleepReduction = savedSleepReduction;
                barManager.cleanDirtinessReduction = savedCleanDirtinessReduction;

                barManager.eatDirtinessIncrease = savedEatDirtinessIncrease;
                barManager.toiletDirtinessIncrease = savedToiletDirtinessIncrease;
                barManager.sleepDirtinessIncrease = savedSleepDirtinessIncrease;
                barManager.foodNeedsIncrease = savedFoodNeedsIncrease;

                barManager.trashDirtinessIncrease = savedTrashDirtinessIncrease;
                barManager.trashDepressionReduction = savedTrashDepressionReduction;

                barManager.eatDepressionReduction = savedEatDepressionReduction;
                barManager.toiletDepressionReduction = savedToiletDepressionReduction;
                barManager.sleepDepressionReduction = savedSleepDepressionReduction;
                barManager.cleanDepressionReduction = savedCleanDepressionReduction;

                barManager.baseFoodCost = savedBaseFoodCost;
                barManager.inflationRatePerDollar = savedInflationRatePerDollar;
                barManager.penaltyMultiplier = savedPenaltyMultiplier;

                barManager.currentHunger = Mathf.Clamp(savedHunger, 0f, barManager.maxHunger);
                barManager.currentNeeds = Mathf.Clamp(savedNeeds, 0f, barManager.maxNeeds);
                barManager.currentSleep = Mathf.Clamp(savedSleep, 0f, barManager.maxSleep);
                barManager.currentDirtiness = Mathf.Clamp(savedDirtiness, 0f, barManager.maxDirtiness);
                barManager.currentDepression = Mathf.Clamp(savedDepression, 0f, barManager.maxDepression);
            }

            // 3. Restore DVDLogoBouncer money & stats
            DVDLogoBouncer bouncer = FindObjectOfType<DVDLogoBouncer>();
            if (bouncer != null && hasSavedBouncerState)
            {
                bouncer.currentMoney = savedCurrentMoney;
                bouncer.edgeReward = savedEdgeReward;
                bouncer.cornerReward = savedCornerReward;
                bouncer.baseSpeed = savedBaseSpeed;
                bouncer.stateMultiplier = savedStateMultiplier;
                bouncer.moneyMultiplier = savedMoneyMultiplier;
                if (savedBouncerDirection.sqrMagnitude > 0.01f)
                {
                    bouncer.SetDirection(savedBouncerDirection);
                }
                bouncer.RefreshMoneyUIFromSave();
            }

            // 4. Restore UpgradeManager unlocked & equipped tiers (applied here and in UpgradeManager.Start)
            UpgradeManager upgradeManager = FindObjectOfType<UpgradeManager>();
            if (upgradeManager != null && hasSavedUpgrades)
            {
                ApplySavedUpgradesToManager(upgradeManager);
            }

            // 5. Restore Player Position & Rotation
            RestorePlayerTransform(sceneName, sceneState);

            // 6. Restore or Reset Trash in 01Day
            if (string.Equals(sceneName, roomSceneName, StringComparison.OrdinalIgnoreCase))
            {
                if (resetTrashOnNextRoomLoad)
                {
                    // Waking up from dream: leave pre-placed trash intact and let TrashManagers spawn fresh day trash!
                    sceneState.hasSavedTrashState = false;
                    sceneState.remainingPreplacedTrashPaths.Clear();
                    sceneState.trashManagerStates.Clear();
                    resetTrashOnNextRoomLoad = false;
                }
                else if (sceneState.hasSavedTrashState)
                {
                    RestoreRoomTrashState(sceneState);
                }
            }

            // 7. Restore Active TimedConsumableBoosters & 2-Effect Combinations
            RestoreActiveConsumablesAndCombos(sceneName, sceneState);

            // 8. Restore Audio Settings
            GlobalAudioSettings.ApplyLoadedSettings();
            if (hasSavedAudioSettings)
            {
                AudioListener.volume = Mathf.Clamp01(savedSfxListenerVolume);
                DynamicMusicManager musicMgr = FindObjectOfType<DynamicMusicManager>();
                if (musicMgr != null)
                {
                    musicMgr.SetMusicSliderVolume(savedMusicSliderVolume);
                    musicMgr.SetSfxListenerVolume(savedSfxListenerVolume);
                }
            }

            lastActiveGameplayScene = sceneName;
            isReturningFromDream = false;
        }
        finally
        {
            isRestoringSceneState = false;
        }
    }

    public bool TryGetSavedUpgradePath(UpgradePath path, out SavedUpgradePathState savedState)
    {
        savedState = null;
        if (!hasSavedUpgrades || path == null) return false;

        for (int i = 0; i < savedUpgradePaths.Count; i++)
        {
            SavedUpgradePathState candidate = savedUpgradePaths[i];
            if (candidate == null) continue;

            if (!string.IsNullOrEmpty(path.pathName) &&
                string.Equals(candidate.pathName, path.pathName, StringComparison.OrdinalIgnoreCase))
            {
                savedState = candidate;
                return true;
            }
        }

        for (int i = 0; i < savedUpgradePaths.Count; i++)
        {
            SavedUpgradePathState candidate = savedUpgradePaths[i];
            if (candidate != null && candidate.category == path.category)
            {
                savedState = candidate;
                return true;
            }
        }

        return false;
    }

    public void ApplySavedUpgradesToManager(UpgradeManager upgradeManager)
    {
        if (upgradeManager == null || !hasSavedUpgrades || upgradeManager.upgradePaths == null) return;

        foreach (UpgradePath path in upgradeManager.upgradePaths)
        {
            if (path == null || path.tiers == null || path.tiers.Count == 0) continue;

            if (TryGetSavedUpgradePath(path, out SavedUpgradePathState saved))
            {
                path.unlockedIndices.Clear();
                foreach (int idx in saved.unlockedIndices)
                {
                    if (idx >= 0 && idx < path.tiers.Count && !path.unlockedIndices.Contains(idx))
                    {
                        path.unlockedIndices.Add(idx);
                    }
                }
                if (!path.unlockedIndices.Contains(0))
                {
                    path.unlockedIndices.Add(0);
                }

                path.equippedIndex = Mathf.Clamp(saved.equippedIndex, 0, path.tiers.Count - 1);
            }
        }
    }

        public void RecordPermanentBoosterBroken(GameObject brokenProp)
    {
        if (brokenProp == null) return;

        string activePath = GetHierarchyPath(brokenProp.transform);
        if (!string.IsNullOrEmpty(activePath))
        {
            runWideBrokenPermanentBoosterPaths.Add(activePath);
        }
        runWideBrokenPermanentBoosterPaths.Add(brokenProp.name);

        // Also find and mark the corresponding BuyableBoosterProp as broken
        BuyableBoosterProp[] allBuyables = FindObjectsOfType<BuyableBoosterProp>(true);
        foreach (BuyableBoosterProp buyable in allBuyables)
        {
            if (buyable == null) continue;
            string bPath = GetHierarchyPath(buyable.transform);
            bool isMatching = (buyable.unlockedPropObject == brokenProp)
                || (!string.IsNullOrEmpty(buyable.name) && buyable.name.Replace("_Buyable", "") == brokenProp.name.Replace("_Active", ""));

            if (isMatching)
            {
                if (!string.IsNullOrEmpty(bPath))
                {
                    runWideBrokenPermanentBoosterPaths.Add(bPath);
                }
                runWideBrokenPermanentBoosterPaths.Add(buyable.name);

                // Remove from all purchased sets so it never respawns
                foreach (var s in sceneStates.Values)
                {
                    s.purchasedPermanentBuyablePaths.Remove(bPath);
                    s.purchasedPermanentBuyablePaths.Remove(activePath);
                    if (!string.IsNullOrEmpty(bPath)) s.brokenPermanentBoosterPaths.Add(bPath);
                    if (!string.IsNullOrEmpty(activePath)) s.brokenPermanentBoosterPaths.Add(activePath);
                }
            }
        }
    }

    public bool IsPermanentBoosterBroken(GameObject prop)
    {
        if (prop == null) return false;
        string path = GetHierarchyPath(prop.transform);
        if (runWideBrokenPermanentBoosterPaths.Contains(path) || runWideBrokenPermanentBoosterPaths.Contains(prop.name))
        {
            return true;
        }

        foreach (var s in sceneStates.Values)
        {
            if (s.brokenPermanentBoosterPaths.Contains(path) || s.brokenPermanentBoosterPaths.Contains(prop.name))
            {
                return true;
            }
        }
        return false;
    }

    private void RestorePermanentBuyableBoosters(SceneSpecificSavedState sceneState)
    {
        BuyableBoosterProp[] allBuyables = FindObjectsOfType<BuyableBoosterProp>(true);
        foreach (BuyableBoosterProp buyable in allBuyables)
        {
            if (buyable == null) continue;

            string path = GetHierarchyPath(buyable.transform);
            bool isBroken = IsPermanentBoosterBroken(buyable.gameObject)
                || (buyable.unlockedPropObject != null && IsPermanentBoosterBroken(buyable.unlockedPropObject));

            if (isBroken)
            {
                // Permanent booster was broken earlier in this run: keep it GONE!
                buyable.gameObject.SetActive(false);
                if (buyable.unlockedPropObject != null)
                {
                    buyable.unlockedPropObject.SetActive(false);
                }
                continue;
            }

            if (sceneState.purchasedPermanentBuyablePaths.Contains(path))
            {
                buyable.RestoreAsPurchasedFromSave(applyBoosterStats: false);
            }
        }

        // Also ensure any standalone BreakablePermanentBooster in the scene is disabled if broken
        BreakablePermanentBooster[] allBreakables = FindObjectsOfType<BreakablePermanentBooster>(true);
        foreach (BreakablePermanentBooster breakable in allBreakables)
        {
            if (breakable == null) continue;
            if (IsPermanentBoosterBroken(breakable.gameObject))
            {
                breakable.gameObject.SetActive(false);
            }
        }
    }

    private void RestorePlayerTransform(string sceneName, SceneSpecificSavedState sceneState)
    {
        if (!sceneState.hasSavedPlayerTransform) return;

        GridCrawlerController player = FindObjectOfType<GridCrawlerController>();
        if (player == null) return;

        Vector3 targetPos = sceneState.playerPosition;
        if (player.gridManager != null)
        {
            targetPos = player.gridManager.GetClosestNodePosition(targetPos);
        }
        player.transform.position = targetPos;

        // If returning from the Bar to the Room (or Room to Bar), or waking up from the Dream Sequence,
        // turn 180 degrees from the interact rotation so the player faces into the room/bar rather than staring into the door/bed
        Quaternion targetRot = sceneState.playerRotation;
        bool isReturningFromOtherGameplayScene = !string.IsNullOrEmpty(lastActiveGameplayScene) &&
            !string.Equals(lastActiveGameplayScene, sceneName, StringComparison.OrdinalIgnoreCase);
        bool isWakingUp = resetTrashOnNextRoomLoad || isReturningFromDream;

        if (isReturningFromOtherGameplayScene || isWakingUp)
        {
            targetRot = sceneState.playerRotation * Quaternion.Euler(0f, 180f, 0f);
        }
        player.transform.rotation = targetRot;

        // Immediately snap the camera to the player's restored transform so the transition-in animation
        // plays at the restored position instead of the scene's default spawn point!
        SmoothCameraFollow camFollow = Camera.main != null ? Camera.main.GetComponent<SmoothCameraFollow>() : FindObjectOfType<SmoothCameraFollow>();
        if (camFollow != null)
        {
            camFollow.target = player.transform;
            camFollow.SnapToTargetImmediate();
        }
        else if (Camera.main != null)
        {
            Camera.main.transform.position = targetPos + new Vector3(0f, 1.5f, 0f);
            Camera.main.transform.rotation = targetRot;
        }
    }

    public bool ShouldTrashManagerSkipInitialSpawn(TrashManager manager)
    {
        if (manager == null) return false;
        string sceneName = manager.gameObject.scene.name;
        if (!string.Equals(sceneName, roomSceneName, StringComparison.OrdinalIgnoreCase)) return false;
        if (resetTrashOnNextRoomLoad) return false;

        if (sceneStates.TryGetValue(roomSceneName, out SceneSpecificSavedState roomState))
        {
            return roomState.hasSavedTrashState;
        }
        return false;
    }

    private void RestoreRoomTrashState(SceneSpecificSavedState sceneState)
    {
        // 1. Restore dynamically spawned TrashManager items
        HashSet<TrashItem> restoredManagerItems = new HashSet<TrashItem>();
        TrashManager[] managers = FindObjectsOfType<TrashManager>(true);
        foreach (TrashManager mgr in managers)
        {
            if (mgr == null) continue;
            string mgrPath = GetHierarchyPath(mgr.transform);
            if (sceneState.trashManagerStates.TryGetValue(mgrPath, out SavedTrashManagerState savedMgrState))
            {
                mgr.RestoreTrashFromSave(savedMgrState.remainingItems, restoredManagerItems);
            }
        }

        // 2. Remove pre-placed TrashItems that were already cleaned earlier in the day
        TrashItem[] allTrashItems = FindObjectsOfType<TrashItem>(false);
        foreach (TrashItem item in allTrashItems)
        {
            if (item == null || restoredManagerItems.Contains(item)) continue;

            string itemPath = GetHierarchyPath(item.transform);
            if (!sceneState.remainingPreplacedTrashPaths.Contains(itemPath))
            {
                Destroy(item.gameObject);
            }
        }
    }

    private void RestoreActiveConsumablesAndCombos(string sceneName, SceneSpecificSavedState sceneState)
    {
        if (savedActiveConsumables.Count == 0) return;

        TimedConsumableBooster[] sceneConsumables = FindObjectsOfType<TimedConsumableBooster>(true);
        List<TimedConsumableBooster> restoredBoosters = new List<TimedConsumableBooster>();

        foreach (SavedActiveConsumableState saved in savedActiveConsumables)
        {
            if (saved == null) continue;

            TimedConsumableBooster matchedBooster = null;

            // First try matching by BuyableBoosterProp path if we're in the same scene where it was bought
            if (string.Equals(saved.sourceSceneName, sceneName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(saved.sourceBuyableHierarchyPath))
            {
                foreach (TimedConsumableBooster candidate in sceneConsumables)
                {
                    if (candidate == null || restoredBoosters.Contains(candidate)) continue;
                    candidate.EnsureReferencesForSave();
                    if (candidate.unusedBuyableProp != null &&
                        string.Equals(GetHierarchyPath(candidate.unusedBuyableProp.transform), saved.sourceBuyableHierarchyPath, StringComparison.Ordinal))
                    {
                        matchedBooster = candidate;
                        break;
                    }
                }
            }

            // Otherwise match by ConsumableVisualEffectType in the target scene
            if (matchedBooster == null)
            {
                foreach (TimedConsumableBooster candidate in sceneConsumables)
                {
                    if (candidate == null || restoredBoosters.Contains(candidate)) continue;
                    if (candidate.effectType == saved.effectType)
                    {
                        matchedBooster = candidate;
                        break;
                    }
                }
            }

            if (matchedBooster != null)
            {
                restoredBoosters.Add(matchedBooster);
                bool hideBuyableInThisScene = string.Equals(saved.sourceSceneName, sceneName, StringComparison.OrdinalIgnoreCase);
                matchedBooster.RestoreFromCrossSceneSave(
                    saved.remainingHoldDuration,
                    saved.currentIntensity,
                    hideBuyableInThisScene);
            }
        }

        if (savedComboWasLocked &&
            savedComboState != ActiveSubstanceComboState.None &&
            SubstanceCombinationManager.HasInstance)
        {
            SubstanceCombinationManager.Instance.RestoreTwoEffectComboFromSave(
                savedComboState,
                savedComboRemainingTimer,
                savedComboIntensity);
        }
    }

    private SceneSpecificSavedState GetOrCreateSceneState(string sceneName)
    {
        if (!sceneStates.TryGetValue(sceneName, out SceneSpecificSavedState state))
        {
            state = new SceneSpecificSavedState();
            sceneStates[sceneName] = state;
        }
        return state;
    }

    public static string GetHierarchyPath(Transform t)
    {
        if (t == null) return string.Empty;
        string path = t.name;
        Transform current = t.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }
}
