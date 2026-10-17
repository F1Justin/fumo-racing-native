using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartItemBox : MonoBehaviour
    {
        public GameObject mesh;
        public float respawnTime = 2f;
        public bool working = true;
        public NativeKartItemKind ForcedKind;
        public bool isFixed;
        public NativeKartItemKind fixedKind;
        public bool Spawned { get; private set; } = true;
        public int PickupCount { get; private set; }
        float respawnRemaining;
        bool originalRespawnCaptured;
        float originalRespawnTime;
        public void ApplyRespawnMode(bool instant)
        {
            if (!originalRespawnCaptured) { originalRespawnTime = respawnTime; originalRespawnCaptured = true; }
            respawnTime = instant ? 0 : originalRespawnTime;
        }

        void Update()
        {
            if (Spawned || Time.timeScale <= 0f) return;
            respawnRemaining -= Time.deltaTime;
            if (respawnRemaining <= 0f) Respawn();
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var inventory = other.GetComponent<NativeKartItemInventory>();
            if (inventory) Pickup(inventory);
        }

        public bool Pickup(NativeKartItemInventory inventory)
        {
            if (!working || !Spawned || !inventory || !inventory.motor || !inventory.motor.driving)
                return false;
            if (ForcedKind != NativeKartItemKind.None) inventory.TryGive(ForcedKind);
            else if (isFixed) inventory.TryGive(fixedKind, false);
            else inventory.TryGiveRandom();
            Spawned = false;
            PickupCount++;
            if (mesh) mesh.SetActive(false);
            respawnRemaining = respawnTime;
            if (respawnTime <= 0f) Respawn();
            return true;
        }

        void Respawn()
        {
            Spawned = true;
            respawnRemaining = 0f;
            if (mesh) mesh.SetActive(true);
        }

        public void ResetBox()
        {
            Respawn();
            PickupCount = 0;
        }
    }
}
