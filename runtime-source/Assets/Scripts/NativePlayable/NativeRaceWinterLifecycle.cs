using System;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        public event Action OriginalRaceStarted;
        public event Action OriginalRaceFinish;
        public int OriginalRaceStartedCount { get; private set; }
        public int OriginalRaceFinishedCount { get; private set; }
        public bool OriginalCircuitPrepared { get; private set; }

        void RaiseOriginalRaceStarted()
        {
            OriginalCircuitPrepared = true;
            OriginalRaceStartedCount++;
            OriginalRaceStarted?.Invoke();
        }

        internal void ObserveOriginalRaceFinish()
        {
            OriginalCircuitPrepared = false;
            OriginalRaceFinishedCount++;
            OriginalRaceFinish?.Invoke();
        }
    }
}
