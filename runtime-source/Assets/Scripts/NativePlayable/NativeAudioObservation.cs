using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeAudioObservation
    {
        [Serializable] public sealed class ListenerState
        {
            public int instanceId, sceneHandle;
            public string path, scene;
            public bool enabled, active, activeAndEnabled;
        }
        [Serializable] public sealed class Snapshot
        {
            public string outputMode, observationError, speakerMode;
            public bool captured, isBatchMode, muted, verificationListenerSilenced, listenerPause;
            public int configurationSampleRate, configurationDspBufferSize, realVoices, virtualVoices;
            public int outputSampleRate, dspBufferFrames, dspBufferCount, listenerCount, activeEnabledListenerCount;
            public double dspTime;
            public float realtimeSeconds, listenerVolume;
            public ListenerState[] listeners;
        }
        public static Snapshot Capture(string outputMode)
        {
            var snapshot = new Snapshot { outputMode = outputMode };
            try
            {
                var configuration = AudioSettings.GetConfiguration();
                snapshot.configurationSampleRate = configuration.sampleRate;
                snapshot.configurationDspBufferSize = configuration.dspBufferSize;
                snapshot.speakerMode = configuration.speakerMode.ToString();
                snapshot.realVoices = configuration.numRealVoices;
                snapshot.virtualVoices = configuration.numVirtualVoices;
                snapshot.outputSampleRate = AudioSettings.outputSampleRate;
                AudioSettings.GetDSPBufferSize(out int frames, out int count);
                snapshot.dspBufferFrames = frames; snapshot.dspBufferCount = count;
                snapshot.dspTime = AudioSettings.dspTime;
                snapshot.realtimeSeconds = Time.realtimeSinceStartup;
                snapshot.isBatchMode = Application.isBatchMode;
                snapshot.muted = NativeAudioRuntime.Muted;
                snapshot.verificationListenerSilenced = NativeAudioRuntime.VerificationListenerSilenced;
                snapshot.listenerVolume = AudioListener.volume;
                snapshot.listenerPause = AudioListener.pause;
                snapshot.listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true)
                    .OrderBy(listener => listener.GetInstanceID()).Select(listener => new ListenerState {
                        instanceId = listener.GetInstanceID(), sceneHandle = listener.gameObject.scene.handle,
                        scene = listener.gameObject.scene.name, path = HierarchyPath(listener.transform),
                        enabled = listener.enabled, active = listener.gameObject.activeInHierarchy,
                        activeAndEnabled = listener.isActiveAndEnabled
                    }).ToArray();
                snapshot.listenerCount = snapshot.listeners.Length;
                snapshot.activeEnabledListenerCount = snapshot.listeners.Count(listener => listener.activeAndEnabled);
                snapshot.captured = true;
            }
            catch (Exception error) { snapshot.observationError = error.ToString(); }
            return snapshot;
        }
        static string HierarchyPath(Transform node)
        {
            string path = node.name;
            while (node.parent) { node = node.parent; path = node.name + "/" + path; }
            return path;
        }
    }
}
