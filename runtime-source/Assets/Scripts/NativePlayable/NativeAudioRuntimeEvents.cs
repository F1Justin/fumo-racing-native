using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAudioRuntime
    {
        static readonly HashSet<NativeEventAudio> eventAudio = new HashSet<NativeEventAudio>();
        public static int EventPlayCount { get; private set; }
        public static string LastEventId { get; private set; }
        public static AudioSource LastEventSource { get; private set; }
        public static AudioClip LastEventClip { get; private set; }
        static void ResetEventAudio() { eventAudio.Clear(); EventPlayCount = 0; LastEventId = null; LastEventSource = null; LastEventClip = null; }
        internal static void RegisterEventAudio(NativeEventAudio owner) => eventAudio.Add(owner);
        internal static void UnregisterEventAudio(NativeEventAudio owner) => eventAudio.Remove(owner);
        static void PauseEventAudio(bool pause)
        {
            foreach (var owner in eventAudio) if (owner) owner.PauseChannels(pause);
        }
        public static AudioSource PlayEvent(string id, Transform owner, float pitchMultiplier = 1f)
        {
            if (!instance || !instance.PlaybackAllowed || !owner)
            {
                NativeTelemetry.Count(!instance ? "audio.event.runtimeUnavailable" : !instance.PlaybackAllowed ? "audio.event.muted" : "audio.event.ownerUnavailable");
                return null;
            }
            var entry = Catalog ? Catalog.FindEvent(id) : null;
            if (entry == null || !entry.template || !entry.clip)
            {
                LastError = "Original audio event absent: " + id;
                NativeTelemetry.Count("audio.event.resourceMissing");
                Debug.LogError(LastError);
                return null;
            }
            var emitter = owner.GetComponent<NativeEventAudio>() ?? owner.gameObject.AddComponent<NativeEventAudio>();
            var source = emitter.Channel(entry, true);
            source.enabled = true;
            source.pitch = entry.template.pitch * pitchMultiplier;
            if (entry.oneShot) source.PlayOneShot(entry.clip);
            else { source.clip = entry.clip; source.Play(); }
            EventPlayCount++; LastEventId = id; LastEventSource = source; LastEventClip = entry.clip;
            NativeTelemetry.Count("audio.event.accepted");
            return source;
        }
        public static void StopEvent(string id, Transform owner)
        {
            if (!owner || !Catalog) return;
            var entry = Catalog.FindEvent(id);
            var emitter = owner.GetComponent<NativeEventAudio>();
            var source = entry != null && emitter ? emitter.Channel(entry, false) : null;
            if (source) { source.Stop(); NativeTelemetry.Count("audio.event.stopped"); }
        }
    }
}
