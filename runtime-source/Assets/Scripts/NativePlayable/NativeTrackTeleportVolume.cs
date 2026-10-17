using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeTrackTeleportVolume : MonoBehaviour
    {
        public Transform target;
        public OcclusionPortal portalToOpen;
        public int Teleports { get; private set; }
        NativeRace race;

        void Awake() => race = FindObjectOfType<NativeRace>();

        void OnTriggerEnter(Collider other)
        {
            var motor = other.GetComponentInParent<NativeKartMotor>();
            if (!motor || !motor.driving || !target || !race || race.racers[0] != motor) return;
            if (portalToOpen) portalToOpen.open = true;
            motor.SetHitFrozen(true);
            motor.SetHitFrozen(false);
            motor.TeleportTrack(target.position, target.rotation);
            Teleports++;
        }
    }
}
