using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Monopoly.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Monopoly.Game
{
    /// <summary>
    /// Binds Resources/UI/GameHud.uxml: main menu, connect screen, lobby, the in-game HUD and all dialogs.
    /// Layout and styling live in the UXML/USS files (editable in UI Builder); this class fills in data,
    /// forwards clicks and drives the UI animations.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        private const string NamePref = "monopoly.playerName";
        private const int MaxLogLines = 150;

        private GameBootstrap bootstrap;
        private UIDocument document;
        private VisualElement root;
        private GameFlow flow;

        // screens & modals
        private VisualElement hud, menuScreen, connectScreen, lobbyScreen;
        private VisualElement deedModal, cardModal, manageModal, messageModal, busyModal;

        // menu / connect
        private TextField menuName, connectAddress;
        private Label connectHeading, connectAddressLabel, connectHint, connectError;
        private Button modeOnlineBtn, modeLanBtn, connectGoBtn;
        private bool connectAsHost, connectLan;

        // lobby
        private ILobby lobby;
        private Label lobbyHeading, lobbyCode, lobbyCodeLabel, lobbyStatus;
        private VisualElement lobbyCodeBox, lobbyEditRow;
        private ScrollView lobbySeats;
        private Button lobbyStartBtn, lobbyAddHumanBtn, lobbyAddCpuBtn;

        // in-game
        private VisualElement playerBar, announce, logDrawer;
        private readonly List<VisualElement> chips = new List<VisualElement>();
        private float[] shownMoney = new float[0];
        private Label statusLabel, toast, announceTitle, announceSub;
        private Button rollBtn, payFineBtn, useCardBtn, buyBtn, declineBtn, payDebtBtn, bankruptBtn, endTurnBtn, manageBtn, speedBtn;
        private ScrollView log;
        private IVisualElementScheduledItem toastHide, announceHide;
        private TurnPhase lastPhase = TurnPhase.GameOver;
        private int lastPhasePlayer = -1;

        private float[] targetMoney = new float[0];

        // player sheet & trading
        private VisualElement playerModal, tradeModal, playerAvatar;
        private Label playerName, playerCash, tradeTitle, tradeGiveLabel, tradeGetLabel, tradeGiveCash, tradeGetCash, tradeNote;
        private ScrollView playerList, tradeGiveList, tradeGetList;
        private Button playerTradeBtn, tradeSendBtn;
        private VisualElement tradeProposeRow, tradeAnswerRow;
        private int sheetPlayer = -1;
        private int tradePartner = -1;
        private readonly HashSet<int> tradeGive = new HashSet<int>();
        private readonly HashSet<int> tradeGet = new HashSet<int>();
        private int tradeGiveAmount, tradeGetAmount;
        private TradeOffer answeredOffer;

        // settings, auction, camera focus
        private VisualElement settingsModal, auctionModal, auctionImage, auctionBidders, auctionActions;
        private ScrollView settingsList;
        private Label auctionProperty, auctionPrice, auctionHigh, auctionLeader, auctionTurn;
        private Button auctionPassBtn, focusBtn;
        private readonly Button[] auctionBidBtns = new Button[3];
        private readonly int[] auctionBidAmounts = new int[3];
        private BoardCamera boardCamera;

        /// <summary>Phone layout: bigger touch targets, no scrollbars. Forced on desktop with -mobileUI.</summary>
        public static bool IsMobileLayout { get; private set; }

        // dialogs
        private VisualElement deedImage, deedGroup, deedRows;
        private Label deedTitle, deedNote;
        private Button deedBuyBtn, deedDeclineBtn, deedCloseBtn;
        private int deedSpace = -1;
        private bool deedIsPurchase;
        private Label cardHeader, cardPlayer, cardText;
        private Button cardOkBtn;
        private bool cardDismissed;
        private Label manageSummary;
        private ScrollView manageList;
        private Label messageTitle, messageText, busyText;
        private Button messageOkBtn, messageCancelBtn;
        private Action messageOk;

        public static HudController Create(Transform parent, GameBootstrap bootstrap)
        {
            var go = new GameObject("UI");
            go.transform.SetParent(parent, false);
            go.SetActive(false);

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/MonopolyTheme");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            IsMobileLayout = Application.isMobilePlatform || System.Environment.GetCommandLineArgs().Contains("-mobileUI");
            if (IsMobileLayout)
            {
                // Phones are small: lay out against a shorter reference so everything is bigger and tappable.
                panel.referenceResolution = new Vector2Int(1500, 800);
                panel.match = 1f;
            }
            else
            {
                panel.referenceResolution = new Vector2Int(1920, 1080);
                panel.match = 0.5f;
            }

            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/GameHud");

            var hud = go.AddComponent<HudController>();
            hud.bootstrap = bootstrap;
            hud.document = doc;
            go.SetActive(true);
            hud.Bind();
            return hud;
        }

        private T Q<T>(string name) where T : VisualElement
        {
            var e = root.Q<T>(name);
            if (e == null) Debug.LogError($"GameHud.uxml is missing element '{name}'");
            return e;
        }

        private Button Btn(string name, Action onClick)
        {
            var b = Q<Button>(name);
            b.clicked += () =>
            {
                Sfx.Play(SfxKind.Click);
                onClick();
            };
            return b;
        }

        private void Bind()
        {
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
            root = document.rootVisualElement.Q("root");

            hud = Q<VisualElement>("hud");
            menuScreen = Q<VisualElement>("menu-screen");
            connectScreen = Q<VisualElement>("connect-screen");
            lobbyScreen = Q<VisualElement>("lobby-screen");
            deedModal = Q<VisualElement>("deed-modal");
            cardModal = Q<VisualElement>("card-modal");
            manageModal = Q<VisualElement>("manage-modal");
            messageModal = Q<VisualElement>("message-modal");
            busyModal = Q<VisualElement>("busy-modal");

            // main menu
            menuName = Q<TextField>("menu-name");
            menuName.value = PlayerPrefs.GetString(NamePref, "Player 1");
            Btn("menu-local-btn", () => bootstrap.PlayLocal(PlayerName));
            Btn("menu-host-btn", () => ShowConnect(host: true));
            Btn("menu-join-btn", () => ShowConnect(host: false));
            var quit = Btn("menu-quit-btn", Application.Quit);
            if (Application.isEditor || Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer) Hide(quit);

            // connect
            connectHeading = Q<Label>("connect-heading");
            connectAddress = Q<TextField>("connect-address");
            connectAddressLabel = Q<Label>("connect-address-label");
            connectHint = Q<Label>("connect-hint");
            connectError = Q<Label>("connect-error");
            modeOnlineBtn = Btn("mode-online-btn", () => SetConnectMode(lan: false));
            modeLanBtn = Btn("mode-lan-btn", () => SetConnectMode(lan: true));
            SetVisible(modeLanBtn.parent, NetSession.SupportsLan);
            Btn("connect-back-btn", ShowMainMenu);
            connectGoBtn = Btn("connect-go-btn", () =>
            {
                Hide(connectError);
                bootstrap.Connect(connectAsHost, connectLan, PlayerName, connectAddress.value);
            });

            // lobby
            lobbyHeading = Q<Label>("lobby-heading");
            lobbyCodeBox = Q<VisualElement>("lobby-code-box");
            lobbyCode = Q<Label>("lobby-code");
            lobbyCodeLabel = Q<Label>("lobby-code-label");
            lobbySeats = Q<ScrollView>("lobby-seats");
            lobbyEditRow = Q<VisualElement>("lobby-edit-row");
            lobbyStatus = Q<Label>("lobby-status");
            Btn("lobby-copy-btn", () =>
            {
                GUIUtility.systemCopyBuffer = lobbyCode.text;
                ShowToast("Copied to clipboard");
            });
            lobbyAddHumanBtn = Btn("lobby-add-human-btn", () => lobby?.AddLocalSeat());
            lobbyAddCpuBtn = Btn("lobby-add-cpu-btn", () => lobby?.AddCpuSeat());
            Btn("lobby-leave-btn", bootstrap.LeaveToMenu);
            lobbyStartBtn = Btn("lobby-start-btn", () =>
            {
                root.focusController?.focusedElement?.Blur(); // commit a name still being edited
                lobby?.StartGame();
            });

            // in-game
            playerBar = Q<VisualElement>("player-bar");
            statusLabel = Q<Label>("status-label");
            toast = Q<Label>("toast");
            announce = Q<VisualElement>("announce");
            announceTitle = Q<Label>("announce-title");
            announceSub = Q<Label>("announce-sub");
            logDrawer = Q<VisualElement>("log-drawer");
            log = Q<ScrollView>("log");

            rollBtn = Btn("roll-btn", () => Send(CommandType.Roll));
            payFineBtn = Btn("pay-fine-btn", () => Send(CommandType.PayJailFine));
            useCardBtn = Btn("use-card-btn", () => Send(CommandType.UseJailCard));
            buyBtn = Btn("buy-btn", () => Send(CommandType.Buy));
            declineBtn = Btn("decline-btn", () => Send(CommandType.Decline));
            payDebtBtn = Btn("pay-debt-btn", () => Send(CommandType.PayDebts));
            bankruptBtn = Btn("bankrupt-btn", () => ShowMessage("Declare bankruptcy?",
                "You'll be out of the game and your creditor takes everything you own.",
                () => Send(CommandType.DeclareBankruptcy), "Go bankrupt", showCancel: true));
            endTurnBtn = Btn("end-turn-btn", () => Send(CommandType.EndTurn));
            manageBtn = Btn("manage-btn", ShowManage);
            Btn("log-btn", ToggleLog);
            speedBtn = Btn("speed-btn", CycleSpeed);
            Btn("leave-btn", () => ShowMessage("Leave the game?",
                flow != null && flow.IsOnline ? "You'll disconnect from this online game." : "The current game will be lost.",
                bootstrap.LeaveToMenu, "Leave", showCancel: true));

            // The roll button breathes while it's waiting for you.
            rollBtn.schedule.Execute(() =>
            {
                if (rollBtn.enabledSelf && IsVisible(rollBtn)) rollBtn.ToggleInClassList("pulse");
                else rollBtn.RemoveFromClassList("pulse");
            }).Every(600);

            // dialogs
            deedImage = Q<VisualElement>("deed-image");
            deedGroup = Q<VisualElement>("deed-group");
            deedRows = Q<VisualElement>("deed-rows");
            deedTitle = Q<Label>("deed-title");
            deedNote = Q<Label>("deed-note");
            deedBuyBtn = Btn("deed-buy-btn", () => { CloseModal(deedModal); Send(CommandType.Buy); });
            deedDeclineBtn = Btn("deed-decline-btn", () => { CloseModal(deedModal); Send(CommandType.Decline); });
            deedCloseBtn = Btn("deed-close-btn", () => CloseModal(deedModal));

            cardHeader = Q<Label>("card-header");
            cardPlayer = Q<Label>("card-player");
            cardText = Q<Label>("card-text");
            cardOkBtn = Btn("card-ok-btn", () => cardDismissed = true);

            manageSummary = Q<Label>("manage-summary");
            manageList = Q<ScrollView>("manage-list");
            Btn("manage-close-btn", () => CloseModal(manageModal));

            messageTitle = Q<Label>("message-title");
            messageText = Q<Label>("message-text");
            busyText = Q<Label>("busy-text");
            messageOkBtn = Btn("message-ok-btn", () =>
            {
                CloseModal(messageModal);
                var ok = messageOk;
                messageOk = null;
                ok?.Invoke();
            });
            messageCancelBtn = Btn("message-cancel-btn", () => { messageOk = null; CloseModal(messageModal); });

            // player sheet
            playerModal = Q<VisualElement>("player-modal");
            playerAvatar = Q<VisualElement>("player-avatar");
            playerName = Q<Label>("player-name");
            playerCash = Q<Label>("player-cash");
            playerList = Q<ScrollView>("player-list");
            Btn("player-close-btn", () => CloseModal(playerModal));
            playerTradeBtn = Btn("player-trade-btn", () =>
            {
                CloseModal(playerModal);
                OpenTradeBuilder(sheetPlayer);
            });

            // trading
            tradeModal = Q<VisualElement>("trade-modal");
            tradeTitle = Q<Label>("trade-title");
            tradeGiveLabel = Q<Label>("trade-give-label");
            tradeGetLabel = Q<Label>("trade-get-label");
            tradeGiveCash = Q<Label>("trade-give-cash");
            tradeGetCash = Q<Label>("trade-get-cash");
            tradeNote = Q<Label>("trade-note");
            tradeGiveList = Q<ScrollView>("trade-give-list");
            tradeGetList = Q<ScrollView>("trade-get-list");
            tradeProposeRow = Q<VisualElement>("trade-propose-row");
            tradeAnswerRow = Q<VisualElement>("trade-answer-row");
            Btn("trade-give-minus", () => StepCash(ref tradeGiveAmount, -50, flow.Game.Players[flow.Game.CurrentPlayerIndex].Money));
            Btn("trade-give-plus", () => StepCash(ref tradeGiveAmount, 50, flow.Game.Players[flow.Game.CurrentPlayerIndex].Money));
            Btn("trade-get-minus", () => StepCash(ref tradeGetAmount, -50, flow.Game.Players[tradePartner].Money));
            Btn("trade-get-plus", () => StepCash(ref tradeGetAmount, 50, flow.Game.Players[tradePartner].Money));
            Btn("trade-cancel-btn", () => CloseModal(tradeModal));
            tradeSendBtn = Btn("trade-send-btn", () =>
            {
                CloseModal(tradeModal);
                flow?.Request(GameCommand.Trade(BuildOffer()));
            });
            Btn("trade-accept-btn", () => { CloseModal(tradeModal); Send(CommandType.AcceptTrade); });
            Btn("trade-reject-btn", () => { CloseModal(tradeModal); Send(CommandType.RejectTrade); });

            // settings
            settingsModal = Q<VisualElement>("settings-modal");
            settingsList = Q<ScrollView>("settings-list");
            Btn("menu-settings-btn", ShowSettings);
            Btn("settings-btn", ShowSettings);
            Btn("settings-done-btn", () => CloseModal(settingsModal));

            // auction
            auctionModal = Q<VisualElement>("auction-modal");
            auctionImage = Q<VisualElement>("auction-image");
            auctionBidders = Q<VisualElement>("auction-bidders");
            auctionActions = Q<VisualElement>("auction-actions");
            auctionProperty = Q<Label>("auction-property");
            auctionPrice = Q<Label>("auction-price");
            auctionHigh = Q<Label>("auction-high");
            auctionLeader = Q<Label>("auction-leader");
            auctionTurn = Q<Label>("auction-turn");
            auctionPassBtn = Btn("auction-pass-btn", () => Send(CommandType.AuctionPass));
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                auctionBidBtns[i] = Btn($"auction-bid{i + 1}-btn", () => flow?.Request(GameCommand.Bid(auctionBidAmounts[slot])));
            }

            // camera
            boardCamera = FindFirstObjectByType<BoardCamera>();
            focusBtn = Btn("focus-btn", () => boardCamera?.ReturnToDirector());
            GameSettings.Changed += UpdateSpeedLabel;
            UpdateSpeedLabel();

            // Phones scroll by dragging; scrollbars only get in the way.
            if (IsMobileLayout)
            {
                root.AddToClassList("mobile");
                root.Query<ScrollView>().ForEach(sv =>
                {
                    sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                    sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                    sv.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
                });
            }
        }

        private string PlayerName
        {
            get
            {
                string name = SeatRules.Sanitize(menuName.value, "Player 1");
                PlayerPrefs.SetString(NamePref, name);
                return name;
            }
        }

        private void Send(CommandType type, int space = -1) => flow?.Request(new GameCommand(type, space));

        private static void Show(VisualElement e) => e.RemoveFromClassList("hidden");
        private static void Hide(VisualElement e) => e.AddToClassList("hidden");
        private static void SetVisible(VisualElement e, bool visible) => e.EnableInClassList("hidden", !visible);
        private static bool IsVisible(VisualElement e) => !e.ClassListContains("hidden");

        /// <summary>
        /// Shows a dialog, then adds "open" a frame later so its pop-in transition runs. The wanted state lives in
        /// userData so a close requested before the pop-in starts (e.g. an instant LAN join) still wins.
        /// </summary>
        private static void OpenModal(VisualElement backdrop)
        {
            backdrop.userData = true;
            Show(backdrop);
            backdrop.schedule.Execute(() =>
            {
                if (Equals(backdrop.userData, true)) backdrop.AddToClassList("open");
            }).StartingIn(16);
        }

        private static void CloseModal(VisualElement backdrop)
        {
            backdrop.userData = false;
            backdrop.RemoveFromClassList("open");
            backdrop.schedule.Execute(() =>
            {
                if (!Equals(backdrop.userData, true)) Hide(backdrop);
            }).StartingIn(180);
        }

        private void ShowOnlyScreen(VisualElement screen)
        {
            foreach (var s in new[] { menuScreen, connectScreen, lobbyScreen }) SetVisible(s, s == screen);
            SetVisible(hud, screen == null);
        }

        // ---------------------------------------------------------------- menus

        public void ShowMainMenu() => ShowOnlyScreen(menuScreen);

        private void ShowConnect(bool host)
        {
            connectAsHost = host;
            connectHeading.text = host ? "Host a game" : "Join a game";
            connectGoBtn.text = host ? "Create" : "Join";
            Hide(connectError);
            SetConnectMode(connectLan);
            ShowOnlyScreen(connectScreen);
        }

        private void SetConnectMode(bool lan)
        {
            connectLan = lan;
            modeOnlineBtn.EnableInClassList("selected", !lan);
            modeLanBtn.EnableInClassList("selected", lan);

            SetVisible(connectAddress, !connectAsHost);
            SetVisible(connectAddressLabel, !connectAsHost);
            connectAddressLabel.text = lan ? "Host IP address" : "Join code";
            connectAddress.value = "";
            if (connectAsHost)
                connectHint.text = lan
                    ? $"Players on the same network join using your IP address (port {NetSession.DefaultPort})."
                    : "You'll get a join code to share with friends anywhere.";
            else
                connectHint.text = lan
                    ? "Enter the IP address shown in the host's lobby, e.g. 192.168.1.20."
                    : "Enter the code shown in the host's lobby.";
        }

        public void ShowConnectError(string message)
        {
            connectError.text = message;
            Show(connectError);
        }

        public void ShowLobby(ILobby newLobby, bool online, string code, bool lan)
        {
            if (lobby != null) lobby.SeatsChanged -= RefreshLobby;
            lobby = newLobby;
            lobby.SeatsChanged += RefreshLobby;

            lobbyHeading.text = online ? "Online lobby" : "New game";
            SetVisible(lobbyCodeBox, online);
            lobbyCode.text = code ?? "";
            lobbyCodeLabel.text = lan ? "Host address" : "Join code";
            ShowOnlyScreen(lobbyScreen);
            RefreshLobby();

            if (online && lobby.CanEdit)
                lobbyStatus.text = lan ? "Players on your network can join with this address." : "Share the join code with your friends.";
            else if (online)
                lobbyStatus.text = "Waiting for the host to start the game...";
            else
                lobbyStatus.text = "Pass the device around on your turn. Computer players play themselves.";
        }

        private void RefreshLobby()
        {
            if (lobby == null) return;
            var seats = lobby.SeatList;
            lobbySeats.Clear();
            for (int i = 0; i < seats.Count; i++)
            {
                int index = i;
                var seat = seats[i];
                var row = new VisualElement();
                row.AddToClassList("seat");

                var stripe = new VisualElement();
                stripe.AddToClassList("seat__stripe");
                stripe.style.backgroundColor = SeatRules.PlayerColors[i % SeatRules.PlayerColors.Length];
                row.Add(stripe);

                // Token picker: portrait between arrows, with the token's name.
                var picker = new VisualElement();
                picker.AddToClassList("seat__picker");
                bool canPick = lobby.IsMine(i);
                var prev = new Button(() => { Sfx.Play(SfxKind.Click); lobby.SetToken(index, SeatRules.CycleToken(lobby.SeatList, index, -1)); }) { text = "<" };
                var next = new Button(() => { Sfx.Play(SfxKind.Click); lobby.SetToken(index, SeatRules.CycleToken(lobby.SeatList, index, 1)); }) { text = ">" };
                prev.AddToClassList("arrow");
                next.AddToClassList("arrow");
                var portrait = new VisualElement();
                portrait.AddToClassList("seat__portrait");
                portrait.style.backgroundImage = new StyleBackground(TokenPortraits.Get(seat.Token));
                var tokenName = new Label(TokenCatalog.Get(seat.Token).Name);
                tokenName.AddToClassList("seat__token-name");
                SetVisible(prev, canPick);
                SetVisible(next, canPick);
                picker.Add(prev);
                picker.Add(portrait);
                picker.Add(next);
                picker.Add(tokenName);
                row.Add(picker);

                if (lobby.CanEdit && lobby.IsMine(i))
                {
                    var field = new TextField { value = seat.Name, maxLength = 16 };
                    field.AddToClassList("seat__name");
                    field.RegisterCallback<FocusOutEvent>(_ => lobby.RenameSeat(index, SeatRules.Sanitize(field.value, seat.Name)));
                    row.Add(field);
                }
                else
                {
                    var name = new Label(seat.Name);
                    name.AddToClassList("seat__name");
                    row.Add(name);
                }

                string badge = seat.Kind == SeatKind.Cpu ? "CPU" : lobby.IsMine(i) ? "YOU" : "ONLINE";
                var badgeLabel = new Label(badge);
                badgeLabel.AddToClassList("seat__badge");
                row.Add(badgeLabel);

                if (lobby.CanEdit && i > 0)
                {
                    var remove = new Button(() => lobby.RemoveSeat(index)) { text = "X" };
                    remove.AddToClassList("arrow");
                    row.Add(remove);
                }
                lobbySeats.Add(row);
            }

            bool full = seats.Count >= SeatRules.MaxSeats;
            SetVisible(lobbyEditRow, lobby.CanEdit);
            lobbyAddHumanBtn.SetEnabled(!full);
            lobbyAddCpuBtn.SetEnabled(!full);
            SetVisible(lobbyStartBtn, lobby.CanEdit);
            lobbyStartBtn.SetEnabled(seats.Count >= 2);
        }

        public void ShowBusy(string text)
        {
            busyText.text = text;
            OpenModal(busyModal);
        }

        public void HideBusy() => CloseModal(busyModal);

        public void ShowMessage(string title, string text, Action onOk = null, string okText = "OK", bool showCancel = false)
        {
            messageTitle.text = title;
            messageText.text = text;
            messageOkBtn.text = okText;
            messageOk = onOk;
            SetVisible(messageCancelBtn, showCancel);
            OpenModal(messageModal);
        }

        // ---------------------------------------------------------------- in-game

        public void BeginGame(GameFlow gameFlow)
        {
            if (lobby != null) lobby.SeatsChanged -= RefreshLobby;
            lobby = null;
            flow = gameFlow;
            ShowOnlyScreen(null);
            log.Clear();

            playerBar.Clear();
            chips.Clear();
            shownMoney = new float[flow.Game.Players.Count];
            targetMoney = new float[flow.Game.Players.Count];
            for (int i = 0; i < flow.Game.Players.Count; i++)
            {
                var color = SeatRules.PlayerColors[i % SeatRules.PlayerColors.Length];
                var chip = new VisualElement();
                chip.AddToClassList("chip");

                var avatar = new VisualElement { name = "avatar" };
                avatar.AddToClassList("chip__avatar");
                avatar.style.backgroundImage = new StyleBackground(TokenPortraits.Get(flow.SeatList[i].Token));
                avatar.style.borderTopColor = avatar.style.borderBottomColor = avatar.style.borderLeftColor = avatar.style.borderRightColor = color;
                chip.Add(avatar);

                var info = new VisualElement();
                info.AddToClassList("chip__info");
                var name = new Label { name = "name" };
                name.AddToClassList("chip__name");
                var money = new Label { name = "money" };
                money.AddToClassList("chip__money");
                var status = new Label { name = "status" };
                status.AddToClassList("chip__status");
                info.Add(name);
                info.Add(money);
                info.Add(status);
                chip.Add(info);
                chip.AddToClassList("glass");

                int player = i;
                chip.AddManipulator(new Clickable(() =>
                {
                    Sfx.Play(SfxKind.Click);
                    ShowPlayerSheet(player);
                }));

                playerBar.Add(chip);
                chips.Add(chip);
                shownMoney[i] = targetMoney[i] = flow.Game.Players[i].Money;
            }
            Refresh();
        }

        private Rect appliedSafeArea;

        /// <summary>
        /// Keeps the HUD and menus clear of the notch / Dynamic Island and the home indicator. Dialog backdrops are
        /// not inset, so their blur still covers the whole screen.
        /// </summary>
        private void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            var panel = document.rootVisualElement.panel;
            if (safe == appliedSafeArea || panel == null || float.IsNaN(panel.visualTree.layout.width) || panel.visualTree.layout.width <= 0) return;
            appliedSafeArea = safe;
            float scale = Screen.width / panel.visualTree.layout.width;
            float left = safe.xMin / scale, right = (Screen.width - safe.xMax) / scale;
            float top = (Screen.height - safe.yMax) / scale, bottom = safe.yMin / scale;
            foreach (var e in new[] { hud, menuScreen, connectScreen, lobbyScreen })
            {
                e.style.left = left;
                e.style.right = right;
                e.style.top = top;
                e.style.bottom = bottom;
            }
        }

        private void Update()
        {
            ApplySafeArea();
            ApplyGlass();
            SetVisible(focusBtn, BoardCamera.IsManual && IsVisible(hud));
            if (flow == null || flow.Game == null) return;
            // Money counters roll toward the amounts the animation has reached (not the engine's final state).
            for (int i = 0; i < chips.Count && i < shownMoney.Length; i++)
            {
                var p = flow.Game.Players[i];
                float target = targetMoney[i];
                if (Mathf.Approximately(shownMoney[i], target)) continue;
                shownMoney[i] = Mathf.MoveTowards(shownMoney[i], target, Mathf.Max(30f, Mathf.Abs(target - shownMoney[i]) * 4f) * Time.unscaledDeltaTime);
                chips[i].Q<Label>("money").text = p.IsBankrupt ? "" : $"${Mathf.RoundToInt(shownMoney[i])}";
            }
        }

        public void Refresh()
        {
            if (flow == null || flow.Game == null) return;
            var game = flow.Game;
            var current = game.CurrentPlayer;

            if (!flow.IsPlaying)
                for (int i = 0; i < targetMoney.Length; i++) targetMoney[i] = game.Players[i].Money;
            for (int i = 0; i < chips.Count; i++) RefreshChip(i);

            // A trade offer waiting for a local player's answer.
            if (game.Phase == TurnPhase.AwaitingTradeResponse && flow.CanLocalAct && answeredOffer != game.PendingTrade)
            {
                answeredOffer = game.PendingTrade;
                ShowTradeOffer(game.PendingTrade);
            }
            if (game.Phase != TurnPhase.AwaitingTradeResponse && IsVisible(tradeAnswerRow) && IsVisible(tradeModal))
                CloseModal(tradeModal);
            if (IsVisible(playerModal) && sheetPlayer >= 0) FillPlayerSheet(sheetPlayer);

            if (game.Phase == TurnPhase.AwaitingAuctionBid)
            {
                FillAuction();
                OpenModal(auctionModal);
            }
            else if (IsVisible(auctionModal)) CloseModal(auctionModal);

            bool mine = flow.LocalControlsSeat(game.CurrentPlayerIndex) && !game.IsOver
                        && game.Phase != TurnPhase.AwaitingTradeResponse && game.Phase != TurnPhase.AwaitingAuctionBid;
            bool canAct = flow.CanLocalAct;
            statusLabel.text = StatusText(game, mine);

            var phase = game.Phase;
            bool rollPhase = mine && phase == TurnPhase.AwaitingRoll;
            SetVisible(rollBtn, rollPhase);
            SetVisible(payFineBtn, rollPhase && current.InJail);
            SetVisible(useCardBtn, rollPhase && current.InJail && current.JailCards.Count > 0);
            SetVisible(buyBtn, mine && phase == TurnPhase.AwaitingBuyDecision);
            SetVisible(declineBtn, mine && phase == TurnPhase.AwaitingBuyDecision);
            SetVisible(payDebtBtn, mine && phase == TurnPhase.AwaitingDebtPayment);
            SetVisible(bankruptBtn, mine && phase == TurnPhase.AwaitingDebtPayment);
            SetVisible(endTurnBtn, mine && phase == TurnPhase.AwaitingEndTurn);
            SetVisible(manageBtn, mine);

            rollBtn.SetEnabled(canAct);
            payFineBtn.SetEnabled(canAct && game.CanPayJailFine);
            useCardBtn.SetEnabled(canAct && game.CanUseJailCard);
            buyBtn.SetEnabled(canAct && game.CanBuyPending);
            declineBtn.SetEnabled(canAct);
            payDebtBtn.SetEnabled(canAct && game.CanPayDebts);
            bankruptBtn.SetEnabled(canAct);
            endTurnBtn.SetEnabled(canAct);
            manageBtn.SetEnabled(canAct);
            if (phase == TurnPhase.AwaitingBuyDecision) buyBtn.text = $"BUY ${BoardLayout.Spaces[game.PendingPurchase].Price}";
            if (phase == TurnPhase.AwaitingDebtPayment) payDebtBtn.text = $"PAY ${game.DebtTotal}";

            // Offer the deed as soon as a local player lands on something for sale.
            bool enteredBuy = phase == TurnPhase.AwaitingBuyDecision &&
                              (lastPhase != TurnPhase.AwaitingBuyDecision || lastPhasePlayer != game.CurrentPlayerIndex);
            if (enteredBuy && mine) ShowDeed(game.PendingPurchase, purchase: true);
            if (phase != TurnPhase.AwaitingBuyDecision && deedIsPurchase && IsVisible(deedModal)) CloseModal(deedModal);
            lastPhase = phase;
            lastPhasePlayer = game.CurrentPlayerIndex;

            if (IsVisible(deedModal)) FillDeed(deedSpace, deedIsPurchase);
            if (IsVisible(manageModal))
            {
                if (mine) FillManage();
                else CloseModal(manageModal);
            }
        }

        /// <summary>Disables every action while animations play or a request is in flight.</summary>
        public void LockActions()
        {
            foreach (var b in new[] { rollBtn, payFineBtn, useCardBtn, buyBtn, declineBtn, payDebtBtn, bankruptBtn, endTurnBtn, manageBtn, deedBuyBtn, deedDeclineBtn })
                b.SetEnabled(false);
            // A buy decision was just made (from the dialog or the big button): get out of the way of the celebration.
            if (deedIsPurchase && IsVisible(deedModal)) CloseModal(deedModal);
        }

        private string StatusText(MonopolyGame game, bool mine)
        {
            var p = game.CurrentPlayer;
            if (game.IsOver) return $"{game.Winner.Name} wins!";
            if (flow.IsWaitingForHost) return "Sending to host...";
            if (game.Phase == TurnPhase.AwaitingAuctionBid) return $"Auction: {BoardLayout.Spaces[game.Auction.Space].Name}";
            if (game.Phase == TurnPhase.AwaitingTradeResponse)
            {
                var partner = game.Players[game.PendingTrade.To];
                return flow.LocalControlsSeat(partner.Id) ? $"{partner.Name}: {p.Name} offers you a trade" : $"Waiting for {partner.Name} to answer the trade...";
            }
            if (!mine)
            {
                var seat = flow.SeatList[game.CurrentPlayerIndex];
                return seat.Kind == SeatKind.Cpu ? $"{p.Name} is thinking..." : $"Waiting for {p.Name}...";
            }
            string who = flow.HasSeveralLocalHumans ? $"{p.Name}: " : "";
            switch (game.Phase)
            {
                case TurnPhase.AwaitingRoll:
                    return who + (p.InJail ? $"In jail! Roll doubles, or pay ${BoardLayout.JailFine} (try {p.JailAttempts + 1} of 3)" : "Your roll!");
                case TurnPhase.AwaitingBuyDecision:
                    var def = BoardLayout.Spaces[game.PendingPurchase];
                    return who + (game.CanBuyPending ? $"Buy {def.Name}?" : $"{def.Name} costs ${def.Price}. Mortgage something or pass.");
                case TurnPhase.AwaitingDebtPayment:
                    return who + $"You owe ${game.DebtTotal}. Sell or mortgage to raise it.";
                case TurnPhase.AwaitingEndTurn:
                    return who + "Build houses, or end your turn.";
                default:
                    return "";
            }
        }

        private void RefreshChip(int i)
        {
            var game = flow.Game;
            var p = game.Players[i];
            var chip = chips[i];
            chip.EnableInClassList("active", i == game.CurrentPlayerIndex && !game.IsOver);
            chip.EnableInClassList("bankrupt", p.IsBankrupt);

            var seat = flow.SeatList[i];
            string tag = seat.Kind == SeatKind.Cpu ? " (CPU)" : flow.LocalControlsSeat(i) && flow.IsOnline ? " (you)" : "";
            chip.Q<Label>("name").text = p.Name + tag;
            if (Mathf.Approximately(shownMoney[i], targetMoney[i])) chip.Q<Label>("money").text = p.IsBankrupt ? "" : $"${Mathf.RoundToInt(targetMoney[i])}";

            int owned = game.OwnedSpaces(p.Id).Count();
            string where = p.IsBankrupt ? "Bankrupt" : p.InJail ? "In jail" : BoardLayout.Spaces[p.Position].Name;
            chip.Q<Label>("status").text = owned > 0 ? $"{where} · {owned} deed{(owned == 1 ? "" : "s")}" : where;
        }

        public float TargetMoney(int playerId) => playerId >= 0 && playerId < targetMoney.Length ? targetMoney[playerId] : 0;

        /// <summary>Moves a player's counter to <paramref name="balance"/>, optionally with a floating "+$200".</summary>
        public void SetMoney(int playerId, float balance, bool showFloat)
        {
            if (playerId < 0 || playerId >= targetMoney.Length) return;
            int delta = Mathf.RoundToInt(balance - targetMoney[playerId]);
            targetMoney[playerId] = balance;
            if (showFloat) FloatMoney(playerId, delta);
        }

        /// <summary>A "+$200" / "-$50" that floats down from the player's chip.</summary>
        private void FloatMoney(int playerId, int delta)
        {
            if (playerId < 0 || playerId >= chips.Count || delta == 0) return;
            var chip = chips[playerId];
            var label = new Label(delta > 0 ? $"+${delta}" : $"-${-delta}") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("money-float");
            label.AddToClassList(delta > 0 ? "up" : "down");
            hud.Add(label);
            Rect r = chip.worldBound;
            Vector2 local = hud.WorldToLocal(new Vector2(r.xMin + r.width * 0.45f, r.yMax - 10f));
            label.style.left = local.x;
            label.style.top = local.y;
            label.schedule.Execute(() =>
            {
                label.style.translate = new Translate(0, 70);
                label.style.opacity = 0;
            }).StartingIn(30);
            label.schedule.Execute(() => label.RemoveFromHierarchy()).StartingIn(1400);
        }

        /// <summary>Big centre-screen callout ("PASS GO!", "SOLD!", "JAIL!").</summary>
        public void Announce(string title, string sub = "", float seconds = 1.4f)
        {
            announceTitle.text = title;
            announceSub.text = sub;
            SetVisible(announceSub, !string.IsNullOrEmpty(sub));
            announce.AddToClassList("show");
            announceHide?.Pause();
            announceHide = announce.schedule.Execute(() => announce.RemoveFromClassList("show")).StartingIn((long)(seconds * 1000));
        }

        public void AddLog(string message, bool highlight)
        {
            var line = new Label(message);
            line.AddToClassList("log-line");
            line.EnableInClassList("highlight", highlight);
            log.Add(line);
            while (log.contentContainer.childCount > MaxLogLines) log.contentContainer.RemoveAt(0);
            log.schedule.Execute(() => log.ScrollTo(line)).StartingIn(30);
        }

        public void ShowToast(string message)
        {
            toast.text = message;
            toast.AddToClassList("visible");
            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => toast.RemoveFromClassList("visible")).StartingIn(2400);
        }

        private void UpdateSpeedLabel() => speedBtn.text = $"{GameSettings.GameSpeed}x";

        private void OnDestroy() => GameSettings.Changed -= UpdateSpeedLabel;

        public bool IsAuctionVisible => IsVisible(auctionModal);

        private void CycleSpeed()
        {
            GameSettings.GameSpeed = GameSettings.GameSpeed >= 4 ? 1 : GameSettings.GameSpeed * 2;
            GameSettings.Save();
        }

        // ---------------------------------------------------------------- cards

        public bool IsCardVisible => IsVisible(cardModal);
        public bool IsDeedVisible => IsVisible(deedModal);

        public void DismissCard() => cardDismissed = true;

        public IEnumerator ShowCard(Card card, string playerName, bool waitForClick)
        {
            bool chance = card.Deck == CardDeck.Chance;
            cardHeader.text = chance ? "CHANCE" : "COMMUNITY CHEST";
            cardHeader.EnableInClassList("chance", chance);
            cardHeader.EnableInClassList("chest", !chance);
            cardPlayer.text = $"{playerName} drew";
            cardText.text = card.Text;
            SetVisible(cardOkBtn, waitForClick);
            cardDismissed = false;
            Sfx.Play(SfxKind.Card);
            OpenModal(cardModal);

            float timeout = waitForClick ? float.MaxValue : 2.6f;
            for (float t = 0; t < timeout && !cardDismissed; t += Time.deltaTime) yield return null;
            CloseModal(cardModal);
            yield return new WaitForSeconds(0.2f);
        }

        // ---------------------------------------------------------------- deeds

        public void ShowDeed(int space, bool purchase)
        {
            deedSpace = space;
            deedIsPurchase = purchase;
            FillDeed(space, purchase);
            OpenModal(deedModal);
        }

        private void FillDeed(int space, bool purchase)
        {
            var game = flow.Game;
            var def = BoardLayout.Spaces[space];
            var st = game.GetProperty(space);

            deedImage.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Tiles/" + def.Texture));
            deedTitle.text = def.Name;
            SetVisible(deedGroup, def.IsOwnable);
            deedGroup.style.backgroundColor = GroupColor(def.Group);
            deedRows.Clear();

            switch (def.Type)
            {
                case SpaceType.Property:
                    DeedRow("Rent", def.Rent[0]);
                    DeedRow("Rent with colour set", def.Rent[0] * 2);
                    for (int h = 1; h <= 4; h++) DeedRow($"With {h} house{(h > 1 ? "s" : "")}", def.Rent[h]);
                    DeedRow("With hotel", def.Rent[5]);
                    DeedRow("House / hotel cost", def.HouseCost);
                    DeedRow("Mortgage value", def.MortgageValue);
                    break;
                case SpaceType.Station:
                    for (int n = 1; n <= 4; n++) DeedRow(n == 1 ? "Rent" : $"If {n} stations are owned", def.Rent[n - 1]);
                    DeedRow("Mortgage value", def.MortgageValue);
                    break;
                case SpaceType.Utility:
                    DeedRow("One utility owned", "4 x dice");
                    DeedRow("Both utilities owned", "10 x dice");
                    DeedRow("Mortgage value", def.MortgageValue);
                    break;
            }

            deedNote.text = DeedNote(def, st, game);

            SetVisible(deedBuyBtn, purchase);
            SetVisible(deedDeclineBtn, purchase);
            if (purchase)
            {
                deedBuyBtn.text = $"Buy ${def.Price}";
                deedBuyBtn.SetEnabled(flow.CanLocalAct && game.CanBuyPending);
                deedDeclineBtn.SetEnabled(flow.CanLocalAct);
            }
            deedCloseBtn.text = purchase ? "Later" : "Close";
        }

        private static string DeedNote(SpaceDef def, PropertyState st, MonopolyGame game)
        {
            switch (def.Type)
            {
                case SpaceType.Go: return $"Collect ${BoardLayout.GoSalary} salary every time you pass GO.";
                case SpaceType.Tax: return $"Pay ${def.TaxAmount} to the bank.";
                case SpaceType.Chance: return "Draw a Chance card.";
                case SpaceType.CommunityChest: return "Draw a Community Chest card.";
                case SpaceType.Jail: return $"Just visiting, unless you were sent here. Pay ${BoardLayout.JailFine}, use a card, or roll doubles to get out.";
                case SpaceType.FreeParking: return "Nothing happens here. Take a break.";
                case SpaceType.GoToJail: return "Go directly to jail. Do not pass GO, do not collect $200.";
            }
            if (!st.IsOwned) return $"For sale: ${def.Price}";
            string owner = game.Players[st.Owner].Name;
            string buildings = st.HasHotel ? " with a hotel" : st.Houses > 0 ? $" with {st.Houses} house{(st.Houses > 1 ? "s" : "")}" : "";
            return $"Owned by {owner}{buildings}.{(st.Mortgaged ? " Mortgaged: no rent is collected." : "")}";
        }

        private void DeedRow(string label, int amount) => DeedRow(label, $"${amount}");

        private void DeedRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("deed-row");
            row.Add(new Label(label));
            row.Add(new Label(value));
            deedRows.Add(row);
        }

        // ---------------------------------------------------------------- manage properties

        internal void ShowManage()
        {
            FillManage();
            OpenModal(manageModal);
        }

        private void FillManage()
        {
            var game = flow.Game;
            var me = game.CurrentPlayer;
            bool canAct = flow.CanLocalAct;
            manageSummary.text = $"{me.Name}  ·  Cash ${me.Money}  ·  Net worth ${game.NetWorth(me.Id)}";

            float scroll = manageList.scrollOffset.y;
            manageList.Clear();
            var owned = game.OwnedSpaces(me.Id).ToList();
            if (owned.Count == 0)
            {
                var empty = new Label("You don't own any properties yet.");
                empty.AddToClassList("manage-empty");
                manageList.Add(empty);
                return;
            }

            foreach (int space in owned)
            {
                var def = BoardLayout.Spaces[space];
                var st = game.GetProperty(space);
                var row = new VisualElement();
                row.AddToClassList("manage-row");

                var chip = new VisualElement();
                chip.AddToClassList("manage-row__chip");
                chip.style.backgroundColor = GroupColor(def.Group);
                row.Add(chip);

                var name = new Label(def.Name);
                name.AddToClassList("manage-row__name");
                row.Add(name);

                string state = st.Mortgaged ? "Mortgaged" : st.HasHotel ? "Hotel" : st.Houses > 0 ? $"{st.Houses} house{(st.Houses > 1 ? "s" : "")}" : "";
                var stateLabel = new Label(state);
                stateLabel.AddToClassList("manage-row__state");
                row.Add(stateLabel);

                if (def.Type == SpaceType.Property)
                {
                    string buildText = st.Houses == 4 ? $"Hotel -${def.HouseCost}" : $"House -${def.HouseCost}";
                    row.Add(ManageButton(buildText, "green", canAct && game.CanBuildHouse(space), CommandType.BuildHouse, space));
                    row.Add(ManageButton($"Sell +${def.HouseCost / 2}", "gray", canAct && game.CanSellHouse(space), CommandType.SellHouse, space));
                }
                row.Add(st.Mortgaged
                    ? ManageButton($"Unmortgage -${def.UnmortgageCost}", "blue", canAct && game.CanUnmortgage(space), CommandType.Unmortgage, space)
                    : ManageButton($"Mortgage +${def.MortgageValue}", "yellow", canAct && game.CanMortgage(space), CommandType.Mortgage, space));
                manageList.Add(row);
            }
            manageList.schedule.Execute(() => manageList.scrollOffset = new Vector2(0, scroll));
        }

        private Button ManageButton(string text, string color, bool enabled, CommandType type, int space)
        {
            var b = new Button(() => { Sfx.Play(SfxKind.Click); Send(type, space); }) { text = text };
            b.AddToClassList("btn");
            b.AddToClassList("small");
            b.AddToClassList(color);
            b.SetEnabled(enabled);
            return b;
        }

        // ---------------------------------------------------------------- helpers

        public bool IsBlockingBoardClicks =>
            IsVisible(deedModal) || IsVisible(cardModal) || IsVisible(manageModal) || IsVisible(messageModal) ||
            IsVisible(busyModal) || IsVisible(playerModal) || IsVisible(tradeModal) || IsVisible(auctionModal) ||
            IsVisible(settingsModal) || !IsVisible(hud);

        // ---------------------------------------------------------------- log, glass

        internal void ToggleLog()
        {
            if (logDrawer.ClassListContains("open"))
            {
                logDrawer.RemoveFromClassList("open");
                logDrawer.schedule.Execute(() =>
                {
                    if (!logDrawer.ClassListContains("open")) Hide(logDrawer);
                }).StartingIn(260);
            }
            else
            {
                Show(logDrawer);
                logDrawer.schedule.Execute(() => logDrawer.AddToClassList("open")).StartingIn(16);
            }
        }

        /// <summary>
        /// Fakes a backdrop blur: every visible .glass element draws the blurred 3D frame as its background,
        /// sized to the whole screen and offset so the pixels line up with what's behind it.
        /// </summary>
        private void ApplyGlass()
        {
            var rt = BlurCapture.Texture;
            var panel = document.rootVisualElement.panel;
            if (!GameSettings.Blur && glassPlacement.Count > 0)
            {
                // Blur switched off: fall back to the plain panel colours.
                foreach (var e in glassPlacement.Keys) e.style.backgroundImage = StyleKeyword.Null;
                glassPlacement.Clear();
            }
            if (rt == null || panel == null || !GameSettings.Blur) return;
            Rect screen = panel.visualTree.layout;
            if (float.IsNaN(screen.width) || screen.width <= 0) return;
            glassElements.Clear();
            root.Query(className: "glass").ToList(glassElements);
            foreach (var e in glassElements)
            {
                if (e.resolvedStyle.display == DisplayStyle.None) continue;
                Rect wb = e.worldBound;
                var key = new Rect(wb.x, wb.y, screen.width, screen.height);
                // Only touch styles when something moved: restyling every element every frame is wasted work.
                if (e.style.backgroundImage.value.renderTexture == rt && glassPlacement.TryGetValue(e, out var last) && last == key) continue;
                glassPlacement[e] = key;
                e.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
                e.style.backgroundSize = new BackgroundSize(new Length(screen.width), new Length(screen.height));
                e.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(-wb.x));
                e.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(-wb.y));
            }
        }

        private readonly List<VisualElement> glassElements = new List<VisualElement>();
        private readonly Dictionary<VisualElement, Rect> glassPlacement = new Dictionary<VisualElement, Rect>();

        // ---------------------------------------------------------------- auction

        private void FillAuction()
        {
            var game = flow.Game;
            var a = game.Auction;
            var def = BoardLayout.Spaces[a.Space];
            auctionImage.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Tiles/" + def.Texture));
            auctionProperty.text = def.Name;
            auctionPrice.text = $"List price ${def.Price}";
            auctionHigh.text = a.HighBidder >= 0 ? $"${a.HighBid}" : "No bids yet";
            auctionLeader.text = a.HighBidder >= 0 ? $"{game.Players[a.HighBidder].Name} is winning" : "Bidding starts at $10";

            auctionBidders.Clear();
            foreach (int id in a.Bidders)
            {
                var avatar = new VisualElement();
                avatar.AddToClassList("auction__bidder");
                avatar.EnableInClassList("current", id == a.CurrentBidder);
                avatar.style.backgroundImage = new StyleBackground(TokenPortraits.Get(flow.SeatList[id].Token));
                var c = SeatRules.PlayerColors[id % SeatRules.PlayerColors.Length];
                avatar.style.borderTopColor = avatar.style.borderBottomColor = avatar.style.borderLeftColor = avatar.style.borderRightColor = c;
                auctionBidders.Add(avatar);
            }

            var bidder = game.Players[a.CurrentBidder];
            bool mine = flow.CanLocalAct;
            auctionTurn.text = mine ? (flow.HasSeveralLocalHumans ? $"{bidder.Name}, your bid (${bidder.Money} cash)" : $"Your bid (${bidder.Money} cash)")
                                    : $"{bidder.Name} is bidding...";
            SetVisible(auctionActions, mine);

            // Quick bids: small, medium and big raises (or opening offers when nobody has bid yet).
            int[] raises = a.HighBidder < 0
                ? new[] { AuctionState.MinimumBid, Mathf.Max(AuctionState.MinimumBid, def.Price / 2 / 10 * 10), def.Price }
                : new[] { a.HighBid + 10, a.HighBid + 50, a.HighBid + 100 };
            for (int i = 0; i < 3; i++)
            {
                auctionBidAmounts[i] = raises[i];
                auctionBidBtns[i].text = $"${raises[i]}";
                auctionBidBtns[i].SetEnabled(mine && game.CanBid(raises[i]));
            }
            auctionPassBtn.SetEnabled(mine);
        }

        // ---------------------------------------------------------------- settings

        internal void ShowSettings()
        {
            FillSettings();
            OpenModal(settingsModal);
        }

        private void FillSettings()
        {
            settingsList.Clear();
            Section("GRAPHICS");
            Choice("Quality", new[] { "Low", "Medium", "High" }, (int)GameSettings.Quality, i => GameSettings.Quality = (GraphicsQuality)i);
            Choice("Frosted glass blur", new[] { "Off", "On" }, GameSettings.Blur ? 1 : 0, i => GameSettings.Blur = i == 1);
            int[] rates = { 30, 60, 120 };
            Choice("Frame rate", new[] { "30", "60", "120" }, System.Array.IndexOf(rates, GameSettings.FrameRate), i => GameSettings.FrameRate = rates[i]);

            Section("AUDIO");
            Choice("Sound effects", new[] { "Off", "Low", "Medium", "High" }, GameSettings.Volume, i =>
            {
                GameSettings.Volume = i;
                Sfx.Volume = GameSettings.VolumeLevel;
            });

            Section("CAMERA");
            Choice("After you move the camera", new[] { "Stay until Focus", "Reset each turn" }, GameSettings.CameraAutoReturn ? 1 : 0,
                   i => GameSettings.CameraAutoReturn = i == 1);
            Choice("Camera shake", new[] { "Off", "On" }, GameSettings.CameraShake ? 1 : 0, i => GameSettings.CameraShake = i == 1);

            Section("GAMEPLAY");
            int[] speeds = { 1, 2, 4 };
            Choice("Animation speed", new[] { "1x", "2x", "4x" }, System.Array.IndexOf(speeds, GameSettings.GameSpeed), i => GameSettings.GameSpeed = speeds[i]);
            Choice("Computer players", new[] { "Relaxed", "Normal", "Fast" }, GameSettings.CpuSpeed, i => GameSettings.CpuSpeed = i);
        }

        private void Section(string title)
        {
            var label = new Label(title);
            label.AddToClassList("settings-section");
            settingsList.Add(label);
        }

        private void Choice(string label, string[] options, int selected, Action<int> pick)
        {
            var row = new VisualElement();
            row.AddToClassList("settings-row");
            var text = new Label(label);
            text.AddToClassList("settings-row__label");
            row.Add(text);
            var group = new VisualElement();
            group.AddToClassList("segmented");
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var b = new Button(() =>
                {
                    Sfx.Play(SfxKind.Click);
                    pick(index);
                    GameSettings.Save();
                    FillSettings();
                }) { text = options[i] };
                b.AddToClassList("btn");
                b.AddToClassList("gray");
                b.EnableInClassList("selected", i == selected);
                group.Add(b);
            }
            row.Add(group);
            settingsList.Add(row);
        }

        // ---------------------------------------------------------------- player sheet & trading

        internal void ShowPlayerSheet(int player)
        {
            if (flow == null || flow.Game == null) return;
            sheetPlayer = player;
            FillPlayerSheet(player);
            OpenModal(playerModal);
        }

        private void FillPlayerSheet(int player)
        {
            var game = flow.Game;
            var p = game.Players[player];
            var color = SeatRules.PlayerColors[player % SeatRules.PlayerColors.Length];
            playerAvatar.style.backgroundImage = new StyleBackground(TokenPortraits.Get(flow.SeatList[player].Token));
            playerAvatar.style.borderTopColor = playerAvatar.style.borderBottomColor = playerAvatar.style.borderLeftColor = playerAvatar.style.borderRightColor = color;
            playerName.text = p.Name;
            string extras = p.JailCards.Count > 0 ? $"  ·  {p.JailCards.Count} Get Out of Jail card{(p.JailCards.Count > 1 ? "s" : "")}" : "";
            playerCash.text = p.IsBankrupt ? "Bankrupt" : $"${p.Money}  ·  worth ${game.NetWorth(p.Id)}{extras}";

            playerList.Clear();
            var owned = game.OwnedSpaces(p.Id).ToList();
            if (owned.Count == 0)
            {
                var empty = new Label("No properties yet.");
                empty.AddToClassList("manage-empty");
                playerList.Add(empty);
            }
            foreach (int space in owned) playerList.Add(PropertyRow(space, selectable: false, selected: false, locked: false, null));

            // You can trade with anyone else, on your own turn, when nothing else is pending.
            int me = game.CurrentPlayerIndex;
            bool canTrade = flow.CanLocalAct && player != me && !p.IsBankrupt && flow.LocalControlsSeat(me)
                            && (game.Phase == TurnPhase.AwaitingRoll || game.Phase == TurnPhase.AwaitingEndTurn);
            SetVisible(playerTradeBtn, canTrade);
        }

        private VisualElement PropertyRow(int space, bool selectable, bool selected, bool locked, Action onClick)
        {
            var def = BoardLayout.Spaces[space];
            var st = flow.Game.GetProperty(space);
            var row = new VisualElement();
            row.AddToClassList("prop-row");
            row.EnableInClassList("selectable", selectable);
            row.EnableInClassList("selected", selected);
            row.EnableInClassList("locked", locked);

            var chip = new VisualElement();
            chip.AddToClassList("prop-row__chip");
            chip.style.backgroundColor = GroupColor(def.Group);
            row.Add(chip);
            var name = new Label(def.Name);
            name.AddToClassList("prop-row__name");
            row.Add(name);
            string state = locked ? "Has buildings" : st.Mortgaged ? "Mortgaged" : st.HasHotel ? "Hotel" : st.Houses > 0 ? $"{st.Houses} house{(st.Houses > 1 ? "s" : "")}" : $"${def.Price}";
            var stateLabel = new Label(state);
            stateLabel.AddToClassList("prop-row__state");
            row.Add(stateLabel);

            if (selectable && !locked && onClick != null)
                row.AddManipulator(new Clickable(() =>
                {
                    Sfx.Play(SfxKind.Click);
                    onClick();
                }));
            return row;
        }

        internal void OpenTradeBuilder(int partner)
        {
            tradePartner = partner;
            tradeGive.Clear();
            tradeGet.Clear();
            tradeGiveAmount = tradeGetAmount = 0;
            SetVisible(tradeProposeRow, true);
            SetVisible(tradeAnswerRow, false);
            Q<VisualElement>("trade-give-stepper").Query<Button>().ForEach(b => SetVisible(b, true));
            Q<VisualElement>("trade-get-stepper").Query<Button>().ForEach(b => SetVisible(b, true));
            FillTradeBuilder();
            OpenModal(tradeModal);
        }

        private void FillTradeBuilder()
        {
            var game = flow.Game;
            int me = game.CurrentPlayerIndex;
            tradeTitle.text = $"Trade with {game.Players[tradePartner].Name}";
            tradeGiveLabel.text = "You give";
            tradeGetLabel.text = "You get";
            FillTradeColumn(tradeGiveList, me, tradeGive);
            FillTradeColumn(tradeGetList, tradePartner, tradeGet);
            tradeGiveCash.text = $"${tradeGiveAmount}";
            tradeGetCash.text = $"${tradeGetAmount}";

            var offer = BuildOffer();
            tradeSendBtn.SetEnabled(game.CanProposeTrade(offer) && flow.CanLocalAct);
            tradeNote.text = offer.IsEmpty ? "Tap properties and set cash to build an offer." :
                             flow.SeatList[tradePartner].Kind == SeatKind.Cpu ? "Computer players take fair deals, but never hand over a colour set cheaply." :
                             $"{game.Players[tradePartner].Name} will be asked to accept.";
        }

        /// <summary>Dev hook: pre-select trade items.</summary>
        internal void SelectForTrade(int give, int get, int giveCash)
        {
            if (give >= 0) tradeGive.Add(give);
            if (get >= 0) tradeGet.Add(get);
            tradeGiveAmount = giveCash;
            FillTradeBuilder();
        }

        internal void CloseDialogs()
        {
            foreach (var m in new[] { playerModal, tradeModal, manageModal, deedModal, settingsModal }) CloseModal(m);
            if (logDrawer.ClassListContains("open")) ToggleLog();
        }

        private void FillTradeColumn(ScrollView list, int owner, HashSet<int> chosen)
        {
            list.Clear();
            var owned = flow.Game.OwnedSpaces(owner).ToList();
            if (owned.Count == 0)
            {
                var empty = new Label("No properties");
                empty.AddToClassList("manage-empty");
                list.Add(empty);
            }
            foreach (int space in owned)
            {
                int s = space;
                bool locked = !flow.Game.IsTradable(owner, space);
                list.Add(PropertyRow(space, selectable: true, selected: chosen.Contains(space), locked: locked, () =>
                {
                    if (!chosen.Remove(s)) chosen.Add(s);
                    FillTradeBuilder();
                }));
            }
        }

        private void StepCash(ref int amount, int step, int max)
        {
            amount = Mathf.Clamp(amount + step, 0, Mathf.Max(0, max));
            FillTradeBuilder();
        }

        private TradeOffer BuildOffer()
            => new TradeOffer(flow.Game.CurrentPlayerIndex, tradePartner, tradeGive, tradeGet, tradeGiveAmount, tradeGetAmount);

        /// <summary>Shows an incoming offer to the player who must answer it.</summary>
        private void ShowTradeOffer(TradeOffer offer)
        {
            var game = flow.Game;
            tradePartner = offer.From;
            tradeTitle.text = $"{game.Players[offer.From].Name} offers {game.Players[offer.To].Name}";
            tradeGiveLabel.text = "You get";
            tradeGetLabel.text = "You give";
            tradeGiveList.Clear();
            tradeGetList.Clear();
            foreach (int s in offer.GiveProperties) tradeGiveList.Add(PropertyRow(s, false, false, false, null));
            foreach (int s in offer.GetProperties) tradeGetList.Add(PropertyRow(s, false, false, false, null));
            tradeGiveCash.text = $"${offer.GiveCash}";
            tradeGetCash.text = $"${offer.GetCash}";
            Q<VisualElement>("trade-give-stepper").Query<Button>().ForEach(b => SetVisible(b, false));
            Q<VisualElement>("trade-get-stepper").Query<Button>().ForEach(b => SetVisible(b, false));
            tradeNote.text = flow.HasSeveralLocalHumans ? $"Pass the device to {game.Players[offer.To].Name}." : "";
            SetVisible(tradeProposeRow, false);
            SetVisible(tradeAnswerRow, true);
            OpenModal(tradeModal);
        }

        /// <summary>True if the screen position (pixels, origin bottom-left) is over an interactive UI element.</summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            var panel = document.rootVisualElement.panel;
            if (panel == null) return false;
            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = panel.Pick(panelPos);
            return picked != null && picked != document.rootVisualElement && picked != panel.visualTree;
        }

        public static Color GroupColor(ColorGroup group)
        {
            switch (group)
            {
                case ColorGroup.Brown: return new Color32(107, 58, 46, 255);
                case ColorGroup.LightBlue: return new Color32(150, 214, 248, 255);
                case ColorGroup.Pink: return new Color32(217, 58, 150, 255);
                case ColorGroup.Orange: return new Color32(247, 148, 29, 255);
                case ColorGroup.Red: return new Color32(237, 27, 36, 255);
                case ColorGroup.Yellow: return new Color32(240, 220, 0, 255);
                case ColorGroup.Green: return new Color32(31, 178, 90, 255);
                case ColorGroup.DarkBlue: return new Color32(0, 114, 187, 255);
                case ColorGroup.Station: return new Color32(40, 40, 40, 255);
                case ColorGroup.Utility: return new Color32(150, 150, 150, 255);
                default: return Color.clear;
            }
        }
    }
}
