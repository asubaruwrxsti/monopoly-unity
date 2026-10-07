using System.Linq;

namespace Monopoly.Core
{
    /// <summary>
    /// Simple computer opponent. <see cref="ChooseCommand"/> picks exactly one command at a time so the view
    /// can animate each step, and so CPU turns travel over the network like any other player's input.
    /// </summary>
    public static class CpuPlayer
    {
        /// <summary>Cash the CPU tries to keep in hand before spending on optional things.</summary>
        private const int Reserve = 150;

        /// <summary>
        /// Values each side of the offer from the CPU's point of view. Properties that complete a colour set are
        /// worth much more to whoever ends up holding them, so the CPU won't hand you a monopoly cheaply.
        /// </summary>
        public static bool WouldAccept(MonopolyGame game, TradeOffer offer)
        {
            int me = offer.To, them = offer.From;
            if (game.Players[me].Money < offer.GetCash) return false;

            float Value(int space, int newOwner, int oldOwner)
            {
                var def = BoardLayout.Spaces[space];
                float v = game.GetProperty(space).Mortgaged ? def.Price * 0.5f : def.Price;
                var group = BoardLayout.SpacesInGroup(def.Group);
                bool completes = group.All(i => i == space || game.GetProperty(i).Owner == newOwner
                                                || offer.GiveProperties.Contains(i) && newOwner == me
                                                || offer.GetProperties.Contains(i) && newOwner == them);
                return completes && group.Count > 1 ? v * 2.5f : v;
            }

            float gained = offer.GiveCash + offer.GiveProperties.Sum(i => Value(i, me, them));
            float given = offer.GetCash + offer.GetProperties.Sum(i => Value(i, them, me));
            return gained >= given * 1.15f + 10f;
        }

        public static GameCommand ChooseCommand(MonopolyGame game)
        {
            var me = game.CurrentPlayer;
            switch (game.Phase)
            {
                case TurnPhase.AwaitingRoll:
                    if (game.CanUseJailCard) return new GameCommand(CommandType.UseJailCard);
                    if (game.CanPayJailFine && me.Money >= 500) return new GameCommand(CommandType.PayJailFine);
                    return new GameCommand(CommandType.Roll);

                case TurnPhase.AwaitingBuyDecision:
                    var space = BoardLayout.Spaces[game.PendingPurchase];
                    bool completesSet = game.CountOwnedInGroup(me.Id, space.Group) == BoardLayout.SpacesInGroup(space.Group).Count - 1;
                    bool worthIt = me.Money - space.Price >= Reserve || completesSet;
                    return new GameCommand(game.CanBuyPending && worthIt ? CommandType.Buy : CommandType.Decline);

                case TurnPhase.AwaitingDebtPayment:
                    if (game.CanPayDebts) return new GameCommand(CommandType.PayDebts);
                    int sell = game.OwnedSpaces(me.Id).Where(game.CanSellHouse)
                                   .OrderByDescending(i => BoardLayout.Spaces[i].HouseCost).DefaultIfEmpty(-1).First();
                    if (sell >= 0) return new GameCommand(CommandType.SellHouse, sell);
                    // Mortgage the least valuable thing first, keeping complete colour sets for last.
                    int mortgage = game.OwnedSpaces(me.Id).Where(game.CanMortgage)
                                       .OrderBy(i => game.OwnsWholeGroup(me.Id, BoardLayout.Spaces[i].Group) ? 1 : 0)
                                       .ThenBy(i => BoardLayout.Spaces[i].MortgageValue).DefaultIfEmpty(-1).First();
                    if (mortgage >= 0) return new GameCommand(CommandType.Mortgage, mortgage);
                    return new GameCommand(CommandType.DeclareBankruptcy);

                case TurnPhase.AwaitingTradeResponse:
                    return new GameCommand(WouldAccept(game, game.PendingTrade) ? CommandType.AcceptTrade : CommandType.RejectTrade);

                case TurnPhase.AwaitingEndTurn:
                    int unmortgage = game.OwnedSpaces(me.Id)
                                         .Where(i => game.CanUnmortgage(i) && me.Money - BoardLayout.Spaces[i].UnmortgageCost >= Reserve * 2)
                                         .DefaultIfEmpty(-1).First();
                    if (unmortgage >= 0) return new GameCommand(CommandType.Unmortgage, unmortgage);
                    // Build one house at a time on the cheapest eligible street, keeping the reserve.
                    int build = game.OwnedSpaces(me.Id)
                                    .Where(i => game.CanBuildHouse(i) && me.Money - BoardLayout.Spaces[i].HouseCost >= Reserve)
                                    .OrderBy(i => BoardLayout.Spaces[i].HouseCost).DefaultIfEmpty(-1).First();
                    if (build >= 0) return new GameCommand(CommandType.BuildHouse, build);
                    return new GameCommand(CommandType.EndTurn);

                default:
                    return new GameCommand(CommandType.EndTurn);
            }
        }
    }
}
