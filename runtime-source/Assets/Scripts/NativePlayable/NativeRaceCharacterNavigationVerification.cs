using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class CharacterNavigationSourceRow
        {
            public string characterId, path;
            public int order, mode;
            public bool wrapAround, interactable;
            public string[] staticTargets;
        }
        [Serializable] sealed class CharacterNavigationSource
        {
            public bool sourceSerializedEdgesAreRuntimeOracle;
            public CharacterNavigationSourceRow[] roster;
        }
        [Serializable] sealed class CharacterNavigationEdgeResult
        {
            public string scenario, origin, direction, expectedFromSourceAlgorithm, actualCache;
            public bool originActive, matches;
        }
        [Serializable] sealed class CharacterNavigationStateResult
        {
            public string scenario;
            public NativeAuthoredUi.CharacterNavigationSample[] slots;
        }
        [Serializable] sealed class CharacterNavigationReport
        {
            public bool passed, scriptedState = true, visualParityAccepted, runtimeOriginalAccepted;
            public int staticDirectionDenominator = 96, sourceRuntimeAcceptedEdges;
            public string sourceResourceSHA256;
            public string source = "Original binary RefreshNavigation/Selectable source algorithm plus original24 serialized slots; actual Native cache and device events are separate evidence";
            public string error, cleanupError, currentAppSHA256, engineExecutablePath, engineExecutableSha256, wrapperExecutablePath, wrapperExecutableSha256, assemblyPath, assemblySha256;
            public List<string> checks = new List<string>();
            public List<CharacterNavigationStateResult> states = new List<CharacterNavigationStateResult>();
            public List<CharacterNavigationEdgeResult> edges = new List<CharacterNavigationEdgeResult>();
        }

        // Independent transcription of original FindSelectable score; production uses Unity Selectable virtual direction methods.
        static string CharacterNavigationSourceNeighbor(NativeAuthoredUi.CharacterNavigationSample origin,
            NativeAuthoredUi.CharacterNavigationSample[] slots, int direction)
        {
            var localDirections = new[] { Vector3.up, Vector3.right, Vector3.down, Vector3.left };
            Vector3 worldDirection = (origin.rotation * localDirections[direction]).normalized;
            Vector3 localDirection = Quaternion.Inverse(origin.rotation) * worldDirection;
            float maximum = Mathf.Max(Mathf.Abs(localDirection.x), Mathf.Abs(localDirection.y));
            Vector2 edge = origin.rect.center;
            if (maximum > 0) edge += Vector2.Scale(origin.rect.size * .5f, new Vector2(localDirection.x, localDirection.y) / maximum);
            Vector3 start = origin.localToWorld.MultiplyPoint3x4(edge);
            float bestScore = float.NegativeInfinity;
            string best = null;
            foreach (var candidate in slots)
            {
                if (candidate.characterId == origin.characterId || !candidate.activeInHierarchy || !candidate.interactable || candidate.mode == 0) continue;
                Vector3 delta = candidate.localToWorld.MultiplyPoint3x4(candidate.rect.center) - start;
                float dot = Vector3.Dot(worldDirection, delta);
                if (dot <= 0) continue;
                float score = dot / delta.sqrMagnitude;
                if (score > bestScore) { bestScore = score; best = candidate.characterId; }
            }
            return best;
        }

        static void CharacterNavigationRequire(CharacterNavigationReport report, bool condition, string check)
        {
            report.checks.Add((condition ? "PASS " : "FAIL ") + check);
            if (!condition) throw new InvalidOperationException(check);
        }

        void CheckCharacterNavigationCache(CharacterNavigationReport report, string scenario, NativeAuthoredUi.CharacterNavigationSample[] slots)
        {
            report.states.Add(new CharacterNavigationStateResult { scenario = scenario, slots = slots });
            CharacterNavigationRequire(report, slots.Length == 24, scenario + " has all24 stable slot identities");
            var names = new[] { "up", "right", "down", "left" };
            foreach (var slot in slots)
            {
                CharacterNavigationRequire(report, !slot.buttonEnabled && slot.mode == -1 && !slot.wrapAround,
                    scenario + " " + slot.characterId + " source mode-1/no-wrap/buttons-disabled");
                for (int direction = 0; direction < 4; direction++)
                {
                    string expected = slot.activeSelf ? CharacterNavigationSourceNeighbor(slot, slots, direction) : null;
                    string actual = slot.cachedTargets == null ? null : slot.cachedTargets[direction];
                    bool match = !slot.activeSelf || expected == actual;
                    report.edges.Add(new CharacterNavigationEdgeResult { scenario = scenario, origin = slot.characterId,
                        direction = names[direction], originActive = slot.activeSelf, expectedFromSourceAlgorithm = expected,
                        actualCache = actual, matches = match });
                    if (slot.activeSelf) CharacterNavigationRequire(report, match, scenario + " " + slot.characterId + "/" + names[direction] + " actual cache vs source-algorithm fixture");
                }
            }
        }

        internal IEnumerator CheckCharacterNavigationLive()
        {
            var report = new CharacterNavigationReport();
            int previousVsync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            bool previousPointer = hud.SuppressPointerInput;
            var pending = new Stack<IEnumerator>(); pending.Push(CheckCharacterNavigationCore(report));
            try
            {
                while (pending.Count > 0)
                {
                    object instruction;
                    bool next;
                    try { next = pending.Peek().MoveNext(); instruction = next ? pending.Peek().Current : null; }
                    catch (Exception error) { report.error = error.ToString(); break; }
                    if (!next) { pending.Pop(); if (pending.Count == 0) report.passed = true; continue; }
                    if (instruction is IEnumerator nested && !(instruction is CustomYieldInstruction)) { pending.Push(nested); continue; }
                    yield return instruction;
                }
            }
            finally
            {
                foreach (var routine in pending)
                    try { (routine as IDisposable)?.Dispose(); }
                    catch (Exception error) { report.cleanupError = error.ToString(); report.passed = false; }
                hud.SuppressPointerInput = previousPointer;
                QualitySettings.vSyncCount = previousVsync;
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory, "character-navigation-report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("CHARACTER_NAVIGATION_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }

        IEnumerator CheckCharacterNavigationCore(CharacterNavigationReport report)
        {
            phase = Phase.Menu;
            yield return null;
            for (int frame = 0; !hud.SettingsAuthoredReady && frame < 120; frame++) yield return null;
            CharacterNavigationRequire(report, hud.SettingsAuthoredReady, "production authored UI initialized");
            var identity = new TextSpacingReport(); TextSpacingArtifactIdentity(identity);
            report.currentAppSHA256 = identity.currentAppSHA256;
            report.engineExecutablePath = identity.engineExecutablePath; report.engineExecutableSha256 = identity.engineExecutableSha256;
            report.wrapperExecutablePath = identity.wrapperExecutablePath; report.wrapperExecutableSha256 = identity.wrapperExecutableSha256;
            report.assemblyPath = identity.assemblyPath; report.assemblySha256 = identity.assemblySha256;
            string sourceText = NativeContent.Load<TextAsset>("NativeVerification/CharacterNavigationSource").text;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                report.sourceResourceSHA256 = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sourceText))).Replace("-", "").ToLowerInvariant();
            var baseline = JsonUtility.FromJson<CharacterNavigationSource>(sourceText);
            CharacterNavigationRequire(report, baseline.roster.Length == 24 && !baseline.sourceSerializedEdgesAreRuntimeOracle,
                "24 original identities/96 static fields have separate source runtime acceptance");
            var data = NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi");
            foreach (var row in baseline.roster)
            {
                var node = data.nodes.Single(n => n.scene == "selection" && n.path == row.path);
                CharacterNavigationRequire(report, node.characterId == row.characterId && node.selectionOrder == row.order
                    && node.navigation.SequenceEqual(row.staticTargets) && row.mode == -1 && !row.wrapAround && row.interactable,
                    row.characterId + " original static four cache fields/source mode/interactable");
            }
            // Inject eligibility into the production authored tree; save/unlock policy remains outside this fixture.
            using (var fixture = new NativeAuthoredUi(data, hud.menuFont, hud.uiImageMaterial))
            {
                var all = baseline.roster.Select(row => row.characterId).ToArray();
                foreach (var excluded in new[] { new string[0], new[] { "frog" }, new[] { "aya", "frog" } })
                {
                    fixture.Hide(); fixture.Show("selection", "CharSelection");
                    fixture.PrepareCharacterNavigation(all.Except(excluded));
                    string scenario = excluded.Length == 0 ? "fixture-all-unlocked" : excluded.Length == 1 ? "fixture-frog-locked" : "fixture-aya-frog-locked";
                    CheckCharacterNavigationCache(report, scenario, fixture.CharacterNavigationSnapshot());
                }
                fixture.PrepareCharacterNavigation(all);
                CharacterNavigationRequire(report, !fixture.CharacterSlotActive("frog") && !fixture.CharacterSlotActive("aya"),
                    "same loaded selection unlock keeps source-hidden nodes inactive");
                fixture.Hide(); fixture.Show("selection", "CharSelection"); fixture.PrepareCharacterNavigation(all);
                CharacterNavigationRequire(report, fixture.CharacterSlotActive("frog") && fixture.CharacterSlotActive("aya"),
                    "selection reentry restores authored active nodes under new flags");
            }
            hud.SetMenuPage(NativeMenuPage.Characters);
            hud.SelectMenuIndex(Array.IndexOf(selectableCharacters, vehicleRoster.CharacterIndexOf("reimu")));
            while (hud.MenuTransitionActive) yield return null;
            yield return null; yield return new WaitForEndOfFrame();
            CheckCharacterNavigationCache(report, "production-current-unlock-policy", hud.CharacterNavigationSnapshot());
            Directory.CreateDirectory(reportDirectory);
            ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, "character-navigation-current.png"));
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            var previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            hud.SuppressPointerInput = true;
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                string origin = vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]);
                string expected = hud.CharacterNavigationSnapshot().Single(slot => slot.characterId == origin).cachedTargets[1];
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow)); yield return null; yield return null;
                CharacterNavigationRequire(report, vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == expected,
                    "real Navigate.started device callback selects one cached right neighbor");
                yield return new WaitForSecondsRealtime(2f);
                CharacterNavigationRequire(report, vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == expected,
                    "real two-second held Custom input creates no additional moves");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.UpArrow, Key.RightArrow)); yield return null; yield return null;
                CharacterNavigationRequire(report, vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == expected,
                    "nonzero direction/diagonal changes are performed and create no new started move");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.DownArrow, Key.RightArrow)); yield return null; yield return null;
                string down = hud.CharacterNavigationSnapshot().Single(slot => slot.characterId == expected).cachedTargets[2];
                CharacterNavigationRequire(report, vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == (down ?? expected),
                    "neutral reset then diagonal started applies original vertical priority");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                string before = vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]);
                string right = hud.CharacterNavigationSnapshot().Single(slot => slot.characterId == before).cachedTargets[1];
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow)); yield return null; yield return null;
                CharacterNavigationRequire(report, vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == (right ?? before),
                    "release and repress emits a new real started callback");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                hud.SetMenuPage(NativeMenuPage.Main); hud.SelectMenuIndex(0);
                while (hud.MenuTransitionActive) yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.DownArrow)); yield return null; yield return null;
                int first = hud.SelectedMenuIndex;
                CharacterNavigationRequire(report, first == 1, "ordinary Main input starts one step");
                yield return new WaitForSecondsRealtime(.35f);
                CharacterNavigationRequire(report, hud.SelectedMenuIndex == first, "ordinary Main retains source .5s initial repeat delay");
                yield return new WaitForSecondsRealtime(.3f);
                CharacterNavigationRequire(report, hud.SelectedMenuIndex != first, "ordinary Main retains source held repeat after .5s");
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = previousBackground;
            }
#else
            throw new PlatformNotSupportedException("Character navigation device-event verification requires the Native player Input System runtime");
#endif
        }
    }
}
