using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        [Serializable] sealed class OnlinePreferences {public bool community;public string server="",serverPort="27777",join="127.0.0.1",joinPort="7777",listenPort="7777";}
        string OnlinePreferencesPath => Path.Combine(Path.GetDirectoryName(NativeSaveStore.FilePath)??Application.persistentDataPath,"native-online.json");
        void LoadOnlinePreferences()
        {
            try
            {
                if(!File.Exists(OnlinePreferencesPath))return;
                var preferences=JsonUtility.FromJson<OnlinePreferences>(File.ReadAllText(OnlinePreferencesPath));
                if(!Environment.GetCommandLineArgs().Contains("--community-server")){communityEnabled=preferences.community;serverAddress=preferences.server;serverPort=preferences.serverPort;}
                directAddress=preferences.join??"127.0.0.1";lastJoinPort=preferences.joinPort??"7777";directPort=preferences.listenPort??"7777";
            }
            catch(Exception error){Debug.LogWarning("ONLINE_PREFERENCES "+error.Message);}
        }
        void SaveOnlinePreferences()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OnlinePreferencesPath));
                string temporary=OnlinePreferencesPath+".tmp";
                File.WriteAllText(temporary,JsonUtility.ToJson(new OnlinePreferences{community=communityEnabled,server=serverAddress,serverPort=serverPort,join=directAddress,joinPort=lastJoinPort,listenPort=directPort},true));
                if(File.Exists(OnlinePreferencesPath))File.Replace(temporary,OnlinePreferencesPath,OnlinePreferencesPath+".bak");else File.Move(temporary,OnlinePreferencesPath);
            }
            catch(Exception error){Status=error.Message;}
        }
        void RenderOnlineSettings() => RenderRoomServerSettings();
        void RenderOnlineProfiles()
        {

            RenderOnlineProfileIdentity();
        }
        void RenderOnlineConfirmation()
        {

            OnlineHud.NetworkPanel("OnlineConfirmation.Panel1");
            OnlineHud.NetworkLabel("OnlineConfirmation.Label1", Status);
            OnlineButton("stay", "Stay", DismissOnlineConfirmation, true);
            OnlineButton("confirm-close", "Confirm", ()=>{var action=confirmationAction;DismissOnlineConfirmation();action?.Invoke();});
        }
        void RenderRaceSetup() => SetOnlineChoice(OnlineChoice.RaceModes);
        void RenderTrackPreview()
        {
            OnlineHud.NetworkVisible(false);onlineButtons.Clear();
            OnlineHud.SetMenuPage(NativeMenuPage.Tracks);
            OnlineHud.SelectMenuIndex(Mathf.Max(0,NativeTrackCatalog.IndexOf(candidateTrack)));
            bool suppress=OnlineHud.SuppressPointerInput;OnlineHud.SuppressPointerInput=true;
            float width=OnlineHud.BeginFrame();
            try{OnlineHud.TrackMenu(width);}finally{OnlineHud.EndFrame();OnlineHud.SuppressPointerInput=suppress;}
            OnlineHud.BeginNetworkFrame("TrackPreview");OnlineHud.NetworkOpacity(1);
            try
            {
                OnlineHud.NetworkFill("TrackPreview.Fill1");
                OnlineHud.NetworkLabel("TrackPreview.Label1", "Host is choosing a track");

                if(OnlineHud.NetworkPrompt("TrackPreview.Prompt1", OnlineHud.keyCancel, "Go Back"))watchTrackPreview=false;
            }
            finally{OnlineHud.EndNetworkFrame();}
        }
        void RenderSpectatorControls()
        {
            OnlineHud.NetworkLabel("SpectatorControls.Label1", "Spectating");
            string name=Peers.FirstOrDefault(p=>!p.spectator&&p.seat==PeerSeat(observedRacer))?.name??(Race.racerNames.Length>observedRacer?Race.racerNames[observedRacer]:"");
            OnlineHud.NetworkLabel("SpectatorControls.Label2", name, localize:false);
            var view = OnlineHud.OnlinePageView.GetComponent<NativeOnlineSpectatorView>();
            if (view) view.Present(spectatorInput?.Device ?? NativeInputBindings.AppliedProfileDevice(), automaticSpectator, spectatorCameraMode, ManualFocusRemaining, Race.BroadcastCameraFallback);
            if(OnlineComplete)OnlineHud.NetworkLabel("SpectatorControls.Label4", "Race complete");
            else if (ManualFocusRemaining > 0) OnlineHud.NetworkLabel("SpectatorControls.Label4",
                string.Format(NativeLocalization.TranslateText("Manual focus: {0}s"), Mathf.CeilToInt(ManualFocusRemaining)), localize: false);
            else if (automaticSpectator && spectatorDirector.Current != null) OnlineHud.NetworkLabel("SpectatorControls.Label4", SpectatorStoryCaption(), localize: false);
            else OnlineHud.NetworkLabel("SpectatorControls.Label4", "", localize: false);
        }
        string SpectatorStoryCaption()
        {
            var story = spectatorDirector.Current;
            if (story.kind == NativeBroadcastStoryKind.Battle)
            {
                int front = Latest.karts.FirstOrDefault(k => k.seat == story.other)?.place ?? 0;
                int behind = Latest.karts.FirstOrDefault(k => k.seat == story.focus)?.place ?? 0;
                return string.Format(NativeLocalization.TranslateText("Battle: P{0} / P{1}"), front, behind);
            }
            string caption = story.kind == NativeBroadcastStoryKind.Overtake ? "Overtake"
                : story.kind == NativeBroadcastStoryKind.ItemUse ? "Item used"
                : story.kind == NativeBroadcastStoryKind.Hit ? "Hit and recovery"
                : story.kind == NativeBroadcastStoryKind.Finish ? "Finish line" : "Following the leader";
            return NativeLocalization.TranslateText(caption);
        }
        string RoomInvitationEndpoint()
        {
            return serverConnection ? Address + ":" + Port : IsHost ? InvitationEndpoint()
                : Address.Contains(":") ? "[" + Address.Trim('[', ']') + "]:" + Port : Address + ":" + Port;
        }
    }
}
