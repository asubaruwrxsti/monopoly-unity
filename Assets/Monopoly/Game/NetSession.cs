using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Monopoly.Core;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>
    /// Online play over Netcode for GameObjects. The game uses host-authoritative lockstep: every peer runs the
    /// same deterministic <see cref="MonopolyGame"/> from a shared seed. Clients send command requests to the
    /// host; the host validates them and broadcasts accepted commands, which everyone applies in order.
    /// Only named messages are used, so no NetworkObjects or network prefabs are needed.
    /// </summary>
    public sealed class NetSession : MonoBehaviour, ILobby
    {
        public const ushort DefaultPort = 7777;

        private const string MsgHello = "monopoly.hello";
        private const string MsgLobby = "monopoly.lobby";
        private const string MsgStart = "monopoly.start";
        private const string MsgRequest = "monopoly.request";
        private const string MsgCommand = "monopoly.command";
        private const string MsgReject = "monopoly.reject";
        private const string MsgToken = "monopoly.token";

        private NetworkManager network;
        private UnityTransport transport;
        private ISession session;
        private string localName;
        private bool gameStarted;
        private bool leaving;

        public List<Seat> Seats { get; private set; } = new List<Seat>();
        public bool IsHost => network != null && network.IsServer;
        public ulong LocalId => network != null ? network.LocalClientId : 0;
        /// <summary>Relay join code, or "ip:port" for LAN games.</summary>
        public string JoinCode { get; private set; }
        public bool IsLan { get; private set; }

        /// <summary>Seat list changed (lobby edits, or a player dropping out mid-game).</summary>
        public event Action SeatsChanged;
        public event Action<int, List<Seat>> GameStarted;
        /// <summary>Host only: a client asked to play a command.</summary>
        public event Action<GameCommand, ulong> RequestReceived;
        /// <summary>Client only: the host accepted a command (with the host's state hash afterwards).</summary>
        public event Action<GameCommand, int> CommandReceived;
        public event Action<string> RequestRejected;
        /// <summary>Host only: a client disconnected after the game started.</summary>
        public event Action<ulong> ClientLeftGame;
        /// <summary>Client only: connection to the host was lost or refused.</summary>
        public event Action<string> Disconnected;

        // ---------------------------------------------------------------- connecting

        public async Task HostOnlineAsync(string playerName)
        {
            localName = playerName;
            await SignInAsync();
            CreateNetworkManager();
            transport.UseWebSockets = true;
            var options = new SessionOptions { MaxPlayers = SeatRules.MaxSeats, IsPrivate = true }
                .WithRelayNetwork()
                .WithNetworkOptions(WebSocketRelay());
            session = await MultiplayerService.Instance.CreateSessionAsync(options);
            JoinCode = session.Code;
            BecomeHost();
        }

        public async Task JoinOnlineAsync(string playerName, string code)
        {
            localName = playerName;
            await SignInAsync();
            CreateNetworkManager();
            transport.UseWebSockets = true;
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(),
                new JoinSessionOptions().WithNetworkOptions(WebSocketRelay()));
            JoinCode = session.Code;
        }

        /// <summary>
        /// Online games always go through Relay over secure WebSockets: browsers can't use anything else, and using
        /// it everywhere lets phone, desktop and web players join the same game.
        /// </summary>
        private static NetworkOptions WebSocketRelay() => new NetworkOptions { RelayProtocol = RelayProtocol.WSS };

        /// <summary>LAN play needs raw sockets, which browsers don't have.</summary>
        public static bool SupportsLan => Application.platform != RuntimePlatform.WebGLPlayer;

        public void HostLan(string playerName, ushort port = DefaultPort)
        {
            localName = playerName;
            IsLan = true;
            CreateNetworkManager();
            transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            if (!network.StartHost()) throw new Exception($"Couldn't start a server on port {port}. Is it already in use?");
            JoinCode = $"{LocalIPv4()}:{port}";
            BecomeHost();
        }

        public void JoinLan(string playerName, string address)
        {
            localName = playerName;
            IsLan = true;
            string host = address.Trim();
            ushort port = DefaultPort;
            int colon = host.LastIndexOf(':');
            if (colon > 0)
            {
                if (!ushort.TryParse(host.Substring(colon + 1), out port)) throw new Exception("The port number is invalid.");
                host = host.Substring(0, colon);
            }
            if (host.Length == 0) throw new Exception("Enter the host's IP address.");

            CreateNetworkManager();
            transport.SetConnectionData(host, port);
            if (!network.StartClient()) throw new Exception("Couldn't start the network client.");
            JoinCode = $"{host}:{port}";
        }

        public async Task LeaveAsync()
        {
            leaving = true;
            gameStarted = false;
            try
            {
                if (session != null) await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Leaving session failed: {e.Message}");
            }
            session = null;
            DestroyNetworkManager();
        }

        private void OnDestroy() => DestroyNetworkManager();

        private static async Task SignInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                // A unique profile per launch lets several game instances on one machine join each other.
                var options = new InitializationOptions().SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 12));
                await UnityServices.InitializeAsync(options);
            }
            while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private void CreateNetworkManager()
        {
            DestroyNetworkManager();

            // Build it inactive so NetworkManager.Awake sees a complete config.
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            transport = go.AddComponent<UnityTransport>();
            network = go.AddComponent<NetworkManager>();
            network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                // No scene sync: every peer already has the game scene and builds its own board.
                EnableSceneManagement = false,
                ConnectionApproval = false,
            };
            go.SetActive(true);

            network.OnClientStarted += RegisterHandlers;
            network.OnClientConnectedCallback += OnClientConnected;
            network.OnClientDisconnectCallback += OnClientDisconnected;
            network.OnTransportFailure += OnTransportFailure;
        }

        private void DestroyNetworkManager()
        {
            if (network == null) return;
            network.OnClientStarted -= RegisterHandlers;
            network.OnClientConnectedCallback -= OnClientConnected;
            network.OnClientDisconnectCallback -= OnClientDisconnected;
            network.OnTransportFailure -= OnTransportFailure;
            if (network.IsListening) network.Shutdown();
            Destroy(network.gameObject);
            network = null;
            transport = null;
        }

        private void BecomeHost()
        {
            Seats = new List<Seat> { new Seat(localName, SeatKind.Human, network.LocalClientId, 0) };
            SeatsChanged?.Invoke();
        }

        private void RegisterHandlers()
        {
            var messaging = network.CustomMessagingManager;
            if (network.IsServer)
            {
                messaging.RegisterNamedMessageHandler(MsgHello, OnHello);
                messaging.RegisterNamedMessageHandler(MsgRequest, OnRequest);
                messaging.RegisterNamedMessageHandler(MsgToken, OnTokenRequest);
            }
            else
            {
                messaging.RegisterNamedMessageHandler(MsgLobby, OnLobby);
                messaging.RegisterNamedMessageHandler(MsgStart, OnStart);
                messaging.RegisterNamedMessageHandler(MsgCommand, OnCommand);
                messaging.RegisterNamedMessageHandler(MsgReject, OnReject);
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!network.IsServer && clientId == network.LocalClientId)
                Send(MsgHello, new[] { NetworkManager.ServerClientId }, (ref FastBufferWriter w) => w.WriteValueSafe(localName));
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (leaving || network == null) return;

            if (network.IsServer)
            {
                if (clientId == network.LocalClientId) return;
                if (gameStarted)
                {
                    ClientLeftGame?.Invoke(clientId);
                }
                else if (Seats.RemoveAll(s => s.Owner == clientId && s.Kind == SeatKind.Human) > 0)
                {
                    BroadcastSeats();
                }
            }
            else if (clientId == network.LocalClientId || clientId == NetworkManager.ServerClientId)
            {
                string reason = network.DisconnectReason;
                Disconnected?.Invoke(string.IsNullOrEmpty(reason) ? "Lost connection to the host." : reason);
            }
        }

        private void OnTransportFailure()
        {
            if (!leaving) Disconnected?.Invoke("The network connection failed.");
        }

        // ---------------------------------------------------------------- host: lobby

        IReadOnlyList<Seat> ILobby.SeatList => Seats;
        bool ILobby.CanEdit => IsHost;
        public bool IsMine(int index) => index >= 0 && index < Seats.Count && Seats[index].Owner == LocalId;

        public void AddCpuSeat()
        {
            if (!IsHost || gameStarted || Seats.Count >= SeatRules.MaxSeats) return;
            Seats.Add(new Seat(SeatRules.NextCpuName(Seats), SeatKind.Cpu, network.LocalClientId, SeatRules.FreeToken(Seats)));
            BroadcastSeats();
        }

        public void AddLocalSeat()
        {
            if (!IsHost || gameStarted || Seats.Count >= SeatRules.MaxSeats) return;
            Seats.Add(new Seat($"Player {Seats.Count + 1}", SeatKind.Human, network.LocalClientId, SeatRules.FreeToken(Seats)));
            BroadcastSeats();
        }

        public void RemoveSeat(int index)
        {
            if (!IsHost || gameStarted || index <= 0 || index >= Seats.Count) return;
            var seat = Seats[index];
            Seats.RemoveAt(index);
            if (seat.Kind == SeatKind.Human && seat.Owner != network.LocalClientId)
                network.DisconnectClient(seat.Owner, "The host removed you from the game.");
            BroadcastSeats();
        }

        public void RenameSeat(int index, string name)
        {
            if (!IsHost || gameStarted || index < 0 || index >= Seats.Count) return;
            Seats[index].Name = name;
            BroadcastSeats();
        }

        public void SetToken(int index, int token)
        {
            if (gameStarted || network == null || index < 0 || index >= Seats.Count) return;
            if (IsHost) ApplyToken(index, token, network.LocalClientId);
            else Send(MsgToken, new[] { NetworkManager.ServerClientId }, (ref FastBufferWriter w) =>
            {
                w.WriteValueSafe(index);
                w.WriteValueSafe(token);
            });
        }

        private void OnTokenRequest(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int index);
            reader.ReadValueSafe(out int token);
            if (!gameStarted && index >= 0 && index < Seats.Count) ApplyToken(index, token, sender);
        }

        private void ApplyToken(int index, int token, ulong requester)
        {
            // The host may pick for its own and CPU seats; clients only for seats they control.
            bool allowed = Seats[index].Owner == requester;
            if (!allowed || token < 0 || token >= TokenCatalog.All.Count || SeatRules.IsTokenTaken(Seats, token, index)) return;
            Seats[index].Token = token;
            BroadcastSeats();
        }

        /// <summary>Host only: a client dropped mid-game, so a CPU takes over their seats.</summary>
        public void ConvertSeatsToCpu(ulong clientId)
        {
            foreach (var seat in Seats.Where(s => s.Owner == clientId))
            {
                seat.Kind = SeatKind.Cpu;
                seat.Owner = network.LocalClientId;
            }
            BroadcastSeats();
        }

        public void StartGame()
        {
            if (!IsHost || gameStarted || Seats.Count < 2) return;
            gameStarted = true;
            int seed = new System.Random().Next();
            Send(MsgStart, RemoteClients(), (ref FastBufferWriter w) =>
            {
                w.WriteValueSafe(seed);
                WriteSeats(ref w, Seats);
            });
            GameStarted?.Invoke(seed, Seats.Select(s => s.Clone()).ToList());
        }

        private void OnHello(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string name);
            if (gameStarted)
            {
                network.DisconnectClient(sender, "That game has already started.");
                return;
            }
            if (Seats.Count >= SeatRules.MaxSeats)
            {
                network.DisconnectClient(sender, "That game is full.");
                return;
            }
            Seats.Add(new Seat(SeatRules.Sanitize(name, $"Player {Seats.Count + 1}"), SeatKind.Human, sender, SeatRules.FreeToken(Seats)));
            BroadcastSeats();
        }

        private void BroadcastSeats()
        {
            Send(MsgLobby, RemoteClients(), (ref FastBufferWriter w) => WriteSeats(ref w, Seats));
            SeatsChanged?.Invoke();
        }

        // ---------------------------------------------------------------- in-game commands

        public void SendRequest(GameCommand cmd)
        {
            Send(MsgRequest, new[] { NetworkManager.ServerClientId }, (ref FastBufferWriter w) => WriteCommand(ref w, cmd));
        }

        public void BroadcastCommand(GameCommand cmd, int stateHash)
        {
            Send(MsgCommand, RemoteClients(), (ref FastBufferWriter w) =>
            {
                WriteCommand(ref w, cmd);
                w.WriteValueSafe(stateHash);
            });
        }

        public void Reject(ulong client, string reason)
        {
            Send(MsgReject, new[] { client }, (ref FastBufferWriter w) => w.WriteValueSafe(reason));
        }

        private void OnRequest(ulong sender, FastBufferReader reader)
        {
            RequestReceived?.Invoke(ReadCommand(ref reader), sender);
        }

        private void OnLobby(ulong sender, FastBufferReader reader)
        {
            Seats = ReadSeats(ref reader);
            SeatsChanged?.Invoke();
        }

        private void OnStart(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int seed);
            Seats = ReadSeats(ref reader);
            gameStarted = true;
            GameStarted?.Invoke(seed, Seats.Select(s => s.Clone()).ToList());
        }

        private void OnCommand(ulong sender, FastBufferReader reader)
        {
            var cmd = ReadCommand(ref reader);
            reader.ReadValueSafe(out int hash);
            CommandReceived?.Invoke(cmd, hash);
        }

        private void OnReject(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string reason);
            RequestRejected?.Invoke(reason);
        }

        // ---------------------------------------------------------------- serialization

        private delegate void Writer(ref FastBufferWriter writer);

        private void Send(string message, IReadOnlyList<ulong> targets, Writer write)
        {
            if (network == null || targets.Count == 0) return;
            var writer = new FastBufferWriter(256, Allocator.Temp, 16 * 1024);
            try
            {
                write(ref writer);
                network.CustomMessagingManager.SendNamedMessage(message, targets, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        private List<ulong> RemoteClients()
            => network.ConnectedClientsIds.Where(id => id != network.LocalClientId).ToList();

        private static void WriteCommand(ref FastBufferWriter w, GameCommand cmd)
        {
            w.WriteValueSafe((byte)cmd.Type);
            w.WriteValueSafe(cmd.Space);
            w.WriteValueSafe(cmd.Amount);
            bool hasOffer = cmd.Offer != null;
            w.WriteValueSafe(hasOffer);
            if (!hasOffer) return;
            var o = cmd.Offer;
            w.WriteValueSafe(o.From);
            w.WriteValueSafe(o.To);
            w.WriteValueSafe(o.GiveCash);
            w.WriteValueSafe(o.GetCash);
            WriteSpaces(ref w, o.GiveProperties);
            WriteSpaces(ref w, o.GetProperties);
        }

        private static GameCommand ReadCommand(ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte type);
            r.ReadValueSafe(out int space);
            r.ReadValueSafe(out int amount);
            r.ReadValueSafe(out bool hasOffer);
            if (!hasOffer) return new GameCommand((CommandType)type, space, null, amount);
            r.ReadValueSafe(out int from);
            r.ReadValueSafe(out int to);
            r.ReadValueSafe(out int giveCash);
            r.ReadValueSafe(out int getCash);
            var give = ReadSpaces(ref r);
            var get = ReadSpaces(ref r);
            return new GameCommand((CommandType)type, space, new TradeOffer(from, to, give, get, giveCash, getCash), amount);
        }

        private static void WriteSpaces(ref FastBufferWriter w, IReadOnlyList<int> spaces)
        {
            w.WriteValueSafe((byte)spaces.Count);
            foreach (int s in spaces) w.WriteValueSafe((byte)s);
        }

        private static List<int> ReadSpaces(ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte count);
            var list = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out byte s);
                list.Add(s);
            }
            return list;
        }

        private static void WriteSeats(ref FastBufferWriter w, List<Seat> seats)
        {
            w.WriteValueSafe(seats.Count);
            foreach (var s in seats)
            {
                w.WriteValueSafe(s.Name);
                w.WriteValueSafe((byte)s.Kind);
                w.WriteValueSafe(s.Owner);
                w.WriteValueSafe((byte)s.Token);
            }
        }

        private static List<Seat> ReadSeats(ref FastBufferReader r)
        {
            r.ReadValueSafe(out int count);
            var seats = new List<Seat>(count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out string name);
                r.ReadValueSafe(out byte kind);
                r.ReadValueSafe(out ulong owner);
                r.ReadValueSafe(out byte token);
                seats.Add(new Seat(name, (SeatKind)kind, owner, token));
            }
            return seats;
        }

        private static string LocalIPv4()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork) return addr.Address.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Couldn't determine local IP: {e.Message}");
            }
            return "127.0.0.1";
        }
    }
}
