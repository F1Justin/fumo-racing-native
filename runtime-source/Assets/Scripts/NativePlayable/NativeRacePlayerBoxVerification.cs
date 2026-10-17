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
        [Serializable] sealed class PlayerBoxSourceBaseline
        {
            public float arrowStartScale, arrowDuration, directionThreshold;
            public int arrowEaseEnum, rightDirection, cupSplitCount, circuitSplitCount;
            public bool startedOnly, positiveTotalOnly, loadedClearsCircuitPanel, backFromCircuitPreservesRecordPanels;
            public string[] easeClosure;
        }
        [Serializable] sealed class PlayerBoxRecordCase
        {
            public string mode, record, stage;
            public NativeAuthoredUi.SelectionRecordPanelSample[] panels;
        }
        [Serializable] sealed class PlayerBoxPreviewSample
        {
            public string page, character, vehicle, skin, shader, material, texture, screenshot, renderTextureCapture, renderFieldsCapture;
            public bool textureCreated, rawMatchesCamera, rawEnabled, rawActive, rawCull, renderFieldsPassed;
            public Color rawColor;
            public float rendererAlpha, near, far;
            public int pixelCount, opaquePixels, nonzeroAlphaPixels, activeRenderers, cameraCullingMask;
            public Vector3 cameraPosition, cameraForward, actorBoundsCenter, actorBoundsSize, viewportCenter;
            public Quaternion actorRotation;
            public Rect rawScreenRect;
        }
        [Serializable] sealed class PlayerBoxAudioSample
        {
            public string page, clip, mixerGroup, sourceClip;
            public int sourceInstanceId, calls, wrapperCalls;
            public bool verificationPlayback;
            public bool muted, sourceEnabled, sourcePlaying, matchesOriginalClip;
        }
        [Serializable] sealed class PlayerBoxReport
        {
            public bool passed, nativeInjectedState = true, realInputDeviceEvents, visualParityAccepted, originalRuntimeAccepted;
            public string error, cleanupError, assemblyPath, assemblySha256, currentAppSHA256, sourceResourceSHA256;
            public string engineExecutablePath, engineExecutableSha256, wrapperExecutablePath, wrapperExecutableSha256;
            public string evidenceScope = "Source binary events, original serialized fields, Native injected records and actual keyboard events; PNG/RT diagnostics are Native captures; original state pairing remains open";
            public List<string> checks = new List<string>();
            public List<PlayerBoxRecordCase> records = new List<PlayerBoxRecordCase>();
            public List<PlayerBoxPreviewSample> previews = new List<PlayerBoxPreviewSample>();
            public List<PlayerBoxAudioSample> audio = new List<PlayerBoxAudioSample>();
        }
        static void PlayerBoxRequire(PlayerBoxReport report, bool valid, string check)
        {
            report.checks.Add((valid ? "PASS " : "FAIL ") + check);
            if (!valid) throw new InvalidOperationException(check);
        }
        AudioClip playerBoxObservedClip;
        AudioSource playerBoxObservedSource;
        int playerBoxObservedCalls;
        void ObservePlayerBoxOneShot(AudioSource source, AudioClip clip)
        {
            if (source != hud.OriginalSelectionMoveSource) return;
            playerBoxObservedSource = source; playerBoxObservedClip = clip; playerBoxObservedCalls++;
        }
        internal IEnumerator CheckPlayerBoxSelectionLive()
        {
            var report = new PlayerBoxReport();
            bool previousUiPlayback = NativeAudioRuntime.UiVerificationPlayback;
            NativeAudioRuntime.UiVerificationPlayback = true;
            playerBoxObservedCalls = 0;
            NativeAudioRuntime.UiOneShotPlayed += ObservePlayerBoxOneShot;
            int previousVsync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            bool previousPointer = hud.SuppressPointerInput;
            var savedTrial = NativeSaveStore.Data.timeTrialRecords;
            var savedCup = NativeSaveStore.Data.cupRecords;
            var pending = new Stack<IEnumerator>(); pending.Push(CheckPlayerBoxCore(report));
            try
            {
                while (pending.Count > 0)
                {
                    bool next; object instruction;
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
                NativeAudioRuntime.UiOneShotPlayed -= ObservePlayerBoxOneShot;
                NativeAudioRuntime.UiVerificationPlayback = previousUiPlayback;
                NativeSaveStore.Data.timeTrialRecords = savedTrial;
                NativeSaveStore.Data.cupRecords = savedCup;
                hud.SuppressPointerInput = previousPointer;
                QualitySettings.vSyncCount = previousVsync;
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory, "playerbox-selection-report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("PLAYERBOX_SELECTION_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }
        IEnumerator CheckPlayerBoxCore(PlayerBoxReport report)
        {
            Directory.CreateDirectory(reportDirectory);
            phase = Phase.Menu; hud.SuppressPointerInput = true;
            for (int frame = 0; !hud.SettingsAuthoredReady && frame < 120; frame++) yield return null;
            PlayerBoxRequire(report, hud.SettingsAuthoredReady, "production authored UI initialized");
            PlayerBoxRequire(report, NativePlayerBoxSoundData.Move.name == "CharacterSubMove",
                "source PlayerBox move index7 binds original CharacterSubMove AudioClip");
            PlayerBoxRequire(report, NativePlayerBoxSoundData.CharacterMove.name == "CharacterMove",
                "source CharacterSlot move index4 binds original CharacterMove AudioClip");
            string baselineText = NativeContent.Load<TextAsset>("NativeVerification/PlayerBoxSource").text;
            var baseline = JsonUtility.FromJson<PlayerBoxSourceBaseline>(baselineText);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                report.sourceResourceSHA256 = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(baselineText))).Replace("-", "").ToLowerInvariant();
            PlayerBoxRequire(report, baseline.arrowStartScale == 1.5f && baseline.arrowDuration == .2f && baseline.arrowEaseEnum == 6
                && baseline.directionThreshold == .5f && baseline.rightDirection == 1 && baseline.startedOnly
                && baseline.cupSplitCount == 4 && baseline.circuitSplitCount == 5 && baseline.positiveTotalOnly
                && baseline.loadedClearsCircuitPanel && baseline.backFromCircuitPreservesRecordPanels && baseline.easeClosure.Length == 5,
                "independent binary/authored baseline closes DOScale, ease initialization, directional input and record lifetime");
            var identity = new TextSpacingReport(); TextSpacingArtifactIdentity(identity);
            report.assemblyPath = identity.assemblyPath; report.assemblySha256 = identity.assemblySha256; report.currentAppSHA256 = identity.currentAppSHA256;
            report.engineExecutablePath = identity.engineExecutablePath; report.engineExecutableSha256 = identity.engineExecutableSha256;
            report.wrapperExecutablePath = identity.wrapperExecutablePath; report.wrapperExecutableSha256 = identity.wrapperExecutableSha256;
            PlayerBoxRequire(report, NativePlayerBoxNavigation.Step(Vector2.right) == 1
                && NativePlayerBoxNavigation.Step(Vector2.up) == -1 && NativePlayerBoxNavigation.Step(Vector2.down) == -1
                && NativePlayerBoxNavigation.Step(Vector2.left) == -1 && NativePlayerBoxNavigation.Step(Vector2.zero) == 0,
                "source31D1BB/31D486 Right1 increment; all other dispatched directions decrement");
            PlayerBoxRequire(report, NativePlayerBoxNavigation.Direction(new Vector2(.5f, 0)) == 1
                && NativePlayerBoxNavigation.Direction(new Vector2(-.5f, 0)) == 3
                && NativePlayerBoxNavigation.Direction(new Vector2(1, .5f)) == 0
                && NativePlayerBoxNavigation.Direction(new Vector2(1, -.5f)) == 2
                && NativePlayerBoxNavigation.Direction(new Vector2(.499f, .499f)) == -1,
                "source33978C/33979D/3397AC/3397B1 inclusive thresholds and vertical priority");
            selectedCharacterIndex = vehicleRoster.CharacterIndexOf("reimu");
            hud.VehicleCharacterSelection = selectedCharacterIndex;
            hud.ConfigureSkinChoices(vehicleRoster.SkinCountAt(selectedCharacterIndex));
            foreach (var page in new[] { NativeMenuPage.Vehicles, NativeMenuPage.Skins })
            {
                hud.SetMenuPage(page); hud.SelectMenuIndex(0);
                while (hud.MenuTransitionActive) yield return null;
                yield return null;
                foreach (int step in new[] { 1, -1 })
                {
                    int before = hud.SelectedMenuIndex;
                    hud.MoveOriginalPlayerBox(step);
                    CapturePlayerBoxAudio(report, page, true);
                    PlayerBoxRequire(report, hud.SelectedMenuIndex == (before + step + hud.PlayerBoxChoiceCount) % hud.PlayerBoxChoiceCount,
                        page + " source cyclic index step " + step);
                    float started = Time.time;
                    PlayerBoxRequire(report, hud.PlayerBoxArrowScale(step > 0) == Vector3.one * baseline.arrowStartScale, page + " arrow initial1.5 leaf scale");
                    yield return new WaitForSeconds(.1f);
                    float elapsed = Time.time - started, t = Mathf.Clamp01(elapsed / baseline.arrowDuration);
                    float expected = 1f + (baseline.arrowStartScale - 1f) * (1f - t) * (1f - t);
                    PlayerBoxRequire(report, Mathf.Abs(hud.PlayerBoxArrowScale(step > 0).x - expected) < .025f,
                        page + " arrow source OutQuad at actual scaled time " + elapsed);
                    yield return new WaitForSeconds(.12f);
                    PlayerBoxRequire(report, hud.PlayerBoxArrowScale(step > 0) == Vector3.one, page + " arrow finishes at1 after.2 scaled seconds");
                }
            }
            yield return CheckPlayerBoxDeviceInput(report);
            yield return CheckPlayerBoxRecordMatrix(report);
            foreach (var page in new[] { NativeMenuPage.Characters, NativeMenuPage.Vehicles, NativeMenuPage.Skins })
            {
                hud.SetMenuPage(page); hud.SelectMenuIndex(page == NativeMenuPage.Characters ? Array.IndexOf(selectableCharacters, selectedCharacterIndex) : 0);
                while (hud.MenuTransitionActive) yield return null;
                yield return null; yield return null; yield return new WaitForEndOfFrame();
                var preview = CapturePlayerBoxPreview(page);
                report.previews.Add(preview);
                PlayerBoxRequire(report, preview.renderFieldsPassed,
                    page + " actual preview RT, original model/CKC/owner/target denominators, Start and eight MPB fields");
            }
            PlayerBoxRequire(report, report.previews.All(p => p.textureCreated && p.rawMatchesCamera && p.rawEnabled && p.rawActive
                && !p.rawCull && p.rendererAlpha > 0 && p.rawColor.a > 0 && p.activeRenderers > 0 && p.nonzeroAlphaPixels > 100),
                "all three actual actor RTs contain rendered pixels and bind visible P1 RawImage");
        }
        IEnumerator CheckPlayerBoxDeviceInput(PlayerBoxReport report)
        {
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            var previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                foreach (var page in new[] { NativeMenuPage.Vehicles, NativeMenuPage.Skins })
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                    hud.SetMenuPage(page); hud.SelectMenuIndex(0);
                    while (hud.MenuTransitionActive) yield return null;
                    yield return null;
                    foreach (var key in new[] { Key.RightArrow, Key.LeftArrow, Key.UpArrow, Key.DownArrow })
                    {
                        int before = hud.SelectedMenuIndex, step = key == Key.RightArrow ? 1 : -1;
                        int audioCalls = playerBoxObservedCalls;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null; yield return null;
                        int expected = (before + step + hud.PlayerBoxChoiceCount) % hud.PlayerBoxChoiceCount;
                        PlayerBoxRequire(report, hud.SelectedMenuIndex == expected, page + " actual keyboard started " + key);
                        PlayerBoxRequire(report, playerBoxObservedCalls == audioCalls + 1,
                            page + " actual started move invokes one original audio clip callback");
                        CapturePlayerBoxAudio(report, page, true);
                        if (key == Key.RightArrow)
                        {
                            yield return new WaitForSecondsRealtime(2f);
                            PlayerBoxRequire(report, hud.SelectedMenuIndex == expected, page + " actual two-second hold moves once");
                            PlayerBoxRequire(report, playerBoxObservedCalls == audioCalls + 1, page + " held input keeps one audio callback");
                            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.UpArrow, Key.RightArrow)); yield return null; yield return null;
                            PlayerBoxRequire(report, hud.SelectedMenuIndex == expected, page + " performed diagonal change leaves started-only selection unchanged");
                        }
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                    }
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                OpenModeCharacters(); hud.SelectMenuIndex(Array.IndexOf(selectableCharacters, vehicleRoster.CharacterIndexOf("reimu")));
                while (hud.MenuTransitionActive) yield return null;
                yield return null;
                int characterCalls = playerBoxObservedCalls;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow)); yield return null; yield return null;
                PlayerBoxRequire(report, playerBoxObservedCalls == characterCalls + 1, "Characters actual device move invokes one audio callback");
                CapturePlayerBoxAudio(report, NativeMenuPage.Characters, false);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                OpenModeCharacters(); hud.SelectMenuIndex(Array.IndexOf(selectableCharacters, vehicleRoster.CharacterIndexOf("reimu")));
                while (hud.MenuTransitionActive) yield return null;
                foreach (var action in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    var beforePage = hud.MenuPage;
                    bool cancel = action == 2 || action == 3;
                    var expectedClip = cancel ? NativePlayerBoxSoundData.PlayerBoxCancel : beforePage == NativeMenuPage.Characters
                        ? NativePlayerBoxSoundData.CharacterSelect : NativePlayerBoxSoundData.PlayerBoxSelect;
                    int beforeCalls = playerBoxObservedCalls;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(cancel ? Key.X : Key.Z)); yield return null; yield return null;
                    PlayerBoxRequire(report, playerBoxObservedCalls == beforeCalls + 1, beforePage + " actual submit/cancel invokes one original audio callback");
                    CapturePlayerBoxAudio(report, beforePage, expectedClip);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                    while (hud.MenuTransitionActive) yield return null;
                }
                report.realInputDeviceEvents = true;
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = previousBackground;
            }
#else
            throw new PlatformNotSupportedException("PlayerBox verification requires player Input System device events");
#endif
        }
        void CapturePlayerBoxAudio(PlayerBoxReport report, NativeMenuPage page, bool playerBox)
            => CapturePlayerBoxAudio(report, page, playerBox ? NativePlayerBoxSoundData.Move : NativePlayerBoxSoundData.CharacterMove);
        void CapturePlayerBoxAudio(PlayerBoxReport report, NativeMenuPage page, AudioClip expected)
        {
            var source = playerBoxObservedSource;
            var clip = playerBoxObservedClip;
            var sample = new PlayerBoxAudioSample { page = page.ToString(), clip = clip ? clip.name : "", calls = playerBoxObservedCalls, wrapperCalls = hud.OriginalSelectionMoveCalls, verificationPlayback = NativeAudioRuntime.UiVerificationPlayback,
                sourceInstanceId = source ? source.GetInstanceID() : 0, sourceEnabled = source && source.enabled,
                sourcePlaying = source && source.isPlaying, mixerGroup = source && source.outputAudioMixerGroup ? source.outputAudioMixerGroup.name : "",
                sourceClip = source && source.clip ? source.clip.name : "", muted = NativeAudioRuntime.Muted, matchesOriginalClip = clip == expected };
            report.audio.Add(sample);
            PlayerBoxRequire(report, sample.matchesOriginalClip && source,
                page + " actual PlayOneShot AudioClip argument and production AudioSource reference");
            PlayerBoxRequire(report, sample.sourceEnabled && sample.mixerGroup == "UI", page + " actual PlayOneShot routes original clip to enabled UI mixer source");
        }
        IEnumerator CheckPlayerBoxRecordMatrix(PlayerBoxReport report)
        {
            foreach (var mode in new[] { NativeSessionMode.GrandPrix, NativeSessionMode.TimeTrials, NativeSessionMode.QuickRace, NativeSessionMode.CustomRace })
            foreach (string recordState in new[] { "positive", "absent", "zero" })
            {
                double total = recordState == "zero" ? 0 : 123.456;
                NativeSaveStore.Data.timeTrialRecords = recordState == "absent" ? new NativeSaveData.TimeTrialRecord[0] : new[] {
                    new NativeSaveData.TimeTrialRecord { trackId = "forest", characterId = "marisa", vehicleId = "broom", bestSeconds = total,
                        lapSeconds = new[] { 11d, 22d, 33d, 44d, 13.456 } } };
                NativeSaveStore.Data.cupRecords = recordState == "absent" ? new NativeSaveData.CupRecord[0] : new[] {
                    new NativeSaveData.CupRecord { cupId = "moriyacup", characterId = "reimu", vehicleId = "kart", bestSeconds = total,
                        stationSeconds = new[] { 11d, 22d, 33d, 57.456 } } };
                selectedMode = mode; OpenModeCup();
                while (hud.MenuTransitionActive) yield return null;
                yield return null; yield return new WaitForEndOfFrame();
                VerifyPlayerBoxRecordCase(report, mode, recordState, "freshLoaded", false);
                if (mode == NativeSessionMode.GrandPrix)
                {
                    OpenModeCharacters(); yield return null; OpenModeCup();
                }
                else
                {
                    OpenModeTracks(); hud.SelectMenuIndex(0); yield return null; yield return new WaitForEndOfFrame();
                    var panels = hud.ReadSelectionRecordPanels();
                    PlayerBoxRequire(report, panels[0].activeSelf == (mode == NativeSessionMode.TimeTrials && recordState == "positive")
                        && !panels[1].activeSelf, mode + "/" + recordState + " circuit record mode/positive-total gate");
                    ExecuteFullModeAction(NativeMenuAction.BackToCup);
                }
                yield return null; yield return new WaitForEndOfFrame();
                VerifyPlayerBoxRecordCase(report, mode, recordState, mode == NativeSessionMode.GrandPrix ? "moduleReentry" : "backToCup", mode == NativeSessionMode.TimeTrials);
                if (recordState == "positive")
                    ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, "playerbox-record-" + mode + ".png"));
                if (mode == NativeSessionMode.TimeTrials)
                {
                    OpenModeCharacters(); yield return null; OpenModeCup(); yield return null; yield return new WaitForEndOfFrame();
                    PlayerBoxRequire(report, !hud.ReadSelectionRecordPanels()[0].activeSelf, "TT " + recordState + " Characters->Cup Loaded clears prior circuit record");
                    OpenModeTracks(); hud.SelectMenuIndex(1); yield return null; yield return new WaitForEndOfFrame();
                    ExecuteFullModeAction(NativeMenuAction.BackToCup); yield return null; yield return new WaitForEndOfFrame();
                    PlayerBoxRequire(report, !hud.ReadSelectionRecordPanels()[0].activeSelf, "TT unrecordedMountain->Cup preserves hidden panel");
                }
            }
            PlayerBoxRequire(report, report.records.Count == 24, "24 mode/record/lifecycle production panel matrix rows");
        }
        void VerifyPlayerBoxRecordCase(PlayerBoxReport report, NativeSessionMode mode, string state, string stage, bool retainCircuit)
        {
            var panels = hud.ReadSelectionRecordPanels();
            report.records.Add(new PlayerBoxRecordCase { mode = mode.ToString(), record = state, stage = stage, panels = panels });
            bool positive = state == "positive", trial = retainCircuit && positive && mode == NativeSessionMode.TimeTrials, cup = positive && mode == NativeSessionMode.GrandPrix;
            PlayerBoxRequire(report, panels[0].activeSelf == trial && panels[1].activeSelf == cup, mode + "/" + state + "/" + stage + " source panel lifetime");
            var panel = trial ? panels[0] : cup ? panels[1] : null;
            if (panel == null) return;
            var expected = trial ? new[] { 11d, 22d, 33d, 44d, 13.456 } : new[] { 11d, 22d, 33d, 57.456 };
            PlayerBoxRequire(report, panel.total == "<mspace=24>02:03.456</mspace>" && panel.splits.SequenceEqual(expected.Select(t => NativeRaceHud.OriginalTimeText(t, 16)))
                && panel.vehicle == NativeLocalization.Get(trial ? "VEHICLE_BROOM" : "VEHICLE_KART")
                && panel.characterSprite == vehicleRoster.CharacterIconAt(vehicleRoster.CharacterIndexOf(trial ? "marisa" : "reimu")).name,
                mode + "/" + stage + " source total24/splits16/character/vehicle leaves");
        }
        PlayerBoxPreviewSample CapturePlayerBoxPreview(NativeMenuPage page)
        {
            var image = hud.PlayerBoxPreviewImage;
            var renderers = selectionPreviewModel.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var sample = new PlayerBoxPreviewSample { page = page.ToString(), character = vehicleRoster.CharacterIdAt(previewCharacter),
                vehicle = vehicleRoster.VehicleIdAt(previewVehicle), skin = vehicleRoster.SkinPhraseAt(previewCharacter, previewSkin),
                textureCreated = selectionPreviewTexture.IsCreated(), rawMatchesCamera = image.texture == selectionPreviewCamera.targetTexture,
                texture = image.texture ? image.texture.name : "", shader = image.material.shader.name, material = image.material.name,
                rawEnabled = image.enabled, rawActive = image.gameObject.activeInHierarchy, rawCull = image.canvasRenderer.cull,
                rawColor = image.color, rendererAlpha = image.canvasRenderer.GetInheritedAlpha(), activeRenderers = renderers.Length,
                cameraPosition = selectionPreviewCamera.transform.position, cameraForward = selectionPreviewCamera.transform.forward,
                cameraCullingMask = selectionPreviewCamera.cullingMask, near = selectionPreviewCamera.nearClipPlane, far = selectionPreviewCamera.farClipPlane,
                actorBoundsCenter = bounds.center, actorBoundsSize = bounds.size, viewportCenter = selectionPreviewCamera.WorldToViewportPoint(bounds.center),
                actorRotation = selectionPreviewModel.transform.localRotation, rawScreenRect = page == NativeMenuPage.Characters ? hud.PreviewRect : hud.VehiclePreviewRect,
                screenshot = "playerbox-preview-" + page + ".png", renderTextureCapture = "playerbox-rt-" + page + ".png" };
            var previous = RenderTexture.active;
            var pixels = new Texture2D(selectionPreviewTexture.width, selectionPreviewTexture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = selectionPreviewTexture;
                pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply();
                var colors = pixels.GetPixels32(); sample.pixelCount = colors.Length;
                sample.nonzeroAlphaPixels = colors.Count(c => c.a > 0); sample.opaquePixels = colors.Count(c => c.a >= 250);
                File.WriteAllBytes(Path.Combine(reportDirectory, sample.renderTextureCapture), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Destroy(pixels); }
            sample.renderFieldsCapture = "playerbox-render-fields-" + page + ".json";
            string renderFields = SelectionPreviewOriginalPropertiesReport();
            File.WriteAllText(Path.Combine(reportDirectory, sample.renderFieldsCapture), renderFields);
            var fieldReport = JsonUtility.FromJson<NativePreviewRenderProbe.Report>(renderFields);
            sample.renderFieldsPassed = fieldReport != null && fieldReport.fieldCheckPassed;
            ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, sample.screenshot));
            return sample;
        }
    }
}
