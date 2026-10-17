using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed class NativeRaceStateVerification : MonoBehaviour
    {
        [Serializable] sealed class Report { public bool accepted=false,passed; public string identity="injected core; standalone QuickRace direct Begin/pause/Restart with actual countdown callbacks; no natural input acceptance";public string error;public List<string> checks=new List<string>();public List<string> failures=new List<string>();public List<string> observations=new List<string>(); }
        Report report=new Report();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
        {if(Array.IndexOf(Environment.GetCommandLineArgs(),"--race-state-source-probe")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"--race-state-source-probe-self-test")>=0)new GameObject("RaceStateSourceProbe").AddComponent<NativeRaceStateVerification>();}
        void Check(bool ok,string id){report.checks.Add((ok?"PASS ":"FAIL ")+id);if(!ok)report.failures.Add(id);}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            bool standalone=Array.IndexOf(args,"--race-state-source-probe-self-test")>=0;
            if(standalone)DontDestroyOnLoad(gameObject);
            var stack=new Stack<IEnumerator>();stack.Push(RunSourceProbe());
            try
            {
                while(stack.Count>0)
                {
                    bool next;object value;
                    try{next=stack.Peek().MoveNext();value=next?stack.Peek().Current:null;}
                    catch(Exception error){report.error=error.ToString();Check(false,"probe exception");break;}
                    if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if (value is IEnumerator nested && !(value is CustomYieldInstruction)){stack.Push(nested);continue;}
                    yield return value;
                }
            }
            finally
            {
                while(stack.Count>0)
                {
                    try{(stack.Pop() as IDisposable)?.Dispose();}
                    catch(Exception error){report.error=error.ToString();Check(false,"probe cleanup exception");}
                }
                report.passed=report.checks.Count>0&&report.failures.Count==0&&string.IsNullOrEmpty(report.error);
                int reportArg=Array.IndexOf(args,"--report-dir");
                string directory=reportArg>=0&&reportArg+1<args.Length?args[reportArg+1]:Application.persistentDataPath;
                Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"race-state-source-probe.json");
                File.WriteAllText(path,JsonUtility.ToJson(report,true));Debug.Log("RACE_STATE_SOURCE_PROBE "+(report.passed?"PASS":"FAIL")+" "+path);
                if(standalone)Application.Quit(report.passed?0:2);
            }
        }
        IEnumerator RunSourceProbe()
        {
            var args=Environment.GetCommandLineArgs();bool standalone=Array.IndexOf(args,"--race-state-source-probe-self-test")>=0;
            if(standalone)QualitySettings.vSyncCount=0;
            float scale=1,time=0;var state=new NativeRaceStateFlow{ReadTimeScale=()=>scale,WriteTimeScale=x=>scale=x,ReadTime=()=>time};
            state.ConfigureHuman(0,4,0,true);state.ConfigureHuman(1,8,1,true);state.StartedCircuit(3);
            state.Paused+=human=>Check(scale==1&&state.PausedBy==-1,"pause-observer-before-clock-owner");
            Check(state.TryPause(4),"pause-owner-inputuser");Check(!state.TryUnPause(8),"foreign-user-unpause-rejected");
            state.UnPaused+=human=>{if(human==4)Check(scale==0&&state.PausedBy==4,"unpause-observer-before-clock-owner");};
            Check(state.TryUnPause(4),"owner-unpause");
            int ended=0;state.RaceEnded+=()=>ended++;state.SetRacerFinished(0);Check(ended==0,"wait-all-human");state.SetRacerFinished(1);Check(ended==1,"last-human-end");state.CheckAllHumansFinished();Check(ended==1,"ended-repeat-guard");
            state.BeginResults(true);bool active=false;state.ReadCardActive=slot=>active;Check(!state.TryReady(0),"actual-hidden-ready-rejected");active=true;
            int scoreboard=0;state.ShowScoreboardRequested+=()=>scoreboard++;Check(state.TryReady(0)&&scoreboard==1,"first-human-scoreboard");Check(!state.TryReady(0)&&!state.TryReady(1),"hidden-and-repeat-after-scoreboard");
            state.BeginResults(false);int finished=0;state.AllReadyFinishRequested+=()=>finished++;state.TryReady(0);Check(finished==0,"tt-wait-all-cards");state.TryReady(1);Check(finished==1,"tt-all-ready-finish");
            state.Cleanup();state.StartedCircuit(0);Check(!state.Restart(),"zero-laps-restart-rejected");
            var append=new NativeRaceResultAppend(3);append.EndRacerRequested+=racer=>{if(racer==1)append.AutoComplete(new[]{0,1,2});};append.FinishedRace(0,new[]{0,1,2});append.FinishedRace(1,new[]{0,1,2});Check(append.Count==3&&append.Snapshot()[2]==2,"synchronous-end-before-append");
            if(standalone)
            {
                yield return null;var lifecycleRace=FindObjectOfType<NativeRace>();
                Check(lifecycleRace!=null,"actual NativeRace lifecycle binding");
                if(lifecycleRace)yield return lifecycleRace.CheckSourceLifecycleShort(Check,value=>report.observations.Add(value));
            }
            for(int frame=0;frame<120;frame++)
            {var race=FindObjectOfType<NativeRace>();if(race&&race.SourceRaceState!=null){var real=race.SourceRaceState;report.observations.Add(frame+":"+real.RaceId+":"+real.InRace+":"+real.EndedRace+":"+real.PausedBy+":"+Time.timeScale+":"+Time.time);}yield return null;}
            Check(report.observations.Count>0,"actual scene SourceRaceState binding observed");

        }
    }
}

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        internal IEnumerator CheckSourceLifecycleShort(Action<bool,string> check,Action<string> observe)
        {
            selectedMode=NativeSessionMode.QuickRace;autopilot=false;
            var config=new NativeSessionConfig{mode=NativeSessionMode.QuickRace,modeId="quick-race",laps=5,racerCount=2,itemType="none",trackIds=new List<string>{trackId}};
            for(int i=0;i<2;i++)config.roster.Add(new NativeSessionSeat{seatId="state-short-"+i,characterId=vehicleRoster.CharacterIdAt(selectedCharacterIndex),vehicleId="kart",skinIndex=0,isHuman=i==0});
            sessionId=NativeGameSession.Create(config);
            int boundaries=0;
            Action started=()=>{
                boundaries++;
                var timestamps=sourceLapTimers.Select(timer=>(double)typeof(NativeSourceLapTimer).GetField("lastTimestamp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(timer)).ToArray();
                check(sourceGameTime==0&&timestamps.All(x=>x==0)&&sourceLapTimers.All(x=>x.GetTotalTime()==TimeSpan.Zero),"actual synchronous StartedRace zero boundary "+boundaries);
                check(sourceRanking.Started&&raceClock.Running&&phase==Phase.Racing,"actual synchronous countdown consumer "+boundaries);
                observe("actual-start-boundary:"+boundaries+":frame="+Time.frameCount+":epoch="+sourceRaceState.RaceId+":clock="+sourceGameTime+":timestamps="+string.Join(",",timestamps));
            };
            SourceStartedBoundaryObserved+=started;
            try
            {
                Begin();
                double deadline=Time.realtimeSinceStartupAsDouble+10;
                while(boundaries<1&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                check(boundaries==1,"first actual countdown completed");if(boundaries!=1)yield break;
                var oldTimers=sourceLapTimers;var oldRanking=sourceRanking;var oldAppend=sourceResultAppend;
                int oldRouter=sourceHumanInput.GetInstanceID(),oldRace=GetInstanceID(),epoch=sourceRaceState.RaceId;
                int owner=sourceRaceState.Humans.First(h=>h.Joined&&h.RacerValid).InputUserIndex;
                check(sourceRaceState.TryPause(owner)&&Time.timeScale==0,"direct pause owns scaled clock before restart");
                check(sourceRaceState.Restart(),"direct QuickRace Restart accepted");
                NativeRace reentered=null;
                deadline=Time.realtimeSinceStartupAsDouble+10;
                while(Time.realtimeSinceStartupAsDouble<deadline)
                {
                    var candidate=FindObjectOfType<NativeRace>();
                    if(candidate&&candidate.GetInstanceID()!=oldRace&&candidate.sourceRaceState!=null&&candidate.sourceLapTimers!=null){reentered=candidate;break;}
                    yield return null;
                }
                check(reentered!=null,"Restart real scene reentry binds new NativeRace");
                if(!reentered)yield break;
                SourceStartedBoundaryObserved-=started;
                int secondBoundaries=0;
                Action secondStarted=()=>{
                    secondBoundaries++;
                    var timestamps=reentered.sourceLapTimers.Select(timer=>(double)typeof(NativeSourceLapTimer).GetField("lastTimestamp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(timer)).ToArray();
                    check(reentered.sourceGameTime==0&&timestamps.All(x=>x==0)&&reentered.sourceLapTimers.All(x=>x.GetTotalTime()==TimeSpan.Zero),"reentered synchronous StartedRace zero boundary");
                    check(reentered.sourceRanking.Started&&reentered.raceClock.Running&&reentered.phase==Phase.Racing,"reentered actual countdown consumer");
                    observe("reentered-start-boundary:"+secondBoundaries+":frame="+Time.frameCount+":epoch="+reentered.sourceRaceState.RaceId);
                };
                reentered.SourceStartedBoundaryObserved+=secondStarted;
                try
                {
                    check(reentered.sourceRaceState.RaceId>epoch&&Time.timeScale==1&&reentered.sourceRaceState.PausedBy==-1,"reentry epoch clock pause ownership reset");
                    check(!ReferenceEquals(oldTimers,reentered.sourceLapTimers)&&!ReferenceEquals(oldRanking,reentered.sourceRanking)&&!ReferenceEquals(oldAppend,reentered.sourceResultAppend),"reentry binds fresh timer ranking append");
                    check(reentered.sourceHumanInput&&reentered.sourceHumanInput.GetInstanceID()!=oldRouter&&reentered.sourceHumanInput.State==reentered.sourceRaceState,"new scene human router binds new source state");
                    check(reentered.sourceRaceState.Humans.Where(h=>h.Joined).All(h=>!h.Finished&&!h.Ready&&!h.CardActive&&h.Selected==NativeRaceStateFlow.Selection.None),"reentry clean human selection");
                    check(!reentered.sourceRaceState.ResultsActive&&!reentered.hud.UsesSourceResults&&!reentered.hud.SourceCardActive(0),"reentry result consumers inactive");
                    deadline=Time.realtimeSinceStartupAsDouble+10;
                    while(secondBoundaries<1&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                    check(secondBoundaries==1,"second actual countdown completed exactly once after scene reload");
                }
                finally{if(reentered)reentered.SourceStartedBoundaryObserved-=secondStarted;}

            }
            finally{SourceStartedBoundaryObserved-=started;}
        }
    }
}
