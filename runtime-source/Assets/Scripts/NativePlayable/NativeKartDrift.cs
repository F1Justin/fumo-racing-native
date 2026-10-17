using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public struct NativeKartDriftInput
    {
        public bool requested, grounded, braking;
        public float wishAccel, wishTurn;
    }

    [Serializable]
    public struct NativeKartDriftState
    {
        public float direction, elapsed;

        public static NativeKartDriftState Enter(NativeKartDriftState previous, float direction,
            bool grounded, float driftAngle)
        {
            if (previous.direction != 0f || !grounded || driftAngle <= 0f) return previous;
            return new NativeKartDriftState { direction = direction, elapsed = 0f };
        }

        public static NativeKartDriftState Request(NativeKartDriftState previous, NativeKartDriftInput input,
            NativeKartSettings settings)
        {
            if (!input.requested || !input.grounded || !input.braking || Mathf.Clamp01(input.wishAccel) < 0.4f)
                return Stop(previous);
            float turn = Mathf.Clamp(input.wishTurn, -1f, 1f);
            if (Mathf.Abs(turn) < 0.1f) return previous;
            return Enter(previous, Mathf.Clamp(turn * 1000f, -1f, 1f), input.grounded, settings._driftAngle);
        }

        public static NativeKartDriftState DirectRequest(NativeKartDriftState previous,
            float direction, bool grounded, NativeKartSettings settings)
        {
            return direction == 0f ? Stop(previous) : Enter(previous, direction, grounded, settings._driftAngle);
        }

        public static NativeKartDriftState Stop(NativeKartDriftState previous)
        {
            return previous.direction == 0f ? previous : default;
        }

        public NativeKartDriftState AdvanceTime(float dt)
        {
            var next = this;
            if (next.direction != 0f) next.elapsed += dt;
            return next;
        }

        public static Quaternion VisualRotation(Quaternion previous, float direction,
            NativeKartSettings settings, float dt)
        {
            return Quaternion.Lerp(previous, Quaternion.Euler(0f, direction * settings._driftAngle, 0f),
                settings._driftSmooth * dt);
        }
    }
}
