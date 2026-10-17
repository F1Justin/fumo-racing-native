using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeTrackFallVolume : MonoBehaviour
    {
        public bool water;
        public bool frozen;
        public GameObject frozenEffectPrefab;

        void OnTriggerEnter(Collider other)
        {
            NativeBridgeSweepTrace.Enter(this, other);
            if (!other.CompareTag("Player")) return;
            var motor = other.GetComponent<NativeKartMotor>();
            if (!motor || !motor.driving || (water && motor.Waterproof)) return;
            var recovery = motor.GetComponent<NativeKartRecovery>();
            if (!recovery) return;
            bool accepted = frozen ? recovery.RequestFrozen(frozenEffectPrefab) : recovery.Request();
            if (accepted) NativeTelemetry.Event("vehicle.fall", "accepted", detail: "entity=" + motor.GetInstanceID() + ";water=" + water + ";frozen=" + frozen);
            if (accepted) Debug.Log("NATIVE_TRACK_FALL volume=" + name + " racer=" + motor.name
                + " water=" + water + " frozen=" + frozen + " position=" + motor.transform.position);
        }
    }
}
