using UnityEngine;

namespace Fumo.NativePlayable
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class NativeCircuitGate : MonoBehaviour
    {
        public NativeRace race;
        public int index;

        void OnTriggerExit(Collider other)
        {
            if (!race || !other) return;
            var motor = other.GetComponent<NativeKartMotor>();
            if (!motor) return;
            bool forward = transform.InverseTransformPoint(other.transform.position).z >= 0f;
            race.CrossCircuitGate(motor, index, forward);
        }
    }
}
