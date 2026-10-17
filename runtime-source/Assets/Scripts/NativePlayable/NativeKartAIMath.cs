using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAIMath
    {
        public struct EvaluateInput
        {
            public NativeKartAIWaypoint waypoint;
            public Vector3 position, velocity, target;
            public Quaternion rotation;
            public float currentTurn, deltaTime, speed, topSpeed, defTopSpeed, turnMax;
            public bool grounded, onGrass, reversed;
            public int spinGoal, flips;
        }

        public struct EvaluateResult
        {
            public float RawSteer, Steer, DirectDrift, Advance, CurrentLocalX, PredictedLocalX;
            public float TargetAngle, WaypointAngle, LocalZ;
            public bool HeadingMode, PassedWaypoint, SpinRequested;
        }

        public static Vector3 SelectTarget(NativeKartAIWaypoint[] waypoints, int index,
            Vector3 carPosition, float speed, float topSpeed, float randomLateral, out int count)
            => SelectTarget(waypoints, index, carPosition, speed, topSpeed, null, () => randomLateral, out count);

        public static Vector3 SelectTarget(NativeKartAIWaypoint[] waypoints, int index,
            Vector3 carPosition, float speed, float topSpeed,
            Func<int, Vector3, Vector3> resolveCandidate, Func<float> randomLateral, out int count)
        {
            if (waypoints == null || waypoints.Length == 0) throw new ArgumentException("No waypoints");
            if (index < 0 || index >= waypoints.Length) throw new ArgumentOutOfRangeException(nameof(index));
            var first = waypoints[index];
            float lookAhead = speed / Mathf.Max(0.001f, topSpeed) * 100f;
            count = 1;
            float distance = 0f;
            for (int offset = 1; offset < 6 && distance < lookAhead; offset++)
            {
                var candidate = waypoints[NextOrZero(index + offset + 1, waypoints.Length)];
                if (candidate.strict) break;
                float nextDistance = Vector3.Distance(candidate.position, carPosition);
                if (nextDistance < lookAhead)
                {
                    distance += nextDistance;
                    count++;
                }
            }

            Vector3 averagedOffset = Vector3.zero;
            for (int offset = 1; offset <= count; offset++)
            {
                int behindIndex = index + offset - 1;
                if (behindIndex >= waypoints.Length) continue;
                var behind = waypoints[behindIndex];
                int aheadIndex = NextOrZero(index + offset + 1, waypoints.Length);
                var ahead = waypoints[aheadIndex];
                if (ahead.strict) break;
                Vector3 candidate = resolveCandidate == null ? ahead.position : resolveCandidate(aheadIndex, ahead.position);
                averagedOffset += (candidate - behind.position) * 0.86f;
            }
            averagedOffset /= count;
            return first.position + averagedOffset +
                (first.rotation * Vector3.right) * randomLateral();
        }

        public static float MeasureLaneDeviation(NativeKartAIWaypoint[] waypoints, int index, Vector3 position)
        {
            int count = waypoints.Length;
            int previous = (index - 1 + count) % count;
            return Mathf.Sqrt(Mathf.Min(SegmentDistanceSquared(waypoints, previous, position),
                SegmentDistanceSquared(waypoints, index, position)));
        }

        static Vector3 OffsetPosition(NativeKartAIWaypoint waypoint) => waypoint.position
            + waypoint.rotation * Vector3.right * waypoint.offset;

        static float SegmentDistanceSquared(NativeKartAIWaypoint[] waypoints, int index, Vector3 position)
        {
            Vector3 from = OffsetPosition(waypoints[index]);
            Vector3 delta = Vector3.ProjectOnPlane(OffsetPosition(waypoints[(index + 1) % waypoints.Length]) - from, Vector3.up);
            float fraction = delta.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(position - from, Vector3.up), delta) / delta.sqrMagnitude)
                : 0f;
            Vector3 closest = from + delta * fraction;
            return Vector3.ProjectOnPlane(position - closest, Vector3.up).sqrMagnitude;
        }

        public static EvaluateResult Evaluate(EvaluateInput input)
        {
            var waypoint = input.waypoint;
            var inverse = Quaternion.Inverse(waypoint.rotation);
            Vector3 relative = inverse * (input.position - waypoint.position);
            Vector3 predicted = input.position +
                Quaternion.Euler(0f, input.currentTurn * input.deltaTime, 0f) * input.velocity * input.deltaTime;
            float predictedX = (inverse * (predicted - waypoint.position)).x;
            bool headingMode = waypoint.steerCheat || (!input.onGrass &&
                (Mathf.Abs(relative.x) <= waypoint.correctWidth || Mathf.Abs(predictedX) <= waypoint.correctWidth));

            Vector3 forward = Vector3.ProjectOnPlane(input.rotation * Vector3.forward, Vector3.up);
            Vector3 waypointForward = Vector3.ProjectOnPlane(waypoint.rotation * Vector3.forward, Vector3.up);
            Vector3 targetDelta = Vector3.ProjectOnPlane(input.target - input.position, Vector3.up);
            float targetAngle = Vector3.SignedAngle(forward, targetDelta, Vector3.up);
            float waypointAngle = Vector3.SignedAngle(forward, waypointForward, Vector3.up);
            float rawSteer;
            if (headingMode)
                rawSteer = waypointAngle / Mathf.Max(0.001f, input.turnMax) * waypoint.steerIntensity;
            else
            {
                Vector3 localTarget = Quaternion.Inverse(input.rotation) * (input.target - input.position);
                float horizontalDistance = new Vector2(localTarget.x, localTarget.z).magnitude;
                rawSteer = horizontalDistance > 0.00001f ? localTarget.x / horizontalDistance : 0f;
            }

            bool eligibleDrift = input.grounded && !input.onGrass && !input.reversed &&
                                 input.speed >= input.topSpeed * 0.25f;
            float drift = 0f;
            if (eligibleDrift && waypoint.driftFactor != 0f &&
                Mathf.Abs(targetAngle) >= waypoint.driftAngleCondition)
                drift = Mathf.Clamp(waypoint.driftFactor, -1f, 1f);
            if (eligibleDrift && rawSteer >= 0.9f && drift == 0f)
                drift = Mathf.Clamp(rawSteer * 1000f, -1f, 1f);

            float steer = rawSteer;
            if (!headingMode)
                steer *= Mathf.Clamp(input.speed / Mathf.Max(0.001f, input.defTopSpeed * 0.75f) + 1f, 1f, 3f);
            if (input.reversed) steer = -steer;
            bool spinBranch = !input.grounded && input.flips < input.spinGoal;
            if (spinBranch)
                steer = input.reversed ? 0f : 1f;
            else
            {
                Transform pivot = waypoint.leftPivotPoint ? waypoint.leftPivotPoint : waypoint.rightPivotPoint;
                if (pivot)
                {
                    Vector3 direction = pivot.position - waypoint.position;
                    direction.y = 0f;
                    Vector3 side = input.rotation * Vector3.right;
                    if (waypoint.leftPivotPoint) side = -side;
                    float angle = Vector3.SignedAngle(side, direction, Vector3.up);
                    float pivotSteer = angle / Mathf.Max(0.001f, input.turnMax) * waypoint.steerIntensity;
                    for (int iteration = 0; iteration < 4; iteration++)
                        steer += (pivotSteer - steer) * waypoint.leftPivotStrength;
                }
                if (input.reversed && !input.onGrass) steer = 1f;
            }
            float advance = Mathf.Clamp(input.speed * input.deltaTime * 0.25f * 10f, 0f, 30f);
            return new EvaluateResult
            {
                RawSteer = rawSteer, Steer = Mathf.Clamp(steer, -1f, 1f),
                DirectDrift = drift, Advance = advance,
                CurrentLocalX = relative.x, PredictedLocalX = predictedX,
                TargetAngle = targetAngle, WaypointAngle = waypointAngle,
                LocalZ = relative.z, HeadingMode = headingMode,
                SpinRequested = spinBranch && !input.reversed,
                PassedWaypoint = relative.z >= -advance
            };
        }

        static int NextOrZero(int index, int count) => index >= count ? 0 : index;
    }
}
