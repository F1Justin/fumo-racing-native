using System;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeItemActor
    {
        bool insanityStarted, insanityEffected, insanityLease;
        NativeKartMotor insanityTarget;
        int insanityGeneration;
        public bool InsanityStarted => insanityStarted;
        public bool InsanityEffected => insanityEffected;
        public string InsanityCollisionConsumer { get; private set; }
        public int InsanityHudCount { get; private set; }
        internal void BeginInsanity(NativeKartMotor receiver, float duration, string consumer)
        {
            if (insanityEffected) return;
            var router = world.GetComponent<NativeInsanityHudRouter>();
            if (!router || !router.Initialized) throw new InvalidOperationException("Insanity human/HUD route was not initialized");
            if (!insanityStarted)
            {
                insanityTarget = receiver;
                insanityGeneration = receiver.AcquireInsanityReverse();
                insanityLease = true;
                insanityStarted = true;
                InsanityCollisionConsumer = consumer;
                int slot = router.FindHumanSlot(receiver);
                // CPU has no HUD. Native schedules recovery instead of reproducing the original partial exception.
                if (slot >= 0)
                {
                    var source = GetComponentInChildren<NativeInsanityHudSource>(true);
                    if (!source || !source.hudPrefab) throw new InvalidOperationException("Insanity source HUD prefab missing");
                    router.SpawnForPlayer(source, receiver); InsanityHudCount++;
                }
            }
            Invoke(nameof(EndInsanity), duration);
            insanityEffected = true;
        }
        void EndInsanity()
        {
            if (!insanityEffected) return;
            ReleaseInsanityLease();
            DestroyNow();
            insanityEffected = false;
        }
        void ReleaseInsanityLease()
        {
            if (!insanityLease) return;
            insanityLease = false;
            CancelInvoke(nameof(EndInsanity));
            if (insanityTarget) insanityTarget.ReleaseInsanityReverse(insanityGeneration);
        }
    }
}
