using System;
using System.Collections.Generic;
using System.Text;

public enum RouletteGameState
{
    Idle = 0,
    Betting = 1,
    Spinning = 2,
    RoundOver = 3
}

public enum RoulettePocketColor
{
    Green = 0,
    Red = 1,
    Black = 2
}

public enum RouletteBetType
{
    StraightNumber = 0,
    Red = 1,
    Black = 2,
    Even = 3,
    Odd = 4,
    Low1To18 = 5,
    High19To36 = 6,
    Dozen1 = 7,
    Dozen2 = 8,
    Dozen3 = 9,
    Column1 = 10,
    Column2 = 11,
    Column3 = 12
}

[Serializable]
public struct RouletteBetKey : IEquatable<RouletteBetKey>
{
    public RouletteBetType BetType;
    public int Number; // 0..36 when BetType == StraightNumber, otherwise 0

    public RouletteBetKey(RouletteBetType betType, int number = 0)
    {
        BetType = betType;
        Number = betType == RouletteBetType.StraightNumber ? number : 0;
    }

    public bool Equals(RouletteBetKey other)
    {
        return BetType == other.BetType && Number == other.Number;
    }

    public override bool Equals(object obj)
    {
        return obj is RouletteBetKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return ((int)BetType * 397) ^ Number;
    }

    public string GetDisplayLabel()
    {
        switch (BetType)
        {
            case RouletteBetType.StraightNumber: return Number.ToString();
            case RouletteBetType.Red: return "RED";
            case RouletteBetType.Black: return "BLACK";
            case RouletteBetType.Even: return "EVEN";
            case RouletteBetType.Odd: return "ODD";
            case RouletteBetType.Low1To18: return "1-18";
            case RouletteBetType.High19To36: return "19-36";
            case RouletteBetType.Dozen1: return "1ST 12";
            case RouletteBetType.Dozen2: return "2ND 12";
            case RouletteBetType.Dozen3: return "3RD 12";
            case RouletteBetType.Column1: return "2:1 R1";
            case RouletteBetType.Column2: return "2:1 R2";
            case RouletteBetType.Column3: return "2:1 R3";
            default: return BetType.ToString();
        }
    }
}

public class RouletteLogicEngine
{
    // Standard European Single-Zero Wheel Sequence (clockwise starting at 0)
    public static readonly int[] WheelPocketSequence =
    {
        0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
        5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
    };

    private static readonly HashSet<int> RedNumbers = new HashSet<int>
    {
        1, 3, 5, 7, 9, 12, 14, 16, 18,
        19, 21, 23, 25, 27, 30, 32, 34, 36
    };

    public RouletteGameState State { get; private set; } = RouletteGameState.Idle;
    public decimal TotalStagedBet { get; private set; }
    public int LastWinningNumber { get; private set; } = -1;
    public decimal LastTotalPayout { get; private set; }
    public decimal LastNetDelta { get; private set; }
    public decimal LastCommittedBet { get; private set; }
    public bool IsRoundResolved { get; private set; }

    private readonly Dictionary<RouletteBetKey, decimal> activeBets = new Dictionary<RouletteBetKey, decimal>();
    private readonly List<int> recentWinningNumbers = new List<int>();
    private readonly Random rng;
    private int? forcedNextWinningNumber;

    public IReadOnlyDictionary<RouletteBetKey, decimal> ActiveBets => activeBets;
    public IReadOnlyList<int> RecentWinningNumbers => recentWinningNumbers;

    public RouletteLogicEngine(int? seed = null)
    {
        rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public static RoulettePocketColor GetPocketColor(int number)
    {
        if (number <= 0 || number > 36) return RoulettePocketColor.Green;
        return RedNumbers.Contains(number) ? RoulettePocketColor.Red : RoulettePocketColor.Black;
    }

    public static int GetWheelIndexForNumber(int number)
    {
        for (int i = 0; i < WheelPocketSequence.Length; i++)
        {
            if (WheelPocketSequence[i] == number)
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>
    /// Returns total return multiplier (including original stake) when the bet wins, or 0 when it loses.
    /// Straight-Up pays 35:1 (36x return); Dozens/Columns pay 2:1 (3x return); Even-Money pays 1:1 (2x return).
    /// </summary>
    public static int GetWinningReturnMultiplier(RouletteBetKey bet, int winningNumber)
    {
        if (winningNumber < 0 || winningNumber > 36) return 0;

        switch (bet.BetType)
        {
            case RouletteBetType.StraightNumber:
                return bet.Number == winningNumber ? 36 : 0;

            case RouletteBetType.Red:
                return winningNumber > 0 && GetPocketColor(winningNumber) == RoulettePocketColor.Red ? 2 : 0;

            case RouletteBetType.Black:
                return winningNumber > 0 && GetPocketColor(winningNumber) == RoulettePocketColor.Black ? 2 : 0;

            case RouletteBetType.Even:
                return winningNumber > 0 && (winningNumber % 2 == 0) ? 2 : 0;

            case RouletteBetType.Odd:
                return winningNumber > 0 && (winningNumber % 2 == 1) ? 2 : 0;

            case RouletteBetType.Low1To18:
                return winningNumber >= 1 && winningNumber <= 18 ? 2 : 0;

            case RouletteBetType.High19To36:
                return winningNumber >= 19 && winningNumber <= 36 ? 2 : 0;

            case RouletteBetType.Dozen1:
                return winningNumber >= 1 && winningNumber <= 12 ? 3 : 0;

            case RouletteBetType.Dozen2:
                return winningNumber >= 13 && winningNumber <= 24 ? 3 : 0;

            case RouletteBetType.Dozen3:
                return winningNumber >= 25 && winningNumber <= 36 ? 3 : 0;

            case RouletteBetType.Column1:
                return winningNumber >= 1 && (winningNumber % 3 == 1) ? 3 : 0;

            case RouletteBetType.Column2:
                return winningNumber >= 1 && (winningNumber % 3 == 2) ? 3 : 0;

            case RouletteBetType.Column3:
                return winningNumber >= 1 && (winningNumber % 3 == 0) ? 3 : 0;

            default:
                return 0;
        }
    }

    public void EnterBettingState()
    {
        activeBets.Clear();
        TotalStagedBet = 0m;
        LastTotalPayout = 0m;
        LastNetDelta = 0m;
        LastCommittedBet = 0m;
        IsRoundResolved = false;
        State = RouletteGameState.Betting;
    }

    public void ReturnToIdleState()
    {
        activeBets.Clear();
        TotalStagedBet = 0m;
        LastTotalPayout = 0m;
        LastNetDelta = 0m;
        LastCommittedBet = 0m;
        IsRoundResolved = false;
        State = RouletteGameState.Idle;
    }

    public bool TryPlaceBet(RouletteBetKey betKey, decimal chipAmount, decimal availableBalance)
    {
        if (State != RouletteGameState.Betting && State != RouletteGameState.Idle && State != RouletteGameState.RoundOver)
        {
            return false;
        }

        if (chipAmount <= 0m || chipAmount > availableBalance)
        {
            return false;
        }

        if (betKey.BetType == RouletteBetType.StraightNumber && (betKey.Number < 0 || betKey.Number > 36))
        {
            return false;
        }

        if (State == RouletteGameState.Idle || State == RouletteGameState.RoundOver)
        {
            EnterBettingState();
        }

        if (activeBets.TryGetValue(betKey, out decimal existing))
        {
            activeBets[betKey] = existing + chipAmount;
        }
        else
        {
            activeBets[betKey] = chipAmount;
        }

        TotalStagedBet += chipAmount;
        return true;
    }

    public decimal GetBetAmountOnSpot(RouletteBetKey betKey)
    {
        return activeBets.TryGetValue(betKey, out decimal amt) ? amt : 0m;
    }

    public decimal ClearAllStagedBets()
    {
        if (State != RouletteGameState.Betting)
        {
            return 0m;
        }

        decimal refunded = TotalStagedBet;
        activeBets.Clear();
        TotalStagedBet = 0m;
        return refunded;
    }

    public bool CanSpin()
    {
        return State == RouletteGameState.Betting && TotalStagedBet > 0m && activeBets.Count > 0;
    }

    public void SetForcedWinningNumberForTesting(int number)
    {
        forcedNextWinningNumber = Math.Clamp(number, 0, 36);
    }

    public bool BeginSpin(out int winningNumber)
    {
        winningNumber = -1;
        if (!CanSpin())
        {
            return false;
        }

        if (forcedNextWinningNumber.HasValue)
        {
            winningNumber = forcedNextWinningNumber.Value;
            forcedNextWinningNumber = null;
        }
        else
        {
            int idx = rng.Next(0, WheelPocketSequence.Length);
            winningNumber = WheelPocketSequence[idx];
        }

        LastWinningNumber = winningNumber;
        LastCommittedBet = TotalStagedBet;
        IsRoundResolved = false;
        State = RouletteGameState.Spinning;
        return true;
    }

    public decimal FinalizeSpinAndResolve()
    {
        if (State != RouletteGameState.Spinning || IsRoundResolved)
        {
            return LastTotalPayout;
        }

        IsRoundResolved = true;
        decimal totalReturn = 0m;

        foreach (KeyValuePair<RouletteBetKey, decimal> kvp in activeBets)
        {
            int mult = GetWinningReturnMultiplier(kvp.Key, LastWinningNumber);
            if (mult > 0)
            {
                totalReturn += Math.Round(kvp.Value * mult, 2);
            }
        }

        LastTotalPayout = totalReturn;
        LastNetDelta = LastTotalPayout - LastCommittedBet;

        recentWinningNumbers.Insert(0, LastWinningNumber);
        if (recentWinningNumbers.Count > 8)
        {
            recentWinningNumbers.RemoveAt(recentWinningNumbers.Count - 1);
        }

        activeBets.Clear();
        TotalStagedBet = 0m;
        State = RouletteGameState.RoundOver;
        return LastTotalPayout;
    }

    public static bool RunSelfTests(out string summary)
    {
        StringBuilder sb = new StringBuilder();
        bool allPassed = true;

        void Assert(bool cond, string name)
        {
            if (cond) sb.AppendLine($"[PASS] {name}");
            else { allPassed = false; sb.AppendLine($"[FAIL] {name}"); }
        }

        // 1. Verify European 37-pocket wheel completeness (0..36)
        HashSet<int> pockets = new HashSet<int>(WheelPocketSequence);
        Assert(WheelPocketSequence.Length == 37 && pockets.Count == 37, "Wheel has 37 unique pockets (0..36)");
        Assert(GetPocketColor(0) == RoulettePocketColor.Green, "0 is Green");
        Assert(GetPocketColor(1) == RoulettePocketColor.Red && GetPocketColor(2) == RoulettePocketColor.Black, "1 is Red and 2 is Black");

        // 2. Verify Straight-Up 35:1, Dozen 2:1, and Red 1:1 payouts
        RouletteLogicEngine engine = new RouletteLogicEngine(42);
        engine.EnterBettingState();
        Assert(!engine.TryPlaceBet(new RouletteBetKey(RouletteBetType.Red), 50m, 20m), "Prevents bets exceeding available balance");
        Assert(engine.TryPlaceBet(new RouletteBetKey(RouletteBetType.StraightNumber, 17), 10m, 100m), "Place $10 on Straight 17");
        Assert(engine.TryPlaceBet(new RouletteBetKey(RouletteBetType.Black), 10m, 90m), "Place $10 on Black");
        Assert(engine.TryPlaceBet(new RouletteBetKey(RouletteBetType.Dozen2), 10m, 80m), "Place $10 on 2nd Dozen (13-24)");

        engine.SetForcedWinningNumberForTesting(17);
        Assert(engine.BeginSpin(out int winNum) && winNum == 17, "Spins and lands on 17 (Black)");
        decimal totalReturn = engine.FinalizeSpinAndResolve();
        // Straight 17 returns 36 * $10 = $360; Black returns 2 * $10 = $20; Dozen2 returns 3 * $10 = $30 -> Total $410
        Assert(totalReturn == 410m && engine.LastNetDelta == 380m, "Correct combined payout ($360 + $20 + $30 = $410)");

        // 3. Verify 0 loses all outside bets and wins Straight 0
        engine.EnterBettingState();
        engine.TryPlaceBet(new RouletteBetKey(RouletteBetType.Red), 10m, 100m);
        engine.TryPlaceBet(new RouletteBetKey(RouletteBetType.Even), 10m, 90m);
        engine.TryPlaceBet(new RouletteBetKey(RouletteBetType.Low1To18), 10m, 80m);
        engine.SetForcedWinningNumberForTesting(0);
        engine.BeginSpin(out _);
        Assert(engine.FinalizeSpinAndResolve() == 0m, "Outside bets (Red/Even/1-18) lose when wheel lands on 0");

        summary = sb.ToString();
        return allPassed;
    }
}
