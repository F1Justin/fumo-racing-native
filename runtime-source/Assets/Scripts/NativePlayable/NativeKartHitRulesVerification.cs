using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartHitRulesVerification
    {
        [Serializable] public sealed class Check { public string name; public bool passed; }
        [Serializable] sealed class Report { public bool passed; public Check[] checks; }

        public static bool CheckRules() => Run(null);

        public static bool Run(string directory)
        {
            var checks = new List<Check>();
            var state = new NativeKartHitState();
            Add("disabled-health-rejects", !state.TakeDamage(1f, false, true) && state.PendingCount == 0);
            Add("item-policy-rejects", !state.TakeDamage(1f, true, false) && !state.Invulnerable);
            Add("accepted-hit-freezes-hides-and-invulnerable", state.TakeDamage(1f, true, true) &&
                state.Frozen && state.Invulnerable && state.DamageVisible && state.AcceptedDamage == 1);
            state.Advance(.5f);
            Add("repeat-hit-rejects-without-extending", !state.TakeDamage(5f, true, true) && state.PendingCount == 1);
            state.Advance(0f);
            Add("zero-scaled-time-pauses-deadline", state.Clock == .5 && state.Frozen);
            state.Advance(.5f);
            Add("one-second-resumes-without-extra-immunity", !state.Frozen && !state.Invulnerable &&
                !state.DamageVisible && state.Resumes == 1 && state.PendingCount == 0);
            Add("new-hit-after-resume-accepted", state.TakeDamage(.25f, true, true) && state.AcceptedDamage == 2);
            state.Reset();
            Add("freeze-only-keeps-model-visible", state.FreezeFor(.5f) && state.Frozen &&
                state.Invulnerable && !state.DamageVisible && state.AcceptedDamage == 0);
            Add("freeze-rejects-existing-invulnerability", !state.FreezeFor(5f) && state.PendingCount == 1);
            state.Advance(.5f);
            Add("freeze-only-resumes", !state.Frozen && !state.Invulnerable && state.Resumes == 1);
            state.Reset();
            state.MakeInvulnerable(.5f);
            Add("immunity-alone-does-not-freeze-or-hide", state.Invulnerable && !state.Frozen && !state.DamageVisible);
            state.MakeInvulnerable(2f);
            state.Advance(.5f);
            Add("multiple-invokes-earlier-expiry-still-clears-immunity", !state.Invulnerable && state.PendingCount == 1);
            state.Reset();
            state.TakeDamage(1f, true, true);
            state.MakeInvulnerable(.25f);
            state.Advance(.25f);
            Add("remove-invulnerability-leaves-freeze-until-respawn", !state.Invulnerable && state.Frozen && state.DamageVisible);
            state.Reset();
            state.Advance(5f);
            Add("restart-cancels-pending-respawn", !state.Frozen && !state.Invulnerable &&
                !state.DamageVisible && state.PendingCount == 0 && state.Resumes == 0 && state.AcceptedDamage == 0);
            state.Reset();
            bool beforeSchedule = false;
            state.TakeDamage(1f, true, true, () => beforeSchedule = state.PendingCount == 0 && state.Frozen);
            Add("damage-feedback-precedes-respawn-schedule", beforeSchedule && state.PendingCount == 1);
            state.MakeInvulnerable(.25f);
            state.Advance(.25f);
            int frozenCalls = 0;
            Add("accepted-hit-repeats-freeze-while-health-frozen", state.TakeDamage(2f, true, true, () => frozenCalls++)
                && state.Frozen && frozenCalls == 1);
            int resumeCalls = 0;
            state.Advance(.75f, () => resumeCalls++);
            Add("first-overlapping-respawn-callback-runs", resumeCalls == 1 && !state.Frozen && state.PendingCount == 1);
            state.Advance(1.25f, () => resumeCalls++);
            Add("second-respawn-runs-after-already-unfrozen", resumeCalls == 2 && state.Resumes == 2 && !state.Frozen);
            state.Reset();
            state.TakeDamage(1f, true, true);
            state.MakeInvulnerable(.25f);
            state.Advance(.25f);
            state.FreezeFor(.75f);
            resumeCalls = 0;
            state.Advance(.75f, () => resumeCalls++);
            Add("same-frame-respawns-each-deliver-callback", resumeCalls == 2 && state.Resumes == 2 && state.PendingCount == 0);
            state.Reset();
            var recoverySounds = new List<bool>();
            state.TakeDamage(1f, true, true);
            state.MakeInvulnerable(.25f);
            state.Advance(.25f);
            state.TakeDamage(2f, true, true);
            state.Advance(.75f, recovered: recoverySounds.Add);
            state.Advance(1.25f, recovered: recoverySounds.Add);
            Add("overlapping-accepted-damage-keeps-each-audible-recovery", recoverySounds.Count == 2
                && recoverySounds[0] && recoverySounds[1]);
            state.Reset();
            recoverySounds.Clear();
            state.TakeDamage(1f, true, true);
            state.MakeInvulnerable(.25f);
            state.Advance(.25f);
            state.FreezeFor(.75f);
            state.Advance(.75f, recovered: recoverySounds.Add);
            Add("same-frame-damage-and-freeze-preserve-audible-then-silent-recovery", recoverySounds.Count == 2
                && recoverySounds[0] && !recoverySounds[1]);
            state.Reset();
            recoverySounds.Clear();
            state.FreezeFor(1f);
            state.MakeInvulnerable(.25f);
            state.Advance(.25f);
            state.TakeDamage(2f, true, true);
            state.Advance(.75f, recovered: recoverySounds.Add);
            state.Advance(1.25f, recovered: recoverySounds.Add);
            Add("freeze-before-damage-preserves-silent-then-audible-recovery", recoverySounds.Count == 2
                && !recoverySounds[0] && recoverySounds[1]);
            bool passed = checks.Count == 22 && checks.TrueForAll(check => check.passed);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "hit-rules-report.json"),
                    JsonUtility.ToJson(new Report { passed = passed, checks = checks.ToArray() }, true));
            }
            return passed;

            void Add(string name, bool passed) => checks.Add(new Check { name = name, passed = passed });
        }
    }
}
