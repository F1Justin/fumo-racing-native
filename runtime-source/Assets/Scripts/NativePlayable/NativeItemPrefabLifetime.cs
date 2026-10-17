using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeItemPrefabLifetime : MonoBehaviour
    {
        public float duration;
        public string sourceComponentId, sourceSiblingPath;
        public bool StartScheduled { get; private set; }
        public float StartedAt { get; private set; }

        void Start()
        {
            StartScheduled = true;
            StartedAt = Time.time;
            Destroy(gameObject, duration);
        }
    }
}
