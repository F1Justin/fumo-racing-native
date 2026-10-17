using System;
using UnityEngine;
namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeKartAINavigationScope
    {
        public string track;
        public int[] waypointIndices = Array.Empty<int>();
        public bool driftTrial;
        public float maximumLaneDeviation;
        public bool ordinaryTargetComplete;
        public bool Contains(string id, int index) => string.Equals(track, id, StringComparison.OrdinalIgnoreCase)
            && Array.IndexOf(waypointIndices, index) >= 0;
    }
    [Serializable]
    public sealed class NativeKartAINavigationPolicy
    {
        public NativeKartAINavigationScope[] scopes = Array.Empty<NativeKartAINavigationScope>();
        public bool DriftTrialAllowed(string track, int index, float laneDeviation,
            bool grounded, bool grass, bool reversed, bool frozen, bool recoveryPending)
        {
            if (!grounded || grass || reversed || frozen || recoveryPending) return false;
            foreach (var scope in scopes)
                if (scope != null && scope.driftTrial && scope.ordinaryTargetComplete
                    && scope.maximumLaneDeviation >= 0f && laneDeviation <= scope.maximumLaneDeviation
                    && scope.Contains(track, index)) return true;
            return false;
        }
    }
    [Serializable]
    public struct NativeKartAINavigationSample
    {
        public string track, kind;
        public int sequence, racer, waypoint, reverseCount, spinGoal, flips;
        public float fixedTime, fixedDeltaTime, timeScale, speed, currentTurn, laneDeviation, stuckTime;
        public float topSpeed, defTopSpeed, turnMax, waypointWidth, correctWidth, driftFactor, driftAngleCondition;
        public Vector3 position, velocity, ordinaryTarget, guide;
        public Quaternion rotation;
        public float actualSteer, actualThrottle, actualDrift, shadowSteer, shadowDrift;
        public float effectiveGroundWish, appliedWishAccel, actualAirTurn;
        public bool assisted, driving, grounded, grass, water, frozen, reversed, autoAccel;
        public bool brake, directDrift, drifting, passed, strict, spinRequested, recoveryPending, driftTrial;
    }
}
