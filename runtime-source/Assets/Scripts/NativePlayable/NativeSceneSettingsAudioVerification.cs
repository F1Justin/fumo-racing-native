using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeSceneSettingsAudioVerification
    {
        static AudioSource[] Sources(NativeRace race) => race.gameObject.scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<AudioSource>(true)).ToArray();
        static AudioSource[] EventAudio(NativeSnowmanDestructable snowman, bool destruction)
        {
            var calls = destruction ? snowman.onDestructed : snowman.onRespawn;
            return Enumerable.Range(0, calls.GetPersistentEventCount())
                .Select(i => calls.GetPersistentTarget(i)).OfType<AudioSource>().ToArray();
        }
        public static IEnumerator AfterNormalBegin(NativeRace race, Action<bool, string, string> check)
        {
            check(race && race.gameObject.scene.IsValid() && !NativeAudioRuntime.Muted,
                "settings-audio.normal-mode", "Caller has completed the production Begin path in ordinary mode");
            if (!race || NativeAudioRuntime.Muted) yield break;
            var snowmen = race.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NativeSnowmanDestructable>(true)).ToArray();
            var deaths = snowmen.SelectMany(s => EventAudio(s, true)).Distinct().ToArray();
            var respawns = snowmen.SelectMany(s => EventAudio(s, false)).Distinct().ToArray();
            var awakeDeaths = snowmen.Select(AwakeDeathSource).Where(a => a).ToArray();
            check(awakeDeaths.Length == 2, "settings-audio.awake-death-denominator2", "Original source13586/13591 death audio is owned by FX activation");
            check(snowmen.Length == 4 && deaths.Length == 2 && respawns.Length == 4,
                "settings-audio.winter-denominator", "4 snowmen, 2 destruction AudioSource, 4 respawn AudioSource");
            foreach (var source in respawns)
                check(source.enabled && source.gameObject.activeInHierarchy,
                    "settings-audio.normal-begin-respawn-source", source.name + " enabled after the real Begin settings application");
            var observedRespawns = new HashSet<AudioSource>();
            var damageStarted = new Dictionary<NativeSnowmanDestructable, float>();
            var listeners = new Dictionary<NativeSnowmanDestructable, UnityAction>();
            try
            {
                foreach (var snowman in snowmen)
                {
                    damageStarted.Add(snowman, Time.time);
                    UnityAction observe = () =>
                    {
                        float scaledAge = Time.time - damageStarted[snowman];
                        check(Mathf.Approximately(snowman.respawnTime, 10f) && scaledAge >= 9.95f,
                            "settings-audio.real-respawn-deadline", snowman.name + " elapsedScaled=" + scaledAge + " sourceRespawnTime=" + snowman.respawnTime);
                        foreach (var source in EventAudio(snowman, false))
                        {
                            observedRespawns.Add(source);
                            check(snowman.IsAlive && source.enabled && source.gameObject.activeInHierarchy && source.isPlaying,
                                "settings-audio.real-respawn-play", snowman.name + " production callback observed immediately after its Play");
                        }
                    };
                    listeners.Add(snowman, observe); snowman.onRespawn.AddListener(observe);
                    var awakeDeath=AwakeDeathSource(snowman);
                    if(awakeDeath)check(!awakeDeath.gameObject.activeInHierarchy && !awakeDeath.isPlaying,
                        "settings-audio.awake-death-dormant", snowman.name+" before production damage");
                    snowman.TakeDamage();
                    if(awakeDeath)
                    {
                        bool awakeOwned=awakeDeath.GetComponents<NativeAudioEmitter>().Any(e=>e.sources!=null && e.originalPlayOnAwake!=null
                            && Enumerable.Range(0,e.sources.Length).Any(i=>e.sources[i]==awakeDeath && i<e.originalPlayOnAwake.Length && e.originalPlayOnAwake[i]));
                        check(awakeDeath.enabled && awakeDeath.gameObject.activeInHierarchy && awakeDeath.isPlaying && awakeOwned && !awakeDeath.playOnAwake,
                            "settings-audio.awake-death-onenable-play", snowman.name+" original FX activation source actually playing");
                    }
                    foreach (var source in EventAudio(snowman, true))
                        check(source.enabled && source.gameObject.activeInHierarchy && source.isPlaying,
                            "settings-audio.death-onenable-then-play", snowman.name + " original FX activation restores its source before Play");
                }
                yield return new WaitForSeconds(10.2f);
                check(observedRespawns.Count == 4, "settings-audio.all-four-real-respawn-callbacks", "Four immediate source state observations after real scaled 10-second Invoke");
            }
            finally
            {
                foreach (var entry in listeners) if (entry.Key) entry.Key.onRespawn.RemoveListener(entry.Value);
            }
            yield return RepeatApply(race, check);
        }
        [Serializable] sealed class AwakeAudioRef { public string sourceId, path; }
        [Serializable] sealed class AwakeAudioBaseline { public AwakeAudioRef[] awakeDeathAudio; }
        static AudioSource AwakeDeathSource(NativeSnowmanDestructable snowman)
        {
            var resource=NativeContent.Load<TextAsset>("NativeVerification/WinterRuntimeSource");
            var rows=resource ? JsonUtility.FromJson<AwakeAudioBaseline>(resource.text)?.awakeDeathAudio : null;
            var row=rows?.SingleOrDefault(r=>r.path.StartsWith(snowman.name+"/",StringComparison.Ordinal));
            if(row==null)return null;
            var node=snowman.transform.Find(row.path.Substring(snowman.name.Length+1));
            return node ? node.GetComponent<AudioSource>() : null;
        }
        static long SamplePhaseError(AudioSource source, int initialSample, int finalSample, double elapsed)
        {
            int length = source.clip.samples;
            long expected = (initialSample + (long)Math.Round(elapsed * source.clip.frequency * Math.Abs(source.pitch))) % length;
            long difference = Math.Abs(finalSample - expected);
            return Math.Min(difference, length - difference);
        }
        static bool WasApplied(NativeAudioEmitter emitter) => (bool)typeof(NativeAudioEmitter)
            .GetField("applied", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(emitter);
        public static IEnumerator RepeatApply(NativeRace race, Action<bool, string, string> check)
        {
            if (!race || NativeAudioRuntime.Muted) yield break;
            var root = new GameObject("Scene settings audio scheduling probe");
            var clip = AudioClip.Create("Zero PCM settings scheduling probe", 44100 * 5, 1, 44100, false);
            try
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, race.gameObject.scene);
                var owner = root.AddComponent<AudioSource>(); owner.clip = clip; owner.playOnAwake = false;
                owner.loop = true; owner.volume = 0;
                var untouched = root.AddComponent<AudioSource>(); untouched.clip = clip;
                untouched.playOnAwake = false; untouched.enabled = false;
                var unownedPlaying = root.AddComponent<AudioSource>(); unownedPlaying.clip = clip;
                unownedPlaying.playOnAwake = false; unownedPlaying.loop = true; unownedPlaying.volume = 0; unownedPlaying.Play();
                var emitter = root.AddComponent<NativeAudioEmitter>();
                emitter.sources = new[] { owner }; emitter.originalPlayOnAwake = new[] { true };
                emitter.groups = new[] { -1 };
                var dormantNode = new GameObject("Dormant awake owner"); dormantNode.transform.SetParent(root.transform, false);
                dormantNode.SetActive(false);
                var dormantSource = dormantNode.AddComponent<AudioSource>(); dormantSource.clip = clip;
                dormantSource.playOnAwake = false; dormantSource.loop = true; dormantSource.volume = 0; dormantSource.enabled = false;
                var dormantEmitter = dormantNode.AddComponent<NativeAudioEmitter>();
                dormantEmitter.sources = new[] { dormantSource }; dormantEmitter.originalPlayOnAwake = new[] { true };
                dormantEmitter.groups = new[] { -1 };
                NativeAudioEmitter.ReapplyHierarchy(root, true);
                AudioSettings.GetDSPBufferSize(out int bufferFrames, out int bufferCount);
                int outputRate = AudioSettings.outputSampleRate;
                check(outputRate > 0, "settings-audio.dsp-output-rate", "Actual AudioSettings outputSampleRate is positive");
                if (outputRate <= 0) yield break;
                long tolerance = (long)Math.Ceiling((double)bufferFrames * (Math.Max(1, bufferCount) + 1) * clip.frequency / outputRate) + 2;
                float warmup = Mathf.Max(.2f, 4f * bufferFrames * (Math.Max(1, bufferCount) + 1) / outputRate);
                yield return new WaitForSecondsRealtime(warmup);
                int start = owner.timeSamples;
                check(owner.enabled && owner.isPlaying && start > tolerance * 2 && WasApplied(emitter),
                    "settings-audio.awake-probe-started", "Long zero PCM loop progressed beyond DSP buffering tolerance before reapply; scheduling evidence");
                for (int pass = 0; pass < 3; pass++)
                {
                    int ownerBefore = owner.timeSamples, unownedBefore = unownedPlaying.timeSamples;
                    double beforeDsp = AudioSettings.dspTime;
                    NativeSceneSettings.Apply(race);
                    double elapsed = AudioSettings.dspTime - beforeDsp;
                    long ownerError = SamplePhaseError(owner, ownerBefore, owner.timeSamples, elapsed);
                    long unownedError = SamplePhaseError(unownedPlaying, unownedBefore, unownedPlaying.timeSamples, elapsed);
                    string detail = "pass=" + pass + " dspElapsed=" + elapsed + " tolerance=" + tolerance
                        + " bufferFrames=" + bufferFrames + " bufferCount=" + bufferCount;
                    check(owner.enabled && owner.isPlaying && ownerError <= tolerance,
                        "settings-audio.repeated-apply-continuity", detail + " ownedPhaseError=" + ownerError);
                    check(unownedPlaying.enabled && unownedPlaying.isPlaying && unownedError <= tolerance,
                        "settings-audio.unowned-playing-continuity", detail + " unownedPhaseError=" + unownedError);
                    check(!dormantSource.enabled && !dormantSource.isPlaying && !WasApplied(dormantEmitter),
                        "settings-audio.inactive-awake-not-consumed", "Dormant source stays disabled and emitter has not attempted its first awake playback");
                }
                dormantNode.SetActive(true);
                check(dormantSource.enabled && dormantSource.isPlaying && WasApplied(dormantEmitter),
                    "settings-audio.inactive-awake-onenable", "Actual owner activation applies and starts its first original awake playback");
                check(!untouched.enabled && !untouched.isPlaying,
                    "settings-audio.unowned-disabled-state", "The same GameObject unowned manually disabled source keeps its state");
                check(Sources(race).Where(a => a.GetComponents<NativeAudioEmitter>().Any(e => e.isActiveAndEnabled && e.sources != null && e.sources.Contains(a))).All(a => a.enabled),
                    "settings-audio.owned-enabled", "Every source actually owned by an active emitter is enabled after ordinary Apply");
            }
            finally { UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(clip); }
        }
        public static void AfterMutedApply(NativeRace race, Action<bool, string, string> check)
        {
            check(NativeAudioRuntime.Muted, "settings-audio.muted-mode", "Caller initializes the real --mute-audio path");
            if (!race || !NativeAudioRuntime.Muted) return;
            NativeSceneSettings.Apply(race);
            check(Sources(race).All(a => !a.enabled && !a.isPlaying), "settings-audio.muted-all-disabled", "All scene AudioSources are stopped and disabled");
        }
    }
}
