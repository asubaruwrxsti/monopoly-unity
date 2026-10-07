using System;
using System.Collections.Generic;
using System.Linq;

namespace Monopoly.Game
{
    /// <summary>The pre-game seat list, shared by local games and online sessions.</summary>
    public interface ILobby
    {
        IReadOnlyList<Seat> SeatList { get; }
        /// <summary>Whether this peer may add, remove and rename seats and start the game.</summary>
        bool CanEdit { get; }
        /// <summary>Whether seat <paramref name="index"/> is controlled from this device.</summary>
        bool IsMine(int index);
        void AddCpuSeat();
        void AddLocalSeat();
        void RemoveSeat(int index);
        void RenameSeat(int index, string name);
        /// <summary>Request a token for a seat; ignored if another seat has it or this peer can't edit the seat.</summary>
        void SetToken(int index, int token);
        void StartGame();
        event Action SeatsChanged;
        event Action<int, List<Seat>> GameStarted;
    }

    /// <summary>Hot-seat game on one device.</summary>
    public sealed class LocalLobby : ILobby
    {
        private readonly List<Seat> seats = new List<Seat>();

        public LocalLobby(string hostName)
        {
            seats.Add(new Seat(hostName, SeatKind.Human, SeatRules.LocalOwner, 0));
            seats.Add(new Seat(SeatRules.NextCpuName(seats), SeatKind.Cpu, SeatRules.LocalOwner, SeatRules.FreeToken(seats)));
        }

        public IReadOnlyList<Seat> SeatList => seats;
        public bool CanEdit => true;
        public bool IsMine(int index) => true;
        public event Action SeatsChanged;
        public event Action<int, List<Seat>> GameStarted;

        public void AddCpuSeat() => Add(new Seat(SeatRules.NextCpuName(seats), SeatKind.Cpu, SeatRules.LocalOwner, SeatRules.FreeToken(seats)));

        public void AddLocalSeat() => Add(new Seat($"Player {seats.Count + 1}", SeatKind.Human, SeatRules.LocalOwner, SeatRules.FreeToken(seats)));

        public void SetToken(int index, int token)
        {
            if (index < 0 || index >= seats.Count || token < 0 || token >= TokenCatalog.All.Count) return;
            if (SeatRules.IsTokenTaken(seats, token, index)) return;
            seats[index].Token = token;
            SeatsChanged?.Invoke();
        }

        private void Add(Seat seat)
        {
            if (seats.Count >= SeatRules.MaxSeats) return;
            seats.Add(seat);
            SeatsChanged?.Invoke();
        }

        public void RemoveSeat(int index)
        {
            if (index <= 0 || index >= seats.Count) return;
            seats.RemoveAt(index);
            SeatsChanged?.Invoke();
        }

        public void RenameSeat(int index, string name) => seats[index].Name = name;

        public void StartGame()
        {
            if (seats.Count < 2) return;
            GameStarted?.Invoke(new Random().Next(), seats.Select(s => s.Clone()).ToList());
        }
    }
}
