using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializePerformanceSeed()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--performance-report") < 0) return;
            if (int.TryParse(Argument(args, "--performance-seed"), out int seed)) UnityEngine.Random.InitState(seed);
        }
        void InitializePerformanceRecording(string[] args)
        {
            if (Array.IndexOf(args, "--performance-report") < 0) return;
            if (verificationRun) throw new InvalidOperationException("Performance recording requires the normal production flow; do not combine with self-test/check flags.");
            string directory = Argument(args, "--report-dir") ?? Path.Combine(Application.persistentDataPath, "performance", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
            Directory.CreateDirectory(directory);
            // Settings/scene initialization has already completed. This override is session-only.
            if (int.TryParse(Argument(args, "--performance-framerate"), out int target))
                Application.targetFrameRate = target <= 0 ? -1 : Mathf.Clamp(target, 30, 300);
            captureTestScreenshots = false;
            // Opt-in experiment; normal gameplay retains the existing grass variant.
            if (Array.IndexOf(args, "--performance-grass-visibility") >= 0)
                Shader.EnableKeyword("FUMO_GRASS_EARLY_VISIBILITY");
            else
                Shader.DisableKeyword("FUMO_GRASS_EARLY_VISIBILITY");
            if (Array.IndexOf(args, "--performance-final-blit") >= 0)
            {
                if (Array.IndexOf(args, "--performance-legacy-post-copy") >= 0)
                    throw new InvalidOperationException("Final blit experiment cannot be combined with a legacy image-effect copy.");
                // Race startup can replace the menu camera and its post-process layer.
                // Apply to the camera actually rendering, including later local views.
                gameObject.AddComponent<NativePerformanceFinalBlit>();
            }
            var audit = gameObject.AddComponent<NativeMotionAudit>();
            audit.Configure(this, directory, true);
            gameObject.AddComponent<NativePerformanceEvidence>().Configure(this, directory);
            if (Array.IndexOf(args, "--performance-callback-probe") >= 0)
            { gameObject.AddComponent<NativeFrameProbe>(); NativeFrameProbe.Enable(); }
            if (Array.IndexOf(args, "--performance-autostart") >= 0) StartCoroutine(PerformanceAutoStart());
            string sync = Argument(args, "--performance-sync");
            if (!string.IsNullOrEmpty(sync)) StartCoroutine(PerformanceSyncAfterStartup(sync));
            string probe = Argument(args, "--performance-fixed-probe");
            if (!string.IsNullOrEmpty(probe)) StartCoroutine(PerformanceFixedProbe(args, probe, directory));
            if (float.TryParse(Argument(args, "--performance-duration"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float duration) && duration > 0 && !float.IsInfinity(duration))
                StartCoroutine(PerformanceStopAfter(Mathf.Min(duration, 3600), audit, Array.IndexOf(args, "--performance-quit") >= 0));
            Debug.Log("NATIVE_PERFORMANCE_RECORDING directory=" + directory);
        }
        IEnumerator PerformanceSyncAfterStartup(string mode)
        {
            yield return new WaitForSecondsRealtime(9);
            if (Net && Net.Open) throw new InvalidOperationException("Controlled sync test requires offline play; network clock remains unchanged.");
            PerformanceApplySync(mode);
            GetComponent<NativePerformanceEvidence>()?.RecordState();
        }
        static void PerformanceApplySync(string mode)
        {
            switch (mode)
            {
                case "software120": QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120; break;
                case "uncapped": QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; break;
                case "vsync": QualitySettings.vSyncCount = 1; Application.targetFrameRate = -1; break;
                default: throw new ArgumentException("Unknown performance sync mode " + mode);
            }
            Debug.Log("NATIVE_PERFORMANCE_SYNC frame=" + Time.frameCount + " mode=" + mode);
        }
        RenderTexture performanceProbeTarget;
        void PerformanceDrawProbePreview()
        {
            if (performanceProbeTarget && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), performanceProbeTarget, ScaleMode.StretchToFill, false);
        }
        IEnumerator PerformanceFixedProbe(string[] args, string kind, string directory)
        {
            // Explicit mechanism diagnostic after ordinary initialization, never normal FPS acceptance.
            yield return new WaitForSecondsRealtime(10);
            if (Net && Net.Open) throw new InvalidOperationException("Fixed probe requires offline play.");
            var path = Argument(args, "--performance-probe-fixture");
            var fixture = JsonUtility.FromJson<NativeVisualVerification.Suite>(File.ReadAllText(path)).fixtures[0];
            SetVisualCapture("race", false);
            raceCamera.transform.SetPositionAndRotation(fixture.position, fixture.rotation);
            raceCamera.fieldOfView = fixture.fieldOfView;
            var layer = raceCamera.GetComponent<PostProcessLayer>();
            RenderTexture target = null;
            if (Argument(args, "--performance-probe-rt-scale") == "2")
            {
                target = new RenderTexture(Screen.width * 2, Screen.height * 2, 24, RenderTextureFormat.ARGBHalf);
                target.Create(); raceCamera.targetTexture = target; performanceProbeTarget = target;
            }
            string[] phases = kind == "sync" ? new[] { "software120", "uncapped", "vsync", "vsync", "uncapped", "software120" }
                : Array.IndexOf(args, "--performance-probe-reverse") >= 0 ? new[] { "B", "A", "A", "B" } : new[] { "A", "B", "B", "A" };
            foreach (string mode in phases)
            {
                PerformanceApplySync(kind == "sync" ? mode : "uncapped");
                if (kind == "uber") BloomFilterDiagnostics.UberFoldEnabled = mode == "B";
                else if (kind == "final-blit") layer.finalBlitToCameraTarget = mode == "B";
                else if (kind != "sync") throw new ArgumentException("Unknown fixed probe " + kind);
                GetComponent<NativePerformanceEvidence>()?.RecordState();
                Debug.Log("NATIVE_FIXED_PROBE frame=" + Time.frameCount + " kind=" + kind + " phase=" + mode);
                yield return new WaitForSecondsRealtime(20);
            }
            GetComponent<NativeMotionAudit>()?.Flush();
            GetComponent<NativePerformanceEvidence>()?.Flush();
            if (Array.IndexOf(args, "--performance-quit") >= 0) Application.Quit();
            if (target) { performanceProbeTarget = null; raceCamera.targetTexture = null; target.Release(); Destroy(target); }
        }
        IEnumerator PerformanceAutoStart()
        {
            // Use the real menu-to-race producer, after ordinary startup initialization.
            yield return new WaitForSecondsRealtime(5);
            Begin();
        }
        IEnumerator PerformanceStopAfter(float seconds, NativeMotionAudit audit, bool quit)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (audit) { audit.Flush(); audit.enabled = false; }
            GetComponent<NativePerformanceEvidence>()?.Flush();
            if (quit) Application.Quit();
        }
    }
}
