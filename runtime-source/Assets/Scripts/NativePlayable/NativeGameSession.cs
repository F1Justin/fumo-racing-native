using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public enum NativeSessionMode { NativeRace, QuickRace, TimeTrials, GrandPrix, CustomRace }
    public enum NativeSessionState { Ready, Racing, StationComplete, Completed, Cancelled }

    [Serializable]
    public sealed class NativeSessionSeat
    {
        public string seatId, characterId, vehicleId;
        public int skinIndex;
        public bool isHuman;
    }

    [Serializable]
    public sealed class NativeSessionConfig
    {
        public NativeSessionMode mode;
        public string modeId = "native-race";
        public List<string> trackIds = new List<string>();
        public int laps = 1, racerCount = 4;
        public string itemType = "normal";
        public NativeCustomRaceRules customRules = new NativeCustomRaceRules();
        public List<NativeSessionSeat> roster = new List<NativeSessionSeat>();
        public List<int> sourceStorageOrder = new List<int>();
    }

    [Serializable]
    public sealed class NativeSessionResult
    {
        public string seatId;
        public double time;
        public int place, score;
    }

    [Serializable]
    public sealed class NativeSessionStation
    {
        public string trackId;
        public int index;
        public List<NativeSessionResult> results = new List<NativeSessionResult>();
    }

    [Serializable]
    public sealed class NativeSessionSnapshot
    {
        public string sessionId;
        public NativeSessionConfig config;
        public NativeSessionState state;
        public int stationIndex;
        public List<NativeSessionResult> pending = new List<NativeSessionResult>();
        public List<NativeSessionResult> totals = new List<NativeSessionResult>();
        public List<NativeSessionStation> stations = new List<NativeSessionStation>();
        public string CurrentTrackId => config.trackIds[stationIndex];
    }

    public static class NativeGameSession
    {
        // IDs match the original character assets and NativeVehicleRosterBuild classes.
        static readonly HashSet<string> characterIds = new HashSet<string> { "alice", "aya", "biten", "byakuren", "cirno", "frog", "marisa", "meiling", "miko", "momiji", "nazrin", "reimu", "reisen", "remilia", "rumia", "sakuya", "sanae", "satori", "suika", "tewi", "yoshika", "youmu", "yuuka", "yuyuko" };
        static readonly HashSet<string> vehicleIds = new HashSet<string> { "kart", "bike", "skate", "cleanerbot", "shoppingcart", "broom", "genji" };
        static NativeSessionSnapshot current;
        static string cancelledSessionId;
        public static NativeSessionSnapshot Current => current == null ? null : Copy(current);
        public static string CurrentTrackId => current == null ? null : current.CurrentTrackId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() { current = null; cancelledSessionId = null; }

        static T Copy<T>(T value) { return NativeSaveJson.FromJson<T>(JsonUtility.ToJson(value)); }
        public static string StableId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Stable ID is empty.");
            value = value.Trim().ToLowerInvariant();
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_')
                    throw new ArgumentException("Stable ID contains invalid characters.");
            return value;
        }

        public static string Create(NativeSessionConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var normalized = Copy(config);
            if (normalized.customRules == null) normalized.customRules = new NativeCustomRaceRules();
            normalized.customRules.Validate();
            if (!Enum.IsDefined(typeof(NativeSessionMode), normalized.mode)) throw new ArgumentException("Unknown mode.");
            string[] modes = { "native-race", "quick-race", "time-trials", "grand-prix", "custom-race" };
            normalized.modeId = StableId(normalized.modeId);
            if (normalized.modeId != modes[(int)normalized.mode]) throw new ArgumentException("Mode ID mismatch.");
            normalized.itemType = StableId(normalized.itemType);
            if (normalized.itemType != "normal" && normalized.itemType != "ufo" && normalized.itemType != "none") throw new ArgumentException("Unknown item type.");
            if (normalized.mode == NativeSessionMode.TimeTrials && normalized.racerCount != 1) throw new ArgumentException("Time trials requires one racer.");
            if (normalized.laps < 1 || normalized.laps > 999 || normalized.racerCount < 1 || normalized.racerCount > 100 || normalized.roster == null || normalized.roster.Count != normalized.racerCount)
                throw new ArgumentException("Invalid race configuration.");
            if (normalized.trackIds == null || normalized.trackIds.Count == 0) throw new ArgumentException("Explicit track playlist required.");
            if (normalized.mode != NativeSessionMode.GrandPrix && normalized.trackIds.Count != 1) throw new ArgumentException("Single-race mode requires one track.");
            for (int i = 0; i < normalized.trackIds.Count; i++)
            {
                string track = StableId(normalized.trackIds[i]);
                int index = NativeTrackCatalog.IndexOf(track);
                if (index < 0) throw new ArgumentException("Unknown track.");
                normalized.trackIds[i] = NativeTrackCatalog.IdAt(index);
            }
            var seats = new HashSet<string>();
            foreach (var seat in normalized.roster)
            {
                if (seat == null) throw new ArgumentException("Missing roster seat.");
                seat.seatId = StableId(seat.seatId);
                seat.characterId = StableId(seat.characterId);
                seat.vehicleId = StableId(seat.vehicleId);
                if (!characterIds.Contains(seat.characterId) || !vehicleIds.Contains(seat.vehicleId)) throw new ArgumentException("Unknown roster character or vehicle.");
                if (seat.skinIndex < 0) throw new ArgumentException("Invalid roster skin index.");
                if (!seats.Add(seat.seatId)) throw new ArgumentException("Duplicate roster seat.");
            }
            var next = new NativeSessionSnapshot { sessionId = Guid.NewGuid().ToString("N"), config = normalized, state = NativeSessionState.Ready };
            foreach (int storage in NativeLocalRosterOrder.Read(normalized)) next.totals.Add(new NativeSessionResult { seatId = normalized.roster[storage].seatId });
            cancelledSessionId = null;
            current = next;
            return next.sessionId;
        }

        public static bool Resolve(string sessionId, string trackId)
        {
            if (current == null || current.sessionId != sessionId) return false;
            try { return current.CurrentTrackId == StableId(trackId); }
            catch (ArgumentException) { return false; }
        }
        static bool Matches(string id) { return current != null && current.sessionId == id; }
        public static bool Begin(string sessionId, string trackId)
        {
            if (!Resolve(sessionId, trackId) || current.state != NativeSessionState.Ready) return false;
            current.state = NativeSessionState.Racing;
            return true;
        }
        public static bool Restart(string sessionId)
        {
            if (!Matches(sessionId)) return false;
            if (current.state == NativeSessionState.Ready) return true;
            if (current.state != NativeSessionState.Racing) return false;
            current.pending.Clear();
            current.state = NativeSessionState.Ready;
            return true;
        }
        public static bool RecordFinish(string sessionId, string seatId, double time, int place)
        {
            if (!Matches(sessionId) || current.state != NativeSessionState.Racing || double.IsNaN(time) || double.IsInfinity(time) || time < 0 || place < 1 || place > current.config.racerCount) return false;
            try { seatId = StableId(seatId); } catch (ArgumentException) { return false; }
            if (!current.config.roster.Exists(s => s.seatId == seatId) || current.pending.Exists(r => r.seatId == seatId || r.place == place)) return false;
            current.pending.Add(new NativeSessionResult { seatId = seatId, time = time, place = place, score = current.config.racerCount - place + 1 + (place <= 3 ? 4 - place : 0) });
            return true;
        }
        public static bool CompleteStation(string sessionId)
        {
            if (!Matches(sessionId)) return false;
            if (current.state == NativeSessionState.StationComplete || current.state == NativeSessionState.Completed) return true;
            if (current.state != NativeSessionState.Racing || current.pending.Count != current.config.racerCount) return false;
            foreach (var result in current.pending)
            {
                var total = current.totals.Find(r => r.seatId == result.seatId);
                total.time += result.time;
                total.score += result.score;
            }
            current.stations.Add(new NativeSessionStation { trackId = current.CurrentTrackId, index = current.stationIndex,
                results = current.pending.ConvertAll(r => new NativeSessionResult { seatId = r.seatId, time = r.time, place = r.place, score = r.score }) });
            current.state = current.stationIndex + 1 == current.config.trackIds.Count ? NativeSessionState.Completed : NativeSessionState.StationComplete;
            return true;
        }
        public static bool Advance(string sessionId)
        {
            if (!Matches(sessionId) || current.state != NativeSessionState.StationComplete) return false;
            current.stationIndex++;
            current.pending.Clear();
            current.state = NativeSessionState.Ready;
            return true;
        }
        public static bool Cancel(string sessionId)
        {
            if (current == null) return sessionId != null && sessionId == cancelledSessionId;
            if (!Matches(sessionId)) return false;
            cancelledSessionId = sessionId;
            current.state = NativeSessionState.Cancelled;
            current = null;
            return true;
        }
    }
}
