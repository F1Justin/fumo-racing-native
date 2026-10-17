using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        public static void RecordSourceAudio(Transform producer,string key)
        {var net=Instance;if(net&&net.IsHost&&net.InRace&&!net.Loading&&net.Race)net.Race.NetworkRecordSourceAudio(producer,key);}
    }
    public sealed partial class NativeRace
    {
        readonly Dictionary<int,List<NativeNetworkSourceAudioEvent>> networkAudioJournal=new Dictionary<int,List<NativeNetworkSourceAudioEvent>>();
        readonly NativeNetworkSourceAudioCursor networkAudioCursor=new NativeNetworkSourceAudioCursor();
        internal int NetworkAudioConsumed,NetworkAudioPlayed;
        internal bool NetworkAudioAvailable;
        internal int NetworkAudioEventsBytes,NetworkAudioMaximumSnapshotBytes;
        readonly List<NativeNetworkAudioInvocation> networkAudioInvocations=new List<NativeNetworkAudioInvocation>();
        internal NativeNetworkAudioInvocation[] NetworkAudioInvocations=>networkAudioInvocations.ToArray();
        void NetworkResetSourceAudio(){networkAudioJournal.Clear();networkAudioCursor.Reset();NetworkAudioConsumed=NetworkAudioPlayed=0;NetworkAudioAvailable=false;NetworkAudioEventsBytes=NetworkAudioMaximumSnapshotBytes=0;networkAudioInvocations.Clear();}
        internal void NetworkRecordSourceAudio(Transform producer,string key)
        {
            var motor=producer?producer.GetComponentInParent<NativeKartMotor>():null;
            int seat=System.Array.IndexOf(racers,motor);
            if(seat<0||raceConfig==null||!raceConfig.roster[seat].isHuman)return;
            List<NativeNetworkSourceAudioEvent> journal;
            if(!networkAudioJournal.TryGetValue(seat,out journal))networkAudioJournal[seat]=journal=new List<NativeNetworkSourceAudioEvent>();
            journal.Add(new NativeNetworkSourceAudioEvent{sequence=journal.Count+1L,key=key,producerEventPlayCount=NativeAudioRuntime.EventPlayCount,producerLastEventId=NativeAudioRuntime.LastEventId});
        }
        NativeNetworkSnapshot NetworkMeasureAudioPacket(NativeNetworkSnapshot snapshot)
        { NetworkAudioEventsBytes=snapshot.karts.Sum(k=>System.Text.Encoding.UTF8.GetByteCount(JsonUtility.ToJson(k.sourceAudio)));NetworkAudioMaximumSnapshotBytes=System.Math.Max(NetworkAudioMaximumSnapshotBytes,System.Text.Encoding.UTF8.GetByteCount(JsonUtility.ToJson(snapshot)));return snapshot;}
        NativeNetworkSourceAudioState NetworkReadSourceAudio(int seat)
        {
            List<NativeNetworkSourceAudioEvent> journal;networkAudioJournal.TryGetValue(seat,out journal);
            return new NativeNetworkSourceAudioState{round=Net.Round,seat=seat,sequence=journal==null?0:journal.Count,
                events=journal==null?new NativeNetworkSourceAudioEvent[0]:journal.Select(e=>new NativeNetworkSourceAudioEvent{sequence=e.sequence,key=e.key,producerEventPlayCount=e.producerEventPlayCount,producerLastEventId=e.producerLastEventId}).ToArray()};
        }
        void NetworkConsumeSourceAudio(NativeNetworkKart state,NativeKartMotor motor)
        {
            if(Net.IsHost||state.seat!=Net.LocalRacerIndex)return;
            NetworkAudioAvailable=state.sourceAudio!=null&&state.sourceAudio.round==Net.Round&&state.sourceAudio.seat==Net.LocalRacerIndex;
            foreach(string key in networkAudioCursor.Consume(state.sourceAudio,Net.Round,Net.LocalRacerIndex))
            {
                NetworkAudioConsumed++;
                int before=NativeAudioRuntime.EventPlayCount;
                if(key=="health.damage.stop")NativeAudioRuntime.StopEvent("health.damage",motor.transform);
                else NativeAudioRuntime.PlayEvent(key,motor.transform);
                int after=NativeAudioRuntime.EventPlayCount;NetworkAudioPlayed+=after-before;
                networkAudioInvocations.Add(new NativeNetworkAudioInvocation{key=key,eventPlayBefore=before,eventPlayAfter=after,stop=key=="health.damage.stop"});
            }
        }
    }
}
