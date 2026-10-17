using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public struct NativeKartAccelerationInput
    {
        public float wishAccel, groundAngle, slopeLimit, topSpeedModifier, extraSpeed;
        public float driftDirection, currentTurn, turnLimit;
        public bool braking, onGrass;
    }

    [Serializable]
    public struct NativeKartAccelerationState
    {
        public float currentSpeed, targetTopSpeed, approachRate;
        public float speedAfterApproach, speedAfterBrake, speedBeforeCaps;
        public float driftDrainFactor, speedAfterDriftDrain;

        public static float GroundStickSpeed(float speedBeforeCaps, Vector3 forward, NativeKartSettings settings)
        {
            float ratio = Mathf.Clamp01(Vector3.Dot(Vector3.up * settings._stickForce,
                forward * speedBeforeCaps) / settings._topSpeed);
            return -((settings._stickForce * 0.5f - settings._stickForce) * ratio + settings._stickForce);
        }

        public static NativeKartAccelerationState Step(float speed, NativeKartAccelerationInput input,
            NativeKartSettings settings, float dt)
        {
            float wish = Mathf.Clamp01(input.wishAccel);
            bool drifting = input.driftDirection != 0f;
            float baseTarget = input.onGrass
                ? (drifting ? settings._driftGrassTopSpeed : settings._topSpeedGrass) : settings._topSpeed;
            float friction = input.onGrass
                ? (drifting ? settings._driftGrassFriction : settings._frictionGrass) : settings._friction;
            float slopeSpeed = input.groundAngle / input.slopeLimit
                * (input.groundAngle < 0f ? settings._slopeSpeedLoss : settings._slopeSpeedGain);
            slopeSpeed = Mathf.Clamp(slopeSpeed, 0f, settings._slopeSpeedGain);
            float targetTopSpeed = Mathf.Abs(input.driftDirection) * settings._driftSpeedGain * (input.onGrass ? 0f : 1f)
                + input.topSpeedModifier * (input.extraSpeed + (slopeSpeed + baseTarget));
            // The original uses surface friction whenever the effective target is below the base top speed.
            bool useFriction = (speed > wish * settings._topSpeed && wish <= 0f)
                || settings._topSpeed > targetTopSpeed
                || (wish > 0f && speed >= wish * targetTopSpeed);
            float rate = useFriction ? friction : settings._acceleration;
            float targetSpeed = wish > 0f ? wish * targetTopSpeed : 0f;
            speed = Mathf.MoveTowards(speed, targetSpeed, dt * rate);
            float speedAfterApproach = speed;

            if (input.braking && !drifting)
                speed = Mathf.MoveTowards(speed, 0f, settings._brakeForce * dt);
            float speedAfterBrake = speed;
            float drain = 1f;
            if (Mathf.Abs(input.driftDirection * input.turnLimit - input.currentTurn) <= input.turnLimit)
                drain = 1f - Mathf.Clamp01(Mathf.Abs(input.currentTurn) / (input.turnLimit - settings._driftDrainAngle));
            if (drifting)
                speed = Mathf.MoveTowards(speed, 0f, settings._driftSpeedDrain * dt * drain);
            float speedAfterDriftDrain = speed;
            if (speed < targetTopSpeed)
            {
                float slopeAcceleration = input.groundAngle / input.slopeLimit
                    * (input.groundAngle < 0f ? settings._slopeAccelLoss : settings._slopeAccelGain);
                slopeAcceleration = Mathf.Clamp(slopeAcceleration, 0f, settings._slopeAccelGain);
                speed += dt * slopeAcceleration;
            }
            float speedBeforeCaps = speed;
            float speedCap = input.topSpeedModifier * settings._topSpeed
                + settings._topSpeedAdd + settings._driftSpeedMaxGain;
            speed = Mathf.Clamp(speed, 0f, speedCap);
            speed = Mathf.Clamp(speed, 0f, settings._topSpeedCap);
            return new NativeKartAccelerationState
            {
                currentSpeed = speed, targetTopSpeed = targetTopSpeed, approachRate = rate,
                speedAfterApproach = speedAfterApproach, speedAfterBrake = speedAfterBrake,
                speedBeforeCaps = speedBeforeCaps, driftDrainFactor = drain, speedAfterDriftDrain = speedAfterDriftDrain
            };
        }
    }
}
