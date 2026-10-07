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
