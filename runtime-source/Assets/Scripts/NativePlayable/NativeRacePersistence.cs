using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        static string switchSessionId;
        static readonly List<string> sessionSwitchChecks = new List<string>();
        static bool sessionSwitchPassed = true;

        [Serializable] sealed class PersistenceReport
        {
            public bool passed;
            public string[] checks;
            public NativeSaveData data;
            public string savePath;
            public NativeSessionSnapshot session;
        }

        void EnsureSession()
        {
            var current = NativeGameSession.Current;
            if (current != null && current.CurrentTrackId == selectedTrackId && current.config.mode == selectedMode
                && (current.state == NativeSessionState.Ready || current.state == NativeSessionState.Racing) && LocalSessionRosterMatches(current.config))
            {
                sessionId = current.sessionId;
                if (current.state == NativeSessionState.Racing) NativeGameSession.Restart(sessionId);
                return;
            }
            var config = BuildModeConfig();
            sessionId = NativeGameSession.Create(config);
        }

        IEnumerator CheckPersistence()
        {
            string originalPath = NativeSaveStore.FilePath;
            yield return NativeSaveVerification.StartVerification(reportDirectory);
            bool savePassed = NativeSaveVerification.Passed;
            bool sessionPassed = NativeSessionVerification.Run(reportDirectory);
            NativeSaveStore.ResetForVerification();
            NativeSaveStore.Initialize(new[] { "--save-directory", Path.GetDirectoryName(originalPath) }, key => 0);
            var report = new PersistenceReport
            {
                passed = savePassed && sessionPassed && errorCount == 0,
                checks = new[] { "save-verification=" + savePassed, "session-verification=" + sessionPassed, "runtime-errors=" + errorCount },
                savePath = originalPath, data = NativeSaveStore.Data
            };
            WritePersistenceReport(report, "persistence-report.json");
            Application.Quit(report.passed ? 0 : 2);
        }

        // The read probe deliberately replaces the race scene. Its coordinator
        // must survive that load, and the newly loaded Race must not start it again.
        sealed class PersistenceProbeHost : MonoBehaviour { }
        IEnumerator CheckPersistenceProbe(string[] args, bool coordinated = false)
        {
            if (!coordinated)
            {
                if (FindObjectOfType<PersistenceProbeHost>()) yield break;
                var host = new GameObject("Persistence probe coordinator");
                DontDestroyOnLoad(host);
                host.AddComponent<PersistenceProbeHost>().StartCoroutine(CheckPersistenceProbe(args, true));
                yield break;
            }
            yield return null;
            NativeRace currentRace = this;
            var checks = new List<string>();
            bool passed = true;
            Action<bool, string> check = (ok, name) => { checks.Add((ok ? "PASS " : "FAIL ") + name); passed &= ok; };
            string action = Argument(args, "--probe-action") ?? "read";
            if (action == "write")
            {
                check(NativeSaveStore.Unlock("aya"), "unlock");
                check(NativeSaveStore.SetPreferences("mountains", "miko", "bike"), "stable preferences");
                check(NativeSaveStore.RecordLegacyBest("forest", 66.125), "legacy best");
                NativeSaveStore.Data.settings = new[] { new NativeSaveData.Setting { key = "mainVolume", value = "0.75", type = "float" } };
                NativeSaveStore.Data.inputProfiles = new[] { new NativeSaveData.InputProfile
                {
                    profileId = "keyboard-0", index = 0, name = "Keyboard", devicePath = "<Keyboard>",
                    overrides = new[] { new NativeSaveData.InputOverride { actionId = "steer", path = "<Keyboard>/a#<Keyboard>/d" } }
                } };
                check(NativeSaveStore.Save(), "save");
            }
            else if (action == "read")
            {
                check(NativeSaveStore.IsUnlocked("aya") && !NativeSaveStore.IsUnlocked("frog"), "restart unlock state");
                check(NativeSaveStore.Data.preferredTrack == "mountains" && selectedCharacterIndex == vehicleRoster.CharacterIndexOf("miko")
                    && selectedVehicleIndex == vehicleRoster.VehicleIndexOf("bike"), "restart stable selection");
                check(NativeSaveStore.GetLegacyBest("forest") == 66.125, "restart legacy best");
                check(NativeSaveStore.Data.settings.Length == 1 && NativeSaveStore.Data.settings[0].value == "0.75"
                    && NativeSaveStore.Data.inputProfiles.Length == 1 && NativeSaveStore.Data.inputProfiles[0].overrides[0].path == "<Keyboard>/a#<Keyboard>/d", "restart settings and profile");
                check(selectableCharacters.Length == 23 && !selectableCharacters.Contains(vehicleRoster.CharacterIndexOf("frog")), "menu unlocked ID mapping");
                check(trackId == "mountains", "restart preferred scene");
                var display = menuPresentation.randomDisplay;
                for (int i = 0; i < 30; i++) display.Populate();
                check(display.DisplayedCharacters.All(id => id != "Frog"), "random display uses unlocked pool");
                check(CreateSourceDiagnosticOwnerBeforeBegin(), "actual restored-selection PlayerInput owner");
                // Original local selection starts P1 on Reimu on its first load;
                // persisted legacy preferences above do not replace that contract.
                var ownerChoice = localOwners.SavedChoice(localOwners.Players[0]);
                check(ownerChoice != null && ownerChoice.Character == vehicleRoster.CharacterIndexOf("reimu")
                    && ownerChoice.Vehicle == vehicleRoster.VehicleIndexOf("bike"),
                    "original first-entry character and restored vehicle on actual owner");
                OpenQuickRaceFromMenu();
                hud.SelectMenuIndex(NativeTrackCatalog.IndexOf(trackId));
                ConfirmCurrentTrackSelection();
                yield return null;
                currentRace = FindObjectOfType<NativeRace>();
                var session = NativeGameSession.Current;
                int human = session == null ? -1 : session.config.roster.FindIndex(seat => seat.isHuman);
                check(currentRace && !ReferenceEquals(currentRace, this) && currentRace.trackId == "mountains"
                    && session != null && human >= 0 && human < session.config.roster.Count
                    && session.config.roster[human].isHuman && session.config.roster[human].characterId == "reimu"
                    && session.config.roster[human].vehicleId == "bike", "committed owner and restored vehicle start real session after scene load");
                if (currentRace) currentRace.ResetRace(false);
                check(NativeGameSession.Current == null, "menu return cancels session");
            }
            else if (action == "import")
            {
                check(string.IsNullOrEmpty(NativeSaveExchange.LastError), "import accepted");
                check(NativeSaveStore.Data.importedSources.Length > 0, "source SHA committed");
                string expectedKind = Argument(args, "--expect-import-kind");
                if (expectedKind == "tt") check(NativeSaveStore.Data.timeTrialRecords.Length > 0, "TT fields imported");
                if (expectedKind == "gp") check(NativeSaveStore.Data.cupRecords.Length > 0, "GP fields imported");
                if (expectedKind == "unlockables") check(NativeSaveStore.IsUnlocked("aya") && NativeSaveStore.IsUnlocked("frog"), "unlock keys imported");
                if (expectedKind == "profile") check(NativeSaveStore.Data.inputProfiles.Length > 0, "profile imported");
            }
            else check(false, "unknown probe action");
            check(errorCount == 0 && currentRace && currentRace.errorCount == 0, "runtime errors");
            yield return new WaitForEndOfFrame();
            currentRace.CaptureTestScreenshot("persistence-menu.png");
            yield return null;
            currentRace.WritePersistenceReport(new PersistenceReport { passed = passed, checks = checks.ToArray(), data = NativeSaveStore.Data,
                savePath = NativeSaveStore.FilePath, session = NativeGameSession.Current }, "persistence-probe.json");
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator CheckSessionSwitches()
        {
            Directory.CreateDirectory(reportDirectory);
            yield return null;
            if (switchTestStage == 0)
            {
                sessionSwitchChecks.Clear(); sessionSwitchPassed = true;
                var config = new NativeSessionConfig { mode = NativeSessionMode.GrandPrix, modeId = "grand-prix", racerCount = racers.Length,
                    trackIds = new List<string> { "forest", "mountains", "purplefield", "wintermistylake", "forest" } };
                for (int i = 0; i < racers.Length; i++) config.roster.Add(new NativeSessionSeat { seatId = "seat-" + i,
                    characterId = racerNames[i].Split('/')[0].Trim().ToLowerInvariant(), vehicleId = "kart", isHuman = i == 0 });
                switchSessionId = NativeGameSession.Create(config);
            }
            var snapshot = NativeGameSession.Current;
            bool valid = SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().name == SceneForTrack(trackId)
                && NativeGameSession.Resolve(switchSessionId, trackId) && snapshot.stationIndex == switchTestStage
                && snapshot.config.roster.Count == racers.Length && FindObjectsOfType<NativeRace>().Length == 1
                && (switchTestStage == 0 || switchPreviousRace == null) && errorCount == 0;
            sessionSwitchChecks.Add((valid ? "PASS " : "FAIL ") + "scene/session/roster " + trackId + " station=" + switchTestStage);
            sessionSwitchPassed &= valid;
            if (valid)
            {
                NativeGameSession.Begin(switchSessionId, trackId);
                for (int i = 0; i < racers.Length; i++) NativeGameSession.RecordFinish(switchSessionId, "seat-" + i, 10 + switchTestStage + i, i + 1);
                sessionSwitchPassed &= NativeGameSession.CompleteStation(switchSessionId);
                snapshot = NativeGameSession.Current;
                sessionSwitchPassed &= snapshot.totals[0].score == (switchTestStage + 1) * 7;
                if (switchTestStage < 4 && sessionSwitchPassed)
                {
                    sessionSwitchPassed &= NativeGameSession.Advance(switchSessionId);
                    switchPreviousRace = this;
                    switchTestStage++;
                    ChangeTrack(NativeGameSession.CurrentTrackId, false);
                    yield break;
                }
            }
            sessionSwitchPassed &= NativeGameSession.Cancel(switchSessionId) && NativeGameSession.Current == null;
            WritePersistenceReport(new PersistenceReport { passed = sessionSwitchPassed, checks = sessionSwitchChecks.ToArray(), session = snapshot }, "session-switch-report.json");
            Application.Quit(sessionSwitchPassed ? 0 : 2);
        }

        void WritePersistenceReport(PersistenceReport report, string name)
        {
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory, name), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("NATIVE_PERSISTENCE_RESULT " + name + " passed=" + report.passed);
        }
    }
}
