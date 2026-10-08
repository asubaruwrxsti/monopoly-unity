using System.Collections.Generic;
using System.Linq;

namespace Monopoly.Core
{
    public enum SpaceType { Go, Property, Station, Utility, Tax, Chance, CommunityChest, Jail, FreeParking, GoToJail }

    public enum ColorGroup { None, Brown, LightBlue, Pink, Orange, Red, Yellow, Green, DarkBlue, Station, Utility }

    /// <summary>Static definition of one board space. Mutable state lives in <see cref="PropertyState"/>.</summary>
    public sealed class SpaceDef
    {
        public int Index { get; }
        public string Name { get; }
        /// <summary>Texture name under Resources/Tiles.</summary>
        public string Texture { get; }
        public SpaceType Type { get; }
        public ColorGroup Group { get; }
        public int Price { get; }
        public int HouseCost { get; }
        /// <summary>For streets: rent with 0..4 houses, then hotel (6 entries).</summary>
        public IReadOnlyList<int> Rent { get; }
        public int TaxAmount { get; }

        public SpaceDef(int index, string name, SpaceType type, ColorGroup group = ColorGroup.None,
                        int price = 0, int houseCost = 0, int[] rent = null, int tax = 0, string texture = null)
        {
            Index = index;
            Name = name;
            Type = type;
            Group = group;
            Price = price;
            HouseCost = houseCost;
            Rent = rent ?? new int[0];
            TaxAmount = tax;
            Texture = texture ?? name;
        }

        public bool IsOwnable => Type == SpaceType.Property || Type == SpaceType.Station || Type == SpaceType.Utility;
        public bool IsCorner => Index % 10 == 0;
        public int MortgageValue => Price / 2;
        /// <summary>Mortgage value plus 10% interest, rounded up.</summary>
        public int UnmortgageCost => (MortgageValue * 11 + 9) / 10;
    }

    /// <summary>The classic London board, in play order starting at GO.</summary>
    public static class BoardLayout
    {
        public const int SpaceCount = 40;
        public const int JailIndex = 10;
        public const int GoToJailIndex = 30;
        public const int GoSalary = 200;
        public const int JailFine = 50;
        public const int StartingMoney = 1500;
        public const int HotelLevel = 5;
        /// <summary>Total houses the bank has to lend out, matching the physical game's 32-piece supply.</summary>
        public const int HouseSupplyLimit = 32;
        /// <summary>Total hotels the bank has to lend out, matching the physical game's 12-piece supply.</summary>
        public const int HotelSupplyLimit = 12;

        public static readonly int[] StationIndices = { 5, 15, 25, 35 };
        public static readonly int[] UtilityIndices = { 12, 28 };

        private static SpaceDef Street(int i, string name, ColorGroup g, int price, int house, params int[] rent)
            => new SpaceDef(i, name, SpaceType.Property, g, price, house, rent);

        private static SpaceDef Station(int i, string name)
            => new SpaceDef(i, name, SpaceType.Station, ColorGroup.Station, 200, rent: new[] { 25, 50, 100, 200 });

        private static SpaceDef Utility(int i, string name)
            => new SpaceDef(i, name, SpaceType.Utility, ColorGroup.Utility, 150);

        public static readonly IReadOnlyList<SpaceDef> Spaces = new[]
        {
            new SpaceDef(0, "GO", SpaceType.Go, texture: "Go"),
            Street(1, "Old Kent Road", ColorGroup.Brown, 60, 50, 2, 10, 30, 90, 160, 250),
            new SpaceDef(2, "Community Chest", SpaceType.CommunityChest),
            Street(3, "Whitechapel Road", ColorGroup.Brown, 60, 50, 4, 20, 60, 180, 320, 450),
            new SpaceDef(4, "Income Tax", SpaceType.Tax, tax: 200),
            Station(5, "King's Cross Station") .WithTexture("Kings Cross Station"),
            Street(6, "The Angel, Islington", ColorGroup.LightBlue, 100, 50, 6, 30, 90, 270, 400, 550).WithTexture("The Angel Islington"),
            new SpaceDef(7, "Chance", SpaceType.Chance),
            Street(8, "Euston Road", ColorGroup.LightBlue, 100, 50, 6, 30, 90, 270, 400, 550),
            Street(9, "Pentonville Road", ColorGroup.LightBlue, 120, 50, 8, 40, 100, 300, 450, 600),
            new SpaceDef(10, "Jail", SpaceType.Jail),
            Street(11, "Pall Mall", ColorGroup.Pink, 140, 100, 10, 50, 150, 450, 625, 750),
            Utility(12, "Electric Company"),
            Street(13, "Whitehall", ColorGroup.Pink, 140, 100, 10, 50, 150, 450, 625, 750),
            Street(14, "Northumberland Avenue", ColorGroup.Pink, 160, 100, 12, 60, 180, 500, 700, 900),
            Station(15, "Marylebone Station"),
            Street(16, "Bow Street", ColorGroup.Orange, 180, 100, 14, 70, 200, 550, 750, 950),
            new SpaceDef(17, "Community Chest", SpaceType.CommunityChest),
            Street(18, "Marlborough Street", ColorGroup.Orange, 180, 100, 14, 70, 200, 550, 750, 950),
            Street(19, "Vine Street", ColorGroup.Orange, 200, 100, 16, 80, 220, 600, 800, 1000),
            new SpaceDef(20, "Free Parking", SpaceType.FreeParking),
            Street(21, "The Strand", ColorGroup.Red, 220, 150, 18, 90, 250, 700, 875, 1050),
            new SpaceDef(22, "Chance", SpaceType.Chance),
            Street(23, "Fleet Street", ColorGroup.Red, 220, 150, 18, 90, 250, 700, 875, 1050),
            Street(24, "Trafalgar Square", ColorGroup.Red, 240, 150, 20, 100, 300, 750, 925, 1100),
            Station(25, "Fenchurch St Station"),
            Street(26, "Leicester Square", ColorGroup.Yellow, 260, 150, 22, 110, 330, 800, 975, 1150),
            Street(27, "Coventry Street", ColorGroup.Yellow, 260, 150, 22, 110, 330, 800, 975, 1150),
            Utility(28, "Water Works"),
            Street(29, "Piccadilly", ColorGroup.Yellow, 280, 150, 24, 120, 360, 850, 1025, 1200),
            new SpaceDef(30, "Go To Jail", SpaceType.GoToJail),
            Street(31, "Regent Street", ColorGroup.Green, 300, 200, 26, 130, 390, 900, 1100, 1275),
            Street(32, "Oxford Street", ColorGroup.Green, 300, 200, 26, 130, 390, 900, 1100, 1275),
            new SpaceDef(33, "Community Chest", SpaceType.CommunityChest),
            Street(34, "Bond Street", ColorGroup.Green, 320, 200, 28, 150, 450, 1000, 1200, 1400),
            Station(35, "Liverpool Street Station"),
            new SpaceDef(36, "Chance", SpaceType.Chance),
            Street(37, "Park Lane", ColorGroup.DarkBlue, 350, 200, 35, 175, 500, 1100, 1300, 1500),
            new SpaceDef(38, "Luxury Tax", SpaceType.Tax, tax: 100),
            Street(39, "Mayfair", ColorGroup.DarkBlue, 400, 200, 50, 200, 600, 1400, 1700, 2000),
        };

        private static readonly Dictionary<ColorGroup, int[]> GroupMembers = Spaces
            .Where(s => s.Group != ColorGroup.None)
            .GroupBy(s => s.Group)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Index).ToArray());

        public static IReadOnlyList<int> SpacesInGroup(ColorGroup group)
            => GroupMembers.TryGetValue(group, out var members) ? members : new int[0];

        public static string GroupName(ColorGroup group)
        {
            switch (group)
            {
                case ColorGroup.LightBlue: return "Light Blue";
                case ColorGroup.DarkBlue: return "Dark Blue";
                case ColorGroup.Station: return "Stations";
                case ColorGroup.Utility: return "Utilities";
                default: return group.ToString();
            }
        }

        private static SpaceDef WithTexture(this SpaceDef s, string texture)
            => new SpaceDef(s.Index, s.Name, s.Type, s.Group, s.Price, s.HouseCost, s.Rent.ToArray(), s.TaxAmount, texture);
    }
}
