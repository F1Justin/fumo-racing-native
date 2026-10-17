using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public struct NativeKartAIWaypoint
    {
        public Vector3 position;
        public Quaternion rotation;
        public float width, correctWidth, steerIntensity, offset, randomness;
        public float driftFactor, driftAngleCondition, jumpChance, jumpSpeedRequired;
        public int jumpSpins;
        public bool strict, steerCheat, driftingWaypoint;
        public Transform leftPivotPoint, rightPivotPoint;
        public float leftPivotStrength;
        public NativeKartItemBox[] boxes;
    }

    [DefaultExecutionOrder(-75)]
    public sealed class NativeKartOriginalAI : MonoBehaviour
    {
        public NativeKartMotor motor;
        public NativeKartAIWaypoint[] waypoints;
        public float defTopSpeed;
        public float minimumUseTime = 1f, maximumUseTime = 4f;
        public bool active;
        public bool assistedNavigation => false;
        public string trackId;
        public event Action<NativeKartAINavigationSample> NavigationObserved;
        public int NavigationSequence { get; private set; }
        Vector3 ordinaryTarget;
        NativeKartItemInventory inventory;
        NativeKartItemBox desiredBox;
        int desiredBoxWaypoint = -1;
        NativeKartRecovery recovery;
        public int verificationSeed;
        public int racerIndex;

        public int WaypointIndex { get; private set; }
        public Vector3 Target { get; private set; }
        public bool OffWidth { get; private set; }
        public int RouteSamples { get; private set; }
        public int GrassFrames { get; private set; }
        public int WaterFrames { get; private set; }
        public int OffRouteFrames { get; private set; }
        public float MaxLaneDeviation { get; private set; }
        public float MeanLaneDeviation => RouteSamples > 0 ? laneDeviationSum / RouteSamples : 0f;

        System.Random verificationRandom;
        bool initialized;
        int spinGoal;
        int observedLandings;
        int grassTraceFrames;
        int grassTraceWaypoint;
        Vector3 grassTracePosition;
        float grassTraceSpeed;
        int grassTraceSpinGoal;
        bool grassTraceGrounded;
        float stuckTime;
        float laneDeviationSum;
        public bool Stuck => stuckTime >= 5f;

        public void ResetAI()
        {
            FlushGrassTrace();
            WaypointIndex = 0;
            NavigationSequence = 0;
            recovery = GetComponent<NativeKartRecovery>();
            inventory = GetComponent<NativeKartItemInventory>();
            desiredBox = null;
            desiredBoxWaypoint = -1;
            stuckTime = 0f;
            spinGoal = 0;
            observedLandings = motor ? motor.Landings : 0;
            if (motor) motor.AISpinRequested = null;
            RouteSamples = GrassFrames = WaterFrames = OffRouteFrames = 0;
            MaxLaneDeviation = laneDeviationSum = 0f;
            verificationRandom = verificationSeed == 0 ? null : new System.Random(verificationSeed);
            initialized = motor && waypoints != null && waypoints.Length > 0;
            if (initialized) SelectTarget();
        }

        public void RecoverFromCheckpoint(Vector3 checkpointPosition)
        {
            if (!motor || waypoints == null || waypoints.Length == 0) return;
            float bestDistance = 99999f;
            int closest = 0;
            for (int index = 0; index < waypoints.Length; index++)
            {
                float distance = Vector3.Distance(checkpointPosition, waypoints[index].position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = index;
                }
            }
            WaypointIndex = closest;
        }

        void FixedUpdate()
        {
            if (!active || !motor || waypoints == null || waypoints.Length == 0) return;
            if (!initialized) ResetAI();
            if (!initialized) return;
            if (motor.HitFrozen) stuckTime = 0f;

            motor.jump = false;
            if (motor.Landings != observedLandings)
            {
                spinGoal = 0;
                observedLandings = motor.Landings;
            }
            float dt = Time.fixedDeltaTime;
            float speed = motor.Speed;
            var waypoint = waypoints[WaypointIndex];
            float laneDeviation = NativeKartAIMath.MeasureLaneDeviation(waypoints, WaypointIndex, transform.position);
            RouteSamples++;
            laneDeviationSum += laneDeviation;
            MaxLaneDeviation = Mathf.Max(MaxLaneDeviation, laneDeviation);
            if (motor.OnGrass) GrassFrames++;
            TraceGrass();
            if (motor.InWater) WaterFrames++;
            if (laneDeviation > Mathf.Max(4f, waypoint.width)) OffRouteFrames++;
            bool recoveryPending = recovery && recovery.Pending;
            var output = NativeKartAIMath.Evaluate(new NativeKartAIMath.EvaluateInput
            {
                waypoint = waypoint, position = transform.position, rotation = transform.rotation,
                target = Target, velocity = motor.DriveVelocity + Vector3.up * motor.CurrentVerticalSpeed,
                currentTurn = motor.CurrentTurn, deltaTime = dt, speed = speed,
                topSpeed = motor.stats._topSpeed,
                defTopSpeed = defTopSpeed > 0f ? defTopSpeed : motor.stats._topSpeed,
                turnMax = motor.stats._turnMax,
                grounded = motor.Grounded, onGrass = motor.OnGrass, reversed = motor.ControlsReversed,
                spinGoal = spinGoal, flips = motor.SpinState.flips
            });
            OffWidth = output.HeadingMode;
            motor.AIDriftActive = true;
            motor.AIDriftDirection = output.DirectDrift;
            motor.steering = output.Steer;
            motor.throttle = motor.ControlsReversed ? 0.3f : 0.5f;
            motor.braking = false;
            bool spin = output.SpinRequested;
            motor.jumpHeld = false;
            motor.AISpinRequested = spin;

            NavigationSequence++;
            NavigationObserved?.Invoke(new NativeKartAINavigationSample
            {
                kind = "command-before-motor", track = trackId, sequence = NavigationSequence,
                racer = racerIndex, waypoint = WaypointIndex, fixedTime = Time.fixedTime,
                fixedDeltaTime = dt, timeScale = Time.timeScale, topSpeed = motor.stats._topSpeed,
                defTopSpeed = defTopSpeed, turnMax = motor.stats._turnMax, waypointWidth = waypoint.width,
                correctWidth = waypoint.correctWidth, driftFactor = waypoint.driftFactor,
                driftAngleCondition = waypoint.driftAngleCondition,
                speed = speed, currentTurn = motor.CurrentTurn, laneDeviation = laneDeviation,
                stuckTime = stuckTime, position = transform.position, rotation = transform.rotation,
                velocity = motor.PhysicsVelocity, ordinaryTarget = ordinaryTarget, guide = Target,
                actualSteer = motor.steering, actualThrottle = motor.throttle, actualDrift = motor.AIDriftDirection,
                shadowSteer = output.Steer, shadowDrift = output.DirectDrift,
                effectiveGroundWish = motor.ControlsReversed ? -motor.steering : motor.steering,
                appliedWishAccel = motor.stats._autoAccel ? 0.5f : motor.throttle,
                assisted = assistedNavigation, driving = motor.driving, grounded = motor.Grounded,
                grass = motor.OnGrass, water = motor.InWater, frozen = motor.HitFrozen,
                reversed = motor.ControlsReversed, reverseCount = motor.ControlReverseCount,
                autoAccel = motor.stats._autoAccel, brake = motor.braking, directDrift = motor.AIDriftActive,
                drifting = motor.IsDrifting, passed = output.PassedWaypoint, strict = waypoint.strict,
                spinGoal = spinGoal, flips = motor.SpinState.flips, spinRequested = spin,
                recoveryPending = recoveryPending, driftTrial = false
            });

            if (desiredBox && !desiredBox.Spawned)
            {
                desiredBox = null;
                desiredBoxWaypoint = -1;
            }
            if (output.PassedWaypoint)
            {
                int departedWaypoint = WaypointIndex;
                WaypointIndex = (WaypointIndex + 1) % waypoints.Length;
                EnterWaypoint(departedWaypoint);
                if (desiredBox && desiredBoxWaypoint < WaypointIndex)
                {
                    desiredBox = null;
                    desiredBoxWaypoint = -1;
                }
                SelectTarget();
            }
            stuckTime = !motor.HitFrozen && motor.PhysicsVelocity.sqrMagnitude <= 100f ? stuckTime + dt : 0f;
            if (inventory) inventory.StepAIUse(dt, minimumUseTime, maximumUseTime);
        }

        public bool ConsumeStuckRequest()
        {
            if (!active || !motor || motor.HitFrozen || stuckTime < 5f) return false;
            stuckTime = 0f;
            return true;
        }

        void SelectTarget()
        {
            var first = waypoints[WaypointIndex];
            Target = NativeKartAIMath.SelectTarget(waypoints, WaypointIndex, transform.position,
                motor.Speed, motor.stats._topSpeed, ResolveBoxTarget,
                () => verificationRandom == null
                    ? UnityEngine.Random.Range(-first.randomness, first.randomness)
                    : (float)(verificationRandom.NextDouble() * 2.0 - 1.0) * first.randomness,
                out _);
            ordinaryTarget = Target;
        }

        Vector3 ResolveBoxTarget(int candidateWaypoint, Vector3 position)
        {
            if (desiredBox || (inventory && inventory.HasItem)) return position;
            var boxes = waypoints[candidateWaypoint].boxes;
            if (boxes == null || boxes.Length == 0) return position;
            var activeBoxes = new List<NativeKartItemBox>();
            foreach (var box in boxes)
                if (box && box.Spawned) activeBoxes.Add(box);
            if (activeBoxes.Count == 0) return position;
            int index = verificationRandom == null
                ? UnityEngine.Random.Range(0, activeBoxes.Count)
                : verificationRandom.Next(activeBoxes.Count);
            desiredBox = activeBoxes[index];
            desiredBoxWaypoint = candidateWaypoint;
            return desiredBox.transform.position;
        }

        void EnterWaypoint(int departedWaypoint)
        {
            var waypoint = waypoints[departedWaypoint];
            if (waypoint.jumpChance <= 0f || motor.Speed < waypoint.jumpSpeedRequired) return;
            float roll = verificationRandom == null
                ? UnityEngine.Random.Range(0f, 100f)
                : (float)(verificationRandom.NextDouble() * 100.0);
            if (roll > waypoint.jumpChance) return;
            motor.jump = true;
            spinGoal = Mathf.Max(0, waypoint.jumpSpins);
        }

        void TraceGrass()
        {
            if (verificationSeed == 0) return;
            if (motor.OnGrass)
            {
                if (grassTraceFrames == 0)
                {
                    grassTraceWaypoint = WaypointIndex;
                    grassTracePosition = transform.position;
                    grassTraceSpeed = motor.Speed;
                    grassTraceSpinGoal = spinGoal;
                    grassTraceGrounded = motor.Grounded;
                    Debug.Log("NATIVE_AI_GRASS_ENTER racer=" + racerIndex + " waypoint=" + grassTraceWaypoint
                        + " position=" + grassTracePosition + " grounded=" + grassTraceGrounded
                        + " speed=" + grassTraceSpeed.ToString("F2") + " spinGoal=" + grassTraceSpinGoal);
                }
                grassTraceFrames++;
            }
            else FlushGrassTrace();
        }

        public void FlushGrassTrace()
        {
            if (grassTraceFrames <= 0) return;
            Debug.Log("NATIVE_AI_GRASS_EXIT racer=" + racerIndex + " startWaypoint=" + grassTraceWaypoint
                + " startPosition=" + grassTracePosition + " endWaypoint=" + WaypointIndex
                + " endPosition=" + transform.position + " grounded=" + (motor && motor.Grounded)
                + " speed=" + (motor ? motor.Speed.ToString("F2") : "0")
                + " spinGoal=" + spinGoal + " frames=" + grassTraceFrames);
            grassTraceFrames = 0;
        }
    }
}
