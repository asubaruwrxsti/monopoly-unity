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
