using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeItemDestructionContext
    {
        public static bool IsSceneLoading { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => IsSceneLoading = false;
    }

    public sealed class NativeItemDestruction : MonoBehaviour
    {
        public GameObject effectPrefab;
        public string sourceComponentId;
        public string sourceSiblingPath;
        public bool ApplicationClosed { get; private set; }
        public int SpawnCount { get; private set; }
        public GameObject LastSpawn { get; private set; }

        void OnApplicationQuit() => ApplicationClosed = true;

        void OnDestroy()
        {
            if (!Application.isPlaying || ApplicationClosed || NativeItemDestructionContext.IsSceneLoading) return;
            LastSpawn = Instantiate(effectPrefab, transform.position, transform.rotation);
            NativeParticleQualityRegistry.RegisterHierarchy(LastSpawn);
            SpawnCount++;
        }
    }
}
