using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        readonly NativeResultsFlow resultsFlow = new NativeResultsFlow();
        readonly NativeResultsAvatarSet sourceResultsAvatars = new NativeResultsAvatarSet();
        bool sourceResultsBound;
        internal void BeginSourceResults(int raceId)
        {
            BindSourceResults();
            var session = NativeGameSession.Current;
            if (session == null) throw new InvalidOperationException("Results require a source-completed station snapshot");
            var station = session.stations.LastOrDefault(s => s.index == session.stationIndex);
            var finished = station == null ? session.pending : station.results;
            if (finished.Count != session.config.racerCount)
                throw new InvalidOperationException("AutoCompleteResults must append all source ranks before ResultsScreen begins");
            var order = SourceResultOrderSnapshot();
            if (order.Length != session.config.racerCount || order.Distinct().Count() != order.Length)
                throw new InvalidOperationException("Original results order snapshot must contain every racer exactly once");
            var rows = new List<NativeResultsRowSnapshot>();
            foreach (int racerIndex in order)
            {
                int racer = racerIndex;
                var result = finished.Single(r => r.seatId == session.config.roster[racer].seatId);
                int human = Array.FindIndex(sourceRaceState.Humans, h => h.Joined && h.RacerIndex == racer);
                var total = session.totals.Single(t => t.seatId == result.seatId);
                rows.Add(new NativeResultsRowSnapshot(racer, human, result.place,
                    total.score - (station == null ? 0 : result.score), result.score, result.time));
            }
            resultsFlow.Begin(raceId, raceConfig.mode, rows, session.stationIndex, session.config.trackIds.Count, .05f, Time.frameCount);
            hud.BindSourceResults(resultsFlow, racerNames, vehicleRoster, session);
            NativeTelemetry.Event("results.prepared", "accepted", detail: "rows=" + rows.Count);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--results-natural-probe") >= 0)
            {
                var probe = GetComponent<NativeResultsNaturalProbe>() ?? gameObject.AddComponent<NativeResultsNaturalProbe>();
                probe.ReadReadyAlpha = hud.SourceReadyAlphas;
                probe.Bind(resultsFlow, sourceRaceState);
            }
        }
        void BindSourceResults()
        {
            if (sourceResultsBound) return;
            sourceResultsBound = true;
            sourceRaceState.GameModeFinished += CommitSourceModeTimes;
            resultsFlow.CardsOpened += () =>
            {
                CaptureSourcePresentationTimes();
                sourceRaceState.BeginResults(resultsFlow.ShowsScoreboard);
                hud.OpenSourceResultCards();
                NativeTelemetry.Event("results.cards.opened", "applied");
                if(!readyStructureDiagnostic)
                foreach (var row in resultsFlow.Rows.Where(r => r.HumanIndex >= 0))
                {
                    var seat = NativeGameSession.Current.config.roster[row.RacerIndex];
                    var model = vehicleRoster.DriverModelAt(vehicleRoster.CharacterIndexOf(seat.characterId), seat.skinIndex);
                    hud.BindSourceResultAvatar(row.HumanIndex, sourceResultsAvatars.Create(row.HumanIndex, model));
                }
            };
            resultsFlow.CardsHidden += () => hud.HideSourceResultCards();
            resultsFlow.CardsDisposed += () => { sourceResultsAvatars.Dispose(); ClearResultsPreview(); };
            resultsFlow.ScoreboardOpened += () => { sourceRaceState.HideResultsCards(); hud.OpenSourceScoreboard(); NativeTelemetry.Event("results.scoreboard.opened", "applied"); };
            resultsFlow.ScoreboardHidden += () => hud.HideSourceScoreboard();
            resultsFlow.PanelHidden += () => { hud.CloseSourceResults(); NativeTelemetry.Event("results.closed", "applied"); };
            resultsFlow.FinishGameRequested += destination =>
            {
                NativeTelemetry.Event("results.confirmed", "accepted", detail: destination.ToString());
                FinishSourceGame();
                if (destination == NativeResultsDestination.NextCircuit || destination == NativeResultsDestination.Ceremony)
                    ContinueGrandPrix();
                else
                {
                    ResetRace(false);
                    selectedMode = destination == NativeResultsDestination.TimeTrialsSelection ? NativeSessionMode.TimeTrials
                        : destination == NativeResultsDestination.CustomRaceSelection ? NativeSessionMode.CustomRace : NativeSessionMode.QuickRace;
                    UpdateModeSummary(); OpenModeCharacters();
                }
            };
        }
        internal void CompleteSourceCeremony()
        {
            FinishSourceGame(); ClearCeremony(); ResetRace(false);
        }
        internal void ReturnSourceModeMenu()
        {
            bool timeTrials = selectedMode == NativeSessionMode.TimeTrials;
            ResetRace(false);
            if (timeTrials) { selectedMode = NativeSessionMode.TimeTrials; UpdateModeSummary(); OpenModeCharacters(); }
        }
        internal void TickSourceResults(int raceId, bool inRace) => resultsFlow.Tick(Time.deltaTime, Time.frameCount, inRace, raceId);
        internal bool SourceCardActive(int slot) => hud.SourceCardActive(slot);
        internal void SourceReadyChanged(int slot)
        {
            hud.SourceResultReady(slot);
            NativeTelemetry.Event("results.player.ready", "applied", detail: "slot=" + slot);
            GetComponent<NativeResultsNaturalProbe>()?.Record("ready-alpha-before-onReady");
        }
        internal void SourceShowScoreboard() => resultsFlow.ShowScoreboard(sourceRaceState.RaceId);
        internal void SourceAllReadyFinish() => resultsFlow.AllReadyFinish(sourceRaceState.RaceId);
        internal void SourceSubmitScoreboard() => resultsFlow.SubmitScoreboard(sourceRaceState.RaceId);
        internal void AbortSourceResults()
        {
            resultsFlow.Abort(); sourceResultsAvatars.Dispose(); hud?.CloseSourceResults();
        }
    }
    public sealed partial class NativeRaceHud
    {
        NativeResultsFlow sourceResults;
        string[] sourceResultNames;
        NativeVehicleRoster sourceResultRoster;
        NativeSessionSnapshot sourceResultSession;
        internal bool UsesSourceResults => sourceResults != null;
        internal void BindSourceResults(NativeResultsFlow flow, string[] names, NativeVehicleRoster roster, NativeSessionSnapshot session)
        {
            sourceResults = flow; sourceResultNames = names; sourceResultRoster = roster; sourceResultSession = session;
            resultPresentationKey = "source:" + flow.RaceId; resultPresentationStarted = Time.time;
            resultScoreboard = false; SetMenuPage(NativeMenuPage.Results);
        }
        internal void OpenSourceResultCards()
        {
            ResultPanel(false);
            authoredUi.SourceResultCards(sourceResults.Rows, sourceResultNames, sourceResultSession.config.racerCount);
        }
        internal float[] SourceReadyAlphas() => authoredUi.SourceReadyAlphas();
        internal bool SourceCardActive(int slot) => authoredUi.SourceResultCardActive(slot);
        internal void SourceResultReady(int slot) => authoredUi.SourceResultReady(slot);
        internal void BindSourceResultAvatar(int slot, Texture texture) => authoredUi.RawTexture(NativeAuthoredUi.SourceResultCardKey(slot) + "/fumo", texture);
        internal void HideSourceResultCards() => authoredUi.SetActive("GlobalHUD/Canvas/Results/playersresult", false);
        internal void OpenSourceScoreboard() { resultScoreboard = true; ResultPanel(true); }
        internal void HideSourceScoreboard() => authoredUi.SetActive("GlobalHUD/Canvas/Results/scoreboard", false);
        internal void CloseSourceResults()
        {
            authoredUi?.SetActive("GlobalHUD/Canvas/Results", false);
            sourceResults = null; resultPresentationKey = null; resultScoreboard = false;
        }
        internal NativeMenuAction DrawSourceResults()
        {
            if (sourceResults == null || sourceResults.State == NativeResultsFlow.Screen.Waiting) return NativeMenuAction.None;
            if (sourceResults.State == NativeResultsFlow.Screen.Cards)
            {
                ResultPanel(false);
                authoredUi.RestoreSourceResultCardVisibility(sourceResults.Rows);
                return NativeMenuAction.None;
            }
            if (sourceResults.State != NativeResultsFlow.Screen.Scoreboard) return NativeMenuAction.None;
            // BeginFrame hides authored pages on every draw, including the live scoreboard.
            ResultPanel(true);
            var rows = sourceResults.Rows;
            authoredUi.Scoreboard(rows.Select(r => ResultCharacterName(sourceResultNames[r.RacerIndex])).ToArray(),
                rows.Select(r => r.HumanIndex >= 0 ? ResultTimeText(r.Seconds) : "").ToArray(),
                rows.Select(r => r.Place).ToArray(), rows.Select(r => r.DisplayedScore).ToArray(),
                rows.Select(r => sourceResultRoster.CharacterIconAt(sourceResultRoster.CharacterIndexOf(sourceResultSession.config.roster[r.RacerIndex].characterId))).ToArray(),
                rows.Select(r => r.HumanIndex >= 0).ToArray(), rows.Select(r => r.DisplayedDelta).ToArray(),
                sourceResultSession.config.racerCount, sourceResults.ScoreboardElapsed);
            authoredUi.SourceScoreboardColors(rows);
            return NativeMenuAction.None;
        }
    }
}
