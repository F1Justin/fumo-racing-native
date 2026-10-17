using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeUfoBox : MonoBehaviour
    {
        public NativeUfoCatalog catalog;
        public GameObject mesh;
        public Renderer[] tintedRenderers;
        public float rotationSpeed, bobAmplitude, bobRate;
        Vector3 meshOrigin;
        public int colorIndex;
        public bool working;
        public bool Spawned { get; private set; } = true;
        public int PickupCount { get; private set; }
        public int PlayerTriggerEntries { get; private set; }
        public int TriggerPickups { get; private set; }
        public int LastPickupColor { get; private set; } = -1;
        public float respawnTime = -1;
        float switchElapsed, respawnRemaining;
        MaterialPropertyBlock block;
        public void ResetBox(int color)
        {
            colorIndex = color; switchElapsed = respawnRemaining = 0; Spawned = true; PickupCount = PlayerTriggerEntries = TriggerPickups = 0; LastPickupColor = -1;
            if (mesh) mesh.SetActive(true); Tint();
        }
        void Awake() { if (mesh) { meshOrigin = mesh.transform.localPosition; mesh.transform.Rotate(0, UnityEngine.Random.Range(0f, 360f), 0); } }
        void FixedUpdate()
        {
            if (!working || !mesh) return;
            mesh.transform.localPosition = meshOrigin + Vector3.up * (Mathf.Sin(Time.time * bobRate) * bobAmplitude);
            mesh.transform.Rotate(0, rotationSpeed * Time.fixedDeltaTime, 0);
        }
        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            PlayerTriggerEntries++;
            var player = other.GetComponent<NativeUfoPlayer>(); if (player && Pickup(player)) TriggerPickups++;
        }
        public bool Pickup(NativeUfoPlayer player)
        {
            if (!working || !Spawned || !player || !player.active || !player.inventory || !player.inventory.motor || !player.inventory.motor.driving) return false;
            LastPickupColor = colorIndex;
            if (colorIndex == catalog.randomColor) player.inventory.TryGiveRandom();
            else player.Collect(colorIndex);
            // The original trigger consumes the box even when the receiving inventory is full.
            Spawned = false; PickupCount++; if (mesh) mesh.SetActive(false);
            if (!catalog.respawns) { gameObject.SetActive(false); return true; }
            respawnRemaining = respawnTime < 0 ? catalog.respawnTime : respawnTime;
            if (respawnRemaining <= 0) Respawn();
            return true;
        }
        void Update() { Advance(Time.deltaTime); }
        public void Advance(float delta)
        {
            if (!working || !catalog || delta <= 0) return;
            switchElapsed += delta;
            if (switchElapsed >= catalog.switchTime)
            {
                colorIndex = NativeUfoRules.NextColor(colorIndex, catalog.colors.Length);
                switchElapsed -= catalog.switchTime;
            }
            if (!Spawned && catalog.respawns) { respawnRemaining -= delta; if (respawnRemaining <= 0) Respawn(); }
            Tint();
        }
        void Respawn() { Spawned = true; if (mesh) mesh.SetActive(true); }
        void Tint()
        {
            if (!catalog || tintedRenderers == null) return;
            Color color = colorIndex == catalog.randomColor ? catalog.randomDisplayColor : catalog.colors[Mathf.Clamp(colorIndex, 0, catalog.colors.Length - 1)];
            if (switchElapsed >= catalog.warnTime && catalog.warnRate > 0)
            {
                float glow = Mathf.PingPong((switchElapsed - catalog.warnTime) / catalog.warnRate, 1f) * .75f;
                color = new Color(Mathf.Clamp01(color.r + glow), Mathf.Clamp01(color.g + glow), Mathf.Clamp01(color.b + glow), color.a);
            }
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var renderer in tintedRenderers) if (renderer) { renderer.GetPropertyBlock(block); block.SetColor("_Color", color); block.SetColor("_BaseColor", color); renderer.SetPropertyBlock(block); }
        }
    }
}
