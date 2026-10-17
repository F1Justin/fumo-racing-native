using System;
using System.Collections.Generic;
using System.Linq;
namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeNetworkAudioInvocation {public string key;public int eventPlayBefore,eventPlayAfter;public bool stop;}
    [Serializable] public sealed class NativeNetworkSourceAudioEvent { public long sequence; public string key,producerLastEventId;public int producerEventPlayCount; }
    [Serializable] public sealed class NativeNetworkSourceAudioState
    { public int round,seat; public long sequence; public NativeNetworkSourceAudioEvent[] events; }
    public sealed class NativeNetworkSourceAudioCursor
    {
        int round,seat;long sequence;bool initialized;
        public void Reset(){initialized=false;sequence=0;}
        public string[] Consume(NativeNetworkSourceAudioState state,int actualRound,int actualSeat)
        {
            if(state==null||state.round!=actualRound||state.seat!=actualSeat||state.sequence<0)return new string[0];
            if(!initialized||round!=actualRound||seat!=actualSeat)
            {initialized=true;round=actualRound;seat=actualSeat;sequence=state.sequence;return new string[0];}
            if(state.sequence<=sequence)return new string[0];
            var pending=(state.events??new NativeNetworkSourceAudioEvent[0]).Where(e=>e!=null&&e.sequence>sequence&&e.sequence<=state.sequence).OrderBy(e=>e.sequence).ToArray();
            long expected=sequence+1;
            foreach(var e in pending){if(e.sequence!=expected||string.IsNullOrEmpty(e.key))throw new InvalidOperationException("Network source audio journal gap/identity");expected++;}
            if(expected!=state.sequence+1)throw new InvalidOperationException("Network source audio journal incomplete");
            sequence=state.sequence;return pending.Select(e=>e.key).ToArray();
        }
    }
}
