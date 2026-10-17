using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAIOriginalVerification
    {
        [Serializable] sealed class Reference
        {
            public string format, sourceSha256, baselineSha256, sourceBoundary;
            public int totalAICalls, excludedTransitionCalls, autonomousStableCalls;
            public Frame[] frames;
            public Transition[] excludedTransitions;
        }
        [Serializable] sealed class Frame
        {
            public int sourceOrdinal, sample, unityFrame;
            public float fixedDeltaTime;
            public bool autonomousBeforeArrangement;
            public Before before;
            public Observed observedAfter;
        }
        [Serializable] sealed class Before
        {
            public Vector3 position, motorVelocity, controllerVelocity, target;
            public Quaternion rotation;
            public float currentTurn, speed, topSpeed, defTopSpeed, turnMax, driftDir;
            public bool grounded, onGrass, reversed;
            public int waypointIndex;
            public NativeKartAIWaypoint waypoint;
        }
        [Serializable] sealed class Observed
        {
            public float wishAccel, wishTurn, driftDir;
            public bool wishBreak, offWidth;
            public int waypointIndex;
            public Vector3 target;
        }
        [Serializable] sealed class Transition
        {
            public int sourceOrdinal, sample;
            public bool waypointChanged, targetChanged;
        }
        [Serializable] public sealed class Check
        {
            public string name, detail;
            public bool passed;
        }
        [Serializable] public sealed class Sample
        {
            public int sourceOrdinal, sample, waypointIndex;
            public bool autonomous, headingMode, observedOffWidth, passedWaypoint;
            public float observedSteer, calculatedSteer, controllerVelocitySteer;
            public float steerError, observedDriftDirection, calculatedDirectDrift;
            public float rawSteer, targetAngle, currentLocalX, predictedLocalX;
        }
        [Serializable] public sealed class Report
        {
            public string version, format, sourceSha256, baselineSha256, boundary;
            public bool passed;
            public int totalAICalls, stableCalls, excludedTransitionCalls, autonomousStableCalls;
            public int observedWaypoints, headingModeCalls, targetSteeringCalls;
            public int groundedCalls, airborneCalls, grassCalls, reversedCalls;
            public int velocityInputsDifferent, velocityDiscriminatingCalls;
            public int predictedDirectDriftCalls, observedDriftingCalls, driftStateMismatches;
            public int headingMismatches, throttleMismatches, brakeMismatches;
            public float maxSteerError, maxAutonomousSteerError, meanSteerError;
            public float maxControllerVelocitySteerError;
            public Check[] checks;
            public Sample[] samples;
        }

        public static bool Run(string referencePath, string directory)
        {
            Directory.CreateDirectory(directory);
            var reference = JsonUtility.FromJson<Reference>(File.ReadAllText(referencePath));
            var checks = new List<Check>();
            void Add(string name, bool passed, string detail) =>
                checks.Add(new Check { name = name, passed = passed, detail = detail });
            bool valid = reference != null && reference.format == "fumo-original-ai-fixedupdate-v1"
                && reference.totalAICalls == 852 && reference.excludedTransitionCalls == 23
                && reference.autonomousStableCalls == 398 && reference.frames != null
                && reference.frames.Length == 829 && reference.excludedTransitions != null
                && reference.excludedTransitions.Length == 23;
            Add("original-sample-schema", valid,
                "AI calls=" + (reference == null ? 0 : reference.totalAICalls)
                + "; stable=" + (reference == null || reference.frames == null ? 0 : reference.frames.Length));
            if (!valid)
            {
                Write(new Report { version = Application.version, format = reference == null ? null : reference.format,
                    passed = false, checks = checks.ToArray(), samples = new Sample[0],
                    boundary = "Original AI fixed-update reference schema failed." });
                return false;
            }

            var results = new List<Sample>(reference.frames.Length);
            var waypointIndices = new HashSet<int>();
            int autonomous = 0, heading = 0, target = 0, ground = 0, air = 0, grass = 0, reversed = 0;
            int velocitiesDiffer = 0, velocitiesDiscriminate = 0, predictedDrift = 0, observedDrift = 0;
            int driftMismatch = 0, headingMismatch = 0, throttleMismatch = 0, brakeMismatch = 0;
            float maxError = 0f, maxAutonomousError = 0f, totalError = 0f, maxControllerError = 0f;
            foreach (var frame in reference.frames)
            {
                var b = frame.before;
                var observed = frame.observedAfter;
                if (frame.autonomousBeforeArrangement) autonomous++;
                if (b.grounded) ground++; else air++;
                if (b.onGrass) grass++;
                if (b.reversed) reversed++;
                waypointIndices.Add(b.waypointIndex);
                var input = new NativeKartAIMath.EvaluateInput
                {
                    waypoint = b.waypoint,
                    position = b.position, rotation = b.rotation, target = b.target,
                    velocity = b.motorVelocity,
                    currentTurn = b.currentTurn, deltaTime = frame.fixedDeltaTime,
                    speed = b.speed, topSpeed = b.topSpeed, defTopSpeed = b.defTopSpeed,
                    turnMax = b.turnMax, grounded = b.grounded, onGrass = b.onGrass,
                    reversed = b.reversed
                };
                var calculated = NativeKartAIMath.Evaluate(input);
                input.velocity = b.controllerVelocity;
                var controller = NativeKartAIMath.Evaluate(input);
                float error = Mathf.Abs(calculated.Steer - observed.wishTurn);
                float controllerError = Mathf.Abs(controller.Steer - observed.wishTurn);
                maxError = Mathf.Max(maxError, error);
                maxControllerError = Mathf.Max(maxControllerError, controllerError);
                totalError += error;
                if (frame.autonomousBeforeArrangement) maxAutonomousError = Mathf.Max(maxAutonomousError, error);
                if (calculated.HeadingMode) heading++; else target++;
                if (calculated.HeadingMode != observed.offWidth) headingMismatch++;
                if (Mathf.Abs(observed.wishAccel - 0.5f) > 0.000001f) throttleMismatch++;
                if (observed.wishBreak) brakeMismatch++;
                if ((b.motorVelocity - b.controllerVelocity).sqrMagnitude > 0.00000001f) velocitiesDiffer++;
                if (Mathf.Abs(calculated.Steer - controller.Steer) > 0.000001f ||
                    calculated.HeadingMode != controller.HeadingMode ||
                    calculated.DirectDrift != controller.DirectDrift) velocitiesDiscriminate++;
                if (calculated.DirectDrift != 0f) predictedDrift++;
                if (observed.driftDir != 0f) observedDrift++;
                if (Mathf.Abs(calculated.DirectDrift - observed.driftDir) > 0.000001f) driftMismatch++;
                results.Add(new Sample
                {
                    sourceOrdinal = frame.sourceOrdinal, sample = frame.sample,
                    waypointIndex = b.waypointIndex, autonomous = frame.autonomousBeforeArrangement,
                    headingMode = calculated.HeadingMode, observedOffWidth = observed.offWidth,
                    passedWaypoint = calculated.PassedWaypoint,
                    observedSteer = observed.wishTurn, calculatedSteer = calculated.Steer,
                    controllerVelocitySteer = controller.Steer, steerError = error,
                    observedDriftDirection = observed.driftDir,
                    calculatedDirectDrift = calculated.DirectDrift, rawSteer = calculated.RawSteer,
                    targetAngle = calculated.TargetAngle, currentLocalX = calculated.CurrentLocalX,
                    predictedLocalX = calculated.PredictedLocalX
                });
            }
            Add("complete-fixed-update-coverage", results.Count == 829 && autonomous == 398
                && waypointIndices.Count == 24,
                "Stable=" + results.Count + "; autonomous=" + autonomous
                + "; waypoints=" + waypointIndices.Count + "; target transitions excluded=23");
            Add("original-throttle-and-brake", throttleMismatch == 0 && brakeMismatch == 0,
                "Throttle mismatches=" + throttleMismatch + "; brake mismatches=" + brakeMismatch);
            Add("original-steering-formula", maxError <= 0.001f && headingMismatch == 0,
                "Max steer error=" + maxError + "; heading mismatches=" + headingMismatch);
            Add("original-drift-state-correspondence", driftMismatch == 0,
                "Predicted request frames=" + predictedDrift + "; original drifting frames="
                + observedDrift + "; mismatches=" + driftMismatch);
            Add("source-velocity-ambiguity-recorded", velocitiesDiffer > 0,
                "Different motor/controller velocities=" + velocitiesDiffer
                + "; formula outputs discriminated=" + velocitiesDiscriminate);
            var report = new Report
            {
                version = Application.version, format = reference.format,
                sourceSha256 = reference.sourceSha256, baselineSha256 = reference.baselineSha256,
                totalAICalls = reference.totalAICalls, stableCalls = results.Count,
                excludedTransitionCalls = reference.excludedTransitionCalls,
                autonomousStableCalls = autonomous, observedWaypoints = waypointIndices.Count,
                headingModeCalls = heading, targetSteeringCalls = target,
                groundedCalls = ground, airborneCalls = air, grassCalls = grass, reversedCalls = reversed,
                velocityInputsDifferent = velocitiesDiffer, velocityDiscriminatingCalls = velocitiesDiscriminate,
                predictedDirectDriftCalls = predictedDrift, observedDriftingCalls = observedDrift,
                driftStateMismatches = driftMismatch, headingMismatches = headingMismatch,
                throttleMismatches = throttleMismatch, brakeMismatches = brakeMismatch,
                maxSteerError = maxError, maxAutonomousSteerError = maxAutonomousError,
                meanSteerError = totalError / results.Count,
                maxControllerVelocitySteerError = maxControllerError,
                checks = checks.ToArray(), samples = results.ToArray(),
                boundary = "Original AIController.FixedUpdate before inputs are independently evaluated by NativeKartAIMath. The original after wishTurn, wishAccel, offWidth and effective driftDir are observations. All 23 same-call waypoint/target transitions are recorded separately. Random target selection is not sampled as an input. The source has no direct drift-request hook. All stable inputs are grounded, dry, forward and normal-route; airborne, grass, reverse and direct random-choice branches remain uncovered. Motor and controller velocities differ in many samples but do not discriminate this trace's final outputs."
            };
            report.passed = checks.TrueForAll(c => c.passed);
            Write(report);
            Debug.Log("NATIVE_AI_ORIGINAL_RESULT passed=" + report.passed
                + " stable=" + report.stableCalls + " maxSteerError=" + report.maxSteerError);
            return report.passed;
            void Write(Report value) => File.WriteAllText(Path.Combine(directory, "ai-original-report.json"),
                JsonUtility.ToJson(value, true) + "\n");
        }
    }
}
