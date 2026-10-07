using System.Collections.Generic;

namespace Monopoly.Core
{
    public enum CardDeck { Chance, CommunityChest }

    public enum CardAction
    {
        AdvanceTo,
        AdvanceToNearestStation,
        AdvanceToNearestUtility,
        MoveBack,
        GoToJail,
        Collect,
        Pay,
        CollectFromEachPlayer,
        PayEachPlayer,
        Repairs,
        GetOutOfJailFree,
    }

    public sealed class Card
    {
        public CardDeck Deck { get; }
        public string Text { get; }
        public CardAction Action { get; }
        /// <summary>Target space, amount, or per-house cost depending on <see cref="Action"/>.</summary>
        public int Value { get; }
        /// <summary>Per-hotel cost for <see cref="CardAction.Repairs"/>.</summary>
        public int Value2 { get; }

        public Card(CardDeck deck, string text, CardAction action, int value = 0, int value2 = 0)
        {
            Deck = deck;
            Text = text;
            Action = action;
            Value = value;
            Value2 = value2;
        }
    }

    public static class CardLibrary
    {
        public static List<Card> Create(CardDeck deck)
            => deck == CardDeck.Chance ? Chance() : CommunityChest();

        private static List<Card> Chance()
        {
            const CardDeck d = CardDeck.Chance;
            return new List<Card>
            {
                new Card(d, "Advance to GO. Collect $200.", CardAction.AdvanceTo, 0),
                new Card(d, "Advance to Trafalgar Square. If you pass GO, collect $200.", CardAction.AdvanceTo, 24),
                new Card(d, "Advance to Mayfair.", CardAction.AdvanceTo, 39),
                new Card(d, "Advance to Pall Mall. If you pass GO, collect $200.", CardAction.AdvanceTo, 11),
                new Card(d, "Take a trip to King's Cross Station. If you pass GO, collect $200.", CardAction.AdvanceTo, 5),
                new Card(d, "Advance to the nearest Station. If owned, pay the owner twice the rent.", CardAction.AdvanceToNearestStation),
                new Card(d, "Advance to the nearest Station. If owned, pay the owner twice the rent.", CardAction.AdvanceToNearestStation),
                new Card(d, "Advance to the nearest Utility. If owned, roll the dice and pay the owner 10 times the total.", CardAction.AdvanceToNearestUtility),
                new Card(d, "Bank pays you a dividend of $50.", CardAction.Collect, 50),
                new Card(d, "Get Out of Jail Free. Keep this card until needed.", CardAction.GetOutOfJailFree),
                new Card(d, "Go back three spaces.", CardAction.MoveBack, 3),
                new Card(d, "Go to Jail. Do not pass GO, do not collect $200.", CardAction.GoToJail),
                new Card(d, "Make general repairs on all your property: $25 per house, $100 per hotel.", CardAction.Repairs, 25, 100),
                new Card(d, "Speeding fine: pay $15.", CardAction.Pay, 15),
                new Card(d, "You have been elected Chairman of the Board. Pay each player $50.", CardAction.PayEachPlayer, 50),
                new Card(d, "Your building loan matures. Collect $150.", CardAction.Collect, 150),
            };
        }

        private static List<Card> CommunityChest()
        {
            const CardDeck d = CardDeck.CommunityChest;
            return new List<Card>
            {
                new Card(d, "Advance to GO. Collect $200.", CardAction.AdvanceTo, 0),
                new Card(d, "Bank error in your favour. Collect $200.", CardAction.Collect, 200),
                new Card(d, "Doctor's fees. Pay $50.", CardAction.Pay, 50),
                new Card(d, "From sale of stock you get $50.", CardAction.Collect, 50),
                new Card(d, "Get Out of Jail Free. Keep this card until needed.", CardAction.GetOutOfJailFree),
                new Card(d, "Go to Jail. Do not pass GO, do not collect $200.", CardAction.GoToJail),
                new Card(d, "Holiday fund matures. Receive $100.", CardAction.Collect, 100),
                new Card(d, "Income tax refund. Collect $20.", CardAction.Collect, 20),
                new Card(d, "It is your birthday. Collect $10 from every player.", CardAction.CollectFromEachPlayer, 10),
                new Card(d, "Life insurance matures. Collect $100.", CardAction.Collect, 100),
                new Card(d, "Pay hospital fees of $100.", CardAction.Pay, 100),
                new Card(d, "Pay school fees of $50.", CardAction.Pay, 50),
                new Card(d, "Receive $25 consultancy fee.", CardAction.Collect, 25),
                new Card(d, "You are assessed for street repairs: $40 per house, $115 per hotel.", CardAction.Repairs, 40, 115),
                new Card(d, "You have won second prize in a beauty contest. Collect $10.", CardAction.Collect, 10),
                new Card(d, "You inherit $100.", CardAction.Collect, 100),
            };
        }
    }
}
