using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class RaceVisualAuditReport
        {
            public bool passed;
            public string source = "Ordinary Player audio and race clock; short live driving and pause capture";
            public string mode, track, item, frontIndicatorDetail;
            public string presentationPolicy = "automated-background-vsync-off";
            public float listenerVolume, speed, displacement;
            public int itemPickups, itemUses;
            public string[] checks;
            public int[] sourceStorageOrder;public Vector3[] startPositions;public bool actualDiagnosticOwner;
        }

        IEnumerator RaceAuditFrame(string name)
        {
            while (hud.MenuTransitionActive) yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, name + ".png"));
            yield return null;
        }

        IEnumerator CheckRaceVisualAudit()
        {
            var auditArgs = Environment.GetCommandLineArgs();
            if (Array.IndexOf(auditArgs, "--grid-spawn-check") >= 0) { yield return CheckGridSpawnLive(); yield break; }
            if (Array.IndexOf(auditArgs, "--particle-material-check") >= 0) { yield return CheckParticleMaterialsLive(); yield break; }
            Directory.CreateDirectory(reportDirectory);
            QualitySettings.vSyncCount = 0;
            yield return new WaitForSecondsRealtime(.5f);
            if(!CreateSourceDiagnosticOwnerBeforeBegin())throw new InvalidOperationException("Race audit actual diagnostic owner failed");
            Begin();
            Vector3 origin = racers[0].transform.position;
            while (phase == Phase.Countdown && countdown > 2.7f) yield return null;
            yield return RaceAuditFrame("countdown");
            while (phase == Phase.Countdown) yield return null;
            yield return new WaitForSecondsRealtime(.15f);
            yield return RaceAuditFrame("start");
            yield return new WaitForSecondsRealtime(1.5f);
            yield return RaceAuditFrame("pack");
            hud.OriginalFrontIndicatorsBound(out string frontDetail);
            yield return new WaitForSecondsRealtime(10.5f);
            yield return RaceAuditFrame("driving");
            var inventory = racers[0].GetComponent<NativeKartItemInventory>();
            var report = new RaceVisualAuditReport {
                sourceStorageOrder=NativeLocalRosterOrder.Read(raceConfig),startPositions=(Vector3[])starts.Clone(),actualDiagnosticOwner=sourceInjectedDiagnosticOwner,mode = selectedMode.ToString(), track = trackId, frontIndicatorDetail = frontDetail,
                item = inventory.CurrentId, itemPickups = inventory.GiveCount, itemUses = inventory.UseCount,
                listenerVolume = AudioListener.volume, speed = racers[0].Speed,
                displacement = Vector3.Distance(origin, racers[0].transform.position)
            };
            bool sourceGridBound=sourceGridPositions.Length==8&&sourceGridRotations.Length==8;
            bool sourceHumanTail=sourceInjectedDiagnosticOwner&&NativeLocalRosterOrder.SourceIndex(raceConfig,0)==racers.Length-1;
            bool minimapBound = hud.OriginalMinimapBound(racers.Length, out string minimapDetail);
            Pause();
            yield return RaceAuditFrame("pause");
            bool paused = phase == Phase.Paused && Time.timeScale == 0;
            Resume();
            yield return new WaitForSecondsRealtime(.2f);
            bool resumed = phase == Phase.Racing && Time.timeScale == 1;
            report.passed = !NativeAudioRuntime.Muted && report.listenerVolume > 0 && report.displacement > 10 && paused && resumed && minimapBound && sourceGridBound && sourceHumanTail;
            report.checks = new[] {
                (!NativeAudioRuntime.Muted && report.listenerVolume > 0 ? "PASS " : "FAIL ") + "ordinary unmuted audio output",
                (report.displacement > 10 ? "PASS " : "FAIL ") + "real race movement exceeds ten metres",
                (paused && resumed ? "PASS " : "FAIL ") + "normal pause and resume",
                (minimapBound ? "PASS " : "FAIL ") + minimapDetail,
                (sourceGridBound?"PASS ":"FAIL ")+"complete original eight starting points",
                (sourceHumanTail?"PASS ":"FAIL ")+"actual diagnostic PlayerInput uses original human-tail source order"
            };
            yield return null;
            File.WriteAllText(Path.Combine(reportDirectory, "race-visual-audit.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
