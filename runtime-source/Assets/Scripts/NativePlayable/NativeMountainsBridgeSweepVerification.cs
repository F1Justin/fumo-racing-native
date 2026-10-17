using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeMountainsBridgeSweepVerification
    {
        [Serializable] public sealed class Trial
        {
            public string name, error;
            public bool passed, recovery, reachedExit, expectedWater, carriedChildExcluded;
            public float seconds, entrySpeed, peakSpeed;
            public Vector3 start, finish;
            public string itemId;
            public string[] carriedColliders;
            public NativeBridgeSweepTrace.Move[] moves;
            public NativeBridgeSweepTrace.Trigger[] triggers;
        }
        [Serializable] public sealed class Report
        {
            public bool passed;
            public string scope="Injected initial placement and scripted throttle/steering on original route; real ordinary Update Motor.Move, all scenery and trigger colliders retained. No production lap/results or physical input acceptance.";
            public string error;
            public int sourceRouteStartIndex, originalColliderCount, retainedColliderCount;
            public Trial[] trials;
        }
        public static IEnumerator Run(NativeRace race,string directory)
        {
            Directory.CreateDirectory(directory);
            var report=new Report();var trials=new List<Trial>();
            var colliders=UnityEngine.Object.FindObjectsOfType<Collider>(true);
            report.originalColliderCount=colliders.Length;
            bool previousEnabled=race.enabled;float oldScale=Time.timeScale, oldCaptureDelta=Time.captureDeltaTime;
            string stepArg=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--bridge-sweep-step="));
            if(stepArg!=null)Time.captureDeltaTime=float.Parse(stepArg.Substring("--bridge-sweep-step=".Length),System.Globalization.CultureInfo.InvariantCulture);
            var ai=race.racers.Select(r=>r.GetComponent<NativeKartOriginalAI>()).ToArray();
            var oldAi=ai.Select(a=>a.enabled).ToArray();var oldMotors=race.racers.Select(r=>r.enabled).ToArray();
            var motor=race.racers[0];
            try
            {
                if(race.trackId!="mountains")throw new InvalidOperationException("Bridge sweep requires mountains source scene");
                race.SetVisualCapture("race",true);race.ResetRace(false);race.SetVisualCapture("race",true);race.enabled=false;Time.timeScale=1;
                for(int i=0;i<ai.Length;i++){ai[i].enabled=false;if(i>0)race.racers[i].enabled=false;}
                motor.gameObject.SetActive(true);motor.enabled=true;
                Debug.Log("NATIVE_BRIDGE_SWEEP_STARTED active="+motor.gameObject.activeInHierarchy);
                var bike=race.vehicleRoster.vehicles.First(v=>v.id=="Bike");motor.stats=bike.settings;
                int waypoint=Enumerable.Range(0,race.route.Length).OrderBy(i=>(race.route[i]-new Vector3(231.92f,15.14f,438.88f)).sqrMagnitude).First();
                report.sourceRouteStartIndex=waypoint-2;
                string carryArg=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--bridge-sweep-carry="));
                var inventory=motor.GetComponent<NativeKartItemInventory>();
                foreach(string itemId in carryArg==null?new[]{""}:carryArg.Substring("--bridge-sweep-carry=".Length).Split(','))
                foreach(float lane in Environment.GetCommandLineArgs().Contains("--bridge-sweep-all-lanes")?new[]{0f,-4f,4f,9f,-9f}:new[]{0f})
                {
                    var trial=new Trial {name="source-route-lane-"+lane+"-carry-"+itemId,itemId=itemId};trials.Add(trial);
                    Vector3 forward=Vector3.ProjectOnPlane(race.route[waypoint-1]-race.route[waypoint-2],Vector3.up).normalized;
                    Vector3 right=Vector3.Cross(Vector3.up,forward);
                    Vector3 start=race.route[waypoint-2]+right*lane;
                    if(!Ground(start,false,out var ground)) {trial.error="Original source route start has no dry support";continue;}
                    start.y=ground.point.y+.215f;trial.start=start;
                    motor.GetComponent<NativeKartRecovery>().ResetState();NativeBridgeSweepTrace.Bind(motor);
                    motor.ResetAt(start,Quaternion.LookRotation(forward),false);motor.driving=true;
                    inventory.ResetItems();inventory.SetFrozen(false);
                    if(itemId.Length>0)
                    {
                        if(!inventory.TryGive(itemId,false)||!inventory.UseItem()) {trial.error="Real inventory carry failed: "+itemId;continue;}
                        yield return null;
                        var actors=motor.GetComponentsInChildren<NativeItemActor>(true).Where(a=>a.Carried).ToArray();
                        if(actors.Length==0){trial.error="Real item did not attach: "+itemId;continue;}
                        trial.carriedColliders=actors.SelectMany(a=>a.GetComponentsInChildren<Collider>(true)).Select(c=>c.name+" type="+c.GetType().Name+" tag="+c.tag+" enabled="+c.enabled+" active="+c.gameObject.activeInHierarchy+" center="+c.bounds.center+" extents="+c.bounds.extents+" rigidbody="+(c.attachedRigidbody?c.attachedRigidbody.name:"none")).ToArray();
                    }
                    Physics.SyncTransforms();
                    int target=waypoint-1;float began=Time.realtimeSinceStartup;
                    while(Time.realtimeSinceStartup-began<14f)
                    {
                        Vector3 point=race.route[target]+right*lane;
                        Vector3 direction=Vector3.ProjectOnPlane(point-motor.transform.position,Vector3.up);
                        if(direction.magnitude<7f&&target<waypoint+2){target++;continue;}
                        motor.throttle=1f;motor.steering=Mathf.Clamp(Vector3.SignedAngle(motor.transform.forward,direction,Vector3.up)/35f,-1,1);
                        motor.braking=motor.drifting=motor.jump=motor.jumpHeld=false;
                        yield return null;
                        trial.peakSpeed=Mathf.Max(trial.peakSpeed,motor.Speed);
                        if(trial.entrySpeed==0&&motor.transform.position.z<=450f)trial.entrySpeed=motor.Speed;
                        if(motor.GetComponent<NativeKartRecovery>().Pending){trial.recovery=true;break;}
                        if(target==waypoint+2&&direction.magnitude<9f){trial.reachedExit=true;break;}
                    }
                    trial.seconds=Time.realtimeSinceStartup-began;trial.finish=motor.transform.position;
                    trial.moves=NativeBridgeSweepTrace.Moves.ToArray();trial.triggers=NativeBridgeSweepTrace.Triggers.ToArray();
                    trial.carriedChildExcluded=trial.triggers.Any(t=>t.legacyParentWouldRecover&&!t.directPlayerWouldRecover);
                    bool expectsChild=new[]{"YinYang","YellowStar","YinYangHoming"}.Contains(itemId);
                    trial.passed=trial.reachedExit&&!trial.recovery&&(!expectsChild||trial.carriedChildExcluded);
                    if(!trial.passed)trial.error=trial.recovery?"Water/fall recovery during source bridge route":!trial.reachedExit?"Source bridge traversal deadline":"Expected real carried collider water contact missing";
                    motor.driving=false;motor.throttle=0;inventory.ResetItems();NativeBridgeSweepTrace.Motor=null;
                    File.WriteAllText(Path.Combine(directory,"bridge-sweep-"+trials.Count+".json"),JsonUtility.ToJson(trial,true));
                    yield return null;
                }
                if(Environment.GetCommandLineArgs().Contains("--bridge-sweep-include-water"))
                {
                var waterTrial=new Trial {name="source-water-positive",expectedWater=true};trials.Add(waterTrial);
                var volume=UnityEngine.Object.FindObjectsOfType<NativeTrackFallVolume>().First(v=>v.water&&v.name=="waterfollTrigger");
                Vector3 inside=volume.GetComponent<Collider>().bounds.center;
                waterTrial.start=inside;
                motor.GetComponent<NativeKartRecovery>().ResetState();NativeBridgeSweepTrace.Bind(motor);
                motor.ResetAt(inside,Quaternion.identity,false);motor.driving=true;motor.throttle=motor.steering=0;
                Physics.SyncTransforms();float waterBegan=Time.realtimeSinceStartup;
                while(Time.realtimeSinceStartup-waterBegan<3f&&!motor.GetComponent<NativeKartRecovery>().Pending)yield return null;
                waterTrial.recovery=motor.GetComponent<NativeKartRecovery>().Pending;waterTrial.passed=waterTrial.recovery;
                waterTrial.seconds=Time.realtimeSinceStartup-waterBegan;waterTrial.finish=motor.transform.position;
                waterTrial.moves=NativeBridgeSweepTrace.Moves.ToArray();waterTrial.triggers=NativeBridgeSweepTrace.Triggers.ToArray();
                if(!waterTrial.passed)waterTrial.error="Actual source water volume did not request recovery";
                }
            }
            finally
            {
                motor.driving=false;NativeBridgeSweepTrace.Motor=null;
                for(int i=0;i<ai.Length;i++){ai[i].enabled=oldAi[i];race.racers[i].enabled=oldMotors[i];}
                race.enabled=previousEnabled;Time.timeScale=oldScale;Time.captureDeltaTime=oldCaptureDelta;
                report.trials=trials.ToArray();report.retainedColliderCount=colliders.Count(c=>c);
                report.passed=trials.Count>0&&trials.All(t=>t.passed)&&report.retainedColliderCount==report.originalColliderCount;
                File.WriteAllText(Path.Combine(directory,"bridge-sweep-report.json"),JsonUtility.ToJson(report,true));
                Debug.Log("NATIVE_BRIDGE_SWEEP passed="+report.passed+" trials="+trials.Count);
            }
            Application.Quit(report.passed?0:2);
        }
        static bool Ground(Vector3 anchor,bool water,out RaycastHit hit)
        {
            hit=default;
            var hits=Physics.RaycastAll(anchor+Vector3.up*30,Vector3.down,60,~0,QueryTriggerInteraction.Ignore)
                .Where(h=>h.collider&&h.collider.CompareTag("Water")==water&&!h.collider.GetComponentInParent<NativeKartMotor>()&&h.normal.y>=Mathf.Cos(65*Mathf.Deg2Rad))
                .OrderBy(h=>h.distance).ToArray();
            if(hits.Length==0)return false;hit=hits[0];return true;
        }
    }
}
