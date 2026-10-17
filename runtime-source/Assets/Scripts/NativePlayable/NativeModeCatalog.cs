using System;
using System.Collections.Generic;

namespace Fumo.NativePlayable
{
    public sealed class NativeModeDefinition
    {
        public readonly NativeSessionMode Mode;
        public readonly string Id, DisplayName, DefaultItemType;
        public readonly int DefaultLaps, DefaultRacerCount, MaxRacerCount, Countdown;
        public NativeModeDefinition(NativeSessionMode mode, string id, string name, int laps, int racers, int maxRacers, int countdown, string items)
        {
            Mode = mode; Id = id; DisplayName = name; DefaultLaps = laps;
            DefaultRacerCount = racers; MaxRacerCount = maxRacers; Countdown = countdown; DefaultItemType = items;
        }
    }

    public static class NativeModeCatalog
    {
        public const string CupId = "moriyacup";
        public const string CupName = "Moriya Cup";
        public const bool CupOrderConfirmed = true;
        public const string CupOrderEvidence = "Shipped Windows catalog Circuit label order and default Addressables loader preserve Forest, Mountains, Purplefield, WinterMistyLake.";
        static readonly string[] cupTracks = { "forest", "mountains", "purplefield", "wintermistylake" };
        static readonly NativeModeDefinition[] definitions =
        {
            new NativeModeDefinition(NativeSessionMode.NativeRace, "native-race", "Native Race", 1, 4, 100, 5, "normal"),
            new NativeModeDefinition(NativeSessionMode.QuickRace, "quick-race", "Quick Race", 5, 8, 8, 5, "normal"),
            new NativeModeDefinition(NativeSessionMode.TimeTrials, "time-trials", "Time Trials", 5, 1, 1, 3, "none"),
            new NativeModeDefinition(NativeSessionMode.GrandPrix, "grand-prix", "Grand Prix", 5, 8, 8, 5, "normal"),
            new NativeModeDefinition(NativeSessionMode.CustomRace, "custom-race", "Custom Race", 5, 8, 100, 5, "normal")
        };
        public static string[] CupTrackIds => (string[])cupTracks.Clone();
        public static NativeModeDefinition Get(NativeSessionMode mode)
        {
            int index = (int)mode;
            if (index < 0 || index >= definitions.Length) throw new ArgumentOutOfRangeException(nameof(mode));
            return definitions[index];
        }
        public static NativeSessionConfig CreateConfig(NativeSessionMode mode, string trackId, IList<NativeSessionSeat> roster)
        {
            var definition = Get(mode);
            if (roster == null || roster.Count < 1 || roster.Count > definition.MaxRacerCount) throw new ArgumentException("Invalid roster count.", nameof(roster));
            if (mode == NativeSessionMode.TimeTrials && roster.Count != 1) throw new ArgumentException("Time trials requires one racer.", nameof(roster));
            int trackIndex = NativeTrackCatalog.IndexOf(trackId);
            if (mode != NativeSessionMode.GrandPrix && trackIndex < 0) throw new ArgumentException("Unknown track.", nameof(trackId));
            var config = new NativeSessionConfig
            {
                mode = mode, modeId = definition.Id, laps = definition.DefaultLaps, racerCount = roster.Count,
                itemType = definition.DefaultItemType,
                trackIds = mode == NativeSessionMode.GrandPrix ? new List<string>(cupTracks) : new List<string> { NativeTrackCatalog.IdAt(trackIndex) }
            };
            foreach (var seat in roster)
            {
                if (seat == null) throw new ArgumentException("Missing roster seat.", nameof(roster));
                config.roster.Add(new NativeSessionSeat { seatId = seat.seatId, characterId = seat.characterId, vehicleId = seat.vehicleId, skinIndex = seat.skinIndex, isHuman = seat.isHuman });
            }
            return config;
        }
        public static int ScoreForPlace(int place, int racerCount)
        {
            if (racerCount < 1 || place < 1 || place > racerCount) throw new ArgumentOutOfRangeException(nameof(place));
            return racerCount - place + 1 + (place <= 3 ? 4 - place : 0);
        }
        public static List<NativeSessionResult> SortScoreboard(IList<NativeSessionResult> results, bool ceremony)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var sorted = new List<NativeSessionResult>(results);
            if (sorted.Contains(null)) throw new ArgumentException("Missing result.", nameof(results));
            if (!ceremony && sorted.Count > 16)
            {
                sorted.Sort((x, y) => y.score.CompareTo(x.score));
                return sorted;
            }
            // Original GP8 introsort uses stable insertion; ceremony LINQ is stable at every size.
            for (int i = 1; i < sorted.Count; i++)
            {
                var value = sorted[i]; int j = i - 1;
                while (j >= 0 && sorted[j].score < value.score) { sorted[j + 1] = sorted[j]; j--; }
                sorted[j + 1] = value;
            }
            return sorted;
        }
    }
}
