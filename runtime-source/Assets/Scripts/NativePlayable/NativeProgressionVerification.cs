using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeProgressionVerification
    {
        [Serializable] sealed class Check { public string name; public bool passed; }
        [Serializable] sealed class Report { public bool passed; public string error; public Check[] checks; }
        public static bool Passed { get; private set; }
        public static IEnumerator StartVerification(string directory)
        {
            var checks = new List<Check>(); var report = new Report();
            string root = Path.Combine(directory, "progression-fixtures-" + Guid.NewGuid().ToString("N"));
            try
            {
                Load(Path.Combine(root, "records"));
                var first = Trial("forest", 100, "reimu");
                CheckThat(checks, "tt-first-five-double-laps", first.success && first.recordChanged && NativeProgression.GetTimeTrial("forest").lapSeconds.Length == 5);
                long generation = NativeSaveStore.Data.generation;
                var slow = Trial("forest", 110, "marisa"); var equal = Trial("forest", 100, "marisa");
                CheckThat(checks, "tt-slower-and-equal-retain-record-version-generation", slow.success && !slow.recordChanged && equal.success && !equal.recordChanged && NativeProgression.GetTimeTrial("forest").characterId == "reimu" && NativeProgression.GetTimeTrial("forest").version == 1 && NativeSaveStore.Data.generation == generation);
                CheckThat(checks, "tt-strictly-faster-updates-character-version", Trial("forest", 99, "marisa").recordChanged && NativeProgression.GetTimeTrial("forest").version == 2);
                Trial("purplefield", 125, "reimu");
                CheckThat(checks, "tt-separate-track-preserves-existing-best", NativeSaveStore.Data.timeTrialRecords.Length == 2 && NativeProgression.GetTimeTrial("forest").bestSeconds == 99);
                CheckThat(checks, "tt-invalid-five-laps-total-id-rejected", !NativeProgression.RecordTimeTrial("forest", "reimu", "kart", 5, new[] { 1d }).success && !NativeProgression.RecordTimeTrial("forest", "reimu", "kart", 6, new[] { 1d, 1d, 1d, 1d, 1d }).success && !Trial("unknown", 100, "reimu").success && !Trial("forest", double.NaN, "reimu").success);
                CheckThat(checks, "aya-threshold-189999-190-190001", NativeProgression.AyaEligible("TimeTrials", "mountains", 189.999) && NativeProgression.AyaEligible("TimeTrials", "mountains", 190) && !NativeProgression.AyaEligible("TimeTrials", "mountains", 190.001));
                CheckThat(checks, "aya-wrong-mode-track-ai-invalid-time", !NativeProgression.AyaEligible("QuickRace", "mountains", 100) && !NativeProgression.AyaEligible("TimeTrials", "forest", 100) && !NativeProgression.AyaEligible("TimeTrials", "mountains", 100, false) && !NativeProgression.AyaEligible("TimeTrials", "mountains", 0));
                var aya = Trial("mountains", 190, "reimu"); generation = NativeSaveStore.Data.generation;
                var repeatAya = Trial("mountains", 190, "reimu");
                CheckThat(checks, "aya-save-prompt-once", aya.success && aya.newlyUnlocked.Length == 1 && aya.newlyUnlocked[0] == "aya" && repeatAya.newlyUnlocked.Length == 0 && NativeSaveStore.Data.generation == generation);
                CheckThat(checks, "frog-ai-wrong-track-tag-racer-rejected", NativeProgression.TriggerFrog("wintermistylake", false).newlyUnlocked.Length == 0 && NativeProgression.TriggerFrog("forest", true).newlyUnlocked.Length == 0 && NativeProgression.TriggerFrog("wintermistylake", true, false).newlyUnlocked.Length == 0 && NativeProgression.TriggerFrog("wintermistylake", true, true, false).newlyUnlocked.Length == 0);
                var frog = NativeProgression.TriggerFrog("wintermistylake", true); generation = NativeSaveStore.Data.generation;
                CheckThat(checks, "frog-human-prompt-save-once-repeat-restart-trigger", frog.success && frog.newlyUnlocked.Length == 1 && NativeProgression.TriggerFrog("wintermistylake", true).newlyUnlocked.Length == 0 && NativeSaveStore.Data.generation == generation);
                var gp = NativeProgression.RecordGrandPrix("moriyacup", new[] { Cup("reimu", 400), Cup("marisa", 380), Cup("aya", 380), Cup("frog", 200, false) });
                CheckThat(checks, "gp-four-stations-human-minimum-first-tie", gp.success && gp.recordChanged && NativeProgression.GetCup("moriyacup").bestSeconds == 380 && NativeProgression.GetCup("moriyacup").characterId == "marisa");
                generation = NativeSaveStore.Data.generation;
                var gpSlow = NativeProgression.RecordGrandPrix("moriyacup", new[] { Cup("reimu", 390) });
                var gpEqual = NativeProgression.RecordGrandPrix("moriyacup", new[] { Cup("reimu", 380) });
                CheckThat(checks, "gp-slow-equal-retain", gpSlow.success && !gpSlow.recordChanged && gpEqual.success && !gpEqual.recordChanged && NativeSaveStore.Data.generation == generation);
                CheckThat(checks, "gp-faster-update", NativeProgression.RecordGrandPrix("moriyacup", new[] { Cup("reimu", 370) }).recordChanged && NativeProgression.GetCup("moriyacup").version == 2);
                CheckThat(checks, "gp-incomplete-no-human-wrong-cup-rejected", !NativeProgression.RecordGrandPrix("moriyacup", new[] { new NativeProgression.GrandPrixCandidate { characterId = "reimu", vehicleId = "kart", stationSeconds = new[] { 1d, 1d, 1d } } }).success && !NativeProgression.RecordGrandPrix("moriyacup", new[] { Cup("reimu", 100, false) }).success && !NativeProgression.RecordGrandPrix("other", new[] { Cup("reimu", 100) }).success);
                string saveDirectory = Path.GetDirectoryName(NativeSaveStore.FilePath); Load(saveDirectory);
                CheckThat(checks, "restart-records-unlocks-idempotent", NativeProgression.GetTimeTrial("forest").bestSeconds == 99 && NativeProgression.GetCup("moriyacup").bestSeconds == 370 && NativeSaveStore.IsUnlocked("aya") && NativeSaveStore.IsUnlocked("frog") && NativeProgression.TriggerFrog("wintermistylake", true).newlyUnlocked.Length == 0 && NativeProgression.EvaluateAya("TimeTrials", "mountains", 190).newlyUnlocked.Length == 0);
                Load(Path.Combine(root, "failure")); NativeSaveStore.Save();
                Directory.CreateDirectory(NativeSaveStore.FilePath + ".tmp");
                var before = NativeSaveStore.Data; generation = before.generation;
                var failedTT = Trial("mountains", 189, "reimu"); var failedFrog = NativeProgression.TriggerFrog("wintermistylake", true);
                CheckThat(checks, "disk-failure-restores-old-memory-no-success-or-prompt", !failedTT.success && !failedTT.recordChanged && failedTT.newlyUnlocked.Length == 0 && !failedFrog.success && failedFrog.newlyUnlocked.Length == 0 && ReferenceEquals(before, NativeSaveStore.Data) && NativeSaveStore.Data.generation == generation && NativeSaveStore.Data.timeTrialRecords.Length == 0 && NativeSaveStore.Data.unlockedKeys.Length == 0);
                Directory.Delete(NativeSaveStore.FilePath + ".tmp");
                CheckThat(checks, "failed-unlock-retry-succeeds-once", Trial("mountains", 189, "reimu").newlyUnlocked.Length == 1 && NativeProgression.TriggerFrog("wintermistylake", true).newlyUnlocked.Length == 1);
                NativeProgression.GetTimeTrial("mountains").bestSeconds = 0;
                CheckThat(checks, "tt-nonpositive-old-slot-accepts", Trial("mountains", 191, "reimu").recordChanged);
                NativeProgression.RecordGrandPrix("moriyacup", new[] { Cup("reimu", 400) });
                NativeProgression.GetCup("moriyacup").bestSeconds = 0;
                CheckThat(checks, "gp-nonpositive-old-slot-accepts", NativeProgression.RecordGrandPrix("moriyacup", new[] { Cup("reimu", 410) }).recordChanged);
                report.passed = checks.TrueForAll(value => value.passed);
            }
            catch (Exception error) { report.error = error.Message; }
            report.checks = checks.ToArray(); Passed = report.passed;
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "progression-verification.json"), JsonUtility.ToJson(report, true));
            yield return null;
        }
        static NativeProgression.Result Trial(string track, double total, string character) => NativeProgression.RecordTimeTrial(track, character, "kart", total, new[] { total / 5, total / 5, total / 5, total / 5, total / 5 });
        static NativeProgression.GrandPrixCandidate Cup(string character, double total, bool human = true) => new NativeProgression.GrandPrixCandidate { characterId = character, vehicleId = "kart", human = human, stationSeconds = new[] { total / 4, total / 4, total / 4, total / 4 } };
        static void Load(string directory) { NativeSaveStore.ResetForVerification(); NativeSaveStore.Initialize(new[] { "--save-directory", directory }); }
        static void CheckThat(List<Check> checks, string name, bool passed) => checks.Add(new Check { name = name, passed = passed });
    }
}
