using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeKartMotor
    {
        public event Action<NativeHapticEvent> HapticEvent;
        long hapticSequence;
        Vector3 hapticApproachVelocity;
        readonly NativeHapticContactLatch hapticContacts=new NativeHapticContactLatch();
        int hapticCartRisk;
        void UpdateHapticCartRisk()
        {
            float charge=ModuleCharge/Mathf.Max(.01f,stats._moduleLimit);
            if(!IsDrifting || charge<.7f) { hapticCartRisk=0;return; }
            int stage=charge>=.9f ? 2 : 1;
            if(stage>hapticCartRisk) { hapticCartRisk=stage;PublishHaptic(NativeHapticKind.CartRisk,stage:stage,context:"shoppingcart"); }
        }
        public void PublishHaptic(NativeHapticKind kind, float magnitude=1, int stage=0, string context=null, int source=0)
        {
            HapticEvent?.Invoke(new NativeHapticEvent { sequence=++hapticSequence, kind=kind,
                magnitude=magnitude, stage=stage, context=context, source=source });
        }
    }
}
