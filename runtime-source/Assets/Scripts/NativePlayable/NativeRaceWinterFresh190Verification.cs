using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fumo.NativePlayable
{
    public sealed class NativeWinterFresh190Observer : MonoBehaviour
    {
        [Serializable] public sealed class Receipt
        {
            public string boundary, scene, resetLastReason, audioContext;
            public int id, sceneHandle, frame, tick, resetCount, raceInstanceId, sourceRaceId, managerId, sourceStartedCount;
            public double sourceGameTime;
            public float age, value, delta, scaledTime, realTime, timeScale;
            public bool enabled, inRace;
        }
        [Serializable] public sealed class RawLog { public string message, stack, type; public float realTime; public bool postBind; }
        [Serializable] public sealed class Check { public string id, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string fixtureType="fresh-process-new-scene-real-fixed-clock-observer", finishScope="injected production Begin/FinishGame lifecycle; physical completion pending";
            public string rawLogScope="All Application.logMessageReceived callbacks since BeforeSceneLoad host installation, without message filtering; full boot log retained by runner", actualModeConfigJson, diagnosticOwnerScheme, diagnosticOwnerDevices, diagnosticInputUserId;
            public int diagnosticOwnerId, diagnosticPlayerIndex;
            public string assemblySha256, failure, audioScope="actual source Splash chain; ordinary hearing pending";
            public bool passed, overallAccepted=false, startupEngineErrorFree, pendingRender=true, pendingPhysical=true, pendingOrdinaryHearing=true, originalVisualAccepted=false;
            public int engineErrorCount, preBindStartupErrors, postBindProductionErrors, sourceStartedCount, splashBegin, splashPlay, splashCompleted;
            public float realElapsed, scaledElapsed;
            public List<RawLog> rawLog=new List<RawLog>();
            public List<Receipt> receipts=new List<Receipt>();
            public List<Check> checks=new List<Check>();
        }
        public static NativeWinterFresh190Observer Current { get; private set; }
        public readonly Report report=new Report();
        public NativeShaderGlobalsOverTime publisher;
        NativeShaderGlobalsManager manager;
        NativeRace race;
        public Func<string> ReadAudioContext;
        string pendingPipelineException;
        float installed, awakeReal, awakeScaled, previousAge;
        int ticks, stableResetCount;
        bool hasAwake, sceneBound, terminal, delayCrossed, midCrossed, finished, begun;
        string directory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHost() { Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!Environment.GetCommandLineArgs().Contains("--winter-fresh190-self-test")) return;
            var go=new GameObject("Independent fresh190 observer");
            DontDestroyOnLoad(go); Current=go.AddComponent<NativeWinterFresh190Observer>();
        }
        void Awake()
        {
            installed=Time.realtimeSinceStartup;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--report-dir");
            directory=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"winter-fresh190");
            NativeShaderGlobalsOverTime.Observation+=Observe;
            SceneManager.sceneLoaded+=SceneLoaded;
            Application.logMessageReceived+=Log;
        }
        void Log(string message,string stack,LogType type)
        {
            report.rawLog.Add(new RawLog{message=message,stack=stack,type=type.ToString(),realTime=Time.realtimeSinceStartup,postBind=race});
            if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)
            {report.engineErrorCount++;if(race)report.postBindProductionErrors++;else report.preBindStartupErrors++;}
            if(type==LogType.Exception)pendingPipelineException=message+"\n"+stack;
        }
        public void Require(bool condition,string id,string detail="")
        { report.checks.Add(new Check{id=id,passed=condition,detail=detail}); }
        Receipt Sample(string boundary, float delta=0)
        {
            var s=publisher.gameObject.scene;
            var receipt=new Receipt{boundary=boundary,scene=s.name,sceneHandle=s.handle,id=publisher.GetInstanceID(),frame=Time.frameCount,tick=ticks,age=publisher.Elapsed,
                value=Shader.GetGlobalFloat(publisher.shaderKey),delta=delta,scaledTime=Time.time,realTime=Time.realtimeSinceStartup,timeScale=Time.timeScale,
                raceInstanceId=race?race.GetInstanceID():0,sourceRaceId=race&&race.SourceRaceState!=null?race.SourceRaceState.RaceId:-1,
                managerId=manager?manager.GetInstanceID():0,resetLastReason=manager?manager.LastResetReason:null,sourceStartedCount=report.sourceStartedCount,
                sourceGameTime=race?race.SourceGameTime:0,audioContext=ReadAudioContext?.Invoke(),
                enabled=publisher.enabled,resetCount=manager?manager.ResetCount:-1,inRace=race&&race.SourceRaceState!=null&&race.SourceRaceState.InRace};
            report.receipts.Add(receipt);return receipt;
        }
        void Observe(NativeShaderGlobalsOverTime item,bool awake)
        {
            if(finished||item.shaderKey!="_MeltingFactor"||item.delay!=30f||item.targetTime!=160f)return;
            if(awake)
            {
                if(hasAwake){Require(false,"single fresh publisher","Second matching Awake encountered");return;}
                publisher=item;hasAwake=true;previousAge=item.Elapsed;awakeReal=Time.realtimeSinceStartup;awakeScaled=Time.time;
                Sample("actual-Awake");Require(item.Elapsed==-30f&&item.enabled,"actual Awake minus30 enabled");return;
            }
            if(item!=publisher){Require(false,"same publisher identity");return;}
            ticks++;
            var sample=Sample("actual-FixedUpdate-after-publish",Time.fixedDeltaTime);
            float expected=Mathf.Min(160f,previousAge+Time.fixedDeltaTime);
            Require(item.Elapsed==expected,"tick age "+ticks);
            Require(sample.value==Mathf.Clamp01(item.Elapsed/160f),"tick published value "+ticks);
            Require(sample.timeScale==1f,"tick real normal timescale "+ticks);
            Require(item.enabled==(item.Elapsed<160f),"tick enabled "+ticks);
            if(begun)Require(manager&&manager.ResetCount==stableResetCount,"stable reset count "+ticks);
            if(!delayCrossed&&previousAge<0&&item.Elapsed>=0){delayCrossed=true;Sample("delay-zero-crossing");}
            if(!midCrossed&&previousAge<80&&item.Elapsed>=80){midCrossed=true;Sample("mid80-crossing");}
            if(item.Elapsed==160f&&!item.enabled){terminal=true;Sample("terminal160-disabled-publish1");}
            previousAge=item.Elapsed;
        }
        void SceneLoaded(Scene scene,LoadSceneMode mode)
        {if(hasAwake&&publisher&&publisher.gameObject.scene.handle==scene.handle){sceneBound=true;Sample("actual-sceneLoaded-postAwake");}}
        public void Bind(NativeRace target)
        {race=target;manager=target.GetComponent<NativeShaderGlobalsManager>();Require(manager,"production reset manager");}
        public void AfterBegin()
        {begun=true;stableResetCount=manager?manager.ResetCount:-1;Sample("production-Begin-reset");}
        public bool Terminal=>terminal;
        public bool HasAwake=>hasAwake;
        public float ScaledAge=>hasAwake?Time.time-awakeScaled:0;
        public bool Complete=>finished;
        public void StartedBoundary()
        {
            report.sourceStartedCount++;Sample("actual-synchronous-SourceStartedRace");
            Require(race.SourceGameTime==0,"SourceStartedRace synchronous zero clock");
        }
        public void Record(string boundary) { Sample(boundary); }
        public void BeforeFinish()
        {
            Sample("before-injected-production-FinishGame");
            Require(publisher.Elapsed==160f&&!publisher.enabled&&Shader.GetGlobalFloat(publisher.shaderKey)==1f,"strict terminal before Finish160 disabled global1");
        }
        public void DuringFinish()
        {Sample("actual-RaceFinish-before-cleanup");Require(race.SourceRaceState.InRace,"finish event before production cleanup");}
        public void AfterFinish()
        {
            Sample("after-production-cleanup-reset0");
            Require(!race.SourceRaceState.InRace&&!race.SourceRaceState.ResultsActive,"production cleanup clears race/results");
            Require(manager.ResetCount==stableResetCount+1&&manager.LastResetReason=="RaceFinish"&&Shader.GetGlobalFloat("_MeltingFactor")==0f,"actual finish manager reset0");
            Require(publisher.Elapsed==160f&&!publisher.enabled,"finish reset preserves disabled terminal publisher");
        }
        public void AfterNextFixedTick()
        {
            Sample("actual-next-Unity-fixed-boundary-after-Finish");
            Require(publisher.Elapsed==160f&&!publisher.enabled&&Shader.GetGlobalFloat(publisher.shaderKey)==0f,"reset0 persists next actual fixed boundary");
            Require(manager.ResetCount==stableResetCount+1,"no extra reset after Finish");
        }
        public void SaveAndQuit(string failure=null)
        {
            if(finished)return;finished=true;report.failure=failure;
            report.realElapsed=hasAwake?Time.realtimeSinceStartup-awakeReal:Time.realtimeSinceStartup-installed;
            report.scaledElapsed=hasAwake?Time.time-awakeScaled:0;
            Require(hasAwake&&sceneBound&&delayCrossed&&midCrossed&&terminal,"all actual fresh boundaries");
            Require(report.scaledElapsed>=190f,"actual full190 scaled interval");
            report.startupEngineErrorFree=report.preBindStartupErrors==0;
            Require(report.postBindProductionErrors==0,"postBind production engine errors zero; preBind raw errors retained separately");
            Require(report.sourceStartedCount==1,"actual SourceStartedRace count1");
            using(var sha=SHA256.Create()){var location=typeof(NativeRace).Assembly.Location;if(File.Exists(location))report.assemblySha256=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(location))).Replace("-","").ToLowerInvariant();}
            report.passed=failure==null&&report.checks.All(c=>c.passed);
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"winter-fresh190-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("WINTER_FRESH190_FINAL passed="+report.passed+" ticks="+ticks);Application.Quit(report.passed?0:1);
        }
        void Update()
        {
            if(finished)return;
            if(pendingPipelineException!=null){SaveAndQuit("actual pipeline exception: "+pendingPipelineException);return;}
            if(Time.realtimeSinceStartup-installed>280f)SaveAndQuit("actual observer wall timeout");
        }
        void OnDestroy()
        {NativeShaderGlobalsOverTime.Observation-=Observe;SceneManager.sceneLoaded-=SceneLoaded;Application.logMessageReceived-=Log;}
    }
    public sealed partial class NativeRace
    {
        IEnumerator CheckWinterFresh190()
        {
            var observer=NativeWinterFresh190Observer.Current;
            if(!observer){Debug.LogError("Fresh190 observer missing BeforeSceneLoad host");Application.Quit(1);yield break;}
            var pending=new Stack<IEnumerator>();pending.Push(CheckWinterFresh190Core(observer));
            try
            {
                while(pending.Count>0&&!observer.Complete)
                {
                    object value=null;bool next=false;Exception failure=null;
                    try {next=pending.Peek().MoveNext();if(next)value=pending.Peek().Current;}
                    catch(Exception error){failure=error;}
                    if(failure!=null){observer.SaveAndQuit("guarded actual pipeline exception: "+failure);yield break;}
                    if(!next)
                    {
                        Exception disposalFailure=null;
                        try{(pending.Pop() as IDisposable)?.Dispose();}
                        catch(Exception error){disposalFailure=error;}
                        if(disposalFailure!=null){observer.SaveAndQuit("actual coroutine completion exception: "+disposalFailure);yield break;}
                        continue;
                    }
                    if(value is IEnumerator child && !(value is CustomYieldInstruction)){pending.Push(child);continue;}
                    yield return value;
                }
            }
            finally
            {
                while(pending.Count>0)
                {
                    try{(pending.Pop() as IDisposable)?.Dispose();}
                    catch(Exception error){observer.SaveAndQuit("actual cleanup exception: "+error);}
                }
            }
        }
        IEnumerator CheckWinterFresh190Core(NativeWinterFresh190Observer observer)
        {
            observer.Bind(this);observer.ReadAudioContext=()=>nativeAudioContext;
            float deadline=Time.realtimeSinceStartup+20;
            while(StartupSplashActive&&!observer.Complete&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!StartupSplash||StartupSplashActive)throw new InvalidOperationException("actual source Splash missing/incomplete");
            observer.report.splashBegin=StartupSplash.BeginCount;observer.report.splashPlay=StartupSplash.PlayCount;observer.report.splashCompleted=StartupSplash.CompletionCount;
            observer.Require(StartupSplash.duration==10f&&StartupSplash.BeginCount==1&&StartupSplash.CompletionCount==1,"actual10second Splash begin/completion");
            observer.Require(NativeAudioRuntime.Muted||StartupSplash.PlayCount==1,"actual source Splash audio playback");
            if(!observer.HasAwake)throw new InvalidOperationException("Winter fresh publisher Awake missing; run --track wintermistylake");
            SourceStartedBoundaryObserved+=observer.StartedBoundary;
            try
            {
                ModePhysicalBegin(false);observer.AfterBegin();
                var actualOwner=SourceActualLocalOwner(0);
                observer.Require(actualOwner,"actual diagnostic PlayerInput owner exists");
                if(actualOwner)
                {
                    observer.report.diagnosticOwnerId=actualOwner.GetInstanceID();observer.report.diagnosticPlayerIndex=actualOwner.playerIndex;
                    observer.report.diagnosticOwnerScheme=actualOwner.currentControlScheme;observer.report.diagnosticOwnerDevices=string.Join(",",actualOwner.devices.Select(device=>device.deviceId));
                    observer.report.diagnosticInputUserId=actualOwner.user.id.ToString();
                }
                observer.report.actualModeConfigJson=JsonUtility.ToJson(raceConfig);
                observer.Require(racers.Length==1&&sourceRaceState.Humans.Count(h=>h.Joined)==1,"production BuildModeConfig TT one bound diagnostic PlayerInput owner no AI");
                deadline=Time.realtimeSinceStartup+10;
                while((observer.report.sourceStartedCount==0||SourceGameTime<=0||string.IsNullOrEmpty(nativeAudioContext)||nativeAudioContext=="Countdown")&&!observer.Complete&&Time.realtimeSinceStartup<deadline)yield return null;
                observer.Require(observer.report.sourceStartedCount==1&&SourceGameTime>0&&phase==Phase.Racing&&nativeAudioContext=="WistyLake","real countdown to SourceStartedRace then advance and actual Winter audio context");
                observer.Record("production-Racing-SourceClock-advanced-audio-bound");
                while(!observer.Terminal&&!observer.Complete)yield return null;
                if(observer.Complete)yield break;
                while(observer.ScaledAge<190f&&!observer.Complete)yield return null;
                if(observer.Complete)yield break;
                yield return null;
                observer.BeforeFinish();SourceRaceFinishObserved+=observer.DuringFinish;
                try { FinishSourceGame();observer.AfterFinish(); }
                finally { SourceRaceFinishObserved-=observer.DuringFinish; }
                yield return new WaitForFixedUpdate();
                observer.AfterNextFixedTick();observer.SaveAndQuit();
            }
            finally {SourceStartedBoundaryObserved-=observer.StartedBoundary;observer.ReadAudioContext=null;}
        }
    }
}
