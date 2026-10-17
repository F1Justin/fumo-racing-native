using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed class NativeSnowmanDamageTrigger : MonoBehaviour
    {
        public static event System.Action<NativeSnowmanDamageTrigger, Collider, bool> PhysicalEnterObserved;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetContactObservers() => PhysicalEnterObserved = null;

        public bool useDamageDuration;
        public float damageDuration;
        public string sourceComponentId, sourceSiblingPath;
        void OnTriggerEnter(Collider other)
        {
            var target = other.GetComponent<NativeSnowmanDestructable>();
            if (target == null) return;
            PhysicalEnterObserved?.Invoke(this, other, false);
            if (useDamageDuration) target.TakeDamage(damageDuration);
            else target.TakeDamage();
            PhysicalEnterObserved?.Invoke(this, other, true);
        }
    }
}
