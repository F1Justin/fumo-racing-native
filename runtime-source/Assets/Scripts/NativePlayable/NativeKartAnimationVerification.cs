using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAnimationVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class PoseSample
        {
            public int racer; public string clip, path; public float normalizedTime, positionError, scaleError, angleError;
            public Vector3 position, scale, expectedPosition, expectedScale;
        }
        [Serializable] sealed class FrameSample
        {
            public int racer, frame; public float timeScale, normalizedTime; public Vector3 characterPosition, kartScale;
        }
        [Serializable] sealed class Report
        {
            public string version, boundary; public bool passed; public Check[] checks;
            public PoseSample[] poses; public FrameSample[] frames;
        }
        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>(); var poses = new List<PoseSample>(); var frames = new List<FrameSample>();
            var rigs = race.racers.Select(r => r.GetComponent<NativeKartAnimation>()).ToArray();
            var cullingModes = rigs.Select(r => r && r.animator ? r.animator.cullingMode : AnimatorCullingMode.AlwaysAnimate).ToArray();
            var motorsEnabled = race.racers.Select(r => r.enabled).ToArray();
            var wheels = race.racers.Select(r => r.GetComponent<NativeKartWheelVisual>()).ToArray();
            var wheelsEnabled = wheels.Select(w => w.enabled).ToArray();
            float oldScale = Time.timeScale; int errors = 0;
            Application.LogCallback log = (message, trace, kind) => { if (kind == LogType.Exception || kind == LogType.Error || kind == LogType.Assert) errors++; };
            Application.logMessageReceived += log;
            try
            {
                race.SetVisualCapture("race", true); Time.timeScale = 1f;
                foreach(var r in race.racers) r.enabled = false;
                foreach(var w in wheels) w.enabled = false;
                Add("four-original-hierarchies", rigs.Length == 4 && rigs.All(r => r && r.animator && r.accelerationPivot
                    && r.animator.transform.Find("Kart") && r.animator.transform.Find("AccelPivot/Accel2Pivot") == r.accelerationPivot), "Four real cars use original animation binding paths");
                if (rigs.Any(r => !r || !r.animator || !r.accelerationPivot)) yield break;
                for(int i=0;i<4;i++)
                {
                    var rig=rigs[i]; rig.enabled=false;
                    Add("controller-settings-"+i, rig.animator.runtimeAnimatorController.name=="AnimPivot"
                        && rig.animator.updateMode==AnimatorUpdateMode.Normal && !rig.animator.applyRootMotion
                        && rig.animator.enabled && rig.animator.cullingMode == (i == 0 ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms), "Original normal-clock AnimPivot controller with no root motion");
                    rig.animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    var kart=rig.animator.transform.Find("Kart");
                    var engine=kart.GetComponentsInChildren<Animator>(true);
                    Add("original-engine-remains-disabled-"+i, engine.Length==1 && !engine[0].enabled
                        && engine[0].runtimeAnimatorController.name=="Root", "Original kart engine is serialized disabled");
                    var wheelRotations=wheels[i].torques.Select(t=>t.localRotation).ToArray();
                    var rootPosition=wheels[i].root.localPosition;
                    rig.ResetPlayback();
                    string[] clips={"idle","normalspeed","highspeed"}; float[] speeds={0f,70f,112f};
                    for(int c=0;c<3;c++) foreach(float phase in new[]{.125f,.375f,.625f,.875f})
                    {
                        rig.SetParameters(speeds[c],false,true,false);
                        rig.animator.Play("Base Layer.Movement",0,phase); rig.animator.Update(0f);
                        var info=rig.animator.GetCurrentAnimatorClipInfo(0);
                        bool weight=info.Any(x=>x.clip.name==clips[c] && Mathf.Abs(x.weight-1f)<.0001f);
                        Add("blend-"+i+"-"+c+"-"+phase,weight,"Speed/140 selects the original endpoint clip");
                        var clip=rig.animator.runtimeAnimatorController.animationClips.First(x=>x.name==clips[c]);
                        foreach(string path in new[]{"","AccelPivot","Kart"})
                        {
                            var t=path.Length==0?rig.animator.transform:rig.animator.transform.Find(path);
                            float seconds=phase*clip.length;
                            var ep=NativeKartAnimationReference.Evaluate(clips[c],path,"position",seconds);
                            var es=NativeKartAnimationReference.Evaluate(clips[c],path,"scale",seconds);
                            var er=Quaternion.Euler(NativeKartAnimationReference.Evaluate(clips[c],path,"euler",seconds));
                            poses.Add(new PoseSample {racer=i,clip=clips[c],path=path,normalizedTime=phase,position=t.localPosition,
                                scale=t.localScale,expectedPosition=ep,expectedScale=es,positionError=Vector3.Distance(t.localPosition,ep),
                                scaleError=Vector3.Distance(t.localScale,es),angleError=Quaternion.Angle(t.localRotation,er)});
                        }
                    }
                    Add("pose-curves-"+i,poses.Where(p=>p.racer==i).All(p=>p.positionError<.0001f && p.scaleError<.0001f && p.angleError<.03f),NativeKartAnimationReference.Source);
                    rig.SetParameters(35f,false,true,false); rig.animator.Play("Base Layer.Movement",0,.3f);rig.animator.Update(0f);
                    var mixed=rig.animator.GetCurrentAnimatorClipInfo(0);
                    Add("midpoint-blend-"+i,mixed.Any(x=>x.clip.name=="idle"&&Mathf.Abs(x.weight-.5f)<.0001f)
                        &&mixed.Any(x=>x.clip.name=="normalspeed"&&Mathf.Abs(x.weight-.5f)<.0001f),"vspeed=.25 blends 0 and .5 thresholds equally");
                    Add("wheel-ownership-"+i,Vector3.Distance(rootPosition,wheels[i].root.localPosition)<.000001f
                        &&wheels[i].torques.Select((t,k)=>Quaternion.Angle(t.localRotation,wheelRotations[k])<.001f).All(v=>v),"Controller changes parent poses while preserving wheel/root local transforms");
                    rig.ResetPlayback();float avg=0f;Quaternion expected=Quaternion.identity;
                    foreach(var pair in new[]{new Vector2(0,1),new Vector2(1,20),new Vector2(20,-30),new Vector2(-30,-30)})
                    {
                        float dt=.02f;avg=(avg+(pair.y-pair.x)*3f)*(1f-16f*dt);
                        expected=Quaternion.Lerp(expected,Quaternion.Euler(Mathf.Clamp(-avg,-40f,40f),0,0),12f*dt);
                        rig.StepAcceleration(pair.x,pair.y,dt);
                    }
                    Add("acceleration-pitch-"+i,Mathf.Abs(avg-rig.AverageAcceleration)<.0001f&&Quaternion.Angle(expected,rig.accelerationPivot.localRotation)<.03f,"Original speed difference, decay, clamp and quaternion smoothing");
                    rig.ResetPlayback();rig.enabled=true;
                }
                yield return null;
                var starts=rigs.Select(r=>r.animator.GetCurrentAnimatorStateInfo(0).normalizedTime).ToArray();
                for(int n=0;n<60;n++)
                {
                    yield return new WaitForEndOfFrame();
                    for(int i=0;i<4;i++) frames.Add(new FrameSample{racer=i,frame=Time.frameCount,timeScale=Time.timeScale,
                        normalizedTime=rigs[i].animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
                        characterPosition=wheels[i].fumo.position,kartScale=rigs[i].animator.transform.Find("Kart").localScale});
                    yield return null;
                }
                Add("actual-frame-progress",rigs.Select((r,i)=>r.animator.GetCurrentAnimatorStateInfo(0).normalizedTime>starts[i]+.05f).All(v=>v),"Actual Player animation evaluation across 60 rendered frames");
                Add("actual-fixed-parameters",rigs.All(r=>r.ParameterSteps>1),"Normal Player FixedUpdate sends original continuous parameters");
                race.SetVisualCapture("pause",true);Time.timeScale=0f;
                yield return new WaitForEndOfFrame();
                var paused=rigs.Select(r=>r.animator.GetCurrentAnimatorStateInfo(0).normalizedTime).ToArray();
                var steps=rigs.Select(r=>r.ParameterSteps).ToArray();
                for(int n=0;n<12;n++) yield return null;
                Add("pause-freezes",rigs.Select((r,i)=>Mathf.Abs(r.animator.GetCurrentAnimatorStateInfo(0).normalizedTime-paused[i])<.000001f&&r.ParameterSteps==steps[i]).All(v=>v),"Scaled animator and FixedUpdate remain frozen through rendered pause frames");
                race.SetVisualCapture("race",true);Time.timeScale=1f;
                for(int n=0;n<10;n++)yield return null;
                Add("resume-progresses",rigs.Select((r,i)=>r.animator.GetCurrentAnimatorStateInfo(0).normalizedTime>paused[i]).All(v=>v),"Playback resumes on actual frames");
                foreach(var rig in rigs)rig.StepAcceleration(0f,30f,.02f);
                race.ResetRace(true);
                Add("restart-resets",rigs.All(r=>r.AverageAcceleration==0f&&Quaternion.Angle(r.accelerationPivot.localRotation,Quaternion.identity)<.001f
                    &&Mathf.Abs(r.animator.GetFloat("vspeed"))<.000001f&&Mathf.Abs(r.animator.GetCurrentAnimatorStateInfo(0).normalizedTime)<.0001f),"Normal race restart clears acceleration memory and controller phase");
                Add("global-silence",AudioListener.volume==0f&&UnityEngine.Object.FindObjectsOfType<AudioSource>(true).All(a=>!a.isPlaying),"Construction build remains silent");
            }
            finally
            {
                Time.timeScale=oldScale;
                for(int i=0;i<race.racers.Length;i++){race.racers[i].enabled=motorsEnabled[i];wheels[i].enabled=wheelsEnabled[i];if(rigs[i]){rigs[i].enabled=true;if(rigs[i].animator)rigs[i].animator.cullingMode=cullingModes[i];}}
                Application.logMessageReceived-=log;
                Add("runtime-errors",errors==0,"Error/exception/assert log count="+errors);
                var report=new Report{version=Application.version,boundary="Original AnimPivot controller/continuous parameters and acceleration pitch. Original disabled engine remains disabled. Synthetic endpoint inputs verify original serialized clip curves; live Player frames verify playback/pause/restart with all four animators temporarily AlwaysAnimate. Action triggers are the next slice.",passed=checks.All(c=>c.passed),checks=checks.ToArray(),poses=poses.ToArray(),frames=frames.ToArray()};
                File.WriteAllText(Path.Combine(directory,"animation-report.json"),JsonUtility.ToJson(report,true));
                Debug.Log("NATIVE_ANIMATION_RESULT passed="+report.passed);
                Application.Quit(report.passed?0:2);
            }
            void Add(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});}
        }
    }
}
