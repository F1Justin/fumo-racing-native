using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAnimationActionsVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string source, trigger, expectedState;
            public int frame, currentHash, nextHash, accepted, suppressed, step;
            public bool transition, grounded, manualJump, landed, leftGround;
            public float speed, gravity, normalizedTime;
            public Vector3 position, animationPosition, characterPosition;
        }
        [Serializable] sealed class Report
        {
            public string version, boundary; public bool passed;
            public Check[] checks; public Sample[] samples;
        }
        public static IEnumerator Run(NativeRace race,string directory)
        {
            Directory.CreateDirectory(directory);
            var checks=new List<Check>();var samples=new List<Sample>();
            var motor=race.racers[0];var rig=motor.GetComponent<NativeKartAnimation>();var actions=motor.GetComponent<NativeKartAnimationActions>();
            bool[] enabled=race.racers.Select(m=>m.enabled).ToArray();
            float oldScale=Time.timeScale,oldCapture=Time.captureDeltaTime;int errors=0;
            Application.LogCallback logger=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors++;};
            Application.logMessageReceived+=logger;GameObject platform=null;
            try
            {
                race.SetVisualCapture("race",true);Time.timeScale=1f;Time.captureDeltaTime=1f/60f;
                foreach(var r in race.racers)r.enabled=false;
                Add("bound-original-actions",rig&&actions&&actions.rig==rig&&rig.animator,"Real car action bridge references original controller");
                if(!rig||!actions||!rig.animator)yield break;
                rig.enabled=false;
                string[] triggers={"boost","driftStart","manualJump","jump","fall","strongLand","swordSlash","whillieStart"};
                string[] states={"boostspeed","drift","strongland","jump","fall","strongland","youmuswordslash","bike_whillie_start"};
                for(int i=0;i<triggers.Length;i++)
                {
                    actions.ResetActions();rig.ResetPlayback();rig.SetParameters(70f,triggers[i]=="driftStart",false,triggers[i]=="whillieStart");
                    actions.SetTrigger(triggers[i]);rig.animator.Update(.001f);
                    int expected=Animator.StringToHash("Base Layer."+states[i]);
                    var current=rig.animator.GetCurrentAnimatorStateInfo(0);var next=rig.animator.GetNextAnimatorStateInfo(0);
                    Add("original-transition-"+triggers[i],current.fullPathHash==expected||next.fullPathHash==expected,"Original controller destination="+states[i]);
                    Record("synthetic-controller",triggers[i],states[i]);
                }
                actions.ResetActions();rig.ResetPlayback();rig.SetParameters(0f,false,true,false);
                actions.Freeze(.2f);int count=actions.AcceptedTriggers;actions.SetTrigger("boost");
                float phase=rig.animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                rig.SetParameters(112f,true,false,false);rig.animator.Update(.05f);
                Add("freeze-only-blocks-triggers",actions.Frozen&&actions.AcceptedTriggers==count&&actions.SuppressedTriggers==1
                    &&rig.animator.GetFloat("vspeed")==.8f&&rig.animator.GetBool("drift")
                    &&rig.animator.GetCurrentAnimatorStateInfo(0).normalizedTime>phase,"Freeze preserves parameter updates and existing animation playback");
                Time.timeScale=0f;
                yield return new WaitForEndOfFrame();
                for(int i=0;i<20;i++)yield return null;
                Add("pause-preserves-freeze-timer",actions.Frozen,"Invoke duration remains pending through paused rendered frames");
                Time.timeScale=1f;
                for(int i=0;i<6;i++)yield return null;
                actions.Freeze(.3f);
                for(int i=0;i<10;i++)yield return null;
                Add("repeated-freeze-replaces-deadline",actions.Frozen,"Second Freeze cancels first unfreeze deadline");
                for(int i=0;i<15;i++)yield return null;
                Add("scaled-unfreeze",!actions.Frozen,"Replacement Invoke expires after scaled duration");
                actions.SetTrigger("boost");Add("trigger-after-unfreeze",actions.AcceptedTriggers==count+1&&actions.LastTrigger=="boost","Events resume after deadline");
                actions.ResetActions();rig.ResetPlayback();
                actions.ObserveSimulation(false,false,true,false,4.999f);bool low=actions.LastTrigger=="fall";
                actions.ObserveSimulation(false,false,true,false,5f);
                Add("departure-gravity-boundary",low&&actions.LastTrigger=="jump"&&actions.FallTriggers==1&&actions.JumpTriggers==1,"Original >=5 comparison separates jump and fall");
                actions.ResetActions();rig.ResetPlayback();rig.enabled=true;
                platform=new GameObject("Animation actions real contact plane");platform.transform.position=new Vector3(0,499.5f,0);
                platform.AddComponent<BoxCollider>().size=new Vector3(1000,1,1000);Physics.SyncTransforms();
                motor.ResetAt(new Vector3(0,500.3f,0),Quaternion.identity,false);motor.UseFixedVerificationClock=false;
                motor.driving=true;motor.throttle=motor.steering=0;motor.braking=motor.drifting=false;motor.enabled=true;
                for(int i=0;i<30;i++){yield return null;Record("actual-settle","","");}
                Add("actual-contact-grounded",motor.Grounded,"Real CharacterController settles on independent BoxCollider");
                actions.ResetActions();rig.ResetPlayback();motor.jump=true;
                bool sawJump=false,sawLand=false;float startY=motor.transform.position.y,maxY=startY;
                for(int i=0;i<180;i++)
                {
                    yield return null;Record("actual-jump","","");
                    sawJump|=motor.JumpedThisStep;sawLand|=motor.LandedThisStep;maxY=Mathf.Max(maxY,motor.transform.position.y);
                    if(sawJump&&sawLand)break;
                }
                Add("actual-manual-jump-event",sawJump&&maxY>startY+.1f&&actions.ManualJumpTriggers==1&&actions.JumpTriggers==0,"Successful real manual jump emits manualJump once");
                Add("actual-landing-event",sawLand&&actions.LandTriggers==1,"Real air-to-ground contact emits strongLand once");
                actions.ResetActions();rig.ResetPlayback();motor.throttle=1;motor.steering=.5f;motor.braking=motor.drifting=true;
                for(int i=0;i<12;i++){yield return null;Record("actual-drift-held","","");}
                Add("actual-drift-enter-once",motor.IsDrifting&&actions.DriftTriggers==1,"Held drift input emits one driftStart at entry");
                motor.braking=motor.drifting=false;
                for(int i=0;i<5;i++)yield return null;
                motor.braking=motor.drifting=true;
                for(int i=0;i<5;i++)yield return null;
                Add("actual-drift-reentry",actions.DriftTriggers==2,"Release and re-entry emit a second driftStart");
                motor.enabled=false;actions.Freeze(10f);actions.SetTrigger("boost");race.ResetRace(true);
                Add("restart-clears-actions",!actions.Frozen&&actions.AcceptedTriggers==0&&actions.SuppressedTriggers==0&&string.IsNullOrEmpty(actions.LastTrigger),"Normal race restart cancels pending freeze and clears event state");
                Add("global-silence",AudioListener.volume==0f&&UnityEngine.Object.FindObjectsOfType<AudioSource>(true).All(a=>!a.isPlaying),"Construction application remains silent");
            }
            finally
            {
                Time.timeScale=oldScale;Time.captureDeltaTime=oldCapture;
                for(int i=0;i<race.racers.Length;i++)race.racers[i].enabled=enabled[i];
                if(rig)rig.enabled=true;if(actions)actions.ResetActions();if(platform)UnityEngine.Object.Destroy(platform);
                Application.logMessageReceived-=logger;Add("runtime-errors",errors==0,"Runtime errors="+errors);
                var report=new Report{version=Application.version,passed=checks.All(c=>c.passed),checks=checks.ToArray(),samples=samples.ToArray(),boundary="Original controller trigger mapping and scaled trigger-suppression timer; real CharacterController jump/landing/drift event bridge. Boost, wheelie and sword cases use synthetic controller requests; their item/vehicle gameplay producers remain outside this slice."};
                File.WriteAllText(Path.Combine(directory,"animation-actions-report.json"),JsonUtility.ToJson(report,true));
                Debug.Log("NATIVE_ANIMATION_ACTIONS_RESULT passed="+report.passed);Application.Quit(report.passed?0:2);
            }
            void Add(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});}
            void Record(string source,string trigger,string expected)
            {
                var state=rig.animator.GetCurrentAnimatorStateInfo(0);
                samples.Add(new Sample{source=source,trigger=trigger,expectedState=expected,frame=Time.frameCount,
                    currentHash=state.fullPathHash,nextHash=rig.animator.GetNextAnimatorStateInfo(0).fullPathHash,
                    transition=rig.animator.IsInTransition(0),normalizedTime=state.normalizedTime,
                    accepted=actions.AcceptedTriggers,suppressed=actions.SuppressedTriggers,step=motor.FixedStepCount,
                    grounded=motor.Grounded,manualJump=motor.JumpedThisStep,landed=motor.LandedThisStep,leftGround=motor.LeftGroundThisStep,
                    speed=motor.Speed,gravity=motor.VerticalSpeed,position=motor.transform.position,
                    animationPosition=rig.animator.transform.localPosition,characterPosition=rig.accelerationPivot.position});
            }
        }
    }
}
