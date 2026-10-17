using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeEventAudio : MonoBehaviour
    {
        readonly Dictionary<string, AudioSource> channels = new Dictionary<string, AudioSource>();
        void OnEnable() => NativeAudioRuntime.RegisterEventAudio(this);
        void OnDestroy() => NativeAudioRuntime.UnregisterEventAudio(this);
        public AudioSource Channel(NativeAudioCatalog.EventEntry entry, bool create)
        {
            if (channels.TryGetValue(entry.family, out var source) && source) return source;
            if (!create) return null;
            source = Instantiate(entry.template, transform);
            source.name = "Original event " + entry.family;
            source.enabled = true;
            source.playOnAwake = false;
            channels[entry.family] = source;
            return source;
        }
        public void PauseChannels(bool pause)
        {
            foreach (var source in channels.Values)
                if (source && !source.ignoreListenerPause) { if (pause) source.Pause(); else source.UnPause(); }
        }
    }
}
