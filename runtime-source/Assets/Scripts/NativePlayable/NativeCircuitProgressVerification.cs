using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeCircuitProgressVerification
    {
        [Serializable] sealed class Check { public string name; public bool passed; public float[] actual, expected; }
        [Serializable] sealed class Report { public bool passed; public string error; public Check[] checks; }

        public static bool CheckRules() => Run(null);

        public static bool Run(string directory)
        {
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var report = new Report();
            try
            {
                var p = Fresh();
                bool initial = p.CurrentTrigger == -1 && p.LastTrigger == -1 && p.CurrentTriggerIndex == -1 && p.Target == 0;
                p.Cross(0, true);
                Add("initial-minus-one-and-start-zero", Join(State(p), initial ? 1f : 0f, p.Started ? 1f : 0f),
                    new float[] { 0, -1, 0, 0, 1, 0, 1, 1 });
                bool changed = p.Cross(0, true);
                Add("duplicate-forward-ignored", Join(State(p), changed ? 1f : 0f), new float[] { 0, -1, 0, 0, 1, 0, 0 });
                p.Cross(1, true); p.Cross(2, true);
                Add("ordered-forward-count", State(p), new float[] { 2, 1, 2, 0, 3, 0 });
                p.Cross(7, true);
                Add("forward-skip-adopts-gate-index", State(p), new float[] { 7, 6, 7, 0, 8, 0 });
                p.Cross(3, true);
                Add("lower-forward-index-increments-counter", State(p), new float[] { 8, 7, 3, 0, 4, 0 });
                p.Cross(3, false);
                Add("backward-decrements-and-selects-predecessor", State(p), new float[] { 7, 8, 2, 0, 3, 0 });
                changed = p.Cross(3, false);
                Add("duplicate-backward-ignored", Join(State(p), changed ? 1f : 0f), new float[] { 7, 8, 2, 0, 3, 0, 0 });
                p.Cross(9, false);
                Add("backward-high-index-adopts-counter", State(p), new float[] { 9, 8, 8, 0, 9, 0 });

                p = Fresh();
                bool initialBack = p.Cross(0, false);
                p.Cross(0, true); p.Cross(0, false);
                Add("reverse-start-goes-negative-with-ranking-penalty", Join(State(p), p.Fraction(Vector3.zero), initialBack ? 1f : 0f),
                    new float[] { -1, 0, 18, 0, 0, 0, -1, 0 });

                p = Fresh();
                for (int i = 0; i < 19; i++) p.Cross(i, true);
                bool before = p.Finished;
                p.Cross(0, true);
                changed = p.Cross(1, true);
                Add("one-lap-completes-on-return-to-zero-once", Join(State(p), before ? 1f : 0f, changed ? 1f : 0f, p.Passed),
                    new float[] { 0, 18, 0, 1, 1, 1, 0, 0, 19 });
                p = Fresh(); p.Cross(0, true); p.Cross(18, true); p.Cross(0, true);
                Add("original-missed-gates-can-still-complete", State(p), new float[] { 0, 18, 0, 1, 1, 1 });
                p = Fresh(); p.Cross(0, true); p.Cross(1, true); p.Cross(0, true);
                Add("early-zero-does-not-complete-lap", State(p), new float[] { 2, 1, 0, 0, 1, 0 });

                p = Fresh(); p.Cross(0, true); p.Cross(1, true); p.Cross(2, true);
                int recovered = p.PrepareRecovery();
                var freshRecovery = Fresh();
                int startRecovery = freshRecovery.PrepareRecovery();
                Add("recovery-rewinds-count-and-preserves-index", Join(State(p), recovered, startRecovery,
                        freshRecovery.CurrentTrigger, freshRecovery.LastTrigger, freshRecovery.CurrentTriggerIndex),
                    new float[] { 0, -1, 2, 0, 3, 0, 1, 0, -1, -2, -1 });

                p = new NativeCircuitProgress(new[] { Vector3.zero, new Vector3(0, 0, 10),
                    new Vector3(10, 0, 10), new Vector3(10, 0, 0) });
                p.Cross(0, true);
                Add("ranking-uses-three-dimensional-distance-without-clamp",
                    new[] { p.Fraction(new Vector3(0, 0, 5)), p.Fraction(new Vector3(12, 0, 5)),
                        p.Fraction(new Vector3(0, 12, 5)) }, new[] { .125f, -.075f, -.075f });
            }
            catch (Exception exception) { report.error = exception.ToString(); }
            report.checks = checks.ToArray();
            report.passed = report.error == null && checks.Count == 14 && checks.TrueForAll(check => check.passed);
            if (!string.IsNullOrEmpty(directory))
                File.WriteAllText(Path.Combine(directory, "circuit-progress-report.json"), JsonUtility.ToJson(report, true));
            return report.passed;

            void Add(string name, float[] actual, float[] expected)
            {
                bool passed = actual.Length == expected.Length;
                for (int i = 0; passed && i < actual.Length; i++)
                    passed = !float.IsNaN(actual[i]) && Mathf.Abs(actual[i] - expected[i]) < .0001f;
                checks.Add(new Check { name = name, actual = actual, expected = expected, passed = passed });
            }
        }

        static NativeCircuitProgress Fresh()
        {
            var points = new Vector3[19];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector3(0, 0, i * 10);
            return new NativeCircuitProgress(points);
        }

        static float[] State(NativeCircuitProgress p) => new float[]
        {
            p.CurrentTrigger, p.LastTrigger, p.CurrentTriggerIndex, p.CompletedLaps, p.Target, p.Finished ? 1 : 0
        };

        static float[] Join(float[] state, params float[] extra)
        {
            var result = new float[state.Length + extra.Length];
            Array.Copy(state, result, state.Length);
            Array.Copy(extra, 0, result, state.Length, extra.Length);
            return result;
        }
    }
}
