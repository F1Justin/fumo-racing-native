using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeModeVerification
    {
        [Serializable]
        public sealed class Report
        {
            public bool passed = true;
            public string scope = "Pure original mode defaults, catalog isolation, scoring and tie rules.";
            public List<string> checks = new List<string>();
        }
        static void Check(Report report, bool value, string name)
        {
            report.passed &= value;
            report.checks.Add((value ? "PASS " : "FAIL ") + name);
        }
        static List<NativeSessionSeat> Roster(int count)
        {
            var roster = new List<NativeSessionSeat>();
            for (int i = 0; i < count; i++) roster.Add(new NativeSessionSeat { seatId = "seat-" + i, characterId = "reimu", vehicleId = "kart", isHuman = i == 0 });
            return roster;
        }
        static bool Rejects(Action action)
        {
            try { action(); return false; } catch (ArgumentException) { return true; }
        }
        public static bool Run(string directory)
        {
            var report = new Report();
            try
            {
                var quick = NativeModeCatalog.Get(NativeSessionMode.QuickRace);
                var tt = NativeModeCatalog.Get(NativeSessionMode.TimeTrials);
                var gp = NativeModeCatalog.Get(NativeSessionMode.GrandPrix);
                var custom = NativeModeCatalog.Get(NativeSessionMode.CustomRace);
                Check(report, quick.DefaultLaps == 5 && quick.DefaultRacerCount == 8 && quick.MaxRacerCount == 8 && quick.Countdown == 5, "Quick Race original defaults");
                Check(report, tt.DefaultLaps == 5 && tt.DefaultRacerCount == 1 && tt.MaxRacerCount == 1 && tt.Countdown == 3 && tt.DefaultItemType == "none", "Time Trials original defaults");
                Check(report, gp.DefaultLaps == 5 && gp.DefaultRacerCount == 8 && gp.MaxRacerCount == 8 && gp.Countdown == 5 && NativeModeCatalog.CupId == "moriyacup", "Grand Prix defaults and save identity");
                Check(report, custom.DefaultLaps == 5 && custom.DefaultRacerCount == 8 && custom.MaxRacerCount == 100, "Custom Race original defaults and upper bound");
                var roster = Roster(8);
                var config = NativeModeCatalog.CreateConfig(NativeSessionMode.GrandPrix, null, roster);
                roster[0].seatId = "changed";
                var tracks = NativeModeCatalog.CupTrackIds; tracks[0] = "changed";
                Check(report, config.roster[0].seatId == "seat-0" && config.trackIds.Count == 4 && NativeModeCatalog.CupTrackIds[0] == "forest", "Config and cup arrays isolate caller mutation");
                Check(report, NativeModeCatalog.CreateConfig(NativeSessionMode.CustomRace, "forest", Roster(100)).racerCount == 100, "Custom Race accepts 100 roster entries");
                Check(report, Rejects(() => NativeModeCatalog.CreateConfig(NativeSessionMode.TimeTrials, "forest", Roster(2))) && Rejects(() => NativeModeCatalog.CreateConfig(NativeSessionMode.QuickRace, "forest", Roster(9))) && Rejects(() => NativeModeCatalog.CreateConfig(NativeSessionMode.CustomRace, "forest", Roster(101))) && Rejects(() => NativeModeCatalog.CreateConfig(NativeSessionMode.QuickRace, "unknown", Roster(1))), "Mode roster bounds and unknown track rejection");
                int[] scores = { 11, 9, 7, 5, 4, 3, 2, 1 };
                bool scoreMatches = true;
                for (int i = 0; i < scores.Length; i++) scoreMatches &= NativeModeCatalog.ScoreForPlace(i + 1, 8) == scores[i];
                Check(report, scoreMatches && NativeModeCatalog.ScoreForPlace(1, 1) == 4 && Rejects(() => NativeModeCatalog.ScoreForPlace(0, 8)), "Original position scoring including one racer");
                var results = new List<NativeSessionResult>();
                for (int i = 0; i < 8; i++) results.Add(new NativeSessionResult { seatId = "seat-" + i, score = i % 2 == 0 ? 10 : 20 });
                foreach (bool ceremony in new[] { false, true })
                {
                    var sorted = NativeModeCatalog.SortScoreboard(results, ceremony);
                    Check(report, sorted[0].seatId == "seat-1" && sorted[1].seatId == "seat-3" && sorted[2].seatId == "seat-5" && sorted[3].seatId == "seat-7" && sorted[4].seatId == "seat-0" && results[0].seatId == "seat-0", ceremony ? "Ceremony equal scores preserve racer order" : "Eight racer scoreboard equal scores preserve result order");
                }
            }
            catch (Exception exception) { Check(report, false, exception.ToString()); }
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "mode-verification.json"), JsonUtility.ToJson(report, true));
            return report.passed;
        }
    }
}
