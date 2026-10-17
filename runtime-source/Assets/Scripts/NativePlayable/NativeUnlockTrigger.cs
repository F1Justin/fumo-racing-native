using UnityEngine;

namespace Fumo.NativePlayable
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class NativeUnlockTrigger : MonoBehaviour
    {
        public NativeRace race;
        public int HumanEntries { get; private set; }
        void OnTriggerEnter(Collider other)
        {
            if (!race || !other.CompareTag("Player")) return;
            var motor = other.GetComponent<NativeKartMotor>();
            if (motor && race.IsHumanRacer(motor)) { HumanEntries++; race.EnterFrogTrigger(motor); }
        }
    }
}
