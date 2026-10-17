using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        bool readyStructureDiagnostic;
        [Serializable] sealed class StructureSourceResources { public StructureSourceResource[] rows; }
        [Serializable] sealed class StructureSourceResource { public string expectedIdentity,sourceGuid,sourcePath,kind; }
        [Serializable] sealed class StructureCheck { public string name; public bool passed; }
        [Serializable] sealed class StructureReport
        {
            public string scope, error, assemblySHA256, baselineSHA256;
            public bool passed, structurePassed, accepted=false, renderEvidenceAvailable=false, physicalHumanAccepted=false, ordinaryAudioAccepted=false;
            public int engineErrors;
            public List<StructureCheck> checks=new List<StructureCheck>();
            public List<string> samples=new List<string>();
            public string[] pending={"original paired pixels and actual OnGUI", "physical human devices", "ordinary audio output", "natural race completion", "result avatar camera rendering (explicit structure diagnostic excludes Avatar Create/Render)"};
            public ItemPrefabPlayerReport itemPrefab;
        }
        static void StructureRequire(StructureReport report,bool passed,string name)
        {
            report.checks.Add(new StructureCheck{name=name,passed=passed});
            if(!passed)throw new InvalidOperationException(name);
        }
        internal IEnumerator CheckStructureDiagnostic(bool ready)
        {
            var report=new StructureReport{scope=ready?"Actual NativeRace source state/results subscriptions; injected four-owner finish and ShowsScoreboard branch; real InputSystem Ok callbacks":"Independent53/32/17 source bindings and production prefab Update/Start/OnDestroy with explicit next-frame observation; EOF/render unobserved",assemblySHA256=ItemPrefabFileSha256(typeof(NativeRace).Assembly.Location)};
            Directory.CreateDirectory(reportDirectory);
            readyStructureDiagnostic=ready;
            var inputs=new List<InputDevice>();int errorsBefore=errorCount;
            bool silenced=NativeAudioRuntime.VerificationListenerSilenced;float timeScale=Time.timeScale;
            NativeAudioRuntime.VerificationListenerSilenced=true;NativeAudioRuntime.ApplyVolumes();Time.timeScale=1;
            var stack=new Stack<IEnumerator>();stack.Push(ready?ReadyStructureCore(report,inputs):PrefabStructureCore(report));
            while(stack.Count>0)
            {
                bool next;object current;
                try{next=stack.Peek().MoveNext();current=next?stack.Peek().Current:null;}
                catch(Exception error){report.error=error.ToString();break;}
                if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(current is IEnumerator child && !(current is CustomYieldInstruction)){stack.Push(child);continue;}
                // This explicit nonrendered observer never claims an EOF or GUI receipt.
                yield return current is WaitForEndOfFrame?null:current;
            }
            while(stack.Count>0){try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception error){report.error=(report.error??"")+"\n"+error;}}
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            foreach(var input in inputs)if(input.added)InputSystem.RemoveDevice(input);
#endif
            yield return null;yield return null;
            if (ready)
            {
                float cleanupDeadline = Time.realtimeSinceStartup + 1;
                while ((localSelectionActive || localOwners.Players.Length != 0) && Time.realtimeSinceStartup < cleanupDeadline) yield return null;
                report.samples.Add("device cleanup: selection=" + localSelectionActive + " page=" + hud.MenuPage + " owners=" + localOwners.Players.Length);
                report.checks.Add(new StructureCheck { name = "last actual device loss returns selection to Main without an ownerless frame", passed = !localSelectionActive && hud.MenuPage == NativeMenuPage.Main && localOwners.Players.Length == 0 });
            }
            report.engineErrors=errorCount-errorsBefore;
            report.checks.Add(new StructureCheck{name="scoped engine errors zero through deferred cleanup",passed=report.engineErrors==0});
            report.structurePassed=report.error==null&&report.checks.Count>0&&report.checks.All(c=>c.passed)
                &&(report.itemPrefab==null||report.itemPrefab.scopes.Count>0&&report.itemPrefab.scopes.All(s=>s.completed&&s.failures==0&&s.checks>0));
            NativeAudioRuntime.VerificationListenerSilenced=silenced;NativeAudioRuntime.ApplyVolumes();Time.timeScale=timeScale;
            Directory.CreateDirectory(reportDirectory);
            report.passed=report.structurePassed;
            File.WriteAllText(Path.Combine(reportDirectory,ready?"ready-structure-report.json":"item-prefab-structure-report.json"),JsonUtility.ToJson(report,true));
            readyStructureDiagnostic=false;
            Application.Quit(report.structurePassed?0:2);
        }
        IEnumerator StructurePrefabRoutine(IEnumerator routine,ItemPrefabScope scope)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            try
            {
                while(stack.Count>0)
                {
                    bool next;object current;
                    try{next=stack.Peek().MoveNext();current=next?stack.Peek().Current:null;}
                    catch(Exception error){scope.error=error.ToString();scope.failures++;break;}
                    if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(current is IEnumerator child&&!(current is CustomYieldInstruction)){stack.Push(child);continue;}
                    scope.coroutineSteps++;scope.lastYield=current is WaitForEndOfFrame?"explicit nonrendered next-frame observer":current==null?"next-frame":current.GetType().FullName;
                    yield return current is WaitForEndOfFrame?null:current;
                }
            }
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
            if(scope.error==null)ReadItemPrefabScope(scope);
            scope.completed=true;
        }
        IEnumerator PrefabStructureCore(StructureReport report)
        {
            StructureRequire(report,Time.captureDeltaTime==0&&Time.timeScale==1&&AudioListener.volume==0&&!NativeAudioRuntime.Muted,"real scaled time and listener-only silent boundary");
            var data=NativeContent.Load<NativeItemPrefabVerificationData>(NativeItemPrefabVerificationData.ResourcePath);
            StructureRequire(report,data&&data.source!=null&&data.source.rows.Length==53&&data.bindings.Length==32&&data.sourceResources.Length==17,"independent typed53 prefab32 resource17 denominator");
            StructureRequire(report,data.sourceBaseline&&ItemPrefabBytesSha256(data.sourceBaseline.bytes)=="8ddca8725fbb908de7f8aa1aef89b5ca4e0df4d2d099f2c40c4cfc6fb2722de5"&&data.baselineSHA256==ItemPrefabBytesSha256(data.sourceBaseline.bytes)
                &&JsonUtility.ToJson(JsonUtility.FromJson<NativeItemPrefabRuntimeVerification.Reference>(data.sourceBaseline.text))==JsonUtility.ToJson(data.source),"original baseline SHA and every typed source field unchanged");
            var resourceBaseline=NativeContent.Load<TextAsset>("NativeVerification/ItemPrefabStructureOriginalResources");
            StructureRequire(report,resourceBaseline&&ItemPrefabBytesSha256(resourceBaseline.bytes)=="73e36762a2635aa760e05bd3d3b9b54e9b837bc12572ab331d05a8dc18427049","independent original17 resource GUID baseline SHA");
            var refs=JsonUtility.FromJson<StructureSourceResources>("{\"rows\":"+resourceBaseline.text+"}").rows;
            StructureRequire(report,refs.Length==17&&refs.Select(row=>row.expectedIdentity).Distinct().Count()==17
                &&refs.All(row=>data.sourceResources.Count(bound=>bound.expectedIdentity==row.expectedIdentity&&bound.sourceGuid==row.sourceGuid&&bound.sourcePath==row.sourcePath)==1),"every original GUID/path/identity matches exported17 bindings");
            report.samples.Add("original17 resource GUID source SHA=73e36762a2635aa760e05bd3d3b9b54e9b837bc12572ab331d05a8dc18427049; actual Unity object identity uses existing exact-resource core; Editor GUID import provenance remains export gate scope");
            report.baselineSHA256=data.baselineSHA256;report.itemPrefab=new ItemPrefabPlayerReport{sourceRows=53,prefabBindings=32,baselineSHA256=data.baselineSHA256,assemblySHA256=report.assemblySHA256};
            var scopes=report.itemPrefab.scopes;var probes=new List<NativeItemPrefabRuntimeVerification>();var jobs=new List<Coroutine>();
            try
            {
                var resource=gameObject.AddComponent<NativeItemPrefabRuntimeVerification>();probes.Add(resource);
                var fields=new ItemPrefabScope{name="resources",evidence="structure-prefab-resource-fields.json"};scopes.Add(fields);
                resource.VerifyResourceBindings(data.source,data.bindings,Path.Combine(reportDirectory,fields.evidence));ReadItemPrefabScope(fields);fields.completed=true;
                VerifyItemPrefabExactResources(data,report.itemPrefab);
                foreach(var binding in data.bindings)
                {
                    var probe=gameObject.AddComponent<NativeItemPrefabRuntimeVerification>();probes.Add(probe);
                    var scope=new ItemPrefabScope{name="lifecycle:"+binding.sourcePath,evidence="structure-prefab-lifecycle-"+scopes.Count+".json"};scopes.Add(scope);
                    jobs.Add(StartCoroutine(StructurePrefabRoutine(probe.VerifyLifecycle(data.source,binding,Path.Combine(reportDirectory,scope.evidence),10.25f),scope)));
                }
                float deadline=Time.realtimeSinceStartup+15;
                while(scopes.Any(s=>!s.completed)&&Time.realtimeSinceStartup<deadline)yield return null;
                StructureRequire(report,scopes.All(s=>s.completed),"all32 actual Update and original lifetime scopes complete within realtime deadline");
                foreach(var binding in data.bindings.Where(b=>data.source.rows.Any(r=>r.sourcePath==b.sourcePath&&r.sourceType=="FXOnDestroy")))
                {
                    var probe=gameObject.AddComponent<NativeItemPrefabRuntimeVerification>();probes.Add(probe);
                    var scope=new ItemPrefabScope{name="destruction:"+binding.sourcePath,evidence="structure-prefab-destruction-"+scopes.Count+".json"};scopes.Add(scope);
                    yield return StructurePrefabRoutine(probe.VerifyDestruction(data.source,binding,Path.Combine(reportDirectory,scope.evidence)),scope);
                }
                foreach(var binding in data.bindings.Where(b=>data.source.rows.Any(r=>r.sourcePath==b.sourcePath&&r.sourceType=="QualityActivator")))
                {
                    var probe=gameObject.AddComponent<NativeItemPrefabRuntimeVerification>();probes.Add(probe);
                    var scope=new ItemPrefabScope{name="quality:"+binding.sourcePath,evidence="structure-prefab-quality-"+scopes.Count+".json"};scopes.Add(scope);
                    yield return StructurePrefabRoutine(probe.VerifyQualityValues(data.source,binding,Path.Combine(reportDirectory,scope.evidence)),scope);
                }
                yield return VerifyItemPrefabWorld(data,report.itemPrefab);
                foreach(var scope in scopes){report.samples.Add(scope.name+" checks="+scope.checks+" failures="+scope.failures+" pending="+scope.pending+" error="+scope.error);StructureRequire(report,scope.completed&&scope.failures==0&&scope.checks>0,"source assertions "+scope.name);}
                report.pending=report.pending.Concat(scopes.Where(scope=>scope.pending>0).Select(scope=>scope.name+": "+scope.pending+" source branches pending; "+scope.error)).ToArray();
                report.itemPrefab.pending=scopes.Sum(s=>s.pending);report.itemPrefab.failures=scopes.Sum(s=>s.failures);
            }
            finally{foreach(var job in jobs)if(job!=null)StopCoroutine(job);foreach(var probe in probes)if(probe)Destroy(probe);}
        }
        IEnumerator ReadyStructureCore(StructureReport report,List<InputDevice> inputs)
        {
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            var baseline=NativeContent.Load<TextAsset>("NativeVerification/ReadyStructureOriginalSource");
            StructureRequire(report,baseline&&ItemPrefabBytesSha256(baseline.bytes)=="dad4fbbc641c058d4fec340be9be82f75049b8e904f0652b47ba480a6a2569cd","independent Ready original source baseline hash");
            report.baselineSHA256="dad4fbbc641c058d4fec340be9be82f75049b8e904f0652b47ba480a6a2569cd";
            yield return PrepareInjectedLocalHumansForRace(4,inputs);
            var keyboard=inputs.OfType<Keyboard>().Single();var pads=inputs.OfType<Gamepad>().ToArray();
            hud.PrepareStructureResultsUi();
            for(int branch=0;branch<2;branch++)
            {
                selectedMode=NativeSessionMode.QuickRace;Begin();
                float deadline=Time.realtimeSinceStartup+10;
                while((phase!=Phase.Racing||!sourceRanking.Started)&&Time.realtimeSinceStartup<deadline)yield return null;
                StructureRequire(report,phase==Phase.Racing&&sourceRaceState.InRace&&sourceRanking.Started,"real Begin countdown start branch "+branch);
                var state=sourceRaceState;var router=sourceHumanInput;
                StructureRequire(report,router&&ReferenceEquals(router.State,state)&&state.Humans.Count(h=>h.Joined&&h.RacerValid)==4,"same production source state/router with four real owners");
                var injectedArrivalOrder=sourceRanking.SnapshotRanks();
                report.samples.Add("NativeInjected arrivals follow actual rank snapshot: "+string.Join(",",injectedArrivalOrder));
                foreach(int racer in injectedArrivalOrder)
                {
                    if(state.EndedRace)break;
                    var prefix=sourceResultAppend.PrefixSnapshot();var ranks=sourceRanking.SnapshotRanks();
                    report.samples.Add("before injected finish racer="+racer+" prefix="+string.Join(",",prefix)+" ranks="+string.Join(",",ranks));
                    StructureRequire(report,prefix.SequenceEqual(ranks.Take(prefix.Length)),"injected finish prefix matches actual rank tail contract branch "+branch+" racer "+racer);
                    while(!progress[racer].Finished)progress[racer].Cross(progress[racer].Target,true);
                    RecordSourceNaturalFinish(racer);
                }
                StructureRequire(report,state.EndedRace&&resultsFlow.State==NativeResultsFlow.Screen.Waiting,"production EndRacer AutoComplete station and results subscribed");
                bool scoreboard=branch==1;
                if(!scoreboard)
                {
                    var rows=resultsFlow.Rows.Select(row=>new NativeResultsRowSnapshot(row.RacerIndex,row.HumanIndex,row.Place,row.PriorScore,row.GainedScore,row.Seconds)).ToArray();
                    resultsFlow.Begin(state.RaceId,NativeSessionMode.TimeTrials,rows,0,1,.05f);
                    report.samples.Add("NativeInjected ShowsScoreboard=false branch on completed four-human QuickRace snapshot; natural TT four-human reachability open");
                }
                int submitted=0,readyChanged=0,flowFinish=0,modeFinish=0,raceFinish=0;var slots=new List<int>();
                Action<int> onReady=slot=>{readyChanged++;slots.Add(slot);};Action<int> onSubmitted=slot=>submitted++;
                Action<NativeResultsDestination> onFlow=destination=>flowFinish++;Action onMode=()=>modeFinish++;Action onFinish=()=>raceFinish++;
                state.ReadyChanged+=onReady;state.ReadySubmitted+=onSubmitted;resultsFlow.FinishGameRequested+=onFlow;SourceModeFinishedObserved+=onMode;SourceRaceFinishObserved+=onFinish;
                Action<PlayerInput,string,int> producer=(owner,action,device)=>{if(action=="Ok")report.samples.Add("actual Ok producer branch="+branch+" userId="+owner.user.id+" userIndex="+owner.user.index+" device="+device+" frame="+Time.frameCount);};
                NativeLocalSelectionOwners.Instance.StartedObserved+=producer;
                try
                {
                    yield return LocalPress(keyboard,pads,0,false);
                    StructureRequire(report,readyChanged==0&&submitted==0&&flowFinish==0,"actual Ok before cards leaves Ready unopened branch "+branch);
                    deadline=Time.realtimeSinceStartup+5;
                    while(resultsFlow.State!=NativeResultsFlow.Screen.Cards&&Time.realtimeSinceStartup<deadline)yield return null;
                    StructureRequire(report,resultsFlow.State==NativeResultsFlow.Screen.Cards&&state.ShowsScoreboard==scoreboard&&Enumerable.Range(0,4).All(SourceCardActive),"actual three-second cards and branch "+branch);
                    StructureRequire(report,hud.SourceReadyAlphas().All(alpha=>alpha==0),"actual ready alpha0 branch "+branch);
                    uint[] ownerIds=Enumerable.Range(0,4).Select(slot=>SourceActualLocalOwner(slot).user.id).ToArray();
                    for(int slot=0;slot<4;slot++)
                    {
                        yield return LocalPress(keyboard,pads,slot,false);
                        if(scoreboard){StructureRequire(report,readyChanged==1&&submitted==1&&resultsFlow.State==NativeResultsFlow.Screen.Scoreboard&&state.Humans.Where(h=>h.Joined).All(h=>h.Selected==NativeRaceStateFlow.Selection.Scoreboard),"first real Ok routes true branch to scoreboard");break;}
                        if(slot<3)StructureRequire(report,readyChanged==slot+1&&submitted==slot+1&&flowFinish==0&&hud.SourceReadyAlphas()[slot]==1,"real own InputUser Ok alpha1 waits remaining "+slot);
                        if(slot==0){yield return LocalPress(keyboard,pads,0,false);StructureRequire(report,readyChanged==1&&submitted==1&&flowFinish==0,"repeat real Ok on ready card produces no duplicate ready");}
                    }
                    if(scoreboard)
                    {
                        StructureRequire(report,hud.SourceReadyAlphas()[0]==1&&!Enumerable.Range(0,4).Any(SourceCardActive),"true branch first alpha1 and cards hidden");
                        yield return LocalPress(keyboard,pads,1,false);
                    }
                    if(!scoreboard)StructureRequire(report,readyChanged==4&&submitted==4&&slots.SequenceEqual(new[]{0,1,2,3})&&hud.SourceReadyAlphas().All(alpha=>alpha==1),"all four real owner ready events and retained alpha1");
                    StructureRequire(report,flowFinish==1&&modeFinish==1&&raceFinish==1&&!state.InRace&&resultsFlow.State==NativeResultsFlow.Screen.Closed,"real FinishSourceGame consumer once cleanup branch "+branch);
                    StructureRequire(report,ReferenceEquals(router.State,state)&&ownerIds.SequenceEqual(Enumerable.Range(0,4).Select(slot=>NativeLocalSelectionOwners.Instance.AtSlot(slot).user.id)),"production same router state and stable owners retained after finish");
                    if(scoreboard)
                    {
                        var returnedOwners=NativeLocalSelectionOwners.Instance.Players;
                        int[] returnedIds=returnedOwners.Select(owner=>checked((int)owner.user.id)).ToArray();
                        report.samples.Add("production scoreboard finish returned ownerIds="+string.Join(",",returnedIds)+" joinedCount="+(localSelection==null?-1:localSelection.JoinedCount));
                        StructureRequire(report,localSelectionActive&&localSelection!=null&&localSelection.JoinedCount==4&&returnedIds.Length==4&&Enumerable.Range(0,4).All(slot=>localSelection.ReadBox(slot)!=null&&localSelection.ReadBox(slot).InputUserId==returnedIds[slot]),"production FinishSourceGame selection replay retains all four owners in actual PlayerInput order");
                    }
                    report.samples.Add("branch="+branch+" readyChanged="+readyChanged+" readySubmitted="+submitted+" slots="+string.Join(",",slots)+" flowFinish="+flowFinish+" modeFinish="+modeFinish+" raceFinish="+raceFinish);
                }
                finally{NativeLocalSelectionOwners.Instance.StartedObserved-=producer;state.ReadyChanged-=onReady;state.ReadySubmitted-=onSubmitted;resultsFlow.FinishGameRequested-=onFlow;SourceModeFinishedObserved-=onMode;SourceRaceFinishObserved-=onFinish;}
            }
#else
            throw new InvalidOperationException("Actual InputSystem owner diagnostic requires Player build");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
#endif
        }
    }
    public sealed partial class NativeRaceHud
    {
        internal void PrepareStructureResultsUi()
        {EnsureAuthoredMenus();}
    }
}
