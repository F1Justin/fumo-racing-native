using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartRecoveryRulesVerification
    {
        [Serializable] public sealed class Check { public string name; public bool passed; }
        [Serializable] sealed class Report { public bool passed; public Check[] checks; }
        public static bool CheckRules() => Run(null);
        public static bool Run(string directory)
        {
            var checks = new List<Check>();
            var state = new NativeKartRecoveryState();
            Add("fall-schedules-half-second", state.Request(false) && state.Pending && state.Deadline == .5);
            Add("wait-retains-pending", !state.Advance(.25f) && state.Pending && state.Teleports == 0);
            Add("automatic-condition-does-not-restart", !state.Request(false) && state.Deadline == .5 && state.Requests == 1);
            Add("pause-preserves-deadline", !state.Advance(0f) && state.Clock == .25);
            Add("exact-half-second-teleports", state.Advance(.25f) && !state.Pending && state.Teleports == 1);
            Add("one-request-one-teleport", !state.Advance(.5f) && state.Teleports == 1);
            state.Request(false);
            state.Advance(.25f);
            Add("manual-repeat-cancels-old-invoke", state.Request(true) && state.Deadline == 1.75 && state.Requests == 3);
            Add("old-deadline-does-not-teleport", !state.Advance(.25f) && state.Pending);
            Add("replacement-deadline-teleports", state.Advance(.25f) && state.Teleports == 2);
            state.Request(false);
            state.Reset();
            Add("race-reset-cancels-pending", !state.Advance(1f) && !state.Pending && state.Teleports == 0 && state.Requests == 0);
            bool passed = checks.TrueForAll(check => check.passed);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,"recovery-rules-report.json"),
                    JsonUtility.ToJson(new Report { passed=passed, checks=checks.ToArray() },true));
            }
            return passed;
            void Add(string name, bool value) => checks.Add(new Check { name=name, passed=value });
        }
    }
}
