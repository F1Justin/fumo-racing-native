using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartSteeringVerification
    {
        const double AbsoluteTolerance = 2e-5;
        const double RelativeTolerance = 2e-6;
        const int FailureDetailLimit = 20;
        const string OriginalAssemblyHash = "17aad94d87a89d8db94009ab7341f938c29fb0c694bafaf92fab6cbc7082574f";

        [Serializable]
        sealed class Source
        {
            public string path, sha256;
        }

        [Serializable]
        sealed class Reference
        {
            public int schemaVersion, sequenceCount, frameCount;
            public string referenceKind, boundary, generatorSha256;
            public Source originalAssembly;
            public Source[] parameterSources;
            public Sequence[] sequences;
        }

        [Serializable]
        sealed class Sequence
        {
            public string name, speedMode;
            public NativeKartSettings parameters;
            public float initialTurn, initialSpeed;
            public Frame[] frames;
        }

        [Serializable]
        sealed class Frame
        {
            public int frameIndex, reversed;
            public float dt, wishTurn, wishTurnAdd, driftDirection, extraSteer, speedBefore;
            public float expectedWishTurn, expectedTurn, expectedSpeed, expectedTurnLimit;
        }

        [Serializable]
        sealed class Metric
        {
            public string name;
            public int comparisons, failedComparisons;
            public double maxAbsoluteError, maxRelativeError;
        }

        [Serializable]
        sealed class Failure
        {
            public string sequence, reason;
            public int frameIndex;
            public float previousActualTurn;
            public Frame reference;
            public NativeKartSteeringState actual;
            public string[] mismatchedFields;
        }

        [Serializable]
        sealed class SequenceResult
        {
            public string name, speedMode;
            public int frames, executedFrames, passedFrames, failedFrames, invalidFrames;
            public bool passed;
            public float initialTurn, finalActualTurn, finalExpectedTurn;
            public double maxAbsoluteError, maxRelativeError;
        }

        [Serializable]
        sealed class Report
        {
            public string version, platform, utc, evidence, referencePath, referenceSha256;
            public string referenceGeneratorSha256, referenceBoundary;
            public Source originalAssembly;
            public Source[] parameterSources;
            public bool passed;
            public double absoluteTolerance, relativeTolerance, maxAbsoluteError, maxRelativeError;
            public int declaredSequenceCount, declaredFrameCount, sequenceCount, frameCount;
            public int executedFrames, passedFrames, failedFrames, invalidFrames;
            public int passedSequences, failedSequences, failureDetailsLimit;
            public Metric[] metrics;
            public SequenceResult[] sequences;
            public Failure[] failures;
            public string[] validationErrors;
        }

        public static bool RunReference(string referencePath, string outputDirectory)
        {
            var errors = new List<string>();
            var failures = new List<Failure>();
            var sequenceResults = new List<SequenceResult>();
            var metrics = new[]
            {
                new Metric { name = "currentTurn" },
                new Metric { name = "currentSpeed" },
                new Metric { name = "turnLimit" }
            };
            var report = new Report
            {
                version = Application.version,
                platform = SystemInfo.operatingSystem,
                utc = DateTime.UtcNow.ToString("o"),
                referencePath = referencePath,
                evidence = "Actual NativeKartSteeringState.Step output compared with original x64 instruction fixtures. "
                    + "Each sequence carries its own actual currentTurn; every speedBefore is explicit fixture input. "
                    + "The reversed flag uses the original integer > 0 predicate.",
                absoluteTolerance = AbsoluteTolerance,
                relativeTolerance = RelativeTolerance,
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
                    if (sequence == null || string.IsNullOrEmpty(sequence.name) || !names.Add(sequence.name))
                        throw new InvalidDataException("Sequence names must be present and unique.");
                    if (sequence.frames == null || sequence.frames.Length == 0)
                        throw new InvalidDataException("Sequence has no frames: " + sequence.name);
                    ValidateParameters(sequence.parameters);
                    if (!Finite(sequence.initialTurn) || !Finite(sequence.initialSpeed))
                        throw new InvalidDataException("Sequence initial state is non-finite: " + sequence.name);

                    var result = new SequenceResult
                    {
                        name = sequence.name,
                        speedMode = sequence.speedMode,
                        frames = sequence.frames.Length,
                        initialTurn = sequence.initialTurn
                    };
                    sequenceResults.Add(result);
                    float actualTurn = sequence.initialTurn;
                    for (int index = 0; index < sequence.frames.Length; index++)
                    {
                        Frame frame = sequence.frames[index];
                        report.frameCount++;
                        string invalid = ValidateFrame(frame, index);
                        if (invalid != null)
                        {
                            report.invalidFrames++;
                            result.invalidFrames++;
                            errors.Add(sequence.name + " frame " + index + ": " + invalid);
                            AddFailure(failures, sequence.name, index, actualTurn, frame,
                                new NativeKartSteeringState(), invalid, new string[0]);
                            continue;
                        }

                        float previousTurn = actualTurn;
                        var input = new NativeKartSteeringInput
                        {
                            speed = frame.speedBefore,
                            wishTurn = frame.wishTurn,
                            wishTurnAdd = frame.wishTurnAdd,
                            driftDirection = frame.driftDirection,
                            extraSteer = frame.extraSteer,
                            reversed = frame.reversed > 0
                        };
                        NativeKartSteeringState actual = NativeKartSteeringState.Step(
                            previousTurn, input, sequence.parameters, frame.dt);
                        actualTurn = actual.currentTurn;
                        result.finalExpectedTurn = frame.expectedTurn;
                        result.executedFrames++;
                        report.executedFrames++;
                        var mismatches = new List<string>();
                        Compare(actual.currentTurn, frame.expectedTurn, metrics[0], result, report, mismatches);
                        Compare(actual.currentSpeed, frame.expectedSpeed, metrics[1], result, report, mismatches);
                        Compare(actual.turnLimit, frame.expectedTurnLimit, metrics[2], result, report, mismatches);
                        if (mismatches.Count == 0)
                        {
                            result.passedFrames++;
                            report.passedFrames++;
                        }
                        else
                        {
                            result.failedFrames++;
                            report.failedFrames++;
                            AddFailure(failures, sequence.name, index, previousTurn, frame, actual,
                                "Actual runtime state differs from original machine-code output.", mismatches.ToArray());
                        }
                    }
                    result.finalActualTurn = actualTurn;
                    result.passed = result.executedFrames == result.frames
                        && result.failedFrames == 0 && result.invalidFrames == 0;
                    if (result.passed) report.passedSequences++;
                    else report.failedSequences++;
                }
                if (report.frameCount != report.declaredFrameCount)
                    errors.Add("Frame count differs from reference metadata: " + report.frameCount + " / " + report.declaredFrameCount);
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
            }

            report.metrics = metrics;
            report.sequences = sequenceResults.ToArray();
            report.failures = failures.ToArray();
            report.validationErrors = errors.ToArray();
            report.passed = errors.Count == 0 && report.frameCount > 0
                && report.sequenceCount == report.declaredSequenceCount
                && report.frameCount == report.declaredFrameCount
                && report.executedFrames == report.frameCount
                && report.passedSequences == report.sequenceCount
                && report.failedFrames == 0 && report.invalidFrames == 0;
            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "steering-reference-report.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogError("Steering reference report could not be written: " + exception);
                return false;
            }
            return report.passed;
        }

        static void ValidateReference(Reference reference)
        {
            if (reference == null || reference.schemaVersion != 1)
                throw new InvalidDataException("Unsupported steering reference schema.");
            if (reference.sequenceCount <= 0 || reference.frameCount <= 0 || reference.sequences == null
                || reference.sequences.Length != reference.sequenceCount)
                throw new InvalidDataException("Reference sequence/frame metadata is empty or inconsistent.");
            if (reference.originalAssembly == null
                || !string.Equals(reference.originalAssembly.sha256, OriginalAssemblyHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Reference original assembly hash differs from the examined game.");
            if (!IsHash(reference.generatorSha256) || reference.parameterSources == null || reference.parameterSources.Length == 0)
                throw new InvalidDataException("Reference generator or parameter provenance is missing.");
            foreach (Source source in reference.parameterSources)
                if (source == null || string.IsNullOrEmpty(source.path) || !IsHash(source.sha256))
                    throw new InvalidDataException("Reference parameter source is invalid.");
        }

        static void ValidateParameters(NativeKartSettings settings)
        {
            if (settings == null || !Finite(settings._turnForce) || !Finite(settings._turnMax)
                || !Finite(settings._turnLowMult) || !Finite(settings._turnFriction)
                || !Finite(settings._turnLoseSpeed) || !Finite(settings._topSpeed)
                || !Finite(settings._driftAutoIntensity) || !Finite(settings._driftSteerGain)
                || settings._turnMax <= 0f || settings._topSpeed <= 0f)
                throw new InvalidDataException("Reference steering parameters are invalid.");
        }

        static string ValidateFrame(Frame frame, int index)
        {
            if (frame == null) return "Frame is missing.";
            if (frame.frameIndex != index) return "Frame index is out of sequence.";
            if (!Finite(frame.dt) || frame.dt < 0f || !Finite(frame.wishTurn)
                || !Finite(frame.wishTurnAdd) || !Finite(frame.driftDirection) || !Finite(frame.extraSteer)
                || !Finite(frame.speedBefore) || !Finite(frame.expectedWishTurn) || !Finite(frame.expectedTurn)
                || !Finite(frame.expectedSpeed) || !Finite(frame.expectedTurnLimit))
                return "Frame contains a non-finite value or negative dt.";
            if (!Near(Mathf.Clamp(frame.wishTurn, -1f, 1f), frame.expectedWishTurn))
                return "Reference wishTurn normalization is inconsistent.";
            return null;
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

        static bool Near(float actual, float expected)
        {
            return Finite(actual) && Finite(expected)
                && Math.Abs((double)actual - expected) <= Math.Max(AbsoluteTolerance,
                    RelativeTolerance * Math.Max(Math.Abs((double)actual), Math.Abs((double)expected)));
        }

        static void AddFailure(List<Failure> failures, string sequence, int frameIndex,
            float previousTurn, Frame reference, NativeKartSteeringState actual, string reason, string[] fields)
        {
            if (failures.Count >= FailureDetailLimit) return;
            failures.Add(new Failure
            {
                sequence = sequence, frameIndex = frameIndex, previousActualTurn = previousTurn,
                reference = reference, actual = actual, reason = reason, mismatchedFields = fields
            });
        }

        static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char digit in value)
                if (!Uri.IsHexDigit(digit)) return false;
            return true;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
