using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartParticleLifetime : MonoBehaviour
    {
        public float duration;
        void Start() => Destroy(gameObject, duration);
    }
}
