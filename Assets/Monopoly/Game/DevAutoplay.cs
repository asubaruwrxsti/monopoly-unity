using System;
using System.Collections;
using System.IO;
using System.Linq;
using Monopoly.Core;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>
    /// Development aid, inactive unless the player is launched with command-line arguments:
    ///   -monopolyAutoplay local &lt;screenshotDir&gt;   all-CPU local game, captures screenshots, then quits
    ///   -monopolyAutoplay host  &lt;screenshotDir&gt;   hosts a LAN game, starts when a client joins, bot-plays
    ///   -monopolyAutoplay join  &lt;screenshotDir&gt;   joins 127.0.0.1 and bot-plays
    /// Used to smoke-test builds, including the full network path between two instances.
    /// </summary>
    internal sealed class DevAutoplay : MonoBehaviour
    {
        private string mode;
        private string outDir;
        private GameBootstrap bootstrap;
        private HudController hud;
        private GameFlow flow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-monopolyAutoplay");
            if (i < 0 || i + 2 >= args.Length) return;
            var dev = new GameObject("DevAutoplay").AddComponent<DevAutoplay>();
            dev.mode = args[i + 1];
            dev.outDir = args[i + 2];
            // On devices, relative paths go to the app's own storage.
            if (!Path.IsPathRooted(dev.outDir)) dev.outDir = Path.Combine(Application.persistentDataPath, dev.outDir);
            Directory.CreateDirectory(dev.outDir);
            DontDestroyOnLoad(dev.gameObject);
        }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(1.5f);
            bootstrap = FindFirstObjectByType<GameBootstrap>();
            hud = FindFirstObjectByType<HudController>();
            flow = FindFirstObjectByType<GameFlow>();
            Shot("01-menu");
            yield return null;

            switch (mode)
            {
                case "local": yield return Local(); break;
                case "tokens": yield return Tokens(); break;
                case "corners": yield return Corners(); break;
                case "host": yield return Host(); break;
                case "join": yield return Join(); break;
            }
            Debug.Log("[DevAutoplay] done");
            Application.Quit();
        }

        private IEnumerator Local()
        {
            bootstrap.PlayLocal("Ada");
            yield return new WaitForSeconds(0.5f);
            var lobby = (LocalLobby)bootstrap.CurrentLobby;
            lobby.AddCpuSeat();
            lobby.AddLocalSeat();
            lobby.SetToken(0, 8); // Knight
            yield return new WaitForSeconds(1.6f);
            Shot("02-lobby");
            yield return null;
            lobby.StartGame();

            yield return new WaitForSeconds(1.2f);
            Shot("03-intro");
            yield return null;
            while (!flow.CanLocalAct) yield return null;
            yield return new WaitForSeconds(0.6f);
            Shot("04-your-turn");
            yield return null;

            flow.Request(CpuPlayer.ChooseCommand(flow.Game));
            yield return new WaitForSeconds(0.75f);
            Shot("05-dice");
            yield return null;
            yield return new WaitForSeconds(1.4f);
            Shot("06-moving");
            yield return null;

            // Play the human seats through the normal UI request path, capturing key dialogs once.
            bool deedShot = false, cardShot = false, manageShot = false, soldShot = false;
            float end = Time.realtimeSinceStartup + 150f;
            Time.timeScale = 2f;
            while (Time.realtimeSinceStartup < end && !flow.Game.IsOver && !(deedShot && cardShot && manageShot))
            {
                if (!deedShot && hud.IsDeedVisible) { deedShot = true; Time.timeScale = 1f; yield return new WaitForSeconds(0.5f); Shot("07-buy-prompt"); yield return null; Time.timeScale = 2f; }
                if (!cardShot && hud.IsCardVisible) { cardShot = true; yield return new WaitForSeconds(0.4f); Shot("08-card"); yield return null; }
                hud.DismissCard();
                if (flow.CanLocalAct)
                {
                    if (!manageShot && flow.Game.Phase == TurnPhase.AwaitingEndTurn && flow.Game.OwnedSpaces(flow.Game.CurrentPlayerIndex).Count() >= 3)
                    {
                        manageShot = true;
                        hud.ShowManage();
                        yield return new WaitForSeconds(0.5f);
                        Shot("09-manage");
                        yield return null;
                        hud.CloseDialogs();

                        // Another player's properties, then a trade offer to them.
                        int me = flow.Game.CurrentPlayerIndex;
                        int other = Enumerable.Range(0, flow.Game.Players.Count).Where(i => i != me)
                                              .OrderByDescending(i => flow.Game.OwnedSpaces(i).Count()).First();
                        yield return new WaitForSeconds(0.3f);
                        hud.ShowPlayerSheet(other);
                        yield return new WaitForSeconds(0.6f);
                        Shot("09b-player-sheet");
                        yield return null;
                        hud.CloseDialogs();
                        yield return new WaitForSeconds(0.3f);
                        hud.OpenTradeBuilder(other);
                        hud.SelectForTrade(flow.Game.OwnedSpaces(me).First(), flow.Game.OwnedSpaces(other).DefaultIfEmpty(-1).First(), 100);
                        yield return new WaitForSeconds(0.6f);
                        Shot("09c-trade");
                        yield return null;
                        hud.CloseDialogs();
                        yield return new WaitForSeconds(0.3f);
                        hud.ToggleLog();
                        yield return new WaitForSeconds(0.6f);
                        Shot("09d-log");
                        yield return null;
                        hud.CloseDialogs();
                        yield return new WaitForSeconds(0.4f);
                    }
                    var cmd = CpuPlayer.ChooseCommand(flow.Game);
                    flow.Request(cmd);
                    if (!soldShot && cmd.Type == CommandType.Buy)
                    {
                        soldShot = true;
                        Time.timeScale = 1f;
                        yield return new WaitForSeconds(0.55f);
                        Shot("10-sold");
                        yield return null;
                        Time.timeScale = 2f;
                    }
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Time.timeScale = 1f;
            yield return new WaitForSeconds(1f);
            Shot("11-midgame");
            LogState();
        }

        /// <summary>Close-ups of each board corner, to check the tiles meet cleanly.</summary>
        private IEnumerator Corners()
        {
            var cam = FindFirstObjectByType<BoardCamera>();
            foreach (int corner in new[] { 0, 10, 20, 30 })
            {
                cam.Focus(BoardView.TileCenter(corner), 0f, 4.2f, 62f);
                yield return new WaitForSeconds(2.5f);
                Shot($"02-corner-{corner}");
                yield return null;
            }
        }

        /// <summary>Lines up the tokens not shown in the local run, and plays each one's celebration.</summary>
        private IEnumerator Tokens()
        {
            bootstrap.PlayLocal("Gallery");
            yield return new WaitForSeconds(0.3f);
            var lobby = (LocalLobby)bootstrap.CurrentLobby;
            while (lobby.SeatList.Count < 6) lobby.AddCpuSeat();
            // Seats start on tokens 0-5; swap three of them to the remaining pieces.
            lobby.SetToken(1, 6);
            lobby.SetToken(2, 7);
            lobby.SetToken(3, 9);
            yield return new WaitForSeconds(0.35f);
            Shot("02-gallery-action");
            yield return null;
            yield return new WaitForSeconds(2.2f);
            Shot("03-gallery-idle");
            yield return null;
        }

        private IEnumerator Host()
        {
            bootstrap.Connect(host: true, lan: true, "Host", "");
            yield return new WaitForSeconds(1f);
            var net = FindFirstObjectByType<NetSession>();
            for (float t = 0; t < 60 && net.Seats.Count < 2; t += 0.5f) yield return new WaitForSeconds(0.5f);
            Shot("02-host-lobby");
            net.AddCpuSeat();
            yield return new WaitForSeconds(0.5f);
            net.StartGame();
            yield return Bot(100f);
            Shot("03-host-game");
            LogState();
        }

        private IEnumerator Join()
        {
            bootstrap.Connect(host: false, lan: true, "Guest", "127.0.0.1");
            yield return new WaitForSeconds(3f);
            Shot("02-join-lobby");
            for (float t = 0; t < 60 && flow.Game == null; t += 0.5f) yield return new WaitForSeconds(0.5f);
            yield return Bot(90f);
            Shot("03-join-game");
            LogState();
        }

        /// <summary>Plays the local seat with the CPU strategy, through the normal UI request path.</summary>
        private IEnumerator Bot(float seconds)
        {
            Time.timeScale = 3f;
            float end = Time.realtimeSinceStartup + seconds;
            bool traded = false;
            while (Time.realtimeSinceStartup < end && (flow.Game == null || !flow.Game.IsOver))
            {
                hud.DismissCard();
                var g = flow.Game;
                // The host offers the online guest cash for one of their properties, once, to test trades over the network.
                if (!traded && mode == "host" && flow.CanLocalAct && g.Phase == TurnPhase.AwaitingEndTurn)
                {
                    int guest = 1;
                    int want = g.OwnedSpaces(guest).Where(i => g.IsTradable(guest, i)).DefaultIfEmpty(-1).First();
                    var offer = new TradeOffer(g.CurrentPlayerIndex, guest, new int[0], want >= 0 ? new[] { want } : new int[0], Mathf.Min(400, g.CurrentPlayer.Money), 0);
                    if (want >= 0 && g.CanProposeTrade(offer))
                    {
                        traded = true;
                        Debug.Log($"[DevAutoplay] proposing trade for space {want}");
                        flow.Request(GameCommand.Trade(offer));
                        yield return new WaitForSecondsRealtime(0.3f);
                        continue;
                    }
                }
                if (flow.CanLocalAct) flow.Request(CpuPlayer.ChooseCommand(g));
                yield return new WaitForSecondsRealtime(0.2f);
            }
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.5f);
        }

        private IEnumerator WaitForCard(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end && !hud.IsCardVisible) yield return null;
        }

        private void LogState()
        {
            var g = flow.Game;
            if (g == null) return;
            Debug.Log($"[DevAutoplay] hash={g.StateHash()} phase={g.Phase} current={g.CurrentPlayerIndex}");
            foreach (var p in g.Players) Debug.Log($"[DevAutoplay] {p.Name} ${p.Money} pos={p.Position} bankrupt={p.IsBankrupt}");
        }

        private void Shot(string name)
        {
            string path = Path.Combine(outDir, $"{mode}-{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[DevAutoplay] screenshot {path}");
        }
    }
}
