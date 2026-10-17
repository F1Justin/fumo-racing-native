using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public struct NativeKartSteeringInput
    {
        public float speed, wishTurn, wishTurnAdd, driftDirection, extraSteer;
        public bool reversed;
    }

    [Serializable]
    public struct NativeKartSteeringState
    {
        public float currentTurn, currentSpeed, turnLimit;

        public static NativeKartSteeringState Step(float previousTurn, NativeKartSteeringInput input,
            NativeKartSettings settings, float dt)
        {
            float wish = Mathf.Clamp(input.wishTurn, -1f, 1f);
            float delta = (wish + input.wishTurnAdd) * (input.reversed ? -1f : 1f)
                * settings._turnForce * dt;
            float lowBonus = Mathf.Lerp(0f, settings._turnLowMult * settings._turnMax,
                1f - Mathf.Clamp01(input.speed / 15f));
            float turn = previousTurn;
            if (wish == 0f || Mathf.Abs(turn + delta) < Mathf.Abs(turn))
                turn = Mathf.MoveTowards(turn, 0f, settings._turnFriction * dt);
            turn += dt * settings._driftAutoIntensity * input.driftDirection;
            float limit = lowBonus + settings._turnMax
                + Mathf.Abs(input.driftDirection) * settings._driftSteerGain + input.extraSteer;
            float cap = Mathf.Clamp01(Mathf.Abs(wish)) * limit;
            float candidate = turn + delta;
            bool increasing = Mathf.Abs(candidate) - Mathf.Abs(turn) > 0f;
            // The native branch tests the candidate strictly below the cap, then clamps the stored turn.
            if (Mathf.Abs(candidate) < cap) turn = candidate;
            if (increasing && Mathf.Abs(turn) > cap) turn = Mathf.Sign(turn) * cap;
            float loss = Mathf.Abs(turn) / settings._turnMax * settings._turnLoseSpeed * dt
                * (input.speed / settings._topSpeed) * (((int)Mathf.Abs(input.driftDirection)) ^ 1);
            return new NativeKartSteeringState
            {
                currentTurn = turn, currentSpeed = input.speed - loss, turnLimit = limit
            };
        }

        public Quaternion GroundRotation(Quaternion rotation, Vector3 normal,
            NativeKartSettings settings, float dt)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 turnedForward = Quaternion.AngleAxis(currentTurn, normal) * forward;
            Quaternion aligned = Quaternion.FromToRotation(rotation * Vector3.up, normal) * rotation;
            Quaternion target = aligned * Quaternion.FromToRotation(forward, turnedForward);
            return Quaternion.Lerp(rotation, target, settings._smooth * dt);
        }
    }
}
