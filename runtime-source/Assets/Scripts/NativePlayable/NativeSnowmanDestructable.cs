using UnityEngine;
using UnityEngine.Events;

namespace Fumo.NativePlayable
{
    public interface NativeSceneDamageable
    {
        float TakeDamage(float damage);
        float TakeDamage();
    }

    public sealed class NativeSnowmanDestructable : MonoBehaviour, NativeSceneDamageable
    {
        public static event System.Action<NativeSnowmanDestructable, Collider> PhysicalStayObserved;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetContactObservers() => PhysicalStayObserved = null;

        public float speedRequired;
        public float respawnTime = 30f;
        public UnityEvent onDestructed = new UnityEvent();
        public UnityEvent onRespawn = new UnityEvent();
        public string sourceComponentId;
        bool isAlive = true;
        public bool IsAlive => isAlive;
        public int DestructionCount { get; private set; }
        public int RespawnCount { get; private set; }
        public float LastDestructionTime { get; private set; }

        void OnTriggerStay(Collider other)
        {
            PhysicalStayObserved?.Invoke(this, other);
            if (!isAlive || !other.CompareTag("Player")) return;
            var controller = other.GetComponent<CharacterController>();
            Vector3 normalized = controller.velocity.normalized;
            float dot = Vector3.Dot(normalized, other.transform.forward);
            Vector3 projectedVelocity = controller.velocity * dot;
            if (projectedVelocity.sqrMagnitude >= speedRequired * speedRequired) Die();
        }

        void Die()
        {
            isAlive = false;
            CancelInvoke();
            Invoke(nameof(Respawn), respawnTime);
            DestructionCount++;
            LastDestructionTime = Time.time;
            onDestructed?.Invoke();
        }

        public float TakeDamage(float damage)
        {
            Die();
            return 0f;
        }
        public float TakeDamage()
        {
            Die();
            return 0f;
        }
        void Respawn()
        {
            isAlive = true;
            RespawnCount++;
            onRespawn?.Invoke();
        }
    }
}
