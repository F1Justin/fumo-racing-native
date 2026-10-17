using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAirborneVerification
    {
        const double AbsoluteTolerance = 2e-5, RelativeTolerance = 2e-6;
        const string OriginalAssemblyHash = "17aad94d87a89d8db94009ab7341f938c29fb0c694bafaf92fab6cbc7082574f";

        [Serializable] sealed class Source { public string path, sha256; }
        [Serializable] sealed class Reference
        {
            public int schemaVersion, sequenceCount, frameCount, caseCount;
            public string boundary, generatorSha256;
            public Source originalAssembly;
            public Source[] parameterSources, generatorDependencies;
            public NativeKartSettings parameters;
            public Sequence[] sequences;
            public JumpRequest[] jumpRequests;
            public SetJump[] setJumps;
            public Launch[] launches;
            public Edge[] edgeDepartures;
            public Ground[] groundResolutions;
            public EdgeGate[] edgeGates;
            public Landing[] landings;
            public Alignment[] alignmentFactors;
            public Cap[] speedCaps;
        }
        [Serializable] sealed class Sequence
        {
            public string name;
            public NativeKartSettings parameters;
            public float initialVerticalSpeed, initialAirTime;
            public Frame[] frames;
        }
        [Serializable] sealed class Frame
        {
            public int frameIndex;
            public float dt, wishTurn, expectedTurnAngle, expectedVerticalSpeed, expectedAirTime;
        }
        [Serializable] sealed class JumpRequest
        {
            public int caseIndex;
            public float jumpForce;
            public bool grounded, frozen, pending, expectedPending;
        }
        [Serializable] sealed class SetJump
        {
            public int caseIndex;
            public float verticalSpeed, expectedVerticalSpeed, expectedVelocityY;
            public bool pending, afterGrounded, grounded, expectedPending, expectedAfterGrounded, expectedGrounded;
        }
        [Serializable] sealed class Launch
        {
            public int caseIndex;
            public Vector3 forward;
            public float speed, jumpForce, expectedVerticalSpeed, expectedVelocityY;
            public bool expectedPending, expectedJumped;
        }
        [Serializable] sealed class Edge
        {
            public int caseIndex;
            public Vector3 forward;
            public float speed, expectedVerticalSpeed;
            public bool dynamicGravity;
        }
        [Serializable] sealed class Ground
        {
            public int caseIndex, collisionFlags;
            public bool validGround, afterGrounded, onGrass, onWater;
            public bool expectedGrounded, expectedAfterGrounded, expectedOnGrass, expectedOnWater;
        }
        [Serializable] sealed class EdgeGate
        {
            public int caseIndex;
            public bool wasGrounded, grounded, jumped, expectedLeave;
        }
        [Serializable] sealed class Landing
        {
            public int caseIndex, flips, expectedFlips;
            public Vector3 velocity;
            public float speed, airTime, turnedOnAir, expectedSpeed, expectedAirTime, expectedTurnedOnAir;
            public bool convertsAirVelocity;
        }
        [Serializable] sealed class Alignment { public int caseIndex; public float dt, expectedFactor; }
        [Serializable] sealed class Cap { public int caseIndex; public float speed, expectedSpeed; }
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
            public int sequenceCount, frameCount, caseCount, referenceCaseCount, auxiliaryCaseCount;
            public int comparisons, failedComparisons;
            public double absoluteTolerance, relativeTolerance, maxAbsoluteError, maxRelativeError;
            public Metric[] metrics;
            public Failure[] failures;
            public string[] validationErrors, auxiliaryEvidence;
        }

        public static bool RunReference(string referencePath, string outputDir)
        {
            var report = new Report
            {
                version = Application.version, utc = DateTime.UtcNow.ToString("o"), platform = SystemInfo.operatingSystem,
                referencePath = referencePath, absoluteTolerance = AbsoluteTolerance, relativeTolerance = RelativeTolerance,
                evidence = "Actual NativeKartAirborne helper methods compared with original x64 instruction execution. "
                    + "Air scalar sequences carry actual vertical speed between frames. Jump and edge projection cases use "
                    + "supplied forward vectors and original native Vector3.normalized, Project and magnitude execution. "
                    + "Fixture speed and velocity share one input unit; Motor world-speed integration is tested separately.",
                auxiliaryEvidence = new[]
                {
                    "SetJump field writes and edge-transition gates remain original-machine-code reference annexes; helper comparison does not exercise them.",
                    "Air time advancement, pending-jump consumption, landing counters and surface flags remain reference annexes; runtime lifecycle belongs to the live verifier.",
                    "Original quaternion/Transform rotation, physics, collision callback production and whole-Update ordering are outside this scalar comparison."
                }
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
                    || reference.originalAssembly.sha256 != OriginalAssemblyHash || reference.parameters == null
                    || reference.sequences == null || reference.jumpRequests == null || reference.setJumps == null
                    || reference.launches == null || reference.edgeDepartures == null || reference.groundResolutions == null
                    || reference.edgeGates == null || reference.landings == null || reference.alignmentFactors == null
                    || reference.speedCaps == null || reference.sequenceCount <= 0 || reference.frameCount <= 0
                    || reference.caseCount <= 0 || string.IsNullOrEmpty(reference.generatorSha256))
                    throw new InvalidDataException("Airborne reference schema, source hash or counts are invalid.");
                report.originalAssembly = reference.originalAssembly;
                report.parameterSources = reference.parameterSources;
                report.generatorDependencies = reference.generatorDependencies;
                report.referenceBoundary = reference.boundary;
                report.referenceGeneratorSha256 = reference.generatorSha256;
                report.referenceCaseCount = reference.caseCount;
                var names = new HashSet<string>();
                foreach (var sequence in reference.sequences)
                {
                    if (sequence == null || sequence.parameters == null || sequence.frames == null || sequence.frames.Length == 0
                        || string.IsNullOrEmpty(sequence.name) || !names.Add(sequence.name))
                        throw new InvalidDataException("Airborne scalar sequence is invalid.");
                    float vertical = sequence.initialVerticalSpeed;
                    report.sequenceCount++;
                    for (int i = 0; i < sequence.frames.Length; i++)
                    {
                        var frame = sequence.frames[i];
                        if (frame == null || frame.frameIndex != i || frame.dt < 0f || !Finite(frame.dt))
                            throw new InvalidDataException(sequence.name + " has an invalid frame at " + i);
                        float turn = NativeKartAirborne.TurnAngle(frame.wishTurn, sequence.parameters, frame.dt);
                        vertical = NativeKartAirborne.StepGravity(vertical, sequence.parameters._gravity, frame.dt);
                        Compare("turnAngle", turn, frame.expectedTurnAngle, sequence.name, i, metrics, failures, report);
                        Compare("verticalSpeed", vertical, frame.expectedVerticalSpeed, sequence.name, i, metrics, failures, report);
                        report.frameCount++;
                    }
                }
                NativeKartSettings settings = reference.parameters;
                foreach (var item in reference.jumpRequests)
                {
                    settings._jumpForce = item.jumpForce;
                    bool pending = NativeKartAirborne.RequestJump(item.pending, item.grounded, item.frozen, settings);
                    Compare("jumpPending", pending ? 1f : 0f, item.expectedPending ? 1f : 0f,
                        "jumpRequests", item.caseIndex, metrics, failures, report);
                    report.caseCount++;
                }
                foreach (var item in reference.launches)
                {
                    settings._jumpForce = item.jumpForce;
                    Compare("jumpVerticalSpeed", NativeKartAirborne.JumpVerticalSpeed(item.forward, item.speed, settings),
                        item.expectedVerticalSpeed, "launches", item.caseIndex, metrics, failures, report);
                    report.caseCount++;
                }
                foreach (var item in reference.edgeDepartures)
                {
                    Compare("edgeVerticalSpeed", NativeKartAirborne.LeaveGroundVerticalSpeed(item.forward, item.speed, item.dynamicGravity),
                        item.expectedVerticalSpeed, "edgeDepartures", item.caseIndex, metrics, failures, report);
                    report.caseCount++;
                }
                foreach (var item in reference.groundResolutions)
                {
                    bool grounded = NativeKartAirborne.ResolveGrounded(item.validGround, (CollisionFlags)item.collisionFlags);
                    Compare("grounded", grounded ? 1f : 0f, item.expectedGrounded ? 1f : 0f,
                        "groundResolutions", item.caseIndex, metrics, failures, report);
                    report.caseCount++;
                }
                foreach (var item in reference.landings)
                {
                    Compare("landingSpeed", NativeKartAirborne.LandingSpeed(item.speed, item.velocity, item.flips,
                        item.convertsAirVelocity, settings), item.expectedSpeed, "landings", item.caseIndex, metrics, failures, report);
                    report.caseCount++;
                }
                foreach (var item in reference.alignmentFactors)
                {
                    Compare("alignmentFactor", NativeKartAirborne.AlignmentFactor(settings, item.dt), item.expectedFactor,
                        "alignmentFactors", item.caseIndex, metrics, failures, report);
                    report.caseCount++;
                }
                foreach (var item in reference.speedCaps)
                {
                    Compare("speedCap", NativeKartAirborne.ClampSpeed(item.speed, settings), item.expectedSpeed,
                        "speedCaps", item.caseIndex, metrics, failures, report);
                    report.caseCount++;
                }
                report.auxiliaryCaseCount = reference.setJumps.Length + reference.edgeGates.Length;
                if (report.sequenceCount != reference.sequenceCount || report.frameCount != reference.frameCount
                    || report.caseCount + report.auxiliaryCaseCount != reference.caseCount)
                    throw new InvalidDataException("Airborne fixture counts differ from declared counts.");
            }
            catch (Exception exception) { errors.Add(exception.ToString()); }
            report.metrics = new List<Metric>(metrics.Values).ToArray();
            report.failures = failures.ToArray();
            report.validationErrors = errors.ToArray();
            report.passed = errors.Count == 0 && report.failedComparisons == 0 && report.frameCount > 0 && report.caseCount > 0;
            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "air-reference-report.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogError("Airborne reference report could not be written: " + exception);
                return false;
            }
            return report.passed;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static void Compare(string name, float actual, float expected, string sequence, int frame,
            Dictionary<string, Metric> metrics, List<Failure> failures, Report report)
        {
            if (!metrics.TryGetValue(name, out var metric)) metrics[name] = metric = new Metric { name = name };
            bool finite = Finite(actual) && Finite(expected);
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
