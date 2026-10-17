using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartDriftVerification
    {
        const double AbsoluteTolerance = 2e-5, RelativeTolerance = 2e-6;
        const string OriginalAssemblyHash = "17aad94d87a89d8db94009ab7341f938c29fb0c694bafaf92fab6cbc7082574f";
        const string CarriedSpeed = "previous native expectedSpeed", ExplicitSpeed = "explicit speed input";

        [Serializable] sealed class Source { public string path, sha256; }
        [Serializable] sealed class Reference
        {
            public int schemaVersion, sequenceCount, frameCount, lifecycleCaseCount, lifecycleOperationCount;
            public int requestCaseCount, requestFrameCount;
            public string referenceKind, boundary, generatorSha256;
            public Source originalAssembly;
            public Source[] parameterSources, generatorDependencies;
            public Sequence[] sequences;
            public Lifecycle[] lifecycleCases;
            public RequestCase[] requestCases;
        }
        [Serializable] sealed class Sequence
        {
            public string name, speedMode;
            public NativeKartSettings parameters;
            public float initialSpeed, initialTurn;
            public Frame[] frames;
        }
        [Serializable] sealed class Frame
        {
            public int frameIndex, reversed;
            public bool braking, onGrass;
            public float dt, wishTurn, wishTurnAdd, driftDirection, extraSteer, speedBefore;
            public float wishAccel, groundAngle, slopeLimit, topSpeedModifier, extraSpeed;
            public float expectedWishTurn, expectedTurn, expectedTurnLimit, expectedSpeedAfterTurnLoss;
            public float expectedTargetTopSpeed, expectedApproachRate, expectedSpeedAfterApproach;
            public float expectedSpeedAfterBrake, expectedDrainFactor, expectedSpeedAfterDrain;
            public float expectedSpeedBeforeCaps, expectedWishAccel, expectedSpeed;
        }
        [Serializable] sealed class Lifecycle
        {
            public string name;
            public NativeKartSettings parameters;
            public NativeKartDriftState initial;
            public Operation[] operations;
        }
        [Serializable] sealed class Operation
        {
            public string action;
            public float direction, dt;
            public bool grounded;
            public NativeKartDriftState expected;
        }
        [Serializable] sealed class RequestCase
        {
            public string name;
            public NativeKartSettings parameters;
            public float initialDirection, initialElapsed;
            public RequestFrame[] frames;
        }
        [Serializable] sealed class RequestFrame
        {
            public bool requested, grounded, braking;
            public float dt, wishAccel, wishTurn, expectedDirection, expectedElapsed;
        }
        [Serializable] sealed class Metric
        {
            public string name;
            public int comparisons, failedComparisons;
            public double maxAbsoluteError, maxRelativeError;
        }
        [Serializable] sealed class Failure
        {
            public string sequence, metric;
            public int frameIndex;
            public float actual, expected;
        }
        [Serializable] sealed class Report
        {
            public string version, utc, platform, evidence, referencePath, referenceSha256;
            public string referenceBoundary, referenceGeneratorSha256;
            public Source originalAssembly;
            public Source[] parameterSources, generatorDependencies;
            public bool passed;
            public int sequenceCount, frameCount, lifecycleCaseCount, lifecycleOperationCount;
            public int requestCaseCount, requestFrameCount, comparisons, failedComparisons;
            public double absoluteTolerance, relativeTolerance, maxAbsoluteError, maxRelativeError;
            public Metric[] metrics;
            public Failure[] failures;
            public string[] validationErrors;
        }

        public static bool RunReference(string referencePath, string outputDir)
        {
            var report = new Report
            {
                version = Application.version, utc = DateTime.UtcNow.ToString("o"), platform = SystemInfo.operatingSystem,
                referencePath = referencePath, absoluteTolerance = AbsoluteTolerance, relativeTolerance = RelativeTolerance,
                evidence = "Actual NativeKartSteeringState.Step, NativeKartAccelerationState.Step and NativeKartDriftState "
                    + "Enter/Stop/Request/AdvanceTime compared with original x64 instruction execution. "
                    + "Scalar sequences carry actual steering and, when declared, actual speed between frames. "
                    + "Low-level lifecycle and composite input-gate/air-exit fixtures retain their declared scheduling boundaries."
            };
            var errors = new List<string>();
            var metrics = new Dictionary<string, Metric>();
            var failures = new List<Failure>();
            try
            {
                byte[] bytes = File.ReadAllBytes(referencePath);
                report.referencePath = Path.GetFullPath(referencePath);
                using (var sha = SHA256.Create())
                    report.referenceSha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                var reference = JsonUtility.FromJson<Reference>(Encoding.UTF8.GetString(bytes));
                if (reference == null || reference.schemaVersion != 1 || reference.originalAssembly == null
                    || reference.originalAssembly.sha256 != OriginalAssemblyHash
                    || reference.sequences == null || reference.lifecycleCases == null || reference.requestCases == null
                    || reference.sequenceCount <= 0 || reference.frameCount <= 0 || reference.lifecycleCaseCount <= 0
                    || reference.requestCaseCount <= 0 || string.IsNullOrEmpty(reference.generatorSha256))
                    throw new InvalidDataException("Drift reference schema, source hash or counts are invalid.");
                report.originalAssembly = reference.originalAssembly;
                report.parameterSources = reference.parameterSources;
                report.generatorDependencies = reference.generatorDependencies;
                report.referenceBoundary = reference.boundary;
                report.referenceGeneratorSha256 = reference.generatorSha256;
                var names = new HashSet<string>();
                foreach (var sequence in reference.sequences)
                {
                    if (sequence == null || string.IsNullOrEmpty(sequence.name) || !names.Add(sequence.name)
                        || sequence.parameters == null || sequence.frames == null || sequence.frames.Length == 0
                        || (sequence.speedMode != CarriedSpeed && sequence.speedMode != ExplicitSpeed))
                        throw new InvalidDataException("Drift scalar sequence is invalid.");
                    float speed = sequence.initialSpeed, turn = sequence.initialTurn, previousExpected = speed;
                    report.sequenceCount++;
                    for (int i = 0; i < sequence.frames.Length; i++)
                    {
                        Frame frame = sequence.frames[i];
                        if (frame == null || frame.frameIndex != i || frame.dt < 0f || frame.slopeLimit <= 0f
                            || (sequence.speedMode == CarriedSpeed && frame.speedBefore != previousExpected))
                            throw new InvalidDataException(sequence.name + " has an invalid scalar frame at " + i);
                        var steering = NativeKartSteeringState.Step(turn, new NativeKartSteeringInput
                        {
                            speed = sequence.speedMode == CarriedSpeed ? speed : frame.speedBefore,
                            wishTurn = frame.wishTurn, wishTurnAdd = frame.wishTurnAdd, reversed = frame.reversed != 0,
                            driftDirection = frame.driftDirection, extraSteer = frame.extraSteer
                        }, sequence.parameters, frame.dt);
                        var acceleration = NativeKartAccelerationState.Step(steering.currentSpeed, new NativeKartAccelerationInput
                        {
                            wishAccel = frame.wishAccel, groundAngle = frame.groundAngle, slopeLimit = frame.slopeLimit,
                            topSpeedModifier = frame.topSpeedModifier, extraSpeed = frame.extraSpeed,
                            driftDirection = frame.driftDirection, currentTurn = steering.currentTurn, turnLimit = steering.turnLimit,
                            braking = frame.braking, onGrass = frame.onGrass
                        }, sequence.parameters, frame.dt);
                        turn = steering.currentTurn;
                        speed = acceleration.currentSpeed;
                        previousExpected = frame.expectedSpeed;
                        string[] fields = { "turn", "turnLimit", "speedAfterTurnLoss", "targetTopSpeed", "approachRate",
                            "speedAfterApproach", "speedAfterBrake", "drainFactor", "speedAfterDrain", "speedBeforeCaps", "speed" };
                        float[] actual = { turn, steering.turnLimit, steering.currentSpeed, acceleration.targetTopSpeed,
                            acceleration.approachRate, acceleration.speedAfterApproach, acceleration.speedAfterBrake,
                            acceleration.driftDrainFactor, acceleration.speedAfterDriftDrain, acceleration.speedBeforeCaps, speed };
                        float[] expected = { frame.expectedTurn, frame.expectedTurnLimit, frame.expectedSpeedAfterTurnLoss,
                            frame.expectedTargetTopSpeed, frame.expectedApproachRate, frame.expectedSpeedAfterApproach,
                            frame.expectedSpeedAfterBrake, frame.expectedDrainFactor, frame.expectedSpeedAfterDrain,
                            frame.expectedSpeedBeforeCaps, frame.expectedSpeed };
                        for (int j = 0; j < fields.Length; j++)
                            Compare(fields[j], actual[j], expected[j], sequence.name, i, metrics, failures, report);
                        report.frameCount++;
                    }
                }
                foreach (var lifecycle in reference.lifecycleCases)
                {
                    if (lifecycle == null || lifecycle.parameters == null || lifecycle.operations == null
                        || lifecycle.operations.Length == 0 || string.IsNullOrEmpty(lifecycle.name))
                        throw new InvalidDataException("Drift lifecycle fixture is invalid.");
                    var state = lifecycle.initial;
                    report.lifecycleCaseCount++;
                    for (int i = 0; i < lifecycle.operations.Length; i++)
                    {
                        var operation = lifecycle.operations[i];
                        if (operation.action == "enter")
                            state = NativeKartDriftState.Enter(state, operation.direction, operation.grounded, lifecycle.parameters._driftAngle);
                        else if (operation.action == "release" || operation.action == "airExit")
                            state = NativeKartDriftState.Stop(state);
                        else if (operation.action != "hold")
                            throw new InvalidDataException("Unknown drift lifecycle action: " + operation.action);
                        state = state.AdvanceTime(operation.dt);
                        Compare("lifecycleDirection", state.direction, operation.expected.direction, lifecycle.name, i, metrics, failures, report);
                        Compare("lifecycleElapsed", state.elapsed, operation.expected.elapsed, lifecycle.name, i, metrics, failures, report);
                        report.lifecycleOperationCount++;
                    }
                }
                foreach (var request in reference.requestCases)
                {
                    if (request == null || request.parameters == null || request.frames == null
                        || request.frames.Length == 0 || string.IsNullOrEmpty(request.name))
                        throw new InvalidDataException("Drift request fixture is invalid.");
                    var state = new NativeKartDriftState { direction = request.initialDirection, elapsed = request.initialElapsed };
                    report.requestCaseCount++;
                    for (int i = 0; i < request.frames.Length; i++)
                    {
                        var frame = request.frames[i];
                        state = NativeKartDriftState.Request(state, new NativeKartDriftInput
                        {
                            requested = frame.requested, grounded = frame.grounded, braking = frame.braking,
                            wishAccel = frame.wishAccel, wishTurn = frame.wishTurn
                        }, request.parameters).AdvanceTime(frame.dt);
                        Compare("requestDirection", state.direction, frame.expectedDirection, request.name, i, metrics, failures, report);
                        Compare("requestElapsed", state.elapsed, frame.expectedElapsed, request.name, i, metrics, failures, report);
                        report.requestFrameCount++;
                    }
                }
                if (report.sequenceCount != reference.sequenceCount || report.frameCount != reference.frameCount
                    || report.lifecycleCaseCount != reference.lifecycleCaseCount || report.lifecycleOperationCount != reference.lifecycleOperationCount
                    || report.requestCaseCount != reference.requestCaseCount || report.requestFrameCount != reference.requestFrameCount)
                    throw new InvalidDataException("Executed drift fixture counts differ from declared counts.");
            }
            catch (Exception exception) { errors.Add(exception.ToString()); }
            report.metrics = new List<Metric>(metrics.Values).ToArray();
            report.failures = failures.ToArray();
            report.validationErrors = errors.ToArray();
            report.passed = errors.Count == 0 && report.failedComparisons == 0 && report.frameCount > 0
                && report.lifecycleOperationCount > 0 && report.requestFrameCount > 0;
            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "drift-reference-report.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogError("Drift reference report could not be written: " + exception);
                return false;
            }
            return report.passed;
        }

        static void Compare(string name, float actual, float expected, string sequence, int frame,
            Dictionary<string, Metric> metrics, List<Failure> failures, Report report)
        {
            if (!metrics.TryGetValue(name, out var metric)) metrics[name] = metric = new Metric { name = name };
            bool finite = !float.IsNaN(actual) && !float.IsInfinity(actual) && !float.IsNaN(expected) && !float.IsInfinity(expected);
            double absolute = finite ? Math.Abs((double)actual - expected) : double.MaxValue;
            double scale = Math.Max(Math.Abs((double)actual), Math.Abs((double)expected));
            double relative = finite ? (scale == 0d ? 0d : absolute / scale) : double.MaxValue;
            metric.comparisons++; report.comparisons++;
            metric.maxAbsoluteError = Math.Max(metric.maxAbsoluteError, absolute);
            metric.maxRelativeError = Math.Max(metric.maxRelativeError, relative);
            report.maxAbsoluteError = Math.Max(report.maxAbsoluteError, absolute);
            report.maxRelativeError = Math.Max(report.maxRelativeError, relative);
            if (!finite || absolute > Math.Max(AbsoluteTolerance, RelativeTolerance * scale))
            {
                metric.failedComparisons++; report.failedComparisons++;
                if (failures.Count < 30)
                    failures.Add(new Failure { sequence = sequence, frameIndex = frame, metric = name, actual = actual, expected = expected });
            }
        }
    }
}
