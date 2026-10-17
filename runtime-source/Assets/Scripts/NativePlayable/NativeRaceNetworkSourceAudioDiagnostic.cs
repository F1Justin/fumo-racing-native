using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class NetworkAudioDiagnosticReport
        {public string scope="host remote-seat injected production callbacks; TCP/client ordinary hearing pending",error;public int round,seat=1,eventPlayBefore,eventPlayAfter,eventsBytes,maximumSnapshotBytes;public bool passed,audioEventInvocationObserved;public string[] stages;public NativeNetworkSourceAudioState journal;}
        bool networkAudioDiagnosticRunning;
        void BeginNetworkSourceAudioDiagnostic()
        {if(Net.IsHost&&!networkAudioDiagnosticRunning&&Environment.GetCommandLineArgs().Contains("--net-source-audio-diagnostic"))StartCoroutine(GuardNetworkSourceAudioDiagnostic());}
        IEnumerator GuardNetworkSourceAudioDiagnostic()
        {
            networkAudioDiagnosticRunning=true;var report=new NetworkAudioDiagnosticReport{round=Net.Round,eventPlayBefore=NativeAudioRuntime.EventPlayCount};
            var steps=new List<string>();var iterator=RunNetworkSourceAudioDiagnostic(steps);Exception error=null;
            while(true){bool next=false;object value=null;try{next=iterator.MoveNext();if(next)value=iterator.Current;}catch(Exception ex){error=ex;}if(error!=null||!next)break;yield return value;}
            (iterator as IDisposable)?.Dispose();report.error=error?.ToString();report.stages=steps.ToArray();report.eventPlayAfter=NativeAudioRuntime.EventPlayCount;report.audioEventInvocationObserved=report.eventPlayAfter>report.eventPlayBefore;report.journal=NetworkReadSourceAudio(1);report.eventsBytes=NetworkAudioEventsBytes;report.maximumSnapshotBytes=NetworkAudioMaximumSnapshotBytes;report.passed=error==null&&steps.Count==7;
            string directory=Argument(Environment.GetCommandLineArgs(),"--net-report-dir")??Application.persistentDataPath;Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"network-source-audio-diagnostic.json"),JsonUtility.ToJson(report,true));Debug.Log("NATIVE_NETWORK_SOURCE_AUDIO_DIAGNOSTIC "+(report.passed?"PASS":"FAIL"));networkAudioDiagnosticRunning=false;
        }
        IEnumerator RunNetworkSourceAudioDiagnostic(List<string> steps)
        {
            float deadline=Time.realtimeSinceStartup+45;while(phase!=Phase.Racing||Net.Loading){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Diagnostic racing wait timeout");yield return null;}
            if(racers.Length<2||!raceConfig.roster[1].isHuman)throw new InvalidOperationException("Diagnostic requires actual TCP remote human seat1");
            yield return new WaitForSeconds(1);
            var motor=racers[1];var inventory=motor.GetComponent<NativeKartItemInventory>();var hit=motor.GetComponent<NativeKartHit>();
            var choices=inventory.catalog.entries.Where(e=>e!=null&&e.family==NativeItemFamily.Throw&&!e.twoAction&&!e.useAtStart&&e.throwPrefab).Take(2).ToArray();if(choices.Length!=2)throw new InvalidOperationException("Two original throw items missing");
            foreach(var entry in choices){inventory.ResetItems();hit.ResetState();if(!inventory.TryGive(entry.id,false)||!inventory.UseItem())throw new InvalidOperationException("Production item use rejected "+entry.id);steps.Add("item."+entry.id);yield return new WaitForSeconds(.3f);}
            inventory.ResetItems();hit.ResetState();if(!hit.TakeDamage(.25f))throw new InvalidOperationException("Production damage rejected");steps.Add("health.damage");deadline=Time.realtimeSinceStartup+5;while(hit.Frozen){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Production recovery timeout");yield return null;}steps.Add("health.respawn");
            var ufo=motor.GetComponent<NativeUfoPlayer>();if(!ufo||!ufo.catalog)throw new InvalidOperationException("Original UFO resources missing");inventory.ResetItems();bool oldActive=ufo.active;ufo.active=true;ufo.ResetState();try{for(int i=0;i<3;i++)if(!ufo.Collect(i%ufo.catalog.colors.Length))throw new InvalidOperationException("Production UFO collect rejected");steps.Add("ufo.collect.0/1/2");if(!ufo.Use())throw new InvalidOperationException("Production UFO use rejected");steps.Add("ufo.use");}finally{ufo.active=oldActive;}
            yield return new WaitForSeconds(.5f);if(!networkAudioJournal.TryGetValue(1,out var journal)||!choices.All(e=>journal.Any(a=>a.key=="item."+e.id))||!new[]{"health.damage","health.respawn","ufo.collect.0","ufo.collect.1","ufo.collect.2","ufo.use"}.All(key=>journal.Any(a=>a.key==key)))throw new InvalidOperationException("Authoritative journal missing production event");string[] required=choices.Select(e=>"item."+e.id).Concat(new[]{"health.damage","health.respawn","ufo.collect.0","ufo.collect.1","ufo.collect.2","ufo.use"}).ToArray();int matched=0;for(int i=0;i<journal.Count;i++){if(journal[i].sequence!=i+1L)throw new InvalidOperationException("Journal sequence discontinuity");if(matched<required.Length&&journal[i].key==required[matched])matched++;}if(matched!=required.Length)throw new InvalidOperationException("Production event order differs");steps.Add("required production event order + exact full journal sequences verified");
        }
    }
}
