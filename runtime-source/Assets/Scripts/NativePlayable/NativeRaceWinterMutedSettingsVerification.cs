using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class WinterMutedSettingsReport
        {
            public bool passed, accepted, ordinaryAudibleAccepted, runtimeMuted, splashActive;
            public string error, evidenceKind = "muted-production-settings-policy";
            public string assemblySHA256, outputMode;
            public string[] commandLineArgs;
            public float listenerAtCheckerEntry;
            public WinterCheck[] checks;
            public NativeAudioObservation.Snapshot audioStart, audioEnd;
        }
        internal IEnumerator CheckWinterMutedSettingsLive()
        {
            winterAudit = new WinterRuntimeReport();
            var report = new WinterMutedSettingsReport {
                assemblySHA256 = WinterHash(File.ReadAllBytes(typeof(NativeRace).Assembly.Location)),
                outputMode = "muted-production-settings-policy", commandLineArgs = Environment.GetCommandLineArgs(),
                listenerAtCheckerEntry = AudioListener.volume,
                audioStart = NativeAudioObservation.Capture("muted-production-settings-policy")
            };
            float previousVolume = AudioListener.volume, previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 1; AudioListener.volume = 0;
                report.runtimeMuted = NativeAudioRuntime.Muted; report.splashActive = StartupSplashActive;
                WinterRequire(report.runtimeMuted && !StartupSplash && !report.splashActive,
                    "settings-audio.muted-initialization-without-splash", "muted-production-settings-policy");
                NativeSceneSettingsAudioVerification.AfterMutedApply(this,
                    (passed,id,detail)=>WinterRequire(passed,id,"muted-production-settings-policy",detail));
            }
            catch(Exception error) { report.error = error.ToString(); }
            yield return null;
            try
            {
                NativeSceneSettingsAudioVerification.AfterMutedApply(this,
                    (passed,id,detail)=>WinterRequire(passed,id,"muted-production-settings-policy",detail));
            }
            catch(Exception error) { report.error = error.ToString(); }
            finally
            {
                report.audioEnd = NativeAudioObservation.Capture(report.outputMode);
                AudioListener.volume = previousVolume; Time.timeScale = previousScale;
                report.checks = winterAudit.checks.ToArray();
                report.passed = string.IsNullOrEmpty(report.error) && report.runtimeMuted && report.checks.Length > 0 && report.checks.All(c=>c.passed);
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory,"winter-muted-settings-report.json"),JsonUtility.ToJson(report,true));
            }
            Debug.Log("WINTER_MUTED_SETTINGS_RESULT passed="+report.passed+" accepted=false");
            Application.Quit(report.passed?0:2);
        }
    }
}
