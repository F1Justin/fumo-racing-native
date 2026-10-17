using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class EventAudioSample
        {
            public string id, clip, group;
            public bool oneShot, completed;
            public float sourcePeak, listenerPeak, pitch, volume, spatialBlend;
            public NativeAudioObservation.Snapshot audioStart, audioEnd;
        }
        [Serializable] sealed class EventAudioReport
        {
            public bool passed, completed, scriptedState = true, injectedTriggers = true, gameplayAcceptance = false;
            public bool raceLoopIsolated, sourceContextReady, startupSplashAtBegin;
            public string error, failedIterator;
            public string[] failedChecks;
            public int sourceStartedCount, sourceStartedFrame, sourceRaceId;
            public double sourceStartedGameTime, sourceAdvancedGameTime;
            public string scope = "Production Begin/countdown/StartedRace context initialized, then race loop isolated during 35 injected output samples; real scaled timers/DSP, no natural driving acceptance";
            public string[] checks;
            public EventAudioSample[] samples;
            public string outputMode = "injected-event-output-requested";
            public NativeAudioObservation.Snapshot audioStart, audioEnd;
        }
        NativeEventAudioAuditHost eventAudioHost;
        EventAudioReport eventAudioReport;
        readonly List<string> eventAudioChecks = new List<string>();
        readonly List<EventAudioSample> eventAudioSamples = new List<EventAudioSample>();
        void EventCheck(bool success, string name)
        {
            string result = (success ? "PASS " : "FAIL ") + name;
            eventAudioChecks.Add(result);
            Debug.Log("NATIVE_EVENT_AUDIO " + result);
        }
        void IsolateEventSource(AudioSource target)
        {
            foreach (var source in FindObjectsOfType<AudioSource>())
                if (source != target) { source.Stop(); source.enabled = false; }
            foreach (var emitter in FindObjectsOfType<NativeAudioEmitter>()) emitter.enabled = false;
        }
        IEnumerator SampleEvent(string id)
        {
            var entry = NativeAudioRuntime.Catalog.FindEvent(id);
            var source = NativeAudioRuntime.LastEventSource;
            bool hooked = NativeAudioRuntime.LastEventId == id && source;
            EventCheck(hooked, id + " actual production hook");
            if (!hooked) yield break;
            EventCheck(NativeAudioRuntime.LastEventClip == entry.clip, id + " original clip dispatched");
            GetComponent<NativeItemWorld>().ClearForOwner(racers[0]);
            IsolateEventSource(source);
            var sample = new EventAudioSample { id = id, clip = NativeAudioRuntime.LastEventClip.name,
                oneShot = entry.oneShot, group = source.outputAudioMixerGroup ? source.outputAudioMixerGroup.name : "master",
                pitch = source.pitch, volume = source.volume, spatialBlend = source.spatialBlend,
                audioStart = NativeAudioObservation.Capture("injected-event-output-requested") };
            eventAudioSamples.Add(sample);
            try
            {
                var buffer = new float[256];
                for (int i = 0; i < 12; i++)
                {
                    yield return new WaitForSecondsRealtime(.012f);
                    source.GetOutputData(buffer, 0);
                    foreach (float value in buffer) sample.sourcePeak = Mathf.Max(sample.sourcePeak, Mathf.Abs(value));
                    AudioListener.GetOutputData(buffer, 0);
                    foreach (float value in buffer) sample.listenerPeak = Mathf.Max(sample.listenerPeak, Mathf.Abs(value));
                }
                EventCheck(sample.sourcePeak > 0 && sample.listenerPeak > 0, id + " source and listener output");
                EventCheck(source.volume == entry.template.volume && source.spatialBlend == entry.template.spatialBlend
                    && source.outputAudioMixerGroup == entry.template.outputAudioMixerGroup, id + " original source and mixer parameters");
                sample.completed = true;
            }
            finally
            {
                sample.audioEnd = NativeAudioObservation.Capture("injected-event-output-requested");
                if (source) source.Stop();
            }
        }
        IEnumerator CheckEventAudioLive()
        {
            if (eventAudioHost) yield break;
            var host = new GameObject("Persistent event audio verification");
            DontDestroyOnLoad(host);
            eventAudioHost = host.AddComponent<NativeEventAudioAuditHost>();
            eventAudioHost.StartCoroutine(CheckEventAudioManaged());
            yield break;
        }
        void EventAudioException(string stage, Exception error)
        {
            string detail = stage + ": " + error;
            eventAudioReport.error = string.IsNullOrEmpty(eventAudioReport.error) ? detail : eventAudioReport.error + "\n" + detail;
            EventCheck(false, stage + " exception");
        }
        IEnumerator CheckEventAudioManaged()
        {
            string directory = reportDirectory;
            bool raceWasEnabled = enabled;
            float previousScale = Time.timeScale;
            eventAudioChecks.Clear(); eventAudioSamples.Clear();
            eventAudioReport = new EventAudioReport {
                audioStart = NativeAudioObservation.Capture("injected-event-output-requested")
            };
            var stack = new Stack<IEnumerator>(); stack.Push(CheckEventAudioCore());
            try
            {
                while (stack.Count > 0)
                {
                    bool next; object value;
                    try { next = stack.Peek().MoveNext(); value = next ? stack.Peek().Current : null; }
                    catch (Exception error)
                    {
                        eventAudioReport.failedIterator = stack.Peek().GetType().FullName;
                        EventAudioException("iteration", error); break;
                    }
                    if (!next)
                    {
                        try { (stack.Pop() as IDisposable)?.Dispose(); }
                        catch (Exception error) { EventAudioException("iterator disposal", error); break; }
                        continue;
                    }
                    if (value is IEnumerator nested && !(value is CustomYieldInstruction)) { stack.Push(nested); continue; }
                    yield return value;
                }
                eventAudioReport.completed = stack.Count == 0 && string.IsNullOrEmpty(eventAudioReport.error);
            }
            finally
            {
                bool reportSaved = false;
                try
                {
                    while (stack.Count > 0)
                    {
                        try { (stack.Pop() as IDisposable)?.Dispose(); }
                        catch (Exception error) { EventAudioException("iterator cleanup", error); }
                    }
                    eventAudioReport.audioEnd = NativeAudioObservation.Capture(eventAudioReport.outputMode);
                    try { Time.timeScale = previousScale; }
                    catch (Exception error) { EventAudioException("clock restoration", error); }
                    try { if (this) enabled = raceWasEnabled; }
                    catch (Exception error) { EventAudioException("race restoration", error); }
                    eventAudioReport.checks = eventAudioChecks.ToArray();
                    eventAudioReport.failedChecks = eventAudioChecks.Where(check => check.StartsWith("FAIL ")).ToArray();
                    eventAudioReport.samples = eventAudioSamples.ToArray();
                    eventAudioReport.passed = eventAudioReport.completed && string.IsNullOrEmpty(eventAudioReport.error)
                        && eventAudioChecks.Count > 0 && eventAudioReport.failedChecks.Length == 0
                        && eventAudioSamples.Where(sample => sample.completed).Select(sample => sample.id).Distinct().Count() == 35;
                    try
                    {
                        Directory.CreateDirectory(directory);
                        File.WriteAllText(Path.Combine(directory, "audio-events-report.json"), JsonUtility.ToJson(eventAudioReport, true));
                        reportSaved = true;
                    }
                    catch (Exception error) { EventAudioException("report write", error); eventAudioReport.passed = false; Debug.LogError(error); }
                    Debug.Log("NATIVE_EVENT_AUDIO_RESULT passed=" + eventAudioReport.passed + " completed=" + eventAudioReport.completed + " samples=" + eventAudioSamples.Count);
                }
                finally { Application.Quit(reportSaved && eventAudioReport.passed ? 0 : 2); }
            }
        }
        IEnumerator CheckEventAudioCore()
        {
            Time.timeScale = 1;
            Directory.CreateDirectory(reportDirectory);
            QualitySettings.vSyncCount = 0;
            Debug.Log("NATIVE_EVENT_AUDIO_BEGIN backgroundVSync=0");
            yield return new WaitForSecondsRealtime(.5f);
            eventAudioReport.startupSplashAtBegin = StartupSplashActive;
            bool synchronousStart = false;
            Action started = () => {
                eventAudioReport.sourceStartedCount++;
                eventAudioReport.sourceStartedFrame = Time.frameCount;
                eventAudioReport.sourceRaceId = sourceRaceState.RaceId;
                eventAudioReport.sourceStartedGameTime = SourceGameTime;
                synchronousStart = SourceGameTime == 0 && sourceRanking.Started && raceClock.Running && phase == Phase.Racing;
            };
            SourceStartedBoundaryObserved += started;
            try
            {
                autopilot = false;
                Begin();
                float startupDeadline = Time.realtimeSinceStartup + 10;
                while (eventAudioReport.sourceStartedCount == 0 && Time.realtimeSinceStartup < startupDeadline) yield return null;
                EventCheck(eventAudioReport.sourceStartedCount == 1 && synchronousStart, "real Begin countdown synchronous StartedRace boundary");
                while (SourceGameTime <= 0 && Time.realtimeSinceStartup < startupDeadline) yield return null;
                eventAudioReport.sourceAdvancedGameTime = SourceGameTime;
                eventAudioReport.sourceContextReady = raceConfig != null && sourceRaceState != null && sourceRaceState.InRace
                    && phase == Phase.Racing && eventAudioReport.sourceStartedCount == 1 && synchronousStart && SourceGameTime > 0;
                EventCheck(eventAudioReport.sourceContextReady, "real production source context and first game-time advance");
                if (!eventAudioReport.sourceContextReady) throw new InvalidOperationException("Production Begin/StartedRace context did not initialize within ten real seconds");
            }
            finally { SourceStartedBoundaryObserved -= started; }
            enabled = false;
            eventAudioReport.raceLoopIsolated = true;
            foreach (var driver in FindObjectsOfType<NativeKartOriginalAI>()) { driver.active = false; driver.enabled = false; }
            foreach (var motor in racers)
            {
                var itemInventory = motor.GetComponent<NativeKartItemInventory>();
                itemInventory.Configure(this, itemWorld, itemInventory.CharacterId, false);
            }
            NativeAudioRuntime.StopCountdown();
            NativeAudioRuntime.StopMusic();
            EventCheck(NativeKartHitRulesVerification.Run(reportDirectory), "overlapping recovery kinds follow original independent invokes");
            EventCheck(!NativeAudioRuntime.Muted && AudioListener.volume > 0, "ordinary unmuted event output");
            EventCheck(NativeAudioRuntime.Catalog.events.Length == 35, "all 35 original event bindings");
            foreach (var motor in racers) { motor.enabled = false; motor.driving = false; }
            foreach (var vehicle in FindObjectsOfType<NativeVehicleAudio>()) vehicle.enabled = false;
            var listener = FindObjectOfType<AudioListener>();
            listener.transform.position = racers[0].transform.position + Vector3.up * 2;
            var inventory = racers[0].GetComponent<NativeKartItemInventory>();
            var itemHud = FindObjectsOfType<NativeKartItemHud>().First(h => h.inventory == inventory);
            var hit = racers[0].GetComponent<NativeKartHit>();
            var ufo = racers[0].GetComponent<NativeUfoPlayer>();
            inventory.ResetItems(); itemHud.ResetState();
            foreach (var h in FindObjectsOfType<NativeKartItemHud>()) h.enabled = false;
            itemHud.enabled = true;
            EventCheck(inventory.TryGive("YinYang", true), "real random item assignment");
            yield return null;
            yield return SampleEvent("item.roulette");
            EventCheck(NativeAudioRuntime.LastEventSource.pitch >= .9f && NativeAudioRuntime.LastEventSource.pitch <= 1.25f, "roulette original pitch range");
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!inventory.ItemDecided && Time.realtimeSinceStartup < deadline) yield return null;
            EventCheck(inventory.ItemDecided, "roulette decision completes within five seconds");
            yield return null;
            itemHud.enabled = false;
            yield return SampleEvent("item.decided");
            int before = NativeAudioRuntime.EventPlayCount;
            itemHud.enabled = true; yield return null; itemHud.enabled = false;
            EventCheck(NativeAudioRuntime.EventPlayCount == before, "decided item does not replay every frame");
            inventory.ResetItems(); hit.ResetState(); hit.DamageAllowed = () => true;
            EventCheck(inventory.Effects.TryReceiveHit("YinYang", racers[1], 1.2f, NativeItemImpactKind.Damage), "production item impact damage accepted");
            before = NativeAudioRuntime.EventPlayCount;
            EventCheck(!hit.TakeDamage(1.2f) && NativeAudioRuntime.EventPlayCount == before, "repeated damage rejected silently");
            yield return SampleEvent("health.damage");
            deadline = Time.realtimeSinceStartup + 5f;
            while (hit.Frozen && Time.realtimeSinceStartup < deadline) yield return null;
            EventCheck(!hit.Frozen, "damage recovery completes within five seconds");
            yield return SampleEvent("health.respawn");
            hit.ResetState(); hit.TakeDamage(1.2f);
            var damageSource = NativeAudioRuntime.LastEventSource;
            yield return new WaitForSecondsRealtime(.05f);
            var pausePhase = phase;
            phase = Phase.Paused; Time.timeScale = 0f; NativeAudioRuntime.SetPaused(true);
            int pausedAt = damageSource.timeSamples;
            yield return new WaitForSecondsRealtime(.1f);
            EventCheck(!damageSource.isPlaying && damageSource.timeSamples == pausedAt, "pause holds event sample position");
            phase = pausePhase; Time.timeScale = 1f; NativeAudioRuntime.SetPaused(false);
            yield return new WaitForSecondsRealtime(.05f);
            EventCheck(damageSource.isPlaying && damageSource.timeSamples > pausedAt, "resume advances original event channel");
            hit.ResetState(); before = NativeAudioRuntime.EventPlayCount;
            EventCheck(inventory.Effects.TryReceiveHit("FreezeBomb", racers[1], .1f, NativeItemImpactKind.Freeze), "production item impact freeze accepted");
            yield return new WaitForSecondsRealtime(.2f);
            EventCheck(NativeAudioRuntime.EventPlayCount == before, "freeze and silent recovery do not play injury audio");
            hit.ResetState();
            hit.TakeDamage(.8f); hit.MakeInvulnerable(.1f);
            yield return new WaitForSecondsRealtime(.15f);
            EventCheck(hit.TakeDamage(.8f), "overlapping damage accepted after separate immunity expiry");
            before = NativeAudioRuntime.EventPlayCount;
            yield return new WaitForSecondsRealtime(1f);
            EventCheck(NativeAudioRuntime.EventPlayCount == before + 2, "each accepted damage recovery plays respawn");
            hit.ResetState(); hit.TakeDamage(.8f); hit.MakeInvulnerable(.1f);
            yield return new WaitForSecondsRealtime(.15f);
            EventCheck(hit.FreezeFor(.8f), "freeze overlaps previously scheduled damage recovery");
            before = NativeAudioRuntime.EventPlayCount;
            yield return new WaitForSecondsRealtime(1f);
            EventCheck(NativeAudioRuntime.EventPlayCount == before + 1, "damage recovery audible and freeze recovery silent in mixed schedule");
            hit.ResetState();
            ufo.active = true; ufo.ResetState();
            for (int i = 0; i < ufo.catalog.recipeLength; i++)
            {
                EventCheck(ufo.Collect(0), "production UFO collection slot " + i);
                yield return SampleEvent("ufo.collect." + i);
            }
            before = NativeAudioRuntime.EventPlayCount;
            EventCheck(!ufo.Collect(0) && NativeAudioRuntime.EventPlayCount == before, "full UFO recipe rejects collection silently");
            EventCheck(ufo.Use(), "production UFO recipe used");
            yield return SampleEvent("ufo.use");
            inventory.ResetItems(); before = NativeAudioRuntime.EventPlayCount;
            EventCheck(!ufo.Use() && NativeAudioRuntime.EventPlayCount == before, "empty UFO recipe rejects use silently");
            foreach (var entry in GetComponent<NativeItemCatalog>().entries)
            {
                inventory.ResetItems(); hit.ResetState();
                before = NativeAudioRuntime.EventPlayCount;
                EventCheck(inventory.TryGive(entry.id, false), "production give " + entry.id);
                if (!entry.useAtStart) EventCheck(inventory.UseItem(), "production use " + entry.id);
                EventCheck(NativeAudioRuntime.EventPlayCount == before + 1, entry.id + " exactly one use event");
                yield return SampleEvent("item." + entry.id);
            }
            inventory.ResetItems(); hit.ResetState();
            inventory.TryGive("YinYang", false);
            EventCheck(hit.FreezeFor(.2f) && hit.Frozen && inventory.Frozen, "real hit callback freezes item inventory");
            before = NativeAudioRuntime.EventPlayCount;
            EventCheck(!inventory.UseItem() && NativeAudioRuntime.EventPlayCount == before, "frozen item use rejected silently");
            hit.ResetState(); inventory.ResetItems(); before = NativeAudioRuntime.EventPlayCount;
            EventCheck(!inventory.UseItem() && NativeAudioRuntime.EventPlayCount == before, "empty inventory rejects use silently");
            ShowProgression(new NativeProgression.Result { success = true, newlyUnlocked = new[] { "frog" } });
            yield return SampleEvent("progression.unlock");
            before = NativeAudioRuntime.EventPlayCount;
            ShowProgression(new NativeProgression.Result { success = true, newlyUnlocked = Array.Empty<string>(), recordChanged = true });
            EventCheck(NativeAudioRuntime.EventPlayCount == before, "record-only result has no unlock sound");
            ShowProgression(new NativeProgression.Result { success = false, newlyUnlocked = new[] { "frog" }, error = "fixture rejection" });
            EventCheck(NativeAudioRuntime.EventPlayCount == before, "failed progression has no unlock sound");
            inventory.ResetItems();
            EventCheck(eventAudioSamples.Select(s => s.id).Distinct().Count() == 35, "every original event sampled");

        }
    }
    public sealed class NativeEventAudioAuditHost : MonoBehaviour { }
}
