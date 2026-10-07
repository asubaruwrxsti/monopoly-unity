namespace Monopoly.Core
{
    public enum CommandType : byte
    {
        Roll,
        PayJailFine,
        UseJailCard,
        Buy,
        Decline,
        PayDebts,
        DeclareBankruptcy,
        EndTurn,
        BuildHouse,
        SellHouse,
        Mortgage,
        Unmortgage,
        ProposeTrade,
        AcceptTrade,
        RejectTrade,
        AuctionBid,
        AuctionPass,
    }

    /// <summary>
    /// One player decision. Everything that changes game state goes through a command, which makes the
    /// game easy to replay deterministically on every networked peer.
    /// </summary>
    public readonly struct GameCommand
    {
        public CommandType Type { get; }
        /// <summary>Target space for property-management commands, otherwise -1.</summary>
        public int Space { get; }
        /// <summary>The offer for <see cref="CommandType.ProposeTrade"/>, otherwise null.</summary>
        public TradeOffer Offer { get; }
        /// <summary>The bid for <see cref="CommandType.AuctionBid"/>.</summary>
        public int Amount { get; }

        public GameCommand(CommandType type, int space = -1, TradeOffer offer = null, int amount = 0)
        {
            Type = type;
            Space = space;
            Offer = offer;
            Amount = amount;
        }

        public static GameCommand Bid(int amount) => new GameCommand(CommandType.AuctionBid, -1, null, amount);

        public static GameCommand Trade(TradeOffer offer) => new GameCommand(CommandType.ProposeTrade, -1, offer);

        public override string ToString() => Space >= 0 ? $"{Type}({Space})" : Type.ToString();
    }

    public static class GameCommandExtensions
    {
        public static bool IsLegal(this MonopolyGame game, GameCommand cmd)
        {
            bool validSpace = cmd.Space >= 0 && cmd.Space < BoardLayout.SpaceCount && game.GetProperty(cmd.Space) != null
                              && game.Phase != TurnPhase.AwaitingTradeResponse && game.Phase != TurnPhase.AwaitingAuctionBid;
            switch (cmd.Type)
            {
                case CommandType.Roll: return game.CanRoll;
                case CommandType.PayJailFine: return game.CanPayJailFine;
                case CommandType.UseJailCard: return game.CanUseJailCard;
                case CommandType.Buy: return game.CanBuyPending;
                case CommandType.Decline: return game.Phase == TurnPhase.AwaitingBuyDecision;
                case CommandType.PayDebts: return game.CanPayDebts;
                case CommandType.DeclareBankruptcy: return game.Phase == TurnPhase.AwaitingDebtPayment;
                case CommandType.EndTurn: return game.CanEndTurn;
                case CommandType.BuildHouse: return validSpace && game.CanBuildHouse(cmd.Space);
                case CommandType.SellHouse: return validSpace && game.CanSellHouse(cmd.Space);
                case CommandType.Mortgage: return validSpace && game.CanMortgage(cmd.Space);
                case CommandType.Unmortgage: return validSpace && game.CanUnmortgage(cmd.Space);
                case CommandType.ProposeTrade: return game.CanProposeTrade(cmd.Offer);
                case CommandType.AcceptTrade:
                case CommandType.RejectTrade: return game.Phase == TurnPhase.AwaitingTradeResponse;
                case CommandType.AuctionBid: return game.CanBid(cmd.Amount);
                case CommandType.AuctionPass: return game.Phase == TurnPhase.AwaitingAuctionBid;
                default: return false;
            }
        }

        public static void Execute(this MonopolyGame game, GameCommand cmd)
        {
            switch (cmd.Type)
            {
                case CommandType.Roll: game.Roll(); break;
                case CommandType.PayJailFine: game.PayJailFine(); break;
                case CommandType.UseJailCard: game.UseJailCard(); break;
                case CommandType.Buy: game.BuyPendingProperty(); break;
                case CommandType.Decline: game.DeclinePendingProperty(); break;
                case CommandType.PayDebts: game.PayDebts(); break;
                case CommandType.DeclareBankruptcy: game.DeclareBankruptcy(); break;
                case CommandType.EndTurn: game.EndTurn(); break;
                case CommandType.BuildHouse: game.BuildHouse(cmd.Space); break;
                case CommandType.SellHouse: game.SellHouse(cmd.Space); break;
                case CommandType.Mortgage: game.Mortgage(cmd.Space); break;
                case CommandType.Unmortgage: game.Unmortgage(cmd.Space); break;
                case CommandType.ProposeTrade: game.ProposeTrade(cmd.Offer); break;
                case CommandType.AcceptTrade: game.RespondToTrade(true); break;
                case CommandType.RejectTrade: game.RespondToTrade(false); break;
                case CommandType.AuctionBid: game.Bid(cmd.Amount); break;
                case CommandType.AuctionPass: game.PassAuction(); break;
            }
        }
    }
}
