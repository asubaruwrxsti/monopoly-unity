using System.Collections.Generic;

namespace Monopoly.Core
{
    public enum TurnPhase
    {
        /// <summary>Current player must roll (or, if jailed, may pay the fine / use a card first).</summary>
        AwaitingRoll,
        /// <summary>Current player landed on an unowned space and must buy or decline it.</summary>
        AwaitingBuyDecision,
        /// <summary>Current player owes money they don't have; they must raise funds or go bankrupt.</summary>
        AwaitingDebtPayment,
        /// <summary>Nothing left to resolve this turn.</summary>
        AwaitingEndTurn,
        /// <summary>The current player offered a trade; <see cref="MonopolyGame.ActingPlayerIndex"/> must accept or reject it.</summary>
        AwaitingTradeResponse,
        /// <summary>A declined property is being auctioned; <see cref="MonopolyGame.ActingPlayerIndex"/> must bid or pass.</summary>
        AwaitingAuctionBid,
        GameOver,
    }

    public sealed class PlayerSetup
    {
        public string Name;
        public bool IsCpu;

        public PlayerSetup(string name, bool isCpu)
        {
            Name = name;
            IsCpu = isCpu;
        }
    }

    public sealed class PlayerState
    {
        public int Id { get; }
        public string Name { get; }
        public bool IsCpu { get; }
        public int Money { get; internal set; }
        public int Position { get; internal set; }
        public bool InJail { get; internal set; }
        public int JailAttempts { get; internal set; }
        public bool IsBankrupt { get; internal set; }
        /// <summary>Get Out of Jail Free cards held, tagged by the deck they return to.</summary>
        public List<CardDeck> JailCards { get; } = new List<CardDeck>();

        public PlayerState(int id, string name, bool isCpu, int money)
        {
            Id = id;
            Name = name;
            IsCpu = isCpu;
            Money = money;
        }
    }

    public sealed class PropertyState
    {
        public const int Unowned = -1;

        public int Owner { get; internal set; } = Unowned;
        /// <summary>0-4 houses, 5 = hotel.</summary>
        public int Houses { get; internal set; }
        public bool Mortgaged { get; internal set; }

        public bool IsOwned => Owner != Unowned;
        public bool HasHotel => Houses == BoardLayout.HotelLevel;
    }

    /// <summary>An open auction for a property the landing player declined.</summary>
    public sealed class AuctionState
    {
        public const int MinimumBid = 10;

        public int Space { get; }
        public int HighBid { get; internal set; }
        /// <summary>Player id of the highest bidder, or -1 if nobody has bid yet.</summary>
        public int HighBidder { get; internal set; } = -1;
        /// <summary>Players still in the auction, in bidding order.</summary>
        public List<int> Bidders { get; }
        internal int Turn;

        public int CurrentBidder => Bidders[Turn];
        public int NextMinimumBid => HighBidder < 0 ? MinimumBid : HighBid + 1;

        public AuctionState(int space, List<int> bidders)
        {
            Space = space;
            Bidders = bidders;
        }
    }

    public sealed class Debt
    {
        /// <summary>Player id, or -1 for the bank.</summary>
        public int Creditor { get; }
        public int Amount { get; }

        public Debt(int creditor, int amount)
        {
            Creditor = creditor;
            Amount = amount;
        }
    }
}
