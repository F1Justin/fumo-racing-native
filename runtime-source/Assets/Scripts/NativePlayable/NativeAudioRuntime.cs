using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAudioRuntime : MonoBehaviour
    {
        static NativeAudioRuntime instance;
        static bool initialized;
        internal static bool VerificationListenerSilenced { get; set; }
        public static bool Muted { get; private set; } = true;
        public static NativeAudioCatalog Catalog { get; private set; }
        public static string CurrentMusic { get; private set; } = "";
        public static string LastError { get; private set; } = "";
        static readonly string[] parameters = { "sfxVolume", "musicVolume", "ambientVolume", "engineVolume", "itemVolume", "explosionVolume", "uiVolume" };
        readonly Dictionary<string, AudioClip[]> segments = new Dictionary<string, AudioClip[]>();
        AudioSource intro, loop, second, secondIntro, outro, secondOutro, countdownSource;
        AudioSource[] selectionIntro, selectionLoop;
        float[] selectionVolumes = new float[3], selectionFrom = new float[3];
        int selectedLayer = -1;
        float selectionAge;
        bool fullPower;
        bool verificationPlayback;
        bool PlaybackAllowed => !Muted || verificationPlayback;
        double resumeMainAt, mainEndAt, secondEndAt;
        NativeAudioCatalog.MusicEntry fadingEntry;
        float fadeAge;
        bool paused;
        double pauseStarted, loopScheduled;
        bool loopPending;
        static float telemetryListenerVolume = float.NaN;
        static readonly float[] telemetryGroupVolumes = new float[7];
        static bool telemetryVolumesObserved;
        static int telemetryMixerFailureMask;
        public static int MusicCount => Catalog == null ? 0 : Catalog.music.Length;
        public static string MusicTitle(int index) => Catalog != null && index >= 0 && index < Catalog.music.Length ? Catalog.music[index].title : "";
        public static string MusicId(int index) => Catalog != null && index >= 0 && index < Catalog.music.Length ? Catalog.music[index].id : "";
        public static bool MusicPaused => instance && (instance.musicRoomMenuHeld ? instance.musicRoomPreviewPaused : instance.paused);
        public static bool PreviewMusicRoom(int index) => instance && instance.StartMusicRoomPreview(index);
        public static void ToggleMusicRoomPause() { if (instance) instance.SetMusicRoomPreviewPaused(!instance.musicRoomPreviewPaused); }

        public static IEnumerator VerifyMusicRoomPlayback(int index, Action<bool, string> check)
        {
            if (!instance) { check(false, "Music Room audio runtime exists"); yield break; }
            var runtime = instance;
            bool previousVerification = runtime.verificationPlayback;
            float previousVolume = AudioListener.volume;
            try
            {
                runtime.verificationPlayback = true;
                AudioListener.volume = 0;
                check(PreviewMusicRoom(index), "Music Room starts original full clip");
                yield return new WaitForSecondsRealtime(.15f);
                check(runtime.musicRoomPreviewSource.isPlaying && runtime.musicRoomPreviewSource.clip == NativeMusicRoomData.At(index).clip, "Music Room original clip actually plays");
                ToggleMusicRoomPause();
                check(MusicPaused && !runtime.musicRoomPreviewSource.isPlaying, "Music Room pauses actual AudioSource");
                ToggleMusicRoomPause();
                yield return new WaitForSecondsRealtime(.05f);
                check(!MusicPaused && runtime.musicRoomPreviewSource.isPlaying, "Music Room resumes actual AudioSource");
            }
            finally
            {
                StopMusicRoomPreview();
                runtime.verificationPlayback = previousVerification;
                AudioListener.volume = previousVolume;
                PreviewMusicRoom(index);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; initialized = false; VerificationListenerSilenced = false; Muted = true; Catalog = null; CurrentMusic = ""; LastError = ""; telemetryListenerVolume = float.NaN; telemetryVolumesObserved = false; telemetryMixerFailureMask = 0; ResetEventAudio(); }
        public static void Initialize(string[] args, bool verification)
        {
            bool changed = !initialized || !instance;
            if (!initialized)
            {
                Muted = verification || Array.IndexOf(args ?? Array.Empty<string>(), "--mute-audio") >= 0;
                Catalog = NativeContent.Load<NativeAudioCatalog>("NativeAudio/Catalog");
                initialized = true;
                if (!Catalog) LastError = "Original audio catalog is missing";
            }
            if (!instance)
            {
                var root = new GameObject("Native original audio"); DontDestroyOnLoad(root);
                instance = root.AddComponent<NativeAudioRuntime>();
                instance.intro = instance.CreateMusicSource(); instance.loop = instance.CreateMusicSource(); instance.second = instance.CreateMusicSource(); instance.secondIntro = instance.CreateMusicSource(); instance.outro = instance.CreateMusicSource(); instance.secondOutro = instance.CreateMusicSource();
                NativeUserSettings.Changed += ApplyVolumes;
            }
            ApplyVolumes();
            if (changed) NativeTelemetry.Event("audio.initialize.result", Catalog ? "completed" : "failed",
                !Catalog ? "requiredCatalogMissing" : Muted ? "initializedMuted" : "initialized", verification ? "verification" : "normal");
        }
        AudioSource CreateMusicSource()
        {
            var source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.spatialBlend = 0; source.volume = 1; source.ignoreListenerPause = true;
            Route(source, 1); return source;
        }
        public static void Route(AudioSource source, int group)
        {
            if (!source) return;
            if (Catalog && Catalog.groups != null && group >= 0 && group < Catalog.groups.Length)
                source.outputAudioMixerGroup = Catalog.groups[group];
            if (Muted) { source.Stop(); source.enabled = false; }
        }
        public static void ApplyVolumes()
        {
            AudioListener.volume = initialized && !Muted && !VerificationListenerSilenced ? NativeUserSettings.GetFloat("mainVolume") : 0;
            float listenerVolume = AudioListener.volume;
            bool changed = !telemetryVolumesObserved || listenerVolume != telemetryListenerVolume;
            telemetryListenerVolume = listenerVolume;
            int mixerFailures = 0;
            if (Catalog && !Catalog.mixer)
            {
                mixerFailures = 1 << 7;
                if ((telemetryMixerFailureMask & mixerFailures) == 0) NativeTelemetry.Event("audio.resource.failure", "failed", "requiredMixerMissing");
            }
            if (Catalog && Catalog.mixer)
                for (int i = 0; i < parameters.Length; i++)
                {
                    float requestedDb = NativeUserSettings.VolumeDb(NativeUserSettings.GetFloat("volumeGroup" + i));
                    changed |= !telemetryVolumesObserved || telemetryGroupVolumes[i] != requestedDb;
                    telemetryGroupVolumes[i] = requestedDb;
                    if (!Catalog.mixer.SetFloat(parameters[i], requestedDb))
                    {
                        string error = "Original mixer parameter missing: " + parameters[i];
                        mixerFailures |= 1 << i;
                        if ((telemetryMixerFailureMask & (1 << i)) == 0) NativeTelemetry.Event("audio.resource.failure", "failed", "mixerParameterMissing", parameters[i]);
                        LastError = error;
                    }
                }
            changed |= mixerFailures != telemetryMixerFailureMask;
            telemetryMixerFailureMask = mixerFailures;
            telemetryVolumesObserved = true;
            if (changed) NativeTelemetry.Event("audio.volume.changed", mixerFailures == 0 ? "applied" : "failed", mixerFailures != 0 ? "partialMixerFailure" : Muted ? "muted" : VerificationListenerSilenced ? "verificationSilenced" : "settingsApplied",
                "listenerVolume=" + listenerVolume.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        public static bool PreviewMusic(int index)
        {
            if (index < 0 || index >= MusicCount) return false;
            return PlayMusic(MusicId(index), true);
        }
        public static bool PlayMusic(string id, bool restart = false)
        {
            if (!instance || !Catalog) { NativeTelemetry.Event("audio.music.result", "failed", !instance ? "runtimeUnavailable" : "requiredCatalogMissing", id); return false; }
            var entry = Catalog.FindMusic(id);
            if (entry == null || !entry.clip) { LastError = "Original music missing: " + id; NativeTelemetry.Event("audio.music.result", "failed", "requiredMusicMissing", id); return false; }
            if (!restart && CurrentMusic == id) { NativeTelemetry.Count("audio.music.sameTrackIgnored"); return true; }
            string operation = NativeTelemetry.Begin("audio.music.requested", id);
            instance.StopChannels(); CurrentMusic = id;
            NativeTelemetry.Event("audio.music.changed", "applied", restart ? "restartRequested" : "selectionChanged", id);
            if (!instance.PlaybackAllowed) { NativeTelemetry.End(operation, "audio.music.result", "accepted", "mutedSelection", id); return true; }
            try
            {
                var parts = instance.GetSegments(entry);
                double start = AudioSettings.dspTime + .05;
                instance.intro.enabled = instance.loop.enabled = true; instance.intro.volume = instance.loop.volume = 1;
                instance.loop.clip = parts[1]; instance.loop.loop = true;
                if (parts[0])
                {
                    instance.intro.clip = parts[0]; instance.intro.loop = false; instance.intro.PlayScheduled(start);
                    start += (double)parts[0].samples / parts[0].frequency;
                }
                instance.loopScheduled = start; instance.loopPending = true; instance.loop.PlayScheduled(start);
                LastError = ""; NativeTelemetry.End(operation, "audio.music.result", "accepted", "scheduled", id); return true;
            }
            catch (Exception error) { LastError = "Music decoding failed: " + error.Message; NativeTelemetry.End(operation, "audio.music.result", "failed", "musicDecodingFailed", error.GetType().Name); return false; }
        }
        AudioClip[] GetSegments(NativeAudioCatalog.MusicEntry entry)
        {
            if (segments.TryGetValue(entry.id, out var result)) return result;
            if (entry.loopStart < 0 || entry.loopEnd <= entry.loopStart || entry.loopEnd > entry.clip.samples || entry.endStart < 0 || entry.endStart > entry.clip.samples)
                throw new ArgumentException("Invalid original sample boundaries: " + entry.id);
            result = new AudioClip[3];
            // Whole-clip reads preserve FMOD decoder alignment through the final frame.
            var original = new float[checked(entry.clip.samples * entry.clip.channels)];
            if (!entry.clip.GetData(original, 0)) throw new InvalidOperationException("Cannot read whole original PCM: " + entry.id);
            try
            {
                result[0] = Slice(entry.clip, 0, entry.loopStart, entry.id + " intro", original);
                result[1] = Slice(entry.clip, entry.loopStart, entry.loopEnd - entry.loopStart, entry.id + " loop", original);
                result[2] = Slice(entry.clip, entry.endStart, entry.clip.samples - entry.endStart, entry.id + " end", original);
                segments.Add(entry.id, result); return result;
            }
            catch { foreach (var clip in result) if (clip) Destroy(clip); throw; }
        }
        public static bool VerifySegments(string id, out string error)
        {
            error = "";
            if (!instance || !Catalog) { error = "catalog not initialized"; return false; }
            var entry = Catalog.FindMusic(id);
            if (entry == null || !entry.clip) { error = "original music absent"; return false; }
            bool existed = instance.segments.ContainsKey(id);
            try
            {
                var parts = instance.GetSegments(entry);
                ComparePCM(entry, parts);
                return true;
            }
            catch (Exception failure) { error = failure.Message; return false; }
            finally
            {
                if (!existed && instance.segments.TryGetValue(id, out var temporary))
                { instance.segments.Remove(id); foreach (var clip in temporary) if (clip) Destroy(clip); }
            }
        }
        static void ComparePCM(NativeAudioCatalog.MusicEntry entry, AudioClip[] parts)
        {
            var expected = new float[checked(entry.clip.samples * entry.clip.channels)];
            if (!entry.clip.GetData(expected, 0)) throw new InvalidOperationException("Whole source PCM unreadable: " + entry.id);
            int[] starts = { 0, entry.loopStart, entry.endStart };
            int[] lengths = { entry.loopStart, entry.loopEnd - entry.loopStart, entry.clip.samples - entry.endStart };
            for (int i = 0; i < parts.Length; i++)
            {
                if (lengths[i] == 0) { if (parts[i]) throw new InvalidOperationException("nonempty zero-length segment"); continue; }
                if (!parts[i] || parts[i].samples != lengths[i] || parts[i].frequency != entry.clip.frequency || parts[i].channels != entry.clip.channels)
                    throw new InvalidOperationException("segment format mismatch");
                var actual = new float[checked(lengths[i] * entry.clip.channels)];
                if (!parts[i].GetData(actual, 0)) throw new InvalidOperationException("Whole segment PCM unreadable: " + i);
                int offset = checked(starts[i] * entry.clip.channels);
                for (int sample = 0; sample < actual.Length; sample++)
                    if (actual[sample] != expected[offset + sample]) throw new InvalidOperationException("PCM differs at segment " + i + " sample " + sample);
            }
        }
        public static bool VerifyMusicPCM(NativeAudioCatalog.MusicEntry entry, out string error)
        {
            error = "";
            var parts = new AudioClip[3];
            try
            {
                var whole = new float[checked(entry.clip.samples * entry.clip.channels)];
                if (!entry.clip.LoadAudioData() || !entry.clip.GetData(whole, 0)) throw new InvalidOperationException("Whole source PCM unreadable: " + entry.id);
                parts[0] = Slice(entry.clip, 0, entry.loopStart, entry.id + " intro", whole);
                parts[1] = Slice(entry.clip, entry.loopStart, entry.loopEnd - entry.loopStart, entry.id + " loop", whole);
                parts[2] = Slice(entry.clip, entry.endStart, entry.clip.samples - entry.endStart, entry.id + " end", whole);
                ComparePCM(entry, parts);
                return true;
            }
            catch (Exception failure) { error = failure.Message; return false; }
            finally
            {
                foreach (var clip in parts) if (clip)
                { if (Application.isPlaying) Destroy(clip); else DestroyImmediate(clip); }
            }
        }
        static AudioClip Slice(AudioClip source, int start, int length, string name, float[] original)
        {
            if (length == 0) return null;
            var samples = new float[checked(length * source.channels)];
            Array.Copy(original, checked(start * source.channels), samples, 0, samples.Length);
            var clip = AudioClip.Create(name, length, source.channels, source.frequency, false);
            if (!clip.SetData(samples, 0)) { Destroy(clip); throw new InvalidOperationException("Cannot create original segment"); }
            return clip;
        }
        void StopChannels()
        {
            ResetMusicRoomAudio();
            intro.Stop(); loop.Stop(); second.Stop(); secondIntro.Stop(); outro.Stop(); secondOutro.Stop(); mainEndAt = secondEndAt = 0; fadingEntry = null; paused = false; loopPending = false;
            if (selectionIntro != null) foreach (var source in selectionIntro) source.Stop();
            if (selectionLoop != null) foreach (var source in selectionLoop) source.Stop();
            selectedLayer = -1; fullPower = false; resumeMainAt = 0;
            foreach (var source in new[] { intro, loop, second, secondIntro, outro, secondOutro }) source.clip = null;
            if (selectionIntro != null) foreach (var source in selectionIntro) source.clip = null;
            if (selectionLoop != null) foreach (var source in selectionLoop) source.clip = null;
            foreach (var parts in segments.Values) foreach (var clip in parts) if (clip) Destroy(clip);
            segments.Clear();
        }
        public static void StopMusic()
        {
            bool changed = !string.IsNullOrEmpty(CurrentMusic);
            if (instance) instance.StopChannels(); CurrentMusic = "";
            if (changed) NativeTelemetry.Event("audio.music.changed", "applied", "stopped");
        }
        public static void SetPaused(bool pause)
        {
            if (!instance || !instance.PlaybackAllowed || instance.paused == pause) return;
            instance.paused = pause;
            NativeTelemetry.Event("audio.pause.changed", "applied", pause ? "paused" : "resumed");
            PauseEventAudio(pause);
            if (pause)
            {
                if (instance.countdownSource) instance.countdownSource.Pause();
                instance.pauseStarted = AudioSettings.dspTime; instance.outro.Pause(); instance.secondOutro.Pause(); instance.intro.Pause(); instance.loop.Pause(); instance.second.Pause(); instance.secondIntro.Pause();
                if (instance.selectedLayer >= 0) { foreach (var source in instance.selectionIntro) source.Pause(); foreach (var source in instance.selectionLoop) source.Pause(); }
            }
            else
            {
                if (instance.countdownSource) instance.countdownSource.UnPause();
                double pausedFor = AudioSettings.dspTime - instance.pauseStarted;
                if (instance.resumeMainAt > 0) instance.resumeMainAt += pausedFor;
                if (instance.mainEndAt > 0) instance.mainEndAt += pausedFor;
                if (instance.secondEndAt > 0) instance.secondEndAt += pausedFor;
                instance.outro.UnPause(); instance.secondOutro.UnPause();
                instance.second.UnPause(); instance.secondIntro.UnPause();
                if (instance.selectedLayer >= 0) { foreach (var source in instance.selectionIntro) source.UnPause(); foreach (var source in instance.selectionLoop) source.UnPause(); }
                if (instance.fullPower || instance.resumeMainAt > 0) return;
                instance.intro.UnPause();
                if (instance.loopPending && instance.pauseStarted < instance.loopScheduled)
                {
                    instance.loop.Stop(); instance.loopScheduled += AudioSettings.dspTime - instance.pauseStarted;
                    instance.loop.PlayScheduled(instance.loopScheduled);
                }
                else instance.loop.UnPause();
            }
        }
        public static void PlayCountdown(int second)
        {
            if (!instance || !Catalog || Muted) { NativeTelemetry.Count(!instance ? "audio.countdown.runtimeUnavailable" : !Catalog ? "audio.countdown.catalogUnavailable" : "audio.countdown.muted"); return; }
            if (!instance.countdownSource && Catalog.countdownTemplate)
                instance.countdownSource = Instantiate(Catalog.countdownTemplate.gameObject, instance.transform, false).GetComponent<AudioSource>();
            var source = instance.countdownSource;
            var clip = second > 0 ? Catalog.countdownSecond : Catalog.countdownEnd;
            if (!source || !clip) { LastError = "Original countdown audio is missing"; NativeTelemetry.Event("audio.resource.failure", "failed", "countdownResourceMissing"); return; }
            source.enabled = true; source.clip = clip; source.Play();
            NativeTelemetry.Count("audio.countdown.accepted");
            float mixerDb; Catalog.mixer.GetFloat("uiVolume", out mixerDb);
            Debug.Log("NATIVE_COUNTDOWN_AUDIO second=" + second + " clip=" + clip.name + " playing=" + source.isPlaying +
                " sourceVolume=" + source.volume + " listenerVolume=" + AudioListener.volume + " mixer=" + source.outputAudioMixerGroup.name + " uiDb=" + mixerDb);
            instance.StartCoroutine(instance.MeasureCountdownOutput(source, clip, second));
        }
        IEnumerator MeasureCountdownOutput(AudioSource source, AudioClip clip, int second)
        {
            var samples = new float[1024];
            float sourceRms = 0, sourcePeak = 0, listenerRms = 0, listenerPeak = 0;
            int sampled = 0;
            for (int frame = 0; frame < 12; frame++)
            {
                yield return new WaitForSecondsRealtime(.04f);
                if (!source || source.clip != clip) break;
                source.GetOutputData(samples, 0); double sum = 0;
                foreach (float sample in samples) { sum += sample * sample; sourcePeak = Mathf.Max(sourcePeak, Mathf.Abs(sample)); }
                sourceRms = Mathf.Max(sourceRms, (float)Math.Sqrt(sum / samples.Length));
                AudioListener.GetOutputData(samples, 0); sum = 0;
                foreach (float sample in samples) { sum += sample * sample; listenerPeak = Mathf.Max(listenerPeak, Mathf.Abs(sample)); }
                listenerRms = Mathf.Max(listenerRms, (float)Math.Sqrt(sum / samples.Length));
                sampled++;
            }
            Debug.Log("NATIVE_COUNTDOWN_OUTPUT second=" + second + " clip=" + clip.name + " samples=" + sampled +
                " sourceRms=" + sourceRms.ToString("R") + " sourcePeak=" + sourcePeak.ToString("R") +
                " listenerRms=" + listenerRms.ToString("R") + " listenerPeak=" + listenerPeak.ToString("R"));
        }
        public static void StopCountdown() { if (instance && instance.countdownSource) instance.countdownSource.Stop(); }
        public static void PlayUi(AudioSource source, AudioClip clip)
        {
            if (!source || !clip || (Muted && !UiVerificationPlayback))
            {
                NativeTelemetry.Count(!source ? "audio.ui.sourceUnavailable" : !clip ? "audio.ui.clipUnavailable" : "audio.ui.muted");
                return;
            }
            Route(source, 6); source.enabled = true; source.PlayOneShot(clip);
            NativeTelemetry.Count("audio.ui.accepted");
            ObserveUiOneShot(source, clip);
        }
        public static bool PlaySelectionLayer(int layer)
        {
            if (!instance || !Catalog || layer < 0 || layer > 2)
            { NativeTelemetry.Event("audio.selection.result", layer < 0 || layer > 2 ? "rejected" : "failed", layer < 0 || layer > 2 ? "invalidLayer" : "runtimeUnavailable"); return false; }
            var ids = new[] { "TrackSelect", "CharSelect", "CustomSelect" };
            if (instance.selectedLayer < 0)
            {
                instance.StopChannels();
                if (instance.selectionIntro == null)
                {
                    instance.selectionIntro = new AudioSource[3]; instance.selectionLoop = new AudioSource[3];
                    for (int i = 0; i < 3; i++) { instance.selectionIntro[i] = instance.CreateMusicSource(); instance.selectionLoop[i] = instance.CreateMusicSource(); }
                }
                double start = AudioSettings.dspTime + .05;
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        instance.selectionVolumes[i] = i == layer ? 1 : 0;
                        if (!instance.PlaybackAllowed) continue;
                        var parts = instance.GetSegments(Catalog.FindMusic(ids[i]));
                        var head = instance.selectionIntro[i]; var body = instance.selectionLoop[i];
                        head.enabled = body.enabled = true; head.volume = body.volume = instance.selectionVolumes[i];
                        head.clip = parts[0]; body.clip = parts[1]; body.loop = true;
                        if (head.clip) head.PlayScheduled(start);
                        body.PlayScheduled(start + (head.clip ? (double)head.clip.samples / head.clip.frequency : 0));
                    }
                }
                catch (Exception error) { LastError = error.Message; NativeTelemetry.Event("audio.selection.result", "failed", "musicDecodingFailed", error.GetType().Name); return false; }
            }
            if (instance.selectedLayer != layer)
            {
                Array.Copy(instance.selectionVolumes, instance.selectionFrom, 3);
                instance.selectionAge = 0; instance.selectedLayer = layer;
                NativeTelemetry.Event("audio.music.changed", "applied", instance.PlaybackAllowed ? "selectionLayerChanged" : "mutedSelection", ids[layer]);
            }
            CurrentMusic = ids[layer]; return true;
        }
        public static void PlayFullPower(bool active)
        {
            if (!instance || !Catalog || instance.fullPower == active) return;
            instance.fullPower = active;
            NativeTelemetry.Event("audio.fullPower.changed", "applied", !instance.PlaybackAllowed ? "mutedSelection" : active ? "activated" : "deactivated");
            if (!instance.PlaybackAllowed) return;
            if (!active)
            {
                instance.resumeMainAt = AudioSettings.dspTime + 1;
                var entry = Catalog.FindMusic("FullPower");
                try
                {
                    var tail = instance.GetSegments(entry)[2];
                    instance.secondEndAt = NextBeat(AudioSettings.dspTime, (instance.secondIntro.clip ? instance.secondIntro.clip.length : 0) + instance.second.time, entry.bpm);
                    if (tail) { instance.secondOutro.clip = tail; instance.secondOutro.loop = false; instance.secondOutro.enabled = true; instance.secondOutro.PlayScheduled(instance.secondEndAt); }
                }
                catch (Exception error) { LastError = error.Message; NativeTelemetry.Event("audio.music.result", "failed", "fullPowerOutroFailed", error.GetType().Name); }
                return;
            }
            instance.resumeMainAt = 0;
            var data = Catalog.FindMusic("FullPower"); if (data == null) { NativeTelemetry.Event("audio.resource.failure", "failed", "fullPowerMusicMissing"); return; }
            instance.intro.Pause(); instance.loop.Pause(); instance.secondEndAt = 0; instance.secondOutro.Stop();
            try
            {
                var parts = instance.GetSegments(data); double start = AudioSettings.dspTime + .05;
                instance.secondIntro.enabled = instance.second.enabled = true;
                instance.secondIntro.clip = parts[0]; instance.secondIntro.loop = false;
                if (parts[0]) instance.secondIntro.PlayScheduled(start);
                instance.second.clip = parts[1]; instance.second.loop = true;
                instance.second.PlayScheduled(start + (parts[0] ? (double)parts[0].samples / parts[0].frequency : 0));
                NativeTelemetry.Event("audio.fullPower.result", "accepted", "scheduled");
            }
            catch (Exception error) { LastError = error.Message; NativeTelemetry.Event("audio.music.result", "failed", "fullPowerDecodingFailed", error.GetType().Name); }
        }
        public static double NextBeat(double dspTime, float elapsed, float bpm)
        {
            double beats = elapsed / 60.0 * bpm;
            return dspTime + (1 - (beats - (int)beats)) * (60.0 / bpm);
        }
        public static void EndCurrentMusic()
        {
            if (!instance || !Catalog || !instance.PlaybackAllowed) return;
            var entry = Catalog.FindMusic(CurrentMusic); if (entry == null) return;
            if (entry.useFadeOut) { instance.fadingEntry = entry; instance.fadeAge = 0; NativeTelemetry.Event("audio.music.end.requested", "accepted", "fadeStarted", CurrentMusic); return; }
            try
            {
                var tail = instance.GetSegments(entry)[2];
                instance.mainEndAt = NextBeat(AudioSettings.dspTime, (instance.intro.clip ? instance.intro.clip.length : 0) + instance.loop.time, entry.bpm);
                if (tail) { instance.outro.clip = tail; instance.outro.loop = false; instance.outro.enabled = true; instance.outro.PlayScheduled(instance.mainEndAt); }
                NativeTelemetry.Event("audio.music.end.requested", "accepted", tail ? "outroScheduled" : "boundaryStopScheduled", CurrentMusic);
            }
            catch (Exception error) { LastError = error.Message; NativeTelemetry.Event("audio.music.result", "failed", "musicEndDecodingFailed", error.GetType().Name); }
        }
        void Update()
        {
            if (paused || musicRoomMenuHeld || !PlaybackAllowed) return;
            if (mainEndAt > 0 && AudioSettings.dspTime >= mainEndAt) { intro.Stop(); loop.Stop(); mainEndAt = 0; NativeTelemetry.Event("audio.music.end.applied", "applied", "scheduledBoundary", CurrentMusic); }
            if (secondEndAt > 0 && AudioSettings.dspTime >= secondEndAt) { secondIntro.Stop(); second.Stop(); secondEndAt = 0; }
            if (fadingEntry != null)
            {
                fadeAge += Time.deltaTime;
                float fadeProgress = fadingEntry.fadeOutDuration > 0 ? Mathf.Clamp01(fadeAge / fadingEntry.fadeOutDuration) : 1;
                float value = fadingEntry.fadeOutCurve.Evaluate(fadeProgress); intro.volume = loop.volume = value;
                if (fadeProgress >= 1) { intro.Stop(); loop.Stop(); fadingEntry = null; NativeTelemetry.Event("audio.music.end.applied", "applied", "fadeCompleted", CurrentMusic); }
            }
            if (resumeMainAt > 0 && AudioSettings.dspTime >= resumeMainAt)
            { resumeMainAt = 0; intro.UnPause(); loop.UnPause(); NativeTelemetry.Event("audio.pause.changed", "applied", "fullPowerMainResumed"); }
            if (selectedLayer < 0) return;
            selectionAge += Time.deltaTime;
            float t = Mathf.Clamp01(selectionAge / Catalog.selectionTransitionTime);
            for (int i = 0; i < 3; i++)
            {
                selectionVolumes[i] = i == selectedLayer ? Mathf.LerpUnclamped(selectionFrom[i], 1, Catalog.selectionIn.Evaluate(t))
                    : selectionFrom[i] * Catalog.selectionOut.Evaluate(t);
                selectionIntro[i].volume = selectionLoop[i].volume = selectionVolumes[i];
            }
        }
        public static bool VerificationSilent()
        {
            if (!Muted || AudioListener.volume != 0 || !instance) return false;
            foreach (var source in instance.GetComponents<AudioSource>())
                if (source.enabled || source.isPlaying) return false;
            return true;
        }
        public static IEnumerator VerifyScheduling(Action<bool, string> check)
        {
            if (!instance || !Catalog || !Muted)
            { check(false, "DSP verification requires initialized muted runtime"); yield break; }
            var runtime = instance;
            string previousMusic = CurrentMusic;
            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 1;
                runtime.verificationPlayback = true;
                AudioListener.volume = 0;
                check(PlayMusic("Menu", true), "DSP menu scheduled");
                yield return new WaitForSecondsRealtime(.15f);
                check(runtime.intro.isPlaying || runtime.loop.isPlaying, "DSP menu actually playing");
                check(PlaySelectionLayer(0), "DSP selection layers scheduled");
                yield return new WaitForSecondsRealtime(.15f);
                bool layersPlaying = true;
                for (int i = 0; i < 3; i++) layersPlaying &= runtime.selectionIntro[i].isPlaying || runtime.selectionLoop[i].isPlaying;
                check(layersPlaying, "DSP all three selection layers actually playing");
                check(PlaySelectionLayer(1), "DSP character layer transition");
                check(PlayMusic("Forest", true), "DSP race music scheduled");
                yield return new WaitForSecondsRealtime(.15f);
                check(runtime.intro.isPlaying || runtime.loop.isPlaying, "DSP race music actually playing");
                PlayFullPower(true);
                yield return new WaitForSecondsRealtime(.15f);
                check((runtime.secondIntro.isPlaying || runtime.second.isPlaying) && !runtime.intro.isPlaying && !runtime.loop.isPlaying,
                    "DSP FullPower playing with main paused");
                PlayFullPower(false);
                yield return new WaitForSecondsRealtime(.2f);
                PlayFullPower(true);
                yield return new WaitForSecondsRealtime(1.05f);
                check(runtime.fullPower && runtime.resumeMainAt == 0 && !runtime.intro.isPlaying && !runtime.loop.isPlaying
                    && (runtime.secondIntro.isPlaying || runtime.second.isPlaying), "DSP reactivated FullPower keeps main paused past previous resume deadline");
                PlayFullPower(false);
                yield return new WaitForSecondsRealtime(1.15f);
                check(!runtime.fullPower && runtime.resumeMainAt == 0 && (runtime.intro.isPlaying || runtime.loop.isPlaying),
                    "DSP main resumes after FullPower cancellation");
                check(AudioListener.volume == 0 && Muted, "DSP playback output remains muted");
            }
            finally
            {
                runtime.StopChannels();
                runtime.verificationPlayback = false;
                Time.timeScale = previousScale;
                CurrentMusic = previousMusic;
                foreach (var source in runtime.GetComponents<AudioSource>()) { source.Stop(); source.enabled = false; }
                ApplyVolumes();
            }
            check(VerificationSilent(), "DSP verification restores stopped disabled sources");
        }
        void OnDestroy()
        {
            if (instance != this) return;
            NativeUserSettings.Changed -= ApplyVolumes;
            foreach (var parts in segments.Values) foreach (var clip in parts) if (clip) Destroy(clip);
            instance = null;
            NativeTelemetry.Event("audio.runtime.destroyed", "completed");
        }
    }
}
