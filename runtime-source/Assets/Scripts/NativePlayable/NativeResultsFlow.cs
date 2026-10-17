using System;
using System.Collections.Generic;

namespace Fumo.NativePlayable
{
    public enum NativeResultsDestination { NextCircuit, QuickRaceSelection, TimeTrialsSelection, CustomRaceSelection, Ceremony }
    public sealed class NativeResultsRowSnapshot
    {
        public readonly int RacerIndex, HumanIndex, Place, PriorScore, GainedScore;
        public readonly double Seconds;
        public NativeResultsRowSnapshot(int racerIndex, int humanIndex, int place, int priorScore, int gainedScore, double seconds)
        { RacerIndex = racerIndex; HumanIndex = humanIndex; Place = place; PriorScore = priorScore; GainedScore = gainedScore; Seconds = seconds; }
    }
    public sealed class NativeResultsRow
    {
        public int RacerIndex, HumanIndex, Place, PriorScore, GainedScore;
        public double Seconds;
        public int DisplayedScore, DisplayedDelta, Remaining;
        internal float Clock;
        internal bool Counting;
        public int FinalScore => PriorScore + GainedScore;
    }
    public sealed class NativeResultsFlow
    {
        public enum Screen { Closed, Waiting, Cards, Scoreboard }
        public Screen State { get; private set; }
        public int RaceId { get; private set; }
        public IReadOnlyList<NativeResultsRow> Rows => rows;
        public bool ShowsScoreboard => mode != NativeSessionMode.TimeTrials;
        public float ScoreboardElapsed { get; private set; }
        public event Action CardsOpened, CardsHidden, CardsDisposed, ScoreboardOpened, ScoreboardHidden, PanelHidden, RowsChanged;
        public event Action<NativeResultsDestination> FinishGameRequested;
        readonly List<NativeResultsRow> rows = new List<NativeResultsRow>();
        NativeSessionMode mode;
        int stationIndex, stationCount, lastFrame = -1;
        float wait, rate;
        bool finishing;
        public void Begin(int raceId, NativeSessionMode selectedMode, IEnumerable<NativeResultsRowSnapshot> sourceRows,
            int currentStation, int stations, float sourceCountRate, int beginFrame = -1)
        {
            if (sourceRows == null) throw new ArgumentNullException(nameof(sourceRows));
            if (sourceCountRate < 0 || stations < 1) throw new ArgumentOutOfRangeException();
            Abort(); RaceId = raceId; mode = selectedMode; stationIndex = currentStation; stationCount = stations;
            rate = sourceCountRate;
            foreach (var row in sourceRows) rows.Add(new NativeResultsRow { RacerIndex = row.RacerIndex, HumanIndex = row.HumanIndex,
                Place = row.Place, PriorScore = row.PriorScore, GainedScore = row.GainedScore, Seconds = row.Seconds });
            wait = 0; lastFrame = beginFrame; finishing = false;
            State = Screen.Waiting;
        }
        public void Tick(float scaledDeltaTime, int frame, bool inRace, int currentRaceId)
        {
            if (State == Screen.Closed || lastFrame == frame) return;
            lastFrame = frame;
            if (!inRace || currentRaceId != RaceId) { Abort(); return; }
            if (State == Screen.Waiting)
            {
                wait += scaledDeltaTime;
                if (wait < 3) return;
                State = Screen.Cards; CardsOpened?.Invoke();
                return;
            }
            if (State != Screen.Scoreboard) return;
            ScoreboardElapsed += scaledDeltaTime;
            bool changed = false;
            foreach (var row in rows)
            {
                if (!row.Counting) continue;
                row.Clock += scaledDeltaTime;
                if (row.Clock < rate) continue;
                row.Clock -= rate;
                row.DisplayedScore = row.FinalScore - row.Remaining;
                row.DisplayedDelta = row.Remaining;
                row.Remaining--;
                if (row.Remaining <= 0)
                {
                    row.DisplayedDelta = 0; row.DisplayedScore = row.FinalScore; row.Counting = false;
                }
                changed = true;
            }
            if (changed) RowsChanged?.Invoke();
        }
        public bool ShowScoreboard(int raceId)
        {
            if (raceId != RaceId || State != Screen.Cards || !ShowsScoreboard || finishing) return false;
            CardsHidden?.Invoke();
            // Original comparison uses cumulative total+gained only; equal totals return zero.
            rows.Sort((x, y) => y.FinalScore.CompareTo(x.FinalScore));
            foreach (var row in rows)
            {
                row.DisplayedScore = row.PriorScore; row.DisplayedDelta = row.GainedScore > 0 ? row.GainedScore : 0;
                row.Remaining = row.GainedScore; row.Clock = -1; row.Counting = row.GainedScore > 0;
            }
            ScoreboardElapsed = 0; State = Screen.Scoreboard;
            ScoreboardOpened?.Invoke(); RowsChanged?.Invoke(); return true;
        }
        public bool AllReadyFinish(int raceId)
        {
            if (raceId != RaceId || State != Screen.Cards || ShowsScoreboard || finishing) return false;
            Finish(false); return true;
        }
        public bool SubmitScoreboard(int raceId)
        {
            if (raceId != RaceId || State != Screen.Scoreboard || finishing) return false;
            Finish(true); return true;
        }
        void Finish(bool scoreboard)
        {
            finishing = true;
            if (scoreboard) ScoreboardHidden?.Invoke();
            CardsDisposed?.Invoke();
            State = Screen.Closed;
            var destination = mode == NativeSessionMode.GrandPrix
                ? (stationIndex + 1 < stationCount ? NativeResultsDestination.NextCircuit : NativeResultsDestination.Ceremony)
                : mode == NativeSessionMode.TimeTrials ? NativeResultsDestination.TimeTrialsSelection
                : mode == NativeSessionMode.CustomRace ? NativeResultsDestination.CustomRaceSelection : NativeResultsDestination.QuickRaceSelection;
            FinishGameRequested?.Invoke(destination);
            PanelHidden?.Invoke();
        }
        public void Abort()
        {
            State = Screen.Closed; rows.Clear(); wait = 0; ScoreboardElapsed = 0; finishing = false;
        }
    }
}
