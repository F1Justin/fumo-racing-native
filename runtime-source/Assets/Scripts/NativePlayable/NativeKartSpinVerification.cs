using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartSpinVerification
    {
        const float AbsoluteTolerance = 2e-5f;
        const float RelativeTolerance = 2e-6f;
        const string OriginalAssemblyHash = "17aad94d87a89d8db94009ab7341f938c29fb0c694bafaf92fab6cbc7082574f";

        [Serializable] sealed class Source { public string path, sha256; }
        [Serializable] sealed class Reference
        {
            public int schemaVersion, turnCount, counterCount, landingCount;
            public string boundary, generatorSha256;
            public Source originalAssembly;
            public Source[] generatorDependencies, parameterSources;
            public NativeKartSettings parameters;
            public TurnCase[] turns;
            public CounterCase[] counters;
            public LandingCase[] landings;
        }
        [Serializable] sealed class TurnCase
        {
            public int caseIndex;
            public float wishTurn, airTime, dt, expectedBodyYaw, expectedVelocityYaw;
            public bool spinning;
        }
        [Serializable] sealed class CounterCase
        {
            public int caseIndex, flips, expectedFlips;
            public float turned, bodyYaw, expectedTurned;
        }
        [Serializable] sealed class LandingCase
        {
            public int caseIndex, flips, expectedFlips;
            public float speed, airTime, turnedOnAir, expectedSpeed, expectedAirTime;
            public float expectedTurnedOnAir, expectedNextFrameCappedSpeed;
            public Vector3 velocity;
            public bool convertsAirVelocity;
        }
        [Serializable] sealed class Failure
        {
            public string metric;
            public int caseIndex;
            public float actual, expected;
        }
        [Serializable] sealed class Report
        {
            public string version, utc, referencePath, referenceSha256, referenceBoundary, referenceGeneratorSha256;
            public Source originalAssembly;
            public Source[] generatorDependencies, parameterSources;
            public bool passed;
            public int turnCount, counterCount, landingCount, comparisons, failedComparisons;
            public float absoluteTolerance, relativeTolerance, maxAbsoluteError;
            public Failure[] failures;
            public string[] validationErrors;
        }

        public static bool RunReference(string referencePath, string outputDir)
        {
            var report = new Report
            {
                version = Application.version,
                utc = DateTime.UtcNow.ToString("o"),
                referencePath = Path.GetFullPath(referencePath),
                absoluteTolerance = AbsoluteTolerance,
                relativeTolerance = RelativeTolerance
            };
            var failures = new List<Failure>();
            var errors = new List<string>();
            try
            {
                byte[] bytes = File.ReadAllBytes(referencePath);
                using (var sha = SHA256.Create())
                    report.referenceSha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                var reference = JsonUtility.FromJson<Reference>(Encoding.UTF8.GetString(bytes));
                if (reference == null || reference.schemaVersion != 1 || reference.originalAssembly == null
                    || reference.originalAssembly.sha256 != OriginalAssemblyHash || reference.parameters == null
                    || reference.turns == null || reference.counters == null || reference.landings == null
                    || reference.turnCount != reference.turns.Length || reference.counterCount != reference.counters.Length
                    || reference.landingCount != reference.landings.Length || reference.turnCount == 0
                    || reference.counterCount == 0 || reference.landingCount == 0
                    || string.IsNullOrEmpty(reference.generatorSha256))
                    throw new InvalidDataException("Spin reference schema, source hash or counts are invalid.");
                report.originalAssembly = reference.originalAssembly;
                report.parameterSources = reference.parameterSources;
                report.generatorDependencies = reference.generatorDependencies;
                report.referenceBoundary = reference.boundary;
                report.referenceGeneratorSha256 = reference.generatorSha256;
                for (int i = 0; i < reference.turns.Length; i++)
                {
                    var item = reference.turns[i];
                    if (item == null || item.caseIndex != i || item.dt < 0f || !Finite(item.dt))
                        throw new InvalidDataException("Invalid turn case " + i);
                    NativeKartSpinTurn turn = NativeKartSpin.Turn(item.wishTurn, item.spinning, item.airTime,
                        reference.parameters, item.dt);
                    Compare("bodyYaw", turn.bodyYaw, item.expectedBodyYaw, i, report, failures);
                    Compare("velocityYaw", turn.velocityYaw, item.expectedVelocityYaw, i, report, failures);
                    report.turnCount++;
                }
                for (int i = 0; i < reference.counters.Length; i++)
                {
                    var item = reference.counters[i];
                    if (item == null || item.caseIndex != i)
                        throw new InvalidDataException("Invalid counter case " + i);
                    NativeKartSpinState state = NativeKartSpinState.Step(
                        new NativeKartSpinState { turned = item.turned, flips = item.flips }, item.bodyYaw);
                    Compare("turned", state.turned, item.expectedTurned, i, report, failures);
                    Compare("flips", state.flips, item.expectedFlips, i, report, failures);
                    report.counterCount++;
                }
                for (int i = 0; i < reference.landings.Length; i++)
                {
                    var item = reference.landings[i];
                    if (item == null || item.caseIndex != i)
                        throw new InvalidDataException("Invalid landing case " + i);
                    float speed = NativeKartAirborne.LandingSpeed(item.speed, item.velocity, item.flips,
                        item.convertsAirVelocity, reference.parameters);
                    Compare("landingSpeed", speed, item.expectedSpeed, i, report, failures);
                    float firstCap = reference.parameters._topSpeed + reference.parameters._topSpeedAdd
                        + reference.parameters._driftSpeedMaxGain;
                    float nextFrameSpeed = Mathf.Clamp(Mathf.Clamp(speed, 0f, firstCap), 0f,
                        reference.parameters._topSpeedCap);
                    Compare("nextFrameCap", nextFrameSpeed,
                        item.expectedNextFrameCappedSpeed, i, report, failures);
                    report.landingCount++;
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
            }
            report.failedComparisons = failures.Count;
            report.failures = failures.ToArray();
            report.validationErrors = errors.ToArray();
            report.passed = errors.Count == 0 && failures.Count == 0;
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "spin-reference-report.json"),
                JsonUtility.ToJson(report, true) + "\n");
            return report.passed;
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        static void Compare(string metric, float actual, float expected, int index, Report report, List<Failure> failures)
        {
            report.comparisons++;
            float error = Mathf.Abs(actual - expected);
            report.maxAbsoluteError = Mathf.Max(report.maxAbsoluteError, error);
            if (!Finite(actual) || !Finite(expected) || error > AbsoluteTolerance + RelativeTolerance * Mathf.Abs(expected))
                failures.Add(new Failure { metric = metric, caseIndex = index, actual = actual, expected = expected });
        }
    }
}
