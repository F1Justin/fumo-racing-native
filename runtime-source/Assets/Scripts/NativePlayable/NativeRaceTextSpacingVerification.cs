using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class TextSpacingFieldSample
        {
            public string path, text;
            public bool expected, actual, found;
        }
        [Serializable] sealed class TextSpacingTextSample
        {
            public string path, expected, actual;
            public bool active, enableKerning, richText;
            public Rect screenRect;
        }
        [Serializable] sealed class TextSpacingState
        {
            public string page = "CircuitSelection", mode = "TimeTrials", track = "forest";
            public string character = "reimu", vehicle = "kart", language = "en";
            public int[] resolution;
        }
        [Serializable] sealed class TextSpacingTimeSample
        {
            public string value;
            public bool enableKerning;
            public int instanceId;
        }
        [Serializable] sealed class TextSpacingReport
        {
            public bool passed, scriptedState = true, gameplayAcceptance, visualParityAccepted;
            public string source = "Injected original TT record through production Cup/Circuit rendering; live authored TMP fields, original main TIME and an injected stale state in the production pooled Online label";
            public string currentAppSHA256, wrapperExecutablePath, wrapperExecutableSha256;
            public string engineExecutablePath, engineExecutableSha256, assemblyPath, assemblySha256;
            public string screenshot = "text-spacing-forest-records.png";
            public string originalFrame = "screenshots/original/local-mac/time-trials-circuit-forest.png";
            public string fontEvidence = "Alphakind SDF and its four original fallback assets have zero modern and legacy kerning pairs; instance migration is verified separately from visible spacing";
            public string[] checks;
            public TextSpacingFieldSample[] authoredFields;
            public TextSpacingTextSample[] recordTexts;
            public TextSpacingState recordState;
            public NativeSaveData.TimeTrialRecord recordSource;
            public TextSpacingTimeSample authoredMainTime;
            public string poolReuseScope = "Inject stale kerning=false into the production pooled Online label, then verify its next production render resets true";
            public int poolContaminatedInstanceId;
            public bool poolReuseKerningReset;
            public int reusedInstanceId, sourceOffCount, sourceOnCount;
        }

        static TMP_Text TextSpacingAuthoredText(GameObject root, NativeAuthoredUiNode definition)
        {
            if (!root) return null;
            Transform sceneRoot = null;
            foreach (Transform child in root.transform)
                if (child.name == definition.scene + " authored Canvas") { sceneRoot = child; break; }
            var target = sceneRoot ? sceneRoot.Find(definition.path.Substring("Canvas/".Length)) : null;
            return target ? target.GetComponent<TMP_Text>() : null;
        }

        static Rect TextSpacingScreenRect(TMP_Text text)
        {
            if (!text) return new Rect();
            var corners = new Vector3[4];
            text.rectTransform.GetWorldCorners(corners);
            var bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            var topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return new Rect(bottomLeft.x, Screen.height - topRight.y, topRight.x - bottomLeft.x, topRight.y - bottomLeft.y);
        }

        static string TextSpacingFileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        [System.Runtime.InteropServices.DllImport("libSystem.dylib", EntryPoint = "_NSGetExecutablePath")]
        static extern int TextSpacingNativeExecutablePath(System.Text.StringBuilder path, ref uint size);

        static void TextSpacingArtifactIdentity(TextSpacingReport report)
        {
            uint capacity = 4096;
            var enginePath = new System.Text.StringBuilder((int)capacity);
            if (TextSpacingNativeExecutablePath(enginePath, ref capacity) != 0)
                throw new InvalidOperationException("Running macOS executable path exceeds buffer");
            report.engineExecutablePath = Path.GetFullPath(enginePath.ToString());
            string macOS = Path.GetDirectoryName(report.engineExecutablePath);
            string contents = Directory.GetParent(macOS).FullName;
            string plist = File.ReadAllText(Path.Combine(contents, "Info.plist"));
            var executable = System.Text.RegularExpressions.Regex.Match(plist,
                @"<key>CFBundleExecutable</key>\s*<string>([^<]+)</string>");
            if (!executable.Success) throw new InvalidOperationException("Bundle executable identity missing");
            report.wrapperExecutablePath = Path.GetFullPath(Path.Combine(macOS, executable.Groups[1].Value));
            report.assemblyPath = Path.GetFullPath(typeof(NativeRace).Assembly.Location);
            report.engineExecutableSha256 = TextSpacingFileHash(report.engineExecutablePath);
            report.wrapperExecutableSha256 = TextSpacingFileHash(report.wrapperExecutablePath);
            report.assemblySha256 = TextSpacingFileHash(report.assemblyPath);
            report.currentAppSHA256 = report.wrapperExecutableSha256;
        }

        internal IEnumerator CheckTextSpacingLive()
        {
            var report = new TextSpacingReport();
            var checks = new List<string>();
            bool completed = false;
            Action<bool, string> require = (passed, name) => {
                checks.Add((passed ? "PASS " : "FAIL ") + name);
                Debug.Log("NATIVE_TEXT_SPACING_CHECK " + passed + " " + name);
            };
            Directory.CreateDirectory(reportDirectory);
            var savedRecords = NativeSaveStore.Data.timeTrialRecords;
            var savedSettings = NativeSaveStore.Data.settings;
            bool savedPointer = hud.SuppressPointerInput;
            float savedTimeScale = Time.timeScale;
            int savedVSync = QualitySettings.vSyncCount;
            var savedPhase = phase;
            var savedMode = selectedMode;
            var savedPage = hud.MenuPage;
            var savedTrack = selectedTrackId;
            int savedCharacter = selectedCharacterIndex, savedVehicle = selectedVehicleIndex, savedSkin = selectedSkinIndex;
            double savedFinish = finishSeconds[0], savedLapTimestamp = lapTimestamps[0];
            var savedDriving = racers.Select(r => r.driving).ToArray();
            try
            {
                QualitySettings.vSyncCount = 0;
                Screen.SetResolution(1280, 800, FullScreenMode.Windowed);
                float deadline = Time.realtimeSinceStartup + 5;
                while ((Screen.width != 1280 || Screen.height != 800) && Time.realtimeSinceStartup < deadline) yield return null;
                require(Screen.width == 1280 && Screen.height == 800, "original 1280x800 framebuffer");
                hud.SuppressPointerInput = true;
                NativeSaveStore.Data.settings = savedSettings.Where(s => s.key != "lang_game").Concat(new[] {
                    new NativeSaveData.Setting { key = "lang_game", value = "0", type = "int" }
                }).ToArray();
                NativeSaveStore.Data.timeTrialRecords = savedRecords.Where(r => r.trackId != "forest").Concat(new[] {
                    new NativeSaveData.TimeTrialRecord {
                        trackId = "forest", characterId = "reimu", vehicleId = "kart", bestSeconds = 96.504,
                        lapSeconds = new[] { 20.553, 18.600, 18.833, 19.351, 19.167 }, lapCount = 5,
                        source = "text-spacing-scripted-original-state"
                    }
                }).ToArray();
                report.recordSource = NativeSaveStore.Data.timeTrialRecords.Single(r => r.trackId == "forest");
                phase = Phase.Menu;
                selectedMode = NativeSessionMode.TimeTrials;
                selectedTrackId = "forest";
                selectedCharacterIndex = vehicleRoster.CharacterIndexOf("reimu");
                selectedVehicleIndex = vehicleRoster.VehicleIndexOf("kart");
                selectedSkinIndex = 0;
                OpenModeCup();
                deadline = Time.realtimeSinceStartup + 5;
                while (hud.MenuTransitionActive && Time.realtimeSinceStartup < deadline) yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                require(hud.MenuPage == NativeMenuPage.Cups && !hud.MenuTransitionActive, "production TT Cup page settles");
                ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Cups));
                hud.SelectMenuIndex(NativeTrackCatalog.IndexOf("forest"));
                deadline = Time.realtimeSinceStartup + 5;
                while (hud.MenuTransitionActive && Time.realtimeSinceStartup < deadline) yield return null;
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                require(hud.MenuPage == NativeMenuPage.Tracks && hud.SelectedMenuIndex == NativeTrackCatalog.IndexOf("forest"), "production Cup confirm renders selected Forest circuit");
                require(hud.OriginalTrackBannersBound(out string bannerDetail), bannerDetail);
                var root = GameObject.Find("Original authored menus");
                var fields = new List<TextSpacingFieldSample>();
                foreach (var definition in hud.authoredUiData.nodes.Where(n => n.hasText))
                {
                    TMP_Text text = TextSpacingAuthoredText(root, definition);
                    fields.Add(new TextSpacingFieldSample {
                        path = definition.scene + "/" + definition.path, expected = definition.enableKerning,
                        actual = text && text.enableKerning, found = text, text = text ? text.text : ""
                    });
                }
                report.authoredFields = fields.ToArray();
                report.sourceOffCount = fields.Count(s => !s.expected);
                report.sourceOnCount = fields.Count(s => s.expected);
                require(fields.Count == 394 && report.sourceOffCount == 31 && report.sourceOnCount == 363, "394 original text instances retain 31 off and 363 on");
                require(fields.All(s => s.found && s.expected == s.actual), "all authored TMP enableKerning values match exported original definitions");
                require(hud.OriginalImageInitializationBound(out string imageDetail), imageDetail);
                hud.WriteOriginalImageInitializationReport(reportDirectory);
                const string panel = "selection/Canvas/CupSelection/TimePanel/";
                var expectedTexts = new[] { "<mspace=24>01:36.504</mspace>", "<mspace=16>00:20.553</mspace>", "<mspace=16>00:18.600</mspace>",
                    "<mspace=16>00:18.833</mspace>", "<mspace=16>00:19.351</mspace>", "<mspace=16>00:19.167</mspace>" };
                var texts = new List<TextSpacingTextSample>();
                for (int i = 0; i < expectedTexts.Length; i++)
                {
                    string key = panel + (i == 0 ? "TimeTotal" : "lap" + i + "time");
                    var definition = hud.authoredUiData.nodes.Single(n => n.scene + "/" + n.path == key);
                    var text = TextSpacingAuthoredText(root, definition);
                    texts.Add(new TextSpacingTextSample { path = key, expected = expectedTexts[i], actual = text ? text.text : "",
                        active = text && text.enabled && text.gameObject.activeInHierarchy, enableKerning = text && text.enableKerning,
                        richText = text && text.richText, screenRect = TextSpacingScreenRect(text) });
                }
                report.recordTexts = texts.ToArray();
                require(texts.All(s => s.active && s.actual == s.expected), "production record total and five lap raw TMP texts retain original mspace=24/16");
                require(texts.All(s => !s.enableKerning && s.richText), "all six record labels parse original mspace tags with source kerning=false");
                require(texts.All(s => s.screenRect.width > 0 && s.screenRect.height > 0), "all six record text regions have runtime layout");
                report.recordState = new TextSpacingState { mode = selectedMode.ToString(), track = selectedTrackId,
                    character = vehicleRoster.CharacterIdAt(selectedCharacterIndex), vehicle = vehicleRoster.VehicleIdAt(selectedVehicleIndex),
                    language = NativeLocalization.Language == 0 ? "en" : NativeLocalization.LanguageName,
                    resolution = new[] { Screen.width, Screen.height } };
                ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, report.screenshot));
                yield return null;
                yield return new WaitForEndOfFrame();
                foreach (var racer in racers) racer.driving = false;
                Time.timeScale = 0;
                phase = Phase.Racing;
                finishSeconds[0] = 0;
                lapTimestamps[0] = PreciseElapsed - 5.555;
                hud.ResetOriginalRaceDynamics();
                hud.SetMenuPage(NativeMenuPage.None);
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                var mainTimeDefinition = hud.authoredUiData.nodes.Single(n => n.scene + "/" + n.path == "HUD/Canvas/Time");
                TMP_Text mainTime = TextSpacingAuthoredText(root, mainTimeDefinition);
                report.authoredMainTime = new TextSpacingTimeSample { value = mainTime ? mainTime.text : "",
                    enableKerning = mainTime && mainTime.enableKerning, instanceId = mainTime ? mainTime.GetInstanceID() : 0 };
                require(mainTime && mainTime.gameObject.activeInHierarchy && mainTime.text == "<mspace=24>00:05.555</mspace>" && !mainTime.enableKerning,
                    "production original HUD/Canvas/Time 00:05.555 uses source kerning=false");
                hud.CheckOriginalTimeBoundaryFields(racers[0], require);
                phase = Phase.Menu;
                hud.SetMenuPage(NativeMenuPage.Online);
                deadline = Time.realtimeSinceStartup + 5;
                while (hud.MenuTransitionActive && Time.realtimeSinceStartup < deadline) yield return null;
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                var pooledRoot = GameObject.Find("Native original UI Canvas");
                var reusedLabel = pooledRoot ? pooledRoot.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t =>
                    t.gameObject.activeInHierarchy && t.text == "Online play is unavailable in this build.") : null;
                report.reusedInstanceId = reusedLabel ? reusedLabel.GetInstanceID() : 0;
                report.poolContaminatedInstanceId = report.reusedInstanceId;
                if (reusedLabel) reusedLabel.enableKerning = false;
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                report.poolReuseKerningReset = reusedLabel && reusedLabel.gameObject.activeInHierarchy && reusedLabel.enableKerning
                    && reusedLabel.GetInstanceID() == report.poolContaminatedInstanceId;
                require(report.poolReuseKerningReset, "next production pooled Online label render clears injected stale kerning=false on the same instance");
                require(errorCount == 0, "no Player runtime errors");
                try { TextSpacingArtifactIdentity(report); completed = true; }
                catch (Exception failure) { require(false, "artifact identity: " + failure.Message); }
            }
            finally
            {
                NativeSaveStore.Data.timeTrialRecords = savedRecords;
                NativeSaveStore.Data.settings = savedSettings;
                hud.SuppressPointerInput = savedPointer;
                Time.timeScale = savedTimeScale;
                QualitySettings.vSyncCount = savedVSync;
                phase = savedPhase; selectedMode = savedMode; selectedTrackId = savedTrack;
                selectedCharacterIndex = savedCharacter; selectedVehicleIndex = savedVehicle; selectedSkinIndex = savedSkin;
                finishSeconds[0] = savedFinish; lapTimestamps[0] = savedLapTimestamp;
                for (int i = 0; i < racers.Length; i++) racers[i].driving = savedDriving[i];
                hud.SetMenuPage(savedPage);
                report.checks = checks.ToArray();
                report.passed = completed && checks.Count > 0 && checks.All(s => s.StartsWith("PASS ", StringComparison.Ordinal));
                File.WriteAllText(Path.Combine(reportDirectory, "text-spacing-report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("NATIVE_TEXT_SPACING_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
