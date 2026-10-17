using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeInsanityHudRouter : MonoBehaviour
    {
        public NativeKartMotor[] humanPlayers = new NativeKartMotor[0];
        public Transform[] hudCanvases = new Transform[0];

        public bool Initialized { get; private set; }
        public void Bind(NativeKartMotor[] players, Transform[] canvases)
        {
            if (players == null || canvases == null || players.Length != canvases.Length) throw new System.ArgumentException("Human/HUD slots differ");
            for (int i = 0; i < players.Length; i++) if (!players[i] || !canvases[i]) throw new System.ArgumentException("Missing human/HUD slot");
            humanPlayers = (NativeKartMotor[])players.Clone(); hudCanvases = (Transform[])canvases.Clone(); Initialized = true;
        }

        public void Clear()
        { humanPlayers = new NativeKartMotor[0]; hudCanvases = new Transform[0]; Initialized = false; }

        public int FindHumanSlot(NativeKartMotor player) => NativeInsanityHud.FindHumanSlot(player, humanPlayers);

        public GameObject SpawnForPlayer(NativeInsanityHudSource source, NativeKartMotor player)
        {
            int index = FindHumanSlot(player);
            return source.Spawn(hudCanvases[index]);
        }
    }
}
