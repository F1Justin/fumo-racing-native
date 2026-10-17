using System.Linq;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        bool networkSourcePrepared,networkSourceStarted,networkSourceClockStopped,networkCountdownBegun;
        internal int NetworkSourceStartCount,NetworkSourceCountdownDispatches;
        internal int NetworkLapClockImportedCount;
        internal bool NetworkLapClockAvailable;
        internal NativeSourceLapClock NetworkReadLocalLapClock()
        {int seat=Net?Net.LocalRacerIndex:-1;return sourceLapTimers!=null&&seat>=0&&seat<sourceLapTimers.Length?sourceLapTimers[seat].ExportNetworkClock():null;}
        internal bool NetworkSourcePrepared=>networkSourcePrepared;
        internal bool NetworkSourceStarted=>networkSourceStarted;
        internal bool NetworkSourceEnded=>sourceRaceState!=null&&sourceRaceState.EndedRace;
        internal bool NetworkEnsureActualOwner()
        {
            if(NetworkActualOwner())return true;
            return AdoptNetworkLocalOwner(NativeInputBindings.ActualConfirmDevice()??hud.ConsumeActualMenuPointerDevice());
        }
        int networkRejoinConfirmDeviceId=-1;
        void NetworkRefreshSourceOwner()
        {
            if (!Net.InRace) return;
            if (!NetworkActualOwner())
            {
                var device=NativeInputBindings.ActualConfirmDevice()??hud.ConsumeActualMenuPointerDevice();
                if(device!=null&&AdoptNetworkLocalOwner(device))networkRejoinConfirmDeviceId=device.deviceId;
            }
            if(!sourceHumanInput)return;
            var owner=NetworkActualOwner();
            if(owner && !Net.requestedSpectator)sourceHumanInput.Bind(0,Net.LocalRacerIndex,owner);
            else sourceHumanInput.Unbind(0);
            for(int slot=1;slot<4;slot++)sourceHumanInput.Unbind(slot);
        }
        void NetworkPrepareSourceCircuit()
        {
            networkSourcePrepared=true;networkCountdownBegun=false;networkSourceStarted=false;networkSourceClockStopped=false;countdownAudioSecond=-1;NetworkLapClockAvailable=false;NetworkLapClockImportedCount=0;
            NetworkResetSourceAudio();
            BeginNetworkSourceAudioDiagnostic();
            RaiseOriginalRaceStarted();
            BeginNetworkOwnerRejoinDiagnostic();
            BeginNetworkParityDiagnostic();
            if(sourceHumanInput)sourceHumanInput.AllowMenuInput=()=>!Net||!Net.Open;
        }
        void NetworkCountdownAudio()
        {
            int second=Mathf.Max(0,Mathf.CeilToInt(countdown));
            if(second==countdownAudioSecond)return;
            countdownAudioSecond=second;NetworkSourceCountdownDispatches++;NativeAudioRuntime.PlayCountdown(second);
        }
        void NetworkStartSourceRace(double anchor)
        {
            if(networkSourceStarted)return;
            networkSourceStarted=true;NetworkSourceStartCount++;raceClock.StartAt(anchor);
            sourceRanking.StartedRace();SourceTimersStartedRace();
            if(Net.IsHost)
                for(int i=0;i<raceConfig.roster.Count;i++)
                    if(i!=Net.LocalRacerIndex&&raceConfig.roster[i].isHuman)sourceLapTimers[i].StartedRace(sourceGameTime);
            NetworkLapClockAvailable=Net.IsHost;
            countdown=0;NetworkCountdownAudio();
        }
        void NetworkTickSourceRace(NativeNetworkSnapshot snapshot=null)
        {
            if(!networkSourceStarted||sourceRaceState==null)return;
            if(snapshot!=null&&snapshot.sourceClockAvailable&&snapshot.sourceClockStopped)networkSourceClockStopped=true;
            if(!networkSourceClockStopped)TickSourceTimers();
            if(Net.IsHost)
            {
                for(int i=0;i<raceConfig.roster.Count;i++)
                    if(i!=Net.LocalRacerIndex&&raceConfig.roster[i].isHuman)sourceLapTimers[i].UpdateTimer(sourceGameTime);
                for(int i=0;i<raceConfig.roster.Count;i++)
                    if(raceConfig.roster[i].isHuman&&progress[i].Finished&&sourceLapTimers[i].EndRace()&&i!=Net.LocalRacerIndex)SourceRacerEndedObserved?.Invoke(i);
            }
            else if(snapshot!=null)
            {
                if(snapshot.sourceClockAvailable)sourceGameTime=snapshot.sourceClockElapsed;
                else if(phase==Phase.Finished)sourceGameTime=raceClock.Elapsed;
                foreach(var h in sourceRaceState.Humans.Where(h=>h.Joined&&h.RacerValid))
                {
                    var actual=snapshot.karts.FirstOrDefault(k=>k.seat==h.RacerIndex);
                    NetworkLapClockAvailable=actual!=null&&actual.sourceLapClock!=null;
                    if(!NetworkLapClockAvailable)continue;
                    sourceLapTimers[h.RacerIndex].ImportNetworkClock(actual.sourceLapClock);NetworkLapClockImportedCount++;
                }
            }
            foreach(var h in sourceRaceState.Humans.Where(h=>h.Joined&&h.RacerValid))
            {
                bool finished=snapshot!=null?snapshot.karts[h.RacerIndex].finished:(progress[h.RacerIndex].Finished || Net.retiredSeats.Contains(h.RacerIndex));
                if(finished&&!h.Finished)
                {
                    sourceLapTimers[h.RacerIndex].EndRace();h.Finished=true;
                    sourceHumanDriving[h.RacerIndex]=false;SourceRacerEndedObserved?.Invoke(h.RacerIndex);
                }
            }
            bool ended=snapshot!=null?snapshot.karts.All(k=>k.finished):progress.Select((p,i)=>p.Finished||Net.retiredSeats.Contains(i)).All(value=>value);
            sourceRaceState.SynchronizeNetworkFinished(ended);
            if(ended){if(raceClock.Running)raceClock.StopAtElapsed(sourceGameTime);networkSourceClockStopped=true;phase=Phase.Finished;}
        }
        void NetworkFinishSourceCircuit()
        {
            if(!networkSourcePrepared)return;
            networkSourcePrepared=false;networkSourceStarted=false;
            sourceRaceState.FinishNetworkCircuit();
        }
    }
}
