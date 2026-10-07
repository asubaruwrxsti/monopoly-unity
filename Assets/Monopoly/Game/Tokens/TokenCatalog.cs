using System.Collections.Generic;

namespace Monopoly.Game
{
    public enum TokenShape { Boot, TopHat, RaceCar, Battleship, Iron, Thimble, Wheelbarrow, Duck, Knight, Valkyrie }

    /// <summary>How a token moves between spaces.</summary>
    public enum MoveStyle
    {
        /// <summary>Hops from space to space with squash and stretch.</summary>
        Hop,
        /// <summary>Glides low over the board (cars, boats, wheelbarrows).</summary>
        Drive,
        /// <summary>An animated character that runs.</summary>
        Run,
    }

    /// <summary>A selectable player piece and its animation personality.</summary>
    public sealed class TokenDef
    {
        public int Id;
        public string Name;
        public TokenShape Shape;
        public MoveStyle Move;
        public float HopHeight = 0.3f;
        /// <summary>How much the piece flattens on landing (0-1).</summary>
        public float Squash = 0.25f;
        /// <summary>Forward tilt while airborne, in degrees (the boot leans into its stomp).</summary>
        public float AirPitch;
        /// <summary>Side-to-side rocking while moving, in degrees (the duck waddles, the ship rolls).</summary>
        public float Waddle;

        public bool IsHero => Move == MoveStyle.Run;
    }

    public static class TokenCatalog
    {
        public static readonly IReadOnlyList<TokenDef> All = new[]
        {
            new TokenDef { Id = 0, Name = "Boot", Shape = TokenShape.Boot, Move = MoveStyle.Hop, HopHeight = 0.38f, Squash = 0.35f, AirPitch = -18f },
            new TokenDef { Id = 1, Name = "Top Hat", Shape = TokenShape.TopHat, Move = MoveStyle.Hop, HopHeight = 0.32f, Squash = 0.22f, Waddle = 6f },
            new TokenDef { Id = 2, Name = "Race Car", Shape = TokenShape.RaceCar, Move = MoveStyle.Drive, HopHeight = 0.04f, Squash = 0.1f },
            new TokenDef { Id = 3, Name = "Battleship", Shape = TokenShape.Battleship, Move = MoveStyle.Drive, HopHeight = 0.05f, Squash = 0.05f, Waddle = 9f },
            new TokenDef { Id = 4, Name = "Iron", Shape = TokenShape.Iron, Move = MoveStyle.Hop, HopHeight = 0.14f, Squash = 0.3f },
            new TokenDef { Id = 5, Name = "Thimble", Shape = TokenShape.Thimble, Move = MoveStyle.Hop, HopHeight = 0.42f, Squash = 0.4f },
            new TokenDef { Id = 6, Name = "Wheelbarrow", Shape = TokenShape.Wheelbarrow, Move = MoveStyle.Drive, HopHeight = 0.05f, Squash = 0.1f, AirPitch = 8f },
            new TokenDef { Id = 7, Name = "Rubber Duck", Shape = TokenShape.Duck, Move = MoveStyle.Hop, HopHeight = 0.22f, Squash = 0.3f, Waddle = 14f },
            new TokenDef { Id = 8, Name = "Knight", Shape = TokenShape.Knight, Move = MoveStyle.Run },
            new TokenDef { Id = 9, Name = "Valkyrie", Shape = TokenShape.Valkyrie, Move = MoveStyle.Run },
        };

        public static TokenDef Get(int id) => id >= 0 && id < All.Count ? All[id] : All[0];
    }
}
