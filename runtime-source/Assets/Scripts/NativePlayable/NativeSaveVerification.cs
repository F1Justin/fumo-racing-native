using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeSaveVerification
    {
        [Serializable] sealed class Check { public string name; public bool passed; }
        [Serializable] sealed class Report { public bool passed; public string error; public Check[] checks; }
        public static bool Passed { get; private set; }
        public static IEnumerator StartVerification(string directory)
        {
            var checks = new List<Check>();
            var report = new Report();
            string root = Path.Combine(directory, "save-fixtures-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string empty = Path.Combine(root, "empty");
                Load(empty);
                Add(checks, "empty-new-save", NativeSaveStore.Data.generation == 0 && NativeSaveStore.Save());
                Add(checks, "repeat-save-generation", NativeSaveStore.Save() && NativeSaveStore.Data.generation == 2);
                string primary = NativeSaveStore.FilePath;
                File.WriteAllText(primary + ".tmp", "interrupted");
                Load(empty);
                Add(checks, "interrupted-temp-ignored", NativeSaveStore.Data.generation == 2);
                File.WriteAllText(primary, "{broken");
                Load(empty);
                Add(checks, "corrupt-primary-backup-recovery", NativeSaveStore.Data.generation == 1);
                string backup = File.ReadAllText(primary + ".bak");
                Add(checks, "recovery-save-preserves-backup", NativeSaveStore.Save() && File.ReadAllText(primary + ".bak") == backup);
                File.Delete(primary);
                Load(empty);
                Add(checks, "missing-primary-backup-recovery", NativeSaveStore.Data.generation == 1 && NativeSaveStore.Save());
                File.WriteAllText(primary, "{\"schemaVersion\":999,\"generation\":22}");
                Load(empty);
                Add(checks, "future-schema-preserved", NativeSaveStore.SaveBlocked && !NativeSaveStore.Save() && File.ReadAllText(primary).Contains("999"));
                string legacy = Path.Combine(root, "legacy");
                int reads = 0;
                Load(legacy, key => { reads++; return 123f; });
                Load(legacy, key => { reads++; return 1f; });
                Add(checks, "legacy-migration-once-separate-from-time-trial", reads == NativeTrackCatalog.Count && NativeSaveStore.Data.legacyRecords.Length == NativeTrackCatalog.Count && NativeSaveStore.Data.timeTrialRecords.Length == 0 && NativeSaveStore.GetLegacyBest("forest") == 123);
                Add(checks, "unlock-idempotent", NativeSaveStore.Unlock("aya") && NativeSaveStore.Unlock("aya") && NativeSaveStore.Data.unlockedKeys.Length == 1 && !NativeSaveStore.Unlock("invalid"));
                NativeSaveStore.Data.settings = new[] { new NativeSaveData.Setting { key = "volume", type = "float", value = "0.75" } };
                NativeSaveStore.Data.inputProfiles = new[] { new NativeSaveData.InputProfile { profileId = "keyboard", index = 0, name = "Keyboard", devicePath = "<Keyboard>", overrides = new[] { new NativeSaveData.InputOverride { actionId = "steer", path = "<Keyboard>/a" } } } };
                NativeSaveStore.Data.timeTrialRecords = new[] { new NativeSaveData.TimeTrialRecord { trackId = "forest", characterId = "reimu", vehicleId = "kart", bestSeconds = 100, lapSeconds = new[] { 20d, 20d, 20d, 20d, 20d }, version = 1, source = "fixture" } };
                NativeSaveStore.Data.cupRecords = new[] { new NativeSaveData.CupRecord { cupId = "moriyacup", characterId = "reimu", vehicleId = "kart", bestSeconds = 400, stationSeconds = new[] { 100d, 100d, 100d, 100d }, version = 1, source = "fixture" } };
                bool saved = NativeSaveStore.SetPreferences("forest", "reimu", "kart") && NativeSaveStore.Save();
                Load(legacy);
                Add(checks, "settings-profiles-records-roundtrip", saved && NativeSaveStore.Data.settings[0].value == "0.75" && NativeSaveStore.Data.inputProfiles[0].overrides[0].path == "<Keyboard>/a" && NativeSaveStore.Data.timeTrialRecords[0].lapSeconds.Length == 5 && NativeSaveStore.Data.cupRecords[0].bestSeconds == 400 && NativeSaveStore.Data.preferredTrack == "forest");
                var validTrial = NativeSaveStore.Data.timeTrialRecords[0];
                validTrial.characterId = "unknown";
                Add(checks, "unknown-content-id-rejected", !NativeSaveStore.Save());
                validTrial.characterId = "reimu";
                NativeSaveStore.Data.timeTrialRecords = new[] { validTrial, new NativeSaveData.TimeTrialRecord { trackId = "forest", characterId = "marisa", vehicleId = "kart", bestSeconds = 100, lapSeconds = new[] { 20d, 20d, 20d, 20d, 20d } } };
                Add(checks, "same-track-different-character-duplicate-rejected", !NativeSaveStore.Save());
                NativeSaveStore.Data.timeTrialRecords = new[] { validTrial };
                validTrial.lapSeconds = new[] { 50d, 50d };
                Add(checks, "wrong-lap-count-rejected", !NativeSaveStore.Save());
                validTrial.lapSeconds = new[] { 20d, 20d, 20d, 20d, 19d };
                Add(checks, "wrong-lap-total-rejected", !NativeSaveStore.Save());
                validTrial.lapSeconds = new[] { 20d, 20d, 20d, 20d, 20d };
                var validProfile = NativeSaveStore.Data.inputProfiles[0];
                NativeSaveStore.Data.inputProfiles = new[] { validProfile, new NativeSaveData.InputProfile { profileId = "other", index = 0, name = "Other", devicePath = "<Gamepad>" } };
                Add(checks, "duplicate-profile-index-rejected", !NativeSaveStore.Save());
                NativeSaveStore.Data.inputProfiles = new[] { validProfile };
                NativeSaveStore.Data.importedSources = new[] { new string('a', 64) };
                Add(checks, "import-hash-saved", NativeSaveStore.Save());
                NativeSaveStore.Data.importedSources = new[] { "invalid" };
                Add(checks, "invalid-import-hash-rejected", !NativeSaveStore.Save());
                NativeSaveStore.Data.importedSources = new[] { new string('a', 64) };
                NativeSaveStore.Data.timeTrialRecords[0].bestSeconds = double.NaN;
                Add(checks, "nan-rejected", !NativeSaveStore.Save());
                NativeSaveStore.Data.timeTrialRecords[0].bestSeconds = 100;
                NativeSaveStore.Data.unlockedKeys = new[] { "aya", "aya" };
                Add(checks, "duplicate-rejected", !NativeSaveStore.Save());
                NativeSaveStore.Data.unlockedKeys = new[] { "aya" };
                File.Delete(NativeSaveStore.FilePath + ".tmp");
                Directory.CreateDirectory(NativeSaveStore.FilePath + ".tmp");
                long generation = NativeSaveStore.Data.generation;
                Add(checks, "disk-failure-keeps-generation-and-primary", !NativeSaveStore.Save() && NativeSaveStore.Data.generation == generation && File.Exists(NativeSaveStore.FilePath));
                string futureBackupDirectory = Path.Combine(root, "future-backup");
                Load(futureBackupDirectory);
                NativeSaveStore.Save();
                string futureBackup = NativeSaveStore.FilePath + ".bak";
                File.WriteAllText(futureBackup, "{\"schemaVersion\":999,\"generation\":99}");
                Add(checks, "future-backup-preserved-during-save", !NativeSaveStore.Save() && NativeSaveStore.SaveBlocked && File.ReadAllText(futureBackup).Contains("999"));
                string invalidRecovery = Path.Combine(root, "invalid-recovery");
                Load(invalidRecovery);
                NativeSaveStore.Save();
                File.WriteAllText(NativeSaveStore.FilePath + ".bak", "{\"schemaVersion\":1,\"generation\":7,\"unlockedKeys\":[\"unknown\"]}");
                File.WriteAllText(NativeSaveStore.FilePath, "broken");
                Load(invalidRecovery);
                Add(checks, "invalid-backup-recovery-starts-empty", NativeSaveStore.Data.generation == 0 && NativeSaveStore.Data.unlockedKeys.Length == 0);
                string precisionDirectory = Path.Combine(root, "precision");
                Load(precisionDirectory);
                double precise = BitConverter.Int64BitsToDouble(0x405A558F1C0AC500L);
                var preciseLaps = new[] { 20d, 20d, 20d, 20d, precise - 80 };
                var recorded = NativeProgression.RecordTimeTrial("forest", "reimu", "kart", precise, preciseLaps);
                long preciseGeneration = NativeSaveStore.Data.generation;
                var copied = NativeUserSettings.CopyData();
                Add(checks, "double-copy-preserves-live-tt-bits", recorded.success && SameBits(copied.timeTrialRecords[0].bestSeconds, precise) && SameBits(copied.timeTrialRecords[0].lapSeconds[4], preciseLaps[4]) && NativeSaveStore.Data.generation == preciseGeneration);
                Load(precisionDirectory);
                Add(checks, "double-disk-reload-preserves-live-tt-bits-generation", SameBits(NativeProgression.GetTimeTrial("forest").bestSeconds, precise) && SameBits(NativeProgression.GetTimeTrial("forest").lapSeconds[4], preciseLaps[4]) && NativeSaveStore.Data.generation == preciseGeneration);
                var equalPrecise = NativeProgression.RecordTimeTrial("forest", "marisa", "kart", precise, preciseLaps);
                Add(checks, "equal-double-best-retains-exact-record-and-generation", equalPrecise.success && !equalPrecise.recordChanged && NativeProgression.GetTimeTrial("forest").characterId == "reimu" && SameBits(NativeProgression.GetTimeTrial("forest").bestSeconds, precise) && NativeSaveStore.Data.generation == preciseGeneration);
                bool settingSaved = NativeUserSettings.TrySetInt("fov", 60);
                Load(precisionDirectory);
                Add(checks, "settings-copy-and-save-preserve-live-tt-bits", settingSaved && SameBits(NativeProgression.GetTimeTrial("forest").bestSeconds, precise) && NativeSaveStore.Data.generation == preciseGeneration + 1);
                var lexical = NativeSaveJson.FromJson<NativeSaveData>("{ \"schemaVersion\":1,\"legacyRecords\":[{\"trackId\":\"forest\",\"source\":\"native-playerprefs-v1\",\"bestSeconds\":1.0533685971309387e2}],\"settings\":[{\"key\":\"text\",\"type\":\"string\",\"value\":\"numeric 9.99, \\\"quoted\\\" and \\u4e2d\"}] }");
                Add(checks, "double-exponent-and-json-escaped-strings", SameBits(lexical.legacyRecords[0].bestSeconds, precise) && lexical.settings[0].value == "numeric 9.99, \"quoted\" and 中");
                report.passed = checks.TrueForAll(check => check.passed);
            }
            catch (Exception error) { report.error = error.Message; report.passed = false; }
            report.checks = checks.ToArray(); Passed = report.passed;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "save-verification.json"), JsonUtility.ToJson(report, true));
            yield return null;
        }
        static bool SameBits(double left, double right) => BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
        static void Load(string directory, Func<string, float> reader = null)
        {
            NativeSaveStore.ResetForVerification();
            NativeSaveStore.Initialize(new[] { "--save-directory", directory }, reader);
        }
        static void Add(List<Check> checks, string name, bool passed) { checks.Add(new Check { name = name, passed = passed }); }
    }
}
