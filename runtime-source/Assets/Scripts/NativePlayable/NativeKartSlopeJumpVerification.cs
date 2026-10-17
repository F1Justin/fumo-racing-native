using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed class NativeKartSlopeJumpContactObserver : MonoBehaviour
    {
        public readonly List<Vector3> normals=new List<Vector3>();
        void OnControllerColliderHit(ControllerColliderHit hit){normals.Add(hit.normal);}
    }
    public sealed class NativeKartSlopeJumpVerification : MonoBehaviour
    {
        [Serializable] public sealed class Sample
        {
            public string phase;public int flags;public float before,after;public bool grounded,jumped,drifting;
            public Vector3 position,lastSide,persistedNormal;public Vector3[] normals;
        }
        [Serializable] public sealed class Report
        {
            public bool passed, realWallContact, realWallLoss, frameNormalCleared;
            public int slopeSidesWithoutSteep, retainedSourceColliders;
            public bool slopeSpeedPreserved, allFrameNormalsCleared, slopeAirJumpObserved;
            public string scope="Injected initial speed and remote controlled geometry; real Motor.Simulate/CharacterController.Move and actual callback normals. Original scene colliders retained. Each slope phase resets initial state; real wall frame cleanup and slope behavior are independent lifecycle checks. No physical-input or whole-race acceptance.";
            public string error;public Sample[] samples;
        }
        static readonly MethodInfo simulate=typeof(NativeKartMotor).GetMethod("Simulate",BindingFlags.Instance|BindingFlags.NonPublic);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot(){if(Environment.GetCommandLineArgs().Contains("--check-kart-slope-jump"))new GameObject("SlopeJumpVerifier").AddComponent<NativeKartSlopeJumpVerification>();}
        IEnumerator Start()
        {
            yield return null;yield return null;
            var race=UnityEngine.Object.FindObjectOfType<NativeRace>();
            var report=new Report();var samples=new List<Sample>();var source=UnityEngine.Object.FindObjectsOfType<Collider>(true);
            var motor=race.racers[0];var observer=motor.gameObject.AddComponent<NativeKartSlopeJumpContactObserver>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="SlopeJumpFixtureFloor";floor.transform.position=new Vector3(10000,-.5f,-50);floor.transform.localScale=new Vector3(30,1,100);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="SlopeJumpFixtureWall";wall.transform.position=new Vector3(10000,10,-20);wall.transform.localScale=new Vector3(30,20,1);
            GameObject ramp=null;
            try
            {
                race.SetVisualCapture("race",true);race.ResetRace(false);race.SetVisualCapture("race",true);race.enabled=false;Time.timeScale=1;
                foreach(var r in race.racers){r.enabled=false;r.GetComponent<NativeKartOriginalAI>().enabled=false;}
                motor.gameObject.SetActive(true);motor.GetComponent<NativeKartItemInventory>().ResetItems();motor.driving=true;
                motor.ResetAt(new Vector3(10000,5,-22),Quaternion.identity,false);Set(motor,"Grounded",false);
                for(int i=0;i<4;i++)
                {
                    Set(motor,"Speed",120f);Set(motor,"velocity",Vector3.forward*120);Set(motor,"verticalSpeed",0f);
                    var sample=Step(motor,observer,"wall",.02f);samples.Add(sample);
                    report.realWallContact|=sample.normals.Any(n=>Vector3.Angle(Vector3.up,n)>65);
                    report.realWallLoss|=sample.after<sample.before-5;
                    yield return null;
                }
                report.frameNormalCleared=report.realWallContact&&motor.CollisionState.hitNormal==Vector3.zero;
                UnityEngine.Object.Destroy(wall);yield return null;
                foreach(float slope in new[]{15f,30f,45f,60f})
                {
                    ramp=new GameObject("SlopeJumpFixtureRamp");var mesh=new Mesh();float top=60*Mathf.Tan(slope*Mathf.Deg2Rad);
                    mesh.vertices=new[]{new Vector3(-15,0,0),new Vector3(-15,top,60),new Vector3(15,top,60),new Vector3(15,0,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();ramp.AddComponent<MeshCollider>().sharedMesh=mesh;ramp.transform.position=new Vector3(10000,0,0);
                    motor.ResetAt(new Vector3(10000,.215f,-5),Quaternion.identity,false);motor.driving=true;Set(motor,"Speed",120f);Set(motor,"velocity",Vector3.forward*120);
                    motor.throttle=1;motor.steering=.1f;motor.drifting=true;motor.jumpHeld=true;
                    for(int i=0;i<45;i++)
                    {
                        motor.jump=i==2;var sample=Step(motor,observer,"slope-"+slope,.02f);samples.Add(sample);
                        if((sample.flags&(int)CollisionFlags.Sides)!=0&&!sample.normals.Any(n=>Vector3.Angle(Vector3.up,n)>65))report.slopeSidesWithoutSteep++;
                        yield return null;
                    }
                    UnityEngine.Object.Destroy(ramp);ramp=null;yield return null;
                }
                report.retainedSourceColliders=source.Count(c=>c);
                report.allFrameNormalsCleared=samples.All(s=>s.persistedNormal.sqrMagnitude<1e-10f);
                var slopeSides=samples.Where(s=>s.phase.StartsWith("slope-")&&(s.flags&(int)CollisionFlags.Sides)!=0&&!s.normals.Any(n=>Vector3.Angle(Vector3.up,n)>65)).ToArray();
                report.slopeSpeedPreserved=slopeSides.Length>0&&slopeSides.All(s=>s.before-s.after<=5f);
                report.slopeAirJumpObserved=slopeSides.Any(s=>s.jumped&&!s.grounded);
                report.passed=report.realWallContact&&report.realWallLoss&&report.frameNormalCleared&&report.allFrameNormalsCleared&&report.slopeSpeedPreserved&&report.slopeAirJumpObserved&&report.retainedSourceColliders==source.Length;
                if(!report.passed)report.error="Runtime scope did not satisfy every controlled-geometry observation; inspect samples";
            }
            finally
            {
                motor.driving=false;motor.throttle=0;report.samples=samples.ToArray();
                var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--report-dir");string directory=args[index+1];Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"slope-jump-report.json"),JsonUtility.ToJson(report,true));
                UnityEngine.Object.Destroy(floor);if(wall)UnityEngine.Object.Destroy(wall);if(ramp)UnityEngine.Object.Destroy(ramp);
            }
        }
        static Sample Step(NativeKartMotor motor,NativeKartSlopeJumpContactObserver observer,string phase,float dt)
        {
            observer.normals.Clear();Physics.SyncTransforms();float before=motor.Speed;simulate.Invoke(motor,new object[]{dt});
            return new Sample{phase=phase,before=before,after=motor.Speed,flags=(int)motor.LastCollisionFlags,grounded=motor.Grounded,jumped=motor.JumpedThisStep,drifting=motor.IsDrifting,position=motor.transform.position,lastSide=motor.LastStepSideContactNormal,persistedNormal=motor.CollisionState.hitNormal,normals=observer.normals.ToArray()};
        }
        static void Set(NativeKartMotor motor,string name,object value)
        {
            var field=typeof(NativeKartMotor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)??typeof(NativeKartMotor).GetField("<"+name+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(motor,value);
        }
    }
}
