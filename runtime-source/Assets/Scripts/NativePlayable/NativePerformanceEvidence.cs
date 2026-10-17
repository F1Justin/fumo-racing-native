using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
namespace Fumo.NativePlayable
{
    // Environment snapshots are deliberately infrequent and taken after rendering.
    public sealed class NativePerformanceEvidence : MonoBehaviour
    {
        [Serializable] sealed class CameraEvidence
        {
            public string name, renderingPath, requestedRenderingPath, targetFormat, bloom, ambientOcclusion, colorGrading;
            public Rect pixelRect;
            public int pixelWidth, pixelHeight, targetWidth, targetHeight, targetSamples;
            public bool hdr, enabled, finalBlit, fallbackCallback, allowMSAA, deferredFogEnabled;
        }
        [Serializable] sealed class QualityEvidence
        {
            public int preset, textureMipmapLimit, antiAliasing, maximumLODLevel, pixelLights, shadowResolution, shadowCascades, skinWeights;
            public string shadows, shadowProjection, anisotropicFiltering, shadowmaskMode;
            public float lodBias, shadowDistance;
            public bool softParticles, softVegetation, realtimeReflectionProbes;
            public NativeSaveData.Setting[] savedSettings;
            public NativeSceneSettings.EffectReport appliedSceneSettings;
        }
        [Serializable] sealed class Snapshot
        {
            public int frame, framebufferWidth, framebufferHeight, targetFrameRate, vSyncCount, racerCount;
            public int metalDrawableWidth = -1, metalDrawableHeight = -1;
            public double engineReportedRefreshRate;
            public float realtime, batteryLevel;
            public string utc, version, track, scene, renderingThreadingMode, batteryStatus, thermalState = "unknown: not exposed by Unity", powerMode = "unknown: capture with host sidecar";
            public string drawableSemantics = "Screen.width/height are Unity framebuffer dimensions. Native CAMetalDrawable dimensions unavailable here; -1 means unmeasured. Engine refresh rate is display-mode metadata, not measured presentation cadence.";
            public string[] racers;
            public CameraEvidence[] cameras;
            public QualityEvidence quality;
            public long hdrLutRebuilds, hdrCurveUploads, hdrDispatches;
            public bool hdrLutCacheDisabled, grassEarlyVisibility, bloomFiveTapEnabled, bloomUberFoldEnabled, sceneFog;
            public string fogMode;
            public float fogDensity, fogStart, fogEnd;
            public long bloomFiveTapPasses, bloomOriginalDownsamplePasses, bloomUberFoldPasses, bloomUberOriginalTentPasses;
            public int particleDiscoveryCount, particleApplyCount, particleRegistered, particleLive, particleMaterials;
        }
        [Serializable] sealed class LightSample
        {
            public int frame, vSyncCount, targetFrameRate, framebufferWidth, framebufferHeight;
            public float realtime;
            public bool focused, bloomUberFoldEnabled, bloomFiveTapEnabled, finalBlit;
            public long bloomUberFoldPasses, bloomUberOriginalTentPasses;
        }
        [Serializable] sealed class Report
        {
            public string unityVersion, operatingSystem, deviceModel, cpu, gpu, graphicsApi, sourceManifest, sourceManifestPath, samplingMode, commandLine;
            public LightSample[] samples;
            public string timerSemantics = "FrameTiming samples arrive asynchronously; zero/missing GPU times are absent in CSV. Use Metal captures for pass costs and native drawable verification. Heavy profiling/validation results are not final performance acceptance.";
            public Snapshot[] snapshots;
        }
        NativeRace race;
        string directory;
        readonly List<Snapshot> snapshots = new List<Snapshot>();
        readonly List<LightSample> samples = new List<LightSample>(720);
        Report report;
        bool crashSafe, flushed;
        public void Configure(NativeRace target, string path)
        {
            race = target; directory = path;
            crashSafe = Array.IndexOf(Environment.GetCommandLineArgs(), "--performance-crash-safe") >= 0;
            var manifest = Path.GetFullPath(Path.Combine(Application.dataPath,
                Application.platform == RuntimePlatform.OSXPlayer ? "Resources/build-source.json" : "../build-source.json"));
            string digest = "unavailable";
            if (File.Exists(manifest))
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(manifest))
                    digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            report = new Report { unityVersion = Application.unityVersion, operatingSystem = SystemInfo.operatingSystem,
                deviceModel = SystemInfo.deviceModel, cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), sourceManifest = digest, sourceManifestPath = manifest,
                samplingMode = crashSafe ? "crash-safe-diagnostic" : "buffered-lightweight-score",
                commandLine = string.Join(" ", Environment.GetCommandLineArgs()) };
            StartCoroutine(Capture());
        }
        IEnumerator Capture()
        {
            yield return new WaitForEndOfFrame();
            snapshots.Add(TakeSnapshot()); // Startup evidence precedes the scored interval.
            while (race && samples.Count < 720 && !flushed)
            {
                RecordState();
                if (crashSafe) { snapshots.Add(TakeSnapshot()); Write(); }
                yield return new WaitForSecondsRealtime(5);
                yield return new WaitForEndOfFrame();
            }
        }
        public void RecordState()
        {
            var layer = race.raceCamera ? race.raceCamera.GetComponent<PostProcessLayer>() : null;
            samples.Add(new LightSample { frame = Time.frameCount, realtime = Time.realtimeSinceStartup,
                vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
                framebufferWidth = Screen.width, framebufferHeight = Screen.height, focused = Application.isFocused,
                bloomUberFoldEnabled = BloomFilterDiagnostics.UberFoldEnabled, bloomFiveTapEnabled = BloomFilterDiagnostics.FiveTapEnabled,
                bloomUberFoldPasses = BloomFilterDiagnostics.UberFoldPasses,
                bloomUberOriginalTentPasses = BloomFilterDiagnostics.UberOriginalTentPasses,
                finalBlit = layer && layer.finalBlitToCameraTarget });
        }
        public void Flush()
        {
            if (report == null || flushed) return;
            flushed = true;
            if (race) { RecordState(); snapshots.Add(TakeSnapshot()); }
            Write();
        }
        void OnApplicationQuit() { Flush(); }
        Snapshot TakeSnapshot()
        {
            var cameras = Camera.allCameras;
            var entries = new List<CameraEvidence>(cameras.Length);
            foreach (var camera in cameras)
            {
                var rt = camera.targetTexture;
                var layer = camera.GetComponent<PostProcessLayer>();
                var fallback = camera.GetComponent<NativeFallbackPostEffect>();
                var evidence = new CameraEvidence {
                    name = camera.name, renderingPath = camera.actualRenderingPath.ToString(), requestedRenderingPath = camera.renderingPath.ToString(),
                    pixelRect = camera.pixelRect, pixelWidth = camera.pixelWidth, pixelHeight = camera.pixelHeight,
                    targetWidth = rt ? rt.width : 0, targetHeight = rt ? rt.height : 0,
                    targetSamples = rt ? rt.antiAliasing : -1, targetFormat = rt ? rt.graphicsFormat.ToString() : "backbuffer: native format unmeasured",
                    hdr = camera.allowHDR, allowMSAA = camera.allowMSAA, enabled = camera.isActiveAndEnabled,
                    deferredFogEnabled = layer && layer.fog != null && layer.fog.enabled,
                    finalBlit = layer && layer.finalBlitToCameraTarget, fallbackCallback = fallback && fallback.isActiveAndEnabled
                };
                if (layer && layer.isActiveAndEnabled)
                {
                    evidence.bloom = JsonUtility.ToJson(layer.GetSettings<Bloom>());
                    evidence.ambientOcclusion = JsonUtility.ToJson(layer.GetSettings<AmbientOcclusion>());
                    evidence.colorGrading = JsonUtility.ToJson(layer.GetSettings<ColorGrading>());
                }
                entries.Add(evidence);
            }
            var names = new string[race.racers.Length];
            for (int i = 0; i < names.Length; i++) names[i] = race.racers[i] ? race.racers[i].name : "missing";
            var sceneSettings = race.raceCamera ? race.raceCamera.GetComponent<NativeSceneSettings>() : null;
            int discoveries = 0, applies = 0, registered = 0, live = 0, materials = 0;
            foreach (var registry in UnityEngine.Object.FindObjectsByType<NativeParticleQualityRegistry>(FindObjectsSortMode.None))
            { discoveries += registry.DiscoveryCount; applies += registry.SettingsApplyCount; registered += registry.RegisteredRendererCount; live += registry.LiveRendererCount; materials += registry.TemporaryMaterialCount; }
            return new Snapshot {
                frame = Time.frameCount, realtime = Time.realtimeSinceStartup, utc = DateTime.UtcNow.ToString("O"),
                version = Application.version, track = race.trackId, scene = race.gameObject.scene.name,
                framebufferWidth = Screen.width, framebufferHeight = Screen.height,
                targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount,
                renderingThreadingMode = SystemInfo.renderingThreadingMode.ToString(), engineReportedRefreshRate = Screen.currentResolution.refreshRateRatio.value,
                batteryLevel = SystemInfo.batteryLevel, batteryStatus = SystemInfo.batteryStatus.ToString(), racerCount = names.Length, racers = names,
                cameras = entries.ToArray(), hdrLutRebuilds = ColorGradingLutDiagnostics.RebuildCount,
                hdrCurveUploads = ColorGradingLutDiagnostics.CurveUploadCount, hdrDispatches = ColorGradingLutDiagnostics.DispatchCount,
                hdrLutCacheDisabled = ColorGradingLutDiagnostics.CacheDisabled,
                grassEarlyVisibility = Shader.IsKeywordEnabled("FUMO_GRASS_EARLY_VISIBILITY"),
                bloomFiveTapEnabled = BloomFilterDiagnostics.FiveTapEnabled,
                bloomFiveTapPasses = BloomFilterDiagnostics.FiveTapPasses,
                bloomOriginalDownsamplePasses = BloomFilterDiagnostics.OriginalDownsamplePasses,
                bloomUberFoldEnabled = BloomFilterDiagnostics.UberFoldEnabled,
                bloomUberFoldPasses = BloomFilterDiagnostics.UberFoldPasses,
                bloomUberOriginalTentPasses = BloomFilterDiagnostics.UberOriginalTentPasses,
                sceneFog = RenderSettings.fog, fogMode = RenderSettings.fogMode.ToString(),
                fogDensity = RenderSettings.fogDensity, fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance,
                particleDiscoveryCount = discoveries, particleApplyCount = applies,
                particleRegistered = registered, particleLive = live,
                particleMaterials = materials,
                quality = new QualityEvidence {
                    preset = QualitySettings.GetQualityLevel(), textureMipmapLimit = QualitySettings.globalTextureMipmapLimit,
                    antiAliasing = QualitySettings.antiAliasing, maximumLODLevel = QualitySettings.maximumLODLevel,
                    pixelLights = QualitySettings.pixelLightCount, shadowResolution = (int)QualitySettings.shadowResolution,
                    shadowCascades = QualitySettings.shadowCascades, skinWeights = (int)QualitySettings.skinWeights,
                    shadows = QualitySettings.shadows.ToString(), shadowProjection = QualitySettings.shadowProjection.ToString(),
                    anisotropicFiltering = QualitySettings.anisotropicFiltering.ToString(), shadowmaskMode = QualitySettings.shadowmaskMode.ToString(),
                    lodBias = QualitySettings.lodBias, shadowDistance = QualitySettings.shadowDistance,
                    softParticles = QualitySettings.softParticles, softVegetation = QualitySettings.softVegetation,
                    realtimeReflectionProbes = QualitySettings.realtimeReflectionProbes,
                    savedSettings = NativeSaveStore.Data.settings, appliedSceneSettings = sceneSettings ? sceneSettings.Report : null
                }
            };
        }
        void Write()
        {
            report.snapshots = snapshots.ToArray();
            report.samples = samples.ToArray();
            File.WriteAllText(Path.Combine(directory, "performance-environment.json"), JsonUtility.ToJson(report, true));
        }
    }
}
