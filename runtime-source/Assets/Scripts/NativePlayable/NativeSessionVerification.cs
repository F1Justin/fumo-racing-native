using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeSessionVerification
    {
        [Serializable]
        public sealed class Report
        {
            public bool passed = true;
            public string scope = "Pure session rules and JSON roundtrip; scene binding is verified by track-switch integration.";
            public List<string> checks = new List<string>();
            public NativeSessionSnapshot roundtrip;
        }
        static void Check(Report report, bool success, string name)
        {
            report.checks.Add((success ? "PASS " : "FAIL ") + name);
            report.passed &= success;
        }
        static NativeSessionConfig Config()
        {
            return new NativeSessionConfig
            {
                mode = NativeSessionMode.GrandPrix, modeId = "grand-prix",
                trackIds = new List<string> { " Forest ", "mountains" }, racerCount = 2, laps = 1,
                roster = new List<NativeSessionSeat>
                {
                    new NativeSessionSeat { seatId = "seat-0", characterId = "reimu", vehicleId = "kart", isHuman = true },
                    new NativeSessionSeat { seatId = "seat-1", characterId = "marisa", vehicleId = "kart", isHuman = false }
                }
            };
        }
        public static bool Run(string directory)
        {
            var report = new Report();
            NativeGameSession.Reset();
            try
            {
                var config = Config();
                string id = NativeGameSession.Create(config);
                config.trackIds[0] = "mutated";
                Check(report, NativeGameSession.CurrentTrackId == "forest", "Config clone and stable ID normalization");
                var snapshot = NativeGameSession.Current;
                snapshot.config.trackIds[0] = "mutated";
                Check(report, NativeGameSession.Resolve(id, "forest") && !NativeGameSession.Resolve("stale", "forest") && !NativeGameSession.Resolve(id, "mountains"), "Session and track identity binding with snapshot isolation");
                Check(report, !NativeGameSession.Advance(id) && !NativeGameSession.RecordFinish(id, "seat-0", 1, 1), "Ready rejects advance and finish");
                Check(report, !NativeGameSession.Begin(id, "mountains") && NativeGameSession.Begin(id, "forest"), "Begin binds current track");
                Check(report, !NativeGameSession.RecordFinish(id, "seat-0", double.NaN, 1) && !NativeGameSession.RecordFinish(id, "missing", 1, 1), "Invalid finish rejected");
                Check(report, NativeGameSession.RecordFinish(id, "seat-0", 10, 1) && !NativeGameSession.RecordFinish(id, "seat-0", 11, 2) && !NativeGameSession.RecordFinish(id, "seat-1", 11, 1), "Duplicate seat and place rejected");
                Check(report, !NativeGameSession.CompleteStation(id) && NativeGameSession.Restart(id) && NativeGameSession.Restart(id) && NativeGameSession.Current.pending.Count == 0 && NativeGameSession.Current.totals[0].time == 0, "Restart clears uncommitted result");
                NativeGameSession.Begin(id, "forest");
                NativeGameSession.RecordFinish(id, "seat-0", 10, 1);
                NativeGameSession.RecordFinish(id, "seat-1", 11, 2);
                Check(report, NativeGameSession.CompleteStation(id) && NativeGameSession.CompleteStation(id) && NativeGameSession.Current.totals[0].score == 5 && NativeGameSession.Current.totals[0].time == 10, "Station settlement is idempotent");
                Check(report, NativeGameSession.Advance(id) && !NativeGameSession.Advance(id) && NativeGameSession.CurrentTrackId == "mountains" && NativeGameSession.Current.pending.Count == 0, "Advance consumes one committed station");
                NativeGameSession.Begin(id, "mountains");
                NativeGameSession.RecordFinish(id, "seat-0", 20, 2);
                NativeGameSession.RecordFinish(id, "seat-1", 19, 1);
                Check(report, NativeGameSession.CompleteStation(id) && NativeGameSession.CompleteStation(id) && NativeGameSession.Current.state == NativeSessionState.Completed && !NativeGameSession.Advance(id), "Last station completes playlist");
                snapshot = NativeGameSession.Current;
                report.roundtrip = NativeSaveJson.FromJson<NativeSessionSnapshot>(JsonUtility.ToJson(snapshot));
                Check(report, JsonUtility.ToJson(snapshot) == JsonUtility.ToJson(report.roundtrip) && report.roundtrip.totals[0].score == 8 && report.roundtrip.totals[0].time == 30 && report.roundtrip.config.roster[0].isHuman, "JSON roundtrip preserves roster and cumulative results");
                Check(report, NativeGameSession.Cancel(id) && NativeGameSession.Cancel(id) && NativeGameSession.Current == null && !NativeGameSession.Resolve(id, "mountains"), "Cancel releases session references");
                const double exactSeconds = 105.33685971309387;
                id = NativeGameSession.Create(Config());
                NativeGameSession.Begin(id, "forest");
                NativeGameSession.RecordFinish(id, "seat-0", exactSeconds, 1);
                NativeGameSession.RecordFinish(id, "seat-1", exactSeconds + 1, 2);
                Check(report, BitConverter.DoubleToInt64Bits(NativeGameSession.Current.pending[0].time) == BitConverter.DoubleToInt64Bits(exactSeconds), "Snapshot retains exact pending double bits");
                NativeGameSession.CompleteStation(id);
                NativeGameSession.Advance(id);
                snapshot = NativeGameSession.Current;
                Check(report, snapshot.CurrentTrackId == "mountains"
                    && BitConverter.DoubleToInt64Bits(snapshot.totals[0].time) == BitConverter.DoubleToInt64Bits(exactSeconds)
                    && BitConverter.DoubleToInt64Bits(snapshot.stations[0].results[0].time) == BitConverter.DoubleToInt64Bits(exactSeconds), "Next station retains exact cumulative and historical double bits");
                NativeGameSession.Cancel(id);
                var invalid = Config(); invalid.roster[1].seatId = "seat-0";
                bool rejected = false;
                try { NativeGameSession.Create(invalid); } catch (ArgumentException) { rejected = true; }
                Check(report, rejected && NativeGameSession.Current == null, "Invalid roster rejected atomically");
                var invalidCases = new List<NativeSessionConfig>();
                invalid = Config(); invalid.trackIds[0] = "water"; invalidCases.Add(invalid);
                invalid = Config(); invalid.itemType = "unknown"; invalidCases.Add(invalid);
                invalid = Config(); invalid.roster[0].characterId = "unknown"; invalidCases.Add(invalid);
                invalid = Config(); invalid.roster[0].vehicleId = "unknown"; invalidCases.Add(invalid);
                invalid = Config(); invalid.laps = 1000; invalidCases.Add(invalid);
                invalid = Config(); invalid.racerCount = 101; invalidCases.Add(invalid);
                invalid = Config(); invalid.mode = NativeSessionMode.TimeTrials; invalid.modeId = "time-trials"; invalid.trackIds.RemoveAt(1); invalidCases.Add(invalid);
                foreach (var invalidConfig in invalidCases)
                {
                    rejected = false;
                    try { NativeGameSession.Create(invalidConfig); } catch (ArgumentException) { rejected = true; }
                    Check(report, rejected && NativeGameSession.Current == null, "Unknown IDs and bounded mode configuration rejected");
                }
                foreach (NativeSessionMode mode in Enum.GetValues(typeof(NativeSessionMode)))
                {
                    var modeConfig = Config(); modeConfig.mode = mode;
                    modeConfig.modeId = new[] { "native-race", "quick-race", "time-trials", "grand-prix", "custom-race" }[(int)mode];
                    if (mode != NativeSessionMode.GrandPrix) modeConfig.trackIds.RemoveAt(1);
                    if (mode == NativeSessionMode.TimeTrials) { modeConfig.racerCount = 1; modeConfig.roster.RemoveAt(1); modeConfig.itemType = "none"; }
                    string modeSession = NativeGameSession.Create(modeConfig);
                    Check(report, NativeGameSession.Current.config.mode == mode, "Mode model " + mode);
                    NativeGameSession.Cancel(modeSession);
                }
            }
            catch (Exception error) { Check(report, false, error.GetType().Name + ": " + error.Message); }
            finally { NativeGameSession.Reset(); }
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "session-report.json"), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("NATIVE_SESSION_VERIFICATION " + (report.passed ? "PASS" : "FAIL"));
            return report.passed;
        }
    }
}
