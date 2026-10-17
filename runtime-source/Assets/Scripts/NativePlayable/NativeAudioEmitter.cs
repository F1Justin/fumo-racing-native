using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeAudioEmitter : MonoBehaviour
    {
        public AudioSource[] sources;
        public bool[] originalPlayOnAwake;
        public int[] groups;
        bool applied;
        public static void ReapplyHierarchy(GameObject root, bool activeOnly = false)
        {
            foreach (var emitter in root.GetComponentsInChildren<NativeAudioEmitter>(true))
                if (!activeOnly || emitter.isActiveAndEnabled) emitter.Apply();
            if (NativeAudioRuntime.Muted)
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true)) { source.Stop(); source.enabled = false; }
        }
        void OnEnable() { applied = false; Apply(); }
        void LateUpdate() { if (!applied && !NativeAudioRuntime.Muted) Apply(); }
        void Apply()
        {
            if (sources == null) return;
            bool playAwake = !applied;
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i]; if (!source) continue;
                NativeAudioRuntime.Route(source, groups[i]);
                if (NativeAudioRuntime.Muted) { source.Stop(); source.enabled = false; }
                else { source.enabled = true; if (playAwake && originalPlayOnAwake[i] && source.clip) source.Play(); }
            }
            applied = !NativeAudioRuntime.Muted;
        }
        void OnDisable() { if (sources != null) foreach (var source in sources) if (source) source.Stop(); }
    }
}
