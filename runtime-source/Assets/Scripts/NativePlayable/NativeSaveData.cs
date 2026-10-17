using System;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeSaveData
    {
        public int schemaVersion = 1;
        public long generation;
        public string[] importedSources = new string[0];
        public string[] unlockedKeys = new string[0];
        public LegacyRecord[] legacyRecords = new LegacyRecord[0];
        public TimeTrialRecord[] timeTrialRecords = new TimeTrialRecord[0];
        public CupRecord[] cupRecords = new CupRecord[0];
        public Setting[] settings = new Setting[0];
        public InputProfile[] inputProfiles = new InputProfile[0];
        public string preferredTrack = "", preferredCharacter = "", preferredVehicle = "";
        public bool legacyMigrationComplete;

        [Serializable] public sealed class LegacyRecord { public string trackId, source; public double bestSeconds; }
        [Serializable] public sealed class TimeTrialRecord { public string trackId, characterId, vehicleId; public double bestSeconds; public double[] lapSeconds = new double[0]; public int lapCount = 5; public int version; public string source; public OriginalMetadata original; }
        [Serializable] public sealed class CupRecord { public string cupId, characterId, vehicleId; public double bestSeconds; public double[] stationSeconds = new double[0]; public int version; public string source; public OriginalMetadata original; public int bestRank, points; }
        [Serializable] public sealed class Setting { public string key, value, type = "string"; }
        [Serializable] public sealed class InputProfile { public string profileId, name, devicePath; public int index; public InputOverride[] overrides = new InputOverride[0]; }
        [Serializable] public sealed class OriginalMetadata { public string circuit, version, character, vehicle, gameversion, dataVersion; }
        [Serializable] public sealed class InputOverride { public string actionId, path; }
    }
}
