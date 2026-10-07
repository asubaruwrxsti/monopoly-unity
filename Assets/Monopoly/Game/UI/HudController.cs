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
            if (Application.isMobilePlatform)
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
            Btn("log-btn", () => logDrawer.ToggleInClassList("open"));
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
                var deeds = new VisualElement { name = "deeds" };
                deeds.AddToClassList("chip__deeds");
                info.Add(name);
                info.Add(money);
                info.Add(status);
                info.Add(deeds);
                chip.Add(info);

                playerBar.Add(chip);
                chips.Add(chip);
                shownMoney[i] = flow.Game.Players[i].Money;
            }
            Refresh();
        }

        private Rect appliedSafeArea;

        /// <summary>Keeps the UI clear of the notch / Dynamic Island and the home indicator.</summary>
        private void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            var panel = document.rootVisualElement.panel;
            if (safe == appliedSafeArea || panel == null || float.IsNaN(panel.visualTree.layout.width) || panel.visualTree.layout.width <= 0) return;
            appliedSafeArea = safe;
            float scale = Screen.width / panel.visualTree.layout.width;
            root.style.position = Position.Absolute;
            root.style.left = safe.xMin / scale;
            root.style.right = (Screen.width - safe.xMax) / scale;
            root.style.top = (Screen.height - safe.yMax) / scale;
            root.style.bottom = safe.yMin / scale;
        }

        private void Update()
        {
            ApplySafeArea();
            if (flow == null || flow.Game == null) return;
            // Money counters roll toward their real values.
            for (int i = 0; i < chips.Count && i < shownMoney.Length; i++)
            {
                var p = flow.Game.Players[i];
                float target = p.Money;
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

            for (int i = 0; i < chips.Count; i++) RefreshChip(i);

            bool mine = flow.LocalControlsSeat(game.CurrentPlayerIndex) && !game.IsOver;
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
            if (Mathf.Approximately(shownMoney[i], p.Money)) chip.Q<Label>("money").text = p.IsBankrupt ? "" : $"${p.Money}";

            var owned = game.OwnedSpaces(p.Id).ToList();
            chip.Q<Label>("status").text = p.IsBankrupt ? "Bankrupt" : p.InJail ? "In jail" : BoardLayout.Spaces[p.Position].Name;

            var deeds = chip.Q("deeds");
            deeds.Clear();
            foreach (int space in owned)
            {
                var pip = new VisualElement();
                pip.AddToClassList("deed-pip");
                pip.EnableInClassList("mortgaged", game.GetProperty(space).Mortgaged);
                pip.style.backgroundColor = GroupColor(BoardLayout.Spaces[space].Group);
                deeds.Add(pip);
            }
        }

        /// <summary>A "+$200" / "-$50" that floats down from the player's chip.</summary>
        public void FloatMoney(int playerId, int delta)
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

        private void CycleSpeed()
        {
            Time.timeScale = Time.timeScale >= 4f ? 1f : Time.timeScale * 2f;
            speedBtn.text = $"{Time.timeScale:0}x";
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
            IsVisible(busyModal) || !IsVisible(hud);

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
