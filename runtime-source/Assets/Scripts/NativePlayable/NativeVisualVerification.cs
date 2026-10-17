using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeVisualVerification
    {
        [Serializable] public sealed class Fixture
        {
            public string name, page;
            public Vector3 position;
            public Quaternion rotation;
            public float fieldOfView;
            public bool showHud;
            public bool overridePlayer;
            public Vector3 playerPosition;
            public Quaternion playerRotation;
        }

        [Serializable] public sealed class Suite { public Fixture[] fixtures; }
        [Serializable] sealed class GrassState
        {
            public string material, windTexture;
            public float windEnabled;
            public Vector4 windData;
        }
        [Serializable] sealed class CaptureReport
        {
            public string version, platform, graphics, trackId;
            public int width, height, errors, quality, antialiasing, pixelLights;
            public float shadowDistance, lodBias;
            public string shadows;
            public string qualityName, shadowResolution, shadowProjection, shadowmaskMode, skinWeights, anisotropicFiltering;
            public int shadowCascades, vSyncCount, maximumLODLevel, particleRaycastBudget;
            public float shadowNearPlaneOffset;
            public bool softParticles, softVegetation, realtimeReflectionProbes, billboardsFaceCameraPosition, streamingMipmapsActive;
            public bool passed, grassVisibilityComparison, bloomFiveTapComparison, bloomUberFoldComparison;
            public long bloomFiveTapPasses, bloomUberFoldPasses;
            public string[] unsupportedShaders;
            public Fixture[] fixtures;
            public GrassState[] grasses;
            public string[] grassProbeUsages;
        }

        public static IEnumerator Run(NativeRace race, string directory, string fixturePath)
        {
            Directory.CreateDirectory(directory);
            var menu = race.menuPresentation.menuCamera;
            var camera = race.raceCamera;
            var player = race.racers[0].transform;
            Fixture view(string name, string page, Vector3 position, Quaternion rotation, float fov, bool hud = false)
                => new Fixture { name = name, page = page, position = position, rotation = rotation, fieldOfView = fov, showHud = hud };
            Fixture look(string name, Vector3 position, Vector3 target, float fov)
                => view(name, "race", position, Quaternion.LookRotation(target - position), fov);
            var suite = string.IsNullOrEmpty(fixturePath) ? new Suite { fixtures = new[]
            {
                view("menu", "menu", menu.transform.position, menu.transform.rotation, menu.fieldOfView, true),
                view("start", "race", camera.transform.position, camera.transform.rotation, camera.fieldOfView, true),
                view("start-scene", "race", camera.transform.position, camera.transform.rotation, camera.fieldOfView),
                view("pause", "pause", camera.transform.position, camera.transform.rotation, camera.fieldOfView, true),
                look("kart", player.TransformPoint(new Vector3(3, 2.1f, -3.5f)), player.position + Vector3.up, 40),
                look("grass-items", race.trackId == "forest" ? new Vector3(-246, 3, 81) : race.route[30] + Vector3.up * 5,
                    race.trackId == "forest" ? new Vector3(-254.53f, 1.2f, 95.50f) : race.route[32] + Vector3.up, 50),
                look("bend", race.route[9] + Vector3.up * 3, race.route[12] + Vector3.up, 60),
                look("straight", race.route[16] + Vector3.up * 3, race.route[18] + Vector3.up, 60),
                look("sky", race.route[16] + Vector3.up * 3, race.route[16] + new Vector3(0, 35, 35), 60)
            }} : JsonUtility.FromJson<Suite>(File.ReadAllText(fixturePath));
            if (string.IsNullOrEmpty(fixturePath) && race.trackId == "mountains")
                suite.fixtures = suite.fixtures.Concat(new[] {
                    look("waterfall", race.route[89] + Vector3.up * 7, race.route[88] + Vector3.up * 3, 65)
                }).ToArray();
            if (string.IsNullOrEmpty(fixturePath) && race.trackId == "purplefield")
                suite.fixtures = suite.fixtures.Concat(new[] {
                    look("field-overview", race.route[23] + Vector3.up * 18, race.route[28], 65)
                }).ToArray();
            if (string.IsNullOrEmpty(fixturePath) && race.trackId == "wintermistylake")
            {
                suite.fixtures = suite.fixtures.Concat(new[] {
                    look("frozen-lake", race.route[50] + Vector3.up * 10, race.route[53], 65)
                }).ToArray();
                foreach (var portal in UnityEngine.Object.FindObjectsOfType<NativeTrackTeleportVolume>())
                    if (portal.target) suite.fixtures = suite.fixtures.Concat(new[] {
                        look("portal-" + portal.name, portal.target.position + Vector3.up * 3,
                            portal.target.position + portal.target.forward * 10 + Vector3.up * 2, 65)
                    }).ToArray();
            }
            if (suite == null || suite.fixtures == null || suite.fixtures.Length == 0)
                throw new InvalidDataException("Visual fixture suite is empty");
            File.WriteAllText(Path.Combine(directory, "fixtures.json"), JsonUtility.ToJson(suite, true));
            string[] comparisonArgs = Environment.GetCommandLineArgs();
            bool compareGrass = Array.IndexOf(comparisonArgs, "--grass-visibility-comparison") >= 0;
            bool compareBloom = Array.IndexOf(comparisonArgs, "--bloom-five-tap-comparison") >= 0;
            bool compareUber = Array.IndexOf(comparisonArgs, "--bloom-uber-fold-comparison") >= 0;
            // Optimization picture comparisons must use the saved normal-play quality.
            if (compareGrass || compareBloom || compareUber) NativeUserSettings.ApplyScene(race);
            foreach (var fixture in suite.fixtures)
            {
                if (compareGrass) Shader.DisableKeyword("FUMO_GRASS_EARLY_VISIBILITY");
                if (compareBloom) UnityEngine.Rendering.PostProcessing.BloomFilterDiagnostics.FiveTapEnabled = false;
                if (compareUber) UnityEngine.Rendering.PostProcessing.BloomFilterDiagnostics.UberFoldEnabled = false;
                if (string.IsNullOrEmpty(fixture.name) || fixture.name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fixture.name.Contains("/"))
                    throw new InvalidDataException("Invalid fixture name");
                race.SetVisualCapture(fixture.page, fixture.showHud);
                if (fixture.overridePlayer) race.racers[0].ResetAt(fixture.playerPosition, fixture.playerRotation, false);
                var active = fixture.page == "menu" ? menu : camera;
                active.transform.SetPositionAndRotation(fixture.position, fixture.rotation);
                active.fieldOfView = fixture.fieldOfView;
                // Let the post-process stack and Canvas settle while gameplay and material time stay frozen.
                yield return new WaitForSecondsRealtime(0.3f);
                yield return new WaitForEndOfFrame();
                var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(directory, fixture.name + ".png"), screenshot.EncodeToPNG());
                UnityEngine.Object.Destroy(screenshot);
                Debug.Log("NATIVE_VISUAL_CAPTURE " + fixture.name);
                if (compareGrass || compareBloom || compareUber)
                {
                    // A/A and B/B controls expose temporal noise independently of the change.
                    yield return new WaitForEndOfFrame();
                    var control = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(directory, fixture.name + ".control.png"), control.EncodeToPNG());
                    UnityEngine.Object.Destroy(control);
                    if (compareGrass) Shader.EnableKeyword("FUMO_GRASS_EARLY_VISIBILITY");
                    if (compareBloom) UnityEngine.Rendering.PostProcessing.BloomFilterDiagnostics.FiveTapEnabled = true;
                    if (compareUber) UnityEngine.Rendering.PostProcessing.BloomFilterDiagnostics.UberFoldEnabled = true;
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return new WaitForEndOfFrame();
                    var experiment = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(directory, fixture.name + ".experiment.png"), experiment.EncodeToPNG());
                    UnityEngine.Object.Destroy(experiment);
                    yield return new WaitForEndOfFrame();
                    var experimentControl = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(directory, fixture.name + ".experiment-control.png"), experimentControl.EncodeToPNG());
                    UnityEngine.Object.Destroy(experimentControl);
                    if (compareGrass) Shader.DisableKeyword("FUMO_GRASS_EARLY_VISIBILITY");
                    if (compareBloom) UnityEngine.Rendering.PostProcessing.BloomFilterDiagnostics.FiveTapEnabled = false;
                if (compareUber) UnityEngine.Rendering.PostProcessing.BloomFilterDiagnostics.UberFoldEnabled = false;
                }
            }
            var unsupported = UnityEngine.Object.FindObjectsOfType<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material && material.shader && !material.shader.isSupported)
                .Select(material => material.shader.name).Distinct().OrderBy(name => name).ToArray();
            var report = new CaptureReport
            {
                grassVisibilityComparison = compareGrass, bloomFiveTapComparison = compareBloom, bloomUberFoldComparison = compareUber,
                bloomUberFoldPasses = UnityEngine.Rendering.PostProcessing.BloomFilterDiagnostics.UberFoldPasses,
                bloomFiveTapPasses = UnityEngine.Rendering.PostProcessing.BloomFilterDiagnostics.FiveTapPasses,
                version = Application.version, trackId = race.trackId, platform = SystemInfo.operatingSystem, graphics = SystemInfo.graphicsDeviceName,
                width = Screen.width, height = Screen.height, errors = race.VisualErrorCount,
                quality = QualitySettings.GetQualityLevel(), antialiasing = QualitySettings.antiAliasing,
                pixelLights = QualitySettings.pixelLightCount, shadowDistance = QualitySettings.shadowDistance,
                lodBias = QualitySettings.lodBias, shadows = QualitySettings.shadows.ToString(),
                qualityName = QualitySettings.names[QualitySettings.GetQualityLevel()],
                shadowResolution = QualitySettings.shadowResolution.ToString(), shadowProjection = QualitySettings.shadowProjection.ToString(),
                shadowmaskMode = QualitySettings.shadowmaskMode.ToString(), skinWeights = QualitySettings.skinWeights.ToString(),
                anisotropicFiltering = QualitySettings.anisotropicFiltering.ToString(), shadowCascades = QualitySettings.shadowCascades,
                vSyncCount = QualitySettings.vSyncCount, maximumLODLevel = QualitySettings.maximumLODLevel,
                particleRaycastBudget = QualitySettings.particleRaycastBudget, shadowNearPlaneOffset = QualitySettings.shadowNearPlaneOffset,
                softParticles = QualitySettings.softParticles, softVegetation = QualitySettings.softVegetation,
                realtimeReflectionProbes = QualitySettings.realtimeReflectionProbes,
                billboardsFaceCameraPosition = QualitySettings.billboardsFaceCameraPosition, streamingMipmapsActive = QualitySettings.streamingMipmapsActive,
                grasses = UnityEngine.Object.FindObjectsOfType<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials).Where(material => material && material.shader.name == "Fumo/NativeGrass")
                    .Distinct().Select(material => new GrassState {
                        material = material.name, windEnabled = material.GetFloat("_featureWind"),
                        windData = material.GetVector("_WindData"),
                        windTexture = material.GetTexture("_WindTex") ? material.GetTexture("_WindTex").name : "missing"
                    }).ToArray(),
                grassProbeUsages = UnityEngine.Object.FindObjectsOfType<Renderer>(true)
                    .Where(renderer => renderer.sharedMaterials.Any(material => material && material.shader.name == "Fumo/NativeGrass"))
                    .Select(renderer => renderer.lightProbeUsage.ToString()).Distinct().OrderBy(value => value).ToArray(),
                passed = race.VisualErrorCount == 0 && unsupported.Length == 0, unsupportedShaders = unsupported, fixtures = suite.fixtures
            };
            File.WriteAllText(Path.Combine(directory, "visual-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_VISUAL_RESULT captures=" + suite.fixtures.Length + " errors=" + report.errors);
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
