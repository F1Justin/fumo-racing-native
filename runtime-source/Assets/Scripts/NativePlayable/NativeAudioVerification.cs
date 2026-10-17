using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeAudioVerification
    {
        [Serializable] public sealed class Report
        {
            public bool passed;
            public string source = "Unity Player; original catalog, mixer state and exact whole PCM segment reads";
            public bool muted;
            public int musicEntries, vehicleEntries, mixerGroups;
            public string[] checks;
            public string outputMode = "muted-audio-rules-scheduling";
            public NativeAudioObservation.Snapshot audioStart, audioEnd;
        }
        public static Report LastResult { get; private set; }
        public static IEnumerator StartVerification(string directory)
        {
            var report = new Report { passed = true, muted = NativeAudioRuntime.Muted,
                audioStart = NativeAudioObservation.Capture("muted-audio-rules-scheduling") };
            var checks = new List<string>();
            Action<bool, string> check = (pass, name) => { report.passed &= pass; checks.Add((pass ? "PASS " : "FAIL ") + name); };
            var catalog = NativeAudioRuntime.Catalog;
            check(catalog, "original catalog loaded");
            if (catalog)
            {
                report.musicEntries = catalog.music.Length; report.vehicleEntries = catalog.vehicles.Length; report.mixerGroups = catalog.groups.Length;
                check(catalog.music.Length == 11 && catalog.groups.Length == 7, "eleven original music entries and seven mixer groups");
                var ids = new HashSet<string>();
                foreach (var entry in catalog.music)
                {
                    check(ids.Add(entry.id) && entry.clip && !string.IsNullOrEmpty(entry.title), "unique original music " + entry.id);
                    if (!entry.clip) continue;
                    entry.clip.LoadAudioData();
                    float deadline = Time.realtimeSinceStartup + 2;
                    while (entry.clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline) yield return null;
                    check(entry.loopStart >= 0 && entry.loopStart < entry.loopEnd && entry.loopEnd <= entry.clip.samples
                        && entry.endStart >= 0 && entry.endStart <= entry.clip.samples, "original sample bounds " + entry.id);
                    string error;
                    check(NativeAudioRuntime.VerifySegments(entry.id, out error), "exact whole PCM segment " + entry.id + " " + error);
                }
                foreach (string vehicle in new[] {"Kart", "Bike", "Skate", "CleanerBot", "ShoppingCart", "Broom", "Genji"})
                    check(catalog.FindVehicle("", vehicle) != null, "original vehicle audio " + vehicle);
                var names = new[] {"sfxVolume", "musicVolume", "ambientVolume", "engineVolume", "itemVolume", "explosionVolume", "uiVolume"};
                NativeAudioRuntime.ApplyVolumes();
                for (int i = 0; i < names.Length; i++)
                {
                    float actual;
                    check(catalog.mixer.GetFloat(names[i], out actual) && Mathf.Abs(actual - NativeUserSettings.VolumeDb(NativeUserSettings.GetFloat("volumeGroup" + i))) < .001f,
                        "actual mixer " + names[i]);
                }
                var races = UnityEngine.Object.FindObjectsOfType<NativeRace>();
                var enabledRaces = new bool[races.Length];
                for (int i = 0; i < races.Length; i++) { enabledRaces[i] = races[i].enabled; races[i].enabled = false; }
                try { yield return NativeAudioRuntime.VerifyScheduling(check); }
                finally { for (int i = 0; i < races.Length; i++) if (races[i]) races[i].enabled = enabledRaces[i]; }
                check(NativeAudioRuntime.VerificationSilent(), "verification remains silent with music sources disabled");
                check(catalog.selectionTransitionTime == 1.5f && catalog.selectionIn != null && catalog.selectionOut != null, "original layered selection transition");
            }
            report.audioEnd = NativeAudioObservation.Capture(report.outputMode);
            report.checks = checks.ToArray(); LastResult = report;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "audio-rules-report.json"), JsonUtility.ToJson(report, true) + "\n");
        }
    }
}
