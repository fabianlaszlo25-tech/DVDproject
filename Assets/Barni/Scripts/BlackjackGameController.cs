using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class BlackjackGameController : MonoBehaviour
{
    [Header("Core Subsystems")]
    public BlackjackBalanceManager balanceManager;
    public BlackjackTableVisuals tableVisuals;
    public TableCameraZoomController cameraZoomController;

    [Header("Interactive 3D Cube Controls")]
    public List<BlackjackInteractiveElement> chipElements = new List<BlackjackInteractiveElement>();
    public BlackjackInteractiveElement dealButton;
    public BlackjackInteractiveElement hitButton;
    public BlackjackInteractiveElement standButton;
    public BlackjackInteractiveElement clearBetButton;
    public BlackjackInteractiveElement leaveTableButton;

    [Header("Bar Scene Integration")]
    public GridCrawlerController playerController;
    public SmoothCameraFollow smoothCamera;
    [Tooltip("If true, hides the screen-edge turn arrows while actively seated at the table so bottom chips are unobstructed, and restores them on Leave Table.")]
    public bool hideTurnArrowsWhileSeated = true;
    [Tooltip("Automatically runs unit self-tests for Blackjack rules on Awake in the Editor.")]
    public bool runLogicSelfTestsOnAwake = true;

    [Header("Timing Settings")]
    public float initialDealStepDelay = 0.14f;
    public float dealerActionDelay = 0.45f;

    public BlackjackLogicEngine Engine { get; private set; }
    public BlackjackGameState CurrentState => Engine != null ? Engine.State : BlackjackGameState.Idle;
    public bool IsTableSessionActive { get; private set; }
    public bool IsBusyAnimating => isBusyAnimating;

    private bool isBusyAnimating;
    private int currentRoundId;
    private decimal committedRoundBet;
    private string currentStatusBanner = "BLACKJACK - PAYS 3 TO 2";
    private string currentActionPrompt = "CLICK ANY CHIP OR DEAL TO JOIN TABLE";

    private class SavedArrowState
    {
        public MovementArrowUI arrow;
        public Vector2 originalPadding;
        public bool wasActive;
    }

    private readonly List<SavedArrowState> suppressedArrows = new List<SavedArrowState>();
    private Vector3 seatedPlayerForward;
    private bool hasRecordedSeatedForward;

    private void Awake()
    {
        Engine = new BlackjackLogicEngine();

        if (runLogicSelfTestsOnAwake)
        {
            if (!BlackjackLogicEngine.RunSelfTests(out string testSummary))
            {
                Debug.LogWarning("[Blackjack] Self-test warning:\n" + testSummary);
            }
        }

        CasinoSceneAutoBuilder.EnsureCasinoTablesInActiveScene(false);
        EnsureSubsystemsAndReferences();
    }

    private void Start()
    {
        EnsureSubsystemsAndReferences();
        if (balanceManager != null)
        {
            balanceManager.EnsureInitialized();
        }

        EnterIdleBarState(refundStagedBet: false);
    }

    private bool lastZoomedState;

    private void Update()
    {
        bool currentlyZoomed = cameraZoomController != null && cameraZoomController.IsZoomedIn;
        if (currentlyZoomed != lastZoomedState)
        {
            lastZoomedState = currentlyZoomed;
            if (!isBusyAnimating && leaveTableButton != null)
            {
                BlackjackGameState st = Engine.State;
                bool canLeaveNow = (IsTableSessionActive || currentlyZoomed) &&
                                   (st == BlackjackGameState.Betting || st == BlackjackGameState.RoundOver || st == BlackjackGameState.Idle);
                leaveTableButton.SetElementEnabled(canLeaveNow, "Leave Blackjack Table (Zoom Out)", "LEAVE\nTABLE");
            }
        }

        if (!IsTableSessionActive || isBusyAnimating) return;

        // If the player turned away or moved away from the table using keyboard WASD,
        // gracefully exit the table session and restore normal bar UI/navigation.
        if (playerController != null)
        {
            float dist = Vector3.Distance(playerController.transform.position, transform.position);
            bool movedTooFar = dist > (playerController.interactReach + 0.65f);

            bool turnedAway = false;
            if (hasRecordedSeatedForward)
            {
                float angle = Vector3.Angle(playerController.transform.forward, seatedPlayerForward);
                if (angle > 65f)
                {
                    turnedAway = true;
                }
            }

            if ((movedTooFar || turnedAway) &&
                (Engine.State == BlackjackGameState.Idle ||
                 Engine.State == BlackjackGameState.Betting ||
                 Engine.State == BlackjackGameState.RoundOver))
            {
                ExitTableSession();
            }
        }
    }

    private void OnDisable()
    {
        // Safely handle interrupted interactions or scene unload
        if (Engine != null && Engine.State == BlackjackGameState.Betting && Engine.CurrentBet > 0m && balanceManager != null)
        {
            decimal staged = Engine.ClearStagedBet();
            balanceManager.RefundStagedBet(staged);
        }

        RestoreBarNavigationArrows();
        IsTableSessionActive = false;
        isBusyAnimating = false;
    }

    private void EnsureSubsystemsAndReferences()
    {
        if (balanceManager == null)
        {
            balanceManager = GetComponent<BlackjackBalanceManager>();
            if (balanceManager == null) balanceManager = gameObject.AddComponent<BlackjackBalanceManager>();
        }

        if (tableVisuals == null)
        {
            tableVisuals = GetComponent<BlackjackTableVisuals>();
            if (tableVisuals == null) tableVisuals = gameObject.AddComponent<BlackjackTableVisuals>();
        }

        if (cameraZoomController == null)
        {
            cameraZoomController = GetComponent<TableCameraZoomController>();
            if (cameraZoomController == null) cameraZoomController = gameObject.AddComponent<TableCameraZoomController>();
        }

        if (playerController == null)
        {
            playerController = FindFirstObjectByType<GridCrawlerController>();
        }

        if (smoothCamera == null)
        {
            smoothCamera = FindFirstObjectByType<SmoothCameraFollow>();
        }

        // Auto-collect interactive elements if not explicitly assigned in Inspector
        if (chipElements == null || chipElements.Count == 0 || dealButton == null || hitButton == null || standButton == null)
        {
            BlackjackInteractiveElement[] allElements = GetComponentsInChildren<BlackjackInteractiveElement>(true);
            if (chipElements == null) chipElements = new List<BlackjackInteractiveElement>();
            chipElements.Clear();

            for (int i = 0; i < allElements.Length; i++)
            {
                BlackjackInteractiveElement elem = allElements[i];
                if (elem == null) continue;
                elem.gameController = this;

                switch (elem.actionType)
                {
                    case BlackjackInteractiveElement.ElementActionType.BetChip:
                        chipElements.Add(elem);
                        break;
                    case BlackjackInteractiveElement.ElementActionType.DealOrNewRound:
                        if (dealButton == null) dealButton = elem;
                        break;
                    case BlackjackInteractiveElement.ElementActionType.Hit:
                        if (hitButton == null) hitButton = elem;
                        break;
                    case BlackjackInteractiveElement.ElementActionType.Stand:
                        if (standButton == null) standButton = elem;
                        break;
                    case BlackjackInteractiveElement.ElementActionType.ClearBet:
                        if (clearBetButton == null) clearBetButton = elem;
                        break;
                    case BlackjackInteractiveElement.ElementActionType.LeaveTable:
                        if (leaveTableButton == null) leaveTableButton = elem;
                        break;
                }
            }
        }
    }

    public void OnInteractiveElementClicked(BlackjackInteractiveElement element)
    {
        if (element == null || isBusyAnimating) return;

        switch (element.actionType)
        {
            case BlackjackInteractiveElement.ElementActionType.BetChip:
                HandleChipClicked((decimal)Mathf.RoundToInt(element.chipDenomination));
                break;

            case BlackjackInteractiveElement.ElementActionType.DealOrNewRound:
                HandleDealOrNewRoundClicked();
                break;

            case BlackjackInteractiveElement.ElementActionType.Hit:
                HandleHitClicked();
                break;

            case BlackjackInteractiveElement.ElementActionType.Stand:
                HandleStandClicked();
                break;

            case BlackjackInteractiveElement.ElementActionType.ClearBet:
                HandleClearBetClicked();
                break;

            case BlackjackInteractiveElement.ElementActionType.LeaveTable:
                ExitTableSession();
                break;

            case BlackjackInteractiveElement.ElementActionType.JoinTable:
                EnterTableBettingSession();
                break;
        }
    }

    public void EnterTableBettingSession()
    {
        if (isBusyAnimating) return;

        ActivateTableSessionLock();

        if (Engine.State == BlackjackGameState.Idle || Engine.State == BlackjackGameState.RoundOver)
        {
            if (tableVisuals != null)
            {
                tableVisuals.ClearTableCards();
                tableVisuals.UpdateBetPotChips(0m);
            }
            Engine.EnterBettingState();
        }

        if (balanceManager.CurrentBalance < 1m && Engine.CurrentBet <= 0m)
        {
            currentStatusBanner = "OUT OF FUNDS - HOUSE GRANT +$50";
            balanceManager.RefundStagedBet(50m);
            currentActionPrompt = "PLACE YOUR BET WITH CHIPS, THEN CLICK DEAL";
        }
        else
        {
            currentStatusBanner = "PLACE YOUR BET";
            currentActionPrompt = Engine.CurrentBet > 0m
                ? "ADD MORE CHIPS, CLEAR, OR CLICK DEAL"
                : "CLICK 3D CHIPS ($1-$50) TO BET";
        }

        RefreshAllVisualsAndButtons();
    }

    public void ExitTableSession()
    {
        if (isBusyAnimating) return;
        // Do not allow abandoning mid-hand during PlayerTurn/DealerTurn; only in Idle, Betting, or RoundOver
        if (Engine.State == BlackjackGameState.PlayerTurn || Engine.State == BlackjackGameState.DealerTurn)
        {
            return;
        }

        if (cameraZoomController != null)
        {
            cameraZoomController.RequestSessionZoomOutAndSuppressAuto();
        }

        EnterIdleBarState(refundStagedBet: true);
    }

    private void EnterIdleBarState(bool refundStagedBet)
    {
        if (refundStagedBet && Engine.State == BlackjackGameState.Betting && Engine.CurrentBet > 0m && balanceManager != null)
        {
            decimal staged = Engine.ClearStagedBet();
            balanceManager.RefundStagedBet(staged);
        }

        Engine.ReturnToIdleState();
        IsTableSessionActive = false;
        hasRecordedSeatedForward = false;
        RestoreBarNavigationArrows();

        if (tableVisuals != null)
        {
            tableVisuals.ClearTableCards();
            tableVisuals.UpdateBetPotChips(0m);
        }

        currentStatusBanner = "BLACKJACK - PAYS 3 TO 2";
        currentActionPrompt = "CLICK ANY CHIP OR DEAL TO PLAY";
        RefreshAllVisualsAndButtons();
    }

    private void HandleChipClicked(decimal chipAmount)
    {
        if (isBusyAnimating) return;
        if (chipAmount <= 0m) return;

        if (Engine.State != BlackjackGameState.Betting &&
            Engine.State != BlackjackGameState.Idle &&
            Engine.State != BlackjackGameState.RoundOver)
        {
            return;
        }

        ActivateTableSessionLock();

        if (Engine.State == BlackjackGameState.Idle || Engine.State == BlackjackGameState.RoundOver)
        {
            if (tableVisuals != null)
            {
                tableVisuals.ClearTableCards();
                tableVisuals.UpdateBetPotChips(0m);
            }
            Engine.EnterBettingState();
        }

        decimal availableBalance = balanceManager != null ? balanceManager.CurrentBalance : 0m;
        if (chipAmount > availableBalance)
        {
            currentStatusBanner = $"INSUFFICIENT BALANCE FOR ${chipAmount:0}";
            currentActionPrompt = availableBalance >= 1m
                ? "CHOOSE A SMALLER CHIP OR CLICK DEAL"
                : (Engine.CurrentBet > 0m ? "ALL IN! CLICK DEAL TO START HAND" : "INSUFFICIENT FUNDS");
            RefreshAllVisualsAndButtons();
            return;
        }

        if (balanceManager != null && balanceManager.TryDeductForBet(chipAmount))
        {
            Engine.TryAddBet(chipAmount, availableBalance);
            if (tableVisuals != null)
            {
                tableVisuals.UpdateBetPotChips(Engine.CurrentBet);
            }

            currentStatusBanner = $"BET STAGED: ${Engine.CurrentBet:0.00}";
            currentActionPrompt = "CLICK DEAL TO START OR ADD MORE CHIPS";
            RefreshAllVisualsAndButtons();
        }
    }

    private void HandleClearBetClicked()
    {
        if (isBusyAnimating) return;
        if (Engine.State != BlackjackGameState.Betting || Engine.CurrentBet <= 0m) return;

        decimal refunded = Engine.ClearStagedBet();
        if (balanceManager != null && refunded > 0m)
        {
            balanceManager.RefundStagedBet(refunded);
        }

        if (tableVisuals != null)
        {
            tableVisuals.UpdateBetPotChips(0m);
        }

        currentStatusBanner = "BET CLEARED";
        currentActionPrompt = "SELECT CHIPS TO PLACE A NEW BET";
        RefreshAllVisualsAndButtons();
    }

    private void HandleDealOrNewRoundClicked()
    {
        if (isBusyAnimating) return;

        if (Engine.State == BlackjackGameState.Idle || Engine.State == BlackjackGameState.RoundOver)
        {
            EnterTableBettingSession();
            return;
        }

        if (Engine.State == BlackjackGameState.Betting)
        {
            if (!Engine.CanDealRound())
            {
                currentStatusBanner = "PLACE A BET FIRST!";
                currentActionPrompt = "CLICK A CHIP ($1, $5, $10, $25, $50) BEFORE DEALING";
                RefreshAllVisualsAndButtons();
                return;
            }

            StartCoroutine(DealInitialHandRoutine());
        }
    }

    private void HandleHitClicked()
    {
        if (isBusyAnimating) return;
        if (!Engine.CanPlayerHit()) return;

        StartCoroutine(PlayerHitRoutine());
    }

    private void HandleStandClicked()
    {
        if (isBusyAnimating) return;
        if (!Engine.CanPlayerStand()) return;

        StartCoroutine(DealerTurnRoutine());
    }

    private IEnumerator DealInitialHandRoutine()
    {
        isBusyAnimating = true;
        ActivateTableSessionLock();

        committedRoundBet = Engine.CurrentBet;
        currentRoundId++;

        if (tableVisuals != null)
        {
            tableVisuals.ClearTableCards();
            tableVisuals.UpdateBetPotChips(committedRoundBet);
        }

        bool started = Engine.StartRound(reshuffleIfLow: true);
        if (!started)
        {
            isBusyAnimating = false;
            RefreshAllVisualsAndButtons();
            yield break;
        }

        currentStatusBanner = "DEALING CARDS...";
        currentActionPrompt = "PLEASE WAIT";
        DisableAllInteractiveControlsDuringAnimation();
        if (tableVisuals != null)
        {
            tableVisuals.RefreshTextDisplays(Engine, balanceManager.CurrentBalance, currentStatusBanner, currentActionPrompt);
        }

        // Deal 4 cards in standard order: Player 0, Dealer 0 (up), Player 1, Dealer 1 (face-down hole card)
        if (tableVisuals != null)
        {
            yield return StartCoroutine(tableVisuals.DealCardAnimatedRoutine(Engine.PlayerHand.Cards[0], toDealer: false, faceDown: false));
            yield return new WaitForSeconds(initialDealStepDelay);

            yield return StartCoroutine(tableVisuals.DealCardAnimatedRoutine(Engine.DealerHand.Cards[0], toDealer: true, faceDown: false));
            yield return new WaitForSeconds(initialDealStepDelay);

            yield return StartCoroutine(tableVisuals.DealCardAnimatedRoutine(Engine.PlayerHand.Cards[1], toDealer: false, faceDown: false));
            yield return new WaitForSeconds(initialDealStepDelay);

            yield return StartCoroutine(tableVisuals.DealCardAnimatedRoutine(Engine.DealerHand.Cards[1], toDealer: true, faceDown: true));
        }

        // Check for immediate Natural Blackjack (Player or Dealer has 21 with 2 cards)
        if (Engine.CheckAndResolveNaturalBlackjack())
        {
            if (tableVisuals != null)
            {
                yield return new WaitForSeconds(0.20f);
                yield return StartCoroutine(tableVisuals.RevealDealerHoleCardRoutine());
            }

            ApplyResolvedRoundOutcomeAndPayout();
            isBusyAnimating = false;
            RefreshAllVisualsAndButtons();
            yield break;
        }

        int playerScore = Engine.PlayerHand.CalculateValue(out bool isSoft);
        currentStatusBanner = $"YOUR TURN - HAND: {playerScore}{(isSoft ? " (SOFT)" : "")}";
        currentActionPrompt = "CLICK HIT FOR ANOTHER CARD OR STAND TO HOLD";

        isBusyAnimating = false;
        RefreshAllVisualsAndButtons();
    }

    private IEnumerator PlayerHitRoutine()
    {
        isBusyAnimating = true;
        DisableAllInteractiveControlsDuringAnimation();

        currentStatusBanner = "HITTING...";
        currentActionPrompt = "DEALING CARD";

        BlackjackCard drawnCard = Engine.PlayerHit(out bool playerBusted, out bool reachedTwentyOne);

        if (tableVisuals != null)
        {
            yield return StartCoroutine(tableVisuals.DealCardAnimatedRoutine(drawnCard, toDealer: false, faceDown: false));
        }

        if (playerBusted)
        {
            if (tableVisuals != null)
            {
                yield return new WaitForSeconds(0.18f);
                yield return StartCoroutine(tableVisuals.RevealDealerHoleCardRoutine());
            }

            ApplyResolvedRoundOutcomeAndPayout();
            isBusyAnimating = false;
            RefreshAllVisualsAndButtons();
            yield break;
        }

        if (reachedTwentyOne)
        {
            // Automatically transition to dealer turn when player hits to 21
            currentStatusBanner = "21! STANDING AUTOMATICALLY...";
            currentActionPrompt = "DEALER'S TURN";
            if (tableVisuals != null)
            {
                tableVisuals.RefreshTextDisplays(Engine, balanceManager.CurrentBalance, currentStatusBanner, currentActionPrompt);
            }
            yield return new WaitForSeconds(0.35f);
            yield return StartCoroutine(DealerTurnRoutine());
            yield break;
        }

        int playerScore = Engine.PlayerHand.CalculateValue(out bool isSoft);
        currentStatusBanner = $"YOUR TURN - HAND: {playerScore}{(isSoft ? " (SOFT)" : "")}";
        currentActionPrompt = "CLICK HIT OR STAND";

        isBusyAnimating = false;
        RefreshAllVisualsAndButtons();
    }

    private IEnumerator DealerTurnRoutine()
    {
        isBusyAnimating = true;
        DisableAllInteractiveControlsDuringAnimation();

        if (!Engine.BeginDealerTurn())
        {
            isBusyAnimating = false;
            RefreshAllVisualsAndButtons();
            yield break;
        }

        currentStatusBanner = "DEALER'S TURN - REVEALING HOLE CARD";
        currentActionPrompt = "DEALER MUST HIT BELOW 17 & STAND ON 17+";
        if (tableVisuals != null)
        {
            tableVisuals.RefreshTextDisplays(Engine, balanceManager.CurrentBalance, currentStatusBanner, currentActionPrompt);
            yield return StartCoroutine(tableVisuals.RevealDealerHoleCardRoutine());
            tableVisuals.RefreshTextDisplays(Engine, balanceManager.CurrentBalance, currentStatusBanner, currentActionPrompt);
        }

        yield return new WaitForSeconds(dealerActionDelay);

        while (Engine.ShouldDealerHit())
        {
            int currentDealerScore = Engine.DealerHand.CalculateValue();
            currentStatusBanner = $"DEALER HAS {currentDealerScore} (< 17) - HITTING";
            if (tableVisuals != null)
            {
                tableVisuals.RefreshTextDisplays(Engine, balanceManager.CurrentBalance, currentStatusBanner, currentActionPrompt);
            }

            BlackjackCard dealerCard = Engine.DealerHitStep();
            if (tableVisuals != null)
            {
                yield return StartCoroutine(tableVisuals.DealCardAnimatedRoutine(dealerCard, toDealer: true, faceDown: false));
                tableVisuals.RefreshTextDisplays(Engine, balanceManager.CurrentBalance, currentStatusBanner, currentActionPrompt);
            }

            yield return new WaitForSeconds(dealerActionDelay);
        }

        Engine.FinalizeDealerTurnAndResolve();
        ApplyResolvedRoundOutcomeAndPayout();

        isBusyAnimating = false;
        RefreshAllVisualsAndButtons();
    }

    private void ApplyResolvedRoundOutcomeAndPayout()
    {
        if (balanceManager != null)
        {
            balanceManager.TryApplyRoundPayout(currentRoundId, committedRoundBet, Engine.LastPayout, Engine.LastOutcome);
        }

        if (tableVisuals != null)
        {
            tableVisuals.UpdateBetPotChips(Engine.LastPayout);
        }

        int playerVal = Engine.PlayerHand.CalculateValue();
        int dealerVal = Engine.DealerHand.CalculateValue();

        switch (Engine.LastOutcome)
        {
            case BlackjackRoundOutcome.PlayerBlackjack:
                currentStatusBanner = $"BLACKJACK! YOU WIN +${Engine.LastNetDelta:0.00} (PAYOUT ${Engine.LastPayout:0.00})";
                break;
            case BlackjackRoundOutcome.DealerBust:
                currentStatusBanner = $"DEALER BUSTS ({dealerVal})! YOU WIN +${Engine.LastNetDelta:0.00}";
                break;
            case BlackjackRoundOutcome.PlayerWin:
                currentStatusBanner = $"YOU WIN ({playerVal} VS {dealerVal})! +${Engine.LastNetDelta:0.00}";
                break;
            case BlackjackRoundOutcome.Push:
                currentStatusBanner = $"PUSH ({playerVal} TIED {dealerVal}) - BET RETURNED (${Engine.LastPayout:0.00})";
                break;
            case BlackjackRoundOutcome.PlayerBust:
                currentStatusBanner = $"BUST ({playerVal})! YOU LOSE ${committedRoundBet:0.00}";
                break;
            case BlackjackRoundOutcome.DealerBlackjack:
                currentStatusBanner = $"DEALER BLACKJACK! YOU LOSE ${committedRoundBet:0.00}";
                break;
            case BlackjackRoundOutcome.DealerWin:
                currentStatusBanner = $"DEALER WINS ({dealerVal} VS {playerVal}) - LOST ${committedRoundBet:0.00}";
                break;
            default:
                currentStatusBanner = "ROUND COMPLETE";
                break;
        }

        currentActionPrompt = "CLICK ANY CHIP TO BET AGAIN, NEW ROUND, OR LEAVE";
    }

    private void DisableAllInteractiveControlsDuringAnimation()
    {
        if (chipElements != null)
        {
            for (int i = 0; i < chipElements.Count; i++)
            {
                if (chipElements[i] != null) chipElements[i].SetElementEnabled(false);
            }
        }

        if (dealButton != null) dealButton.SetElementEnabled(false);
        if (hitButton != null) hitButton.SetElementEnabled(false);
        if (standButton != null) standButton.SetElementEnabled(false);
        if (clearBetButton != null) clearBetButton.SetElementEnabled(false);
        if (leaveTableButton != null) leaveTableButton.SetElementEnabled(false);
    }

    public void RefreshAllVisualsAndButtons()
    {
        decimal balance = balanceManager != null ? balanceManager.CurrentBalance : 0m;

        if (tableVisuals != null)
        {
            tableVisuals.RefreshTextDisplays(Engine, balance, currentStatusBanner, currentActionPrompt);
        }

        if (isBusyAnimating)
        {
            DisableAllInteractiveControlsDuringAnimation();
            return;
        }

        BlackjackGameState state = Engine.State;
        bool canBetState = (state == BlackjackGameState.Idle || state == BlackjackGameState.Betting || state == BlackjackGameState.RoundOver);

        if (chipElements != null)
        {
            for (int i = 0; i < chipElements.Count; i++)
            {
                BlackjackInteractiveElement chip = chipElements[i];
                if (chip == null) continue;

                decimal denom = (decimal)Mathf.RoundToInt(chip.chipDenomination);
                bool canAffordChip = canBetState && balance >= denom;
                string prompt = canAffordChip
                    ? $"Bet +${denom:0} Chip"
                    : $"Need ${denom:0} (Balance: ${balance:0.00})";
                chip.SetElementEnabled(canAffordChip, prompt, $"${denom:0}");
            }
        }

        if (dealButton != null)
        {
            if (state == BlackjackGameState.Idle)
            {
                dealButton.SetElementEnabled(true, "Sit / Join Blackjack Table", "JOIN\nTABLE");
            }
            else if (state == BlackjackGameState.Betting)
            {
                bool canDeal = Engine.CanDealRound();
                dealButton.SetElementEnabled(canDeal, canDeal ? $"Deal Hand (Bet: ${Engine.CurrentBet:0})" : "Place Bet First", "DEAL\nCARDS");
            }
            else if (state == BlackjackGameState.RoundOver)
            {
                dealButton.SetElementEnabled(true, "Start New Betting Round", "NEW\nROUND");
            }
            else
            {
                dealButton.SetElementEnabled(false, "Hand in Progress", "DEAL\nCARDS");
            }
        }

        if (hitButton != null)
        {
            bool canHit = Engine.CanPlayerHit();
            hitButton.SetElementEnabled(canHit, "Hit (Draw Card)", "HIT");
        }

        if (standButton != null)
        {
            bool canStand = Engine.CanPlayerStand();
            standButton.SetElementEnabled(canStand, "Stand (Hold Hand)", "STAND");
        }

        if (clearBetButton != null)
        {
            bool canClear = state == BlackjackGameState.Betting && Engine.CurrentBet > 0m;
            clearBetButton.SetElementEnabled(canClear, $"Clear Staged Bet (${Engine.CurrentBet:0})", "CLEAR\nBET");
        }

        if (leaveTableButton != null)
        {
            bool isZoomed = cameraZoomController != null && cameraZoomController.IsZoomedIn;
            bool canLeave = (IsTableSessionActive || isZoomed) &&
                            (state == BlackjackGameState.Betting || state == BlackjackGameState.RoundOver || state == BlackjackGameState.Idle);
            leaveTableButton.SetElementEnabled(canLeave, "Leave Blackjack Table (Zoom Out)", "LEAVE\nTABLE");
        }
    }

    private void ActivateTableSessionLock()
    {
        if (cameraZoomController != null)
        {
            cameraZoomController.RequestSessionZoomIn();
        }

        if (IsTableSessionActive) return;
        IsTableSessionActive = true;

        if (playerController != null)
        {
            seatedPlayerForward = playerController.transform.forward;
            hasRecordedSeatedForward = true;
        }

        if (!hideTurnArrowsWhileSeated) return;

        suppressedArrows.Clear();
        MovementArrowUI[] arrows = FindObjectsByType<MovementArrowUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < arrows.Length; i++)
        {
            MovementArrowUI arrow = arrows[i];
            if (arrow == null) continue;

            suppressedArrows.Add(new SavedArrowState
            {
                arrow = arrow,
                originalPadding = arrow.hitboxPadding,
                wasActive = arrow.gameObject.activeSelf
            });

            // Collapse hitbox padding so GridCrawlerController's manual Rect.Contains check
            // will never intercept mouse clicks aimed at bottom-row Blackjack chips/buttons.
            arrow.hitboxPadding = new Vector2(-10000f, -10000f);
            arrow.SetHoverState(false);
            arrow.gameObject.SetActive(false);
        }
    }

    private void RestoreBarNavigationArrows()
    {
        for (int i = 0; i < suppressedArrows.Count; i++)
        {
            SavedArrowState saved = suppressedArrows[i];
            if (saved != null && saved.arrow != null)
            {
                saved.arrow.hitboxPadding = saved.originalPadding;
                saved.arrow.gameObject.SetActive(saved.wasActive);
                saved.arrow.SetHoverState(false);
            }
        }
        suppressedArrows.Clear();
    }
}
