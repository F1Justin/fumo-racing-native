using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeHapticVerification : MonoBehaviour
    {
        [Serializable] sealed class Report { public bool passed; public string[] checks; public string bluetoothPacket; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--haptics-self-test")>=0)
                new GameObject("Haptic verification").AddComponent<NativeHapticVerification>();
        }
        IEnumerator Start()
        {
            yield return null;
            var args=Environment.GetCommandLineArgs();int arg=Array.IndexOf(args,"--report-dir");
            string directory=arg>=0 ? args[arg+1] : Path.Combine(Application.persistentDataPath,"haptic-verification");Directory.CreateDirectory(directory);
            var checks=new List<string>();bool passed=true;string packet="";
            Action<bool,string> check=(ok,name)=>{ passed&=ok;checks.Add((ok?"PASS ":"FAIL ")+name); };
            try
            {
                var kart=NativeHapticProfiles.Get("kart");var mixer=new NativeHapticMixer();
                long sequence=1;
                var busy=new NativeHapticState { driving=true,grounded=true,drifting=true,speed=1,throttle=.5f,charge=.6f,boost=1 };
                foreach(string id in new[]{"kart","bike","skate","cleanerbot","shoppingcart","broom","genji"})
                {
                    var profile=NativeHapticProfiles.Get(id);check(profile.id==id,"profile identity "+id);
                    foreach(int hz in new[]{30,60,120})
                        for(int i=0;i<hz;i++)
                        {
                            busy.distance=i*1.5f;var o=mixer.Evaluate(busy,profile,i/(double)hz,1f/hz);
                            check(o.low==0 && o.high==0,"ordinary driving exact zero "+id+" "+hz+" "+i);
                        }
                }
                var offroad=busy;offroad.grass=true;
                foreach(int hz in new[]{30,60,120})
                {
                    mixer.Reset();mixer.Response=new NativeHapticResponse { measured=true,highStart=.5f,highOn=.15f,highGap=.2f,lowGap=.15f };
                    foreach(string id in new[]{"kart","bike","skate","cleanerbot","shoppingcart","broom","genji"})
                    {
                        mixer.Reset();var profile=NativeHapticProfiles.Get(id);
                        for(int i=0;i<hz;i++)
                        {
                            double t=i/(double)hz;var o=mixer.Evaluate(offroad,profile,t,1f/hz);
                            check(o.low==0 && (t<.08 ? o.high==0 : t<.23 ? o.high==.5f : o.high>0 && o.high<=.24f),"offroad start then restrained sustain "+id+" "+hz+" "+i);
                        }
                        var paved=mixer.Evaluate(busy,profile,1,1f/hz);
                        check(paved.low==0 && paved.high==0 && mixer.Dominant=="Silence","paved return stops road feel "+id+" "+hz);
                    }
                }
                foreach(string stop in new[]{"stationary","airborne","frozen","inactive"})
                {
                    mixer.Reset();mixer.Evaluate(offroad,kart,0,.01f);mixer.Evaluate(offroad,kart,.3,.01f);
                    var stopped=offroad;
                    if(stop=="stationary")stopped.speed=0;
                    if(stop=="airborne")stopped.grounded=false;
                    if(stop=="frozen")stopped.frozen=true;
                    if(stop=="inactive")stopped.driving=false;
                    var o=mixer.Evaluate(stopped,kart,.31,.01f);check(o.low==0 && o.high==0,"offroad stops immediately "+stop);
                }
                mixer.Reset();mixer.Evaluate(offroad,kart,0,.01f);mixer.Evaluate(busy,kart,.04,.01f);
                check(mixer.Evaluate(offroad,kart,.05,.01f).high==0 && mixer.Evaluate(offroad,kart,.1,.01f).high==0,"brief grass contact rejected before startup");
                mixer.Reset();mixer.Strength=0;mixer.Evaluate(offroad,kart,0,.01f);check(mixer.Evaluate(offroad,kart,.3,.01f).high==0,"master zero silences offroad");mixer.Strength=.75f;
                mixer.Reset();mixer.Evaluate(offroad,kart,0,.01f);
                check(mixer.Evaluate(offroad,kart,.3,.01f).high<.5f,"sustain remains below short-pulse command floor");
                mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Ready },kart,.31);
                check(mixer.Evaluate(offroad,kart,.5,.01f).high==0,"offroad never fills ready off interval");
                check(mixer.Evaluate(offroad,kart,.9,.01f).high==0,"offroad respects post-event motor stop");
                check(mixer.Evaluate(offroad,kart,1.02,.01f).high==0 && mixer.Evaluate(offroad,kart,1.12,.01f).high>0,"offroad restarts after event and contact debounce");
                mixer.Response=new NativeHapticResponse();
                mixer.Reset();var fact=new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Damage,magnitude=1 };
                check(mixer.Submit(fact,kart,2),"accepted damage");check(!mixer.Submit(fact,kart,2.1),"sequence deduplication");
                var damage=mixer.Evaluate(busy,kart,2.02,1f/60);check(damage.low>0 && damage.high>0,"retained heavy impact");
                check(!mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Pickup },kart,2.45),"heavy impact refractory period drops small event");
                var expired=mixer.Evaluate(busy,kart,3,1f/60);check(expired.low==0 && expired.high==0,"impulse and refractory return to zero");
                mixer.Reset();check(mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Ready },kart,4),"ready accepted");
                foreach(double time in new[]{4.16,4.2,4.3,4.39})
                {
                    var gap=mixer.Evaluate(busy,kart,time,.01f);check(gap.low==0 && gap.high==0,"protected double-pulse gap "+time);
                }
                check(!mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Pickup },kart,4.2),"lower voice cannot fill gap");
                check(mixer.Evaluate(busy,kart,4.42,.01f).high>0,"second pulse has a fresh edge");
                check(mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Boost },kart,4.43),"release replaces ready");
                var release=mixer.Evaluate(busy,kart,4.44,.01f);check(release.low>0 && release.high==0,"release owns both channels");
                check(mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Damage },kart,4.45),"damage interrupts release");
                mixer.Reset();check(!mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Overheat },kart,5),"overheat waits for actual damage");
                check(!mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.DriftEnter },kart,5),"ordinary drift stays quiet");
                check(!mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.ChargeStage,stage=2 },kart,5),"middle charge stage stays quiet");
                check(mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.CartRisk,stage=1 },kart,5),"cart risk edge accepted");
                mixer.Evaluate(default,kart,5.01,.01f);
                check(mixer.Evaluate(busy,kart,5.45,.01f).high==0,"release drift cancels pending cart pulse");
                mixer.Reset();mixer.MechanismsEnabled=false;
                check(!mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Ready },kart,6),"mechanism source toggle");
                check(mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.Damage },kart,6),"impact source remains active");mixer.MechanismsEnabled=true;
                mixer.Reset();var cartState=busy;cartState.module=NativeVehicleModuleKind.ShoppingCartDriftCap;
                mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.CartRisk,stage=1 },kart,6.5);
                mixer.Evaluate(cartState,kart,7.06,.01f);
                check(mixer.Submit(new NativeHapticEvent { sequence=sequence++,kind=NativeHapticKind.CartRisk,stage=2 },kart,7.1),"urgent risk replaces earlier risk and its recovery period");
                var latch=new NativeHapticContactLatch();check(latch.Touch(5,0,true),"fresh wall contact");
                for(int i=1;i<=60;i++)check(!latch.Touch(5,i/60.0,true),"sustained wall contact silent "+i);
                check(latch.Touch(5,1.3,true),"separate wall contact rearmed");
                check(!latch.Touch(6,2,false) && !latch.Touch(6,2.01,true),"gentle existing contact does not become repeated impact");
                var gate=new NativeHapticOutputGate();check(!gate.ShouldSend(default,0),"idle avoids device keepalive");
                gate.Sent(new NativeHapticOutput(.003f,.003f),0);
                check(gate.ShouldSend(default,.001),"exact zero bypasses delta threshold and 60Hz gate");gate.Sent(default,.001);
                check(gate.ShouldSend(new NativeHapticOutput(0,.4f),.002),"fresh onset bypasses 60Hz gate");
                check(!gate.ShouldSend(default,1),"stopped device stays silent");
                var model=new NativeHapticMotorEstimate();check(model.Update(default,new NativeHapticResponse(),.01f).low==-1,"unmeasured model marked unavailable");
                var response=new NativeHapticResponse { lowUpTau=.08f,lowDownTau=.18f,highUpTau=.04f,highDownTau=.09f };
                var spinning=model.Update(new NativeHapticOutput(1,1),response,.5f);
                var coasting=model.Update(default,response,.01f);check(spinning.low>.9f && coasting.low>0,"measured model retains inertia after command zero");
                var trials=NativeHapticCharacterization.CreateTrials();check(trials.Count==80,"80 independent characterization trials");
                foreach(var trial in trials)trial.label=trial.pulses==2 ? "两下" : "一下";
                var derived=NativeHapticCharacterization.DeriveResponse(trials,"test","evidence");
                check(derived.measured && derived.lowOn==.05f && Mathf.Abs(derived.lowGap-.1f)<.001f,"perceptual labels derive device timing");
                var saved=UnityEngine.Random.state;
                try
                {
                    UnityEngine.Random.InitState(7031);float expected=UnityEngine.Random.value;UnityEngine.Random.InitState(7031);
                    for(int i=0;i<100;i++)mixer.Evaluate(busy,kart,7+i*.02,.02f);
                    check(UnityEngine.Random.value==expected,"gameplay random sequence unchanged");
                }
                finally { UnityEngine.Random.state=saved; }
                mixer.Reset();check(mixer.Submit(new NativeHapticEvent { sequence=1,kind=NativeHapticKind.Ready },kart,10),"new binding starts its own cursor");
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                var usb=NativeMacDS4Haptics.Packet(false,.3f,.6f);var bt=NativeMacDS4Haptics.Packet(true,.3f,.6f);
                check(usb.Length==32 && usb[0]==5 && usb[4]==153 && usb[5]==77,"USB motor order and packet length");
                check(bt.Length==78 && bt[0]==0x11 && bt[6]==153 && bt[7]==77,"Bluetooth motor order and packet length");
                packet=Convert.ToBase64String(bt);
#endif
            }
            catch(Exception e) { check(false,e.ToString()); }
            File.WriteAllText(Path.Combine(directory,"mixer.json"),JsonUtility.ToJson(new Report { passed=passed,checks=checks.ToArray(),bluetoothPacket=packet },true));
            Debug.Log("NATIVE_HAPTICS_RESULT passed="+passed+" checks="+checks.Count);Application.Quit(passed?0:2);
        }
    }
}
