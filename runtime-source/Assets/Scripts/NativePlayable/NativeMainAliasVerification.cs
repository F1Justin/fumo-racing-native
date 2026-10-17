using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class MainAliasFrameObservation
        {
            public string sourcePath, mutation, captureFile, menuPage, requiredCondition;
            public int selectedMenuIndex, screenWidth, screenHeight;
            public bool requiredInObservedSourceState = true;
            public NativeMainPromptSource.State confirmPrompt, cancelPrompt;
            public int sourceFrame, sampleFrame, instanceId;
            public Rect screenRect;
            public bool identityBound, duplicateWriter;
            public NativeAuthoredUi.RequiredLeafSample leaf;
            public NativeUiCanvas.SourceGraphicSample source;
        }
        [Serializable] sealed class MainAliasDifferential
        {
            public string sourcePath, mutation, baselineFile, mutationFile;
            public int baselineFrame, mutationFrame, changedInsideROI, changedOutsideROI;
            public bool fixtureStable, blankOrHiddenRejected, baselineContributionObserved, zeroContributionRejected;
            public string suppressedFile;
            public int changedWhenMutatedLeafSuppressed, suppressedFrame;
            public bool suppressedIdentityConsistent;
            public string contributionConclusion;
        }
        [Serializable] sealed class MainAliasVerificationReport
        {
            public string evidenceCategory = "native-observation";
            public bool visualParityAccepted = false, sourceDenominatorComplete = false, matchedOriginalFrames = false;
            public MainAliasFrameObservation[] frames;
            public MainAliasDifferential[] differentials;
            public bool allObservedPromptStatesClosed;
            public int sourceStateConditionalHiddenCount;
            public string[] checks;
        }
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
        MainAliasVerificationReport mainAliasObservationReport;
        IEnumerator CaptureMainAliasVerification()
        {
            var writer = hud.MainWriterCanvas;
            var frames = new List<MainAliasFrameObservation>(); var diffs = new List<MainAliasDifferential>();
            var checks = new List<string>(); var paths = NativeAuthoredUi.RequiredMenuFieldPaths("MainMenu").Where(p => p != "menu2/Canvas/MainMenu/discordserverbutton").ToArray();
            if (writer == null) { requiredMenuFieldChecks.Add("FAIL Main alias producer canvas absent"); yield break; }
            var oldHook = writer.SourceRenderFixtureHook; IDisposable mutationRestore = null;
            string mutationPath = null, mutationKind = null; bool applied = false, suppressMutatedLeaf = false; IDisposable suppressionRestore = null;
            writer.SourceRenderFixtureHook = canvas =>
            {
                if (oldHook != null) oldHook(canvas);
                if (suppressionRestore != null) { suppressionRestore.Dispose(); suppressionRestore = null; }
                if (mutationRestore != null) { mutationRestore.Dispose(); mutationRestore = null; }
                applied = false;
                Graphic graphic;
                if (mutationPath != null && canvas.TryGetSourceGraphic(mutationPath, out graphic))
                { mutationRestore = NativeMainAliasEvidence.Mutate(graphic, mutationKind); applied = true;
                    if (suppressMutatedLeaf) suppressionRestore = NativeMainAliasEvidence.Mutate(graphic, "disabled"); }
            };
            try
            {
                foreach (string path in paths)
                {
                    mutationPath = null;
                    yield return null; yield return new WaitForEndOfFrame();
                    Graphic graphic;
                    var prompt = path == "menu2/Canvas/MainMenu/Prompts/AText/icon" ? hud.MainConfirmPromptState
                        : path == "menu2/Canvas/MainMenu/Prompts/BText/icon" ? hud.MainCancelPromptState : null;
                    if (prompt != null && prompt.closed && !prompt.spriteFound)
                    {
                        bool absent = !writer.TryGetSourceGraphic(path, out graphic);
                        var hidden = MainAliasObservation(writer, path, "source-state-hidden", null);
                        hidden.requiredInObservedSourceState = false;
                        hidden.requiredCondition = "TIPromptUI.Set source sprite lookup miss closes icon GameObject";
                        frames.Add(hidden);
                        checks.Add((absent ? "PASS " : "FAIL ") + "Main legal source-state hidden icon alias absence " + path);
                        continue;
                    }
                    if (!writer.TryGetSourceGraphic(path, out graphic)) { checks.Add("FAIL Main actual alias " + path); continue; }
                    var baselineIdentity = writer.SourceSample(path); int baselineFrame = Time.frameCount;
                    bool bound = baselineIdentity.frame == baselineFrame && baselineIdentity.instanceId == graphic.GetInstanceID() && !baselineIdentity.duplicateWriter;
                    checks.Add((bound ? "PASS " : "FAIL ") + "Main source/render current exact alias " + path);
                    var baselineObservation = MainAliasObservation(writer, path, "baseline", null);
                    checks.Add((baselineObservation.leaf != null && baselineObservation.leaf.fieldEligible ? "PASS " : "FAIL ") + "Main actual alias baseline leaf fields " + path);
                    var rect = MainAliasPixelRect(graphic); var baseline = MainAliasCapture();
                    string key = "main-alias-" + Array.IndexOf(paths, path); string baselineFile = key + "-baseline.png";
                    File.WriteAllBytes(Path.Combine(reportDirectory, baselineFile), baseline.EncodeToPNG());
                    baselineObservation.captureFile = baselineFile; frames.Add(baselineObservation);
                    var kinds = new List<string> { "disabled", "alpha-zero", "opaque-cover" };
                    if (graphic is TMPro.TMP_Text || graphic is Text) kinds.Add("blank-text");
                    else if (graphic is RawImage) kinds.Add("blank-texture");
                    foreach (string kind in kinds)
                    {
                        mutationPath = path; mutationKind = kind; suppressMutatedLeaf = false;
                        yield return null; yield return new WaitForEndOfFrame();
                        var changed = MainAliasCapture(); string changedFile = key + "-" + kind + ".png";
                        File.WriteAllBytes(Path.Combine(reportDirectory, changedFile), changed.EncodeToPNG());
                        var observation = MainAliasObservation(writer, path, kind, changedFile); frames.Add(observation);
                        var a = baseline.GetPixels32(); var b = changed.GetPixels32(); int inside = 0, outside = 0;
                        if (a.Length == b.Length) for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i]))
                        { int x = i % baseline.width, y = i / baseline.width; if (rect.Contains(new Vector2(x + .5f, y + .5f))) inside++; else outside++; }
                        bool stable = bound && applied && observation.identityBound && observation.instanceId == baselineObservation.instanceId
                            && observation.screenRect == baselineObservation.screenRect && baseline.width == changed.width && baseline.height == changed.height && outside == 0;
                        bool fieldRejected = observation.leaf != null && !observation.leaf.fieldEligible;
                        if (kind == "disabled" || kind == "alpha-zero" || kind == "blank-text")
                            checks.Add((applied && fieldRejected ? "PASS " : "FAIL ") + "Main actual render mutation field rejection " + kind + " " + path);
                        int suppressedDifference = -1, suppressedFrame = -1; string suppressedFile = null; bool zeroContribution = false, suppressedIdentityConsistent = false;
                        if (kind == "blank-texture" || kind == "opaque-cover")
                        {
                            suppressMutatedLeaf = true;
                            yield return null; yield return new WaitForEndOfFrame();
                            var suppressedObservation = MainAliasObservation(writer, path, kind + "-leaf-suppressed", null);
                            suppressedFrame = suppressedObservation.sampleFrame;
                            suppressedIdentityConsistent = suppressedObservation.identityBound && suppressedObservation.instanceId == observation.instanceId
                                && suppressedObservation.screenRect == observation.screenRect;
                            var suppressed = MainAliasCapture(); suppressedFile = key + "-" + kind + "-leaf-suppressed.png";
                            File.WriteAllBytes(Path.Combine(reportDirectory, suppressedFile), suppressed.EncodeToPNG());
                            suppressedObservation.captureFile = suppressedFile; frames.Add(suppressedObservation);
                            var c = suppressed.GetPixels32(); int other = 0; suppressedDifference = 0;
                            if (b.Length == c.Length)
                            {
                                for (int i = 0; i < b.Length; i++) if (!b[i].Equals(c[i]))
                                { int x = i % changed.width, y = i / changed.width; if (rect.Contains(new Vector2(x + .5f, y + .5f))) suppressedDifference++; else other++; }
                            }
                            else { suppressedDifference = -1; }
                            zeroContribution = applied && observation.identityBound && suppressedIdentityConsistent
                                && changed.width == suppressed.width && changed.height == suppressed.height && b.Length == c.Length && other == 0 && suppressedDifference == 0;
                            UnityEngine.Object.Destroy(suppressed); suppressMutatedLeaf = false;
                        }
                        diffs.Add(new MainAliasDifferential { sourcePath = path, mutation = kind, baselineFile = baselineFile, mutationFile = changedFile,
                            baselineFrame = baselineFrame, mutationFrame = observation.sampleFrame, changedInsideROI = inside, changedOutsideROI = outside,
                            fixtureStable = stable, blankOrHiddenRejected = fieldRejected, zeroContributionRejected = zeroContribution,
                            suppressedFile = suppressedFile, changedWhenMutatedLeafSuppressed = suppressedDifference,
                            suppressedFrame = suppressedFrame, suppressedIdentityConsistent = suppressedIdentityConsistent,
                            contributionConclusion = zeroContribution ? "rejected-zero-current-leaf-contribution" : !stable ? "unknown-dynamic-or-unbound-pair"
                                : inside > 0 ? "native-baseline-contribution-observed" : "unknown-baseline-zero-contribution",
                            baselineContributionObserved = kind != "opaque-cover" && stable && inside > 0 });
                        UnityEngine.Object.Destroy(changed);
                    }
                    mutationPath = null;
                    if (suppressionRestore != null) { suppressionRestore.Dispose(); suppressionRestore = null; }
                    if (mutationRestore != null) { mutationRestore.Dispose(); mutationRestore = null; }
                    UnityEngine.Object.Destroy(baseline);
                }
            }
            finally { writer.SourceRenderFixtureHook = oldHook; if (suppressionRestore != null) suppressionRestore.Dispose(); if (mutationRestore != null) mutationRestore.Dispose(); }
            var baselines = frames.Where(frame => frame.mutation == "baseline").ToArray();
            var hiddenFrames = frames.Where(frame => frame.mutation == "source-state-hidden").ToArray();
            var observedPaths = baselines.Concat(hiddenFrames).ToArray();
            int eligibleCount = paths.Length - hiddenFrames.Length;
            bool exactDenominator = observedPaths.Length == paths.Length && observedPaths.Select(frame => frame.sourcePath).Distinct().Count() == paths.Length
                && paths.OrderBy(path => path).SequenceEqual(observedPaths.Select(frame => frame.sourcePath).OrderBy(path => path))
                && baselines.All(frame => frame.identityBound) && baselines.Select(frame => frame.instanceId).Distinct().Count() == eligibleCount
                && hiddenFrames.All(frame => !frame.identityBound && !frame.requiredInObservedSourceState);
            checks.Add((exactDenominator ? "PASS " : "FAIL ") + "Main19 conditional exact source paths and eligible Graphic instances");
            requiredMenuFieldChecks.AddRange(checks);
            mainAliasObservationReport = new MainAliasVerificationReport { frames = frames.ToArray(), differentials = diffs.ToArray(), checks = checks.ToArray(),
                sourceStateConditionalHiddenCount = hiddenFrames.Length,
                allObservedPromptStatesClosed = frames.All(frame => frame.confirmPrompt != null && frame.confirmPrompt.closed && frame.cancelPrompt != null && frame.cancelPrompt.closed) };
            PersistMainAliasObservation();
        }
        void PersistMainAliasObservation()
        {
            if (mainAliasObservationReport != null)
                File.WriteAllText(Path.Combine(reportDirectory, "main-alias-native-observation.json"), JsonUtility.ToJson(mainAliasObservationReport, true));
        }
        void AppendMainPromptAliasObservation(string fixture, string path)
        {
            if (mainAliasObservationReport == null) return;
            var frame = MainAliasObservation(hud.MainWriterCanvas, path, "prompt-fixture-" + fixture, null);
            var prompt = path.EndsWith("AText/icon") ? frame.confirmPrompt : path.EndsWith("BText/icon") ? frame.cancelPrompt : null;
            if (prompt != null && prompt.closed && !prompt.spriteFound)
            {
                frame.requiredInObservedSourceState = false;
                frame.requiredCondition = "TIPromptUI.Set source sprite lookup miss closes icon GameObject";
            }
            mainAliasObservationReport.frames = mainAliasObservationReport.frames.Concat(new[] { frame }).ToArray();
            mainAliasObservationReport.allObservedPromptStatesClosed &= frame.confirmPrompt != null && frame.confirmPrompt.closed && frame.cancelPrompt != null && frame.cancelPrompt.closed;
        }
        MainAliasFrameObservation MainAliasObservation(NativeUiCanvas canvas, string path, string mutation, string file)
        {
            Graphic target; bool exists = canvas.TryGetSourceGraphic(path, out target);
            var source = canvas.SourceSample(path);
            return new MainAliasFrameObservation { menuPage = hud.MenuPage.ToString(), selectedMenuIndex = hud.SelectedMenuIndex,
                screenWidth = Screen.width, screenHeight = Screen.height,
                confirmPrompt = hud.MainConfirmPromptState, cancelPrompt = hud.MainCancelPromptState,
                requiredCondition = "source default visible; closed missing-sprite branch excludes only matching prompt icon",
                source = source, sourcePath = path, mutation = mutation, captureFile = file, sourceFrame = source.frame, sampleFrame = Time.frameCount,
                instanceId = exists ? target.GetInstanceID() : 0, screenRect = exists ? MainAliasPixelRect(target) : new Rect(), duplicateWriter = source.duplicateWriter,
                identityBound = exists && source.frame == Time.frameCount && source.instanceId == target.GetInstanceID() && !source.duplicateWriter,
                leaf = exists ? NativeAuthoredUi.ReadGraphicRequiredLeaf(path, target) : null };
        }
        static Texture2D MainAliasCapture()
        {
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false); texture.Apply(false); return texture;
        }
        static Rect MainAliasPixelRect(Graphic graphic)
        {
            var corners = new Vector3[4]; graphic.rectTransform.GetWorldCorners(corners);
            var canvas = graphic.canvas; var camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var points = corners.Select(p => RectTransformUtility.WorldToScreenPoint(camera, p)).ToArray();
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }
#endif
    }
}
