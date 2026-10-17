using System;
namespace Fumo.NativePlayable
{
    public sealed class NativeRaceStateFlow
    {
        public enum Selection { None, Pause, ResultCard, Scoreboard }
        public sealed class HumanSlot
        {
            public int InputUserIndex=-1, RacerIndex=-1;
            public bool Joined, RacerValid, Finished, CardActive, Ready, JustStarted;
            public float LastSubmit;
            public Selection Selected;
        }
        public readonly HumanSlot[] Humans={new HumanSlot(),new HumanSlot(),new HumanSlot(),new HumanSlot()};
        public NativeRaceStateFlow(int previousRaceId=0){RaceId=previousRaceId;}
        public int RaceId {get;private set;}
        public int PausedBy {get;private set;}=-1;
        public bool InRace {get;private set;}
        public bool EndedRace {get;private set;}
        public bool ResultsActive {get;private set;}
        public bool ShowsScoreboard {get;private set;}
        public int LastSettingsLaps {get;private set;}
        public Func<float> ReadTimeScale, ReadTime;
        public Func<int,bool> ReadCardActive;
        public Func<int,bool> ReadRacerValid;
        public Action<float> WriteTimeScale;
        public event Action<int> Paused, UnPaused, ReadyChanged, ReadySubmitted, PauseSubmit, PauseCancel, ScoreboardSubmit, CancelReleased;
        public event Action PreRaceEnded, AutoCompleteResultsRequested, RaceEnded, ShowScoreboardRequested, AllReadyFinishRequested;
        public event Action RaceFinish, HudCleanup, ModeRestartRequested, ModeQuitRequested, InstantMusicEndRequested, StartLastSettingsRequested, GameModeFinished, ModeFinishRequested, FinishGameRequested;
        public void ConfigureHuman(int slot,int inputUserIndex,int racerIndex,bool joined,bool racerValid=true)
        {
            var h=Humans[slot];h.InputUserIndex=inputUserIndex;h.RacerIndex=racerIndex;h.Joined=joined;h.RacerValid=racerValid;
        }
        public int HumanForInputUser(int index)
        { for(int i=0;i<Humans.Length;i++)if(Humans[i].Joined&&Humans[i].InputUserIndex==index)return i;return -1; }
        public void StartedCircuit(int laps)
        {
            RaceId++;LastSettingsLaps=laps;InRace=true;EndedRace=false;ResultsActive=false;
            foreach(var h in Humans){h.Finished=false;h.Ready=false;h.CardActive=false;h.Selected=Selection.None;}
        }
        public void SynchronizeNetworkFinished(bool ended)
        { EndedRace=ended;ResultsActive=false; }
        public void FinishNetworkCircuit()
        { RaceFinish?.Invoke();Cleanup(); }
        public bool TryPause(int inputUserIndex)
        {
            if(!InRace||ReadTimeScale()==0f||EndedRace)return false;
            Paused?.Invoke(inputUserIndex);
            foreach(var h in Humans)h.Selected=Selection.None;
            int slot=HumanForInputUser(inputUserIndex);if(slot>=0)Humans[slot].Selected=Selection.Pause;
            PausedBy=inputUserIndex;WriteTimeScale(0f);return true;
        }
        public bool TryUnPause(int inputUserIndex)
        {
            if(inputUserIndex!=PausedBy)return false;
            UnPaused?.Invoke(inputUserIndex);
            foreach(var h in Humans)h.Selected=Selection.None;
            PausedBy=-1;WriteTimeScale(1f);return true;
        }
        public void PauseStarted(int inputUserIndex)
        { if(ReadTimeScale()==0f)TryUnPause(inputUserIndex);else TryPause(inputUserIndex); }
        public void SetRacerFinished(int racerIndex)
        {foreach(var h in Humans)if(h.Joined&&h.RacerIndex==racerIndex)h.Finished=true;CheckAllHumansFinished();}
        public bool CheckAllHumansFinished()
        {
            if(EndedRace)return false;
            foreach(var h in Humans)if(h.Joined&&(ReadRacerValid!=null?ReadRacerValid(h.RacerIndex):h.RacerValid)&&!h.Finished)return false;
            PreRaceEnded?.Invoke();AutoCompleteResultsRequested?.Invoke();EndedRace=true;RaceEnded?.Invoke();return true;
        }
        public bool DelayStillCurrent(int capturedRaceId)=>InRace&&RaceId==capturedRaceId;
        public void BeginResults(bool showsScoreboard)
        {
            ShowsScoreboard=showsScoreboard;ResultsActive=true;
            for(int i=0;i<Humans.Length;i++){var h=Humans[i];h.CardActive=h.Joined&&h.RacerValid;h.Ready=false;h.Selected=h.CardActive?Selection.ResultCard:Selection.None;}
        }
        public bool TryReady(int slot)
        {
            var h=Humans[slot];if(h.Ready||!h.CardActive||(ReadCardActive!=null&&!ReadCardActive(slot)))return false;
            h.Ready=true;ReadyChanged?.Invoke(slot);ReadySubmitted?.Invoke(slot);
            if(ShowsScoreboard)
            {
                HideResultsCards();foreach(var player in Humans)if(player.Joined)player.Selected=Selection.Scoreboard;
                ShowScoreboardRequested?.Invoke();
            }
            else
            {
                foreach(var player in Humans)if(player.Joined&&player.RacerValid&&!player.Ready)return true;
                AllReadyFinishRequested?.Invoke();
            }
            return true;
        }
        public void HideResultsCards(){foreach(var h in Humans)h.CardActive=false;}
        public void OkStarted(int inputUserIndex)
        {
            int slot=HumanForInputUser(inputUserIndex);if(slot<0||Humans[slot].JustStarted)return;
            var h=Humans[slot];
            if(h.Selected==Selection.Pause)PauseSubmit?.Invoke(slot);
            else if(h.Selected==Selection.ResultCard)TryReady(slot);
            else if(h.Selected==Selection.Scoreboard)ScoreboardSubmit?.Invoke(slot);
            h.LastSubmit=ReadTime();
        }
        public void CancelStarted(int inputUserIndex)
        {int slot=HumanForInputUser(inputUserIndex);if(slot>=0&&Humans[slot].Selected==Selection.Pause)PauseCancel?.Invoke(slot);}
        public void CancelCanceled(int inputUserIndex){int slot=HumanForInputUser(inputUserIndex);if(slot>=0)CancelReleased?.Invoke(slot);}
        public void Cleanup()
        {
            HudCleanup?.Invoke();UnPaused?.Invoke(-1);WriteTimeScale(1f);PausedBy=-1;
            InRace=false;EndedRace=false;ResultsActive=false;
            foreach(var h in Humans){h.CardActive=false;h.Selected=Selection.None;}
        }
        public bool Restart()
        {
            if(LastSettingsLaps==0)return false;
            EndedRace=true;RaceFinish?.Invoke();Cleanup();ModeRestartRequested?.Invoke();InstantMusicEndRequested?.Invoke();EndedRace=false;StartLastSettingsRequested?.Invoke();return true;
        }
        public void Quit()
        {EndedRace=true;RaceFinish?.Invoke();Cleanup();InstantMusicEndRequested?.Invoke();EndedRace=false;ModeQuitRequested?.Invoke();}
        public void FinishGame(){RaceFinish?.Invoke();Cleanup();GameModeFinished?.Invoke();ModeFinishRequested?.Invoke();FinishGameRequested?.Invoke();}
    }
}
