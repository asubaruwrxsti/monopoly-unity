using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Monopoly.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monopoly.Game
{
    /// <summary>
    /// Runs a game: routes player input to the rules engine (directly, or through the host when online),
    /// stages the engine's events as cinematic beats (camera moves, dice throws, token reactions, effects),
    /// and drives CPU players on the authority.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        [SerializeField] private float cpuDelay = 0.6f;
        [SerializeField] private float secondsPerSpace = 0.28f;

        /// <summary>Tokens are shrunk on the board so up to six fit on one space.</summary>
        private const float TokenScale = 0.8f;

        private List<Seat> seats;
        private NetSession net;
        private BoardView board;
        private HudController hud;
        private BoardCamera boardCamera;
        private DiceView dice;
        private Effects fx;
        private TokenView[] tokens;
        private bool playing;
        private bool dirty;
        private bool gameOverShown;
        private bool desyncReported;
        private bool introDone;
        private float cpuTimer;
        private Camera cam;

        public MonopolyGame Game { get; private set; }
        public IReadOnlyList<Seat> SeatList => seats;
        public bool IsOnline => net != null;
        public bool IsAuthority => net == null || net.IsHost;
        public bool IsWaitingForHost { get; private set; }
        private ulong LocalId => net != null ? net.LocalId : SeatRules.LocalOwner;

        public bool HasSeveralLocalHumans => seats.Count(s => s.Kind == SeatKind.Human && s.Owner == LocalId) > 1;

        public bool LocalControlsSeat(int index) => seats[index].Kind == SeatKind.Human && seats[index].Owner == LocalId;

        /// <summary>The local user may issue a command right now.</summary>
        public bool CanLocalAct => Game != null && introDone && !playing && !IsWaitingForHost && !Game.IsOver
                                   && LocalControlsSeat(Game.CurrentPlayerIndex);

        public void Begin(int seed, List<Seat> seatList, NetSession session, BoardView boardView, HudController hudController,
                          BoardCamera director, DiceView diceView, Effects effects)
        {
            seats = seatList;
            net = session;
            board = boardView;
            hud = hudController;
            boardCamera = director;
            dice = diceView;
            fx = effects;
            cam = director.GetComponent<Camera>();
            Game = new MonopolyGame(SeatRules.ToPlayerSetup(seats), seed);

            tokens = new TokenView[seats.Count];
            for (int i = 0; i < seats.Count; i++)
            {
                tokens[i] = TokenView.Create(board.transform, TokenCatalog.Get(seats[i].Token), SeatRules.PlayerColors[i]);
                tokens[i].transform.localScale = Vector3.one * TokenScale;
                tokens[i].gameObject.SetActive(false);
            }

            if (net != null)
            {
                net.RequestReceived += OnRemoteRequest;
                net.CommandReceived += OnHostCommand;
                net.RequestRejected += OnRejected;
                net.ClientLeftGame += OnClientLeft;
                net.SeatsChanged += OnSeatsChanged;
            }

            hud.BeginGame(this);
            StartCoroutine(Intro());
            StartCoroutine(Loop());
        }

        private void OnDestroy()
        {
            if (net == null) return;
            net.RequestReceived -= OnRemoteRequest;
            net.CommandReceived -= OnHostCommand;
            net.RequestRejected -= OnRejected;
            net.ClientLeftGame -= OnClientLeft;
            net.SeatsChanged -= OnSeatsChanged;
        }

        /// <summary>Tokens drop onto GO one after another.</summary>
        private IEnumerator Intro()
        {
            hud.LockActions();
            boardCamera.Focus(BoardView.TileCenter(0) + new Vector3(-0.4f, 0, 0.6f), 0f, 5.5f, 34f);
            yield return new WaitForSeconds(0.8f);
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                Vector3 slot = BoardView.TokenSlot(0, i, false);
                token.gameObject.SetActive(true);
                token.Place(slot + Vector3.up * 3f, Vector3.left);
                Sfx.Play(SfxKind.Whoosh, 0.5f);
                for (float t = 0; t < 1f; t += Time.deltaTime / 0.35f)
                {
                    token.transform.localPosition = slot + Vector3.up * (3f * (1f - t * t));
                    yield return null;
                }
                token.Place(slot, Vector3.left);
                board.BounceTile(0, 0.04f);
                Sfx.Play(SfxKind.Hop, 1f, 0.8f + i * 0.08f);
                fx.Sparkle(slot, SeatRules.PlayerColors[i], 14);
                yield return new WaitForSeconds(0.12f);
            }
            hud.Announce("LET'S PLAY!", "", 1.2f);
            Sfx.Play(SfxKind.Fanfare);
            yield return new WaitForSeconds(1.0f);
            introDone = true;
            dirty = true;
        }

        // ---------------------------------------------------------------- commands

        /// <summary>Called by the UI when the local user clicks an action.</summary>
        public void Request(GameCommand cmd)
        {
            if (!CanLocalAct) return;
            if (IsAuthority)
            {
                string error = Authorize(cmd, LocalId, fromCpu: false);
                if (error != null) hud.ShowToast(error);
            }
            else
            {
                IsWaitingForHost = true;
                net.SendRequest(cmd);
                hud.LockActions();
            }
        }

        /// <summary>Authority only: validate and apply a command, then share it with clients.</summary>
        private string Authorize(GameCommand cmd, ulong sender, bool fromCpu)
        {
            var seat = seats[Game.CurrentPlayerIndex];
            bool allowed = fromCpu ? seat.Kind == SeatKind.Cpu : seat.Kind == SeatKind.Human && seat.Owner == sender;
            if (!allowed) return "It's not your turn.";
            if (!Game.IsLegal(cmd)) return "That move isn't allowed right now.";

            Game.Execute(cmd);
            net?.BroadcastCommand(cmd, Game.StateHash());
            dirty = true;
            return null;
        }

        private void OnRemoteRequest(GameCommand cmd, ulong sender)
        {
            string error = Authorize(cmd, sender, fromCpu: false);
            if (error != null) net.Reject(sender, error);
        }

        private void OnHostCommand(GameCommand cmd, int hostHash)
        {
            IsWaitingForHost = false;
            if (!Game.IsLegal(cmd))
            {
                ReportDesync($"received illegal command {cmd}");
                return;
            }
            Game.Execute(cmd);
            if (Game.StateHash() != hostHash) ReportDesync($"state hash mismatch after {cmd}");
            dirty = true;
        }

        private void OnRejected(string reason)
        {
            IsWaitingForHost = false;
            hud.ShowToast(reason);
            hud.Refresh();
        }

        private void OnClientLeft(ulong clientId)
        {
            var names = seats.Where(s => s.Owner == clientId && s.Kind == SeatKind.Human).Select(s => s.Name).ToList();
            net.ConvertSeatsToCpu(clientId);
            foreach (var name in names) hud.AddLog($"{name} disconnected. A computer player takes over.", true);
        }

        private void OnSeatsChanged()
        {
            // Mid-game seat changes are only control hand-overs (a player dropped and a CPU took over).
            for (int i = 0; i < seats.Count && i < net.Seats.Count; i++)
            {
                seats[i].Kind = net.Seats[i].Kind;
                seats[i].Owner = net.Seats[i].Owner;
            }
            dirty = true;
        }

        private void ReportDesync(string detail)
        {
            Debug.LogError($"Monopoly desync: {detail}");
            if (desyncReported) return;
            desyncReported = true;
            hud.ShowMessage("Out of sync", "This game got out of sync with the host. Please leave and rejoin a new game.");
        }

        // ---------------------------------------------------------------- playback

        private IEnumerator Loop()
        {
            while (!introDone) yield return null;

            while (true)
            {
                if (Game.TryDequeueEvent(out var e))
                {
                    if (!playing)
                    {
                        // The engine has already resolved the move; show the result only once it's animated.
                        playing = true;
                        hud.LockActions();
                    }
                    yield return Play(e);
                    continue;
                }

                if (playing || dirty)
                {
                    playing = false;
                    dirty = false;
                    board.Sync(Game);
                    SnapTokens();
                    hud.Refresh();
                }

                if (Game.IsOver)
                {
                    if (!gameOverShown)
                    {
                        gameOverShown = true;
                        hud.ShowMessage($"{Game.Winner.Name} wins!", "Everyone else has gone bankrupt.", FindFirstObjectByType<GameBootstrap>().LeaveToMenu, "Back to menu");
                    }
                }
                else if (IsAuthority && seats[Game.CurrentPlayerIndex].Kind == SeatKind.Cpu)
                {
                    cpuTimer += Time.deltaTime;
                    if (cpuTimer >= cpuDelay)
                    {
                        cpuTimer = 0;
                        Authorize(CpuPlayer.ChooseCommand(Game), LocalId, fromCpu: true);
                    }
                }
                else
                {
                    cpuTimer = 0;
                }

                yield return null;
            }
        }

        private IEnumerator Play(GameEvent e)
        {
            switch (e)
            {
                case TurnStartedEvent turn:
                    for (int i = 0; i < tokens.Length; i++) tokens[i].SetHighlighted(i == turn.PlayerId);
                    FollowPlayer(turn.PlayerId, 1.15f);
                    if (LocalControlsSeat(turn.PlayerId))
                        hud.Announce(HasSeveralLocalHumans ? Game.Players[turn.PlayerId].Name.ToUpperInvariant() : "YOUR TURN", HasSeveralLocalHumans ? "your turn" : "", 1.1f);
                    else
                        hud.ShowToast($"{Game.Players[turn.PlayerId].Name}'s turn");
                    hud.Refresh();
                    yield return new WaitForSeconds(0.35f);
                    break;

                case DiceRolledEvent roll:
                    yield return ThrowDice(roll);
                    break;

                case TokenMovedEvent move:
                    yield return MoveToken(move);
                    break;

                case PassedGoEvent go:
                    hud.Announce("PASS GO!", $"+${BoardLayout.GoSalary}", 1.3f);
                    Sfx.Play(SfxKind.CashRegister);
                    StartCoroutine(fx.CoinRain(TokenPosition(go.PlayerId)));
                    yield return new WaitForSeconds(0.3f);
                    break;

                case MoneyChangedEvent money:
                    hud.FloatMoney(money.PlayerId, money.Delta);
                    break;

                case PaymentEvent payment:
                    tokens[payment.FromPlayer].StartCoroutine(tokens[payment.FromPlayer].React(TokenReaction.Hurt));
                    Sfx.Play(SfxKind.Sad, 0.5f);
                    yield return fx.CoinStream(TokenPosition(payment.FromPlayer), TokenPosition(payment.ToPlayer), Mathf.Clamp(payment.Amount / 25, 4, 14));
                    break;

                case PropertyBoughtEvent bought:
                    yield return Purchase(bought);
                    break;

                case BuildingChangedEvent building:
                    board.AnimateBuildings(building.Space, building.Houses);
                    if (building.Built)
                    {
                        fx.Sparkle(BoardView.TileCenter(building.Space) + Vector3.up * 0.3f, new Color(0.4f, 1f, 0.5f), 24);
                        tokens[building.PlayerId].StartCoroutine(tokens[building.PlayerId].React(TokenReaction.Build));
                    }
                    yield return new WaitForSeconds(0.15f);
                    break;

                case CardDrawnEvent card:
                    yield return hud.ShowCard(card.Card, Game.Players[card.PlayerId].Name, LocalControlsSeat(card.PlayerId));
                    break;

                case PlayerBankruptEvent bankrupt:
                    FollowPlayer(bankrupt.PlayerId, 0.9f);
                    hud.Announce("BANKRUPT!", Game.Players[bankrupt.PlayerId].Name, 1.6f);
                    Sfx.Play(SfxKind.Sad);
                    yield return tokens[bankrupt.PlayerId].React(TokenReaction.Defeated);
                    break;

                case GameWonEvent won:
                    yield return Victory(won.PlayerId);
                    break;

                case LogEvent log:
                    hud.AddLog(log.Message, log.Highlight);
                    break;
            }
        }

        private IEnumerator ThrowDice(DiceRolledEvent roll)
        {
            var token = tokens[roll.PlayerId];
            Vector3 tokenPos = token.transform.localPosition;
            Vector3 inward = -new Vector3(tokenPos.x, 0, tokenPos.z).normalized;
            Vector3 landing = new Vector3(tokenPos.x, 0.01f, tokenPos.z) + inward * 2.4f;
            Vector3 from = tokenPos + Vector3.up * 1.2f - inward * 0.6f;
            float yaw = BoardCamera.SideYaw(Game.Players[roll.PlayerId].Position);

            boardCamera.Focus(Vector3.Lerp(tokenPos, landing, 0.6f), yaw, 6.2f, 52f);
            yield return dice.Throw(roll.Die1, roll.Die2, from, landing, () => boardCamera.Shake(0.12f));
            bool doubles = roll.Die1 == roll.Die2;
            hud.Announce((roll.Die1 + roll.Die2).ToString(), doubles ? "DOUBLES!" : "", 0.9f);
            if (doubles) Sfx.Play(SfxKind.Fanfare, 0.5f);
            yield return new WaitForSeconds(0.45f);
        }

        private IEnumerator MoveToken(TokenMovedEvent move)
        {
            var token = tokens[move.PlayerId];
            var player = Game.Players[move.PlayerId];

            if (move.Kind == MoveKind.Direct)
            {
                // Only jail is reached directly.
                hud.Announce("JAIL!", "Do not pass GO", 1.3f);
                Sfx.Play(SfxKind.Whoosh);
                boardCamera.Focus(BoardView.TileCenter(BoardLayout.JailIndex), 45f, 7.5f, 50f);
                yield return token.FlyTo(BoardView.TokenSlot(move.To, move.PlayerId, inJail: true), 1.0f);
                board.BounceTile(BoardLayout.JailIndex, 0.08f);
                boardCamera.Shake(0.15f);
                Sfx.Play(SfxKind.Sad);
                yield return token.React(TokenReaction.Jailed);
                dice.HideSoon(0.5f);
                yield break;
            }

            var spaces = new List<int>();
            var path = new List<Vector3>();
            int step = move.Kind == MoveKind.Forward ? 1 : -1;
            for (int s = move.From; s != move.To;)
            {
                s = (s + step + BoardLayout.SpaceCount) % BoardLayout.SpaceCount;
                spaces.Add(s);
                path.Add(BoardView.TokenSlot(s, move.PlayerId, inJail: false));
            }

            boardCamera.Follow(token.transform, BoardCamera.SideYaw(move.From));
            // Long trips go a little quicker per space.
            float perSpace = secondsPerSpace * Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(4, 12, spaces.Count));
            yield return token.MoveAlong(path, perSpace, i =>
            {
                int space = spaces[i];
                board.BounceTile(space, i == spaces.Count - 1 ? 0.07f : 0.035f);
                Sfx.Play(SfxKind.Hop, 0.6f, 0.9f + (i % 4) * 0.06f);
                boardCamera.Follow(token.transform, BoardCamera.SideYaw(space), i == spaces.Count - 1 ? 0.9f : 1f);
            });

            int landed = move.To;
            fx.TileGlow(BoardView.TileCenter(landed), Quaternion.Euler(0, BoardView.TileYaw(landed), 0), BoardView.TileSize(landed),
                        SeatRules.PlayerColors[move.PlayerId]);
            dice.HideSoon(1.2f);
            yield return new WaitForSeconds(0.15f);
        }

        private IEnumerator Purchase(PropertyBoughtEvent bought)
        {
            var token = tokens[bought.PlayerId];
            Vector3 tile = BoardView.TileCenter(bought.Space) + Vector3.up * BoardView.TileTop;
            boardCamera.Follow(token.transform, BoardCamera.SideYaw(bought.Space), 0.8f);
            hud.Announce("SOLD!", BoardLayout.Spaces[bought.Space].Name, 1.4f);
            Sfx.Play(SfxKind.CashRegister);
            Sfx.Play(SfxKind.Fanfare, 0.7f);
            fx.Confetti(tile + Vector3.up * 0.2f);
            StartCoroutine(board.AnimatePurchase(bought.Space, bought.PlayerId));
            yield return token.React(TokenReaction.Celebrate);
        }

        private IEnumerator Victory(int playerId)
        {
            var token = tokens[playerId];
            token.SetHighlighted(true);
            boardCamera.Follow(token.transform, BoardCamera.SideYaw(Game.Players[playerId].Position), 0.7f);
            hud.Announce("WINNER!", Game.Players[playerId].Name, 3f);
            Sfx.Play(SfxKind.Fanfare);
            for (int i = 0; i < 3; i++)
            {
                fx.Confetti(token.transform.localPosition + new Vector3(Random.Range(-1f, 1f), 0.3f, Random.Range(-1f, 1f)), 120);
                yield return token.React(TokenReaction.Celebrate);
            }
            boardCamera.Orbit();
        }

        private void FollowPlayer(int playerId, float distanceScale)
        {
            var p = Game.Players[playerId];
            boardCamera.Follow(tokens[playerId].transform, BoardCamera.SideYaw(p.Position), distanceScale);
        }

        private Vector3 TokenPosition(int playerId) => tokens[playerId].transform.localPosition + Vector3.up * 0.3f;

        private void SnapTokens()
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                var p = Game.Players[i];
                if (p.IsBankrupt) continue; // the defeat animation already removed it
                var token = tokens[i];
                Vector3 target = BoardView.TokenSlot(p.Position, i, p.InJail);
                if ((token.transform.localPosition - target).sqrMagnitude > 0.0001f)
                    token.Place(target, BoardView.SideRotation(p.Position) * Vector3.left);
            }
        }

        // ---------------------------------------------------------------- board clicks

        private void Update()
        {
            if (Game == null || cam == null) return;
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame) return;
            if (hud.IsBlockingBoardClicks) return;

            Vector2 pos = pointer.position.ReadValue();
            if (hud.IsPointerOverUI(pos)) return;
            if (Physics.Raycast(cam.ScreenPointToRay(pos), out var hit, 200f) && board.TryGetTile(hit.collider, out int space))
                hud.ShowDeed(space, purchase: false);
        }
    }
}
