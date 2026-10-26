using System;
using UnityEngine;

public class BlackjackBalanceManager : MonoBehaviour
{
    [Header("Economy Integration")]
    [Tooltip("Optional reference to the scene's DVDLogoBouncer economy. Auto-found if left empty.")]
    public DVDLogoBouncer economyBouncer;
    [Tooltip("Optional reference to BarManager. Auto-found if left empty.")]
    public BarManager barManager;

    [Header("Fallback / Direct Scene Start Balance")]
    [Tooltip("Used if no DVDLogoBouncer exists in the scene, or when starting BarGameBarni directly with $0.")]
    public float initialStartingBalance = 100f;
    public bool seedBalanceWhenZeroOnStart = true;

    private decimal selfContainedBalance = 100m;
    private int lastPayoutRoundId = -1;
    private bool initialized;

    public decimal CurrentBalance
    {
        get
        {
            EnsureInitialized();
            if (economyBouncer != null)
            {
                return Math.Max(0m, Math.Round(economyBouncer.currentMoney, 2));
            }
            return Math.Max(0m, Math.Round(selfContainedBalance, 2));
        }
    }

    private void Awake()
    {
        selfContainedBalance = (decimal)Mathf.Max(1f, initialStartingBalance);
    }

    private void Start()
    {
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        if (barManager == null)
        {
            barManager = FindFirstObjectByType<BarManager>();
        }

        if (economyBouncer == null && barManager != null)
        {
            economyBouncer = barManager.economyBouncer;
        }

        if (economyBouncer == null)
        {
            economyBouncer = FindFirstObjectByType<DVDLogoBouncer>();
        }

        selfContainedBalance = (decimal)Mathf.Max(1f, initialStartingBalance);

        if (economyBouncer != null && seedBalanceWhenZeroOnStart)
        {
            bool hasPriorGambling = RunSaveManager.HasInstance &&
                                    (RunSaveManager.Instance.MoneyLostByGambling > 0m ||
                                     RunSaveManager.Instance.MoneyEarnedByGambling > 0m);

            if (economyBouncer.currentMoney < 1m && !hasPriorGambling)
            {
                economyBouncer.currentMoney = (decimal)Mathf.Max(10f, initialStartingBalance);
                economyBouncer.RefreshMoneyUIFromSave();
            }
        }
    }

    public bool CanAfford(decimal amount)
    {
        EnsureInitialized();
        if (amount <= 0m) return false;
        return CurrentBalance >= amount;
    }

    public bool TryDeductForBet(decimal amount)
    {
        EnsureInitialized();
        amount = Math.Round(amount, 2);
        if (amount <= 0m || CurrentBalance < amount)
        {
            return false;
        }

        if (economyBouncer != null)
        {
            economyBouncer.currentMoney = Math.Max(0m, Math.Round(economyBouncer.currentMoney - amount, 2));
            economyBouncer.RefreshMoneyUIFromSave();
        }
        else
        {
            selfContainedBalance = Math.Max(0m, Math.Round(selfContainedBalance - amount, 2));
        }

        return true;
    }

    public void RefundStagedBet(decimal amount)
    {
        EnsureInitialized();
        amount = Math.Round(amount, 2);
        if (amount <= 0m) return;

        AddFundsInternal(amount);
    }

    /// <summary>
    /// Applies the payout for a completed round once and only once per roundId.
    /// </summary>
    public bool TryApplyRoundPayout(int roundId, decimal committedBet, decimal totalReturn, BlackjackRoundOutcome outcome)
    {
        EnsureInitialized();
        if (roundId <= 0 || roundId == lastPayoutRoundId)
        {
            return false;
        }

        lastPayoutRoundId = roundId;
        committedBet = Math.Max(0m, Math.Round(committedBet, 2));
        totalReturn = Math.Max(0m, Math.Round(totalReturn, 2));

        if (totalReturn > 0m)
        {
            AddFundsInternal(totalReturn);
        }

        decimal netProfit = totalReturn - committedBet;
        if (netProfit > 0m)
        {
            if (RunSaveManager.HasInstance)
            {
                RunSaveManager.Instance.RecordGamblingWin(netProfit, addToCurrentMoney: false);
            }
            SpawnEconomyPopup(netProfit, isNegative: false);
        }
        else if (netProfit < 0m)
        {
            decimal lossAmount = -netProfit;
            if (RunSaveManager.HasInstance)
            {
                RunSaveManager.Instance.RecordGamblingLoss(lossAmount, deductFromCurrentMoney: false);
            }
            SpawnEconomyPopup(lossAmount, isNegative: true);
        }

        return true;
    }

    private void AddFundsInternal(decimal amount)
    {
        if (amount <= 0m) return;

        if (economyBouncer != null)
        {
            economyBouncer.currentMoney = Math.Round(economyBouncer.currentMoney + amount, 2);
            economyBouncer.RefreshMoneyUIFromSave();
        }
        else
        {
            selfContainedBalance = Math.Round(selfContainedBalance + amount, 2);
        }
    }

    private void SpawnEconomyPopup(decimal amount, bool isNegative)
    {
        if (economyBouncer == null || economyBouncer.popupCanvasParent == null) return;

        GameObject prefab = isNegative ? economyBouncer.negativeMoneyPopupPrefab : economyBouncer.moneyPopupPrefab;
        if (prefab == null) return;

        GameObject popupObj = Instantiate(prefab, economyBouncer.popupCanvasParent);
        MoneyPopup popupScript = popupObj.GetComponent<MoneyPopup>();
        if (popupScript != null)
        {
            popupScript.AnimatePopup(isNegative ? -amount : amount);
        }
    }
}
