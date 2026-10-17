using System;
namespace Fumo.NativePlayable
{
    public sealed class NativeSourceLapTimer
    {
        readonly TimeSpan[] laps;
        public bool Ended { get; private set; }
        public int CurrentLap { get; private set; }
        double lastTimestamp;
        public NativeSourceLapTimer(int lapCount) { laps = new TimeSpan[lapCount]; }
        public void StartedRace(double gameTime) { lastTimestamp = gameTime; }
        public void UpdateTimer(double gameTime)
        {
            if (Ended || CurrentLap >= laps.Length) return;
            double seconds = gameTime - lastTimestamp;
            // Original TimeSpan.Interval rounds milliseconds away from zero before constructing ticks.
            long milliseconds = (long)(seconds * 1000.0 + (seconds >= 0 ? .5 : -.5));
            var live = new TimeSpan(checked(milliseconds * TimeSpan.TicksPerMillisecond));
            if (live.TotalMinutes < 59) laps[CurrentLap] = live;
        }
        public void SetCurrentLap(int newLap) { CurrentLap = newLap; }
        public void RefreshLapTime(double gameTime) { lastTimestamp = gameTime; }
        public bool EndRace() { if (Ended) return false; Ended = true; return true; }
        public TimeSpan GetTotalTime() { TimeSpan total = TimeSpan.Zero; foreach (var lap in laps) total = total.Add(lap); return total; }
        public NativeSourceLapClock ExportNetworkClock()
        { return new NativeSourceLapClock { sourceLapTicks=Array.ConvertAll(laps,lap=>lap.Ticks),sourceLapIndex=CurrentLap,sourceLastTimestamp=lastTimestamp,sourceTimerEnded=Ended }; }
        public void ImportNetworkClock(NativeSourceLapClock actualAuthority)
        {
            if(actualAuthority==null||actualAuthority.sourceLapTicks==null||actualAuthority.sourceLapTicks.Length!=laps.Length
                ||actualAuthority.sourceLapIndex<0||actualAuthority.sourceLapIndex>laps.Length
                ||double.IsNaN(actualAuthority.sourceLastTimestamp)||double.IsInfinity(actualAuthority.sourceLastTimestamp))
                throw new ArgumentException("Authoritative source lap clock layout differs");
            for(int i=0;i<laps.Length;i++)laps[i]=TimeSpan.FromTicks(actualAuthority.sourceLapTicks[i]);
            CurrentLap=actualAuthority.sourceLapIndex;lastTimestamp=actualAuthority.sourceLastTimestamp;Ended=actualAuthority.sourceTimerEnded;
        }
        public double[] LapSeconds() { var result = new double[laps.Length]; for (int i = 0; i < result.Length; i++) result[i] = laps[i].TotalSeconds; return result; }
    }
}
