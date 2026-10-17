using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeHapticResponse
    {
        public int version=2;
        public bool measured;
        public string identity, evidence;
        public float lowOn=.18f,highOn=.15f,lowGap=.35f,highGap=.25f;
        public float lowStart,highStart;
        public float lowUpTau,lowDownTau,highUpTau,highDownTau;
        public static string FileFor(string identity)
        {
            using(var sha=SHA256.Create())
            {
                string key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").Substring(0,16);
                return Path.Combine(Application.persistentDataPath,"haptics-response-"+key+".json");
            }
        }
        public static NativeHapticResponse Load(string identity)
        {
            string file=FileFor(identity);
            if(File.Exists(file))
            {
                try
                {
                    var p=JsonUtility.FromJson<NativeHapticResponse>(File.ReadAllText(file));
                    if(p!=null && p.identity==identity && p.measured && p.lowOn>=.05f && p.highOn>=.05f && p.lowGap>=.05f && p.highGap>=.05f)
                    {
                        p.lowOn=Mathf.Clamp(p.lowOn,.05f,1);p.highOn=Mathf.Clamp(p.highOn,.05f,1);
                        p.lowGap=Mathf.Clamp(p.lowGap,.05f,1);p.highGap=Mathf.Clamp(p.highGap,.05f,1);return p;
                    }
                }
                catch(Exception e) { Debug.LogWarning("NATIVE_HAPTICS_RESPONSE "+e.GetType().Name); }
            }
            return new NativeHapticResponse { identity=identity };
        }
    }

    public sealed class NativeHapticMotorEstimate
    {
        float low,high;
        public NativeHapticOutput Update(NativeHapticOutput command,NativeHapticResponse p,float dt)
        {
            if(p.lowUpTau<=0 || p.lowDownTau<=0 || p.highUpTau<=0 || p.highDownTau<=0)return new NativeHapticOutput(-1,-1);
            low=Step(low,command.low,p.lowStart,p.lowUpTau,p.lowDownTau,dt);
            high=Step(high,command.high,p.highStart,p.highUpTau,p.highDownTau,dt);
            return new NativeHapticOutput(low*low,high*high);
        }
        static float Step(float current,float command,float deadZone,float up,float down,float dt)
        {
            float target=command<deadZone ? 0 : command;
            return Mathf.Lerp(current,target,1-Mathf.Exp(-Mathf.Max(0,dt)/(target>current ? up : down)));
        }
    }

    public sealed class NativeHapticOutputGate
    {
        NativeHapticOutput last;
        double next,lastSent;
        bool sent;
        public bool ShouldSend(NativeHapticOutput o,double time)
        {
            bool zeroEdge=sent && ((last.low>0 && o.low==0) || (last.high>0 && o.high==0));
            bool startEdge=(!sent || (last.low==0 && last.high==0)) && (o.low>0 || o.high>0);
            if(zeroEdge || startEdge)return true;
            if(time<next)return false;
            bool changed=Mathf.Abs(o.low-last.low)>.007f || Mathf.Abs(o.high-last.high)>.007f;
            return (!sent && (o.low>0 || o.high>0)) || changed || ((o.low>0 || o.high>0) && time-lastSent>.3);
        }
        public void Sent(NativeHapticOutput o,double time) { last=o;lastSent=time;next=time+1.0/60;sent=true; }
        public void Reset() { last=default;next=lastSent=0;sent=false; }
    }

    public sealed class NativeHapticContactLatch
    {
        readonly Dictionary<int,double> contacts=new Dictionary<int,double>();
        public bool Touch(int source,double time,bool significant)
        {
            bool fresh=!contacts.TryGetValue(source,out var last) || time-last>.15;
            contacts[source]=time;
            return fresh && significant;
        }
        public void Reset() => contacts.Clear();
    }
}
