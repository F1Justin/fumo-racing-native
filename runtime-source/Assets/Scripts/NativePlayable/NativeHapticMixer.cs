using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public enum NativeHapticKind { Impact, Landing, Damage, Pickup, ItemUse, Boost, Ready, ChargeStage, WheelieBegin, WheelieEnd, Overheat, Respawn, DriftEnter, CalibrationLow, CalibrationHigh, CartRisk }

    public struct NativeHapticEvent
    {
        public long sequence;
        public int source, stage;
        public NativeHapticKind kind;
        public float magnitude;
        public string context;
    }

    public struct NativeHapticState
    {
        public bool grounded, grass, water, drifting, wheelie, braking, driving, frozen;
        public float speed, steering, distance, charge, boost, throttle, threat;
        public NativeVehicleModuleKind module;
        public int stage;
        public string surface;
        public static NativeHapticState Read(NativeKartMotor m)
        {
            var s = m.stats;
            return new NativeHapticState { grounded=m.Grounded, grass=m.OnGrass, water=m.InWater,
                drifting=m.IsDrifting, wheelie=m.IsWheelie, braking=m.braking, driving=m.driving, frozen=m.HitFrozen,
                speed=s == null ? 0 : Mathf.Clamp01(Mathf.Abs(m.Speed) / Mathf.Max(1, s._topSpeed)),
                steering=Mathf.Abs(m.CurrentSteering), distance=m.Distance, throttle=s != null && s._autoAccel ? .5f : m.throttle,
                charge=s == null ? 0 : Mathf.Clamp01(m.ModuleCharge / Mathf.Max(.01f, s._moduleLimit)),
                boost=m.ModuleBoostRemaining, module=s == null ? NativeVehicleModuleKind.None : s._moduleKind,
                stage=m.ModuleStage, surface=m.CurrentSurfaceKey };
        }
    }

    [Serializable]
    public sealed class NativeVehicleHapticProfile
    {
        public string id;
        public float propulsion, surface, drift, mass, impact, decay, irregularity, landing;
    }

    public static class NativeHapticProfiles
    {
        [Serializable] sealed class Data { public NativeVehicleHapticProfile[] vehicles; }
        static Data data;
        public static NativeVehicleHapticProfile Get(string id)
        {
            if (data == null)
            {
                var file = NativeContent.Load<TextAsset>("NativeSettings/haptics-profiles");
                if (!file) throw new InvalidOperationException("Haptic profiles resource missing");
                data = JsonUtility.FromJson<Data>(file.text);
                if (data.vehicles == null || data.vehicles.Length != 7) throw new InvalidOperationException("Seven haptic vehicle profiles required");
            }
            foreach (var p in data.vehicles) if (string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return data.vehicles[0];
        }
        public static string Vehicle(NativeKartSettings s)
        {
            if (s == null) return "kart";
            if (s._moduleKind == NativeVehicleModuleKind.BroomAirBoost) return "broom";
            if (s._moduleKind == NativeVehicleModuleKind.GenjiGrassBoost) return "genji";
            if (s._moduleKind == NativeVehicleModuleKind.ShoppingCartDriftCap) return "shoppingcart";
            if (s._canWheelie) return "bike";
            if (s._autoAccel) return "cleanerbot";
            return s._turnLoseSpeed < 1 ? "skate" : "kart";
        }
    }

    public struct NativeHapticOutput
    {
        public float low, high;
        public NativeHapticOutput(float low, float high) { this.low=low; this.high=high; }
    }

    public sealed class NativeHapticMixer
    {
        struct Voice
        {
            public bool active, decay;
            public double start;
            public float on, gap, low, high;
            public int pulses, priority;
            public NativeHapticKind kind;
            public double End => start + on * pulses + gap * (pulses-1);
        }
        Voice voice;
        long lastSequence;
        double silenceUntil;
        double offroadSince=-1;
        int silencePriority;
        public float Strength=.75f;
        public bool ImpactsEnabled=true, MechanismsEnabled=true, SurfaceEnabled=true, LegacyComparison;
        public NativeHapticResponse Response=new NativeHapticResponse();
        public int AcceptedEvents { get; private set; }
        public NativeHapticKind LastEvent { get; private set; }
        public float BackgroundGain { get; private set; }
        public bool HardSilence { get; private set; }
        public string Dominant => voice.active ? voice.kind.ToString() : BackgroundGain>0 ? "Offroad" : "Silence";
        public void Reset()
        {
            voice=default;lastSequence=0;silenceUntil=0;silencePriority=0;HardSilence=false;offroadSince=-1;BackgroundGain=0;
        }
        void Expire(double time)
        {
            if(voice.active && time>=voice.End)
            {
                silenceUntil=Math.Max(silenceUntil,voice.End+Mathf.Max(voice.low>0 ? Response.lowGap : 0,voice.high>0 ? Response.highGap : 0));
                silencePriority=voice.priority;voice.active=false;
            }
            HardSilence=time<silenceUntil;
        }
        public bool Submit(NativeHapticEvent e,NativeVehicleHapticProfile p,double time)
        {
            if(e.sequence<=lastSequence)return false;
            lastSequence=e.sequence;Expire(time);
            bool impact=e.kind==NativeHapticKind.Impact || e.kind==NativeHapticKind.Landing || e.kind==NativeHapticKind.Damage;
            if(impact ? !ImpactsEnabled : !MechanismsEnabled)return false;
            float k=Mathf.Clamp(e.magnitude,.08f,1);
            var v=new Voice { active=true,start=time,on=Mathf.Max(.15f,Response.highOn),gap=Response.highGap,high=.5f,pulses=1,priority=20,kind=e.kind };
            switch(e.kind)
            {
                case NativeHapticKind.Impact: v.low=k*.85f*p.impact;v.high=k*.62f;v.on=.22f*p.decay;v.decay=true;v.priority=90;break;
                case NativeHapticKind.Landing: v.low=k*.8f*p.landing;v.high=k*.22f;v.on=.25f*p.decay;v.decay=true;v.priority=80;break;
                case NativeHapticKind.Damage: v.low=.92f*p.impact;v.high=.75f;v.on=.42f;v.decay=true;v.priority=100;break;
                case NativeHapticKind.Boost: v.low=.52f+Mathf.Clamp(e.stage,0,3)*.1f;v.high=0;v.on=Mathf.Max(.28f,Response.lowOn);v.priority=60;break;
                case NativeHapticKind.Ready: v.pulses=2;v.priority=40;break;
                case NativeHapticKind.ChargeStage:
                    if(e.stage==2)return false;
                    v.pulses=e.stage>=3 ? 2 : 1;v.priority=40;break;
                case NativeHapticKind.CartRisk: v.pulses=1;v.on=e.stage>=2 ? Mathf.Max(.28f,Response.highOn) : v.on;v.priority=45;break;
                case NativeHapticKind.WheelieBegin: v.priority=30;break;
                case NativeHapticKind.WheelieEnd: v.on=Mathf.Max(.25f,Response.highOn);v.priority=30;break;
                case NativeHapticKind.Respawn: v.on=Mathf.Max(.28f,Response.highOn);break;
                case NativeHapticKind.Pickup: break;
                case NativeHapticKind.ItemUse: v.on=Mathf.Max(.28f,Response.highOn);break;
                case NativeHapticKind.CalibrationLow: v.low=.7f;v.high=0;v.on=.9f;v.priority=200;break;
                case NativeHapticKind.CalibrationHigh: v.high=.7f;v.on=.9f;v.priority=200;break;
                case NativeHapticKind.DriftEnter: case NativeHapticKind.Overheat:return false;
            }
            if(voice.active && v.priority<=voice.priority)return false;
            if(HardSilence && v.priority<=silencePriority)return false;
            voice=v;silenceUntil=0;HardSilence=false;offroadSince=-1;BackgroundGain=0;AcceptedEvents++;LastEvent=e.kind;return true;
        }
        public NativeHapticOutput Evaluate(NativeHapticState s,NativeVehicleHapticProfile p,double time,float dt)
        {
            Expire(time);
            BackgroundGain=0;
            if(voice.active && voice.kind==NativeHapticKind.CartRisk && (!s.drifting || s.module!=NativeVehicleModuleKind.ShoppingCartDriftCap))
            {
                voice.active=false;silenceUntil=time+Response.highGap;silencePriority=45;HardSilence=true;
            }
            if(voice.active)
            {
                offroadSince=-1;
                float local=(float)(time-voice.start);
                if(local<0)return default;
                local%=voice.on+voice.gap;
                // One voice owns both channels, including its complete off interval.
                if(local>=voice.on)return default;
                float envelope=voice.decay ? Mathf.Clamp01(local/.012f)*Mathf.Pow(Mathf.Clamp01(1-local/voice.on),1.7f) : 1;
                float low=voice.low*envelope*Strength,high=voice.high*envelope*Strength;
                if(Response.measured && !voice.decay && Strength>0 && voice.priority<200)
                {
                    if(low>0)low=Mathf.Max(low,Response.lowStart);
                    if(high>0)high=Mathf.Max(high,Response.highStart);
                }
                return new NativeHapticOutput(Mathf.Clamp01(low),Mathf.Clamp01(high));
            }
            if(LegacyComparison && !HardSilence)return NativeHapticLegacyComparison.Evaluate(s,p,time,Strength);
            if(HardSilence || !SurfaceEnabled || Strength<=0 || !s.driving || s.frozen || !s.grounded || !s.grass || s.speed<.02f)
            {
                offroadSince=-1;return default;
            }
            if(offroadSince<0)offroadSince=time;
            double elapsed=time-offroadSince;
            if(elapsed<.08)return default;
            // The measured short-pulse floor starts the rotor; sustained road feel uses a lower command.
            float sustain=Mathf.Clamp((.14f+.2f*Mathf.Sqrt(Mathf.Clamp01(s.speed)))*p.surface,.1f,.32f)*Strength;
            float roadHigh=elapsed<.08+Mathf.Max(.15f,Response.highOn) ? Mathf.Max(.5f*Strength,Response.measured ? Response.highStart : 0) : sustain;
            BackgroundGain=Mathf.Clamp01(roadHigh);
            return new NativeHapticOutput(0,BackgroundGain);
        }
    }

    // The former multi-source bed remains available in the guided A/B comparison.
    public static class NativeHapticLegacyComparison
    {
        static float Pulse(double time, float hz, float duty=.35f)
        {
            float phase=(float)(time*hz-Math.Floor(time*hz));
            return phase < duty ? Mathf.Sin(phase/duty*Mathf.PI) : 0;
        }
        static float Texture(float distance)
        {
            return Mathf.Clamp01(.5f + .23f*Mathf.Sin(distance*1.71f) + .17f*Mathf.Sin(distance*4.37f+.8f) + .1f*Mathf.Sin(distance*8.13f));
        }
        public static NativeHapticOutput Evaluate(NativeHapticState s, NativeVehicleHapticProfile p, double time, float strength)
        {
            float low=0, high=0, cueLow=0, cueHigh=0;
            if (s.driving && !s.frozen)
            {
                low=p.propulsion*(.025f+.075f*s.speed)*Mathf.Clamp01(s.throttle*2);
                low*=Mathf.Sqrt(Mathf.Max(0,p.mass));
                if (p.id == "genji") low*=.5f+.5f*Pulse(time,1.7f,.65f);
                if (s.grounded)
                {
                    float texture=Texture(s.distance);
                    high=p.surface*s.speed*(s.grass ? .12f+.22f*texture : s.water ? .07f+.11f*texture : .012f+.025f*texture);
                    if (p.irregularity > .6f) { high+=p.irregularity*s.speed*(.035f+.10f*texture); low+=.035f*s.speed*texture; }
                    if (s.drifting) high+=p.drift*(.08f+.22f*s.speed)*(.65f+.35f*Pulse(time,6,.65f));
                    if (s.wheelie) high*=.42f;
                }
                if (s.module == NativeVehicleModuleKind.ShoppingCartDriftCap && s.charge >= .7f)
                {
                    cueHigh=(.3f+.35f*s.charge)*Pulse(time,s.charge >= .9f ? 7 : 3,.55f);
                    high*=.28f; low*=.5f;
                }
                if (s.module == NativeVehicleModuleKind.BroomAirBoost && s.charge > .05f)
                    cueHigh=(.05f+.28f*s.charge)*Pulse(time,1.4f+2*s.charge,.6f);
                if (s.module == NativeVehicleModuleKind.GenjiGrassBoost && s.grounded && s.grass)
                    cueLow=(.12f+.32f*s.charge)*Pulse(time,1.2f+2.3f*s.charge,.45f);
                if (s.boost > 0 && s.module != NativeVehicleModuleKind.ShoppingCartDriftCap) low+=.1f;
                if (p.id == "cleanerbot") high+=.025f*s.speed*(.7f+.3f*Pulse(time,4+s.steering*3,.8f));
                if (s.threat > 0) cueHigh=Mathf.Max(cueHigh,(.25f+.3f*s.threat)*Pulse(time,1.5f+5*s.threat));
            }
            return new NativeHapticOutput(Mathf.Clamp01(Mathf.Max(Mathf.Min(.4f,low),cueLow)*strength),Mathf.Clamp01(Mathf.Max(Mathf.Min(.5f,high),cueHigh)*strength));
        }
    }
}
