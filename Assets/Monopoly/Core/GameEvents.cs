namespace Monopoly.Core
{
    /// <summary>
    /// Something the presentation layer should show. The engine resolves rules instantly and queues these;
    /// the view plays them back in order (animating dice, tokens, cards) before asking for the next input.
    /// </summary>
    public abstract class GameEvent { }

    public sealed class TurnStartedEvent : GameEvent
    {
        public int PlayerId { get; }
        public TurnStartedEvent(int playerId) { PlayerId = playerId; }
    }

    public sealed class DiceRolledEvent : GameEvent
    {
        public int PlayerId { get; }
        public int Die1 { get; }
        public int Die2 { get; }
        public DiceRolledEvent(int playerId, int die1, int die2) { PlayerId = playerId; Die1 = die1; Die2 = die2; }
    }

    public enum MoveKind { Forward, Backward, Direct }

    public sealed class TokenMovedEvent : GameEvent
    {
        public int PlayerId { get; }
        public int From { get; }
        public int To { get; }
        public MoveKind Kind { get; }
        public TokenMovedEvent(int playerId, int from, int to, MoveKind kind) { PlayerId = playerId; From = from; To = to; Kind = kind; }
    }

    public sealed class CardDrawnEvent : GameEvent
    {
        public int PlayerId { get; }
        public Card Card { get; }
        public CardDrawnEvent(int playerId, Card card) { PlayerId = playerId; Card = card; }
    }

    public sealed class LogEvent : GameEvent
    {
        public string Message { get; }
        /// <summary>Important messages (purchases, rent, jail...) are also surfaced as a toast.</summary>
        public bool Highlight { get; }
        public LogEvent(string message, bool highlight) { Message = message; Highlight = highlight; }
    }

    public sealed class MoneyChangedEvent : GameEvent
    {
        public int PlayerId { get; }
        public int Delta { get; }
        public int Balance { get; }
        public MoneyChangedEvent(int playerId, int delta, int balance) { PlayerId = playerId; Delta = delta; Balance = balance; }
    }

    /// <summary>Money moving directly between two players (rent, card payments, bankruptcy).</summary>
    public sealed class PaymentEvent : GameEvent
    {
        public int FromPlayer { get; }
        public int ToPlayer { get; }
        public int Amount { get; }
        public PaymentEvent(int fromPlayer, int toPlayer, int amount) { FromPlayer = fromPlayer; ToPlayer = toPlayer; Amount = amount; }
    }

    public sealed class PropertyBoughtEvent : GameEvent
    {
        public int PlayerId { get; }
        public int Space { get; }
        public PropertyBoughtEvent(int playerId, int space) { PlayerId = playerId; Space = space; }
    }

    public sealed class PassedGoEvent : GameEvent
    {
        public int PlayerId { get; }
        public PassedGoEvent(int playerId) { PlayerId = playerId; }
    }

    public sealed class BuildingChangedEvent : GameEvent
    {
        public int PlayerId { get; }
        public int Space { get; }
        public int Houses { get; }
        public bool Built { get; }
        public BuildingChangedEvent(int playerId, int space, int houses, bool built) { PlayerId = playerId; Space = space; Houses = houses; Built = built; }
    }

    public sealed class PlayerBankruptEvent : GameEvent
    {
        public int PlayerId { get; }
        public PlayerBankruptEvent(int playerId) { PlayerId = playerId; }
    }

    public sealed class TradeProposedEvent : GameEvent
    {
        public TradeOffer Offer { get; }
        public TradeProposedEvent(TradeOffer offer) { Offer = offer; }
    }

    public sealed class TradeResolvedEvent : GameEvent
    {
        public TradeOffer Offer { get; }
        public bool Accepted { get; }
        public TradeResolvedEvent(TradeOffer offer, bool accepted) { Offer = offer; Accepted = accepted; }
    }

    public sealed class GameWonEvent : GameEvent
    {
        public int PlayerId { get; }
        public GameWonEvent(int playerId) { PlayerId = playerId; }
    }
}
