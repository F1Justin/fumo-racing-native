using System;
namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeSourceLapClock
    {
        public long[] sourceLapTicks;
        public int sourceLapIndex;
        public double sourceLastTimestamp;
        public bool sourceTimerEnded;
    }
}
