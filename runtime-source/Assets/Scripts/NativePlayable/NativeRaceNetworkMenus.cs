using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        internal bool requestedSpectator;
        internal NativeNetPeer[] Participants => Peers.Where(p=>!p.spectator).ToArray();
        internal readonly HashSet<int> retiredSeats = new HashSet<int>();
        bool communityEnabled, setupActive, setupPage, advancedConnection, profilesPage;
        bool settingsPage, confirmationOpen, participationRequested;
        readonly NativeMainQuitSourceState lobbyExitHold=new NativeMainQuitSourceState();
        bool lobbyExitHeld,automaticSetupRequested;
        bool CanHoldLobbyExit=>OnlinePage=="Lobby"&&!OnlineEditing&&!(candidateTrack!=null&&!IsHost&&watchTrackPreview);
        bool TickLobbyExitHold(bool cancel)
        {
            bool held=NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Cancel)||Input.GetKey(KeyCode.X)||Input.GetKey(KeyCode.Escape);
            if(held&&!lobbyExitHeld)lobbyExitHold.Press();
            if(!held&&lobbyExitHeld)lobbyExitHold.Release();
            lobbyExitHeld=held;
            if(held){lobbyExitHold.Update(Time.unscaledDeltaTime,LeaveToOnlineMenu);onlineInputConsumedFrame=Time.frameCount;}
            return held||cancel;
        }

        string invitationDraft;
        string lastJoinPort="7777",candidateTrack, invitationAddress = "", invitationPort = "7777", confirmationFocus, confirmationStatus;
        Action confirmationAction;
        int observedRacer, lastCandidate = -1;
        bool automaticSpectator = NativeReleaseFeatures.BroadcastEnabled,watchTrackPreview=true,networkCountdownPending;
        NativeNetTransport roomListTransport;
        float roomListStarted;
        string RoomWaitReason()
        {
            if(Recovering)return "Reconnecting";
            if(Participants.Length<2)return "Waiting for players";
            var waiting=Participants.Where(p=>!p.connected||!p.returned||!p.ready).ToArray();
            if(waiting.Length==0)return setupActive?"Waiting for host to start":"Ready for race setup";
            var peer=waiting[0];
            return string.Format(NativeLocalization.TranslateText(!peer.connected?"Waiting for {0} to reconnect":!peer.returned?"Waiting for {0} to return":"Waiting for {0} to ready"),peer.name);
        }
        void OpenRaceSetup()
        {
            if(!IsHost || (!setupActive&&!RoomReadyToStart))return;
            setupPage=true; onlinePage=null; onlineFocus=null; SetOnlineChoice(Config.mode == NativeSessionMode.GrandPrix && Config.gpStation > 0 && Config.gpStation < NativeModeCatalog.CupTrackIds.Length ? OnlineChoice.Cups : OnlineChoice.RaceModes);
            if(!setupActive)Send("setup");
        }
        void ReturnFromRaceSetup()
        {
            if(IsHost&&!requestedSpectator)SetReady(false);
            CloseOnlineCustomDropdown(); SetOnlineChoice(OnlineChoice.None); Send("candidate",null); setupPage=false;onlineFocus="ready";
        }
        void ChangeNumber(string key,int delta)
        {
            if(!IsHost||!setupPage)return;
            if(key=="laps")Config.laps=Mathf.Clamp(Config.laps+delta,1,99);
            if(key=="racers")Config.racers=Mathf.Clamp(Config.racers+delta,MinimumSetupRacers,100);
            BroadcastConfig();
        }
        void SetRoomRole(bool spectator)
        {
            if(InRace)
            {
                if(!requestedSpectator && spectator)ConfirmOnlineAction(()=>Send("retire"),"Retire and Spectate");
                else if(requestedSpectator&&!spectator){participationRequested=true;Send("next-race");}
                return;
            }
            if(transport==null||Recovering)return;
            transport.Send(new NativeNetMessage{kind="role",room=Room,player=playerId,seat=LocalSeat,round=Round,spectator=spectator});
        }
        void ConfirmOnlineAction(Action action,string title)
        {
            confirmationAction=action;confirmationFocus=onlineFocus;confirmationStatus=Status;confirmationOpen=true;
            Status=title;onlineFocus="stay";onlineButtons.Clear();
            if(onlineInputRoot)onlineInputRoot.SetActive(false);
        }
        void DismissOnlineConfirmation()
        {confirmationOpen=false;confirmationAction=null;Status=confirmationStatus;onlineFocus=confirmationFocus;onlineButtons.Clear();}
        void RefreshRooms()
        {
            if(roomListTransport!=null) { roomListTransport.Dispose(); roomListTransport = null; }
            availableRooms = new NativeNetBroker.RoomInfo[0]; roomListReceived = false;
            if (string.IsNullOrWhiteSpace(serverAddress)) { Status = "Set a room server in Settings"; return; }
            int port;if(!int.TryParse(serverPort,out port) || port < 1 || port > 65535){Status="Invalid port";return;}
            roomListTransport=new NativeNetTransport();roomListTransport.Connect(serverAddress,port);
            roomListStarted=Time.realtimeSinceStartup;listHandshake=false;roomListReceived=false;Status="Finding rooms...";
        }
        bool listHandshake, roomListReceived;
        void TryAutomaticRaceSetup()
        {
            if(!RoomReadyToStart||(!requestedSpectator&&!readySent)){automaticSetupRequested=false;return;}
            if(!IsHost||!Open||playerId==null||InRace||Loading||Recovering||autoStart||settingsPage||profilesPage||setupPage||automaticSetupRequested||onlineChoice!=OnlineChoice.None||confirmationOpen)return;
            automaticSetupRequested=true;OpenRaceSetup();
        }
        void NormalizeRoomDisplayNames()
        {
            foreach (var room in availableRooms) if (room != null) room.name = NativePlayerNamePolicy.Display(room.name);
        }
        void TickOnlineMenuState()
        {
            TryAutomaticRaceSetup();
            if(!CanHoldLobbyExit&&lobbyExitHeld){lobbyExitHold.Release();lobbyExitHeld=false;}
            if(roomListTransport!=null)
            {
                if(roomListTransport.IsConnected&&!listHandshake){listHandshake=true;roomListTransport.Send(new NativeNetMessage{kind="rooms-watch"});}
                NativeNetMessage message;
                while(roomListTransport.TryReceive(out message))if(message.kind=="rooms")
                { availableRooms = JsonUtility.FromJson<NativeNetBroker.RoomList>(message.payload)?.rooms ?? new NativeNetBroker.RoomInfo[0]; NormalizeRoomDisplayNames(); roomListReceived = true; roomListPage = Mathf.Clamp(roomListPage, 0, Mathf.Max(0, (availableRooms.Length - 1) / 5)); Status="Available rooms"; }
                if(roomListTransport!=null&&((!roomListReceived && Time.realtimeSinceStartup-roomListStarted>12)||!string.IsNullOrEmpty(roomListTransport.Error)|| (listHandshake && !roomListTransport.IsConnected)))
                {Status="Room refresh failed";roomListTransport.Dispose();roomListTransport=null;}
            }
            if(IsHost&&onlineChoice==OnlineChoice.Tracks&&OnlineHud.SelectedMenuIndex!=lastCandidate)
            {lastCandidate=OnlineHud.SelectedMenuIndex;Send("candidate",NativeTrackCatalog.IdAt(lastCandidate));}
            if(SpectatorControlsActive)
            {
                TickSpectatorDirector(Time.unscaledTime);
                Race.NetworkSpectatorFocus(observedRacer, spectatorCameraMode);
            }
        }
        void EditInvitationEndpoint(string value)
        {
            invitationDraft=value;
            string host="",port="";int split=value.LastIndexOf(':');
            if(value.StartsWith("[",StringComparison.Ordinal))
            {
                int close=value.IndexOf(']');
                if(close>1&&close+1<value.Length&&value[close+1]==':'){host=value.Substring(1,close-1);port=value.Substring(close+2);}
            }
            else if(split>0&&value.IndexOf(':')==split){host=value.Substring(0,split);port=value.Substring(split+1);}
            int number;
            if(host.Trim().Length>0&&int.TryParse(port,out number)&&number>0&&number<=65535){invitationAddress=host.Trim();invitationPort=port;}
            else {invitationAddress="";invitationPort="";}
        }
        string InvitationEndpoint()
        {
            string host=invitationAddress.Trim();int port;
            if(host.Length==0 || !int.TryParse(invitationPort,out port)||port<1||port>65535)return "";
            if(host.Contains(":"))host="["+host.Trim('[',']')+"]";
            return host+":"+port;
        }
    }
    public sealed partial class NativeRace
    {
        internal void NetworkSpectatorFocus(int index, int viewMode = 0)
        {
            viewMode = Mathf.Clamp(viewMode, 0, NativeOnlineSpectatorView.ModeCount - 1);
            if(racers==null||racers.Length==0)return;
            index=Mathf.Clamp(index,0,racers.Length-1);
            if(!cameraRig||cameraRig.target!=racers[index])
            {cameraRig=NativeKartCamera.Attach(raceCamera,racers[index]);FinishCamera=NativeKartFinishCamera.Attach(cameraRig,finishCameraController);cameraRig.SnapToTarget();}
            if(FinishCamera && FinishCamera.Ended)FinishCamera.ResetForRace();
            SetBroadcastView(viewMode >= 4);
            bool firstPerson = viewMode < 4 && (viewMode & 2) != 0;
            if (cameraRig.FirstPerson != firstPerson) cameraRig.ToggleView();
            cameraRig.SetLookBehind(viewMode < 4 && (viewMode & 1) != 0);
            if(itemHud)itemHud.SetVisible(false);
        }
    }
}
