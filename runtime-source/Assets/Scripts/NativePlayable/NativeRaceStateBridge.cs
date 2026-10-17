using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        NativeRaceStateFlow sourceRaceState;
        NativeRaceHumanInputRouter sourceHumanInput;
        NativeRaceResultAppend sourceResultAppend;
        static int sourceRaceEpoch;
        public NativeRaceStateFlow SourceRaceState => sourceRaceState;
        public string SourceNativePhase => phase.ToString();
        public bool SourceActualCardActive(int slot) => SourceCardActive(slot);
        public event Action SourceRaceFinishObserved, SourceModeFinishedObserved;
        int sourcePauseSubmitFrame = -1;
        void EnsureSourceRaceState()
        {
            if(sourceRaceState!=null)return;
            sourceRaceState=new NativeRaceStateFlow(sourceRaceEpoch){ReadTimeScale=()=>Time.timeScale,ReadTime=()=>Time.time,WriteTimeScale=value=>Time.timeScale=value,ReadRacerValid=SourceRacerObjectValid};
            sourceHumanInput=gameObject.AddComponent<NativeRaceHumanInputRouter>();sourceHumanInput.State=sourceRaceState;
            sourceRaceState.Paused+=human=>{NativeInputBindings.SetPromptOwner(sourceRaceState.HumanForInputUser(human));beforePause=phase;phase=Phase.Paused;hud.SetMenuPage(NativeMenuPage.Pause);heldMenuDirection=0;NativeTelemetry.Event("race.pause","accepted",detail:"inputUser="+human);};
            sourceRaceState.UnPaused+=human=>{NativeInputBindings.SetPromptOwner(-1);bool wasPaused=phase==Phase.Paused;if(wasPaused)phase=beforePause;hud.SetMenuPage(NativeMenuPage.None);heldMenuDirection=0;if(wasPaused)NativeTelemetry.Event("race.resume","accepted",detail:"inputUser="+human);};
            sourceRaceState.PauseSubmit+=slot=>{sourcePauseSubmitFrame=Time.frameCount;ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Pause));};
            sourceRaceState.PauseCancel+=slot=>ExecuteMenuAction(hud.CancelMenu(NativeMenuPage.Pause));
            sourceHumanInput.NavigateStarted+=(slot,vector)=>{if(sourceRaceState.Humans[slot].Selected==NativeRaceStateFlow.Selection.Pause){int delta=Mathf.Abs(vector.y)>.5f?(vector.y>0?-1:1):0;if(delta!=0)hud.NavigateMenu(NativeMenuPage.Pause,delta);}else if(sourceRaceState.Humans[slot].Selected==NativeRaceStateFlow.Selection.Scoreboard&&(Mathf.Abs(vector.y)>=.5f||Mathf.Abs(vector.x)>=.5f))SourceScoreboardNavigate(slot,vector);};
            sourceRaceState.ReadCardActive=SourceCardActive;
            sourceRaceState.ReadyChanged+=SourceReadyChanged;
            sourceRaceState.ShowScoreboardRequested+=SourceShowScoreboard;
            sourceRaceState.AllReadyFinishRequested+=SourceAllReadyFinish;
            sourceRaceState.ScoreboardSubmit+=slot=>SourceSubmitScoreboard();
            sourceRaceState.RaceEnded+=()=>{phase=Phase.Finished;TelemetryObserveState();NativeTelemetry.Event("race.finished","completed");if (!FinishCamera.Ended) FinishCamera.OnEndedRace();hud.SetMenuPage(NativeMenuPage.Results);heldMenuDirection=0;BeginSourceResults(sourceRaceState.RaceId);};
            sourceRaceState.AutoCompleteResultsRequested+=CompleteSourceResultSnapshot;
            sourceRaceState.RaceFinish+=()=>SourceRaceFinishObserved?.Invoke();
            sourceRaceState.HudCleanup+=ClearResultsPreview;
            sourceRaceState.InstantMusicEndRequested+=NativeAudioRuntime.StopMusic;
            sourceRaceState.ModeRestartRequested+=()=>{AbortSourceResults();if(sessionId!=null&&!NativeGameSession.Restart(sessionId))throw new InvalidOperationException("Restart session must be Racing/Ready");};
            sourceRaceState.StartLastSettingsRequested+=()=>ChangeTrack(trackId,true);
            sourceRaceState.ModeQuitRequested+=()=>{AbortSourceResults();ReturnSourceModeMenu();};
            sourceRaceState.GameModeFinished+=()=>SourceModeFinishedObserved?.Invoke();
        }
        bool SourceRacerObjectValid(int racerIndex) => racers != null && racerIndex >= 0 && racerIndex < racers.Length && racers[racerIndex];
        public void BindSourceHuman(int slot,int racerIndex,InputActionAsset actualAsset)
        {EnsureSourceRaceState();sourceHumanInput.Bind(slot,racerIndex,actualAsset);}
        void ConfigureSourceHumans()
        {
            if (Net && Net.Open)
            {
                EnsureSourceRaceState(); SynchronizeRaceLocalOwners();
                var owner=NetworkActualOwner();
                if(owner && !Net.requestedSpectator && SourceRacerObjectValid(Net.LocalRacerIndex))sourceHumanInput.Bind(0,Net.LocalRacerIndex,owner);else sourceHumanInput.Unbind(0);
                for(int i=1;i<4;i++)sourceHumanInput.Unbind(i);
                return;
            }
            EnsureInjectedDiagnosticOwner();EnsureSourceRaceState();SynchronizeRaceLocalOwners();int slot=0;
            for(int racer=0;racer<raceConfig.roster.Count;racer++)
                if(raceConfig.roster[racer].isHuman){if(slot==4)throw new InvalidOperationException("Source human limit4");var owner=SourceActualLocalOwner(slot);if(owner)sourceHumanInput.Bind(slot,racer,owner);else sourceHumanInput.Unbind(slot);slot++;}
            for(int i=slot;i<4;i++)sourceHumanInput.Unbind(i);
        }
        void StartSourceCircuit()
        {
            EnsureSourceRaceState();AbortSourceResults();sourceRaceState.Cleanup();sourceRaceState.StartedCircuit(RaceLaps);sourceRaceEpoch=sourceRaceState.RaceId;
            StartSourceTimers();
            sourceResultAppend=new NativeRaceResultAppend(racers.Length);
            sourceResultAppend.EndRacerRequested+=SourceEndRacer;
        }
        int[] CurrentSourceRankInput()=>sourceRanking.SnapshotRanks();
        public int[] SourceResultOrderSnapshot()=>sourceResultAppend.Snapshot();
        void RecordSourceNaturalFinish(int racer)
        {
            sourceResultAppend.FinishedRace(racer,CurrentSourceRankInput());
            PublishSourceAppendRows();
        }
        void CompleteSourceResultSnapshot()
        {
            sourceResultAppend.AutoComplete(CurrentSourceRankInput());var order=sourceResultAppend.Snapshot();
            PublishSourceAppendRows();
            if(sessionId!=null&&!NativeGameSession.CompleteStation(sessionId))throw new InvalidOperationException("Source Results require complete station snapshot");
        }
        void PublishSourceAppendRows()
        {
            var order=sourceResultAppend.PrefixSnapshot();
            var session=NativeGameSession.Current;
            for(int place=0;place<order.Length;place++)
            {
                int racer=order[place];finishPlaces[racer]=place+1;
                if(sessionId==null)continue;
                string seatId=session.config.roster[racer].seatId;
                var existing=session.pending.FirstOrDefault(row=>row.seatId==seatId);
                if(existing!=null){if(existing.place!=place+1)throw new InvalidOperationException("Natural result place differs from source append order");continue;}
                if(!NativeGameSession.RecordFinish(sessionId,seatId,SourceTotalSeconds(racer),place+1))throw new InvalidOperationException("Source append row rejected");
            }
        }
        void FinishSourceGame(){sourceRaceState.FinishGame();}
        void TickSourceState()
        {
            if(sourceRaceState==null)return;
            if(sourceRaceState.InRace)ConfigureSourceHumans();
            TickSourceResults(sourceRaceState.RaceId,sourceRaceState.InRace);
        }
        void DisposeSourceState(){AbortSourceResults();if(sourceHumanInput)Destroy(sourceHumanInput);}
    }
}
