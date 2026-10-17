using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAIMathVerification
    {
        [Serializable] sealed class Check
        {
            public string name, source;
            public bool passed;
            public float[] actual, expected;
        }
        [Serializable] sealed class Report
        {
            public string version = Application.version, error;
            public string reference = "Fixed fixtures from original GameAssembly x64; live racing is verified separately";
            public bool passed;
            public Check[] checks;
        }

        public static bool Run(string directory)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var report = new Report();
            try
            {
                var points = new[]
                {
                    Point(Vector3.zero), Point(new Vector3(5f, 0f, 0f)),
                    Point(new Vector3(20f, 0f, 10f)), Point(new Vector3(40f, 0f, 20f))
                };
                int count;
                Vector3 target = NativeKartAIMath.SelectTarget(points, 0, Vector3.zero, 0f, 100f, 0f, out count);
                Add("way-count-one-crosses-two-points", "2DDE89-2DE173",
                    new[] { target.x, target.y, target.z, (float)count }, new[] { 17.2f, 0f, 8.6f, 1f });
                points[0].offset = 3f;
                target = NativeKartAIMath.SelectTarget(points, 0, Vector3.zero, 0f, 100f, 0f, out count);
                Add("way-offset-does-not-shift-source-target", "AIController.Way 2DD870",
                    new[] { target.x, target.y, target.z, (float)count }, new[] { 17.2f, 0f, 8.6f, 1f });
                points[0].offset = 0f;
                target = NativeKartAIMath.SelectTarget(points, 2, Vector3.zero, 0f, 100f, 0f, out count);
                Add("way-penultimate-candidate-wraps-to-zero", "2DDEF8-2DDF1A",
                    new[] { target.x, target.y, target.z, (float)count }, new[] { 2.8f, 0f, 1.4f, 1f });
                target = NativeKartAIMath.SelectTarget(points, 3, Vector3.zero, 100f, 100f, 0f, out count);
                Add("way-tail-missing-samples-retains-six-denominator", "2DDE89-2DE173",
                    new[] { target.x, target.y, target.z, (float)count }, new[] { 34.2666667f, 0f, 17.1333333f, 6f });

                var input = Input(new Vector3(9.5f, 0f, 3.122499f));
                var result = NativeKartAIMath.Evaluate(input);
                Add("positive-raw-steer-starts-drift", "2DC448-2DC4A6",
                    new[] { result.RawSteer, result.Steer, result.DirectDrift }, new[] { .95f, 1f, 1f });
                input.target.x = -9.5f;
                result = NativeKartAIMath.Evaluate(input);
                Add("negative-raw-steer-keeps-asymmetric-threshold", "2DC448-2DC44F",
                    new[] { result.RawSteer, result.Steer, result.DirectDrift }, new[] { -.95f, -1f, 0f });

                input = Input(new Vector3(6f, 0f, 8f));
                result = NativeKartAIMath.Evaluate(input);
                input.speed = 25f;
                input.defTopSpeed = 200f;
                var defTop = NativeKartAIMath.Evaluate(input);
                Add("drift-before-multiplier-and-default-top-speed", "2DC448;2DC4CA-2DC51A",
                    new[] { result.RawSteer, result.Steer, result.DirectDrift, defTop.Steer, defTop.DirectDrift },
                    new[] { .6f, 1f, 0f, .7f, 0f });

                input = Input(new Vector3(9.5f, 0f, 3.122499f));
                input.waypoint.driftFactor = -1f;
                input.waypoint.driftAngleCondition = 45f;
                result = NativeKartAIMath.Evaluate(input);
                input.target.x = -9.5f;
                var left = NativeKartAIMath.Evaluate(input);
                Add("first-drift-call-retains-direction", "2DC425-2DC4B8;359150",
                    new[] { result.DirectDrift, left.DirectDrift }, new[] { -1f, -1f });

                input = Input(new Vector3(0f, 0f, 10f));
                input.waypoint.correctWidth = 1f;
                input.position = new Vector3(1f, 0f, -10f);
                input.velocity = Vector3.right;
                input.deltaTime = 1f;
                result = NativeKartAIMath.Evaluate(input);
                Add("current-width-inclusive-boundary", "2DC182-2DC1A8",
                    new[] { result.CurrentLocalX, result.PredictedLocalX, Flag(result.HeadingMode) }, new[] { 1f, 2f, 1f });
                input.position.x = 2f;
                input.velocity = Vector3.left;
                result = NativeKartAIMath.Evaluate(input);
                input.velocity.x = -.999f;
                var outside = NativeKartAIMath.Evaluate(input);
                Add("predicted-width-inclusive-boundary", "2DC196-2DC1A8",
                    new[] { result.CurrentLocalX, result.PredictedLocalX, Flag(result.HeadingMode),
                        outside.PredictedLocalX, Flag(outside.HeadingMode) }, new[] { 2f, 1f, 1f, 1.001f, 0f });
                input.position.x = 0f;
                input.velocity = Vector3.zero;
                input.onGrass = true;
                result = NativeKartAIMath.Evaluate(input);
                input.waypoint.steerCheat = true;
                var cheat = NativeKartAIMath.Evaluate(input);
                Add("grass-rejects-width-and-preserves-steer-cheat", "2DBF28;2DC179-2DC1A8",
                    new[] { Flag(result.HeadingMode), Flag(cheat.HeadingMode), cheat.DirectDrift }, new[] { 0f, 1f, 0f });

                input = Input(new Vector3(0f, 0f, 10f));
                input.rotation = Quaternion.Euler(60f, 0f, 0f);
                input.waypoint.rotation = Quaternion.Euler(-30f, 0f, 0f);
                input.waypoint.driftFactor = 1f;
                input.waypoint.driftAngleCondition = 45f;
                result = NativeKartAIMath.Evaluate(input);
                Add("slope-forward-flattening-prevents-false-drift", "2DBDB9;2DC2A2",
                    new[] { result.TargetAngle, result.WaypointAngle, result.RawSteer, result.DirectDrift }, new[] { 0f, 0f, 0f, 0f });

                input = Input(new Vector3(0f, 0f, 10f));
                input.position = new Vector3(0f, 2f, 0f);
                input.velocity = Vector3.down;
                input.deltaTime = 1f;
                input.waypoint.rotation = Quaternion.Euler(0f, 0f, 90f);
                input.waypoint.correctWidth = 1.00001f;
                result = NativeKartAIMath.Evaluate(input);
                input.velocity = Vector3.zero;
                var withoutVertical = NativeKartAIMath.Evaluate(input);
                Add("vertical-velocity-changes-predicted-width", "2DC087-2DC14D",
                    new[] { result.CurrentLocalX, result.PredictedLocalX, Flag(result.HeadingMode),
                        withoutVertical.PredictedLocalX, Flag(withoutVertical.HeadingMode) }, new[] { 2f, 1f, 1f, 2f, 0f });
            }
            catch (Exception exception) { report.error = exception.ToString(); }
            report.checks = checks.ToArray();
            report.passed = report.error == null && checks.Count == 13 && checks.TrueForAll(check => check.passed);
            File.WriteAllText(Path.Combine(directory, "ai-math-report.json"), JsonUtility.ToJson(report, true));
            return report.passed;

            void Add(string name, string source, float[] actual, float[] expected)
            {
                bool passed = actual.Length == expected.Length;
                for (int i = 0; passed && i < actual.Length; i++)
                    passed = !float.IsNaN(actual[i]) && !float.IsInfinity(actual[i]) && Mathf.Abs(actual[i] - expected[i]) < .0002f;
                checks.Add(new Check { name = name, source = source, actual = actual, expected = expected, passed = passed });
            }
        }

        static NativeKartAIWaypoint Point(Vector3 position) => new NativeKartAIWaypoint
        {
            position = position, rotation = Quaternion.identity, correctWidth = -1f,
            steerIntensity = 1f, driftAngleCondition = 45f
        };

        static NativeKartAIMath.EvaluateInput Input(Vector3 target) => new NativeKartAIMath.EvaluateInput
        {
            waypoint = Point(Vector3.zero), position = Vector3.zero, rotation = Quaternion.identity,
            target = target, velocity = Vector3.zero, currentTurn = 0f, deltaTime = .02f,
            speed = 75f, topSpeed = 100f, defTopSpeed = 100f, turnMax = 90f, grounded = true
        };

        static float Flag(bool value) => value ? 1f : 0f;
    }
}
