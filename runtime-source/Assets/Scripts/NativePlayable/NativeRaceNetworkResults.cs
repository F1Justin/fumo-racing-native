using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        const float FinishShotDuration = 3f, ResultsFadeDuration = .25f;
        float localFinishAt = -1, resultsShownAt = -1, nextHeartbeat;
        bool personalReturnPending, returnedToRoom;
        NativeNetworkConfig roundConfig;
        readonly System.Collections.Generic.Dictionary<int,string> roundNames=new System.Collections.Generic.Dictionary<int,string>();
        bool LocalFinished => !requestedSpectator && InRace && !Loading && Latest?.karts != null && Latest.karts.Any(k => k.seat == LocalRacerIndex && k.finished);
        bool PersonalResultsVisible => InRace && resultsShownAt >= 0;
        float PersonalResultsAlpha => PersonalResultsVisible ? Mathf.Clamp01((Time.unscaledTime - resultsShownAt) / ResultsFadeDuration) : 0;
        bool CanReturnToRoom => PersonalResultsVisible && OnlineComplete && lastResultsRound == Round && !Recovering && !personalReturnPending;
        bool RoomReadyToStart => Participants.Length >= 2 && Participants.All(p => p.returned && p.ready && p.connected);

        void ResetPersonalResults()
        {
            localFinishAt = resultsShownAt = -1;
            personalReturnPending = returnedToRoom = false;
        }
        void TickPersonalResults()
        {
            if (!LocalFinished && !(requestedSpectator && InRace && OnlineComplete)) return;
            if (localFinishAt < 0) { localFinishAt = Time.unscaledTime; paused = false; }
            if (resultsShownAt < 0 && Time.unscaledTime - localFinishAt >= FinishShotDuration) ShowPersonalResults();
        }
        void ShowPersonalResults()
        {
            if ((!LocalFinished && !(requestedSpectator && InRace && OnlineComplete)) || PersonalResultsVisible) return;
            resultsShownAt = Time.unscaledTime;
            onlineFocus = "room"; onlineButtons.Clear(); paused = false;
        }
        bool HandlePersonalResultsConfirm()
        {
            if (!LocalFinished && !(requestedSpectator && InRace && OnlineComplete)) return false;
            onlineInputConsumedFrame = Time.frameCount;
            if (!PersonalResultsVisible) ShowPersonalResults();
            else ReturnToRoom();
            return true;
        }
        void ReturnToRoom()
        {
            if (!CanReturnToRoom) return;
            personalReturnPending = true;
            Send("return-room");
        }
        void ReceivePersonalReturn(NativeNetMessage message)
        {
            if (message.round != Round || message.player != playerId || !message.completed) return;
            LocalSeat=message.seat;requestedSpectator=message.spectator;if(!requestedSpectator)participationRequested=false;
            selection.seat=LocalSeat; Peers = message.peers ?? Peers;
            personalReturnPending = false; returnedToRoom = true; setupActive=setupPage=false; candidateTrack=null;watchTrackPreview=true;
            InRace = Loading = false; paused = readySent = startSent = false;
            frameInput = new NativeNetworkInput(); Inputs.Clear();
            ResetTelemetryInputCache(); TelemetryNetworkIdentity(); NativeTelemetry.Event("network.returned-to-room", "applied");
            onlineChoice = onlineChoiceEntry = OnlineChoice.None; onlinePage = onlineFocus = null;
            if (Race) Race.NetworkLobby();
            if(!requestedSpectator)Send("selection",JsonUtility.ToJson(selection));
            lobbyAt = Time.realtimeSinceStartup; Status = "Room " + Room;
        }
        string ResultsStatusText()
        {
            if (Recovering) return "Reconnecting";
            if (personalReturnPending) return "Returning to room...";
            if (OnlineComplete) return lastResultsRound == Round ? "Race complete" : "Confirming race results...";
            int remaining = Latest.karts.Count(k => !k.finished);
            return string.Format(NativeLocalization.TranslateText("Waiting for racers to finish ({0} remaining)"), remaining);
        }
    }
}
