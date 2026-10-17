using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public struct NativeKartSpinState
    {
        public float turned;
        public int flips;

        public static NativeKartSpinState Step(NativeKartSpinState previous, float bodyYaw)
        {
            previous.turned += bodyYaw;
            if (previous.turned >= 358f || previous.turned <= -358f)
            {
                previous.flips++;
                previous.turned = 0f;
            }
            return previous;
        }
    }

    public struct NativeKartSpinTurn
    {
        public float bodyYaw, velocityYaw;
    }

    public static class NativeKartSpin
    {
        public static NativeKartSpinTurn Turn(float wishTurn, bool spinning, float airTime,
            NativeKartSettings settings, float dt)
        {
            float wish = Mathf.Clamp(wishTurn, -1f, 1f);
            float ordinary = settings._turnForce * wish * dt * settings._turnVelAirMult;
            return new NativeKartSpinTurn
            {
                velocityYaw = spinning ? ordinary * 0.1f : ordinary,
                bodyYaw = spinning ? settings._turnForce * wish * dt * settings._turnAirMult
                    * Mathf.Clamp01(airTime * 3f) : ordinary
            };
        }
    }
}
