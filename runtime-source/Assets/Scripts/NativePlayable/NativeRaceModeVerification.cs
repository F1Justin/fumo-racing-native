using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ModeLiveReport
        {
            public bool passed;
            public string mode, track, source = "Unity macOS Player, live scaled clock and CharacterController trigger events";
            public string[] checks;
            public double seconds;
            public double[] laps;
            public int[] resets, gateExits;
            public NativeSessionSnapshot session;
            public NativeSaveData save;
            public ModeFrame[] frames;
            public float maximumDelta;
            public NativeSessionSnapshot[] customConfigurations;
        }
        [Serializable] sealed class ModeFrame { public double seconds; public float delta, realtime; public Vector3 position; public int checkpoint, laps; }
        readonly List<NativeSessionSnapshot> customConfigurations = new List<NativeSessionSnapshot>();
        readonly List<ModeFrame> modeFrames = new List<ModeFrame>();
        float maximumModeDelta;
        void RecordModeFrame()
        {
            if (!verificationRun || !autopilot || selfTest || phase != Phase.Racing) return;
            maximumModeDelta = Mathf.Max(maximumModeDelta, Time.deltaTime);
            if (Time.frameCount % 60 == 0) modeFrames.Add(new ModeFrame { seconds = PreciseElapsed, delta = Time.deltaTime, realtime = Time.realtimeSinceStartup,
                position = racers[0].transform.position, checkpoint = progress[0].CurrentTriggerIndex, laps = progress[0].CompletedLaps });
        }
        readonly List<string> modeChecks = new List<string>();
        bool modePassed = true;
        void ModeCheck(bool pass, string name)
        { modeChecks.Add((pass ? "PASS " : "FAIL ") + name); modePassed &= pass; Debug.Log("NATIVE_MODE_CHECK " + pass + " " + name); }
        void FinishModeReport(string filename)
        {
            ModeCheck(errorCount == 0, "runtime errors=" + errorCount);
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory, filename), JsonUtility.ToJson(new ModeLiveReport {
                passed = modePassed, mode = selectedMode.ToString(), track = trackId, checks = modeChecks.ToArray(),
                seconds = finishSeconds == null || finishSeconds.Length == 0 ? PreciseElapsed : finishSeconds[0],
                laps = lapSeconds == null || lapSeconds.Length == 0 ? new double[0] : lapSeconds[0].ToArray(),
                resets = racers.Select(r => r.Resets).ToArray(), gateExits = progress.Select(p => p.ExitEvents).ToArray(),
                session = NativeGameSession.Current, save = NativeSaveStore.Data, frames = modeFrames.ToArray(), maximumDelta = maximumModeDelta, customConfigurations = customConfigurations.ToArray() }, true) + "\n");
            Debug.Log("NATIVE_MODE_RESULT " + filename + " passed=" + modePassed);
            Application.Quit(modePassed ? 0 : 2);
        }
        internal void EnsureInputVerificationOwner(UnityEngine.InputSystem.InputDevice device) { EnsureInjectedDiagnosticOwner(device); }
        internal void BeginInputVerificationRace() { selectedMode = NativeSessionMode.TimeTrials; Begin(); }
        internal void ResetInputVerificationMenu() { ResetRace(false); }
        IEnumerator CheckInputProfilesLive()
        {
            var verification = NativeInputLiveVerification.Run(this, reportDirectory);
            while (true)
            {
                bool next; object instruction = null;
                try { next = verification.MoveNext(); if (next) instruction = verification.Current; }
                catch (Exception error) { ModeCheck(false, "input runtime: " + error.Message); break; }
                if (!next) break;
                yield return instruction;
            }
            ModeCheck(NativeInputLiveVerification.LastResult != null && NativeInputLiveVerification.LastResult.passed, "actual input system keyboard/gamepad/rebind");
            FinishModeReport("input-profile-report.json");
        }
        IEnumerator CheckProgressionRules()
        {
            string savePath = NativeSaveStore.FilePath;
            yield return NativeProgressionVerification.StartVerification(reportDirectory);
            ModeCheck(NativeProgressionVerification.Passed, "progression disk rules");
            ModeCheck(NativeModeVerification.Run(reportDirectory), "mode defaults, score and tie rules");
            double now = 1e8 + .000123;
            var clock = new NativeRaceClock(() => now); clock.Start(); now += .123456789;
            ModeCheck(Math.Abs(clock.Elapsed - .123456789) < 1e-8, "double clock subtraction");
            clock.StartAt(now + 3); now += 3.037;
            ModeCheck(Math.Abs(clock.Elapsed - .037) < 1e-8, "countdown overshoot retained");
            clock.Reset(); ModeCheck(clock.Elapsed == 0 && !clock.Running, "clock reset");
            var circuit = new NativeCircuitProgress(new[] { Vector3.zero, Vector3.forward * 10, Vector3.right * 10 }, 5);
            circuit.Cross(0, true);
            for (int lap = 1; lap <= 5; lap++)
            {
                circuit.Cross(1, true); circuit.Cross(2, true); circuit.Cross(0, true);
                ModeCheck(circuit.CompletedLaps == lap && circuit.Finished == (lap == 5), "five-lap finish boundary " + lap);
            }
            NativeSaveStore.ResetForVerification();
            NativeSaveStore.Initialize(new[] { "--save-directory", Path.GetDirectoryName(savePath) });
            FinishModeReport("progression-live-report.json");
        }
        IEnumerator CheckTimeTrialsLive()
        {
            selectedMode = NativeSessionMode.TimeTrials;
            ExecuteMenuAction(NativeMenuAction.TimeTrials);
            ModeCheck(hud.MenuPage == NativeMenuPage.Characters, "TT Character entry");
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Characters));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Vehicles));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Skins));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Cups));
            hud.SelectMenuIndex(NativeTrackCatalog.IndexOf(trackId)); ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Tracks));
            ModeCheck(racers.Length == 1 && RaceLaps == 5 && countdown == 3, "TT one racer, five laps, three-second countdown");
            ModeCheck(!racers[0].UseFixedVerificationClock && Time.captureDeltaTime == 0 && Time.timeScale == 1, "TT ordinary frame clock and unscaled playback rate");
            ModeCheck(FindObjectsOfType<NativeKartItemBox>(true).All(b => b.isFixed || !b.gameObject.activeSelf), "TT random boxes disabled");
            ModeCheck(FindObjectsOfType<NativeKartItemBox>(true).Where(b => b.isFixed).All(b => b.gameObject.activeSelf), "TT fixed boxes retained");
            float timeout = Time.realtimeSinceStartup + 10;
            while (phase != Phase.Racing && Time.realtimeSinceStartup < timeout) yield return null;
            ModeCheck(phase == Phase.Racing, "live countdown starts clock");
            yield return new WaitForSeconds(.5f);
            Pause(); double frozen = PreciseElapsed;
            yield return new WaitForSecondsRealtime(.3f);
            ModeCheck(PreciseElapsed == frozen, "pause freezes scaled double clock"); Resume();
            yield return new WaitForSeconds(.1f); ModeCheck(PreciseElapsed > frozen, "resume continues clock");
            Begin(); ModeCheck(PreciseElapsed == 0 && lapSeconds[0].Count == 0 && progress[0].CompletedLaps == 0, "restart clears laps and clock");
            timeout = Time.realtimeSinceStartup + 900;
            while (!progress[0].Finished && Time.realtimeSinceStartup < timeout)
            {
                File.WriteAllText(Path.Combine(reportDirectory, "time-trials-live.json"), JsonUtility.ToJson(new ModeLiveReport {
                    track = trackId, mode = "TimeTrials", seconds = PreciseElapsed, laps = lapSeconds[0].ToArray(),
                    gateExits = new[] { progress[0].ExitEvents }, resets = new[] { racers[0].Resets } }));
                yield return new WaitForSecondsRealtime(2);
            }
            yield return null;
            ModeCheck(progress[0].Finished && lapSeconds[0].Count == 5, "five actual laps finish");
            // The original counter fills forward skipped gates, so collider exits can differ from nominal lap counts.
            ModeCheck(progress[0].ExitEvents >= 5 * (gates.Length - 1) && racers[0].Distance > 5000, "live route and collider gates");
            ModeCheck(Math.Abs(lapSeconds[0].Sum() - finishSeconds[0]) < 1e-8, "lap sum equals precise total");
            var record = NativeProgression.GetTimeTrial(trackId);
            ModeCheck(record != null && record.bestSeconds == finishSeconds[0] && record.lapSeconds.Length == 5, "actual finish saved with five double laps");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("time-trials-results.png");
            long generation = NativeSaveStore.Data.generation;
            if (record != null) ModeCheck(!NativeProgression.RecordTimeTrial(trackId, record.characterId, record.vehicleId, record.bestSeconds, record.lapSeconds).recordChanged
                && NativeSaveStore.Data.generation == generation, "equal finish preserves record and generation");
            string saveDirectory = Path.GetDirectoryName(NativeSaveStore.FilePath); double expected = finishSeconds[0];
            NativeSaveStore.ResetForVerification(); NativeSaveStore.Initialize(new[] { "--save-directory", saveDirectory });
            ModeCheck(NativeProgression.GetTimeTrial(trackId) != null && NativeProgression.GetTimeTrial(trackId).bestSeconds == expected, "disk reload retains exact record");
            FinishModeReport("time-trials-report.json");
        }
        void OpenCustomThroughMainMenu()
        {
            hud.SetMenuPage(NativeMenuPage.Main);
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Main));
            hud.SelectMenuIndex(2);
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.RaceModes));
            ModeCheck(hud.MenuPage == NativeMenuPage.Characters, "Custom Character entry");
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Characters));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Vehicles));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Skins));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Cups));
            hud.SelectMenuIndex(NativeTrackCatalog.IndexOf(trackId));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Tracks));
        }
        void ConfirmCustomRow(int row)
        {
            hud.SelectMenuIndex(row);
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Custom));
        }
        void StartCustomThroughMenus()
        {
            ModeCheck(hud.MenuPage == NativeMenuPage.Custom && selectedMode == NativeSessionMode.CustomRace, "Custom configuration follows Circuit");
            ConfirmCustomRow(0);
        }
        void CheckCustomBoxes(int itemType)
        {
            var ordinary = FindObjectsOfType<NativeKartItemBox>(true);
            ModeCheck(ordinary.Any(b => !b.isFixed), "scene contains original random boxes");
            ModeCheck(ordinary.Where(b => !b.isFixed).All(b => b.gameObject.activeSelf == (itemType == 0)), "ordinary random box activation itemType=" + itemType);
            ModeCheck(ordinary.Where(b => b.isFixed).All(b => b.gameObject.activeSelf), "fixed boxes retained itemType=" + itemType);
            var manager = GetComponent<NativeUfoManager>();
            ModeCheck(manager && manager.ItemType == itemType && manager.Players.Length == racers.Length, "real UFO manager roster and itemType=" + itemType);
            if (manager) ModeCheck(itemType == 1 ? manager.Boxes.Length > 0 && manager.Boxes.All(b => b.gameObject.activeSelf)
                : manager.Boxes.Length == 0, "replacement UFO boxes itemType=" + itemType);
        }
        IEnumerator CheckCustomRulesLive()
        {
            hud.SuppressPointerInput = true; autopilot = false;
            OpenCustomThroughMainMenu();
            ModeCheck(hud.CustomControlCount == 11 && hud.SelectedMenuIndex == 0, "original eleven rows begin with Continue");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("custom-rules-default.png");
            ConfirmCustomRow(3); customDropdownOption = 2; CloseCustomDropdown();
            ModeCheck(customItemType == 0 && hud.MenuPage == NativeMenuPage.Custom, "item dropdown cancel retains DEFAULT and Custom page");
            ConfirmCustomRow(4); ConfirmCustomDropdown(1);
            ModeCheck(customRules.itemRespawn == 1 && !customDropdownOpen, "respawn dropdown confirmation commits Instant");
            for (int row = 5; row <= 10; row++)
            {
                SetCustomValue(row, -1000); ModeCheck(CustomValue(row) == NativeCustomRaceRules.Minimum(row), "original slider minimum " + NativeCustomRaceRules.Rows[row]);
                SetCustomValue(row, 1000); ModeCheck(CustomValue(row) == NativeCustomRaceRules.Maximum(row), "original slider maximum " + NativeCustomRaceRules.Rows[row]);
            }
            SetCustomValue(1, 3); SetCustomValue(2, 3);
            SetCustomValue(5, 150); SetCustomValue(6, 200); SetCustomValue(7, 50); SetCustomValue(8, 120); SetCustomValue(9, 80); SetCustomValue(10, 125);
            hud.SelectMenuIndex(10); yield return new WaitForEndOfFrame(); CaptureTestScreenshot("custom-rules-modified.png");
            var originalRespawns = FindObjectsOfType<NativeKartItemBox>(true).ToDictionary(b => b, b => b.respawnTime);
            StartCustomThroughMenus();
            var originals = racers.Select((motor, i) => vehicleRoster.vehicles[vehicleRoster.VehicleIndexOf(raceConfig.roster[i].vehicleId)].settings).ToArray();
            for (int i = 0; i < racers.Length; i++)
            {
                var actual = racers[i].stats; var original = originals[i];
                ModeCheck(!ReferenceEquals(actual, original) && Mathf.Approximately(actual._topSpeed, original._topSpeed * 1.5f)
                    && Mathf.Approximately(actual._topSpeedGrass, original._topSpeedGrass * 1.5f) && Mathf.Approximately(actual._topSpeedCap, original._topSpeedCap * 1.5f), "seat " + i + " independent three speed fields");
                ModeCheck(Mathf.Approximately(actual._acceleration, original._acceleration * 4) && Mathf.Approximately(actual._friction, original._friction * .5f)
                    && Mathf.Approximately(actual._frictionGrass, original._frictionGrass * .5f), "seat " + i + " squared acceleration and both friction fields");
                ModeCheck(Mathf.Approximately(actual._turnForce, original._turnForce * 1.2f) && Mathf.Approximately(actual._turnMax, original._turnMax * 1.2f)
                    && Mathf.Approximately(actual._turnFriction, original._turnFriction * 1.2f) && Mathf.Approximately(actual._gravity, original._gravity * .8f)
                    && Mathf.Approximately(actual._jumpForce, original._jumpForce * 1.25f), "seat " + i + " steering gravity and jump consumers");
            }
            var boxes = FindObjectsOfType<NativeKartItemBox>(true);
            ModeCheck(boxes.Where(b => !b.isFixed).All(b => b.respawnTime == 0), "Instant reaches every random box");
            ModeCheck(boxes.Where(b => b.isFixed).All(b => b.respawnTime == originalRespawns[b]), "fixed boxes retain original timing");
            var beforeRestart = racers.Select(r => r.stats._acceleration).ToArray();
            ResetRace(true);
            ModeCheck(racers.Select((r, i) => Mathf.Approximately(r.stats._acceleration, beforeRestart[i])).All(v => v), "restart preserves modifiers without accumulating");
            customConfigurations.Add(NativeGameSession.Current);
            ResetRace(false); customItemType = 1; Begin();
            var ufo = GetComponent<NativeUfoManager>();
            ModeCheck(ufo.Boxes.Length > 0 && ufo.Boxes.All(b => b.respawnTime == 0), "Instant reaches every UFO box");
            ResetRace(false); customRules.itemRespawn = 0; Begin();
            ModeCheck(ufo.Boxes.All(b => b.respawnTime < 0), "Normal restores catalog UFO respawn");
            ModeCheck(originalRespawns.All(pair => pair.Key.respawnTime == pair.Value), "Normal restores original random and fixed box respawn");
            yield return null;
            FinishModeReport("custom-rules-report.json");
        }
        IEnumerator CheckCustomRaceLive()
        {
            autopilot = true;
            OpenCustomThroughMainMenu();
            ModeCheck(hud.MenuPage == NativeMenuPage.Custom && customLaps == 5 && customRacers == 8 && customItemType == 0, "actual Custom menu defaults 5/8/DEFAULT");
            for (int i = 0; i < 110; i++) { AdjustCustom(1, -1); AdjustCustom(2, -1); }
            ModeCheck(customLaps == 1 && customRacers == 1, "Custom lower UI boundaries 1/1");
            for (int i = 0; i < 110; i++) { AdjustCustom(1, 1); AdjustCustom(2, 1); }
            ModeCheck(customLaps == 100 && customRacers == 100, "Custom upper UI boundaries 100/100");
            ExecuteMenuAction(hud.CancelMenu(NativeMenuPage.Custom));
            ModeCheck(hud.MenuPage == NativeMenuPage.Cups && NativeGameSession.Current == null, "Customization cancel returns to Cup before race");
            for (int type = 0; type < 3; type++)
            {
                OpenCustomThroughMainMenu();
                if (type > 0) { ConfirmCustomRow(3); ConfirmCustomDropdown(type); }
                ModeCheck(customItemType == type, "Custom UI item choice " + new[] { "DEFAULT", "UFO", "NONE" }[type]);
                StartCustomThroughMenus();
                ModeCheck(racers.Length == 100 && RaceLaps == 100 && raceConfig.roster.Count == 100, "actual hundred actor roster and hundred lap configuration itemType=" + type);
                ModeCheck(racers.All(r => r.gameObject.activeInHierarchy) && racers.Distinct().Count() == 100
                    && raceConfig.roster.Select(r => r.seatId).Distinct().Count() == 100, "hundred distinct active scene actors and seats");
                ModeCheck(raceConfig.roster.Count(r => r.isHuman) == 1 && raceConfig.roster.Skip(1).All(r => r.vehicleId != "shoppingcart"), "hundred roster one human and original AI vehicle exclusion");
                CheckCustomBoxes(type);
                customConfigurations.Add(JsonUtility.FromJson<NativeSessionSnapshot>(JsonUtility.ToJson(NativeGameSession.Current)));
                yield return new WaitForEndOfFrame(); CaptureTestScreenshot("custom-100-" + type + ".png");
                Pause(); hud.SelectMenuIndex(2);
                ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Pause));
                yield return null;
                ModeCheck(phase == Phase.Menu && hud.MenuPage == NativeMenuPage.Main && NativeGameSession.Current == null, "hundred roster cancellation clears session");
                ModeCheck(rosterPool.Skip(1).All(r => !r.gameObject.activeSelf && !r.driving), "hundred actor menu cleanup disables background AI");
            }
            OpenCustomThroughMainMenu();
            ConfirmCustomRow(3); ConfirmCustomDropdown(0); ModeCheck(customItemType == 0, "Custom item choice wraps NONE to DEFAULT");
            AdjustCustom(3, -1); ModeCheck(customItemType == 2, "Custom item left wraps DEFAULT to NONE");
            for (int i = 0; i < 110; i++) { AdjustCustom(1, -1); AdjustCustom(2, -1); }
            StartCustomThroughMenus();
            ModeCheck(racers.Length == 1 && RaceLaps == 1 && raceConfig.itemType == "none", "actual one actor one lap NONE race");
            CheckCustomBoxes(2);
            float timeout = Time.realtimeSinceStartup + 900;
            while (!progress[0].Finished && Time.realtimeSinceStartup < timeout) yield return new WaitForSecondsRealtime(1);
            yield return null;
            ModeCheck(progress[0].Finished && progress[0].CompletedLaps == 1 && lapSeconds[0].Count == 1
                && finishSeconds[0] > 0 && progress[0].ExitEvents >= gates.Length + 1, "actual Custom controller completes one lap through scene gates");
            ModeCheck(NativeGameSession.Current.state == NativeSessionState.Completed && NativeGameSession.Current.pending.Count == 1, "single racer Custom session completes");
            customConfigurations.Add(JsonUtility.FromJson<NativeSessionSnapshot>(JsonUtility.ToJson(NativeGameSession.Current)));
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("custom-one-none-results.png");
            hud.SelectMenuIndex(1); ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Results));
            ModeCheck(NativeGameSession.Current == null && hud.MenuPage == NativeMenuPage.Main, "finished Custom returns to clean main");
            FinishModeReport("custom-race-report.json");
        }
        IEnumerator CheckQuickRaceLive()
        {
            selectedMode = NativeSessionMode.QuickRace; Begin();
            yield return RunLiveStation();
            ModeCheck(NativeGameSession.Current.state == NativeSessionState.Completed, "quick race completes one station");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("quick-race-results.png");
            FinishModeReport("quick-race-report.json");
        }
        IEnumerator RunLiveStation()
        {
            ModeCheck(racers.Length == 8 && RaceLaps == 5 && !racers.Any(r => r.UseFixedVerificationClock) && Time.captureDeltaTime == 0 && Time.timeScale == 1, "eight racers five laps ordinary frame clock");
            ModeCheck(raceConfig.roster.Skip(1).All(s => s.vehicleId != "shoppingcart"), "AI excluded vehicle fallback");
            float timeout = Time.realtimeSinceStartup + 1200;
            while (!progress.All(p => p.Finished) && Time.realtimeSinceStartup < timeout)
            {
                File.WriteAllText(Path.Combine(reportDirectory, "station-live.json"), JsonUtility.ToJson(new ModeLiveReport {
                    track = trackId, mode = selectedMode.ToString(), seconds = PreciseElapsed, laps = lapSeconds[0].ToArray(),
                    gateExits = progress.Select(p => p.ExitEvents).ToArray(), resets = racers.Select(r => r.Resets).ToArray(),
                    session = NativeGameSession.Current }));
                yield return new WaitForSecondsRealtime(3);
            }
            yield return null;
            ModeCheck(progress.All(p => p.Finished && p.CompletedLaps == 5), "eight actual five-lap finishes");
            ModeCheck(lapSeconds.All(laps => laps.Count == 5) && finishSeconds.All(t => t > 0), "all double lap times captured");
            ModeCheck(NativeGameSession.Current.pending.Count == 8, "all eight actual finishes in session");
        }
        IEnumerator CheckGrandPrixLive()
        {
            if (NativeGameSession.Current == null)
            {
                selectedMode = NativeSessionMode.GrandPrix; selectedTrackId = NativeModeCatalog.CupTrackIds[0];
                Begin();
            }
            yield return RunLiveStation();
            if (!modePassed || errorCount > 0) { FinishModeReport("grand-prix-report.json"); yield break; }
            var session = NativeGameSession.Current;
            int station = session.stationIndex;
            ModeCheck(trackId == NativeModeCatalog.CupTrackIds[station] && session.stations[station].trackId == trackId, "actual shipped cup station order " + station);
            ModeCheck(session.stations.Count == station + 1, "station history committed once");
            ModeCheck(session.pending.Select(r => r.score).OrderBy(v => v).SequenceEqual(new[] { 1, 2, 3, 4, 5, 7, 9, 11 }), "actual station 11/9/7/5/4/3/2/1 points");
            ModeCheck(session.totals.Sum(r => r.score) == 42 * (station + 1), "cumulative total points");
            foreach (var seat in session.config.roster)
                ModeCheck(session.totals.Single(r => r.seatId == seat.seatId).score == session.stations.Sum(s => s.results.Single(r => r.seatId == seat.seatId).score), "cross-station score " + seat.seatId);
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("grand-prix-station-" + station + ".png");
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory, "grand-prix-station-" + station + ".json"), JsonUtility.ToJson(new ModeLiveReport {
                passed = modePassed && errorCount == 0, track = trackId, checks = modeChecks.ToArray(), seconds = finishSeconds[0],
                laps = lapSeconds[0].ToArray(), resets = racers.Select(r => r.Resets).ToArray(), gateExits = progress.Select(p => p.ExitEvents).ToArray(),
                session = session, frames = modeFrames.ToArray(), maximumDelta = maximumModeDelta, customConfigurations = customConfigurations.ToArray() }, true));
            if (!modePassed || errorCount > 0) { FinishModeReport("grand-prix-report.json"); yield break; }
            if (station < 3) { ExecuteMenuAction(NativeMenuAction.Continue); yield break; }
            ExecuteMenuAction(NativeMenuAction.Continue); yield return null;
            ModeCheck(ceremony && ceremonyRoot != null && session.state == NativeSessionState.Completed, "final ceremony podium");
            var cup = NativeProgression.GetCup(NativeModeCatalog.CupId);
            ModeCheck(cup != null && cup.stationSeconds.Length == 4 && Math.Abs(cup.bestSeconds - session.stations.Sum(s => s.results.Single(r => r.seatId == "seat-0").time)) < 1e-8, "four actual station times saved as cup record");
            yield return new WaitForSecondsRealtime(.25f);
            ModeCheck(FindObjectsOfType<TMPro.TMP_Text>().Any(text => text.gameObject.activeInHierarchy && text.text == NativeModeCatalog.CupName.ToUpperInvariant())
                && !FindObjectsOfType<TMPro.TMP_Text>().Any(text => text.gameObject.activeInHierarchy && text.text.Contains("4/4")), "actual ceremony UI replaces station scoreboard");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("grand-prix-ceremony.png");
            yield return new WaitForSecondsRealtime(.25f);
            ExecuteMenuAction(NativeMenuAction.MainMenu);
            ModeCheck(NativeGameSession.Current == null && hud.MenuPage == NativeMenuPage.Main && !ceremony, "ceremony returns to clean main menu");
            FinishModeReport("grand-prix-report.json");
        }
        [Serializable] sealed class CeremonyReplayReport
        {
            public bool passed;
            public string source = "Unity macOS Player; ceremony replay of a previously completed live cup", sourceFile;
            public string[] checks;
            public NativeSessionSnapshot session;
        }
        IEnumerator CheckCeremonyReplay(string[] args)
        {
            QualitySettings.vSyncCount = 0;
            string sourceFile = Argument(args, "--ceremony-replay-file");
            var fixture = NativeSaveJson.FromJson<ModeLiveReport>(File.ReadAllText(sourceFile));
            ModeCheck(fixture.passed && fixture.session.state == NativeSessionState.Completed && fixture.session.stations.Count == 4,
                "completed live cup fixture");
            if (!modePassed) { FinishModeReport("ceremony-replay-report.json"); yield break; }
            string id = NativeGameSession.Create(fixture.session.config);
            foreach (var station in fixture.session.stations.OrderBy(value => value.index))
            {
                ModeCheck(NativeGameSession.Begin(id, station.trackId), "replay station " + station.index);
                foreach (var result in station.results.OrderBy(value => value.place))
                    ModeCheck(NativeGameSession.RecordFinish(id, result.seatId, result.time, result.place), "replay finish " + station.index + " " + result.seatId);
                ModeCheck(NativeGameSession.CompleteStation(id), "replay station score " + station.index);
                if (station.index < 3) ModeCheck(NativeGameSession.Advance(id), "replay next station");
            }
            var session = NativeGameSession.Current;
            ModeCheck(session.state == NativeSessionState.Completed && session.totals.All(row => fixture.session.totals.Any(old => old.seatId == row.seatId && old.score == row.score && old.time == row.time)), "replayed exact live cup totals");
            selectedMode = NativeSessionMode.GrandPrix; phase = Phase.Finished;
            if (menuPresentation) menuPresentation.SetVisible(false);
            PrepareModeRoster(NativeSaveJson.FromJson<NativeSessionConfig>(JsonUtility.ToJson(session.config)));
            ResetRace(true, false);
            var finalStation = session.stations.OrderBy(station => station.index).Last();
            for (int i = 0; i < racers.Length; i++)
            {
                var result = finalStation.results.Single(row => row.seatId == session.config.roster[i].seatId);
                finishSeconds[i] = result.time;
                finishTimes[i] = (float)result.time;
                finishPlaces[i] = result.place;
                while (!progress[i].Finished) progress[i].Cross(progress[i].Target, true);
                racers[i].driving = false;
                racers[i].enabled = false;
                var driver = racers[i].GetComponent<NativeKartOriginalAI>();
                driver.active = false;
                driver.enabled = false;
            }
            capturedFinish = true;
            progressionCommitted = true;
            phase = Phase.Finished;
            yield return null;
            var savedShadowResolution = QualitySettings.shadowResolution;
            var savedShadowProjection = QualitySettings.shadowProjection;
            int savedShadowCascades = QualitySettings.shadowCascades;
            ShowCeremony(session);
            ModeCheck(QualitySettings.shadowResolution == ShadowResolution.VeryHigh && QualitySettings.shadowProjection == ShadowProjection.StableFit
                && QualitySettings.shadowCascades == 2, "ceremony foliage uses stable high-resolution cascaded shadows");
            ModeCheck(NativeCeremonyPlaybackVerification.Run(ceremonyView,raceCamera,reportDirectory), "original Timeline curves and activation boundaries");
            yield return NativeCeremonyPlaybackVerification.CaptureReplay(ceremonyView,raceCamera,reportDirectory);
            ceremonyView.SeekPlayback(raceCamera,17.5f);
            yield return new WaitForSecondsRealtime(.25f);
            ModeCheck(hud.MenuPage == NativeMenuPage.Ceremony && FindObjectsOfType<TMPro.TMP_Text>().Any(text => text.gameObject.activeInHierarchy && text.name == "CompleteText" && text.fontSize == 64), "original ceremony title layout");
            ModeCheck(!FindObjectsOfType<TMPro.TMP_Text>().Any(text => text.gameObject.activeInHierarchy && text.text.Contains("4/4")), "station scoreboard removed");
            ModeCheck(raceCamera.enabled && (!menuPresentation || !menuPresentation.IsVisible), "ceremony uses race camera");
            ModeCheck(ceremonyView.podiumAnchors.All(anchor => anchor.GetComponentsInChildren<Renderer>().Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)), "all three podium avatars visible");
            var title = FindObjectsOfType<TMPro.TMP_Text>().FirstOrDefault(text => text.name == "CompleteText");
            var titleScaler = title ? title.GetComponentInParent<UnityEngine.UI.CanvasScaler>() : null;
            ModeCheck(titleScaler && titleScaler.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize
                && titleScaler.referenceResolution == new Vector2(1280,720), "ceremony title and player text scale with window height");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("ceremony-replay.png");
            ModeCheck(ceremonyView.podiumAnchors.All(anchor => anchor.GetComponentsInChildren<Transform>(true).Any(node => node.name == "Kart")), "podium retains each completed racer vehicle");
            ceremonyView.SeekPlayback(raceCamera, ceremonyView.finishSignalTime - .1f);
            yield return new WaitForSecondsRealtime(.25f);
            ModeCheck(NativeGameSession.Current == null && hud.MenuPage == NativeMenuPage.Main && !ceremony, "original finish signal returns automatically to clean main menu");
            ModeCheck(QualitySettings.shadowResolution == savedShadowResolution && QualitySettings.shadowProjection == savedShadowProjection
                && QualitySettings.shadowCascades == savedShadowCascades, "ceremony restores previous shadow settings");
            ModeCheck(errorCount == 0, "runtime errors=0");
            File.WriteAllText(Path.Combine(reportDirectory, "ceremony-replay-report.json"), JsonUtility.ToJson(new CeremonyReplayReport { passed = modePassed, sourceFile = sourceFile, checks = modeChecks.ToArray(), session = session }, true));
            Application.Quit(modePassed ? 0 : 2);
        }
        IEnumerator CheckFrogTriggerLive()
        {
            selectedMode = NativeSessionMode.NativeRace; Begin();
            while (phase != Phase.Racing) yield return null;
            var trigger = FindObjectOfType<NativeUnlockTrigger>();
            ModeCheck(trackId == "wintermistylake" && trigger != null, "original Winter EETrigger present");
            if (!trigger) { FinishModeReport("frog-trigger-report.json"); yield break; }
            foreach (var motor in racers) { motor.driving = false; motor.enabled = false; motor.GetComponent<NativeKartOriginalAI>().enabled = false; }
            var collider = trigger.GetComponent<BoxCollider>(); Vector3 center = collider.bounds.center;
            Vector3 outside = center + Vector3.right * (collider.bounds.extents.x + 4);
            long generation = NativeSaveStore.Data.generation;
            racers[1].ResetAt(outside, Quaternion.identity, false); Physics.SyncTransforms();
            yield return null;
            for (int step = 0; step < 10; step++) { racers[1].GetComponent<CharacterController>().Move((center - outside) / 10); yield return new WaitForFixedUpdate(); }
            ModeCheck(!NativeSaveStore.IsUnlocked("frog") && NativeSaveStore.Data.generation == generation, "actual AI collider entry preserves lock");
            racers[0].ResetAt(outside, Quaternion.identity, false); Physics.SyncTransforms(); yield return null;
            for (int step = 0; step < 10; step++) { racers[0].GetComponent<CharacterController>().Move((center - outside) / 10); yield return new WaitForFixedUpdate(); }
            ModeCheck(trigger.HumanEntries == 1 && NativeSaveStore.IsUnlocked("frog"), "actual human controller entry unlocks Frog");
            ModeCheck(selectableCharacters.Contains(vehicleRoster.CharacterIndexOf("frog")) && progressionNotice.Contains("FROG"), "unlock prompt and selectable character update");
            generation = NativeSaveStore.Data.generation;
            racers[0].GetComponent<CharacterController>().Move(outside - center); yield return new WaitForFixedUpdate();
            racers[0].GetComponent<CharacterController>().Move(center - outside); yield return new WaitForFixedUpdate();
            ModeCheck(NativeSaveStore.Data.generation == generation, "repeated real entry keeps one unlock save");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("frog-unlocked.png");
            string saveDirectory = Path.GetDirectoryName(NativeSaveStore.FilePath);
            NativeSaveStore.ResetForVerification(); NativeSaveStore.Initialize(new[] { "--save-directory", saveDirectory });
            ModeCheck(NativeSaveStore.IsUnlocked("frog"), "Frog survives disk reload");
            FinishModeReport("frog-trigger-report.json");
        }
    }
}
