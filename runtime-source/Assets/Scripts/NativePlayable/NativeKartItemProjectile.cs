using UnityEngine;
using System.Collections.Generic;

namespace Fumo.NativePlayable
{
    public interface INativeKartDamageReceiver { bool TakeDamage(); }

    public sealed class NativeKartItemProjectile : MonoBehaviour
    {
        static readonly List<NativeKartItemProjectile> live = new List<NativeKartItemProjectile>();
        public float gravity = 25f;
        public float bounce = 1f;
        public float stickForce = 45f;
        public float duration = 6f;

        CharacterController body;
        NativeKartMotor owner;
        Vector3 velocity;
        float remaining;
        bool carried;
        bool initialized;
        bool wallHit;
        Vector3 wallNormal;
        public bool HasHit { get; private set; }
        public Vector3 Velocity => velocity;
        public NativeKartMotor Owner => owner;

        void Awake()
        {
            body = GetComponent<CharacterController>();
        }

        void OnEnable() { if (!live.Contains(this)) live.Add(this); }
        void OnDestroy() { live.Remove(this); }

        public static void ClearForOwner(NativeKartMotor source)
        {
            NativeKartItemVisual.ClearEffectsForOwner(source);
            for (int i = live.Count - 1; i >= 0; i--)
                if (live[i] && live[i].owner == source)
                {
                    var visual = live[i].GetComponent<NativeKartItemVisual>();
                    if (visual) visual.SuppressDestroyEffect();
                    Destroy(live[i].gameObject);
                }
        }

        public void Init(NativeKartMotor source, Vector3 initialVelocity, bool isCarried)
        {
            owner = source;
            var visual = GetComponent<NativeKartItemVisual>();
            if (visual) visual.owner = source;
            velocity = initialVelocity;
            carried = isCarried;
            remaining = duration;
            initialized = true;
            if (carried && owner)
            {
                var ownerCollider = owner.GetComponent<Collider>();
                if (ownerCollider)
                    foreach (var collider in GetComponentsInChildren<Collider>(true))
                        Physics.IgnoreCollision(collider, ownerCollider, true);
            }
            if (body) body.enabled = true;
        }

        void LateUpdate()
        {
            if (!initialized || carried || Time.timeScale <= 0f) return;
            float dt = Time.deltaTime;
            remaining -= dt;
            if (remaining <= 0f) { Destroy(gameObject); return; }
            if (!body) return;
            if (body.isGrounded) velocity.y = -stickForce;
            else velocity.y -= gravity * dt;
            body.Move(velocity * dt);
            if (wallHit)
            {
                Vector3 horizontalNormal = new Vector3(wallNormal.x, 0f, wallNormal.z).normalized;
                velocity = Vector3.Reflect(velocity, horizontalNormal) * bounce;
            }
            wallHit = false;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider.CompareTag("Item")) return;
            if (Vector3.Angle(hit.normal, Vector3.up) > body.slopeLimit)
            {
                wallNormal = hit.normal;
                wallHit = true;
            }
        }

        public void Hit(Collider other)
        {
            if (HasHit || !other || !other.CompareTag("Player")) return;
            INativeKartDamageReceiver target = null;
            foreach (var behaviour in other.GetComponents<MonoBehaviour>())
                if (behaviour is INativeKartDamageReceiver receiver) { target = receiver; break; }
            if (target == null) return;
            HasHit = true;
            target.TakeDamage();
            Destroy(gameObject);
        }
    }

}
