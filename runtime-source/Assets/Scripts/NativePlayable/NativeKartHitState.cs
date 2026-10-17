using System;
using System.Collections.Generic;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartHitState
    {
        struct Pending { public double due; public bool resume, damaged; public int order; }
        readonly List<Pending> pending = new List<Pending>();
        double clock;
        int order;
        public bool Invulnerable { get; private set; }
        public bool Frozen { get; private set; }
        public bool DamageVisible { get; private set; }
        public int AcceptedDamage { get; private set; }
        public int RejectedDamage { get; private set; }
        public int Resumes { get; private set; }
        public int PendingCount => pending.Count;
        public double Clock => clock;

        public void ApplyNetworkState(bool frozen, bool invulnerable, bool visible, int acceptedDamage)
        { Frozen = frozen; Invulnerable = invulnerable; DamageVisible = visible; AcceptedDamage = acceptedDamage; }

        public bool TakeDamage(float duration, bool working, bool itemAllowsDamage, Action accepted = null)
        {
            if (!working || !itemAllowsDamage || Invulnerable)
            {
                RejectedDamage++;
                return false;
            }
            Validate(duration);
            Invulnerable = Frozen = DamageVisible = true;
            AcceptedDamage++;
            accepted?.Invoke();
            Schedule(duration, true, true);
            return true;
        }

        public bool FreezeFor(float duration, Action accepted = null)
        {
            if (Invulnerable) return false;
            Validate(duration);
            Invulnerable = Frozen = true;
            accepted?.Invoke();
            Schedule(duration, true);
            return true;
        }

        public void MakeInvulnerable(float duration)
        {
            Validate(duration);
            Invulnerable = true;
            Schedule(duration, false);
        }

        public void Advance(float deltaTime, Action resumed = null, Action<bool> recovered = null)
        {
            if (deltaTime <= 0f) return;
            clock += deltaTime;
            while (pending.Count > 0 && pending[0].due <= clock)
            {
                var next = pending[0];
                pending.RemoveAt(0);
                Invulnerable = false;
                if (!next.resume) continue;
                Frozen = DamageVisible = false;
                Resumes++;
                resumed?.Invoke();
                recovered?.Invoke(next.damaged);
            }
        }

        public void Reset()
        {
            pending.Clear();
            clock = 0;
            order = AcceptedDamage = RejectedDamage = Resumes = 0;
            Invulnerable = Frozen = DamageVisible = false;
        }

        void Schedule(float delay, bool resume, bool damaged = false)
        {
            pending.Add(new Pending { due = clock + delay, resume = resume, damaged = damaged, order = order++ });
            pending.Sort((a, b) => a.due == b.due ? a.order.CompareTo(b.order) : a.due.CompareTo(b.due));
        }

        static void Validate(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
                throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }
}
