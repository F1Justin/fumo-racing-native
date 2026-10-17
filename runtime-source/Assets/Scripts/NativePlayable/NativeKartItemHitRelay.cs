using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartItemHitRelay : MonoBehaviour
    {
        public NativeKartItemProjectile projectile;
        void OnTriggerEnter(Collider other) { if (projectile) projectile.Hit(other); }
    }
}
