using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        enum OnlineChoice { None, Characters, Vehicles, Skins, Tracks, RaceModes, Cups, Custom }
        enum OnlineConnectionStep { Menu, HostServer, JoinServer, Rooms, HostDirect, JoinDirect, ServerSettings, HostChoice, JoinBrowser }
        sealed class OnlineControl { public string key; public Rect rect; public bool enabled; public Action action; }
        readonly List<OnlineControl> onlineButtons = new List<OnlineControl>();
        readonly Dictionary<string, TMP_InputField> onlineFields = new Dictionary<string, TMP_InputField>();
        readonly HashSet<string> onlineVisibleFields=new HashSet<string>();
        readonly HashSet<TMP_InputField> boundOnlineFields = new HashSet<TMP_InputField>();
        GameObject onlineInputRoot;
        NativeOnlineLobbyView onlineLobby;
        string onlineFocus, onlinePage;
        OnlineChoice onlineChoice;
        OnlineChoice onlineChoiceEntry;
        OnlineConnectionStep connectionStep;
        OnlineConnectionStep connectionReturnStep;
        string connectionReturnFocus;
        int draftCharacter, draftVehicle, draftSkin, roomListPage, onlineInputConsumedFrame = -1;
        float onlineNextNavigate;
        Vector2 onlineHeldDirection, onlineLastPointer;
        bool onlinePointerInitialized;
        bool roomListNeedsRefresh;


        NativeRaceHud OnlineHud => Race.hud;
        internal bool OnlineRaceMenuVisible => paused || PersonalResultsVisible;
        bool OnlineComplete => Latest != null && Latest.karts != null && Latest.karts.Length > 0 && Latest.karts.All(k => k.finished);
        int onlineTextEndFrame = -1;
        // TMP can finish Return/Escape before the menu Update sees the same key.
        // Keep that frame in text mode so it cannot also activate or leave a menu.
        bool OnlineEditing => onlineTextEndFrame == Time.frameCount
            || onlineFields.Values.Any(field => field && field.gameObject.activeInHierarchy && field.isFocused);
        string OnlinePage => ProfileFromSettings ? "Profiles" : !Open ? "Main" : onlineChoice != OnlineChoice.None ? onlineChoice.ToString()
            : confirmationOpen ? "Confirmation" : settingsPage ? "Online Settings" : profilesPage ? "Profiles" : setupPage ? "Race Setup" : Loading ? "Loading" : InRace ? PersonalResultsVisible ? "Results" : paused ? "Pause" : "Racing"
            : playerId == null ? connectionStep == OnlineConnectionStep.Menu ? "Online" : connectionStep.ToString() : "Lobby";

        internal void RenderNetworkUi()
        {
            if (!Race) return;
            OnlineHud.UseOnlineAuthoring(Open || ProfileFromSettings);
            if (Open || ProfileFromSettings) EnsureOnlineEventSystem();
            TickPersonalResults();
            bool lobbyVisible = Open && OnlinePage == "Lobby" && !(candidateTrack != null && !IsHost && watchTrackPreview);
            if (onlineLobby && onlineLobby.gameObject.activeSelf != lobbyVisible) onlineLobby.gameObject.SetActive(lobbyVisible);
            if (!Open && !ProfileFromSettings)
            {
                OnlineHud.NetworkVisible(false);
                if (onlineInputRoot) onlineInputRoot.SetActive(false);
                onlineButtons.Clear(); onlinePage = "Main"; onlineChoice = OnlineChoice.None;
                return;
            }
            onlineVisibleFields.Clear();
            bool showFields=!confirmationOpen && onlineChoice==OnlineChoice.None && !InRace && !Loading && (settingsPage || profilesPage || (playerId==null && connectionStep!=OnlineConnectionStep.Menu && connectionStep!=OnlineConnectionStep.Rooms));
            if(onlineInputRoot && onlineInputRoot.activeSelf!=showFields)onlineInputRoot.SetActive(showFields);
            if (!confirmationOpen && !settingsPage && !profilesPage && !IsHost && candidateTrack!=null && watchTrackPreview && !InRace && !Loading) {RenderTrackPreview();return;}
            if (setupPage && onlineChoice == OnlineChoice.None && !confirmationOpen) SetOnlineChoice(OnlineChoice.RaceModes);
            if (onlineChoice != OnlineChoice.None && !InRace && !Loading) { RenderOnlineChoice(); return; }
            if (!ProfileFromSettings && !profilesPage && playerId == null && !Loading && !InRace)
            {
                float menuWidth = OnlineHud.BeginFrame();
                bool transitioning;
                try { transitioning = OnlineHud.DrawMenuTransition(menuWidth); }
                finally { OnlineHud.EndFrame(); }
                if (transitioning) { OnlineHud.NetworkVisible(false); onlineButtons.Clear(); return; }
            }
            string page = OnlinePage;
            if(page!="Online")OnlineHud.HideNetworkWheel();
            if (onlinePage != page) { onlinePage = page; if (playerId != null && page!="Results") onlineFocus = null; }
            onlineButtons.Clear();
            OnlineHud.OnlineHover = key => onlineFocus = key;
            if (page == "Lobby")
            {
                OnlineHud.NetworkVisible(false);
                RenderOnlineLobby();
                HideUnusedOnlineFields();
                return;
            }
            OnlineHud.BeginNetworkFrame(OnlineAuthoredPageId());
            OnlineHud.NetworkOpacity(page == "Results" ? PersonalResultsAlpha : 1);
            try
            {
                if (page == "Confirmation") {RenderOnlineConfirmation();return;}
                if (page == "Online Settings") {RenderOnlineSettings();return;}
                if (page == "Profiles") {RenderOnlineProfiles();return;}
                if (page == "Racing")
                {
                    if (requestedSpectator) {RenderSpectatorControls();return;}
                    return;
                }
                if (playerId == null && !Loading)
                {
                    RenderOnlineConnection();
                    return;
                }

                OnlineHud.NetworkPicture("NetworkUi.Picture1");
                string title = page == "Race Setup" ? "Race Setup" : page == "Lobby" ? "Room " + Room : page == "Results" ? "Race Results" : page == "Pause" ? "Paused" : "Online";
                OnlineHud.NetworkLabel("NetworkUi.Label2", title);
                string subtitle = page == "Lobby" ? (serverConnection ? "Server room" : "IP direct") + "  /  " + Participants.Length + " of 8 players"
                    : page == "Pause" ? "The race continues while you pause." : page == "Results" ? NativeTrackCatalog.NameAt(Mathf.Max(0, NativeTrackCatalog.IndexOf((roundConfig ?? Config).track))) + "  /  Round " + (lastResultsRound == Round ? CompletedRounds : CompletedRounds + 1)
                    : "Choose how to play together.";
                if(page!="Lobby")OnlineHud.NetworkLabel("NetworkUi.Label3", subtitle);
                if (page == "Loading") RenderOnlineLoading();
                else if (page == "Race Setup") RenderRaceSetup();

                else if (page == "Results" || page == "Pause") RenderOnlineRaceMenu(page == "Results");
                if (page != "Results" && page != "Race Setup" && page != "Lobby") OnlineHud.NetworkLabel("NetworkUi.Label4", Status);
            }
            finally { HideUnusedOnlineFields();OnlineHud.EndNetworkFrame(); }
        }
        string OnlineAuthoredPageId()
        {
            if (confirmationOpen) return "Confirmation";
            if (settingsPage) return "RoomServerSettings";
            if (profilesPage) return "Profiles";
            if (playerId == null && connectionStep == OnlineConnectionStep.HostServer && !Loading && !InRace) return "RoomHost";
            if (playerId == null && !Loading && !InRace)
                return connectionStep == OnlineConnectionStep.Menu ? "Entry" : connectionStep.ToString() +
                    (advancedConnection && (connectionStep == OnlineConnectionStep.JoinDirect || connectionStep == OnlineConnectionStep.JoinServer) ? "Advanced" : "");
            if (OnlinePage == "Racing") return requestedSpectator ? "Spectator" : "Racing";
            return OnlinePage.Replace(" ", "");
        }
        void OnlineButton(string key, string caption, Action action, bool enabled = true, string viewKey = null)
        {
            string binding = viewKey ?? key;
            var rect = OnlineHud.OnlineElementRect(binding);
            if (rect.width <= 0 || rect.height <= 0) return;
            var button = new OnlineControl { key = key, rect = rect, enabled = enabled, action = action };
            onlineButtons.Add(button);
            if (onlineFocus == null && enabled) onlineFocus = key;
            if (OnlineHud.NetworkButton(binding, caption, onlineFocus == key, enabled, key)) ActivateOnlineButton(button);
        }
        void ActivateOnlineButton(OnlineControl button)
        {
            if (!button.enabled) return;
            onlineFocus = button.key; OnlineHud.NetworkSound(true); button.action();
        }
        void BeginConnectionStep(OnlineConnectionStep step, string origin)
        {
            if (connectionStep == OnlineConnectionStep.Rooms && transport != null)
            {
                roomListNeedsRefresh = true;
                transport.Dispose(); transport = null;
            }
            connectionReturnStep = connectionStep;
            connectionReturnFocus = origin; connectionStep = step; onlinePage = null;
            onlineFocus = step == OnlineConnectionStep.Rooms ? "refresh" : "connect"; advancedConnection=false;
            onlineHeldDirection = Vector2.zero;
            bool direct = step == OnlineConnectionStep.HostDirect || step == OnlineConnectionStep.JoinDirect;
            serverConnection = !direct;
            Address = direct ? directAddress : serverAddress; Port = direct ? step==OnlineConnectionStep.HostDirect?directPort:lastJoinPort : serverPort;
            if (step == OnlineConnectionStep.HostServer) Room = Guid.NewGuid().ToString("N").Substring(0, 8);
            if (step == OnlineConnectionStep.JoinBrowser) { Address = directAddress; Port = lastJoinPort; onlineFocus = "field-address"; RefreshRooms(); }
            if (step == OnlineConnectionStep.HostChoice) onlineFocus = "host-ip";
            if (step == OnlineConnectionStep.Rooms) { roomListNeedsRefresh = false; availableRooms = new NativeNetBroker.RoomInfo[0]; roomListPage = 0; RefreshRooms(); }
        }
        void CancelConnectionStep()
        {
            var destination = connectionReturnStep;
            bool direct = connectionStep == OnlineConnectionStep.HostDirect || connectionStep == OnlineConnectionStep.JoinDirect;
            if (direct) {if(connectionStep==OnlineConnectionStep.JoinDirect){directAddress=Address;lastJoinPort=Port;}else directPort=Port;}
            if (transport != null) Leave(false);
            if(destination!=OnlineConnectionStep.Rooms && roomListTransport!=null){roomListTransport.Dispose();roomListTransport=null;}
            Status="Choose how to play together.";
            connectionStep = destination; advancedConnection=false; onlinePage = null; onlineFocus = connectionReturnFocus ?? "create-server";
            connectionReturnStep = OnlineConnectionStep.Menu;
            connectionReturnFocus = destination == OnlineConnectionStep.Rooms ? "join-server" : "create-server";
            if (!direct) { Address = serverAddress; Port = serverPort; }
            foreach (var field in onlineFields.Values) if(field) field.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
            if (onlineInputRoot) onlineInputRoot.SetActive(false);
            OnlineHud.NetworkBackSound();
            if (destination == OnlineConnectionStep.Rooms && roomListNeedsRefresh)
            {
                roomListNeedsRefresh = false; availableRooms = new NativeNetBroker.RoomInfo[0]; roomListPage = 0;
                RefreshRooms();
            }
        }
        void ConnectFromMenu(string requestedMode)
        {
            var step = connectionStep;
            if (requestedMode == "host" || requestedMode == "join")
            {
                if(requestedMode=="join"){directAddress = Address;lastJoinPort=Port;} else {invitationAddress="";invitationDraft=null;invitationPort=Port;directPort=Port;} Room = "fumo";
            }
            else { Address = serverAddress; Port = serverPort; }
            SaveOnlinePreferences();
            var role=requestedSpectator; Connect(requestedMode); requestedSpectator=role; connectionStep = step; onlineFocus = "connect";
            if (step == OnlineConnectionStep.Rooms) onlineFocus = "refresh";
        }
        void LeaveToOnlineMenu()
        {
            Leave(false); Open = true; Status="Choose how to play together."; onlineFocus = "host-direct";
            if (Race) Race.NetworkOpenMenu();
        }
        void RenderOnlineConnection()
        {
            bool root=connectionStep==OnlineConnectionStep.Menu;
            if(root)
            {
                RenderOnlineEntryWheel();
                return;
            }
            OnlineHud.NetworkPicture("OnlineConnection.Picture1");

            if (connectionStep == OnlineConnectionStep.HostChoice) RenderHostChoice();
            else if (connectionStep == OnlineConnectionStep.JoinBrowser) RenderJoinBrowser();
            else if (connectionStep == OnlineConnectionStep.HostServer) RenderRoomHost();
            else if(connectionStep==OnlineConnectionStep.Rooms)RenderOnlineRooms();
            else RenderConnectionDialog();
            if (connectionStep == OnlineConnectionStep.HostDirect) OnlineHud.NetworkLabel("host-error", ConnectionError());
            else if (connectionStep != OnlineConnectionStep.HostChoice && connectionStep != OnlineConnectionStep.JoinBrowser) OnlineHud.NetworkLabel("OnlineConnection.Label4", ConnectionError());
        }
        void RenderOnlineRooms()
        {
            bool connecting=transport!=null&&playerId==null;
            OnlineHud.NetworkLabel("OnlineRooms.Label1", "Community Rooms");
            OnlineHud.NetworkLabel("OnlineRooms.Label2", serverAddress+":"+serverPort, localize:false);
            OnlineButton("refresh", "Refresh", RefreshRooms, !connecting);
            int first=roomListPage*5;var visible=availableRooms.Skip(first).Take(5).ToArray();
            if(visible.Length==0)OnlineHud.NetworkLabel("OnlineRooms.Label3", roomListTransport!=null?"Finding rooms...":"No open rooms");
            for(int i=0;i<visible.Length;i++)
            {
                var room=visible[i];
                OnlineHud.BeginOnlineRow("Rooms", i);
                OnlineHud.NetworkRow("OnlineRooms.Row1");
                OnlineHud.NetworkLabel("OnlineRooms.Label4", room.name, localize:false);
                OnlineHud.NetworkLabel("OnlineRooms.Label5", "ID: "+room.id+"  ·  "+room.count+" / "+room.capacity, localize:false);
                OnlineButton("room-"+room.id, room.racing?"Racing...":room.count>=room.capacity?"Full":"Join", ()=>{requestedSpectator=false;Room=room.id;ConnectFromMenu("server-join");}, !connecting&&!room.racing&&room.count<room.capacity, viewKey: "join");
                OnlineButton("watch-"+room.id, "Spectate", ()=>{requestedSpectator=true;Room=room.id;ConnectFromMenu("server-join");}, !connecting, viewKey: "watch");
                OnlineHud.EndOnlineRow();
            }
            OnlineButton("rooms-back", connecting?"Cancel":"Back", CancelConnectionStep);
            OnlineButton("rooms-create", "Host Game", ()=>BeginConnectionStep(OnlineConnectionStep.HostServer,"rooms-create"), !connecting);
            OnlineButton("rooms-id", "Join by Room ID", ()=>BeginConnectionStep(OnlineConnectionStep.JoinServer,"rooms-id"), !connecting);
            OnlineButton("rooms-prev", "Previous", ()=>roomListPage--, !connecting&&roomListPage>0);
            OnlineButton("rooms-next", "Next", ()=>roomListPage++, !connecting&&(roomListPage+1)*5<availableRooms.Length);
        }
        void RenderConnectionDialog()
        {
            bool hosting=connectionStep==OnlineConnectionStep.HostDirect||connectionStep==OnlineConnectionStep.HostServer;
            bool server=connectionStep==OnlineConnectionStep.ServerSettings;
            bool connecting=transport!=null&&playerId==null;
            string title=server?"Community Server":hosting?"Host Game":"Join Game";
            OnlineHud.NetworkLabel("ConnectionDialog.Label1", title);
            OnlineHud.NetworkLabel("ConnectionDialog.Label2", "Playing as "+PlayerName);
            EnsureOnlineFields();onlineInputRoot.SetActive(true);


            if(!hosting)
            {
                string key=connectionStep==OnlineConnectionStep.JoinServer?"room":"address";
                OnlineHud.NetworkLabel("ConnectionDialog.Label3", key=="room"?"Room ID":"Address");
                OnlineField(key, key=="room"?Room:Address, v=>{if(key=="room")Room=v;else Address=v;});
            }
            if(hosting||server||advancedConnection)
            {
                OnlineHud.NetworkLabel("ConnectionDialog.Label4", "Port");
                OnlineField("port", Port, v=>Port=v);
            }
            if(!server)OnlineButton("role", requestedSpectator?"Join as Spectator":"Join as Racer", ()=>requestedSpectator=!requestedSpectator, !connecting);
            if(!hosting&&!server)OnlineButton("advanced", "Advanced Connection", ()=>advancedConnection=!advancedConnection, !connecting);
            if (hosting) ConnectionHint("cancel", connecting ? "Cancel" : "Back", CancelConnectionStep);
            else OnlineButton("cancel", connecting?"Cancel":"Back", CancelConnectionStep);
            Action connect = ()=>
            {
                if(connecting)CancelConnectionStep();
                else if(server){serverAddress=Address;serverPort=Port;CancelConnectionStep();}
                else ConnectFromMenu(connectionStep==OnlineConnectionStep.HostDirect?"host":connectionStep==OnlineConnectionStep.JoinDirect?"join":connectionStep==OnlineConnectionStep.HostServer?"server-create":"server-join");
            };
            if (hosting) ConnectionHint("connect", connecting ? "Cancel" : "Host Game", connect);
            else OnlineButton("connect", connecting?"Cancel":server?"Done":"Join Game", connect);
        }
        void RenderOnlineLobby()
        {
            if (!onlineLobby)
            {
                EnsureOnlineEventSystem();
                var prefab = NativeContent.Load<NativeOnlineLobbyView>(NativeOnlineLobbyView.ResourcePath);
                if (!prefab) throw new InvalidOperationException("OnlineLobby.prefab resource missing");
                onlineLobby = Instantiate(prefab, transform, false);
                onlineLobby.name = "OnlineLobby";
                onlineLobby.Bind(key =>
                {
                    if (OnlineHud.SuppressPointerInput) return;
                    var control = onlineButtons.FirstOrDefault(button => button.key == key);
                    if (control != null) ActivateOnlineButton(control);
                }, key => { if (!OnlineHud.SuppressPointerInput) onlineFocus = key; }, EditInvitationEndpoint);
                onlineFields["invite-address"] = onlineLobby.InvitationInput;
            }
            onlineLobby.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            onlineLobby.Header(RoomDisplayName(Peers.FirstOrDefault(p => p.host)?.name ?? PlayerName));
            for (int seat = 0; seat < 8; seat++)
            {
                var peer = Peers.FirstOrDefault(p => p.seat == seat && !p.spectator);
                var choice = Config.selections.FirstOrDefault(c => c.seat == seat);
                var icon = peer != null && choice != null ? Race.vehicleRoster.CharacterIconAt(Mathf.Max(0, Race.vehicleRoster.CharacterIndexOf(choice.character))) : null;
                var preview = peer != null && choice != null ? Race.NetworkRosterVehiclePreview(choice.character, choice.vehicle, choice.skin) : null;
                string status = peer == null ? null : !peer.connected ? "Reconnecting" : !peer.returned ? "Viewing results" : peer.ready ? "Ready!" : "Choosing";
                onlineLobby.Row(seat, peer?.name, status, icon, preview, seat == LocalSeat, peer != null && peer.ready);
            }
            onlineLobby.Model(requestedSpectator ? null : Race.NetworkSelectionPreview(selection.character, selection.vehicle, selection.skin), !requestedSpectator);
            if (!requestedSpectator)
                LobbyButton("character", onlineLobby.CharacterButton, "", () => { if (readySent) SetReady(false); BeginOnlineChoice(OnlineChoice.Characters); });
            string endpoint = RoomInvitationEndpoint();
            bool editable = IsHost && !serverConnection;
            onlineLobby.Invitation(serverConnection ? "Room Server" : IsHost ? "Invite Address" : "Connected to", editable ? invitationDraft ?? endpoint : endpoint,
                editable, onlineFocus == "field-invite-address");
            if (editable)
            {
                var field = onlineLobby.InvitationInput;
                onlineVisibleFields.Add("invite-address");
                onlineButtons.Add(new OnlineControl { key = "field-invite-address", rect = NativeOnlineLobbyView.LogicalRect((RectTransform)field.transform), enabled = true,
                    action = () => { field.Select(); field.ActivateInputField(); } });
            }
            if (onlineLobby.SettingsButton && serverConnection) LobbyButton("room-settings", onlineLobby.SettingsButton, "Settings", OpenRoomServerSettings);
            if (onlineLobby.SettingsButton) onlineLobby.SettingsButton.gameObject.SetActive(serverConnection);
            LobbyButton("copy-invite", onlineLobby.CopyButton, "Copy", () => { GUIUtility.systemCopyBuffer = RoomInvitationEndpoint(); Status = "Invite copied"; }, !string.IsNullOrWhiteSpace(endpoint));
            LobbyButton("role", onlineLobby.SpectateButton, requestedSpectator ? "Join Race" : "Spectate", () => SetRoomRole(!requestedSpectator));
            LobbyButton("ready", onlineLobby.ReadyButton, requestedSpectator ? IsHost ? "Race Setup" : "Spectating" : readySent ? "Unready" : "Ready", () =>
            {
                if (requestedSpectator) { OpenRaceSetup(); return; }
                SetReady(!readySent); if (!readySent) BeginOnlineChoice(OnlineChoice.Characters);
            }, !requestedSpectator || (IsHost && setupActive && RoomReadyToStart));
            onlineLobby.Quit(lobbyExitHeld, lobbyExitHold.FillAmount);
        }
        void LobbyButton(string key, NativeLobbyButton view, string caption, Action action, bool enabled = true)
        {
            onlineButtons.Add(new OnlineControl { key = key, rect = NativeOnlineLobbyView.LogicalRect(view.Rect), enabled = enabled, action = action });
            if (onlineFocus == null && enabled) onlineFocus = key;
            view.Present(caption, onlineFocus == key, enabled);
        }
        void RenderOnlineLoading()
        {
            OnlineHud.NetworkPanel("OnlineLoading.Panel1");
            OnlineHud.NetworkSprite("OnlineLoading.Sprite1", OnlineHud.TrackBannerAt(Mathf.Max(0, NativeTrackCatalog.IndexOf(Config.track))));
            OnlineHud.NetworkLabel("OnlineLoading.Label1", "Getting everyone on the track...");
            OnlineHud.NetworkLabel("OnlineLoading.Label2", IsHost && loaded.Count>0 ? string.Format(NativeLocalization.TranslateText("Waiting for {0} to load"),string.Join(", ",Participants.Where(p=>!loaded.Contains(p.seat)).Select(p=>p.name))) : "Race starts when every player has loaded.");
            OnlineButton("leave", "Leave Room", LeaveToOnlineMenu);
        }
        void RenderOnlineRaceMenu(bool complete)
        {
            if (complete)
            {
                OnlineHud.NetworkPanel("OnlineRaceMenu.Panel1");
                var results = Latest.karts.OrderBy(k => k.retired ? 2 : k.finished ? 0 : 1).ThenBy(k => k.place).ToArray();
                OnlineHud.NetworkLabel("OnlineRaceMenu.Label1", "Place");
                OnlineHud.NetworkLabel("OnlineRaceMenu.Label2", "Racer");
                OnlineHud.NetworkLabel("OnlineRaceMenu.Label3", "Time");
                for (int i = 0; i < results.Length; i++)
                {
                    var kart = results[i];
                    OnlineHud.BeginOnlineRow("Results", i);
                    OnlineHud.NetworkRow("OnlineRaceMenu.Row1", kart.seat==LocalRacerIndex);
                    var choice = (roundConfig ?? Config).selections.FirstOrDefault(s => s.seat == (roundConfig ?? Config).PeerSeat(kart.seat));
                    int ci = choice == null ? kart.seat : Race.vehicleRoster.CharacterIndexOf(choice.character);
                    OnlineHud.NetworkLabel("OnlineRaceMenu.Label4", kart.retired ? "—" : kart.finished ? kart.place.ToString() : "—");
                    OnlineHud.NetworkSprite("OnlineRaceMenu.Sprite1", Race.vehicleRoster.CharacterIconAt(Mathf.Max(0, ci)));
                    var peer = Peers.FirstOrDefault(p => !p.spectator && p.seat == PeerSeat(kart.seat));
                    OnlineHud.NetworkLabel("OnlineRaceMenu.Label5", roundNames.TryGetValue(kart.seat,out var raceName)?raceName:peer?.name ?? Race.racerNames.ElementAtOrDefault(kart.seat) ?? "Player", localize: peer == null);
                    OnlineHud.NetworkLabel("OnlineRaceMenu.Label6", kart.retired ? "DNF" : kart.finished ? TimeSpan.FromSeconds(kart.finish).ToString(@"mm\:ss\.fff") : "Racing...");
                    OnlineHud.EndOnlineRow();
                }
                OnlineHud.NetworkLabel("OnlineRaceMenu.Label7", ResultsStatusText());
                if (CanReturnToRoom)
                {
                    if(onlineFocus==null)onlineFocus="room";
                    var rect = OnlineHud.OnlineElementRect("OnlineRaceMenu.Prompt1");
                    onlineButtons.Add(new OnlineControl { key = "room", rect = rect, enabled = true, action = ReturnToRoom });
                    if (OnlineHud.NetworkPrompt("OnlineRaceMenu.Prompt1", OnlineHud.keyConfirm, "Return to Room")) ReturnToRoom();
                }
                return;
            }

            RenderOnlinePausedMenu();
        }
        void BeginOnlineChoice(OnlineChoice choice)
        {
            onlineChoiceEntry = choice;
            draftCharacter = Mathf.Max(0, Race.vehicleRoster.CharacterIndexOf(selection.character));
            draftVehicle = Mathf.Max(0, Race.vehicleRoster.VehicleIndexOf(selection.vehicle)); draftSkin = selection.skin;
            SetOnlineChoice(choice);
        }
        void SetOnlineChoice(OnlineChoice choice)
        {
            CloseOnlineCharacterProfile();
            onlineChoice = choice; lastCandidate=-1; onlineHeldDirection = Vector2.zero; onlineNextNavigate = 0;
            OnlineHud.ConfigureChoiceCounts(Race.vehicleRoster.SelectableCharacters().Length, Race.vehicleRoster.VehicleCount);
            OnlineHud.ConfigureSkinChoices(Race.vehicleRoster.SkinCountAt(draftCharacter));
            var page = choice == OnlineChoice.Characters ? NativeMenuPage.Characters : choice == OnlineChoice.Vehicles ? NativeMenuPage.Vehicles
                : choice == OnlineChoice.Skins ? NativeMenuPage.Skins : choice == OnlineChoice.Tracks ? NativeMenuPage.Tracks
                : choice == OnlineChoice.RaceModes ? NativeMenuPage.RaceModes : choice == OnlineChoice.Cups ? NativeMenuPage.Cups : choice == OnlineChoice.Custom ? NativeMenuPage.Custom : NativeMenuPage.Online;
            OnlineHud.SetMenuPage(page);
            int index = choice == OnlineChoice.Characters ? Array.IndexOf(Race.vehicleRoster.SelectableCharacters(), draftCharacter)
                : choice == OnlineChoice.Vehicles ? draftVehicle : choice == OnlineChoice.Skins ? draftSkin : Mathf.Max(0, NativeTrackCatalog.IndexOf(Config.track));
            OnlineHud.SelectMenuIndex(choice == OnlineChoice.RaceModes ? Config.mode == NativeSessionMode.GrandPrix ? 0 : Config.mode == NativeSessionMode.CustomRace ? 2 : 1 : choice == OnlineChoice.Cups || choice == OnlineChoice.Custom ? 0 : Mathf.Max(0, index));
            if (choice == OnlineChoice.Cups) OnlineHud.LoadOriginalCupRecords();
            if (choice == OnlineChoice.Custom) { OnlineHud.ResetCustomizationFocus(); CloseOnlineCustomDropdown(); }
        }
        void CompleteOnlineChoice()
        {
            if (IsSetupChoice) { if (onlineCustomDropdown) { SetOnlineCustomValue(onlineCustomRow, onlineCustomOption); CloseOnlineCustomDropdown(); } else HandleOnlineSetupAction(OnlineHud.ConfirmMenu(OnlineHud.MenuPage)); return; }
            if (onlineChoice == OnlineChoice.Characters) { draftCharacter = Race.vehicleRoster.SelectableCharacters()[OnlineHud.SelectedMenuIndex]; draftSkin = 0; SetOnlineChoice(OnlineChoice.Vehicles); }
            else if (onlineChoice == OnlineChoice.Vehicles) { draftVehicle = OnlineHud.SelectedMenuIndex; SetOnlineChoice(OnlineChoice.Skins); }
            else if (onlineChoice == OnlineChoice.Skins)
            {
                selection.character = Race.vehicleRoster.CharacterIdAt(draftCharacter); selection.vehicle = Race.vehicleRoster.VehicleIdAt(draftVehicle); selection.skin = OnlineHud.SelectedMenuIndex;
                Send("selection", JsonUtility.ToJson(selection)); SetOnlineChoice(OnlineChoice.None);
            }
            else if (onlineChoice == OnlineChoice.Tracks) { Config.track = NativeTrackCatalog.IdAt(OnlineHud.SelectedMenuIndex); BroadcastConfig(); Send("candidate",null); SetOnlineChoice(OnlineChoice.None); setupPage=true;onlineFocus="track"; }
        }
        void CancelOnlineChoice()
        {
            if (IsSetupChoice) { CancelOnlineSetup(); OnlineHud.NetworkBackSound(); return; }
            if (onlineChoice == OnlineChoice.Skins) SetOnlineChoice(OnlineChoice.Vehicles);
            else if (onlineChoice == OnlineChoice.Vehicles) SetOnlineChoice(onlineChoiceEntry == OnlineChoice.Vehicles ? OnlineChoice.None : OnlineChoice.Characters);
            else {if(onlineChoice==OnlineChoice.Tracks)Send("candidate",null);SetOnlineChoice(OnlineChoice.None);}
            OnlineHud.NetworkBackSound();
        }
        void RenderOnlineChoice()
        {
            OnlineHud.NetworkVisible(false); onlineButtons.Clear(); onlinePage = onlineChoice.ToString();
            float width = OnlineHud.BeginFrame();
            bool additionalPlayers = OnlineHud.AdditionalPlayersCanJoin;
            bool pointerSuppressed = OnlineHud.SuppressPointerInput;
            OnlineHud.AdditionalPlayersCanJoin = false;
            if (onlineCharacterProfile.Open) OnlineHud.SuppressPointerInput = true;
            try
            {
                NativeMenuAction action = NativeMenuAction.None;
                if (onlineChoice == OnlineChoice.RaceModes) action = OnlineHud.RaceModeMenu(width);
                else if (onlineChoice == OnlineChoice.Cups) action = OnlineHud.AuthoredCup(width);
                else if (onlineChoice == OnlineChoice.Custom) action = RenderOnlineCustom(width);
                else if (onlineChoice == OnlineChoice.Characters) action = OnlineHud.CharacterMenu(width, Race.vehicleRoster, Race.vehicleRoster.SelectableCharacters());
                else if (onlineChoice == OnlineChoice.Tracks) action = OnlineHud.TrackMenu(width);
                else
                {
                    OnlineHud.VehicleCharacterSelection = draftCharacter; OnlineHud.VehicleSelection = draftVehicle;
                    OnlineHud.ChoosingSkin = onlineChoice == OnlineChoice.Skins;
                    action = OnlineHud.VehicleMenu(width, Race.vehicleRoster);
                }
                OnlineHud.NetworkSelectionContext();
                if (!IsSetupChoice)
                {
                    int ci = onlineChoice == OnlineChoice.Characters ? Race.vehicleRoster.SelectableCharacters()[OnlineHud.SelectedMenuIndex] : draftCharacter;
                    int vi = onlineChoice == OnlineChoice.Vehicles ? OnlineHud.SelectedMenuIndex : draftVehicle;
                    int si = onlineChoice == OnlineChoice.Characters ? 0 : onlineChoice == OnlineChoice.Skins ? OnlineHud.SelectedMenuIndex : draftSkin;
                    OnlineHud.PreviewPicture(OnlineHud.PreviewRect, Race.NetworkSelectionPreview(Race.vehicleRoster.CharacterIdAt(ci), Race.vehicleRoster.VehicleIdAt(vi), si));
                }
                if (OnlineCharacterProfilePage && onlineCharacterProfile.Open) OnlineHud.DrawCharacterProfile(onlineCharacterProfile);
                else HandleOnlineChoicePointerAction(action);
            }
            finally { OnlineHud.AdditionalPlayersCanJoin = additionalPlayers; OnlineHud.SuppressPointerInput = pointerSuppressed; OnlineHud.EndFrame(); }
            HideUnusedOnlineFields();
        }
        void HandleOnlineChoicePointerAction(NativeMenuAction action)
        {
            if (action == NativeMenuAction.None) return;
            if (IsSetupChoice) { HandleOnlineSetupAction(action); return; }
            switch (action)
            {
                case NativeMenuAction.OpenVehicles:
                case NativeMenuAction.OpenSkins:
                case NativeMenuAction.OpenCups:
                case NativeMenuAction.ConfirmCircuit:
                    CompleteOnlineChoice(); break;
                case NativeMenuAction.BackToMain:
                case NativeMenuAction.BackToCharacters:
                case NativeMenuAction.BackToVehicles:
                case NativeMenuAction.BackToCup:
                    CancelOnlineChoice(); break;
            }
        }
        void TickNetworkUiInput()
        {
            if (!Race || (!Open && !ProfileFromSettings) || OnlineHud.MenuTransitionActive) return;
            if (SpectatorControlsActive) { TickSpectatorInput(); return; }
            if (OnlineEditing && NativeTextInputBridge.BlocksMenuInput)
            { onlineInputConsumedFrame = Time.frameCount; return; }
            bool escape = Input.GetKeyDown(KeyCode.Escape) || (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame);
            if (HandleOnlineCharacterProfileInput(escape)) { onlineInputConsumedFrame = Time.frameCount; return; }
            if (HandleProfileNameInput(escape)) return;
            bool cancel = escape || (!OnlineEditing && !profilesPage && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Cancel))
                || NativeInputBindings.GetGamepadButtonDown(NativeInputBindings.Action.Cancel);
            if (connectionStep == OnlineConnectionStep.JoinBrowser && !settingsPage && OnlineEditing &&
                !cancel && (OnlineKeyboardSubmit || NativeInputBindings.GetGamepadButtonDown(NativeInputBindings.Action.Ok)))
            {
                EndOnlineTextEdit(); onlineInputConsumedFrame = Time.frameCount;
                ConnectBrowserIp(); return;
            }
            if (OnlineEditing)
            {
                onlineInputConsumedFrame = Time.frameCount;
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (Input.GetKeyDown(KeyCode.Tab) || (keyboard != null && keyboard.tabKey.wasPressedThisFrame))
                {
                    var fields = onlineFields.Values.Where(f => f && f.gameObject.activeInHierarchy).ToArray(); int index = Array.FindIndex(fields, f => f.isFocused);
                    if (fields.Length > 0)
                    {
                        int step = keyboard != null && keyboard.shiftKey.isPressed ? fields.Length - 1 : 1;
                        var next = fields[(Mathf.Max(0, index) + step) % fields.Length]; next.Select(); next.ActivateInputField();
                    }
                }
                if (cancel || OnlineKeyboardSubmit || NativeInputBindings.GetGamepadButtonDown(NativeInputBindings.Action.Ok)) EndOnlineTextEdit();
                return;
            }
            if(CanHoldLobbyExit && TickLobbyExitHold(cancel))return;
            if (!confirmationOpen && !requestedSpectator && LocalFinished)
            {
                if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok) || Input.GetKeyDown(KeyCode.Return)) HandlePersonalResultsConfirm();
                if (cancel) onlineInputConsumedFrame = Time.frameCount;
                return;
            }
            if (InRace && !requestedSpectator && !PersonalResultsVisible && !confirmationOpen)
            {
                if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Pause))
                { paused=!paused;onlineInputConsumedFrame=Time.frameCount;return; }
                // Face-button Cancel belongs to menus, never opens the race pause menu.
                if(!paused)return;
            }
            if (cancel)
            {
                onlineInputConsumedFrame = Time.frameCount;
                if(!confirmationOpen&&!IsHost&&candidateTrack!=null&&watchTrackPreview){watchTrackPreview=false;onlineFocus="preview";}
                else if (onlineChoice != OnlineChoice.None) CancelOnlineChoice();
                else if (confirmationOpen)DismissOnlineConfirmation();
                else if(settingsPage) CloseRoomServerSettings(false);
                else if(profilesPage) CloseOnlineProfile();
                else if(setupPage)ReturnFromRaceSetup();
                else if (InRace && !OnlineComplete) paused = !paused;
                else if (playerId != null) ConfirmOnlineAction(LeaveToOnlineMenu,IsHost?"Close Room":"Leave Room");
                else if (connectionStep == OnlineConnectionStep.JoinBrowser) CancelBrowserConnection();
                else if (connectionStep != OnlineConnectionStep.Menu) CancelConnectionStep();
                else CloseMenu();
                return;
            }
            if (!confirmationOpen&&!IsHost&&candidateTrack!=null&&watchTrackPreview)return;
            if (InRace && !requestedSpectator && !PersonalResultsVisible && !paused) return;
            Vector2 direction = NativeInputBindings.GetVector();
            if (direction.sqrMagnitude < .2f) { onlineHeldDirection = Vector2.zero; return; }
            bool move = onlineHeldDirection == Vector2.zero || Time.unscaledTime >= onlineNextNavigate;
            if (move)
            {
                onlineNextNavigate = Time.unscaledTime + (onlineHeldDirection == Vector2.zero ? .3f : .12f); onlineHeldDirection = direction;
                if (onlineChoice != OnlineChoice.None)
                {
                    if (onlineChoice == OnlineChoice.Custom) NavigateOnlineCustom(direction);
                    else if (onlineChoice == OnlineChoice.Characters || onlineChoice == OnlineChoice.Tracks)
                        OnlineHud.NavigateOriginalChoice(OnlineHud.MenuPage, direction, Race.vehicleRoster, Race.vehicleRoster.SelectableCharacters());
                    else OnlineHud.NavigateMenu(OnlineHud.MenuPage, Mathf.Abs(direction.x) > .5f ? direction.x > 0 ? 1 : -1 : direction.y < 0 ? 1 : -1);
                }
                else if(setupPage&&Mathf.Abs(direction.x)>.5f&&(onlineFocus=="laps"||onlineFocus=="racers"))ChangeNumber(onlineFocus,direction.x>0?1:-1);
                else MoveOnlineFocus(direction);
            }
        }
        void LateUpdate()
        {
            if (!Race || (!Open && !ProfileFromSettings) || (!confirmationOpen&&!IsHost&&candidateTrack!=null&&watchTrackPreview) || OnlineHud.MenuTransitionActive || OnlineEditing || onlineInputConsumedFrame == Time.frameCount || (InRace && !requestedSpectator && !PersonalResultsVisible && !paused)) return;
            if (SpectatorControlsActive) return;
            if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok) || Input.GetKeyDown(KeyCode.Return))
            {
                if (requestedSpectator && OnlineComplete && !PersonalResultsVisible)HandlePersonalResultsConfirm();
                else if (onlineChoice != OnlineChoice.None) { OnlineHud.NetworkSound(true); CompleteOnlineChoice(); }
                else { var button = onlineButtons.FirstOrDefault(b => b.key == onlineFocus && b.enabled); if (button != null) ActivateOnlineButton(button); }
            }
            Vector2 pointer = Input.mousePosition;
            if (Application.isFocused && !OnlineHud.SuppressPointerInput && onlinePointerInitialized && (pointer - onlineLastPointer).sqrMagnitude > 1)
            {
                Vector2 point = new Vector2(pointer.x, Screen.height - pointer.y) * (600f / Screen.height);
                var button = onlineButtons.FirstOrDefault(b => b.enabled && b.rect.Contains(point) && BrowserPointerAllowed(b, point)); if (button != null) onlineFocus = button.key;
            }
            onlinePointerInitialized = true; onlineLastPointer = pointer;
        }
        void MoveOnlineFocus(Vector2 direction)
        {
            if (TryMoveBrowserRoomFocus(direction)) return;
            if ((OnlinePage == "Online" || profilesPage) && Mathf.Abs(direction.y)>=Mathf.Abs(direction.x) && IsOnlineWheelKey(onlineFocus))
            {
                var rows = onlineButtons.Where(b => b.enabled && IsOnlineWheelKey(b.key)).ToArray();
                if (rows.Length == 0 || Mathf.Abs(direction.y) < Mathf.Abs(direction.x)) return;
                int index = Array.FindIndex(rows, b => b.key == onlineFocus);
                onlineFocus = rows[(Mathf.Max(0, index) + (direction.y < 0 ? 1 : rows.Length - 1)) % rows.Length].key;
                OnlineHud.NetworkSound(false);
                return;
            }
            var current = onlineButtons.FirstOrDefault(b => b.key == onlineFocus && b.enabled) ?? onlineButtons.FirstOrDefault(b => b.enabled);
            if (current == null) return;
            Vector2 axis = Mathf.Abs(direction.x) > Mathf.Abs(direction.y) ? new Vector2(Mathf.Sign(direction.x), 0) : new Vector2(0, -Mathf.Sign(direction.y));
            var next = onlineButtons.Where(b => b.enabled && b != current && Vector2.Dot(b.rect.center - current.rect.center, axis) > 1)
                .OrderBy(b => Vector2.Distance(b.rect.center, current.rect.center) + Mathf.Abs(Vector2.Dot(b.rect.center - current.rect.center, new Vector2(-axis.y, axis.x))) * 3).FirstOrDefault();
            if (next != null) { onlineFocus = next.key; OnlineHud.NetworkSound(false); }
        }
        void HideUnusedOnlineFields()
        {
            foreach(var pair in onlineFields.ToArray())
            {
                if(!pair.Value){onlineFields.Remove(pair.Key);boundOnlineFields.Remove(pair.Value);continue;}
                if(pair.Value.gameObject.activeSelf && !onlineVisibleFields.Contains(pair.Key))pair.Value.gameObject.SetActive(false);
            }
        }
        void EnsureOnlineFields()
        {
            onlineInputRoot = OnlineHud.OnlinePageView ? OnlineHud.OnlinePageView.gameObject : null;
            EnsureOnlineEventSystem();
        }
        void EnsureOnlineEventSystem() => NativeOnlinePointer.EnsureEventSystem();
        bool OnlineKeyboardUnmodified
        {
            get
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                return !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                    || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                    || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                    || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                    || (keyboard != null && (keyboard.shiftKey.isPressed || keyboard.ctrlKey.isPressed
                        || keyboard.altKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed)));
            }
        }
        bool OnlineKeyboardSpacePressed => Input.GetKeyDown(KeyCode.Space)
            || (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame);
        bool OnlineKeyboardSpace => OnlineKeyboardUnmodified && OnlineKeyboardSpacePressed;
        bool OnlineKeyboardEnterPressed => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
            || (UnityEngine.InputSystem.Keyboard.current != null && (UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame
                || UnityEngine.InputSystem.Keyboard.current.numpadEnterKey.wasPressedThisFrame));
        bool OnlineKeyboardSubmit => OnlineKeyboardUnmodified && OnlineKeyboardEnterPressed;
        void EndOnlineTextEdit()
        {
            foreach (var field in onlineFields.Values) if (field) field.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
        }
        void OnlineField(string key, string value, Action<string> changed)
        {
            var element = OnlineHud.OnlineInput(key);
            if (!element || !element.input) return;
            var field = element.input;
            if (boundOnlineFields.Add(field))
            {
                field.onValueChanged.AddListener(v => changed(v));
                field.onEndEdit.AddListener(_ =>
                { onlineTextEndFrame = Time.frameCount; NativeTextInputBridge.KeepEditingAfterIme(field); });
                if (key == "player-name")
                {
                    field.characterLimit = 0; field.richText = false; field.isRichTextEditingAllowed = false;
                    field.restoreOriginalTextOnEscape = false; field.onFocusSelectAll = false;
                    // Only the actual exit shortcut is suppressed here. Invalid paste is kept and explained, never silently rewritten.
                    field.onValidateInput = (text, index, character) => character == ' ' && !NativeTextInputBridge.BlocksMenuInput
                        && OnlineKeyboardSpacePressed ? '\0' : character;
                    field.onSelect.AddListener(_ =>
                    {
                        if (!NativeTextInputBridge.Restoring(field)) profileNameBeforeEdit = profileNameDraft ?? PlayerName;
                        onlineFocus = "field-player-name";
                    });
                }
            }
            onlineFields[key] = field;
            onlineVisibleFields.Add(key);
            var rect = OnlineHud.OnlineElementRect("field-" + key);
            if (!onlineButtons.Any(b => b.key == "field-" + key) && rect.width > 0)
                onlineButtons.Add(new OnlineControl { key = "field-" + key, rect = rect, enabled = true,
                    action = () => { field.Select(); field.ActivateInputField(); } });
            if (!field.isFocused && field.text != value) field.SetTextWithoutNotify(value);
        }
        [Serializable] sealed class OnlineFields { public string address, port, room; }
        [Serializable] sealed class OnlineUiProbe
        {
            public string page, focus, address, port, room, name, profileId, nameSource, status, selectionCharacter, selectionVehicle, editing;
            public NativeRaceHud.MenuFocusSnapshot menuFocus;
            public string[] lobbyNames;
            public int skin, fields, buttons, nativeTextCount; public bool nativeFonts, inputCanvasVisible, raceHudVisible;
            public string[] actions, activeFields; public bool[] enabled;
            public bool resultsVisible,returnEnabled,spectator,setup,allReady,finishCameraActive,automaticCamera;public float resultsAlpha,manualFocusRemaining;
            public int observedRacer,spectatorCameraMode,directorCuts,directorFocus;public string directorStory;
            public bool broadcastActive,broadcastFallback; public int broadcastFocus,broadcastOther,broadcastCuts;
            public string broadcastShot,broadcastProfile; public float broadcastCoverage;
        }
        void WriteOnlineUiProbe()
        {
            if (string.IsNullOrEmpty(reportDir)) return;
            var texts = FindObjectsOfType<TMP_Text>().Where(t => t.GetComponentInParent<NativeOnlinePageView>() != null || (onlineLobby && t.transform.IsChildOf(onlineLobby.transform)) || (onlineInputRoot && t.transform.IsChildOf(onlineInputRoot.transform))).ToArray();
            var probe = new OnlineUiProbe { page = OnlinePage, focus = onlineFocus, address = Address, port = Port, room = Room, name = PlayerName,
                profileId = NativeInputBindings.SelectedProfile(), nameSource = playerNameOverride != null ? "command-line" : !string.IsNullOrWhiteSpace(NativeUserSettings.ReadValue(OnlineNameSetting)) ? "saved-player-name" : "profile",
                status = Status,finishCameraActive=Race && Race.FinishCamera && Race.FinishCamera.Ended,resultsVisible=PersonalResultsVisible,returnEnabled=CanReturnToRoom,spectator=requestedSpectator,setup=setupActive,allReady=RoomReadyToStart,resultsAlpha=PersonalResultsAlpha,
                lobbyNames = Peers.Select(p => p.name).ToArray(),
                observedRacer = observedRacer, automaticCamera = automaticSpectator, manualFocusRemaining = ManualFocusRemaining,
                spectatorCameraMode = spectatorCameraMode, directorStory = spectatorDirector.Current?.kind.ToString(), directorCuts = spectatorDirector.CutCount,
                directorFocus = spectatorDirector.Current?.focus ?? -1,
                broadcastActive = Race.BroadcastCameraActive, broadcastFallback = Race.BroadcastCameraFallback,
                broadcastFocus = Race.BroadcastShot?.focus ?? -1, broadcastOther = Race.BroadcastShot?.other ?? -1,
                broadcastShot = Race.BroadcastShot?.kind.ToString(), broadcastProfile = Race.BroadcastProfile,
                broadcastCuts = Race.BroadcastShotCuts, broadcastCoverage = Race.BroadcastShot?.coverage ?? 0,
                menuFocus = OnlinePage == "Online" ? OnlineHud.NetworkMenuFocus : null,
                selectionCharacter = selection.character, selectionVehicle = selection.vehicle, skin = selection.skin,
                fields = onlineFields.Count, buttons = onlineButtons.Count, nativeTextCount = texts.Length,
                editing = onlineFields.FirstOrDefault(pair => pair.Value.isFocused).Key,
                nativeFonts = texts.Length > 0 && texts.All(t => (t.GetComponent<NativeLobbyTypography>() ? t.GetComponent<NativeLobbyTypography>().UsesAuthoredFont : t.font == (NativeLocalization.IsChinese ? NativeChineseFont.Font : OnlineHud.menuFont.GetFont()))), inputCanvasVisible = (onlineInputRoot && onlineInputRoot.activeInHierarchy) || (onlineLobby && onlineLobby.InvitationInput.gameObject.activeInHierarchy),
                raceHudVisible = FindObjectsOfType<Canvas>(true).Any(c => c.name == "Native original UI Canvas" && c.gameObject.activeInHierarchy),
                activeFields = onlineFields.Where(pair => pair.Value && pair.Value.gameObject.activeInHierarchy).Select(pair => pair.Key).ToArray(),
                actions = onlineButtons.Select(b => b.key).ToArray(), enabled = onlineButtons.Select(b => b.enabled).ToArray() };
            System.IO.File.WriteAllText(System.IO.Path.Combine(reportDir, "net-ui.json"), JsonUtility.ToJson(probe, true));
        }
        void RunNetworkUiCommand(string command)
        {
            if (command == "open") Race.NetworkUiOpenFromMain();
            else if (command.StartsWith("spectator ")) RunSpectatorAction(command.Substring(10));
            else if (command == "probe") WriteOnlineUiProbe();
            else if (command == "navigate up") MoveOnlineFocus(Vector2.up);
            else if (command == "navigate down") MoveOnlineFocus(Vector2.down);
            else if (command.StartsWith("focus ")) { var field = onlineFields[command.Substring(6)]; field.Select(); field.ActivateInputField(); }
            else if (command == "confirm") CompleteOnlineChoice();
            else if (command == "cancel") { if (onlineChoice != OnlineChoice.None) CancelOnlineChoice(); else if (playerId != null) LeaveToOnlineMenu(); else if (connectionStep == OnlineConnectionStep.JoinBrowser) CancelBrowserConnection();
                else if (connectionStep != OnlineConnectionStep.Menu) CancelConnectionStep(); else CloseMenu(); }
            else if (command.StartsWith("select ")) OnlineHud.SelectMenuIndex(int.Parse(command.Substring(7)));
            else if (command.StartsWith("fields "))
            {
                var data = JsonUtility.FromJson<OnlineFields>(command.Substring(7));
                foreach (var pair in new[] { ("address", data.address), ("port", data.port), ("room", data.room) })
                    if (pair.Item2 != null && onlineFields.TryGetValue(pair.Item1, out var field)) field.text = pair.Item2;
            }
            else { var button = onlineButtons.FirstOrDefault(b => b.key == command); if (button != null) ActivateOnlineButton(button); }
        }
    }
    public sealed partial class NativeRace
    {
        internal void NetworkUiOpenFromMain()
        {
            hud.SetMenuPage(NativeMenuPage.Main); hud.SelectMenuIndex(2);
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Main));
        }
    }
}
