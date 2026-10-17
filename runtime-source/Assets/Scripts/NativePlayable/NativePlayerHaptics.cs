using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(500)]
    public sealed class NativePlayerHaptics : MonoBehaviour
    {
        public NativeKartMotor Motor { get; private set; }
        public readonly NativeHapticMixer Mixer=new NativeHapticMixer();
        public NativeHapticOutput Output { get; private set; }
        public INativeHapticDevice Device { get; private set; }
        public Gamepad ExperienceDevice;
        public bool ExperienceActive;
        public string Vehicle => NativeHapticProfiles.Vehicle(Motor ? Motor.stats : null);
        public string Status => Device == null ? "等待手柄连接" : Device.Status;
        public int SuccessfulWrites { get; private set; }
        public event Action<NativeHapticEvent> Observed;
        NativeRace race;
        int slot;
        Gamepad boundPad;
        readonly NativeHapticOutputGate outputGate=new NativeHapticOutputGate();
        readonly NativeHapticMotorEstimate estimate=new NativeHapticMotorEstimate();
        public string TraceDirectory;
        public int TraceClip;
        public float TraceBegan;
        NativeHapticOutput actualCommand,modeledOutput;
        [Serializable] sealed class WriteReceipt { public int clip;public float elapsed;public double time;public float commandLow,commandHigh,estimatedMotorLow,estimatedMotorHigh;public int lowByte,highByte;public bool success;public string dominant,status; }
        int resetFrame=-10000;
        bool stopped=true;
        readonly bool auditOnly=Array.IndexOf(Environment.GetCommandLineArgs(),"--haptics-audit")>=0;
        public void Configure(NativeRace race,NativeKartMotor motor,int slot)
        {
            if (Motor==motor && this.race==race && this.slot==slot) return;
            if(Motor)Motor.HapticEvent-=OnFact;
            Stop(); this.race=race; this.slot=slot; Motor=motor;
            if(Motor)Motor.HapticEvent+=OnFact;
            Mixer.Reset();
        }
        void OnFact(NativeHapticEvent e)
        {
            if (!Motor) return;
            if (Mixer.Submit(e,NativeHapticProfiles.Get(Vehicle),Time.timeAsDouble)) Observed?.Invoke(e);
        }
        Gamepad DesiredPad()
        {
            if (ExperienceDevice != null && ExperienceActive) return ExperienceDevice;
            var owner=race ? race.SourceActualLocalOwner(slot) : null;
            if(!owner || !owner.user.valid)return null;
            foreach (var device in owner.user.pairedDevices) if (device is Gamepad pad && pad.added) return pad;
            return null;
        }
        void LateUpdate()
        {
            modeledOutput=estimate.Update(actualCommand,Mixer.Response,Time.unscaledDeltaTime);
            if (!Motor) { Stop(); return; }
            var pad=DesiredPad();
            if (pad!=boundPad || (auditOnly && Device==null))
            {
                Device?.Dispose(); actualCommand=default;Device=null; boundPad=pad; stopped=true; outputGate.Reset(); Mixer.Reset();
                if(pad!=null || auditOnly) { Device=auditOnly ? NativeHapticDevices.Audit() : NativeHapticDevices.Open(pad); Mixer.Response=NativeHapticResponse.Load(Device.Name+"|"+Device.Status); Debug.Log("NATIVE_HAPTICS_DEVICE "+Device.Name+" "+Device.Status); }
            }
            bool active=(Application.isFocused || auditOnly) && Time.timeScale>0 && (Motor.driving && (ExperienceActive || (race && race.IsRaceRunning)));
            if (!active || Device==null || !Device.Connected) { Stop(); return; }
            if(resetFrame!=Motor.LastResetFrame) { resetFrame=Motor.LastResetFrame; Mixer.Reset(); }
            var state=NativeHapticState.Read(Motor);
            var radar=Motor.GetComponent<NativeItemRadarSensor>();
            if (radar) for (int i=0;i<radar.Count;i++) if(radar.GetItem(i,out var item))
                state.threat=Mathf.Max(state.threat,Mathf.Clamp01(1-Vector3.Distance(item.transform.position,Motor.transform.position)/100));
            Output=Mixer.Evaluate(state,NativeHapticProfiles.Get(Vehicle),Time.timeAsDouble,Time.deltaTime);
            if(auditOnly)return;
            double now=Time.realtimeSinceStartupAsDouble;
            if(outputGate.ShouldSend(Output,now))Send(Output,now);
        }
        void Send(NativeHapticOutput output,double now)
        {
            bool ok=Device!=null && Device.Write(output);
            var modeled=modeledOutput;
            if(!string.IsNullOrEmpty(TraceDirectory))
            {
                Directory.CreateDirectory(TraceDirectory);
                File.AppendAllText(Path.Combine(TraceDirectory,"writes.jsonl"),JsonUtility.ToJson(new WriteReceipt { time=now,clip=TraceClip,elapsed=Time.time-TraceBegan,
                    commandLow=output.low,commandHigh=output.high,estimatedMotorLow=modeled.low,estimatedMotorHigh=modeled.high,
                    lowByte=Mathf.RoundToInt(output.low*255),highByte=Mathf.RoundToInt(output.high*255),success=ok,dominant=Mixer.Dominant,status=Status })+"\n");
            }
            if(ok) { actualCommand=output;SuccessfulWrites++;outputGate.Sent(output,now);stopped=output.low==0 && output.high==0; }
            else { Device?.Stop();actualCommand=default;stopped=true;outputGate.Reset(); }
        }
        public void Stop()
        {
            Output=default;
            if(!stopped && !auditOnly)Send(default,Time.realtimeSinceStartupAsDouble);
            stopped=true;outputGate.Reset();Mixer.Reset();
        }
        void OnApplicationFocus(bool focused) { if(!focused && !auditOnly)Stop(); }
        void OnApplicationPause(bool paused) { if(paused && !auditOnly)Stop(); }
        void OnDisable() { Stop(); Mixer.Reset(); }
        void OnDestroy() { if(Motor)Motor.HapticEvent-=OnFact; Device?.Dispose(); }
    }

    [DefaultExecutionOrder(100)]
    public sealed class NativeHapticsRuntime : MonoBehaviour
    {
        NativeRace race;
        readonly NativePlayerHaptics[] players=new NativePlayerHaptics[4];
        readonly NativePlayerHaptics[] targets=new NativePlayerHaptics[4];
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--haptics-characterize")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"--haptics-characterize-ui-self-test")>=0 || FindObjectOfType<NativeHapticsRuntime>())return;
            var go=new GameObject("THFR Haptics"); DontDestroyOnLoad(go); go.AddComponent<NativeHapticsRuntime>();
        }
        void Update()
        {
            if(!race)race=FindObjectOfType<NativeRace>();
            if(!race)return;
            for(int slot=0;slot<4;slot++)
            {
                int index=race.HapticRacerIndex(slot); targets[slot]=null;
                if(index<0 || index>=race.racers.Length)continue;
                var m=race.racers[index];var player=m.GetComponent<NativePlayerHaptics>();
                if(!player)player=m.gameObject.AddComponent<NativePlayerHaptics>();
                targets[slot]=player;
            }
            foreach(var previous in players)
                if(previous && !previous.ExperienceActive && Array.IndexOf(targets,previous)<0)previous.enabled=false;
            for(int slot=0;slot<4;slot++)
            {
                players[slot]=targets[slot];var player=players[slot];if(!player)continue;
                player.Configure(race,race.racers[race.HapticRacerIndex(slot)],slot);player.enabled=true;
            }
        }
    }

    public sealed partial class NativeRace
    {
        internal int HapticRacerIndex(int slot)
        {
            if(Net && Net.Open)return slot==0 ? Net.LocalRacerIndex : -1;
            if(raceConfig==null)return -1;
            int human=0;
            for(int i=0;i<raceConfig.roster.Count;i++)if(raceConfig.roster[i].isHuman && human++==slot)return i;
            return -1;
        }
    }
}
