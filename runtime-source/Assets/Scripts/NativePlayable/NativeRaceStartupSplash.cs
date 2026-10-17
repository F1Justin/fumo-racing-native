using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        static bool startupSplashConsumed;
        NativeSplashAudioProducer startupSplash;
        public NativeSplashAudioProducer StartupSplash => startupSplash;
        public bool StartupSplashActive => startupSplash && startupSplash.IsRunning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStartupSplash() { startupSplashConsumed = false; }

        void BeginStartupSplash(string[] args)
        {
            bool audit = args.Contains("--winter-runtime-audit") || args.Contains("--winter-fresh190-self-test") || args.Contains("--winter-snowman-contact-self-test");
            bool diagnostics = verificationRun || args.Any(arg => arg.Contains("self-test") || arg.Contains("-audit")
                || arg.StartsWith("--check-", StringComparison.Ordinal) || arg.StartsWith("--verify-", StringComparison.Ordinal)
                || arg == "--visual-test" || arg == "--menu-display-test" || arg == "--skip-splash");
            if (startupSplashConsumed) return;
            startupSplashConsumed = true;
            if (!audit && (diagnostics || phase != Phase.Menu)) return;
            var prefab = NativeContent.Load<NativeSplashAudioProducer>("NativeMenu/Splash");
            if (!prefab) throw new InvalidOperationException("Original splash producer prefab missing");
            startupSplash = Instantiate(prefab, transform, false);
            string telemetryOperation = NativeTelemetry.Begin("ui.startup-splash");
            startupSplash.gameObject.SetActive(true);
            NativeAudioRuntime.StopMusic();
            NativeAudioRuntime.StopCountdown();
            nativeAudioContext = null;
            bool previousSuppressPointerInput = hud.SuppressPointerInput;
            hud.SuppressPointerInput = true;
            hud.SetVisible(false);
            if (itemHud) itemHud.SetVisible(false);
            var ufo = GetComponent<NativeUfoManager>();
            if (ufo) ufo.SetVisible(false);
            menuPresentation.SetVisible(false);
            int previousMask = raceCamera.cullingMask;
            var previousClear = raceCamera.clearFlags;
            var previousBackground = raceCamera.backgroundColor;
            raceCamera.cullingMask = 0;
            raceCamera.clearFlags = CameraClearFlags.SolidColor;
            raceCamera.backgroundColor = startupSplash.sourceCameraBackground;
            startupSplash.Begin(() =>
            {
                raceCamera.cullingMask = previousMask;
                raceCamera.clearFlags = previousClear;
                raceCamera.backgroundColor = previousBackground;
                hud.SetMenuPage(NativeMenuPage.Main);
                hud.SetVisible(true);
                menuPresentation.SetVisible(true);
                nativeAudioContext = null;
            }, () =>
            {
                if (hud != null) hud.SuppressPointerInput = previousSuppressPointerInput;
                NativeTelemetry.End(telemetryOperation, "ui.startup-splash", "completed");
            });
        }
    }
}
