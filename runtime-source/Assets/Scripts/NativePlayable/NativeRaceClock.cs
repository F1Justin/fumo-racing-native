using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeRaceClock
    {
        readonly Func<double> time;
        double started, stoppedElapsed;
        public bool Running { get; private set; }
        public double Elapsed => Running ? Math.Max(0, time() - started) : stoppedElapsed;
        public NativeRaceClock(Func<double> time = null) { this.time = time ?? (() => Time.timeSinceLevelLoadAsDouble); }
        public void Start() { StartAt(time()); }
        public void StartAt(double timestamp)
        {
            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp)) throw new ArgumentOutOfRangeException(nameof(timestamp));
            started = timestamp; stoppedElapsed=0; Running = true;
        }
        public void StopAtElapsed(double authoritativeElapsed)
        {
            if(double.IsNaN(authoritativeElapsed)||double.IsInfinity(authoritativeElapsed)||authoritativeElapsed<0)throw new ArgumentOutOfRangeException(nameof(authoritativeElapsed));
            stoppedElapsed=authoritativeElapsed;Running=false;
        }
        public void Reset() { started = 0; stoppedElapsed=0; Running = false; }
    }
}
