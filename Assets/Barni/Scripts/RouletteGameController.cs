using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RouletteGameController : MonoBehaviour
{
    [Header("Core Subsystems")]
    public BlackjackBalanceManager balanceManager;
    public RouletteTableVisuals tableVisuals;
    public TableCameraZoomController cameraZoomController;

    [Header("Interactive 3D Cube Controls")]
    public List<RouletteInteractiveElement> chipSelectorElements = new List<RouletteInteractiveElement>();
    public List<RouletteInteractiveElement> boardBetElements = new List<RouletteInteractiveElement>();
    public RouletteInteractiveElement spinButton;
    public RouletteInteractiveElement clearBetsButton;
    public RouletteInteractiveElement leaveTableButton;

    [Header("Bar Scene Integration")]
    public GridCrawlerController playerController;
    public SmoothCameraFollow smoothCamera;
    public bool runLogicSelfTestsOnAwake = true;

    public RouletteLogicEngine Engine { get; private set; }
    public RouletteGameState CurrentState => Engine != null ? Engine.State : RouletteGameState.Idle;
    public bool IsTableSessionActive { get; private set; }
    public bool IsBusyAnimating => isBusyAnimating;
    public decimal SelectedChipDenomination => selectedChipDenomination;

    private decimal selectedChipDenomination = 5m;
    private bool isBusyAnimating;
    private int currentSpinRoundId = 100000;
    private string currentStatusBanner = "EUROPEAN ROULETTE - STRAIGHT PAYS 35 TO 1";
    private string currentActionPrompt = "SELECT CHIP ($1-$50), CLICK NUMBERS/OUTSIDE BETS, THEN SPIN";

    private readonly Dictionary<RouletteBetKey, RouletteInteractiveElement> boardSpotLookup =
        new Dictionary<RouletteBetKey, RouletteInteractiveElement>();

    private Vector3 seatedPlayerForward;
    private bool hasRecordedSeatedForward;
    private bool lastZoomedState;

    private void Awake()
    {
        Engine = new RouletteLogicEngine();

        if (runLogicSelfTestsOnAwake)
        {
            if (!RouletteLogicEngine.RunSelfTests(out string testSummary))
            {
                Debug.LogWarning("[Roulette] Self-test warning:\n" + testSummary);
            }
        }

        EnsureSubsystemsAndReferences();
    }

    private void Start()
    {
        EnsureSubsystemsAndReferences();
        if (balanceManager != null)
        {
            balanceManager.EnsureInitialized();
        }

        EnterIdleBarState(refundStagedBets: false);
    }

    private void Update()
    {
        bool currentlyZoomed = cameraZoomController != null && cameraZoomController.IsZoomedIn;
        if (currentlyZoomed != lastZoomedState)
        {
            lastZoomedState = currentlyZoomed;
            if (!isBusyAnimating && leaveTableButton != null)
            {
                RouletteGameState st = Engine.State;
                bool canLeaveNow = (IsTableSessionActive || currentlyZoomed) && st != RouletteGameState.Spinning;
                leaveTableButton.SetElementEnabled(canLeaveNow, "Leave Roulette Table (Zoom Out)", "LEAVE\nTABLE");
            }
        }

        if (!IsTableSessionActive || isBusyAnimating) return;

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

            if ((movedTooFar || turnedAway) && Engine.State != RouletteGameState.Spinning)
            {
                ExitTableSession();
            }
        }
    }

    private void OnDisable()
    {
        if (Engine != null && Engine.State == RouletteGameState.Betting && Engine.TotalStagedBet > 0m && balanceManager != null)
        {
            decimal refunded = Engine.ClearAllStagedBets();
            balanceManager.RefundStagedBet(refunded);
        }

        IsTableSessionActive = false;
        isBusyAnimating = false;
    }

    private void EnsureSubsystemsAndReferences()
    {
        if (balanceManager == null)
        {
            balanceManager = GetComponent<BlackjackBalanceManager>();
            if (balanceManager == null)
            {
                balanceManager = FindFirstObjectByType<BlackjackBalanceManager>();
            }
            if (balanceManager == null)
            {
                balanceManager = gameObject.AddComponent<BlackjackBalanceManager>();
            }
        }

        if (tableVisuals == null)
        {
            tableVisuals = GetComponent<RouletteTableVisuals>();
            if (tableVisuals == null) tableVisuals = gameObject.AddComponent<RouletteTableVisuals>();
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

        if (chipSelectorElements.Count == 0 || boardBetElements.Count == 0 || spinButton == null)
        {
            RouletteInteractiveElement[] all = GetComponentsInChildren<RouletteInteractiveElement>(true);
            chipSelectorElements.Clear();
            boardBetElements.Clear();

            for (int i = 0; i < all.Length; i++)
            {
                RouletteInteractiveElement elem = all[i];
                if (elem == null) continue;
                elem.gameController = this;

                switch (elem.actionType)
                {
                    case RouletteInteractiveElement.RouletteActionType.SelectChipDenomination:
                        chipSelectorElements.Add(elem);
                        break;
                    case RouletteInteractiveElement.RouletteActionType.PlaceBoardBet:
                        boardBetElements.Add(elem);
                        break;
                    case RouletteInteractiveElement.RouletteActionType.SpinOrNewRound:
                        if (spinButton == null) spinButton = elem;
                        break;
                    case RouletteInteractiveElement.RouletteActionType.ClearBets:
                        if (clearBetsButton == null) clearBetsButton = elem;
                        break;
                    case RouletteInteractiveElement.RouletteActionType.LeaveTable:
                        if (leaveTableButton == null) leaveTableButton = elem;
                        break;
                }
            }
        }

        boardSpotLookup.Clear();
        for (int i = 0; i < boardBetElements.Count; i++)
        {
            RouletteInteractiveElement spot = boardBetElements[i];
            if (spot != null)
            {
                boardSpotLookup[spot.GetBetKey()] = spot;
            }
        }
    }

    public void OnInteractiveElementClicked(RouletteInteractiveElement element)
    {
        if (element == null || isBusyAnimating) return;

        switch (element.actionType)
        {
            case RouletteInteractiveElement.RouletteActionType.SelectChipDenomination:
                HandleSelectChipClicked((decimal)Mathf.RoundToInt(element.chipDenomination));
                break;

            case RouletteInteractiveElement.RouletteActionType.PlaceBoardBet:
                HandleBoardSpotClicked(element);
                break;

            case RouletteInteractiveElement.RouletteActionType.SpinOrNewRound:
                HandleSpinOrNewRoundClicked();
                break;

            case RouletteInteractiveElement.RouletteActionType.ClearBets:
                HandleClearBetsClicked();
                break;

            case RouletteInteractiveElement.RouletteActionType.LeaveTable:
                ExitTableSession();
                break;
        }
    }

    private void HandleSelectChipClicked(decimal denom)
    {
        if (isBusyAnimating || denom <= 0m) return;
        ActivateTableSessionLock();

        if (Engine.State == RouletteGameState.Idle || Engine.State == RouletteGameState.RoundOver)
        {
            ClearWinningHighlights();
            if (tableVisuals != null)
            {
                tableVisuals.ClearBoardChipStacks();
                tableVisuals.HideWinningDollyMarker();
            }
            Engine.EnterBettingState();
        }

        selectedChipDenomination = denom;
        currentStatusBanner = $"SELECTED ${selectedChipDenomination:0} CHIP";
        currentActionPrompt = "CLICK ANY NUMBER (0-36) OR OUTSIDE BET SPOT TO PLACE CHIP";
        RefreshAllVisualsAndButtons();
    }

    private void HandleBoardSpotClicked(RouletteInteractiveElement spotElement)
    {
        if (isBusyAnimating || spotElement == null) return;
        if (Engine.State == RouletteGameState.Spinning) return;

        ActivateTableSessionLock();

        if (Engine.State == RouletteGameState.Idle || Engine.State == RouletteGameState.RoundOver)
        {
            ClearWinningHighlights();
            if (tableVisuals != null)
            {
                tableVisuals.ClearBoardChipStacks();
                tableVisuals.HideWinningDollyMarker();
            }
            Engine.EnterBettingState();
        }

        decimal balance = balanceManager != null ? balanceManager.CurrentBalance : 0m;
        if (balance < 1m && Engine.TotalStagedBet <= 0m)
        {
            balanceManager.RefundStagedBet(50m);
            balance = balanceManager.CurrentBalance;
            currentStatusBanner = "OUT OF FUNDS - HOUSE GRANT +$50";
        }

        // Auto-adjust chip denomination downward if current selection exceeds remaining balance
        if (selectedChipDenomination > balance && balance >= 1m)
        {
            int[] denoms = { 25, 10, 5, 1 };
            for (int i = 0; i < denoms.Length; i++)
            {
                if (balance >= denoms[i])
                {
                    selectedChipDenomination = denoms[i];
                    break;
                }
            }
        }

        if (selectedChipDenomination > balance || balance <= 0m)
        {
            currentStatusBanner = $"INSUFFICIENT FUNDS FOR ${selectedChipDenomination:0} CHIP";
            currentActionPrompt = Engine.TotalStagedBet > 0m
                ? "CLICK SPIN WHEEL OR CLEAR BETS"
                : "SELECT A SMALLER CHIP";
            RefreshAllVisualsAndButtons();
            return;
        }

        RouletteBetKey betKey = spotElement.GetBetKey();
        if (balanceManager != null && balanceManager.TryDeductForBet(selectedChipDenomination))
        {
            Engine.TryPlaceBet(betKey, selectedChipDenomination, balance);
            if (tableVisuals != null)
            {
                tableVisuals.RefreshBoardChipStacks(Engine.ActiveBets, boardSpotLookup);
            }

            decimal spotTotal = Engine.GetBetAmountOnSpot(betKey);
            currentStatusBanner = $"BET ${spotTotal:0} ON {betKey.GetDisplayLabel()} (TOTAL BET: ${Engine.TotalStagedBet:0})";
            currentActionPrompt = "ADD MORE BETS OR CLICK SPIN WHEEL";
            RefreshAllVisualsAndButtons();
        }
    }

    private void HandleClearBetsClicked()
    {
        if (isBusyAnimating) return;
        if (Engine.State != RouletteGameState.Betting || Engine.TotalStagedBet <= 0m) return;

        decimal refunded = Engine.ClearAllStagedBets();
        if (balanceManager != null && refunded > 0m)
        {
            balanceManager.RefundStagedBet(refunded);
        }

        if (tableVisuals != null)
        {
            tableVisuals.ClearBoardChipStacks();
        }

        currentStatusBanner = "ALL STAGED BETS CLEARED";
        currentActionPrompt = "CLICK ANY BOARD SPOT TO PLACE NEW BETS";
        RefreshAllVisualsAndButtons();
    }

    private void HandleSpinOrNewRoundClicked()
    {
        if (isBusyAnimating) return;

        ActivateTableSessionLock();

        if (Engine.State == RouletteGameState.Idle || Engine.State == RouletteGameState.RoundOver)
        {
            ClearWinningHighlights();
            if (tableVisuals != null)
            {
                tableVisuals.ClearBoardChipStacks();
                tableVisuals.HideWinningDollyMarker();
            }
            Engine.EnterBettingState();
            currentStatusBanner = "PLACE YOUR ROULETTE BETS";
            currentActionPrompt = "SELECT CHIP ($1-$50) & CLICK NUMBERS OR OUTSIDE BETS";
            RefreshAllVisualsAndButtons();
            return;
        }

        if (Engine.State == RouletteGameState.Betting)
        {
            if (!Engine.CanSpin())
            {
                currentStatusBanner = "PLACE A BET ON THE BOARD FIRST!";
                currentActionPrompt = "CLICK ANY NUMBER (0-36), DOZEN, OR RED/BLACK BEFORE SPINNING";
                RefreshAllVisualsAndButtons();
                return;
            }

            StartCoroutine(SpinWheelRoutine());
        }
    }

    private IEnumerator SpinWheelRoutine()
    {
        isBusyAnimating = true;
        ActivateTableSessionLock();
        ClearWinningHighlights();

        if (!Engine.BeginSpin(out int winningNumber))
        {
            isBusyAnimating = false;
            RefreshAllVisualsAndButtons();
            yield break;
        }

        currentSpinRoundId++;
        currentStatusBanner = "NO MORE BETS - WHEEL SPINNING...";
        currentActionPrompt = "WATCH THE BALL!";
        DisableAllControlsDuringSpin();

        if (tableVisuals != null)
        {
            tableVisuals.RefreshTextDisplays(Engine, balanceManager.CurrentBalance, selectedChipDenomination, currentStatusBanner, currentActionPrompt);
            yield return StartCoroutine(tableVisuals.SpinWheelAnimatedRoutine(winningNumber));
        }

        decimal totalReturn = Engine.FinalizeSpinAndResolve();
        BlackjackRoundOutcome economyOutcome = totalReturn > Engine.LastCommittedBet
            ? BlackjackRoundOutcome.PlayerWin
            : (totalReturn == Engine.LastCommittedBet ? BlackjackRoundOutcome.Push : BlackjackRoundOutcome.DealerWin);

        if (balanceManager != null)
        {
            balanceManager.TryApplyRoundPayout(currentSpinRoundId, Engine.LastCommittedBet, totalReturn, economyOutcome);
        }

        // Highlight winning number spot and place the golden Dolly marker
        HighlightWinningSpots(winningNumber);

        RoulettePocketColor winColor = RouletteLogicEngine.GetPocketColor(winningNumber);
        string colorStr = winColor == RoulettePocketColor.Green ? "GREEN" : (winColor == RoulettePocketColor.Red ? "RED" : "BLACK");

        if (totalReturn > Engine.LastCommittedBet)
        {
            currentStatusBanner = $"WINNING NUMBER: {winningNumber} {colorStr}! YOU WIN +${Engine.LastNetDelta:0.00} (PAYOUT ${totalReturn:0.00})";
        }
        else if (totalReturn == Engine.LastCommittedBet && totalReturn > 0m)
        {
            currentStatusBanner = $"WINNING NUMBER: {winningNumber} {colorStr} - PUSH (RETURNED ${totalReturn:0.00})";
        }
        else
        {
            currentStatusBanner = $"WINNING NUMBER: {winningNumber} {colorStr} - NO WIN (-${Engine.LastCommittedBet:0.00})";
        }

        currentActionPrompt = "CLICK ANY SPOT TO BET NEXT SPIN, NEW ROUND, OR LEAVE";
        isBusyAnimating = false;
        RefreshAllVisualsAndButtons();
    }

    public void ExitTableSession()
    {
        if (isBusyAnimating || Engine.State == RouletteGameState.Spinning) return;

        if (cameraZoomController != null)
        {
            cameraZoomController.RequestSessionZoomOutAndSuppressAuto();
        }

        EnterIdleBarState(refundStagedBets: true);
    }

    private void EnterIdleBarState(bool refundStagedBets)
    {
        if (refundStagedBets && Engine.State == RouletteGameState.Betting && Engine.TotalStagedBet > 0m && balanceManager != null)
        {
            decimal refunded = Engine.ClearAllStagedBets();
            balanceManager.RefundStagedBet(refunded);
        }

        Engine.ReturnToIdleState();
        IsTableSessionActive = false;
        hasRecordedSeatedForward = false;
        ClearWinningHighlights();

        if (tableVisuals != null)
        {
            tableVisuals.ClearBoardChipStacks();
            tableVisuals.HideWinningDollyMarker();
        }

        currentStatusBanner = "EUROPEAN ROULETTE - STRAIGHT PAYS 35 TO 1";
        currentActionPrompt = "CLICK ANY CHIP OR BOARD SPOT TO PLAY";
        RefreshAllVisualsAndButtons();
    }

    private void HighlightWinningSpots(int winningNumber)
    {
        RouletteBetKey straightKey = new RouletteBetKey(RouletteBetType.StraightNumber, winningNumber);
        if (boardSpotLookup.TryGetValue(straightKey, out RouletteInteractiveElement straightSpot) && straightSpot != null)
        {
            if (tableVisuals != null)
            {
                tableVisuals.ShowWinningDollyOnSpot(straightSpot.transform);
            }
        }

        for (int i = 0; i < boardBetElements.Count; i++)
        {
            RouletteInteractiveElement spot = boardBetElements[i];
            if (spot == null) continue;
            int mult = RouletteLogicEngine.GetWinningReturnMultiplier(spot.GetBetKey(), winningNumber);
            spot.SetWinningSpotHighlight(mult > 0);
        }
    }

    private void ClearWinningHighlights()
    {
        for (int i = 0; i < boardBetElements.Count; i++)
        {
            if (boardBetElements[i] != null)
            {
                boardBetElements[i].SetWinningSpotHighlight(false);
            }
        }
    }

    private void DisableAllControlsDuringSpin()
    {
        for (int i = 0; i < chipSelectorElements.Count; i++)
        {
            if (chipSelectorElements[i] != null) chipSelectorElements[i].SetElementEnabled(false);
        }
        for (int i = 0; i < boardBetElements.Count; i++)
        {
            if (boardBetElements[i] != null) boardBetElements[i].SetElementEnabled(false);
        }
        if (spinButton != null) spinButton.SetElementEnabled(false);
        if (clearBetsButton != null) clearBetsButton.SetElementEnabled(false);
        if (leaveTableButton != null) leaveTableButton.SetElementEnabled(false);
    }

    public void RefreshAllVisualsAndButtons()
    {
        decimal balance = balanceManager != null ? balanceManager.CurrentBalance : 0m;

        if (tableVisuals != null)
        {
            tableVisuals.RefreshTextDisplays(Engine, balance, selectedChipDenomination, currentStatusBanner, currentActionPrompt);
        }

        if (isBusyAnimating)
        {
            DisableAllControlsDuringSpin();
            return;
        }

        RouletteGameState state = Engine.State;
        bool canBet = state != RouletteGameState.Spinning;

        for (int i = 0; i < chipSelectorElements.Count; i++)
        {
            RouletteInteractiveElement chip = chipSelectorElements[i];
            if (chip == null) continue;

            decimal denom = (decimal)Mathf.RoundToInt(chip.chipDenomination);
            bool affordable = canBet && (balance >= denom || Engine.TotalStagedBet > 0m);
            bool isSelected = denom == selectedChipDenomination;
            chip.SetSelectedHighlight(isSelected);
            string prompt = isSelected ? $"Selected ${denom:0} Chip" : $"Select ${denom:0} Chip";
            chip.SetElementEnabled(affordable, prompt, $"${denom:0}");
        }

        for (int i = 0; i < boardBetElements.Count; i++)
        {
            RouletteInteractiveElement spot = boardBetElements[i];
            if (spot == null) continue;

            RouletteBetKey key = spot.GetBetKey();
            decimal currentOnSpot = Engine.GetBetAmountOnSpot(key);
            string labelName = key.GetDisplayLabel();
            string oddsStr = key.BetType == RouletteBetType.StraightNumber
                ? "35:1"
                : (key.BetType >= RouletteBetType.Dozen1 ? "2:1" : "1:1");
            string prompt = currentOnSpot > 0m
                ? $"Bet +${selectedChipDenomination:0} on {labelName} (Current: ${currentOnSpot:0}, Pays {oddsStr})"
                : $"Bet +${selectedChipDenomination:0} on {labelName} (Pays {oddsStr})";
            spot.SetElementEnabled(canBet, prompt);
        }

        if (spinButton != null)
        {
            if (state == RouletteGameState.Idle)
            {
                spinButton.SetElementEnabled(true, "Sit / Join Roulette Table", "JOIN\nTABLE");
            }
            else if (state == RouletteGameState.Betting)
            {
                bool canSpin = Engine.CanSpin();
                spinButton.SetElementEnabled(canSpin, canSpin ? $"Spin Wheel (Total Bet: ${Engine.TotalStagedBet:0})" : "Place Bet on Board First", "SPIN\nWHEEL");
            }
            else if (state == RouletteGameState.RoundOver)
            {
                spinButton.SetElementEnabled(true, "Start New Betting Round", "NEW\nROUND");
            }
            else
            {
                spinButton.SetElementEnabled(false, "Wheel Spinning...", "SPIN\nWHEEL");
            }
        }

        if (clearBetsButton != null)
        {
            bool canClear = state == RouletteGameState.Betting && Engine.TotalStagedBet > 0m;
            clearBetsButton.SetElementEnabled(canClear, $"Clear All Bets (${Engine.TotalStagedBet:0})", "CLEAR\nBETS");
        }

        if (leaveTableButton != null)
        {
            bool isZoomed = cameraZoomController != null && cameraZoomController.IsZoomedIn;
            bool canLeave = (IsTableSessionActive || isZoomed) && state != RouletteGameState.Spinning;
            leaveTableButton.SetElementEnabled(canLeave, "Leave Roulette Table (Zoom Out)", "LEAVE\nTABLE");
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
    }
}
