using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ResultsUiReference
        {
            public bool passed;
            public double seconds;
            public double[] laps;
            public NativeSessionSnapshot session;
        }
        [Serializable] sealed class ResultsUiReport
        {
            public bool passed, scriptedState = true, gameplayAcceptance = false;
            public string source = "Native TT results UI replay; no race completion acceptance", referencePath;
            public string[] checks, rendererDiagnostics;
            public NativeMenuEnvironment currentEnvironment, authoredRaceEnvironment;
            public string[] materialDiagnostics;
            public Vector4[] shaderGlobalSH;
            public string previewRenderingPath;
            public string[] controlledPreviewRenders;
            public NativeResultsAvatarSet.RenderObservation[] sourceAvatarRendering;
            public string lightDiagnosticsScope = "Active scene lights after the production avatar render; backend color handling excluded from functional acceptance";
            public ResultsLightDiagnostic[] lights;
            public double seconds;
            public float scaledDelay;
            public int textureWidth, textureHeight, opaquePixels, runtimeErrors;
            public string nextPage;
            public bool previewCleaned;
        }
        [Serializable] sealed class ResultsLightDiagnostic
        {
            public string hierarchy, scene, type, renderMode, shadows;
            public bool active, enabled, coversRenderT, resultsPointLight;
            public int cullingMask;
            public float intensity, range;
            public Vector3 worldPosition;
            public Color color;
        }
#if ENABLE_INPUT_SYSTEM
        IEnumerator CheckResultsUiReplay(string[] args)
        {
            var checks = new List<string>();
            var report = new ResultsUiReport { referencePath = Argument(args, "--results-reference") };
            Action<bool, string> check = (pass, name) => {
                checks.Add((pass ? "PASS " : "FAIL ") + name);
                Debug.Log("NATIVE_RESULTS_UI_CHECK " + pass + " " + name);
            };
            ResultsUiReference reference = null;
            if (!string.IsNullOrEmpty(report.referencePath))
            {
                if (File.Exists(report.referencePath)) reference = JsonUtility.FromJson<ResultsUiReference>(File.ReadAllText(report.referencePath));
                bool valid = reference != null && reference.passed && reference.seconds > 0 && reference.laps != null && reference.laps.Length == 5
                    && reference.session != null && reference.session.config.mode == NativeSessionMode.TimeTrials;
                check(valid, "stored Stage4C TT reference is a passed five-lap receipt");
                if (!valid)
                {
                    report.checks = checks.ToArray();
                    File.WriteAllText(Path.Combine(reportDirectory, "results-ui-report.json"), JsonUtility.ToJson(report, true));
                    Application.Quit(2);
                    yield break;
                }
            }
            else report.source = "Explicit scripted TT results UI state; no race completion acceptance";
            double seconds = reference == null ? 246.38 : reference.seconds;
            double[] laps = reference == null ? Enumerable.Repeat(seconds / 5, 5).ToArray() : reference.laps;
            report.seconds = seconds;
            selectedMode = NativeSessionMode.TimeTrials;
            if (reference != null)
            {
                var seat = reference.session.config.roster.First(s => s.isHuman);
                selectedCharacterIndex = vehicleRoster.CharacterIndexOf(seat.characterId);
                selectedVehicleIndex = vehicleRoster.VehicleIndexOf(seat.vehicleId);
                selectedSkinIndex = seat.skinIndex;
            }
            hud.SuppressPointerInput = true;
            var previousBackground = InputSystem.settings.backgroundBehavior;
            Keyboard keyboard = null;
            try
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                var inputs = new List<InputDevice>();
                yield return PrepareInjectedLocalHumansForRace(1, inputs, NativeSessionMode.TimeTrials);
                keyboard = inputs.OfType<Keyboard>().Single();
                float startDeadline = Time.realtimeSinceStartup + 10;
                while (phase != Phase.Racing && Time.realtimeSinceStartup < startDeadline) yield return null;
                check(phase == Phase.Racing && sourceRaceState.InRace, "production countdown enters racing with the actual keyboard owner");
                for (int i = 0; i < racers.Length; i++)
                {
                    racers[i].driving = false;
                    racers[i].GetComponent<NativeKartOriginalAI>().active = false;
                }
                raceClock.Reset();
                finishSeconds[0] = seconds;
                finishTimes[0] = (float)seconds;
                finishPlaces[0] = 1;
                lapSeconds[0] = laps.ToList();
                progressionCommitted = true;
                while (!progress[0].Finished) progress[0].Cross(progress[0].Target, true);
                RecordSourceNaturalFinish(0);
                check(sourceRaceState.EndedRace && resultsFlow.State == NativeResultsFlow.Screen.Waiting, "production EndRacer opens the delayed results lifecycle");
                yield return null;
                yield return new WaitForEndOfFrame();
                float started = Time.time;
                check(!hud.ResultsVisible, "rankcard waits for the source three-second delay");
                CaptureTestScreenshot("results-ui-before-delay.png");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Z));
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                check(phase == Phase.Finished && hud.MenuPage == NativeMenuPage.Results, "Next before the delay leaves results pending");
                float timeout = Time.realtimeSinceStartup + 5;
                while (!hud.ResultsVisible && Time.realtimeSinceStartup < timeout) yield return null;
                report.scaledDelay = Time.time - started;
                yield return null;
                yield return new WaitForEndOfFrame();
                check(hud.ResultsVisible && report.scaledDelay >= 2.9f, "rankcard opens after three scaled seconds");
                var resultsPreviewRoot = GameObject.Find("Original ResultPlayerUI avatar 0");
                var resultsPreviewCamera = resultsPreviewRoot ? resultsPreviewRoot.GetComponentInChildren<Camera>(true) : null;
                var resultsPreviewLight = resultsPreviewRoot ? resultsPreviewRoot.GetComponentInChildren<Light>(true) : null;
                var resultsPreviewTexture = resultsPreviewCamera ? resultsPreviewCamera.targetTexture : null;
                var resultsPreviewModel = resultsPreviewRoot ? resultsPreviewRoot.transform.Cast<Transform>()
                    .FirstOrDefault(node => node.GetComponentInChildren<Renderer>(true))?.gameObject : null;
                report.sourceAvatarRendering = sourceResultsAvatars.RenderingObservations;
                var ui = GameObject.Find("Original authored menus");
                var fumo = ui ? ui.transform.Find("GlobalHUD authored Canvas/Results/playersresult/ResultPlayerUI/fumo") : null;
                var image = fumo ? fumo.GetComponent<RawImage>() : null;
                var banner = ui ? ui.transform.Find("GlobalHUD authored Canvas/Results/playersresult/ResultPlayerUI/banner") : null;
                var bannerGroup = banner ? banner.GetComponent<CanvasGroup>() : null;
                check(bannerGroup && Mathf.Approximately(bannerGroup.alpha, .5f), "source banner CanvasGroup alpha is one half");
                var resultsCanvas = fumo ? fumo.GetComponentInParent<Canvas>() : null;
                var raceHudObject = GameObject.Find("Native original UI Canvas");
                var raceHudCanvas = raceHudObject ? raceHudObject.GetComponent<Canvas>() : null;
                check(resultsCanvas && raceHudCanvas && resultsCanvas.sortingOrder == 200 && resultsCanvas.sortingOrder > raceHudCanvas.sortingOrder, "Results canvas renders above the race HUD");
                check(image && image.gameObject.activeInHierarchy && image.texture == resultsPreviewTexture, "authored Fumo RawImage displays the results render texture");
                if (resultsPreviewTexture)
                {
                    report.textureWidth = resultsPreviewTexture.width;
                    report.textureHeight = resultsPreviewTexture.height;
                    var previous = RenderTexture.active;
                    var pixels = new Texture2D(report.textureWidth, report.textureHeight, TextureFormat.RGBA32, false);
                    try
                    {
                        RenderTexture.active = resultsPreviewTexture;
                        pixels.ReadPixels(new Rect(0, 0, report.textureWidth, report.textureHeight), 0, 0);
                        pixels.Apply();
                        report.opaquePixels = pixels.GetPixels32().Count(p => p.a > 32);
                    }
                    finally { RenderTexture.active = previous; Destroy(pixels); }
                }
                check(resultsPreviewRoot && resultsPreviewRoot.transform.position == Vector3.one * 1000 &&
                    resultsPreviewCamera && resultsPreviewCamera.cullingMask == LayerMask.GetMask("RenderT") &&
                    resultsPreviewLight && resultsPreviewLight.cullingMask == -1,
                    "source world origin, RenderT camera mask and default light mask are restored");
                var sourceModel = vehicleRoster.DriverModelAt(selectedCharacterIndex, selectedSkinIndex);
                var sourceRenderers = sourceModel.GetComponentsInChildren<Renderer>(true);
                var resultRenderers = resultsPreviewModel.GetComponentsInChildren<Renderer>(true);
                Func<Transform, Transform, string> relativePath = (node, root) => {
                    string path = "";
                    while (node != root) { path = node.name + (path.Length == 0 ? "" : "/" + path); node = node.parent; }
                    return path;
                };
                bool rendererStateMatches = resultsPreviewModel.layer == LayerMask.NameToLayer("RenderT");
                report.rendererDiagnostics = resultRenderers.Select(renderer => {
                    string path = relativePath(renderer.transform, resultsPreviewModel.transform);
                    var sourceRenderer = sourceRenderers.FirstOrDefault(candidate =>
                        relativePath(candidate.transform, sourceModel.transform) == path && candidate.GetType() == renderer.GetType());
                    bool active = renderer.gameObject.activeInHierarchy;
                    bool matches = sourceRenderer && renderer.reflectionProbeUsage == sourceRenderer.reflectionProbeUsage &&
                        (!active || (renderer.gameObject.layer == LayerMask.NameToLayer("RenderT") &&
                            renderer.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.Off));
                    rendererStateMatches &= matches;
                    return "path=" + (path.Length == 0 ? "<root>" : path) + " active=" + active +
                        " actual[layer=" + renderer.gameObject.layer + ",lightProbe=" + renderer.lightProbeUsage +
                        ",reflectionProbe=" + renderer.reflectionProbeUsage + "] source[" +
                        (sourceRenderer ? "layer=" + sourceRenderer.gameObject.layer + ",lightProbe=" + sourceRenderer.lightProbeUsage +
                            ",reflectionProbe=" + sourceRenderer.reflectionProbeUsage : "missing") + "] matches=" + matches;
                }).ToArray();
                check(rendererStateMatches,
                    "results active renderers use source RenderT and light probes off while retaining prefab reflection probes");
                report.currentEnvironment = NativeMenuEnvironment.Capture();
                report.authoredRaceEnvironment = menuPresentation ? menuPresentation.raceEnvironment : null;
                report.previewRenderingPath = resultsPreviewCamera.actualRenderingPath.ToString();

                report.lights = FindObjectsOfType<Light>(true)
                    .Where(light => (light.gameObject.activeInHierarchy && light.enabled) || light == resultsPreviewLight)
                    .Select(light => new ResultsLightDiagnostic {
                        hierarchy = light.transform.root.name + "/" + relativePath(light.transform, light.transform.root), scene = light.gameObject.scene.name,
                        type = light.type.ToString(), renderMode = light.renderMode.ToString(), shadows = light.shadows.ToString(),
                        active = light.gameObject.activeInHierarchy, enabled = light.enabled,
                        coversRenderT = (light.cullingMask & (1 << LayerMask.NameToLayer("RenderT"))) != 0,
                        resultsPointLight = light == resultsPreviewLight, cullingMask = light.cullingMask,
                        intensity = light.intensity, range = light.range, worldPosition = light.transform.position, color = light.color
                    }).ToArray();
                report.shaderGlobalSH = new[] { "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" }
                    .Select(Shader.GetGlobalVector).ToArray();
                report.materialDiagnostics = resultRenderers.SelectMany(renderer => renderer.sharedMaterials.Select(material =>
                    "renderer=" + relativePath(renderer.transform, resultsPreviewModel.transform) +
                    " material=" + (material ? material.name : "missing") +
                    (material ? " shader=" + material.shader.name + " supported=" + material.shader.isSupported +
                        " deferredPass=" + material.FindPass("DEFERRED") + " passCount=" + material.passCount +
                        " shaderLOD=" + material.shader.maximumLOD + " globalShaderLOD=" + Shader.globalMaximumLOD +
                        " graphicsTier=" + Graphics.activeTier + " renderTargets=" + SystemInfo.supportedRenderTargetCount +
                        " keywords=" + string.Join(",", material.shaderKeywords) + " lightmapIndex=" + renderer.lightmapIndex +
                        " color=" + (material.HasProperty("_Color") ? material.GetColor("_Color").ToString("R") : "none") : ""))).ToArray();
                report.controlledPreviewRenders = Array.Empty<string>();
                check(report.textureWidth == 512 && report.textureHeight == 512 && report.opaquePixels > 1000, "512-square source texture contains rendered character pixels");
                CaptureTestScreenshot("results-ui-tt-rankcard.png");
                yield return new WaitForSecondsRealtime(.3f);
                check(report.sourceAvatarRendering.Length == 1 && resultsPreviewCamera && !resultsPreviewCamera.enabled,
                    "production avatar renders once with its camera disabled after capture");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Z));
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return new WaitForEndOfFrame();
                report.nextPage = hud.MenuPage.ToString();
                check(phase == Phase.Menu && hud.MenuPage == NativeMenuPage.Characters && NativeGameSession.Current == null, "Next returns TT to Characters and clears the session");
                report.previewCleaned = !resultsPreviewRoot && !resultsPreviewCamera && !resultsPreviewModel && !resultsPreviewTexture && !resultsPreviewLight;
                check(report.previewCleaned, "Next destroys and clears the results preview");
                yield return new WaitForSecondsRealtime(.45f);
                yield return new WaitForEndOfFrame();
                CaptureTestScreenshot("results-ui-tt-next-characters.png");
                yield return new WaitForSecondsRealtime(.6f);
            }
            finally
            {
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = previousBackground;
            }
            report.runtimeErrors = errorCount;
            check(errorCount == 0, "runtime errors=0");
            report.checks = checks.ToArray();
            report.passed = checks.All(c => c.StartsWith("PASS ", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(reportDirectory, "results-ui-report.json"), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("NATIVE_RESULTS_UI_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }
#else
        IEnumerator CheckResultsUiReplay(string[] args) { throw new InvalidOperationException("Input System package is required for Results UI verification."); }
#endif
    }
}
