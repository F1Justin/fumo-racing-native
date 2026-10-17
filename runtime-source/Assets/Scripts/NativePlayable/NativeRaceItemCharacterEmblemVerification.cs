using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ItemEmblemResult { public bool passed; }

        IEnumerator CheckItemCharacterEmblemsLive()
        {
            QualitySettings.vSyncCount = 0;
            Screen.SetResolution(1280, 800, false);
            Directory.CreateDirectory(reportDirectory);
            yield return null;
            SetVisualCapture("race", true);
            Time.timeScale = 1f;
            if (raceConfig == null) raceConfig = new NativeSessionConfig();
            raceConfig.laps = 5;
            hud.RaceLapCount = 5;
            for (int i = 0; i < racers.Length; i++)
            {
                progress[i] = new NativeCircuitProgress(gates.Select(g => g.position).ToArray(), 5);
                lapSeconds[i].Clear(); lapTimestamps[i] = 0;
                finishSeconds[i] = 0; finishTimes[i] = 0;
                racers[i].enabled = false;
                var ai = racers[i].GetComponent<NativeKartOriginalAI>();
                if (ai) ai.enabled = false;
            }
            raceClock.StartAt(Time.timeSinceLevelLoadAsDouble - 2);
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return NativeItemCharacterEmblemVerification.Run(this, reportDirectory);
            var path = Path.Combine(reportDirectory, "item-character-emblem-report.json");
            bool passed = File.Exists(path) && JsonUtility.FromJson<ItemEmblemResult>(File.ReadAllText(path)).passed;
            Debug.Log("NATIVE_ITEM_EMBLEM_RESULT passed=" + passed);
            Application.Quit(passed && errorCount == 0 ? 0 : 2);
        }
    }
}
