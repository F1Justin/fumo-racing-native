using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Fumo.NativePlayable
{
    public sealed class NativeAudioCatalog : ScriptableObject
    {
        [Serializable] public sealed class MusicEntry
        {
            public string id, title, source;
            public AudioClip clip;
            public float bpm, fadeOutDuration;
            public int loopStart, loopEnd, endStart;
            public bool useFadeOut;
            public AnimationCurve fadeOutCurve;
        }
        [Serializable] public sealed class VehicleEntry
        {
            public string id, source;
            public AudioClip idle, accel, jump, land;
            public bool silentOnAirborne;
        }
        [Serializable] public sealed class SurfaceEntry { public string id, source; public AudioClip clip; }
        [Serializable] public sealed class EventEntry
        {
            public string id, family, source;
            public AudioSource template;
            public AudioClip clip;
            public bool oneShot;
        }
        public EventEntry[] events = Array.Empty<EventEntry>();
        public EventEntry FindEvent(string id) => Array.Find(events, entry => entry.id == id);
        public SurfaceEntry[] surfaces = Array.Empty<SurfaceEntry>();
        public AudioSource driftTemplate, surfaceTemplate;
        public AnimationCurve selectionIn, selectionOut;
        public float selectionTransitionTime;
        public AudioSource idleTemplate, accelTemplate, sfxTemplate, countdownTemplate;
        public AudioClip countdownSecond, countdownEnd;
        public AudioMixer mixer;
        public AudioMixerGroup[] groups;
        public MusicEntry[] music = Array.Empty<MusicEntry>();
        public VehicleEntry[] vehicles = Array.Empty<VehicleEntry>();
        public float maxRevPerGear = 2, max = 1, minSpeed = 15;
        public int gearsMax = 4;
        public MusicEntry FindMusic(string id) => Array.Find(music, entry => entry.id == id);
        public VehicleEntry FindVehicle(string character, string vehicle) =>
            Array.Find(vehicles, entry => string.Equals(entry.id, character + vehicle, StringComparison.OrdinalIgnoreCase))
            ?? Array.Find(vehicles, entry => string.Equals(entry.id, vehicle, StringComparison.OrdinalIgnoreCase));
    }
}
