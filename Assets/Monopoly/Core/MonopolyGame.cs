using System;
using System.Collections.Generic;
using System.Linq;

namespace Monopoly.Core
{
    /// <summary>
    /// The complete rules of Monopoly as a plain state machine. Commands (Roll, Buy, EndTurn...) validate the
    /// current <see cref="Phase"/>, mutate state and queue <see cref="GameEvent"/>s for the view to replay.
    /// Has no Unity dependency so it can be unit tested and simulated headlessly.
    /// </summary>
    public sealed class MonopolyGame
    {
        private readonly List<PlayerState> players;
        private readonly PropertyState[] properties = new PropertyState[BoardLayout.SpaceCount];
        private readonly Dictionary<CardDeck, List<Card>> decks = new Dictionary<CardDeck, List<Card>>();
        private readonly List<Debt> debts = new List<Debt>();
        private readonly Queue<GameEvent> events = new Queue<GameEvent>();
        private readonly Func<int> rollDie;

        private int doublesCount;
        private bool extraRollPending;

        public IReadOnlyList<PlayerState> Players => players;
        public int CurrentPlayerIndex { get; private set; }
        public PlayerState CurrentPlayer => players[CurrentPlayerIndex];
        public TurnPhase Phase { get; private set; }
        public int Die1 { get; private set; }
        public int Die2 { get; private set; }
        public int DiceTotal => Die1 + Die2;
        /// <summary>Space index awaiting a buy decision, or -1.</summary>
        public int PendingPurchase { get; private set; } = -1;
        public IReadOnlyList<Debt> Debts => debts;
        public int DebtTotal => debts.Sum(d => d.Amount);
        public PlayerState Winner { get; private set; }

        /// <param name="seed">Seed for dice and card shuffles; null for a random game.</param>
        /// <param name="rollDie">Optional dice override returning 1-6 (used by tests).</param>
        public MonopolyGame(IList<PlayerSetup> setup, int? seed = null, Func<int> rollDie = null)
        {
            if (setup == null || setup.Count < 2) throw new ArgumentException("Monopoly needs at least two players.");

            var rng = seed.HasValue ? new Random(seed.Value) : new Random();
            this.rollDie = rollDie ?? (() => rng.Next(1, 7));

            players = setup.Select((p, i) => new PlayerState(i, p.Name, p.IsCpu, BoardLayout.StartingMoney)).ToList();
            foreach (var space in BoardLayout.Spaces)
                if (space.IsOwnable) properties[space.Index] = new PropertyState();

            foreach (CardDeck deck in Enum.GetValues(typeof(CardDeck)))
                decks[deck] = CardLibrary.Create(deck).OrderBy(_ => rng.Next()).ToList();

            Phase = TurnPhase.AwaitingRoll;
            Emit(new TurnStartedEvent(0));
            Log($"{CurrentPlayer.Name} goes first.");
        }

        // ---------------------------------------------------------------- queries

        public PropertyState GetProperty(int space) => properties[space];

        public bool TryDequeueEvent(out GameEvent e)
        {
            if (events.Count > 0) { e = events.Dequeue(); return true; }
            e = null;
            return false;
        }

        public IEnumerable<int> OwnedSpaces(int playerId)
            => BoardLayout.Spaces.Where(s => s.IsOwnable && properties[s.Index].Owner == playerId).Select(s => s.Index);

        public bool OwnsWholeGroup(int playerId, ColorGroup group)
            => BoardLayout.SpacesInGroup(group).All(i => properties[i].Owner == playerId);

        public int CountOwnedInGroup(int playerId, ColorGroup group)
            => BoardLayout.SpacesInGroup(group).Count(i => properties[i].Owner == playerId);

        /// <summary>Rent owed for landing on <paramref name="space"/> with the given dice total.</summary>
        public int CalculateRent(int space, int diceTotal)
        {
            var def = BoardLayout.Spaces[space];
            var state = properties[space];
            if (state == null || !state.IsOwned || state.Mortgaged) return 0;

            switch (def.Type)
            {
                case SpaceType.Property:
                    if (state.Houses > 0) return def.Rent[state.Houses];
                    return OwnsWholeGroup(state.Owner, def.Group) ? def.Rent[0] * 2 : def.Rent[0];
                case SpaceType.Station:
                    return def.Rent[CountOwnedInGroup(state.Owner, ColorGroup.Station) - 1];
                case SpaceType.Utility:
                    return diceTotal * (CountOwnedInGroup(state.Owner, ColorGroup.Utility) == 2 ? 10 : 4);
                default:
                    return 0;
            }
        }

        /// <summary>Cash plus everything the player could raise by selling buildings and mortgaging.</summary>
        public int LiquidationValue(int playerId)
        {
            int total = players[playerId].Money;
            foreach (int i in OwnedSpaces(playerId))
            {
                var def = BoardLayout.Spaces[i];
                var st = properties[i];
                total += st.Houses * def.HouseCost / 2;
                if (!st.Mortgaged) total += def.MortgageValue;
            }
            return total;
        }

        public int NetWorth(int playerId)
        {
            int total = players[playerId].Money;
            foreach (int i in OwnedSpaces(playerId))
            {
                var def = BoardLayout.Spaces[i];
                var st = properties[i];
                total += st.Houses * def.HouseCost + (st.Mortgaged ? def.MortgageValue : def.Price);
            }
            return total;
        }

        public bool CanRoll => Phase == TurnPhase.AwaitingRoll;
        public bool CanPayJailFine => Phase == TurnPhase.AwaitingRoll && CurrentPlayer.InJail && CurrentPlayer.Money >= BoardLayout.JailFine;
        public bool CanUseJailCard => Phase == TurnPhase.AwaitingRoll && CurrentPlayer.InJail && CurrentPlayer.JailCards.Count > 0;
        public bool CanBuyPending => Phase == TurnPhase.AwaitingBuyDecision && debts.Count == 0
                                     && CurrentPlayer.Money >= BoardLayout.Spaces[PendingPurchase].Price;
        public bool CanPayDebts => Phase == TurnPhase.AwaitingDebtPayment && CurrentPlayer.Money >= debts[0].Amount;
        public bool CanEndTurn => Phase == TurnPhase.AwaitingEndTurn;
        public bool IsOver => Phase == TurnPhase.GameOver;

        // ---------------------------------------------------------------- property management

        public bool CanBuildHouse(int space) => CanBuildHouse(CurrentPlayer, space);
        public bool CanSellHouse(int space) => CanSellHouse(CurrentPlayer, space);
        public bool CanMortgage(int space) => CanMortgage(CurrentPlayer, space);
        public bool CanUnmortgage(int space) => !IsOver && OwnedBy(CurrentPlayer, space) && properties[space].Mortgaged
                                                && debts.Count == 0 && CurrentPlayer.Money >= BoardLayout.Spaces[space].UnmortgageCost;

        public void BuildHouse(int space)
        {
            Require(CanBuildHouse(space), "Can't build there.");
            var def = BoardLayout.Spaces[space];
            ChangeMoney(CurrentPlayer, -def.HouseCost);
            properties[space].Houses++;
            Emit(new BuildingChangedEvent(CurrentPlayer.Id, space, properties[space].Houses, true));
            Log(properties[space].HasHotel
                ? $"{CurrentPlayer.Name} built a hotel on {def.Name}."
                : $"{CurrentPlayer.Name} built a house on {def.Name}.");
        }

        public void SellHouse(int space)
        {
            Require(CanSellHouse(space), "Can't sell a building there.");
            SellHouseFor(CurrentPlayer, space);
        }

        public void Mortgage(int space)
        {
            Require(CanMortgage(space), "Can't mortgage that.");
            MortgageFor(CurrentPlayer, space);
        }

        public void Unmortgage(int space)
        {
            Require(CanUnmortgage(space), "Can't unmortgage that.");
            var def = BoardLayout.Spaces[space];
            ChangeMoney(CurrentPlayer, -def.UnmortgageCost);
            properties[space].Mortgaged = false;
            Log($"{CurrentPlayer.Name} paid off the mortgage on {def.Name} (${def.UnmortgageCost}).");
        }

        internal bool CanBuildHouse(PlayerState p, int space)
        {
            if (IsOver || !OwnedBy(p, space) || debts.Count > 0) return false;
            var def = BoardLayout.Spaces[space];
            var st = properties[space];
            if (def.Type != SpaceType.Property || st.Houses >= BoardLayout.HotelLevel || p.Money < def.HouseCost) return false;
            if (!OwnsWholeGroup(p.Id, def.Group)) return false;
            var group = BoardLayout.SpacesInGroup(def.Group);
            if (group.Any(i => properties[i].Mortgaged)) return false;
            // Build evenly: only on a property with the fewest buildings in its group.
            return st.Houses == group.Min(i => properties[i].Houses);
        }

        internal bool CanSellHouse(PlayerState p, int space)
        {
            if (IsOver || !OwnedBy(p, space)) return false;
            var def = BoardLayout.Spaces[space];
            var st = properties[space];
            if (st.Houses == 0) return false;
            // Sell evenly: only from a property with the most buildings in its group.
            return st.Houses == BoardLayout.SpacesInGroup(def.Group).Max(i => properties[i].Houses);
        }

        internal bool CanMortgage(PlayerState p, int space)
        {
            if (IsOver || !OwnedBy(p, space) || properties[space].Mortgaged) return false;
            var def = BoardLayout.Spaces[space];
            // All buildings in the colour group must be sold first.
            return def.Type != SpaceType.Property || BoardLayout.SpacesInGroup(def.Group).All(i => properties[i].Houses == 0);
        }

        internal void SellHouseFor(PlayerState p, int space)
        {
            var def = BoardLayout.Spaces[space];
            properties[space].Houses--;
            ChangeMoney(p, def.HouseCost / 2);
            Emit(new BuildingChangedEvent(p.Id, space, properties[space].Houses, false));
            Log($"{p.Name} sold a building on {def.Name} for ${def.HouseCost / 2}.");
        }

        internal void MortgageFor(PlayerState p, int space)
        {
            var def = BoardLayout.Spaces[space];
            properties[space].Mortgaged = true;
            ChangeMoney(p, def.MortgageValue);
            Log($"{p.Name} mortgaged {def.Name} for ${def.MortgageValue}.");
        }

        /// <summary>
        /// Sells buildings, then mortgages properties, until the player has at least <paramref name="target"/> cash
        /// or runs out of assets. Used for CPU players and for debts owed outside the debtor's own turn.
        /// </summary>
        internal void AutoRaiseFunds(PlayerState p, int target)
        {
            while (p.Money < target)
            {
                int sell = OwnedSpaces(p.Id).Where(i => CanSellHouse(p, i))
                                            .OrderByDescending(i => BoardLayout.Spaces[i].HouseCost).DefaultIfEmpty(-1).First();
                if (sell >= 0) { SellHouseFor(p, sell); continue; }

                // Mortgage the least valuable thing first, keeping complete colour sets for last.
                int mortgage = OwnedSpaces(p.Id).Where(i => CanMortgage(p, i))
                                                .OrderBy(i => OwnsWholeGroup(p.Id, BoardLayout.Spaces[i].Group) ? 1 : 0)
                                                .ThenBy(i => BoardLayout.Spaces[i].MortgageValue)
                                                .DefaultIfEmpty(-1).First();
                if (mortgage >= 0) { MortgageFor(p, mortgage); continue; }
                return;
            }
        }

        // ---------------------------------------------------------------- turn commands

        public void Roll()
        {
            Require(CanRoll, "You can't roll right now.");
            var p = CurrentPlayer;
            RollDice(p);
            bool doubles = Die1 == Die2;

            if (p.InJail)
            {
                extraRollPending = false; // Leaving jail never grants a bonus roll.
                if (doubles)
                {
                    ReleaseFromJail(p);
                    Log($"{p.Name} rolled doubles and walks out of jail.", true);
                }
                else if (++p.JailAttempts >= 3)
                {
                    ReleaseFromJail(p);
                    Log($"{p.Name} failed a third time and must pay the ${BoardLayout.JailFine} fine.", true);
                    Charge(p, BoardLayout.JailFine, null);
                }
                else
                {
                    Log($"{p.Name} stays in jail (attempt {p.JailAttempts} of 3).");
                    FinishResolution();
                    return;
                }
                MoveForward(p, DiceTotal);
            }
            else
            {
                if (doubles && ++doublesCount == 3)
                {
                    Log($"{p.Name} rolled doubles three times in a row. Go to jail!", true);
                    SendToJail(p);
                    FinishResolution();
                    return;
                }
                extraRollPending = doubles;
                MoveForward(p, DiceTotal);
            }
            FinishResolution();
        }

        public void PayJailFine()
        {
            Require(CanPayJailFine, "You can't pay the fine right now.");
            var p = CurrentPlayer;
            ChangeMoney(p, -BoardLayout.JailFine);
            ReleaseFromJail(p);
            Log($"{p.Name} paid ${BoardLayout.JailFine} to leave jail.", true);
        }

        public void UseJailCard()
        {
            Require(CanUseJailCard, "You don't have a Get Out of Jail Free card.");
            var p = CurrentPlayer;
            var deck = p.JailCards[0];
            p.JailCards.RemoveAt(0);
            ReturnJailCard(deck);
            ReleaseFromJail(p);
            Log($"{p.Name} used a Get Out of Jail Free card.", true);
        }

        public void BuyPendingProperty()
        {
            Require(CanBuyPending, "You can't afford that.");
            var p = CurrentPlayer;
            var def = BoardLayout.Spaces[PendingPurchase];
            ChangeMoney(p, -def.Price);
            properties[def.Index].Owner = p.Id;
            PendingPurchase = -1;
            Emit(new PropertyBoughtEvent(p.Id, def.Index));
            Log($"{p.Name} bought {def.Name} for ${def.Price}.", true);
            FinishResolution();
        }

        public void DeclinePendingProperty()
        {
            Require(Phase == TurnPhase.AwaitingBuyDecision, "Nothing to decline.");
            Log($"{CurrentPlayer.Name} decided not to buy {BoardLayout.Spaces[PendingPurchase].Name}.");
            PendingPurchase = -1;
            FinishResolution();
        }

        /// <summary>Pays as many outstanding debts as the player's cash allows.</summary>
        public void PayDebts()
        {
            Require(CanPayDebts, "Not enough cash to pay yet.");
            var p = CurrentPlayer;
            while (debts.Count > 0 && p.Money >= debts[0].Amount)
            {
                var debt = debts[0];
                debts.RemoveAt(0);
                Transfer(p, debt.Amount, debt.Creditor >= 0 ? players[debt.Creditor] : null);
            }
            FinishResolution();
        }

        public void DeclareBankruptcy()
        {
            Require(Phase == TurnPhase.AwaitingDebtPayment, "You can only go bankrupt when you owe money.");
            int creditor = debts[0].Creditor;
            debts.Clear();
            GoBankrupt(CurrentPlayer, creditor >= 0 ? players[creditor] : null);
            if (!CheckForWinner()) AdvanceTurn();
        }

        public void EndTurn()
        {
            Require(CanEndTurn, "You can't end your turn yet.");
            AdvanceTurn();
        }

        // ---------------------------------------------------------------- resolution

        private void RollDice(PlayerState p)
        {
            Die1 = rollDie();
            Die2 = rollDie();
            Emit(new DiceRolledEvent(p.Id, Die1, Die2));
            Log($"{p.Name} rolled {Die1} + {Die2} = {DiceTotal}{(Die1 == Die2 ? " (doubles!)" : "")}.");
        }

        private enum LandingRule { Normal, DoubleStationRent, TenTimesUtility }

        private void MoveForward(PlayerState p, int steps, LandingRule rule = LandingRule.Normal)
        {
            int from = p.Position;
            int to = (from + steps) % BoardLayout.SpaceCount;
            Emit(new TokenMovedEvent(p.Id, from, to, MoveKind.Forward));
            p.Position = to;
            if (from + steps >= BoardLayout.SpaceCount)
            {
                Emit(new PassedGoEvent(p.Id));
                ChangeMoney(p, BoardLayout.GoSalary);
                Log($"{p.Name} passed GO and collected ${BoardLayout.GoSalary}.");
            }
            Land(p, rule);
        }

        private void AdvanceTo(PlayerState p, int target, LandingRule rule = LandingRule.Normal)
        {
            int steps = (target - p.Position + BoardLayout.SpaceCount) % BoardLayout.SpaceCount;
            MoveForward(p, steps == 0 ? BoardLayout.SpaceCount : steps, rule);
        }

        private void Land(PlayerState p, LandingRule rule)
        {
            var def = BoardLayout.Spaces[p.Position];
            switch (def.Type)
            {
                case SpaceType.Property:
                case SpaceType.Station:
                case SpaceType.Utility:
                    LandOnOwnable(p, def, rule);
                    break;
                case SpaceType.Tax:
                    Log($"{p.Name} pays ${def.TaxAmount} {def.Name}.", true);
                    Charge(p, def.TaxAmount, null);
                    break;
                case SpaceType.Chance:
                    DrawCard(p, CardDeck.Chance);
                    break;
                case SpaceType.CommunityChest:
                    DrawCard(p, CardDeck.CommunityChest);
                    break;
                case SpaceType.GoToJail:
                    Log($"{p.Name} goes to jail!", true);
                    SendToJail(p);
                    break;
                case SpaceType.Jail:
                    if (!p.InJail) Log($"{p.Name} is just visiting the jail.");
                    break;
                case SpaceType.FreeParking:
                    Log($"{p.Name} relaxes on Free Parking.");
                    break;
            }
        }

        private void LandOnOwnable(PlayerState p, SpaceDef def, LandingRule rule)
        {
            var st = properties[def.Index];
            if (!st.IsOwned)
            {
                PendingPurchase = def.Index;
                Log($"{def.Name} is for sale for ${def.Price}.");
                return;
            }
            if (st.Owner == p.Id)
            {
                Log($"{p.Name} landed on their own property, {def.Name}.");
                return;
            }
            var owner = players[st.Owner];
            if (st.Mortgaged)
            {
                Log($"{def.Name} is mortgaged, so no rent is due.");
                return;
            }

            int rent;
            if (rule == LandingRule.TenTimesUtility)
            {
                RollDice(p);
                rent = DiceTotal * 10;
            }
            else
            {
                rent = CalculateRent(def.Index, DiceTotal);
                if (rule == LandingRule.DoubleStationRent) rent *= 2;
            }
            Log($"{p.Name} owes {owner.Name} ${rent} rent for {def.Name}.", true);
            Charge(p, rent, owner);
        }

        private void DrawCard(PlayerState p, CardDeck deck)
        {
            var pile = decks[deck];
            var card = pile[0];
            pile.RemoveAt(0);
            if (card.Action != CardAction.GetOutOfJailFree) pile.Add(card);

            Emit(new CardDrawnEvent(p.Id, card));
            Log($"{p.Name} drew {(deck == CardDeck.Chance ? "Chance" : "Community Chest")}: {card.Text}");

            switch (card.Action)
            {
                case CardAction.AdvanceTo:
                    AdvanceTo(p, card.Value);
                    break;
                case CardAction.AdvanceToNearestStation:
                    AdvanceTo(p, NextOf(BoardLayout.StationIndices, p.Position), LandingRule.DoubleStationRent);
                    break;
                case CardAction.AdvanceToNearestUtility:
                    AdvanceTo(p, NextOf(BoardLayout.UtilityIndices, p.Position), LandingRule.TenTimesUtility);
                    break;
                case CardAction.MoveBack:
                    int from = p.Position;
                    p.Position = (from - card.Value + BoardLayout.SpaceCount) % BoardLayout.SpaceCount;
                    Emit(new TokenMovedEvent(p.Id, from, p.Position, MoveKind.Backward));
                    Land(p, LandingRule.Normal);
                    break;
                case CardAction.GoToJail:
                    SendToJail(p);
                    break;
                case CardAction.Collect:
                    ChangeMoney(p, card.Value);
                    break;
                case CardAction.Pay:
                    Charge(p, card.Value, null);
                    break;
                case CardAction.CollectFromEachPlayer:
                    foreach (var other in ActiveOpponents(p).ToList())
                        ChargeOutOfTurn(other, card.Value, p);
                    break;
                case CardAction.PayEachPlayer:
                    foreach (var other in ActiveOpponents(p).ToList())
                        Charge(p, card.Value, other);
                    break;
                case CardAction.Repairs:
                    int houses = 0, hotels = 0;
                    foreach (int i in OwnedSpaces(p.Id))
                    {
                        if (properties[i].HasHotel) hotels++;
                        else houses += properties[i].Houses;
                    }
                    int cost = houses * card.Value + hotels * card.Value2;
                    if (cost > 0) Charge(p, cost, null);
                    else Log($"{p.Name} has no buildings to repair.");
                    break;
                case CardAction.GetOutOfJailFree:
                    p.JailCards.Add(deck);
                    break;
            }
        }

        private static int NextOf(int[] candidates, int position)
        {
            foreach (int c in candidates)
                if (c > position) return c;
            return candidates[0];
        }

        private void SendToJail(PlayerState p)
        {
            int from = p.Position;
            p.Position = BoardLayout.JailIndex;
            p.InJail = true;
            p.JailAttempts = 0;
            extraRollPending = false;
            Emit(new TokenMovedEvent(p.Id, from, BoardLayout.JailIndex, MoveKind.Direct));
        }

        private static void ReleaseFromJail(PlayerState p)
        {
            p.InJail = false;
            p.JailAttempts = 0;
        }

        private void ReturnJailCard(CardDeck deck)
            => decks[deck].Add(CardLibrary.Create(deck).First(c => c.Action == CardAction.GetOutOfJailFree));

        /// <summary>Decides what the current player must do next after a move has been fully resolved.</summary>
        private void FinishResolution()
        {
            if (Phase == TurnPhase.GameOver) return;
            if (PendingPurchase >= 0) Phase = TurnPhase.AwaitingBuyDecision;
            else if (debts.Count > 0) Phase = TurnPhase.AwaitingDebtPayment;
            else if (extraRollPending && !CurrentPlayer.InJail)
            {
                extraRollPending = false;
                Phase = TurnPhase.AwaitingRoll;
                Log($"{CurrentPlayer.Name} rolled doubles and goes again.");
            }
            else Phase = TurnPhase.AwaitingEndTurn;

            if (Phase == TurnPhase.AwaitingDebtPayment)
                Log($"{CurrentPlayer.Name} owes ${DebtTotal} and must raise funds or declare bankruptcy.", true);
        }

        private void AdvanceTurn()
        {
            doublesCount = 0;
            extraRollPending = false;
            do CurrentPlayerIndex = (CurrentPlayerIndex + 1) % players.Count;
            while (CurrentPlayer.IsBankrupt);
            Phase = TurnPhase.AwaitingRoll;
            Emit(new TurnStartedEvent(CurrentPlayer.Id));
            Log($"It's {CurrentPlayer.Name}'s turn.");
        }

        // ---------------------------------------------------------------- money

        /// <summary>Charge the current player. If they can't pay right now the amount is recorded as a debt.</summary>
        private void Charge(PlayerState payer, int amount, PlayerState creditor)
        {
            if (amount <= 0) return;
            if (debts.Count == 0 && payer.Money >= amount) Transfer(payer, amount, creditor);
            else debts.Add(new Debt(creditor?.Id ?? -1, amount));
        }

        /// <summary>A non-current player owes money: raise it automatically, or go bankrupt to the creditor.</summary>
        private void ChargeOutOfTurn(PlayerState payer, int amount, PlayerState creditor)
        {
            AutoRaiseFunds(payer, amount);
            if (payer.Money >= amount)
            {
                Transfer(payer, amount, creditor);
                return;
            }
            Log($"{payer.Name} can't pay ${amount}.", true);
            GoBankrupt(payer, creditor);
            CheckForWinner();
        }

        private void Transfer(PlayerState payer, int amount, PlayerState creditor)
        {
            if (creditor != null) Emit(new PaymentEvent(payer.Id, creditor.Id, amount));
            ChangeMoney(payer, -amount);
            if (creditor != null) ChangeMoney(creditor, amount);
        }

        private void ChangeMoney(PlayerState p, int delta)
        {
            if (delta == 0) return;
            p.Money += delta;
            Emit(new MoneyChangedEvent(p.Id, delta, p.Money));
        }

        private void GoBankrupt(PlayerState p, PlayerState creditor)
        {
            foreach (int i in OwnedSpaces(p.Id).ToList())
            {
                var st = properties[i];
                ChangeMoney(p, st.Houses * BoardLayout.Spaces[i].HouseCost / 2);
                st.Houses = 0;
                if (creditor != null)
                {
                    st.Owner = creditor.Id;
                }
                else
                {
                    st.Owner = PropertyState.Unowned;
                    st.Mortgaged = false;
                }
            }

            if (creditor != null)
            {
                if (p.Money > 0) Transfer(p, p.Money, creditor);
                creditor.JailCards.AddRange(p.JailCards);
            }
            else
            {
                foreach (var deck in p.JailCards) ReturnJailCard(deck);
            }

            p.JailCards.Clear();
            ChangeMoney(p, -p.Money);
            p.IsBankrupt = true;
            p.InJail = false;
            Emit(new PlayerBankruptEvent(p.Id));
            Log(creditor != null
                ? $"{p.Name} is bankrupt! Everything they own goes to {creditor.Name}."
                : $"{p.Name} is bankrupt! Their properties return to the bank.", true);
        }

        private bool CheckForWinner()
        {
            var alive = players.Where(pl => !pl.IsBankrupt).ToList();
            if (alive.Count > 1) return false;
            Winner = alive[0];
            CurrentPlayerIndex = Winner.Id;
            Phase = TurnPhase.GameOver;
            Emit(new GameWonEvent(Winner.Id));
            PendingPurchase = -1;
            debts.Clear();
            Log($"{Winner.Name} wins the game!", true);
            return true;
        }

        // ---------------------------------------------------------------- helpers

        private IEnumerable<PlayerState> ActiveOpponents(PlayerState p) => players.Where(o => o != p && !o.IsBankrupt);

        private bool OwnedBy(PlayerState p, int space) => properties[space] != null && properties[space].Owner == p.Id;

        private void Emit(GameEvent e) => events.Enqueue(e);

        private void Log(string message, bool highlight = false) => Emit(new LogEvent(message, highlight));

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Cheap fingerprint of the full game state. Networked peers compare it after every command to
        /// detect desyncs.
        /// </summary>
        public int StateHash()
        {
            unchecked
            {
                int h = 17;
                void Mix(int v) => h = h * 31 + v;

                Mix((int)Phase); Mix(CurrentPlayerIndex); Mix(Die1); Mix(Die2); Mix(PendingPurchase); Mix(DebtTotal);
                foreach (var p in players)
                {
                    Mix(p.Money); Mix(p.Position); Mix(p.InJail ? 1 : 0); Mix(p.JailAttempts);
                    Mix(p.IsBankrupt ? 1 : 0); Mix(p.JailCards.Count);
                }
                foreach (var st in properties)
                {
                    if (st == null) continue;
                    Mix(st.Owner); Mix(st.Houses); Mix(st.Mortgaged ? 1 : 0);
                }
                return h;
            }
        }

        // Test hooks
        internal List<Card> DeckFor(CardDeck deck) => decks[deck];
        internal void SetPosition(int playerId, int space) => players[playerId].Position = space;
        internal void SetMoney(int playerId, int money) => players[playerId].Money = money;
        internal void SetOwner(int space, int playerId, int houses = 0)
        {
            properties[space].Owner = playerId;
            properties[space].Houses = houses;
        }
    }
}
