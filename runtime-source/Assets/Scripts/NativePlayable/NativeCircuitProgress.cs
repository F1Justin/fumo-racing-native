using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeCircuitProgress
    {
        readonly Vector3[] points;
        readonly float[] lengths;
        readonly float lapDistance;
        readonly int targetLaps;
        readonly bool continuousTraffic;
        public NativeRacerRankState SourceRankState=>new NativeRacerRankState(CurrentTrigger,LastTrigger,CurrentTriggerIndex,CompletedLaps);
        public int CurrentTrigger { get; private set; } = -1;
        public int LastTrigger { get; private set; } = -1;
        public int CurrentTriggerIndex { get; private set; } = -1;
        public int CompletedLaps { get; private set; }
        public int ExitEvents { get; private set; }
        public int Count => points.Length;
        public int Passed => Finished ? Count : CurrentTrigger;
        public int Target => Next(CurrentTriggerIndex);
        public bool Started { get; private set; }
        public bool Finished => !continuousTraffic && CompletedLaps >= targetLaps;

        public NativeCircuitProgress(Vector3[] gatePositions, int laps = 1, bool continuousTraffic = false)
        {
            if (gatePositions == null || gatePositions.Length < 2)
                throw new ArgumentException("At least two circuit gates are required", nameof(gatePositions));
            points = (Vector3[])gatePositions.Clone();
            if (laps < 1 || laps > 999) throw new ArgumentOutOfRangeException(nameof(laps));
            targetLaps = laps; this.continuousTraffic = continuousTraffic;
            lengths = new float[Count];
            for (int i = 0; i < Count; i++)
            {
                lengths[i] = Vector3.Distance(points[i], points[Next(i)]);
                lapDistance += lengths[i];
            }
        }

        public bool Cross(int index, bool forward)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (Finished) return false;
            ExitEvents++;
            if (forward)
            {
                if (index == CurrentTriggerIndex) return false;
                bool lap = CurrentTrigger >= Count - 1 && index == 0;
                if (index > Next(CurrentTriggerIndex) && CurrentTrigger >= 0)
                    SetCount(index, index - 1);
                else
                    SetCount(CurrentTrigger + 1, CurrentTrigger);
                if (index == 0) Started = true;
                if (lap)
                {
                    CompletedLaps++;
                    CurrentTrigger = 0;
                }
                CurrentTriggerIndex = index;
                return true;
            }

            int previous = index == 0 ? Count - 1 : index - 1;
            if (CurrentTriggerIndex == previous || CurrentTriggerIndex == index - 1) return false;
            if (index >= Next(CurrentTriggerIndex) && CurrentTrigger >= 0)
                SetCount(index, index - 1);
            else
                SetCount(CurrentTrigger - 1, CurrentTrigger);
            CurrentTriggerIndex = previous;
            return true;
        }

        public int PrepareRecovery()
        {
            int gate = LastTrigger;
            if (gate < 0 || (CurrentTrigger == 0 && gate >= 1)) gate = 0;
            SetCount(gate - 1, gate - 2);
            return gate;
        }

        public float Fraction(Vector3 position)
        {
            if (Finished) return targetLaps;
            if (lapDistance <= 0f) return 0f;
            if (CurrentTrigger < 0 && CurrentTriggerIndex >= 0 && LastTrigger <= 1) return -1f;
            float completedDistance = 0f;
            for (int i = 0; i < CurrentTriggerIndex; i++) completedDistance += lengths[i];
            if (CurrentTriggerIndex >= 0) completedDistance += lengths[CurrentTriggerIndex];
            completedDistance -= Vector3.Distance(position, points[Target]);
            return CompletedLaps + completedDistance / lapDistance;
        }

        void SetCount(int current, int last)
        {
            CurrentTrigger = current;
            LastTrigger = last;
        }

        int Next(int index) => index + 1 >= Count ? 0 : index + 1;
    }
}
