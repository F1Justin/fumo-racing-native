using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeProgression
    {
        [Serializable] public sealed class Result
        {
            public bool success, recordChanged;
            public string[] newlyUnlocked = new string[0];
            public string error = "";
        }
        [Serializable] public sealed class GrandPrixCandidate
        {
            public string characterId, vehicleId;
            public double[] stationSeconds;
            public bool human = true;
        }
        public static NativeSaveData.TimeTrialRecord GetTimeTrial(string trackId)
        {
            foreach (var record in NativeSaveStore.Data.timeTrialRecords) if (record.trackId == trackId) return record;
            return null;
        }
        public static NativeSaveData.CupRecord GetCup(string cupId)
        {
            foreach (var record in NativeSaveStore.Data.cupRecords) if (record.cupId == cupId) return record;
            return null;
        }
        public static Result RecordTimeTrial(string trackId, string characterId, string vehicleId, double total, double[] laps)
        {
            var candidate = Copy();
            var record = new NativeSaveData.TimeTrialRecord { trackId = trackId, characterId = characterId, vehicleId = vehicleId, bestSeconds = total,
                lapSeconds = laps == null ? null : (double[])laps.Clone(), lapCount = 5, source = "native-time-trials" };
            var old = GetTimeTrial(trackId);
            // Validate the submitted race even when the existing best would be retained.
            if (!ValidateTrial(candidate, record, out string error)) return Failure(error);
            bool changed = old == null || old.bestSeconds <= 0 || total < old.bestSeconds;
            if (changed)
            {
                if (old != null && old.version == int.MaxValue) return Failure("Record version exhausted");
                record.version = old == null ? 1 : old.version + 1;
                var records = new List<NativeSaveData.TimeTrialRecord>(candidate.timeTrialRecords);
                records.RemoveAll(value => value.trackId == trackId); records.Add(record); candidate.timeTrialRecords = records.ToArray();
            }
            var unlocked = new List<string>();
            if (AyaEligible("TimeTrials", trackId, total, true)) AddUnlock(candidate, "aya", unlocked);
            return Commit(candidate, changed, unlocked);
        }
        public static Result RecordGrandPrix(string cupId, GrandPrixCandidate[] candidates)
        {
            if (candidates == null) return Failure("Missing cup candidates");
            NativeSaveData.CupRecord best = null;
            var data = Copy();
            foreach (var player in candidates)
            {
                if (player == null) return Failure("Missing cup candidate");
                if (!player.human) continue;
                double total = 0;
                if (player.stationSeconds == null) return Failure("Missing station times");
                foreach (double value in player.stationSeconds) total += value;
                var record = new NativeSaveData.CupRecord { cupId = cupId, characterId = player.characterId, vehicleId = player.vehicleId,
                    bestSeconds = total, stationSeconds = (double[])player.stationSeconds.Clone(), source = "native-grand-prix" };
                var validation = Copy(); validation.cupRecords = new[] { record };
                if (!NativeSaveStore.Validate(validation, out string error)) return Failure(error);
                if (best == null || total < best.bestSeconds) best = record;
            }
            if (best == null) return Failure("Cup has no human finishers");
            var old = GetCup(cupId);
            bool changed = old == null || old.bestSeconds <= 0 || best.bestSeconds < old.bestSeconds;
            if (changed)
            {
                if (old != null && old.version == int.MaxValue) return Failure("Record version exhausted");
                best.version = old == null ? 1 : old.version + 1;
                var records = new List<NativeSaveData.CupRecord>(data.cupRecords);
                records.RemoveAll(value => value.cupId == cupId); records.Add(best); data.cupRecords = records.ToArray();
            }
            return Commit(data, changed, new List<string>());
        }
        public static bool AyaEligible(string modeId, string trackId, double total, bool human = true)
            => human && modeId == "TimeTrials" && trackId == "mountains" && total > 0 && total <= 190 && !double.IsNaN(total) && !double.IsInfinity(total);
        public static Result EvaluateAya(string modeId, string trackId, double total, bool human = true)
        {
            var data = Copy(); var unlocked = new List<string>();
            if (AyaEligible(modeId, trackId, total, human)) AddUnlock(data, "aya", unlocked);
            return Commit(data, false, unlocked);
        }
        public static Result TriggerFrog(string trackId, bool human, bool playerTag = true, bool hasPlayerRacer = true)
        {
            var data = Copy(); var unlocked = new List<string>();
            if (trackId == "wintermistylake" && human && playerTag && hasPlayerRacer) AddUnlock(data, "frog", unlocked);
            return Commit(data, false, unlocked);
        }
        static bool ValidateTrial(NativeSaveData data, NativeSaveData.TimeTrialRecord record, out string error)
        {
            var records = data.timeTrialRecords;
            data.timeTrialRecords = new[] { record };
            bool valid = NativeSaveStore.Validate(data, out error);
            data.timeTrialRecords = records;
            return valid;
        }
        static NativeSaveData Copy() => NativeSaveJson.FromJson<NativeSaveData>(JsonUtility.ToJson(NativeSaveStore.Data));
        static void AddUnlock(NativeSaveData data, string key, List<string> newlyUnlocked)
        {
            var keys = new List<string>(data.unlockedKeys);
            if (keys.Contains(key)) return;
            keys.Add(key); data.unlockedKeys = keys.ToArray(); newlyUnlocked.Add(key);
        }
        static Result Commit(NativeSaveData data, bool changed, List<string> unlocked)
        {
            if (!changed && unlocked.Count == 0) return new Result { success = true };
            if (!NativeSaveStore.ReplaceData(data)) return Failure(NativeSaveStore.LastError);
            return new Result { success = true, recordChanged = changed, newlyUnlocked = unlocked.ToArray() };
        }
        static Result Failure(string error) => new Result { error = error };
    }
}
