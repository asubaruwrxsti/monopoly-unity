using System;
using System.Collections.Generic;
using System.Linq;
using Monopoly.Core;
using NUnit.Framework;

namespace Monopoly.Tests
{
    public class MonopolyGameTests
    {
        private Queue<int> dice;

        private MonopolyGame NewGame(int players = 2)
        {
            dice = new Queue<int>();
            var setup = Enumerable.Range(1, players).Select(i => new PlayerSetup($"P{i}", false)).ToList();
            var game = new MonopolyGame(setup, seed: 1, rollDie: () => dice.Dequeue());
            Drain(game);
            return game;
        }

        private void Roll(MonopolyGame game, int d1, int d2)
        {
            dice.Enqueue(d1);
            dice.Enqueue(d2);
            game.Roll();
            Drain(game);
        }

        private static List<GameEvent> Drain(MonopolyGame game)
        {
            var list = new List<GameEvent>();
            while (game.TryDequeueEvent(out var e)) list.Add(e);
            return list;
        }

        private static void DeclineAndNobodyBids(MonopolyGame game)
        {
            game.DeclinePendingProperty();
            while (game.Phase == TurnPhase.AwaitingAuctionBid) game.PassAuction();
            Drain(game);
        }

        private static void PutCardOnTop(MonopolyGame game, CardDeck deck, CardAction action)
        {
            var pile = game.DeckFor(deck);
            var card = pile.First(c => c.Action == action);
            pile.Remove(card);
            pile.Insert(0, card);
        }

        [Test]
        public void Landing_on_unowned_property_offers_purchase_and_buying_transfers_ownership()
        {
            var game = NewGame();
            Roll(game, 1, 2); // Whitechapel Road (3)

            Assert.AreEqual(TurnPhase.AwaitingBuyDecision, game.Phase);
            Assert.AreEqual(3, game.PendingPurchase);

            game.BuyPendingProperty();
            Assert.AreEqual(0, game.GetProperty(3).Owner);
            Assert.AreEqual(1440, game.Players[0].Money);
            Assert.AreEqual(TurnPhase.AwaitingEndTurn, game.Phase);
        }

        [Test]
        public void Passing_go_collects_salary()
        {
            var game = NewGame();
            game.SetPosition(0, 36);
            Roll(game, 2, 3); // 36 -> 1 (Old Kent Road)
            Assert.AreEqual(1, game.Players[0].Position);
            Assert.AreEqual(1700, game.Players[0].Money);
        }

        [Test]
        public void Rent_is_paid_to_owner_and_doubles_with_a_full_colour_set()
        {
            var game = NewGame();
            game.SetOwner(3, playerId: 1);
            Roll(game, 1, 2); // Whitechapel Road

            Assert.AreEqual(1500 - 4, game.Players[0].Money);
            Assert.AreEqual(1500 + 4, game.Players[1].Money);

            game.SetOwner(1, playerId: 1);
            Assert.AreEqual(8, game.CalculateRent(3, 7), "Unimproved rent doubles with the whole set");
        }

        [Test]
        public void Rent_with_houses_and_hotel_uses_rent_table()
        {
            var game = NewGame();
            game.SetOwner(37, 1, houses: 3);
            game.SetOwner(39, 1, houses: 5);
            Assert.AreEqual(1100, game.CalculateRent(37, 7));
            Assert.AreEqual(2000, game.CalculateRent(39, 7));
        }

        [Test]
        public void Station_and_utility_rent_scale_with_number_owned()
        {
            var game = NewGame();
            game.SetOwner(5, 1);
            Assert.AreEqual(25, game.CalculateRent(5, 7));
            game.SetOwner(15, 1);
            game.SetOwner(25, 1);
            Assert.AreEqual(100, game.CalculateRent(5, 7));

            game.SetOwner(12, 1);
            Assert.AreEqual(28, game.CalculateRent(12, 7));
            game.SetOwner(28, 1);
            Assert.AreEqual(70, game.CalculateRent(12, 7));
        }

        [Test]
        public void Mortgaged_property_charges_no_rent()
        {
            var game = NewGame();
            game.SetOwner(39, 1);
            game.GetProperty(39).Mortgaged = true;
            Assert.AreEqual(0, game.CalculateRent(39, 7));
        }

        [Test]
        public void Doubles_grant_another_roll_and_three_doubles_send_you_to_jail()
        {
            var game = NewGame();
            Roll(game, 2, 2); // 4: Income Tax
            Assert.AreEqual(TurnPhase.AwaitingRoll, game.Phase);
            Roll(game, 3, 3); // 10: Just visiting
            Assert.AreEqual(TurnPhase.AwaitingRoll, game.Phase);
            Roll(game, 1, 1);

            Assert.IsTrue(game.Players[0].InJail);
            Assert.AreEqual(BoardLayout.JailIndex, game.Players[0].Position);
            Assert.AreEqual(TurnPhase.AwaitingEndTurn, game.Phase);
        }

        [Test]
        public void Go_to_jail_space_jails_the_player()
        {
            var game = NewGame();
            game.SetPosition(0, 25);
            Roll(game, 2, 3);
            Assert.IsTrue(game.Players[0].InJail);
            Assert.AreEqual(BoardLayout.JailIndex, game.Players[0].Position);
        }

        [Test]
        public void Third_failed_jail_roll_forces_fine_and_moves()
        {
            var game = NewGame();
            game.SetPosition(0, 25);
            Roll(game, 2, 3); // to jail
            for (int turn = 0; turn < 2; turn++)
            {
                game.EndTurn(); Drain(game);
                Roll(game, 1, 2); DeclineAndNobodyBids(game); // P2 moves along
                game.EndTurn(); Drain(game);
                Roll(game, 1, 2);
                Assert.IsTrue(game.Players[0].InJail);
            }
            game.EndTurn(); Drain(game);
            Roll(game, 4, 1); if (game.Phase == TurnPhase.AwaitingBuyDecision) DeclineAndNobodyBids(game);
            game.EndTurn(); Drain(game);

            Roll(game, 1, 2);
            Assert.IsFalse(game.Players[0].InJail);
            Assert.AreEqual(13, game.Players[0].Position);
            Assert.AreEqual(1500 - 50, game.Players[0].Money);
        }

        [Test]
        public void Houses_must_be_built_evenly_and_require_full_set()
        {
            var game = NewGame();
            game.SetOwner(1, 0);
            Assert.IsFalse(game.CanBuildHouse(1), "Need the whole colour set");

            game.SetOwner(3, 0);
            Assert.IsTrue(game.CanBuildHouse(1));
            game.BuildHouse(1);
            Assert.IsFalse(game.CanBuildHouse(1), "Must build evenly");
            Assert.IsTrue(game.CanBuildHouse(3));
            Assert.IsFalse(game.CanMortgage(3), "Can't mortgage while the set has buildings");

            Assert.IsTrue(game.CanSellHouse(1));
            game.SellHouse(1);
            Assert.AreEqual(1500 - 50 + 25, game.Players[0].Money);
        }

        [Test]
        public void Mortgage_and_unmortgage_move_money_with_interest()
        {
            var game = NewGame();
            game.SetOwner(39, 0);
            game.Mortgage(39);
            Assert.AreEqual(1700, game.Players[0].Money);
            game.Unmortgage(39);
            Assert.AreEqual(1700 - 220, game.Players[0].Money);
        }

        [Test]
        public void Unaffordable_rent_becomes_debt_and_bankruptcy_hands_assets_to_creditor()
        {
            var game = NewGame();
            game.SetOwner(39, 1, houses: 5);
            game.SetOwner(1, 0);
            game.SetMoney(0, 100);
            game.SetPosition(0, 34);
            Roll(game, 2, 3); // Mayfair with hotel: $2000

            Assert.AreEqual(TurnPhase.AwaitingDebtPayment, game.Phase);
            Assert.AreEqual(2000, game.DebtTotal);
            Assert.IsFalse(game.CanPayDebts);

            game.DeclareBankruptcy();
            Drain(game);
            Assert.IsTrue(game.Players[0].IsBankrupt);
            Assert.AreEqual(1, game.GetProperty(1).Owner);
            Assert.AreEqual(TurnPhase.GameOver, game.Phase);
            Assert.AreEqual(game.Players[1], game.Winner);
        }

        [Test]
        public void Debt_can_be_paid_after_mortgaging()
        {
            var game = NewGame();
            game.SetOwner(1, 0);
            game.SetMoney(0, 100);
            game.SetPosition(0, 1);
            Roll(game, 1, 2); // Income Tax $200

            Assert.AreEqual(TurnPhase.AwaitingDebtPayment, game.Phase);
            game.Mortgage(1);
            Assert.IsFalse(game.CanPayDebts);
            game.SetMoney(0, 250);
            game.PayDebts();
            Assert.AreEqual(50, game.Players[0].Money);
            Assert.AreEqual(TurnPhase.AwaitingEndTurn, game.Phase);
        }

        [Test]
        public void Nearest_station_card_charges_double_rent()
        {
            var game = NewGame();
            game.SetOwner(15, 1);
            PutCardOnTop(game, CardDeck.Chance, CardAction.AdvanceToNearestStation);
            Roll(game, 3, 4); // Chance at 7 -> Marylebone (15)

            Assert.AreEqual(15, game.Players[0].Position);
            Assert.AreEqual(1500 - 50, game.Players[0].Money);
            Assert.AreEqual(1500 + 50, game.Players[1].Money);
        }

        [Test]
        public void Get_out_of_jail_card_is_kept_and_can_be_used()
        {
            var game = NewGame();
            PutCardOnTop(game, CardDeck.CommunityChest, CardAction.GetOutOfJailFree);
            Roll(game, 1, 1); // CC at 2
            Assert.AreEqual(1, game.Players[0].JailCards.Count);
            Assert.IsFalse(game.DeckFor(CardDeck.CommunityChest).Any(c => c.Action == CardAction.GetOutOfJailFree));

            Assert.AreEqual(TurnPhase.AwaitingRoll, game.Phase, "Doubles roll again");

            game.SetPosition(0, BoardLayout.JailIndex);
            game.Players[0].InJail = true;
            game.UseJailCard();
            Assert.IsFalse(game.Players[0].InJail);
            Assert.AreEqual(0, game.Players[0].JailCards.Count);
            Assert.IsTrue(game.DeckFor(CardDeck.CommunityChest).Any(c => c.Action == CardAction.GetOutOfJailFree));
        }

        [Test]
        public void Birthday_card_bankrupts_an_opponent_who_cannot_pay()
        {
            var game = NewGame(3);
            game.SetMoney(1, 0);
            PutCardOnTop(game, CardDeck.CommunityChest, CardAction.CollectFromEachPlayer);
            Roll(game, 1, 1);

            Assert.IsTrue(game.Players[1].IsBankrupt);
            Assert.AreEqual(1490, game.Players[2].Money);
            Assert.AreEqual(1510, game.Players[0].Money);
            Assert.AreNotEqual(TurnPhase.GameOver, game.Phase);
        }

        [Test]
        public void Simulated_cpu_games_never_break_invariants([Values(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)] int seed)
        {
            var setup = Enumerable.Range(1, 4).Select(i => new PlayerSetup($"CPU {i}", true)).ToList();
            var game = new MonopolyGame(setup, seed);

            for (int step = 0; step < 20000 && !game.IsOver; step++)
            {
                var cmd = CpuPlayer.ChooseCommand(game);
                Assert.IsTrue(game.IsLegal(cmd), $"CPU chose illegal {cmd} in {game.Phase}");
                game.Execute(cmd);
                Drain(game);

                for (int i = 0; i < BoardLayout.SpaceCount; i++)
                {
                    var st = game.GetProperty(i);
                    if (st == null) continue;
                    Assert.That(st.Houses, Is.InRange(0, 5));
                    if (st.IsOwned) Assert.IsFalse(game.Players[st.Owner].IsBankrupt, "Bankrupt players can't own property");
                    if (st.Mortgaged) Assert.AreEqual(0, st.Houses);
                }
                foreach (var p in game.Players)
                {
                    Assert.GreaterOrEqual(p.Money, 0, $"{p.Name} has negative cash");
                    Assert.That(p.Position, Is.InRange(0, 39));
                }
                Assert.IsFalse(game.CurrentPlayer.IsBankrupt || (game.IsOver && game.Winner == null));
            }
        }

        [Test]
        public void Replaying_the_same_commands_with_the_same_seed_gives_identical_state()
        {
            var setup = Enumerable.Range(1, 3).Select(i => new PlayerSetup($"P{i}", true)).ToList();
            var host = new MonopolyGame(setup, seed: 42);
            var client = new MonopolyGame(setup, seed: 42);

            for (int step = 0; step < 3000 && !host.IsOver; step++)
            {
                var cmd = CpuPlayer.ChooseCommand(host);
                host.Execute(cmd);
                Assert.IsTrue(client.IsLegal(cmd));
                client.Execute(cmd);
                Assert.AreEqual(host.StateHash(), client.StateHash(), $"Desync after step {step} ({cmd})");
            }
        }

        [Test]
        public void Accepted_trade_swaps_properties_and_cash()
        {
            var game = NewGame();
            game.SetOwner(1, 0);
            game.SetOwner(39, 1);
            var offer = new TradeOffer(0, 1, new[] { 1 }, new[] { 39 }, giveCash: 300, getCash: 0);
            Assert.IsTrue(game.IsLegal(GameCommand.Trade(offer)));

            game.Execute(GameCommand.Trade(offer));
            Assert.AreEqual(TurnPhase.AwaitingTradeResponse, game.Phase);
            Assert.AreEqual(1, game.ActingPlayerIndex);
            Assert.IsFalse(game.IsLegal(new GameCommand(CommandType.Roll)), "Nothing else happens while a trade is pending");

            game.Execute(new GameCommand(CommandType.AcceptTrade));
            Assert.AreEqual(TurnPhase.AwaitingRoll, game.Phase);
            Assert.AreEqual(0, game.GetProperty(39).Owner);
            Assert.AreEqual(1, game.GetProperty(1).Owner);
            Assert.AreEqual(1200, game.Players[0].Money);
            Assert.AreEqual(1800, game.Players[1].Money);
        }

        [Test]
        public void Rejected_trade_changes_nothing()
        {
            var game = NewGame();
            game.SetOwner(39, 1);
            game.Execute(GameCommand.Trade(new TradeOffer(0, 1, new int[0], new[] { 39 }, 100, 0)));
            game.Execute(new GameCommand(CommandType.RejectTrade));
            Assert.AreEqual(1, game.GetProperty(39).Owner);
            Assert.AreEqual(1500, game.Players[0].Money);
            Assert.AreEqual(TurnPhase.AwaitingRoll, game.Phase);
        }

        [Test]
        public void Trades_with_buildings_or_unaffordable_cash_are_illegal()
        {
            var game = NewGame();
            game.SetOwner(1, 1, houses: 1);
            game.SetOwner(3, 1);
            Assert.IsFalse(game.CanProposeTrade(new TradeOffer(0, 1, new int[0], new[] { 3 }, 100, 0)), "Group has a house");
            Assert.IsFalse(game.CanProposeTrade(new TradeOffer(0, 1, new int[0], new int[0], 5000, 0)), "Can't give more cash than you have");
            Assert.IsFalse(game.CanProposeTrade(new TradeOffer(0, 1, new int[0], new int[0], 0, 0)), "Empty offer");
            Assert.IsFalse(game.CanProposeTrade(new TradeOffer(1, 0, new int[0], new int[0], 10, 0)), "Only the current player proposes");
        }

        [Test]
        public void Cpu_refuses_to_complete_your_colour_set_cheaply()
        {
            var game = NewGame();
            game.SetOwner(37, 0);
            game.SetOwner(39, 1);
            Assert.IsFalse(CpuPlayer.WouldAccept(game, new TradeOffer(0, 1, new int[0], new[] { 39 }, 450, 0)));
            Assert.IsTrue(CpuPlayer.WouldAccept(game, new TradeOffer(0, 1, new int[0], new[] { 39 }, 1300, 0)));
        }

        [Test]
        public void Declined_property_is_auctioned_to_the_highest_bidder()
        {
            var game = NewGame(3);
            Roll(game, 1, 2); // P1 lands on Whitechapel Road ($60)
            game.DeclinePendingProperty();
            Assert.AreEqual(TurnPhase.AwaitingAuctionBid, game.Phase);
            Assert.AreEqual(1, game.ActingPlayerIndex, "Bidding starts with the next player");

            Assert.IsFalse(game.CanBid(5), "Below the minimum bid");
            game.Bid(20);                    // P2
            Assert.AreEqual(2, game.ActingPlayerIndex);
            Assert.IsFalse(game.CanBid(20), "Must beat the high bid");
            game.Bid(35);                    // P3
            game.PassAuction();              // P1 (who declined) drops out
            game.Bid(40);                    // P2
            game.PassAuction();              // P3 drops out: P2 wins

            Assert.AreEqual(1, game.GetProperty(3).Owner);
            Assert.AreEqual(1460, game.Players[1].Money);
            Assert.AreEqual(TurnPhase.AwaitingEndTurn, game.Phase);
            Assert.AreEqual(0, game.CurrentPlayerIndex, "Still the landing player's turn");
        }

        [Test]
        public void Auction_with_no_bids_leaves_property_with_the_bank()
        {
            var game = NewGame(2);
            Roll(game, 1, 2);
            DeclineAndNobodyBids(game);
            Assert.IsFalse(game.GetProperty(3).IsOwned);
            Assert.AreEqual(TurnPhase.AwaitingEndTurn, game.Phase);
        }

        [Test]
        public void Illegal_commands_are_rejected()
        {
            var game = NewGame();
            Assert.IsFalse(game.IsLegal(new GameCommand(CommandType.EndTurn)));
            Assert.IsFalse(game.IsLegal(new GameCommand(CommandType.Buy)));
            Assert.IsFalse(game.IsLegal(new GameCommand(CommandType.BuildHouse, 99)));
            Assert.IsFalse(game.IsLegal(new GameCommand(CommandType.Mortgage, 0)));
            Assert.IsTrue(game.IsLegal(new GameCommand(CommandType.Roll)));
        }
    }
}
