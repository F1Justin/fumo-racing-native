using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartAnimationActions : MonoBehaviour
    {
        public NativeKartAnimation rig;
        public bool Frozen { get; private set; }
        public int AcceptedTriggers { get; private set; }
        public int SuppressedTriggers { get; private set; }
        public int DriftTriggers { get; private set; }
        public int ManualJumpTriggers { get; private set; }
        public int JumpTriggers { get; private set; }
        public int FallTriggers { get; private set; }
        public int LandTriggers { get; private set; }
        public string LastTrigger { get; private set; }
        public int LastTriggerFrame { get; private set; }

        public void SetTrigger(string trigger)
        {
            if (Frozen) { SuppressedTriggers++; return; }
            rig.animator.SetTrigger(trigger);
            AcceptedTriggers++;
            LastTrigger = trigger;
            LastTriggerFrame = Time.frameCount;
            switch(trigger)
            {
                case "driftStart": DriftTriggers++; break;
                case "manualJump": ManualJumpTriggers++; break;
                case "jump": JumpTriggers++; break;
                case "fall": FallTriggers++; break;
                case "strongLand": LandTriggers++; break;
            }
        }

        public void ObserveSimulation(bool driftEntered, bool manualJump, bool leftGround, bool landed, float departureGravity)
        {
            if (driftEntered) SetTrigger("driftStart");
            if (manualJump) SetTrigger("manualJump");
            if (landed) SetTrigger("strongLand");
            else if (leftGround) SetTrigger(departureGravity >= 5f ? "jump" : "fall");
        }

        public void Freeze(float duration)
        {
            Frozen = true;
            CancelInvoke(nameof(UnFreeze));
            Invoke(nameof(UnFreeze), duration);
        }

        void UnFreeze() { Frozen = false; }

        public void ResetActions()
        {
            CancelInvoke(nameof(UnFreeze));
            Frozen = false;
            AcceptedTriggers = SuppressedTriggers = DriftTriggers = ManualJumpTriggers = JumpTriggers = FallTriggers = LandTriggers = 0;
            LastTrigger = string.Empty;
            LastTriggerFrame = -1;
            if (!rig || !rig.animator) return;
            foreach (var parameter in rig.animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Trigger) rig.animator.ResetTrigger(parameter.nameHash);
        }
    }
}
