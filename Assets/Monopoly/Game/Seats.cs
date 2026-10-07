using System.Collections.Generic;
using Monopoly.Core;
using UnityEngine;

namespace Monopoly.Game
{
    public enum SeatKind : byte { Human, Cpu }

    /// <summary>
    /// Who sits in each player slot and which peer controls it. CPU seats are always driven by the
    /// authority (the host, or this device in a local game).
    /// </summary>
    public sealed class Seat
    {
        public string Name;
        public SeatKind Kind;
        /// <summary>Network client id of the controlling peer (<see cref="SeatRules.LocalOwner"/> offline).</summary>
        public ulong Owner;
        /// <summary>Index into <see cref="TokenCatalog.All"/>.</summary>
        public int Token;

        public Seat(string name, SeatKind kind, ulong owner, int token = 0)
        {
            Name = name;
            Kind = kind;
            Owner = owner;
            Token = token;
        }

        public Seat Clone() => new Seat(Name, Kind, Owner, Token);
    }

    public static class SeatRules
    {
        public const int MaxSeats = 6;
        public const ulong LocalOwner = 0;

        public static readonly Color[] PlayerColors =
        {
            new Color32(229, 72, 77, 255),
            new Color32(62, 123, 250, 255),
            new Color32(48, 164, 108, 255),
            new Color32(245, 165, 36, 255),
            new Color32(142, 78, 198, 255),
            new Color32(18, 165, 148, 255),
        };

        public static List<PlayerSetup> ToPlayerSetup(IReadOnlyList<Seat> seats)
        {
            var list = new List<PlayerSetup>();
            foreach (var s in seats) list.Add(new PlayerSetup(s.Name, s.Kind == SeatKind.Cpu));
            return list;
        }

        public static string NextCpuName(IReadOnlyList<Seat> seats)
        {
            for (int i = 1; ; i++)
            {
                string name = $"CPU {i}";
                bool taken = false;
                foreach (var s in seats) taken |= s.Name == name;
                if (!taken) return name;
            }
        }

        /// <summary>The first token nobody else is using.</summary>
        public static int FreeToken(IReadOnlyList<Seat> seats)
        {
            for (int t = 0; t < TokenCatalog.All.Count; t++)
                if (!IsTokenTaken(seats, t, -1)) return t;
            return 0;
        }

        public static bool IsTokenTaken(IReadOnlyList<Seat> seats, int token, int exceptSeat)
        {
            for (int i = 0; i < seats.Count; i++)
                if (i != exceptSeat && seats[i].Token == token) return true;
            return false;
        }

        /// <summary>Steps seat <paramref name="seat"/>'s token forward or back, skipping tokens other seats use.</summary>
        public static int CycleToken(IReadOnlyList<Seat> seats, int seat, int direction)
        {
            int count = TokenCatalog.All.Count;
            int token = seats[seat].Token;
            for (int i = 0; i < count; i++)
            {
                token = ((token + direction) % count + count) % count;
                if (!IsTokenTaken(seats, token, seat)) return token;
            }
            return seats[seat].Token;
        }

        public static string Sanitize(string name, string fallback)
        {
            name = (name ?? "").Trim();
            if (name.Length > 16) name = name.Substring(0, 16);
            return name.Length == 0 ? fallback : name;
        }
    }
}
