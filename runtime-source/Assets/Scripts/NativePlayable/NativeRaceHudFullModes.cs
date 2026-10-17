using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public void EnableOnlineMenu()
        {
            Navigation.MainDisabled = new bool[Navigation.MainActions.Length];
        }
        int resultsFirst;
        public void ScrollFullResults(int delta, int racerCount) { resultsFirst = Mathf.Clamp(resultsFirst + delta * 8, 0, Mathf.Max(0, racerCount - 8)); }
        void ConfigureResults(NativeMenuAction[] actions)
        {
            Navigation.ResultActions = actions;
            SetMenuPage(NativeMenuPage.Results);
            if (SelectedMenuIndex >= actions.Length) Navigation.Select(0);
        }
        string resultPresentationKey;
        bool resultScoreboard;
        float resultPresentationStarted;
        float resultScoreboardStarted;
        float resultScoreboardClock;
        int resultScoreboardTicks, resultScoreboardFrame;
        public bool ResultsVisible => sourceResults != null
            ? sourceResults.State == NativeResultsFlow.Screen.Cards || sourceResults.State == NativeResultsFlow.Screen.Scoreboard
            : resultPresentationKey != null && Time.time - resultPresentationStarted >= 3;
        public bool ResultsScoreboardVisible => sourceResults != null ? sourceResults.State == NativeResultsFlow.Screen.Scoreboard : resultScoreboard;
        public void ConfirmResultCard() { if (!resultScoreboard) { resultScoreboardStarted = Time.time; resultScoreboardClock = -1; resultScoreboardTicks = 0; resultScoreboardFrame = Time.frameCount; } resultScoreboard = true; }
        void BeginResultPresentation(string key)
        {
            if (MenuPage != NativeMenuPage.Results || resultPresentationKey != key)
            {
                resultPresentationKey = key;
                resultPresentationStarted = Time.time;
                resultScoreboard = false;
                resultsFirst = 0;
            }
            ConfigureResults(new[] { NativeMenuAction.None });
        }
        void ResultPanel(bool scoreboard)
        {
            authoredUi.Show("GlobalHUD", "Results", "/prompt");
            authoredUi.SetActive("GlobalHUD/Canvas/Results/scoreboard", scoreboard);
            authoredUi.SetActive("GlobalHUD/Canvas/Results/playersresult", !scoreboard);
            authoredUi.SetText("GlobalHUD/Canvas/Results/Prompt", NativeLocalization.Get("RESULTS_PROMPT_CONTINUE", "Next"));
            authoredUi.ResultBackdrop();
        }
        public void ResultCharacterTexture(Texture texture) => authoredUi.RawTexture("GlobalHUD/Canvas/Results/playersresult/ResultPlayerUI/fumo", texture);
        public NativeMenuAction TimeTrialResults(float width, double seconds, IList<double> laps, NativeSaveData.TimeTrialRecord best, string notice, string character = "")
        {
            BeginResultPresentation("tt:" + seconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (!ResultsVisible) { authoredUi.Hide(); return NativeMenuAction.None; }
            ResultPanel(false);
            authoredUi.ResultPlayer(ResultCharacterName(character), 1, ResultTimeText(seconds), 1);
            return NativeMenuAction.None;
        }
        static string ResultCharacterName(string name)
        {
            const string humanSuffix = " / YOU";
            string character = name != null && name.EndsWith(humanSuffix, StringComparison.Ordinal) ? name.Substring(0, name.Length - humanSuffix.Length) : name;
            return string.IsNullOrEmpty(character) ? character : NativeLocalization.Get("CHAR_" + character.ToUpperInvariant(), character);
        }
        static string ResultTimeText(double seconds)
        {
            if (seconds <= 0 || double.IsInfinity(seconds) || double.IsNaN(seconds)) return "<mspace=18>00:00:00.000</mspace>";
            var time = TimeSpan.FromSeconds(seconds);
            return "<mspace=18>" + time.Hours.ToString("00") + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00") + "." + time.Milliseconds.ToString("000") + "</mspace>";
        }
        public NativeMenuAction FullRaceResults(float width, double[] seconds, string[] names, NativeSessionSnapshot session, string notice, bool grandPrix, int[] places, NativeVehicleRoster roster = null)
        {
            BeginResultPresentation((grandPrix ? "gp:" : "quick:") + (session == null ? "" : session.sessionId + ":" + session.stationIndex));
            activeFont = menuFont;
            if (!ResultsVisible) { authoredUi.Hide(); return NativeMenuAction.None; }
            ResultPanel(resultScoreboard);
            if (!resultScoreboard)
            {
                authoredUi.ResultPlayer(ResultCharacterName(names[0]), places[0], ResultTimeText(seconds[0]), seconds.Length);
                return NativeMenuAction.None;
            }
            var station = session == null ? null : session.stations.LastOrDefault(s => s.index == session.stationIndex);
            var gained = Enumerable.Range(0, seconds.Length).Select(i => session == null
                ? (places[i] > 0 && places[i] <= seconds.Length ? NativeModeCatalog.ScoreForPlace(places[i], seconds.Length) : 0)
                : (station == null ? session.pending : station.results)
                    .FirstOrDefault(r => r.seatId == session.config.roster[i].seatId)?.score ?? 0).ToArray();
            var finalScores = Enumerable.Range(0, seconds.Length).Select(i => session == null ? gained[i]
                : (session.totals.FirstOrDefault(t => t.seatId == session.config.roster[i].seatId)?.score ?? 0)
                    + (station == null ? gained[i] : 0)).ToArray();
            var order = Enumerable.Range(0, seconds.Length).OrderByDescending(i => finalScores[i])
                .ThenBy(i => places[i] > 0 ? places[i] : int.MaxValue).ToArray();
            float elapsed = Time.time - resultScoreboardStarted;
            if (resultScoreboardFrame != Time.frameCount)
            {
                resultScoreboardFrame = Time.frameCount;
                resultScoreboardClock += Time.deltaTime;
                if (resultScoreboardClock >= .05f)
                {
                    resultScoreboardClock -= .05f;
                    resultScoreboardTicks++;
                }
            }
            var remaining = gained.Select(score => resultScoreboardTicks >= score ? 0
                : Mathf.Max(0, score - Mathf.Max(0, resultScoreboardTicks - 1))).ToArray();
            var displayed = finalScores.Select((score, i) => score - remaining[i]).ToArray();
            authoredUi.Scoreboard(order.Select(i => ResultCharacterName(names[i])).ToArray(),
                order.Select(i => (session == null ? i == 0 : session.config.roster[i].isHuman) ? ResultTimeText(seconds[i]) : "").ToArray(),
                order.Select(i => places[i]).ToArray(), order.Select(i => displayed[i]).ToArray(),
                roster == null || session == null ? null : order.Select(i => roster.CharacterIconAt(roster.CharacterIndexOf(session.config.roster[i].characterId))).ToArray(),
                order.Select(i => session == null ? i == 0 : session.config.roster[i].isHuman).ToArray(),
                order.Select(i => remaining[i]).ToArray(), seconds.Length, elapsed);
            return NativeMenuAction.None;
        }
        public NativeMenuAction CeremonyMenu(float width, NativeSessionSnapshot session, NativeVehicleRoster roster, string notice, NativeCeremonyView playback = null)
        {
            SetMenuPage(NativeMenuPage.Ceremony); activeFont = menuFont;
            var sorted = NativeModeCatalog.SortScoreboard(session.totals, true);
            int humanPlace = sorted.FindIndex(r => session.config.roster.Find(s => s.seatId == r.seatId).isHuman);
            string phrase = humanPlace >= 0 && humanPlace < 3 ? "GP_" + (humanPlace + 1) + "PLACE" : "GP_LOSE";
            const string complete = "ceremony/ceremony/Canvas/CompleteText";
            const string player = "ceremony/ceremony/Canvas/PlayerText";
            authoredUi.ShowPages(complete, player);
            authoredUi.SetText(complete, NativeLocalization.Get(phrase));
            authoredUi.SetActive(complete, playback == null || playback.CompleteTitleVisible);
            authoredUi.SetActive(player, playback == null || playback.PlayerResultVisible);
            var definition = authoredUi.Definition(complete);
            // Timeline records absolute anchored Y; Offset accepts downward HUD units.
            if (playback != null) authoredUi.Offset(complete, new Vector2(0, (definition.position.y - playback.CompleteTextY) / 1.8f));
            string playerName = "";
            if (humanPlace >= 0 && humanPlace <= 3)
            {
                var seat = session.config.roster.Find(s => s.seatId == sorted[humanPlace].seatId);
                int humanSlot = session.config.roster.TakeWhile(s => s.seatId != seat.seatId).Count(s => s.isHuman);
                playerName = NativeLocalization.Get("SYSTEM_P" + (humanSlot + 1));
                authoredUi.TextColor(player, NativeAuthoredUi.SourceHumanColor(humanSlot));
            }
            authoredUi.SetText(player, playerName);
            foreach (var text in UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshProUGUI>(true))
            {
                var originalCanvas = text.GetComponentInParent<Canvas>(true);
                if (!originalCanvas || originalCanvas.name != "ceremony/ceremony authored Canvas") continue;
                text.fontStyle = TMPro.FontStyles.Bold;
                if (text.name == "CompleteText")
                {
                    text.enableVertexGradient = true;
                    text.colorGradient = new TMPro.VertexGradient { topLeft = Color.white, topRight = Color.white,
                        bottomLeft = new Color(1, .85634255f, .6367924f), bottomRight = new Color(1, .85634255f, .6367924f) };
                }
            }
            if (playback != null && playback.FadeAlpha > 0) Fill(new Rect(0, 0, width, 600), new Color(0, 0, 0, playback.FadeAlpha));
            return NativeMenuAction.None;
        }
    }
}
