using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAudioRuntime
    {
        AudioSource musicRoomPreviewSource, musicRoomMenuProbe;
        bool musicRoomMenuHeld, musicRoomPreviewPaused;
        string musicRoomPreviewId = "";

        public static string CurrentMusicRoomPreview => instance ? instance.musicRoomPreviewId : "";

        public sealed class MusicRoomSourceSnapshot
        {
            public AudioSource source { get; }
            public AudioClip clip { get; }
            public int sourceInstanceId { get; }
            public int clipInstanceId { get; }
            public int timeSamples { get; }
            public bool paused { get; }
            public bool isPlaying { get; }
            public bool enabled { get; }
            public bool active { get; }
            internal MusicRoomSourceSnapshot(AudioSource value, bool held)
            {
                source = value; clip = value ? value.clip : null;
                sourceInstanceId = value ? value.GetInstanceID() : 0;
                clipInstanceId = clip ? clip.GetInstanceID() : 0;
                timeSamples = value && clip ? value.timeSamples : 0;
                paused = held; isPlaying = value && value.isPlaying;
                enabled = value && value.enabled; active = value && value.gameObject.activeInHierarchy;
            }
        }
        public sealed class MusicRoomAudioSnapshot
        {
            public MusicRoomSourceSnapshot menu { get; }
            public MusicRoomSourceSnapshot[] menuSources { get; }
            public MusicRoomSourceSnapshot preview { get; }
            public bool menuPaused { get; }
            public double dspTime { get; }
            internal MusicRoomAudioSnapshot(NativeAudioRuntime runtime)
            {
                menuPaused = runtime && runtime.musicRoomMenuHeld; dspTime = AudioSettings.dspTime;
                var probe = runtime ? runtime.musicRoomMenuProbe : null;
                if (!probe && runtime) probe = runtime.intro.isPlaying ? runtime.intro : runtime.loop;
                menu = new MusicRoomSourceSnapshot(probe, menuPaused);
                menuSources = new[] { new MusicRoomSourceSnapshot(runtime ? runtime.intro : null, menuPaused),
                    new MusicRoomSourceSnapshot(runtime ? runtime.loop : null, menuPaused) };
                preview = new MusicRoomSourceSnapshot(runtime ? runtime.musicRoomPreviewSource : null,
                    runtime && runtime.musicRoomPreviewPaused);
            }
        }
        public static MusicRoomAudioSnapshot CaptureMusicRoomAudioSnapshot() => new MusicRoomAudioSnapshot(instance);

        public static void PauseMenuForMusicRoom()
        {
            if (!instance || instance.musicRoomMenuHeld) return;
            var runtime = instance;
            runtime.musicRoomMenuProbe = runtime.intro.isPlaying ? runtime.intro : runtime.loop;
            runtime.musicRoomMenuHeld = true;
            // Original MusicLayerBehaviour.PauseMusic keeps the scheduled loop while holding the intro.
            if (runtime.PlaybackAllowed && runtime.intro.time > 0)
                runtime.loop.SetScheduledStartTime(AudioSettings.dspTime + 999999d);
            runtime.intro.Pause(); runtime.loop.Pause();
            NativeTelemetry.Event("audio.musicRoom.menuPause.changed", "applied", runtime.PlaybackAllowed ? "paused" : "mutedSelection");
        }
        public static void ResumeMenuAfterMusicRoom()
        {
            if (!instance || !instance.musicRoomMenuHeld) return;
            var runtime = instance;
            runtime.musicRoomMenuHeld = false;
            if (!runtime.PlaybackAllowed) { NativeTelemetry.Event("audio.musicRoom.menuPause.changed", "applied", "mutedSelection"); return; }
            if (runtime.intro.clip && runtime.intro.time > 0)
            {
                runtime.loopScheduled = AudioSettings.dspTime + runtime.intro.clip.length - runtime.intro.time;
                runtime.loop.SetScheduledStartTime(runtime.loopScheduled);
            }
            runtime.intro.UnPause(); runtime.loop.UnPause();
            NativeTelemetry.Event("audio.musicRoom.menuPause.changed", "applied", "resumed");
        }
        public static void StopMusicRoomPreview()
        {
            if (!instance) return;
            bool changed = !string.IsNullOrEmpty(instance.musicRoomPreviewId) || instance.musicRoomPreviewPaused;
            if (instance.musicRoomPreviewSource)
            {
                instance.musicRoomPreviewSource.Stop();
                if (!instance.PlaybackAllowed) instance.musicRoomPreviewSource.enabled = false;
            }
            instance.musicRoomPreviewPaused = false; instance.musicRoomPreviewId = "";
            if (changed) NativeTelemetry.Event("audio.musicRoom.preview.changed", "applied", "stopped");
        }
        bool StartMusicRoomPreview(int index)
        {
            var entry = NativeMusicRoomData.At(index);
            if (entry == null || !entry.clip) { NativeTelemetry.Event("audio.musicRoom.preview.result", "failed", "previewResourceMissing"); return false; }
            PauseMenuForMusicRoom();
            if (!musicRoomPreviewSource) musicRoomPreviewSource = CreateMusicRoomPreviewSource();
            musicRoomPreviewSource.Stop(); musicRoomPreviewSource.clip = entry.clip;
            musicRoomPreviewId = entry.id; musicRoomPreviewPaused = false;
            if (PlaybackAllowed) { musicRoomPreviewSource.enabled = true; musicRoomPreviewSource.Play(); }
            else musicRoomPreviewSource.enabled = false;
            NativeTelemetry.Event("audio.musicRoom.preview.changed", "applied", PlaybackAllowed ? "playRequested" : "mutedSelection", entry.id);
            return true;
        }
        void SetMusicRoomPreviewPaused(bool value)
        {
            bool changed = musicRoomPreviewPaused != value;
            musicRoomPreviewPaused = value;
            if (!musicRoomPreviewSource || !PlaybackAllowed)
            {
                if (changed) NativeTelemetry.Event("audio.musicRoom.previewPause.changed", "applied", !musicRoomPreviewSource ? "sourceUnavailable" : "mutedSelection");
                return;
            }
            if (value) musicRoomPreviewSource.Pause(); else musicRoomPreviewSource.UnPause();
            if (changed) NativeTelemetry.Event("audio.musicRoom.previewPause.changed", "applied", value ? "paused" : "resumed");
        }
        AudioSource CreateMusicRoomPreviewSource()
        {
            var root = new GameObject("MusicSource (original menu2 source662)");
            root.layer = 5; root.transform.SetParent(transform, false);
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false; source.clip = null; source.volume = 1; source.pitch = 1;
            source.loop = false; source.mute = false; source.spatialize = false; source.spatializePostEffects = false;
            source.priority = 100; source.dopplerLevel = 0; source.minDistance = 1000; source.maxDistance = 5000;
            source.panStereo = 0; source.rolloffMode = AudioRolloffMode.Linear; source.spatialBlend = 0;
            source.spread = 0; source.reverbZoneMix = 1;
            source.bypassEffects = false; source.bypassListenerEffects = false; source.bypassReverbZones = false;
            source.ignoreListenerPause = false; source.ignoreListenerVolume = false;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, MusicRoomCurve(MusicRoomKey(0, 1), MusicRoomKey(1, 0)));
            source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, MusicRoomCurve(MusicRoomKey(0, 0)));
            source.SetCustomCurve(AudioSourceCurveType.Spread, MusicRoomCurve(MusicRoomKey(0, 0)));
            source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, MusicRoomCurve(MusicRoomKey(0, 1)));
            Route(source, 1);
            return source;
        }
        static Keyframe MusicRoomKey(float time, float value)
        {
            return new Keyframe(time, value, 0, 0, .3333333432674408f, .3333333432674408f) { weightedMode = WeightedMode.None };
        }
        static AnimationCurve MusicRoomCurve(params Keyframe[] keys)
        {
            return new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }
        void ResetMusicRoomAudio()
        {
            StopMusicRoomPreview(); musicRoomMenuHeld = false; musicRoomMenuProbe = null;
        }
    }
}
