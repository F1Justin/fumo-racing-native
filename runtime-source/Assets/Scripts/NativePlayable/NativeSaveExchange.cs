using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeSaveExchange
    {
        [Serializable] sealed class Source { public string kind, path, sha256; }
        [Serializable] sealed class Audit { public string status, mappingSHA256; public long sourceBytes; }
        [Serializable] sealed class Original { public int circuit, version, character, vehicle, dataVersion; public string gameversion; }
        [Serializable] sealed class Record
        {
            public string mode, stableId, characterId, vehicleId;
            public double bestSeconds;
            public double[] lapSeconds, stationSeconds;
            public Original original;
        }
        [Serializable] sealed class Exchange
        {
            public int schemaVersion;
            public Source source;
            public Audit audit;
            public Record[] importRecords;
            public string[] unlockedKeys, unknownUnlockKeys;
            public NativeSaveData.Setting[] settings;
            public NativeSaveData.InputProfile[] profiles;
        }
        public static string LastError { get; private set; } = "";
        public static bool Import(string path)
        {
            string telemetryOperation = NativeTelemetry.Begin("storage.import");
            bool telemetrySucceeded = false;
            LastError = "";
            try
            {
                if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Exchange exceeds size limit");
                var input = NativeSaveJson.FromJson<Exchange>(File.ReadAllText(path));
                if (input == null || input.schemaVersion != 1 || input.source == null || input.audit == null
                    || input.audit.status != "accepted" || !Regex.IsMatch(input.source.sha256 ?? "", "^[0-9a-f]{64}$"))
                    throw new InvalidDataException("Unaccepted exchange schema or source");
                string originalBytes = path + ".original-bytes";
                if (!File.Exists(originalBytes)) throw new InvalidDataException("Original byte backup missing");
                using (var stream = File.OpenRead(originalBytes))
                using (var sha = SHA256.Create())
                {
                    if (stream.Length != input.audit.sourceBytes || BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != input.source.sha256)
                        throw new InvalidDataException("Original source SHA mismatch");
                }
                if (NativeSaveStore.Data.importedSources.Contains(input.source.sha256)) { NativeTelemetry.Event("storage.import.duplicate", "ignored", "already-imported"); return telemetrySucceeded = true; }
                if ((input.source.kind == "tt" || input.source.kind == "gp") && !Regex.IsMatch(input.audit.mappingSHA256 ?? "", "^[0-9a-f]{64}$"))
                    throw new InvalidDataException("Explicit record mapping missing");
                if (input.source.kind != "tt" && input.source.kind != "gp" && input.source.kind != "unlockables" && input.source.kind != "profile")
                    throw new InvalidDataException("Unknown import kind");
                var candidate = NativeSaveJson.FromJson<NativeSaveData>(JsonUtility.ToJson(NativeSaveStore.Data));
                var trials = new List<NativeSaveData.TimeTrialRecord>(candidate.timeTrialRecords);
                var cups = new List<NativeSaveData.CupRecord>(candidate.cupRecords);
                foreach (var record in input.importRecords ?? new Record[0])
                {
                    if (record == null || record.original == null) throw new InvalidDataException("Record metadata missing");
                    var metadata = new NativeSaveData.OriginalMetadata
                    {
                        circuit = record.original.circuit.ToString(CultureInfo.InvariantCulture), version = record.original.version.ToString(CultureInfo.InvariantCulture),
                        character = record.original.character.ToString(CultureInfo.InvariantCulture), vehicle = record.original.vehicle.ToString(CultureInfo.InvariantCulture),
                        gameversion = record.original.gameversion, dataVersion = record.original.dataVersion.ToString(CultureInfo.InvariantCulture)
                    };
                    if (record.mode == "TimeTrials" && input.source.kind == "tt")
                    {
                        var incoming = new NativeSaveData.TimeTrialRecord { trackId = record.stableId, characterId = record.characterId, vehicleId = record.vehicleId,
                            bestSeconds = record.bestSeconds, lapSeconds = record.lapSeconds, lapCount = 5, version = record.original.version, source = "original-tt", original = metadata };
                        string error;
                        if (!NativeSaveStore.Validate(new NativeSaveData { timeTrialRecords = new[] { incoming } }, out error)) throw new InvalidDataException(error);
                        var existing = trials.Find(r => r.trackId == incoming.trackId);
                        if (existing == null) trials.Add(incoming);
                        else if (incoming.bestSeconds < existing.bestSeconds) trials[trials.IndexOf(existing)] = incoming;
                    }
                    else if (record.mode == "GrandPrix" && input.source.kind == "gp")
                    {
                        var incoming = new NativeSaveData.CupRecord { cupId = record.stableId, characterId = record.characterId, vehicleId = record.vehicleId,
                            bestSeconds = record.bestSeconds, stationSeconds = record.stationSeconds,
                            version = record.original.version, source = "original-gp", original = metadata };
                        string error;
                        if (!NativeSaveStore.Validate(new NativeSaveData { cupRecords = new[] { incoming } }, out error)) throw new InvalidDataException(error);
                        var existing = cups.Find(r => r.cupId == incoming.cupId);
                        if (existing == null) cups.Add(incoming);
                        else if (incoming.bestSeconds < existing.bestSeconds) cups[cups.IndexOf(existing)] = incoming;
                    }
                    else throw new InvalidDataException("Record mode/kind mismatch");
                }
                candidate.timeTrialRecords = trials.ToArray(); candidate.cupRecords = cups.ToArray();
                candidate.unlockedKeys = candidate.unlockedKeys.Concat(input.unlockedKeys ?? new string[0]).Distinct().ToArray();
                if (input.settings != null && input.settings.Length != 0) throw new InvalidDataException("Original settings exchange is unsupported");
                var profiles = new List<NativeSaveData.InputProfile>(candidate.inputProfiles);
                foreach (var profile in input.profiles ?? new NativeSaveData.InputProfile[0])
                {
                    if (profile == null) throw new InvalidDataException("Null profile");
                    if (!profiles.Exists(p => p.profileId == profile.profileId)) profiles.Add(profile);
                }
                candidate.inputProfiles = profiles.ToArray();
                candidate.importedSources = candidate.importedSources.Concat(new[] { input.source.sha256 }).ToArray();
                if (!NativeSaveStore.ReplaceData(candidate)) throw new InvalidDataException(NativeSaveStore.LastError);
                return telemetrySucceeded = true;
            }
            catch (Exception error) { LastError = error.Message; return false; }
            finally { NativeTelemetry.End(telemetryOperation, "storage.import", telemetrySucceeded ? "completed" : "failed", telemetrySucceeded ? null : "import-failed", telemetrySucceeded ? null : LastError); }
        }
    }
}
