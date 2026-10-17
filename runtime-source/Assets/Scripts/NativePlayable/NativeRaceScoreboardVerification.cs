using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ScoreboardUiReport
        {
            public bool passed, scriptedState = true, gameplayAcceptance = false;
            public string source = "Explicit GP, Quick, partial station and 24-racer results UI fixtures";
            public string[] checks;
            public int runtimeErrors;
        }
        IEnumerator CheckScoreboardUiReplay()
        {
            var checks = new List<string>();
            Action<bool, string> check = (pass, name) => checks.Add((pass ? "PASS " : "FAIL ") + name);
            Directory.CreateDirectory(reportDirectory);
            foreach (var mode in new[] { NativeSessionMode.GrandPrix, NativeSessionMode.QuickRace, NativeSessionMode.CustomRace })
            {
                if (NativeGameSession.Current != null) NativeGameSession.Cancel(NativeGameSession.Current.sessionId);
                selectedMode = mode;
                if (mode == NativeSessionMode.CustomRace) customRacers = 24;
                Begin();
                var session = NativeGameSession.Current;
                for (int i = 0; i < racers.Length; i++)
                {
                    racers[i].driving = false;
                    racers[i].GetComponent<NativeKartOriginalAI>().active = false;
                    finishPlaces[i] = i == 0 ? racers.Length : i;
                    finishSeconds[i] = 101.497 + i;
                    finishTimes[i] = (float)finishSeconds[i];
                    NativeGameSession.RecordFinish(session.sessionId, session.config.roster[i].seatId, finishSeconds[i], finishPlaces[i]);
                }
                check(NativeGameSession.CompleteStation(session.sessionId), mode + " scripted station installed");
                session = NativeGameSession.Current;
                if (mode == NativeSessionMode.GrandPrix)
                {
                    session.totals.Single(t => t.seatId == session.config.roster[0].seatId).score += 30;
                    typeof(NativeGameSession).GetField("current", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, session);
                }
                int expectedHumanTotal = session.totals.Single(t => t.seatId == session.config.roster[0].seatId).score;
                progressionCommitted = true;
                while (!progress[0].Finished) progress[0].Cross(progress[0].Target, true);
                phase = Phase.Finished;
                FinishCamera.OnEndedRace();
                hud.SetMenuPage(NativeMenuPage.Results);
                yield return null;
                float timeout = Time.realtimeSinceStartup + 5;
                while (!hud.ResultsVisible && Time.realtimeSinceStartup < timeout) yield return null;
                check(hud.ResultsVisible, mode + " rankcard delay completes");
                float scoreboardStarted = Time.time;
                hud.ConfirmResultCard();
                yield return null;
                yield return new WaitForEndOfFrame();
                var root = GameObject.Find("Original authored menus").transform;
                var content = root.Find("GlobalHUD authored Canvas/Results/scoreboard/Scroll View/Viewport/Content");
                var rows = content.Cast<Transform>().Where(t => t.name.StartsWith("Native result entry ") && t.gameObject.activeSelf).ToArray();
                check(rows.Length == racers.Length, mode + " all scoreboard rows exist");
                var human = rows.Single(r => r.Find("time").GetComponent<TMP_Text>().text.Length > 0);
                check(human.Find("rank").GetComponent<TMP_Text>().text == racers.Length.ToString(), mode + " human preserves last finish rank");
                check(rows.All(r => r.Find("name").GetComponent<TMP_Text>().color == Color.white), mode + " every name is white");
                check(rows.Count(r => r.Find("time").GetComponent<TMP_Text>().text.Length > 0) == 1, mode + " human alone displays finish time");
                check(rows.All(r => r.Find("scoreDiff").GetComponent<TMP_Text>().text.StartsWith("+")), mode + " initial points remain in delta");
                check(human.Find("score").GetComponent<TMP_Text>().text == (expectedHumanTotal - 1).ToString(), mode + " human shows prior score");
                check(human.Find("banner").GetComponent<Image>().color == new Color(1, 0, 0, .5f)
                    && human.Find("banner").GetComponent<CanvasGroup>().alpha == .5f, mode + " source player banner color and alpha");
                if (mode == NativeSessionMode.GrandPrix) check(rows[0] == human, "GP cumulative ordering retains displayed finish rank eight");
                CaptureTestScreenshot("scoreboard-ui-" + mode + "-initial.png");
                yield return new WaitForSeconds(1.17f);
                yield return new WaitForEndOfFrame();
                check(rows.Any(r => r.Find("scoreDiff").GetComponent<TMP_Text>().text.Length > 0)
                    && rows.Any(r => r.Find("scoreDiff").GetComponent<TMP_Text>().text.Length == 0), mode + " points transfer progressively after delay");
                CaptureTestScreenshot("scoreboard-ui-" + mode + "-counting.png");
                yield return new WaitForSeconds(mode == NativeSessionMode.CustomRace ? .25f : .5f);
                yield return new WaitForEndOfFrame();
                if (mode != NativeSessionMode.CustomRace) check(rows.All(r => r.Find("scoreDiff").GetComponent<TMP_Text>().text == ""), mode + " point deltas clear on completion");
                check(human.Find("score").GetComponent<TMP_Text>().text == expectedHumanTotal.ToString(), mode + " final human score appears");
                check(NativeGameSession.Current.totals.Single(t => t.seatId == session.config.roster[0].seatId).score == expectedHumanTotal, mode + " UI preserves session totals");
                CaptureTestScreenshot("scoreboard-ui-" + mode + "-final.png");
                if (mode == NativeSessionMode.GrandPrix)
                {
                    var partial = NativeGameSession.Current;
                    var originalTotals = partial.totals.Select(t => t.score).ToArray();
                    partial.stations.Last().results.RemoveAll(r => r.seatId != partial.config.roster[0].seatId);
                    for (int i = 1; i < finishPlaces.Length; i++) finishPlaces[i] = 0;
                    typeof(NativeGameSession).GetField("current", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, partial);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    check(rows.Length == racers.Length && rows.Skip(1).All(r => r.Find("rank").GetComponent<TMP_Text>().text == ""), "partial station renders unfinished AI with empty ranks");
                    check(NativeGameSession.Current.totals.Select(t => t.score).SequenceEqual(originalTotals), "partial station rendering preserves totals");
                    check(errorCount == 0, "partial station rendering raises zero runtime errors");
                    CaptureTestScreenshot("scoreboard-ui-GP-partial.png");
                    NativeGameSession.Cancel(partial.sessionId);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    check(errorCount == 0, "missing session and unfinished places render safely");
                }
                if (mode == NativeSessionMode.CustomRace)
                {
                    var first = rows[0].GetComponent<RectTransform>();
                    float top = first.anchoredPosition.y;
                    yield return new WaitForSeconds(.18f);
                    yield return new WaitForEndOfFrame();
                    check(Time.time - scoreboardStarted < 2 && Mathf.Abs(first.anchoredPosition.y - top) < .1f, "24 rows remain at the top for the source two-second delay");
                    var viewport = content.parent.GetComponent<RectTransform>();
                    check(content.GetComponent<RectTransform>().rect.height > viewport.rect.height && rows.Length == 24, "24-row content exceeds viewport height");
                    CaptureTestScreenshot("scoreboard-ui-24-top.png");
                    yield return new WaitForSeconds(1.4f);
                    yield return new WaitForEndOfFrame();
                    float scrollTime = Time.time - scoreboardStarted - 2;
                    float sourceOffset = Mathf.Max(0, 64.71f * 24 - viewport.rect.height)
                        * (1 - (.5f * Mathf.Cos(2f / 25 * scrollTime * Mathf.PI) + .5f));
                    float displacement = first.anchoredPosition.y - top;
                    check(displacement > 1 && Mathf.Abs(displacement - sourceOffset) < 2, "24-row displacement follows original cosine time and template child count");
                    check(viewport.GetComponent<Mask>() && viewport.GetComponent<Mask>().enabled,
                        "overflowing 24-row content retains authored viewport clipping");
                    CaptureTestScreenshot("scoreboard-ui-24-scrolling.png");
                }
                // Exercise the source consumer through normal OnGUI/BeginFrame, not only the legacy fixture.
                var flow = new NativeResultsFlow();
                flow.Begin(123, mode, Enumerable.Range(0, racers.Length).Select(i =>
                    new NativeResultsRowSnapshot(i, i == 0 ? 0 : -1, finishPlaces[i],
                        session.totals.Single(t => t.seatId == session.config.roster[i].seatId).score, 0, finishSeconds[i])), session.stationIndex, session.config.trackIds.Count, .05f);
                flow.Tick(3, 1, true, 123);
                hud.BindSourceResults(flow, racerNames, vehicleRoster, session);
                hud.OpenSourceResultCards();
                check(flow.ShowScoreboard(123), mode + " source cards advance to scoreboard");
                hud.OpenSourceScoreboard();
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    var board = root.Find("GlobalHUD authored Canvas/Results/scoreboard");
                    check(board.gameObject.activeInHierarchy && board.GetComponentsInChildren<TMP_Text>()
                        .Any(t => t.name == "score" && t.text.Length > 0), mode + " source scoreboard remains rendered after BeginFrame " + frame);
                }
                CaptureTestScreenshot("scoreboard-source-" + mode + ".png");
                hud.CloseSourceResults();
                ClearResultsPreview();
                phase = Phase.Menu;
                hud.SetMenuPage(NativeMenuPage.Main);
                yield return null;
            }
            check(errorCount == 0, "runtime errors zero");
            var report = new ScoreboardUiReport { runtimeErrors = errorCount, checks = checks.ToArray(), passed = checks.All(c => c.StartsWith("PASS ")) };
            File.WriteAllText(Path.Combine(reportDirectory, "scoreboard-ui-report.json"), JsonUtility.ToJson(report, true) + "\n");
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
