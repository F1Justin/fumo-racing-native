using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        string roomSettingsFocus, roomSettingsStatus, roomServerDraft, roomPortDraft;
        bool hostAfterServerSettings;
        static string RoomDisplayName(string owner) => string.Format(NativeLocalization.TranslateText("{0}'s Room"), owner);
        bool ValidRoomServer => !string.IsNullOrWhiteSpace(serverAddress) && int.TryParse(serverPort, out var port) && port >= 1 && port <= 65535;
        void OpenRoomServerSettings()
        {
            roomSettingsFocus = onlineFocus; roomSettingsStatus = Status;
            roomServerDraft = serverAddress; roomPortDraft = serverPort;
            Status = ""; settingsPage = true; onlinePage = null; onlineFocus = "field-community-address";
        }
        void CloseRoomServerSettings(bool saved)
        {
            settingsPage = false; onlinePage = null; onlineFocus = roomSettingsFocus; Status = roomSettingsStatus;
            foreach (var field in onlineFields.Values) if (field) field.DeactivateInputField();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            if (saved && hostAfterServerSettings) { hostAfterServerSettings = false; HostAdvertisedRoom(); }
            else
            {
                hostAfterServerSettings = false;
                if (saved && playerId == null && connectionStep == OnlineConnectionStep.JoinBrowser) RefreshRooms();
            }
        }
        void RenderRoomServerSettings()
        {
            OnlineHud.NetworkLabel("OnlineSettings.Label1", "Room Server Settings");
            OnlineHud.NetworkLabel("OnlineSettings.Label2", "Address");
            OnlineHud.NetworkLabel("OnlineSettings.Label3", "Port");
            EnsureOnlineFields();
            OnlineField("community-address", roomServerDraft, value => roomServerDraft = value);
            OnlineField("community-port", roomPortDraft, value => roomPortDraft = value);
            OnlineHud.NetworkLabel("server-setting-help", "Applies to future rooms. Your current room stays connected.");
            OnlineButton("done", "Save Settings", () =>
            {
                if (string.IsNullOrWhiteSpace(roomServerDraft) || !int.TryParse(roomPortDraft, out var port) || port < 1 || port > 65535)
                { Status = "Enter a valid community server"; return; }
                serverAddress = roomServerDraft.Trim(); serverPort = port.ToString(); communityEnabled = true;
                SaveOnlinePreferences(); CloseRoomServerSettings(true);
            });
            ConnectionHint("settings-back", "Back", () => CloseRoomServerSettings(false));
            OnlineHud.NetworkLabel("server-setting-error", ConnectionError());
        }
        void RenderHostChoice()
        {
            OnlineHud.NetworkLabel("host-choice-title", "Host Game");
            OnlineHud.NetworkLabel("host-choice-help", "Choose IP direct or a listed room.");
            OnlineButton("host-ip", "IP Direct", () => BeginConnectionStep(OnlineConnectionStep.HostDirect, "host-ip"));
            OnlineButton("host-room", "Room", HostAdvertisedRoom);
            ConnectionHint("host-back", "Back", CancelConnectionStep);
        }
        void HostAdvertisedRoom()
        {
            if (!ValidRoomServer) { hostAfterServerSettings = true; OpenRoomServerSettings(); return; }
            requestedSpectator = false;
            BeginConnectionStep(OnlineConnectionStep.HostServer, "host-room");
            connectionReturnStep = OnlineConnectionStep.HostChoice;
            ConnectFromMenu("server-create");
        }
        void RenderRoomHost()
        {
            OnlineHud.NetworkLabel("room-host-title", RoomDisplayName(PlayerName), false);
            OnlineHud.NetworkLabel("room-host-server", serverAddress + ":" + serverPort, false);
            OnlineHud.NetworkLabel("room-host-status", Status == "Choose how to play together." ? "Connecting" : Status);
            OnlineButton("room-settings", "Settings", () => { hostAfterServerSettings = true; OpenRoomServerSettings(); });
            ConnectionHint("room-host-back", "Back", CancelConnectionStep);
        }
        void ConnectBrowserIp()
        {
            directAddress = Address; lastJoinPort = Port;
            ConnectFromMenu("join");
        }
        void RenderJoinBrowser()
        {
            bool connecting = transport != null && playerId == null;
            OnlineHud.NetworkPanel("ip-panel"); OnlineHud.NetworkPanel("rooms-panel");
            OnlineHud.NetworkLabel("join-title", "Join Game");
            OnlineHud.NetworkLabel("join-ip-title", "IP Direct");
            OnlineHud.NetworkLabel("join-address-title", "Address");
            OnlineHud.NetworkLabel("join-port-title", "Port");
            EnsureOnlineFields();
            OnlineField("address", Address, value => Address = value);
            OnlineField("port", Port, value => Port = value);
            ConnectionHint("join-ip", connecting ? "Cancel" : "Join IP", () => { if (connecting) CancelBrowserConnection(); else ConnectBrowserIp(); });
            OnlineHud.NetworkLabel("join-rooms-title", "Rooms");
            OnlineHud.NetworkLabel("join-server-address", ValidRoomServer ? serverAddress + ":" + serverPort : "Set a room server in Settings", false);
            OnlineButton("room-settings", "Settings", OpenRoomServerSettings, !connecting);
            OnlineButton("refresh", "Refresh", RefreshRooms, !connecting && ValidRoomServer);
            var visible = availableRooms;
            var rowRects = new System.Collections.Generic.Dictionary<string, RectTransform>();
            var scroll = OnlineHud.OnlinePageView.GetComponentInChildren<NativeOnlineRoomScroll>(true);
            if (visible.Length == 0) OnlineHud.NetworkLabel("join-empty", !ValidRoomServer ? "Set a room server in Settings" : roomListTransport != null && !roomListReceived ? "Finding rooms..." : "No open rooms");
            for (int i = 0; i < visible.Length; i++)
            {
                var room = visible[i];
                OnlineHud.BeginOnlineRow("BrowserRooms", i);
                string key = "room-" + room.id;
                OnlineButton(key, RoomDisplayName(room.name), () => { requestedSpectator = false; Room = room.id; ConnectFromMenu("server-join"); }, !connecting && !room.racing && room.count < room.capacity, "room-select");
                OnlineHud.NetworkLabel("room-count", room.count + " / " + room.capacity + (room.racing ? "  ·  " + NativeLocalization.TranslateText("Racing...") : ""), false);
                rowRects[key] = OnlineHud.OnlineRowRect;
                OnlineHud.EndOnlineRow();
            }
            // A room can disappear while focused; don't leave navigation on a dead identity.
            if (onlineFocus != null && onlineFocus.StartsWith("room-") && onlineFocus != "room-settings" && !onlineButtons.Any(b => b.key == onlineFocus && b.enabled)) onlineFocus = "refresh";
            // The authored ScrollRect retains its position across data refreshes. Only a
            // changed keyboard/controller focus asks it to reveal a row.
            if (scroll)
            {
                scroll.Focus(onlineFocus, rowRects.TryGetValue(onlineFocus ?? "", out var selected) ? selected : null);
                foreach (var control in onlineButtons)
                    if (rowRects.TryGetValue(control.key, out var row)) control.rect = NativeOnlineLobbyView.LogicalRect(row);
            }
            ConnectionHint("join-back", connecting ? "Cancel" : "Back", CancelBrowserConnection);
            OnlineHud.NetworkLabel("join-error", ConnectionError());
        }
        bool BrowserPointerAllowed(OnlineControl control, Vector2 point)
        {
            if (connectionStep != OnlineConnectionStep.JoinBrowser || !control.key.StartsWith("room-") || control.key == "room-settings") return true;
            var scroll = OnlineHud.OnlinePageView?.GetComponentInChildren<NativeOnlineRoomScroll>(true);
            return scroll && NativeOnlineLobbyView.LogicalRect(scroll.Scroll.viewport).Contains(point);
        }
        bool TryMoveBrowserRoomFocus(Vector2 direction)
        {
            if (connectionStep != OnlineConnectionStep.JoinBrowser || settingsPage || confirmationOpen || Mathf.Abs(direction.x) > Mathf.Abs(direction.y)) return false;
            var rows = onlineButtons.Where(b => b.enabled && availableRooms.Any(r => b.key == "room-" + r.id)).ToArray();
            int index = Array.FindIndex(rows, b => b.key == onlineFocus);
            if (index < 0) return false;
            int next = index + (direction.y < 0 ? 1 : -1);
            if (next < 0 || next >= rows.Length) return false;
            onlineFocus = rows[next].key; OnlineHud.NetworkSound(false); return true;
        }
        void CancelBrowserConnection()
        {
            if (transport == null) { CancelConnectionStep(); return; }
            Leave(false); Open = true;
            BeginConnectionStep(OnlineConnectionStep.JoinBrowser, "join-direct");
        }
        // Plain authored hints have no Button component or pointer target. They may carry
        // a logical keyboard/controller action in the navigation list.
        void ConnectionHint(string key, string caption, Action action, bool enabled = true)
        {
            OnlineHud.NetworkLabel(key, caption);
            OnlineHud.OnlinePageView?.Use(key)?.label?.StateColor(onlineFocus == key ? new Color(1, .88f, .58f) : (Color?)null);
            var rect = OnlineHud.OnlineElementRect(key);
            if (rect.width > 0 && rect.height > 0) onlineButtons.Add(new OnlineControl { key = key, rect = rect, enabled = enabled, action = action });
        }
        string ConnectionError()
        {
            if (Status == "Choose how to play together." || Status == "Available rooms" || Status == "Finding rooms..." || Status == "Server room list" || Status == "Set a room server in Settings") return "";
            return Status;
        }
    }
}
