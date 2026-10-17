using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAccelerationVerification
    {
        const double AbsoluteTolerance = 2e-5;
        const double RelativeTolerance = 2e-6;
        const int FailureDetailLimit = 20;
        const string OriginalAssemblyHash = "17aad94d87a89d8db94009ab7341f938c29fb0c694bafaf92fab6cbc7082574f";
        const string CarriedSpeed = "previous native expectedSpeed";
        const string ExplicitSpeed = "explicit speed input";

        [Serializable] sealed class Source { public string path, sha256; }
        [Serializable] sealed class Reference
        {
            public int schemaVersion, sequenceCount, frameCount;
            public string referenceKind, boundary, generatorSha256;
            public Source originalAssembly;
            public Source[] parameterSources;
            public Sequence[] sequences;
        }
        [Serializable] sealed class Sequence
        {
            public string name, speedMode;
            public NativeKartSettings parameters;
            public float initialSpeed;
            public Frame[] frames;
        }
        [Serializable] sealed class Frame
        {
            public int frameIndex;
            public bool braking, onGrass;
            public float dt, wishAccel, speedBefore, groundAngle, slopeLimit, topSpeedModifier, extraSpeed;
            public float expectedWishAccel, expectedTargetTopSpeed, expectedApproachRate;
            public float expectedSpeedAfterApproach, expectedSpeedAfterBrake, expectedSpeedBeforeCaps, expectedSpeed;
        }
        [Serializable] sealed class Metric
        {
            public string name;
            public int comparisons, failedComparisons;
            public double maxAbsoluteError, maxRelativeError;
        }
        [Serializable] sealed class Failure
        {
            public string sequence, reason;
            public int frameIndex;
            public float speedInput;
            public Frame reference;
            public NativeKartAccelerationState actual;
            public string[] mismatchedFields;
        }
        [Serializable] sealed class SequenceResult
        {
            public string name, speedMode;
            public int frames, executedFrames, passedFrames, failedFrames;
            public bool passed;
            public float initialSpeed, finalActualSpeed, finalExpectedSpeed;
            public double maxAbsoluteError, maxRelativeError;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, utc, evidence, referencePath, referenceSha256;
            public string referenceGeneratorSha256, referenceBoundary;
            public Source originalAssembly;
            public Source[] parameterSources;
            public bool passed;
            public double absoluteTolerance, relativeTolerance, maxAbsoluteError, maxRelativeError;
            public int declaredSequenceCount, declaredFrameCount, sequenceCount, frameCount;
            public int executedFrames, passedFrames, failedFrames, passedSequences, failedSequences, failureDetailsLimit;
            public Metric[] metrics;
            public SequenceResult[] sequences;
            public Failure[] failures;
            public string[] validationErrors;
        }

        public static bool RunReference(string referencePath, string outputDir)
        {
            var errors = new List<string>();
            var failures = new List<Failure>();
            var sequenceResults = new List<SequenceResult>();
            string[] fields = { "currentSpeed", "targetTopSpeed", "approachRate", "speedAfterApproach", "speedAfterBrake", "speedBeforeCaps" };
            var metrics = new Metric[fields.Length];
            for (int i = 0; i < fields.Length; i++) metrics[i] = new Metric { name = fields[i] };
            var report = new Report
            {
                version = Application.version, platform = SystemInfo.operatingSystem,
                utc = DateTime.UtcNow.ToString("o"), referencePath = referencePath,
                evidence = "Actual NativeKartAccelerationState.Step output compared with original x64 instruction fixtures. "
                    + "Continuous sequences carry the runtime currentSpeed between frames; explicit sequences use fixture speedBefore. "
                    + "Target, rate, acceleration, brake, slope and final caps are compared separately.",
                absoluteTolerance = AbsoluteTolerance, relativeTolerance = RelativeTolerance,
                failureDetailsLimit = FailureDetailLimit
            };
            try
            {
                byte[] bytes = File.ReadAllBytes(referencePath);
                report.referencePath = Path.GetFullPath(referencePath);
                using (var sha = SHA256.Create())
                    report.referenceSha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                var reference = JsonUtility.FromJson<Reference>(Encoding.UTF8.GetString(bytes));
                ValidateReference(reference);
                report.declaredSequenceCount = reference.sequenceCount;
                report.declaredFrameCount = reference.frameCount;
                report.referenceGeneratorSha256 = reference.generatorSha256;
                report.referenceBoundary = reference.boundary;
                report.originalAssembly = reference.originalAssembly;
                report.parameterSources = reference.parameterSources;
                report.sequenceCount = reference.sequences.Length;
                var names = new HashSet<string>();
                foreach (var sequence in reference.sequences)
                {
                    if (sequence == null || string.IsNullOrEmpty(sequence.name) || !names.Add(sequence.name)
                        || sequence.frames == null || sequence.frames.Length == 0 || !Finite(sequence.initialSpeed))
                        throw new InvalidDataException("Sequence name, frames or initial speed is invalid.");
                    if (sequence.speedMode != CarriedSpeed && sequence.speedMode != ExplicitSpeed)
                        throw new InvalidDataException("Unknown speed mode: " + sequence.speedMode);
                    ValidateParameters(sequence.parameters);
                    var result = new SequenceResult
                    {
                        name = sequence.name, speedMode = sequence.speedMode,
                        frames = sequence.frames.Length, initialSpeed = sequence.initialSpeed
                    };
                    sequenceResults.Add(result);
                    float actualSpeed = sequence.initialSpeed;
                    float previousExpectedSpeed = sequence.initialSpeed;
                    for (int index = 0; index < sequence.frames.Length; index++)
                    {
                        var frame = sequence.frames[index];
                        ValidateFrame(frame, index);
                        if (sequence.speedMode == CarriedSpeed && frame.speedBefore != previousExpectedSpeed)
                            throw new InvalidDataException(sequence.name + " has a discontinuous reference speed at " + index);
                        float speedInput = sequence.speedMode == CarriedSpeed ? actualSpeed : frame.speedBefore;
                        var input = new NativeKartAccelerationInput
                        {
                            wishAccel = frame.wishAccel, braking = frame.braking, onGrass = frame.onGrass,
                            groundAngle = frame.groundAngle, slopeLimit = frame.slopeLimit,
                            topSpeedModifier = frame.topSpeedModifier, extraSpeed = frame.extraSpeed
                        };
                        var actual = NativeKartAccelerationState.Step(speedInput, input, sequence.parameters, frame.dt);
                        actualSpeed = actual.currentSpeed;
                        previousExpectedSpeed = frame.expectedSpeed;
                        result.finalExpectedSpeed = frame.expectedSpeed;
                        result.executedFrames++;
                        report.frameCount++;
                        report.executedFrames++;
                        var mismatches = new List<string>();
                        Compare(actual.currentSpeed, frame.expectedSpeed, metrics[0], result, report, mismatches);
                        Compare(actual.targetTopSpeed, frame.expectedTargetTopSpeed, metrics[1], result, report, mismatches);
                        Compare(actual.approachRate, frame.expectedApproachRate, metrics[2], result, report, mismatches);
                        Compare(actual.speedAfterApproach, frame.expectedSpeedAfterApproach, metrics[3], result, report, mismatches);
                        Compare(actual.speedAfterBrake, frame.expectedSpeedAfterBrake, metrics[4], result, report, mismatches);
                        Compare(actual.speedBeforeCaps, frame.expectedSpeedBeforeCaps, metrics[5], result, report, mismatches);
                        if (mismatches.Count == 0)
                        {
                            result.passedFrames++;
                            report.passedFrames++;
                        }
                        else
                        {
                            result.failedFrames++;
                            report.failedFrames++;
                            if (failures.Count < FailureDetailLimit)
                                failures.Add(new Failure
                                {
                                    sequence = sequence.name, frameIndex = index, speedInput = speedInput,
                                    reference = frame, actual = actual, mismatchedFields = mismatches.ToArray(),
                                    reason = "Runtime state differs from original machine-code output."
                                });
                        }
                    }
                    result.finalActualSpeed = actualSpeed;
                    result.passed = result.executedFrames == result.frames && result.failedFrames == 0;
                    if (result.passed) report.passedSequences++;
                    else report.failedSequences++;
                }
                if (report.frameCount != report.declaredFrameCount)
                    errors.Add("Frame count differs from reference metadata: " + report.frameCount + " / " + report.declaredFrameCount);
            }
            catch (Exception exception) { errors.Add(exception.ToString()); }
            report.metrics = metrics;
            report.sequences = sequenceResults.ToArray();
            report.failures = failures.ToArray();
            report.validationErrors = errors.ToArray();
            report.passed = errors.Count == 0 && report.frameCount > 0
                && report.sequenceCount == report.declaredSequenceCount
                && report.frameCount == report.declaredFrameCount
                && report.executedFrames == report.frameCount
                && report.passedSequences == report.sequenceCount && report.failedFrames == 0;
            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "acceleration-reference-report.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogError("Acceleration reference report could not be written: " + exception);
                return false;
            }
            return report.passed;
        }

        static void ValidateReference(Reference reference)
        {
            if (reference == null || reference.schemaVersion != 1 || reference.sequenceCount <= 0
                || reference.frameCount <= 0 || reference.sequences == null
                || reference.sequences.Length != reference.sequenceCount)
                throw new InvalidDataException("Acceleration reference schema or counts are invalid.");
            if (reference.originalAssembly == null
                || !string.Equals(reference.originalAssembly.sha256, OriginalAssemblyHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Reference original assembly hash differs from the examined game.");
            if (!IsHash(reference.generatorSha256) || reference.parameterSources == null || reference.parameterSources.Length == 0)
                throw new InvalidDataException("Reference generator or parameter provenance is missing.");
            foreach (var source in reference.parameterSources)
                if (source == null || string.IsNullOrEmpty(source.path) || !IsHash(source.sha256))
                    throw new InvalidDataException("Reference parameter source is invalid.");
        }

        static void ValidateParameters(NativeKartSettings settings)
        {
            if (settings == null) throw new InvalidDataException("Reference parameters are missing.");
            float[] values = { settings._acceleration, settings._topSpeed, settings._topSpeedGrass,
                settings._brakeForce, settings._friction, settings._frictionGrass, settings._slopeSpeedGain,
                settings._slopeSpeedLoss, settings._slopeAccelGain, settings._slopeAccelLoss,
                settings._topSpeedAdd, settings._driftSpeedMaxGain, settings._topSpeedCap };
            foreach (float value in values)
                if (!Finite(value)) throw new InvalidDataException("Reference parameter is non-finite.");
            if (settings._topSpeed <= 0f || settings._topSpeedCap <= 0f)
                throw new InvalidDataException("Reference speed caps must be positive.");
        }

        static void ValidateFrame(Frame frame, int index)
        {
            if (frame == null || frame.frameIndex != index)
                throw new InvalidDataException("Reference frame is missing or out of sequence: " + index);
            float[] values = { frame.dt, frame.wishAccel, frame.speedBefore, frame.groundAngle, frame.slopeLimit,
                frame.topSpeedModifier, frame.extraSpeed, frame.expectedWishAccel, frame.expectedTargetTopSpeed,
                frame.expectedApproachRate, frame.expectedSpeedAfterApproach, frame.expectedSpeedAfterBrake,
                frame.expectedSpeedBeforeCaps, frame.expectedSpeed };
            foreach (float value in values)
                if (!Finite(value)) throw new InvalidDataException("Reference frame is non-finite: " + index);
            if (frame.dt < 0f || frame.slopeLimit <= 0f || !Near(Mathf.Clamp01(frame.wishAccel), frame.expectedWishAccel))
                throw new InvalidDataException("Reference dt, slope limit or input clamp is invalid: " + index);
        }

        static void Compare(float actual, float expected, Metric metric, SequenceResult sequence,
            Report report, List<string> mismatches)
        {
            metric.comparisons++;
            bool finite = Finite(actual);
            double absolute = finite ? Math.Abs((double)actual - expected) : double.MaxValue;
            double scale = Math.Max(Math.Abs((double)actual), Math.Abs((double)expected));
            double relative = finite ? (scale == 0d ? 0d : absolute / scale) : double.MaxValue;
            metric.maxAbsoluteError = Math.Max(metric.maxAbsoluteError, absolute);
            metric.maxRelativeError = Math.Max(metric.maxRelativeError, relative);
            sequence.maxAbsoluteError = Math.Max(sequence.maxAbsoluteError, absolute);
            sequence.maxRelativeError = Math.Max(sequence.maxRelativeError, relative);
            report.maxAbsoluteError = Math.Max(report.maxAbsoluteError, absolute);
            report.maxRelativeError = Math.Max(report.maxRelativeError, relative);
            if (!finite || !Near(actual, expected))
            {
                metric.failedComparisons++;
                mismatches.Add(metric.name);
            }
        }

        static bool Near(float actual, float expected) => Finite(actual) && Finite(expected)
            && Math.Abs((double)actual - expected) <= Math.Max(AbsoluteTolerance,
                RelativeTolerance * Math.Max(Math.Abs((double)actual), Math.Abs((double)expected)));
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char digit in value) if (!Uri.IsHexDigit(digit)) return false;
            return true;
        }
    }
}
