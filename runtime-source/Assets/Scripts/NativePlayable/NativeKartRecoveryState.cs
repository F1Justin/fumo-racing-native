using System;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartRecoveryState
    {
        public const double Delay = .5;
        public bool Pending { get; private set; }
        public double Clock { get; private set; }
        public double Deadline { get; private set; }
        public int Requests { get; private set; }
        public int Teleports { get; private set; }

        public bool Request(bool restartPending)
        {
            if (Pending && !restartPending) return false;
            Pending = true;
            Deadline = Clock + Delay;
            Requests++;
            return true;
        }

        public bool Advance(float deltaTime)
        {
            if (deltaTime <= 0f) return false;
            Clock += deltaTime;
            if (!Pending || Clock < Deadline) return false;
            Pending = false;
            Teleports++;
            return true;
        }

        public void Reset()
        {
            Pending = false;
            Clock = Deadline = 0;
            Requests = Teleports = 0;
        }
    }
}
