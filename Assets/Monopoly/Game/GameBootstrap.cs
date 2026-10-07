using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Monopoly.Game
{
    /// <summary>
    /// Entry point. Builds the world, board, camera director and UI, and moves between the main menu,
    /// lobby and game.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private BoardView board;
        private HudController hud;
        private GameFlow flow;
        private BoardCamera boardCamera;
        private DiceView dice;
        private Effects fx;
        private LobbyShowcase showcase;
        private NetSession net;
        private ILobby lobby;
        private bool busy;

        internal ILobby CurrentLobby => lobby;

        private void Awake()
        {
            Time.timeScale = 1f;
            // Mobile defaults to 30 fps; the animations are made for 60.
            Application.targetFrameRate = 60;
            boardCamera = EnsureSceneBasics();
            board = BoardView.Create(transform);
            Scenery.Build(transform);
            dice = DiceView.Create(board.transform);
            fx = Effects.Create(board.transform);
            hud = HudController.Create(transform, this);
            flow = gameObject.AddComponent<GameFlow>();
            hud.ShowMainMenu();
            boardCamera.Orbit(snap: true);
        }

        private static BoardCamera EnsureSceneBasics()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.gameObject.AddComponent<AudioListener>();
            }
            var director = cam.GetComponent<BoardCamera>();
            if (director == null) director = cam.gameObject.AddComponent<BoardCamera>();

            // UI Toolkit routes Input System events through the EventSystem.
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            return director;
        }

        private void ShowLobby(ILobby newLobby, bool online, string code, bool lan)
        {
            lobby = newLobby;
            hud.ShowLobby(newLobby, online, code, lan);
            if (showcase != null) Destroy(showcase.gameObject);
            showcase = LobbyShowcase.Create(board.transform, newLobby, boardCamera);
        }

        // ---------------------------------------------------------------- menu actions

        public void PlayLocal(string playerName)
        {
            var local = new LocalLobby(playerName);
            local.GameStarted += StartGame;
            ShowLobby(local, online: false, code: null, lan: false);
        }

        public async void Connect(bool host, bool lan, string playerName, string address)
        {
            if (busy) return;
            busy = true;
            hud.ShowBusy(host ? "Creating game..." : "Joining game...");

            net = new GameObject("Network Session").AddComponent<NetSession>();
            net.GameStarted += StartGame;
            net.Disconnected += OnDisconnected;
            try
            {
                if (lan && host) net.HostLan(playerName);
                else if (lan) net.JoinLan(playerName, address);
                else if (host) await net.HostOnlineAsync(playerName);
                else
                {
                    if (string.IsNullOrWhiteSpace(address)) throw new Exception("Enter the join code from the host.");
                    await net.JoinOnlineAsync(playerName, address);
                }

                ShowLobby(net, online: true, code: net.JoinCode, lan: lan);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                await DisposeNetwork();
                hud.ShowConnectError(FriendlyError(e, lan));
            }
            finally
            {
                hud.HideBusy();
                busy = false;
            }
        }

        private static string FriendlyError(Exception e, bool lan)
        {
            string message = e.Message;
            if (!lan && (message.Contains("project") || message.Contains("Project") || message.Contains("environment") || message.Contains("cloud")))
                return "Online play needs this Unity project linked to Unity Cloud (Edit > Project Settings > Services). " +
                       "You can use LAN mode meanwhile.\n\n" + message;
            return message;
        }

        private void StartGame(int seed, List<Seat> seats)
        {
            if (showcase != null) Destroy(showcase.gameObject);
            showcase = null;
            flow.Begin(seed, seats, net, board, hud, boardCamera, dice, fx);
        }

        private void OnDisconnected(string reason)
        {
            hud.HideBusy();
            hud.ShowMessage("Disconnected", reason, LeaveToMenu, "Back to menu");
        }

        public async void LeaveToMenu()
        {
            await DisposeNetwork();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private async Task DisposeNetwork()
        {
            if (net == null) return;
            var session = net;
            net = null;
            session.Disconnected -= OnDisconnected;
            await session.LeaveAsync();
            if (session != null) Destroy(session.gameObject);
        }
    }
}
