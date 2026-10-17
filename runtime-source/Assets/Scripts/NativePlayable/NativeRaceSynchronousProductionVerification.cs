using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed class NativeGateProductionReceipt : MonoBehaviour
    {
        public int entries, exits;
        public readonly HashSet<int> motors = new HashSet<int>();
        readonly Dictionary<int,int> actorEntries = new Dictionary<int,int>(), actorExits = new Dictionary<int,int>();
        public int ActorEntries(int id) => actorEntries.TryGetValue(id,out int value) ? value : 0;
        public int ActorExits(int id) => actorExits.TryGetValue(id,out int value) ? value : 0;
        void OnTriggerEnter(Collider other) { var motor=other.GetComponent<NativeKartMotor>(); if(motor){entries++;int id=motor.GetInstanceID();actorEntries[id]=ActorEntries(id)+1;} }
        void OnTriggerExit(Collider other) { var motor=other.GetComponent<NativeKartMotor>(); if(motor){exits++;int id=motor.GetInstanceID();motors.Add(id);actorExits[id]=ActorExits(id)+1;} }
    }
    public sealed partial class NativeRace
    {
        [Serializable] sealed class SynchronousEvent
        { public string kind,phase; public float scaledTime; public int racer,frame,currentLap,appendCount,pendingCount; public bool ended,humanDrive,aiEnabled,aiActive; public double total,lastTimestamp,cachedGameTime; }
        [Serializable] sealed class SynchronousLapRow { public int racer; public double[] laps; }
        [Serializable] sealed class SynchronousProductionReport
        {
            public bool passed,accepted; public string scope; public string[] checks; public SynchronousEvent[] events;
            public int[] order,gateEntries,gateExits; public SynchronousLapRow[] frozenLaps; public double[] totals; public float[] displacement;
        }
        [Serializable] sealed class GatePhysicalContactDiagnostic
        {
            public int racer,gate,actorId,controllerId,colliderId,frame,sceneHandle,entryDelta,exitDelta;
            public Vector3 triggerCenter,triggerSize,triggerScale,controllerCenter,enteredPosition,exitedPosition,
                enteredBoundsCenter,enteredBoundsSize,exitBoundsCenter,exitBoundsSize;
            public float controllerRadius,controllerHeight,minMoveDistance,startFixedTime,insideFixedTime,endFixedTime;
            public int startFrame,insideFrame;
            public bool startEnabled,insideEnabled,endEnabled;
            public Vector3 startBoundsCenter,localInsideCenter,localEndCenter,gateBoundsCenter,gateBoundsSize,attemptedMove,insideMoveDelta,exitMoveDelta;
            public float gateHalfDepth,projectedCapsuleSupport;
            public int insideMoveFlags,exitMoveFlags;
            public bool enterBoundsIntersect,exitBoundsIntersect;
        }
        IEnumerator PassProductionGate(int racer,int gate)
        {
            var motor=racers[racer]; var trigger=gates[gate].GetComponent<BoxCollider>();
            var controller=motor.GetComponent<CharacterController>();
            var receipt = trigger.GetComponent<NativeGateProductionReceipt>();
            int actorId = motor.GetInstanceID();
            bool originalEnabled = motor.enabled;
            motor.enabled=false;
            var recovery = motor.GetComponent<NativeKartRecovery>();
            try
            {
                yield return DrainPhysicalGateRecovery(motor,recovery,gate,"before-layout");
                if (!controller || !controller.enabled) throw new InvalidOperationException("Physical source actor requires its enabled CharacterController");
                int entriesBefore = receipt ? receipt.ActorEntries(actorId) : -1, exitsBefore = receipt ? receipt.ActorExits(actorId) : -1;
                var startBounds = controller.bounds; int startFrame = Time.frameCount; float startFixed = Time.fixedTime;
                bool startEnabled = controller.enabled;
                int attemptTeleports = recovery ? recovery.State.Teleports : -1;
                int recoveryTeleportsBefore = attemptTeleports;
            var center=trigger.transform.TransformPoint(trigger.center);
            motor.transform.rotation=trigger.transform.rotation;
            motor.transform.position=center-(controller?motor.transform.TransformVector(controller.center):Vector3.zero);
            Physics.SyncTransforms();
            var direction = trigger.transform.TransformVector(Vector3.forward).normalized;
            float distance = Mathf.Max(controller.minMoveDistance * 2f, .001f);
            var attemptedMove = direction * distance;
            float halfDepth = trigger.transform.TransformVector(Vector3.forward * trigger.size.z * .5f).magnitude;
            var extents = controller.bounds.extents;
            float support = Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y + Mathf.Abs(direction.z) * extents.z;
            if (halfDepth <= support + distance * 2f)
                throw new InvalidOperationException("Original gate interior lacks actual capsule support margin: gate=" + gate + " halfDepth=" + halfDepth + " support=" + support + " move=" + distance);
            // 超过原胶囊最小移动阈值，保持位移落在原检查点内部裕量中。
            var beforeInsideMove = motor.transform.position;
            var insideFlags = controller.Move(attemptedMove);
            var insideDelta = motor.transform.position - beforeInsideMove;
            Physics.SyncTransforms(); yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            int insideFrame=Time.frameCount;float insideFixed=Time.fixedTime;bool insideEnabled=controller.enabled;
            var enterPosition = motor.transform.position;
            var enterBounds = controller.bounds;
            bool enterIntersects = trigger.bounds.Intersects(enterBounds);
            motor.transform.position+=trigger.transform.TransformVector(Vector3.forward*(trigger.size.z*.5f+10f));
            Physics.SyncTransforms();
            var beforeExitMove = motor.transform.position;
            var exitFlags = controller.Move(attemptedMove);
            var exitDelta = motor.transform.position - beforeExitMove;
            Physics.SyncTransforms(); yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                if (recovery && recovery.State.Teleports != attemptTeleports)
                    throw new InvalidOperationException("Natural recovery interrupted gate contact attempt actor="+actorId+" gate="+gate);
            Debug.Log("NATIVE_GATE_PHYSICAL_CONTACT " + JsonUtility.ToJson(new GatePhysicalContactDiagnostic {
                racer=racer, gate=gate, actorId=motor.GetInstanceID(), controllerId=controller.GetInstanceID(),
                colliderId=trigger.GetInstanceID(), frame=Time.frameCount, sceneHandle=gameObject.scene.handle,
                triggerCenter=center, triggerSize=trigger.size, triggerScale=trigger.transform.lossyScale,
                controllerCenter=controller.center, controllerRadius=controller.radius, controllerHeight=controller.height,
                minMoveDistance=controller.minMoveDistance,startFrame=startFrame,insideFrame=insideFrame,
                attemptedMove=attemptedMove,insideMoveDelta=insideDelta,exitMoveDelta=exitDelta,
                gateHalfDepth=halfDepth,projectedCapsuleSupport=support,insideMoveFlags=(int)insideFlags,exitMoveFlags=(int)exitFlags,
                startFixedTime=startFixed,insideFixedTime=insideFixed,endFixedTime=Time.fixedTime,
                startEnabled=startEnabled,insideEnabled=insideEnabled,endEnabled=controller.enabled,
                startBoundsCenter=startBounds.center,localInsideCenter=trigger.transform.InverseTransformPoint(enterBounds.center),
                localEndCenter=trigger.transform.InverseTransformPoint(controller.bounds.center),
                gateBoundsCenter=trigger.bounds.center,gateBoundsSize=trigger.bounds.size,
                enteredPosition=enterPosition, exitedPosition=motor.transform.position,
                enteredBoundsCenter=enterBounds.center, enteredBoundsSize=enterBounds.size,
                exitBoundsCenter=controller.bounds.center, exitBoundsSize=controller.bounds.size,
                enterBoundsIntersect=enterIntersects, exitBoundsIntersect=trigger.bounds.Intersects(controller.bounds),
                entryDelta=receipt ? receipt.ActorEntries(actorId)-entriesBefore : -1, exitDelta=receipt ? receipt.ActorExits(actorId)-exitsBefore : -1
            }));
                yield return DrainPhysicalGateRecovery(motor,recovery,gate,"after-contact");
                Debug.Log("NATIVE_GATE_RECOVERY_INTERVAL actor="+motor.GetInstanceID()+" gate="+gate
                    +" teleportsBefore="+recoveryTeleportsBefore+" teleportsAfter="+(recovery?recovery.State.Teleports:-1)
                    +" originalMotorEnabled="+originalEnabled);
            }
            finally { if(motor)motor.enabled=originalEnabled; }
        }
        IEnumerator DrainPhysicalGateRecovery(NativeKartMotor motor,NativeKartRecovery recovery,int gate,string boundary)
        {
            if (!recovery) yield break;
            int beforeTeleports=recovery.State.Teleports;
            bool pendingBefore=recovery.Pending;
            double deadline=Time.realtimeSinceStartupAsDouble+2;
            while(recovery.Pending)
            {
                if(Time.realtimeSinceStartupAsDouble>=deadline)
                    throw new InvalidOperationException("Natural recovery drain timeout actor="+motor.GetInstanceID()+" gate="+gate+" boundary="+boundary);
                if(!recovery.enabled)
                    throw new InvalidOperationException("Pending natural recovery component disabled actor="+motor.GetInstanceID());
                yield return null;
            }
            if(pendingBefore) { yield return new WaitForFixedUpdate(); yield return null; }
            if(recovery.Pending)
                throw new InvalidOperationException("Natural recovery became pending during stable boundary actor="+motor.GetInstanceID()+" gate="+gate+" boundary="+boundary);
            Debug.Log("NATIVE_GATE_RECOVERY_DRAIN " + JsonUtility.ToJson(new GateRecoveryDrainDiagnostic {
                actorId=motor.GetInstanceID(),gate=gate,boundary=boundary,frame=Time.frameCount,fixedTime=Time.fixedTime,
                pendingBefore=pendingBefore,pendingAfter=recovery.Pending,recoveryEnabled=recovery.enabled,motorEnabled=motor.enabled,
                clock=recovery.State.Clock,deadline=recovery.State.Deadline,requests=recovery.State.Requests,
                teleportsBefore=beforeTeleports,teleportsAfter=recovery.State.Teleports,position=motor.transform.position
            }));
        }
        [Serializable] sealed class GateRecoveryDrainDiagnostic
        {
            public string boundary;
            public int actorId,gate,frame,requests,teleportsBefore,teleportsAfter;
            public double clock,deadline;
            public float fixedTime;
            public bool pendingBefore,pendingAfter,recoveryEnabled,motorEnabled;
            public Vector3 position;
        }
        internal IEnumerator CheckSynchronousProductionLive()
        {
            var stack=new Stack<IEnumerator>(); Exception failure=null;
            try { Directory.CreateDirectory(reportDirectory); stack.Push(CheckSynchronousProductionBody()); }
            catch(Exception error) { failure=error; }
            while(failure==null && stack.Count>0)
            {
                bool moved=false; object current=null;
                try
                {
                    var iterator=stack.Peek(); moved=iterator.MoveNext();
                    if(moved)current=iterator.Current;
                    else { stack.Pop(); (iterator as IDisposable)?.Dispose(); }
                }
                catch(Exception error) { failure=error; }
                if(failure!=null)break;
                if(!moved)continue;
                if (current is IEnumerator nested && !(current is CustomYieldInstruction))stack.Push(nested);
                else yield return current;
            }
            while(stack.Count>0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch(Exception error) { failure=failure==null?error:new AggregateException(failure,error); }
            }
            if(failure!=null)
            {
                Debug.LogError("NATIVE_RANK_TIME_PRODUCTION_EXCEPTION "+failure);
                try { File.WriteAllText(Path.Combine(reportDirectory,"rank-time-production-report.json"),JsonUtility.ToJson(new SynchronousProductionReport {
                    passed=false,accepted=false,scope="Injected placement / actual production trigger. Exception aborted the production receipt.",
                    checks=new[]{"FAIL managed iterator exception: "+failure},events=new SynchronousEvent[0],order=new int[0]},true)); }
                catch(Exception reportError) { Debug.LogError("NATIVE_RANK_TIME_REPORT_EXCEPTION "+reportError); }
                Application.Quit(2);
            }
        }
        IEnumerator CheckSynchronousProductionBody()
        {
            var checks=new List<string>(); var events=new List<SynchronousEvent>(); bool passed=true;
            Action<bool,string> check=(ok,name)=>{passed&=ok;checks.Add((ok?"PASS ":"FAIL ")+name);};
            yield return null;
            selectedMode=NativeSessionMode.QuickRace; selectedTrackId=trackId; autopilot=false;
            var config=new NativeSessionConfig {mode=NativeSessionMode.QuickRace,modeId="quick-race",laps=1,racerCount=3,itemType="none",trackIds=new List<string>{trackId}};
            for(int i=0;i<3;i++)config.roster.Add(new NativeSessionSeat {seatId="sync-seat-"+i,characterId=vehicleRoster.CharacterIdAt(selectedCharacterIndex),vehicleId="kart",skinIndex=0,isHuman=i<2});
            sessionId=NativeGameSession.Create(config);
            Begin(); // Actual roster, source circuit, timers, append callbacks and countdown.
            var receipts=gates.Select(g=>g.gameObject.AddComponent<NativeGateProductionReceipt>()).ToArray();
            Action<int,string> capture=(racer,kind)=>{
                var ai=racers[racer].GetComponent<NativeKartOriginalAI>();
                events.Add(new SynchronousEvent {kind=kind,racer=racer,scaledTime=Time.time,frame=Time.frameCount,phase=phase.ToString(),currentLap=sourceLapTimers[racer].CurrentLap,
                    ended=sourceLapTimers[racer].Ended,humanDrive=SourceHumanDrivingEnabled(racer),aiEnabled=ai.enabled,aiActive=ai.active,
                    appendCount=sourceResultAppend.Count,pendingCount=NativeGameSession.Current.pending.Count,total=SourceTotalSeconds(racer),cachedGameTime=sourceGameTime,
                    lastTimestamp=(double)typeof(NativeSourceLapTimer).GetField("lastTimestamp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(sourceLapTimers[racer])});
            };
            Action<int> ended=r=>capture(r,"ended-before-base"); Action<int> takeover=r=>capture(r,"takeover-after-base");
            SourceRacerEndedObserved+=ended; SourceHumanAiTakeoverObserved+=takeover;
            double[] totals=new double[3]; double[][] laps=new double[3][]; float[] displacement=new float[3]; int[] order=new int[0];
            try
            {
                double deadline=Time.realtimeSinceStartupAsDouble+8;
                while(phase!=Phase.Racing&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                check(phase==Phase.Racing&&sourceRanking.Started&&raceClock.Running,"real countdown started source rank and timers");
                var startedTimestamps=sourceLapTimers.Select(timer=>(double)typeof(NativeSourceLapTimer).GetField("lastTimestamp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(timer)).ToArray();
                foreach(var motor in racers)motor.enabled=false;
                yield return new WaitForSeconds(.12f);
                for(int i=0;i<2;i++)
                {
                    for(int gate=0;gate<gates.Length;gate++)yield return PassProductionGate(i,gate);
                    yield return PassProductionGate(i,0);
                    check(progress[i].Finished&&sourceLapTimers[i].Ended,"physical exits ended human "+i);
                    totals[i]=SourceTotalSeconds(i);laps[i]=sourceLapTimers[i].LapSeconds();
                    check(totals[i]>0&&laps[i].Length==1&&laps[i][0]==totals[i],"source frozen one-lap total "+i);
                    if(i==0)check(phase==Phase.Racing&&!sourceLapTimers[1].Ended&&sourceResultAppend.Count==1,"first human return leaves second racing and one published prefix");
                }
                var firstEnd=events.FirstOrDefault(e=>e.racer==0&&e.kind=="ended-before-base");
                var firstAi=events.FirstOrDefault(e=>e.racer==0&&e.kind=="takeover-after-base");
                var lastAi=events.FirstOrDefault(e=>e.racer==1&&e.kind=="takeover-after-base");
                check(firstEnd!=null&&firstEnd.lastTimestamp==startedTimestamps[0],"synchronous End observes old source lastTimestamp before gate refresh");
                check(firstEnd!=null&&firstEnd.ended&&firstEnd.currentLap==1&&firstEnd.humanDrive,"ended event before human controller takeover");
                check(firstAi!=null&&!firstAi.humanDrive&&firstAi.aiEnabled&&firstAi.aiActive&&firstAi.phase=="Racing","first human actual AI enabled after base returns");
                check(lastAi!=null&&lastAi.phase=="Finished"&&lastAi.appendCount==3&&lastAi.pendingCount==3&&!lastAi.humanDrive,"last human base synchronously fills publisher before AI return");
                check(phase==Phase.Finished&&sourceRaceState.EndedRace&&sourceResultAppend.Count==3,"synchronous production Results started");
                if(sourceResultAppend.Count==3){order=SourceResultOrderSnapshot();check(order.Length==3&&order.Distinct().Count()==3,"source append complete exact permutation");}
                check(NativeGameSession.Current.pending.Count==3&&NativeGameSession.Current.pending.Select(x=>x.place).OrderBy(x=>x).SequenceEqual(new[]{1,2,3}),"session publisher complete unique places");
                int baseline=events.Count; yield return PassProductionGate(0,0); yield return PassProductionGate(1,0);
                check(events.Count==baseline&&sourceResultAppend.Count==3&&NativeGameSession.Current.pending.Count==3,"repeat physical finish exits produce no duplicate End or published row");
                for(int i=0;i<3;i++)racers[i].enabled=true;
                var positions=racers.Select(m=>m.transform.position).ToArray();
                yield return new WaitForSeconds(.75f);
                for(int i=0;i<3;i++){displacement[i]=Vector3.Distance(positions[i],racers[i].transform.position);check(displacement[i]>.01f&&racers[i].GetComponent<NativeKartOriginalAI>().active&&racers[i].driving,"actual ongoing AI displacement actor "+i);}
                check(SourceTotalSeconds(0)==totals[0]&&SourceTotalSeconds(1)==totals[1]&&SourceTotalSeconds(2)==0,"human arrays stay frozen and CPU time retains original zero producer");
                deadline=Time.realtimeSinceStartupAsDouble+5;
                while(resultsFlow.State!=NativeResultsFlow.Screen.Cards&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                check(lastAi!=null && Time.time-lastAi.scaledTime>=3f,"source three scaled seconds elapsed before Cards observation");
                check(events.Count(e=>e.racer==0&&e.kind=="ended-before-base")==1&&events.Count(e=>e.racer==1&&e.kind=="ended-before-base")==1&&events.Count(e=>e.kind=="takeover-after-base")==2,"each human production End and takeover callback exactly once");
                check(resultsFlow.State==NativeResultsFlow.Screen.Cards&&hud.SourceCardActive(0)&&hud.SourceCardActive(1),"actual delayed two-human production Cards active");
                check(resultsFlow.Rows.Count==3&&resultsFlow.Rows.All(row=>row.Seconds==SourceTotalSeconds(row.RacerIndex)),"CardsOpened reads exact source total fields");
                foreach(var receipt in receipts)check(receipt.entries>=2&&receipt.exits>=2&&receipt.motors.Contains(racers[0].GetInstanceID())&&receipt.motors.Contains(racers[1].GetInstanceID()),"actual native gate physics contact "+receipt.name);
            }
            finally {SourceRacerEndedObserved-=ended;SourceHumanAiTakeoverObserved-=takeover;foreach(var motor in racers)motor.enabled=true;}
            File.WriteAllText(Path.Combine(reportDirectory,"rank-time-production-report.json"),JsonUtility.ToJson(new SynchronousProductionReport {passed=passed,accepted=false,
                scope="Injected transform placement through actual scene trigger physics exits; real Begin/countdown/source timers/append/session/EndRace AI callbacks and motion. Two-human QuickRace only. Natural driving, device join, TT/GP modefinish persistence and paired visual acceptance remain separate.",
                checks=checks.ToArray(),events=events.ToArray(),order=order,gateEntries=receipts.Select(x=>x.entries).ToArray(),gateExits=receipts.Select(x=>x.exits).ToArray(),totals=totals,frozenLaps=laps.Select((values,index)=>new SynchronousLapRow {racer=index,laps=values}).ToArray(),displacement=displacement},true));
            Debug.Log("NATIVE_SYNCHRONOUS_PRODUCTION "+(passed?"PASS":"FAIL"));
            Application.Quit(passed?0:2);
        }
    }
}
