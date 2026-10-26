using System;
using System.Collections.Generic;
using System.Text;

public enum BlackjackGameState
{
    Idle = 0,
    Betting = 1,
    PlayerTurn = 2,
    DealerTurn = 3,
    RoundOver = 4
}

public enum CardSuit
{
    Clubs = 0,
    Diamonds = 1,
    Hearts = 2,
    Spades = 3
}

public enum CardRank
{
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
    Ace = 14
}

public enum BlackjackRoundOutcome
{
    None = 0,
    PlayerBlackjack = 1,
    PlayerWin = 2,
    DealerBust = 3,
    DealerBlackjack = 4,
    DealerWin = 5,
    PlayerBust = 6,
    Push = 7
}

[Serializable]
public struct BlackjackCard
{
    public CardSuit Suit;
    public CardRank Rank;

    public BlackjackCard(CardSuit suit, CardRank rank)
    {
        Suit = suit;
        Rank = rank;
    }

    public int GetBaseValue()
    {
        switch (Rank)
        {
            case CardRank.Jack:
            case CardRank.Queen:
            case CardRank.King:
                return 10;
            case CardRank.Ace:
                return 11;
            default:
                return (int)Rank;
        }
    }

    public bool IsAce => Rank == CardRank.Ace;
    public bool IsRedSuit => Suit == CardSuit.Hearts || Suit == CardSuit.Diamonds;

    public string GetRankSymbol()
    {
        switch (Rank)
        {
            case CardRank.Jack: return "J";
            case CardRank.Queen: return "Q";
            case CardRank.King: return "K";
            case CardRank.Ace: return "A";
            default: return ((int)Rank).ToString();
        }
    }

    public string GetSuitName()
    {
        switch (Suit)
        {
            case CardSuit.Clubs: return "CLUBS";
            case CardSuit.Diamonds: return "DIAMONDS";
            case CardSuit.Hearts: return "HEARTS";
            case CardSuit.Spades: return "SPADES";
            default: return "SUIT";
        }
    }

    public string GetSuitShortCode()
    {
        switch (Suit)
        {
            case CardSuit.Clubs: return "C";
            case CardSuit.Diamonds: return "D";
            case CardSuit.Hearts: return "H";
            case CardSuit.Spades: return "S";
            default: return "?";
        }
    }

    public override string ToString()
    {
        return $"{GetRankSymbol()} of {GetSuitName()}";
    }
}

public class BlackjackDeck
{
    private readonly List<BlackjackCard> cards = new List<BlackjackCard>(52);
    private readonly Random rng;

    public int RemainingCards => cards.Count;

    public BlackjackDeck(int? seed = null)
    {
        rng = seed.HasValue ? new Random(seed.Value) : new Random();
        ResetAndShuffle();
    }

    public void ResetAndShuffle()
    {
        cards.Clear();
        foreach (CardSuit suit in Enum.GetValues(typeof(CardSuit)))
        {
            foreach (CardRank rank in Enum.GetValues(typeof(CardRank)))
            {
                cards.Add(new BlackjackCard(suit, rank));
            }
        }

        // Fisher-Yates shuffle
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int swapIdx = rng.Next(i + 1);
            BlackjackCard temp = cards[i];
            cards[i] = cards[swapIdx];
            cards[swapIdx] = temp;
        }
    }

    public BlackjackCard DrawCard()
    {
        if (cards.Count == 0)
        {
            ResetAndShuffle();
        }

        int lastIndex = cards.Count - 1;
        BlackjackCard drawn = cards[lastIndex];
        cards.RemoveAt(lastIndex);
        return drawn;
    }

    public void SetCustomDeckOrderForTesting(IEnumerable<BlackjackCard> topFirstCards)
    {
        cards.Clear();
        List<BlackjackCard> list = new List<BlackjackCard>(topFirstCards);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            cards.Add(list[i]);
        }
    }
}

public class BlackjackHand
{
    private readonly List<BlackjackCard> cards = new List<BlackjackCard>(8);

    public IReadOnlyList<BlackjackCard> Cards => cards;
    public int Count => cards.Count;

    public void Clear()
    {
        cards.Clear();
    }

    public void AddCard(BlackjackCard card)
    {
        cards.Add(card);
    }

    public int CalculateValue(out bool isSoft)
    {
        int total = 0;
        int softAces = 0;

        for (int i = 0; i < cards.Count; i++)
        {
            BlackjackCard card = cards[i];
            total += card.GetBaseValue();
            if (card.IsAce)
            {
                softAces++;
            }
        }

        while (total > 21 && softAces > 0)
        {
            total -= 10;
            softAces--;
        }

        isSoft = softAces > 0 && total <= 21;
        return total;
    }

    public int CalculateValue()
    {
        return CalculateValue(out _);
    }

    public bool IsNaturalBlackjack => cards.Count == 2 && CalculateValue() == 21;
    public bool IsBusted => CalculateValue() > 21;
}

public class BlackjackLogicEngine
{
    public const int MinimumDeckCardsBeforeReshuffle = 12;
    public const int DealerStandThreshold = 17;

    public BlackjackGameState State { get; private set; } = BlackjackGameState.Idle;
    public BlackjackDeck Deck { get; private set; }
    public BlackjackHand PlayerHand { get; private set; } = new BlackjackHand();
    public BlackjackHand DealerHand { get; private set; } = new BlackjackHand();
    public BlackjackRoundOutcome LastOutcome { get; private set; } = BlackjackRoundOutcome.None;

    public decimal CurrentBet { get; private set; }
    public decimal LastPayout { get; private set; }
    public decimal LastNetDelta { get; private set; }
    public bool IsDealerHoleCardRevealed { get; private set; }
    public bool IsRoundResolved { get; private set; }

    public BlackjackLogicEngine(int? seed = null)
    {
        Deck = new BlackjackDeck(seed);
    }

    public void EnterBettingState()
    {
        PlayerHand.Clear();
        DealerHand.Clear();
        LastOutcome = BlackjackRoundOutcome.None;
        LastPayout = 0m;
        LastNetDelta = 0m;
        IsDealerHoleCardRevealed = false;
        IsRoundResolved = false;
        State = BlackjackGameState.Betting;
    }

    public void ReturnToIdleState()
    {
        PlayerHand.Clear();
        DealerHand.Clear();
        CurrentBet = 0m;
        LastOutcome = BlackjackRoundOutcome.None;
        LastPayout = 0m;
        LastNetDelta = 0m;
        IsDealerHoleCardRevealed = false;
        IsRoundResolved = false;
        State = BlackjackGameState.Idle;
    }

    public bool TryAddBet(decimal chipAmount, decimal availableBalance)
    {
        if (State != BlackjackGameState.Betting && State != BlackjackGameState.Idle && State != BlackjackGameState.RoundOver)
        {
            return false;
        }

        if (chipAmount <= 0m || chipAmount > availableBalance)
        {
            return false;
        }

        if (State == BlackjackGameState.Idle || State == BlackjackGameState.RoundOver)
        {
            EnterBettingState();
        }

        CurrentBet += chipAmount;
        return true;
    }

    public decimal ClearStagedBet()
    {
        if (State != BlackjackGameState.Betting)
        {
            return 0m;
        }

        decimal refunded = CurrentBet;
        CurrentBet = 0m;
        return refunded;
    }

    public bool CanDealRound()
    {
        return State == BlackjackGameState.Betting && CurrentBet > 0m;
    }

    public bool StartRound(bool reshuffleIfLow = true)
    {
        if (!CanDealRound())
        {
            return false;
        }

        if (reshuffleIfLow && Deck.RemainingCards < MinimumDeckCardsBeforeReshuffle)
        {
            Deck.ResetAndShuffle();
        }

        PlayerHand.Clear();
        DealerHand.Clear();
        LastOutcome = BlackjackRoundOutcome.None;
        LastPayout = 0m;
        LastNetDelta = 0m;
        IsDealerHoleCardRevealed = false;
        IsRoundResolved = false;

        // Standard Blackjack dealing order: Player 1, Dealer 1, Player 2, Dealer 2 (hole card)
        PlayerHand.AddCard(Deck.DrawCard());
        DealerHand.AddCard(Deck.DrawCard());
        PlayerHand.AddCard(Deck.DrawCard());
        DealerHand.AddCard(Deck.DrawCard());

        State = BlackjackGameState.PlayerTurn;
        return true;
    }

    /// <summary>
    /// Checks if either Player or Dealer has a Natural 21 right after the initial 4 cards are dealt.
    /// If so, reveals the dealer hole card and resolves the round immediately.
    /// </summary>
    public bool CheckAndResolveNaturalBlackjack()
    {
        if (State != BlackjackGameState.PlayerTurn || IsRoundResolved)
        {
            return false;
        }

        bool playerNatural = PlayerHand.IsNaturalBlackjack;
        bool dealerNatural = DealerHand.IsNaturalBlackjack;

        if (!playerNatural && !dealerNatural)
        {
            return false;
        }

        IsDealerHoleCardRevealed = true;

        if (playerNatural && dealerNatural)
        {
            ResolveRoundInternal(BlackjackRoundOutcome.Push);
        }
        else if (playerNatural)
        {
            ResolveRoundInternal(BlackjackRoundOutcome.PlayerBlackjack);
        }
        else
        {
            ResolveRoundInternal(BlackjackRoundOutcome.DealerBlackjack);
        }

        return true;
    }

    public bool CanPlayerHit()
    {
        return State == BlackjackGameState.PlayerTurn && !IsRoundResolved && !PlayerHand.IsBusted;
    }

    public bool CanPlayerStand()
    {
        return State == BlackjackGameState.PlayerTurn && !IsRoundResolved;
    }

    public BlackjackCard PlayerHit(out bool playerBusted, out bool reachedTwentyOne)
    {
        playerBusted = false;
        reachedTwentyOne = false;

        if (!CanPlayerHit())
        {
            return default;
        }

        BlackjackCard card = Deck.DrawCard();
        PlayerHand.AddCard(card);

        int value = PlayerHand.CalculateValue();
        if (value > 21)
        {
            playerBusted = true;
            IsDealerHoleCardRevealed = true;
            ResolveRoundInternal(BlackjackRoundOutcome.PlayerBust);
        }
        else if (value == 21)
        {
            reachedTwentyOne = true;
        }

        return card;
    }

    public bool BeginDealerTurn()
    {
        if (State != BlackjackGameState.PlayerTurn || IsRoundResolved)
        {
            return false;
        }

        State = BlackjackGameState.DealerTurn;
        IsDealerHoleCardRevealed = true;
        return true;
    }

    public bool ShouldDealerHit()
    {
        if (State != BlackjackGameState.DealerTurn || IsRoundResolved)
        {
            return false;
        }

        return DealerHand.CalculateValue() < DealerStandThreshold;
    }

    public BlackjackCard DealerHitStep()
    {
        if (!ShouldDealerHit())
        {
            return default;
        }

        BlackjackCard card = Deck.DrawCard();
        DealerHand.AddCard(card);
        return card;
    }

    public BlackjackRoundOutcome FinalizeDealerTurnAndResolve()
    {
        if (State != BlackjackGameState.DealerTurn || IsRoundResolved)
        {
            return LastOutcome;
        }

        IsDealerHoleCardRevealed = true;
        int dealerVal = DealerHand.CalculateValue();
        int playerVal = PlayerHand.CalculateValue();

        if (dealerVal > 21)
        {
            ResolveRoundInternal(BlackjackRoundOutcome.DealerBust);
        }
        else if (playerVal > dealerVal)
        {
            ResolveRoundInternal(BlackjackRoundOutcome.PlayerWin);
        }
        else if (playerVal < dealerVal)
        {
            ResolveRoundInternal(BlackjackRoundOutcome.DealerWin);
        }
        else
        {
            ResolveRoundInternal(BlackjackRoundOutcome.Push);
        }

        return LastOutcome;
    }

    public int GetVisibleDealerScore()
    {
        if (DealerHand.Count == 0) return 0;
        if (IsDealerHoleCardRevealed) return DealerHand.CalculateValue();
        return DealerHand.Cards[0].GetBaseValue();
    }

    public static decimal CalculateTotalPayoutReturn(decimal bet, BlackjackRoundOutcome outcome)
    {
        if (bet <= 0m) return 0m;

        switch (outcome)
        {
            case BlackjackRoundOutcome.PlayerBlackjack:
                // 3:2 profit + original bet returned = 2.5x bet
                return Math.Round(bet * 2.5m, 2);
            case BlackjackRoundOutcome.PlayerWin:
            case BlackjackRoundOutcome.DealerBust:
                // 1:1 profit + original bet returned = 2.0x bet
                return bet * 2m;
            case BlackjackRoundOutcome.Push:
                // Original bet returned
                return bet;
            default:
                // Loss
                return 0m;
        }
    }

    private void ResolveRoundInternal(BlackjackRoundOutcome outcome)
    {
        if (IsRoundResolved)
        {
            return;
        }

        IsRoundResolved = true;
        LastOutcome = outcome;
        LastPayout = CalculateTotalPayoutReturn(CurrentBet, outcome);
        LastNetDelta = LastPayout - CurrentBet;
        CurrentBet = 0m;
        State = BlackjackGameState.RoundOver;
    }

    public static bool RunSelfTests(out string summary)
    {
        StringBuilder sb = new StringBuilder();
        bool allPassed = true;

        void Assert(bool condition, string testName)
        {
            if (!condition)
            {
                allPassed = false;
                sb.AppendLine($"[FAIL] {testName}");
            }
            else
            {
                sb.AppendLine($"[PASS] {testName}");
            }
        }

        // 1. 52-card deck uniqueness test
        BlackjackDeck deck = new BlackjackDeck(42);
        Assert(deck.RemainingCards == 52, "Deck starts with 52 cards");
        HashSet<string> unique = new HashSet<string>();
        for (int i = 0; i < 52; i++)
        {
            unique.Add(deck.DrawCard().ToString());
        }
        Assert(unique.Count == 52 && deck.RemainingCards == 0, "All 52 cards have unique rank and suit");

        // 2. Ace calculations (1 or 11)
        BlackjackHand aceHand = new BlackjackHand();
        aceHand.AddCard(new BlackjackCard(CardSuit.Spades, CardRank.Ace));
        aceHand.AddCard(new BlackjackCard(CardSuit.Hearts, CardRank.Six));
        Assert(aceHand.CalculateValue(out bool soft17) == 17 && soft17, "Ace + 6 = Soft 17");
        aceHand.AddCard(new BlackjackCard(CardSuit.Clubs, CardRank.Ten));
        Assert(aceHand.CalculateValue(out bool hard17) == 17 && !hard17, "Ace + 6 + 10 = Hard 17 (Ace counts as 1)");

        BlackjackHand multiAce = new BlackjackHand();
        multiAce.AddCard(new BlackjackCard(CardSuit.Spades, CardRank.Ace));
        multiAce.AddCard(new BlackjackCard(CardSuit.Hearts, CardRank.Ace));
        Assert(multiAce.CalculateValue() == 12, "Ace + Ace = 12");
        multiAce.AddCard(new BlackjackCard(CardSuit.Diamonds, CardRank.Nine));
        Assert(multiAce.CalculateValue() == 21, "Ace + Ace + 9 = 21");

        // 3. Natural Blackjack vs 3-card 21
        BlackjackHand natBj = new BlackjackHand();
        natBj.AddCard(new BlackjackCard(CardSuit.Spades, CardRank.Ace));
        natBj.AddCard(new BlackjackCard(CardSuit.Hearts, CardRank.King));
        Assert(natBj.IsNaturalBlackjack && !multiAce.IsNaturalBlackjack, "Natural Blackjack requires 2 cards totaling 21");

        // 4. Dealer behavior (< 17 hits, >= 17 stands) & Payouts
        BlackjackLogicEngine engine = new BlackjackLogicEngine(100);
        engine.EnterBettingState();
        Assert(!engine.TryAddBet(25m, 10m), "Prevents bets exceeding available balance");
        Assert(engine.TryAddBet(10m, 50m), "Allows valid bet within balance");

        engine.Deck.SetCustomDeckOrderForTesting(new[]
        {
            new BlackjackCard(CardSuit.Hearts, CardRank.Ten),     // Player 1
            new BlackjackCard(CardSuit.Clubs, CardRank.Ten),      // Dealer 1
            new BlackjackCard(CardSuit.Spades, CardRank.Eight),   // Player 2 (18)
            new BlackjackCard(CardSuit.Diamonds, CardRank.Six),   // Dealer 2 (16 -> must hit)
            new BlackjackCard(CardSuit.Clubs, CardRank.Two)       // Dealer draws 2 -> 18 (Push)
        });
        Assert(engine.StartRound(false), "Starts round with valid bet");
        Assert(engine.GetVisibleDealerScore() == 10 && !engine.IsDealerHoleCardRevealed, "Dealer hole card hidden during PlayerTurn");
        Assert(engine.BeginDealerTurn(), "Player stands -> DealerTurn");
        Assert(engine.ShouldDealerHit(), "Dealer hits on 16");
        engine.DealerHitStep();
        Assert(!engine.ShouldDealerHit(), "Dealer stands on 18 (>= 17)");
        Assert(engine.FinalizeDealerTurnAndResolve() == BlackjackRoundOutcome.Push && engine.LastPayout == 10m, "Push returns 1x bet");

        summary = sb.ToString();
        return allPassed;
    }
}
