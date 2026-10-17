using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeSaveStore
    {
        public static NativeSaveData Data { get; private set; } = new NativeSaveData();
        public static string LastError { get; private set; } = "";
        public static string FilePath { get; private set; }
        public static bool SaveBlocked { get; private set; }
        static long generation;
        static bool primaryValid;
        static string initializedPath;
        static readonly HashSet<string> characterIds = new HashSet<string> { "alice", "aya", "biten", "byakuren", "cirno", "frog", "marisa", "meiling", "miko", "momiji", "nazrin", "reimu", "reisen", "remilia", "rumia", "sakuya", "sanae", "satori", "suika", "tewi", "yoshika", "youmu", "yuuka", "yuyuko" };
        static readonly HashSet<string> vehicleIds = new HashSet<string> { "kart", "bike", "skate", "cleanerbot", "shoppingcart", "broom", "genji" };
        static readonly Regex stableId = new Regex(@"^[A-Za-z0-9][A-Za-z0-9_.:/-]{0,127}$");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntime()
        {
            initializedPath = null; FilePath = null; LastError = "";
            SaveBlocked = false; primaryValid = false; generation = 0; Data = new NativeSaveData();
        }

        public static bool Initialize(string[] args, Func<string, float> legacyReader = null)
        {
            string telemetryOperation = NativeTelemetry.Begin("storage.load");
            bool telemetrySucceeded = false;
            LastError = "";
            try
            {
                string directory = Application.persistentDataPath;
                for (int i = 0; args != null && i < args.Length; i++)
                    if (args[i] == "--save-directory")
                    {
                        if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException("Missing --save-directory value");
                        directory = args[i];
                    }
                    else if (args[i].StartsWith("--save-directory=", StringComparison.Ordinal)) directory = args[i].Substring(17);
                if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Empty save directory");
                string requestedPath = Path.Combine(Path.GetFullPath(directory), "native-progress.json");
                if (initializedPath == requestedPath) return telemetrySucceeded = !SaveBlocked;
                FilePath = requestedPath; initializedPath = requestedPath;
                SaveBlocked = false; primaryValid = false; generation = 0; Data = new NativeSaveData();
                NativeSaveData loaded;
                int state = Read(FilePath, out loaded);
                if (state == 2) { SaveBlocked = true; NativeTelemetry.Event("storage.schema.blocked", "ignored", "future-primary-schema"); return Fail("Future primary schema preserved"); }
                if (state == 1) { Data = loaded; primaryValid = true; }
                else
                {
                    state = Read(FilePath + ".bak", out loaded);
                    if (state == 2) { SaveBlocked = true; NativeTelemetry.Event("storage.schema.blocked", "ignored", "future-backup-schema"); return Fail("Future backup schema preserved"); }
                    if (state == 1) { Data = loaded; NativeTelemetry.Event("storage.backup.used", "completed"); }
                }
                NativeTelemetry.SetContext("saveSource", primaryValid ? "primary" : state == 1 ? "backup" : "defaults");
                generation = Data.generation;
                if (!Data.legacyMigrationComplete && legacyReader != null)
                {
                    for (int i = 0; i < NativeTrackCatalog.Count; i++)
                    {
                        float value = legacyReader(NativeTrackCatalog.BestKeyAt(i));
                        if (FinitePositive(value)) RecordLegacyBest(NativeTrackCatalog.IdAt(i), value);
                    }
                    Data.legacyMigrationComplete = true;
                    return telemetrySucceeded = Save();
                }
                return telemetrySucceeded = true;
            }
            catch (Exception error) { SaveBlocked = true; return Fail(error.Message); }
            finally { NativeTelemetry.End(telemetryOperation, "storage.load", telemetrySucceeded ? "completed" : "failed", telemetrySucceeded ? null : SaveBlocked ? "save-blocked" : "load-failed", telemetrySucceeded ? null : LastError); }
        }

        public static bool ReplaceData(NativeSaveData candidate)
        {
            string error;
            if (!Validate(candidate, out error)) return Fail(error);
            NativeSaveData previous = Data;
            Data = candidate;
            if (Save()) return true;
            Data = previous;
            return false;
        }

        public static bool Save()
        {
            if (SaveBlocked) { NativeTelemetry.Event("storage.save", "rejected", "save-blocked"); return Fail("Save blocked to preserve unknown schema or failed initialization"); }
            string telemetryOperation = NativeTelemetry.Begin("storage.save");
            bool telemetrySucceeded = false;
            try
            {
                string error;
                if (!Validate(Data, out error)) return Fail(error);
                NativeSaveData diskData;
                int diskState = Read(FilePath, out diskData);
                if (diskState == 2) { SaveBlocked = true; return Fail("Future primary schema preserved"); }
                NativeSaveData backupData;
                if (Read(FilePath + ".bak", out backupData) == 2) { SaveBlocked = true; return Fail("Future backup schema preserved"); }
                primaryValid = diskState == 1;
                if (primaryValid) generation = Math.Max(generation, diskData.generation);
                if (generation == long.MaxValue) return Fail("Generation exhausted");
                long previous = Data.generation;
                Data.generation = generation + 1;
                string json;
                try { json = JsonUtility.ToJson(Data, true); }
                finally { Data.generation = previous; }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temporary = FilePath + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                // Recovery must retain the valid backup when the primary was corrupt or missing.
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, primaryValid ? FilePath + ".bak" : null);
                else File.Move(temporary, FilePath);
                generation++; Data.generation = generation; primaryValid = true; LastError = "";
                NativeTelemetry.SetContext("saveGeneration", generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return telemetrySucceeded = true;
            }
            catch (Exception error) { return Fail(error.Message); }
            finally { NativeTelemetry.End(telemetryOperation, "storage.save", telemetrySucceeded ? "completed" : "failed", telemetrySucceeded ? null : "save-failed", telemetrySucceeded ? null : LastError); }
        }

        public static double GetLegacyBest(string track) { foreach (var record in Data.legacyRecords) if (record.trackId == track) return record.bestSeconds; return 0; }
        public static bool IsUnlocked(string key) => Array.IndexOf(Data.unlockedKeys, key) >= 0;
        public static bool SetPreferences(string track, string character, string vehicle)
        {
            if (!Track(track) || !characterIds.Contains(character) || !vehicleIds.Contains(vehicle)) return Fail("Invalid preferences");
            Data.preferredTrack = track; Data.preferredCharacter = character; Data.preferredVehicle = vehicle; return true;
        }
        public static void ResetForVerification() { initializedPath = null; }

        public static bool RecordLegacyBest(string track, double seconds)
        {
            if (!Track(track) || !FinitePositive(seconds)) return Fail("Invalid legacy record");
            foreach (var record in Data.legacyRecords)
                if (record.trackId == track) { if (seconds < record.bestSeconds) record.bestSeconds = seconds; return true; }
            var records = new List<NativeSaveData.LegacyRecord>(Data.legacyRecords);
            records.Add(new NativeSaveData.LegacyRecord { trackId = track, bestSeconds = seconds, source = "native-playerprefs-v1" });
            Data.legacyRecords = records.ToArray(); return true;
        }

        public static bool Unlock(string key)
        {
            if (key != "aya" && key != "frog") return Fail("Invalid unlock key");
            var keys = new List<string>(Data.unlockedKeys);
            if (!keys.Contains(key)) keys.Add(key);
            Data.unlockedKeys = keys.ToArray(); return true;
        }

        static int Read(string path, out NativeSaveData data)
        {
            data = null;
            if (!File.Exists(path)) return 0;
            try
            {
                string json = File.ReadAllText(path);
                if (!json.TrimStart().StartsWith("{", StringComparison.Ordinal) || !Regex.IsMatch(json, "\"schemaVersion\"\\s*:")) return 0;
                var header = JsonUtility.FromJson<NativeSaveData>(json);
                if (header == null) return 0;
                if (header.schemaVersion > 1) { data = header; return 2; }
                data = NativeSaveJson.FromJson<NativeSaveData>(json);
                if (data == null) return 0;
                string error;
                return Validate(data, out error) ? 1 : 0;
            }
            catch (Exception) { return 0; }
        }

        public static bool Validate(NativeSaveData data, out string error)
        {
            error = "Invalid save fields";
            if (data == null || data.schemaVersion != 1 || data.generation < 0 || data.importedSources == null || data.unlockedKeys == null || data.legacyRecords == null || data.timeTrialRecords == null || data.cupRecords == null || data.settings == null || data.inputProfiles == null) return false;
            if ((!string.IsNullOrEmpty(data.preferredCharacter) && !characterIds.Contains(data.preferredCharacter)) || (!string.IsNullOrEmpty(data.preferredVehicle) && !vehicleIds.Contains(data.preferredVehicle)) || (!string.IsNullOrEmpty(data.preferredTrack) && !Track(data.preferredTrack))) return false;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string hash in data.importedSources) if (hash == null || !Regex.IsMatch(hash, "^[0-9a-f]{64}$") || !unique.Add(hash)) return false;
            unique.Clear();
            foreach (string key in data.unlockedKeys) if ((key != "aya" && key != "frog") || !unique.Add(key)) return false;
            unique.Clear();
            foreach (var record in data.legacyRecords) if (record == null || !Track(record.trackId) || !FinitePositive(record.bestSeconds) || !Id(record.source) || !unique.Add(record.trackId)) return false;
            unique.Clear();
            foreach (var record in data.timeTrialRecords) if (record == null || !Track(record.trackId) || !characterIds.Contains(record.characterId) || !vehicleIds.Contains(record.vehicleId) || record.lapCount != 5 || !FinitePositive(record.bestSeconds) || !Times(record.lapSeconds, 5, record.bestSeconds) || record.version < 0 || !OptionalId(record.source) || !unique.Add(record.trackId)) return false;
            unique.Clear();
            foreach (var record in data.cupRecords) if (record == null || record.cupId != "moriyacup" || !characterIds.Contains(record.characterId) || !vehicleIds.Contains(record.vehicleId) || !FinitePositive(record.bestSeconds) || !Times(record.stationSeconds, 4, record.bestSeconds) || record.version < 0 || !OptionalId(record.source) || record.bestRank < 0 || record.points < 0 || !unique.Add(record.cupId)) return false;
            unique.Clear();
            foreach (var setting in data.settings) if (setting == null || !Id(setting.key) || setting.value == null || setting.value.Length > 65536 || !SettingValid(setting) || !unique.Add(setting.key)) return false;
            unique.Clear();
            var profileIndices = new HashSet<int>();
            foreach (var profile in data.inputProfiles) if (profile == null || string.IsNullOrWhiteSpace(profile.name) || profile.name.Length > 128 || profile.devicePath == null || profile.overrides == null || profile.overrides.Length > 4096 || !Id(profile.profileId) || profile.index < 0 || !profileIndices.Add(profile.index) || !Overrides(profile.overrides) || !unique.Add(profile.profileId)) return false;
            error = ""; return true;
        }
        static bool Times(double[] values, int count, double total) { if (values == null || values.Length != count) return false; double sum = 0; foreach (double value in values) { if (!FinitePositive(value)) return false; sum += value; } return Math.Abs(sum - total) <= 1e-5; }
        static bool Overrides(NativeSaveData.InputOverride[] values) { var ids = new HashSet<string>(); foreach (var value in values) if (value == null || !Id(value.actionId) || value.path == null || value.path.Length > 4096 || !ids.Add(value.actionId)) return false; return true; }
        static bool SettingValid(NativeSaveData.Setting setting)
        {
            int integer; double number; bool boolean;
            switch (setting.type) {
                case "string": return true;
                case "int": return int.TryParse(setting.value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out integer);
                case "float": return double.TryParse(setting.value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number);
                case "bool": return bool.TryParse(setting.value, out boolean);
                default: return false;
            }
        }
        static bool Track(string value) => Id(value) && NativeTrackCatalog.IndexOf(value) >= 0 && NativeTrackCatalog.IdAt(NativeTrackCatalog.IndexOf(value)) == value;
        static bool Id(string value) => value != null && stableId.IsMatch(value);
        static bool OptionalId(string value) => string.IsNullOrEmpty(value) || Id(value);
        static bool FinitePositive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        static bool Fail(string error) { LastError = error; return false; }
    }
}
