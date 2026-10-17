using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeInsanityHud
    {
        public static int FindHumanSlot(object player, object[] humanPlayers)
        {
            for (int index = 0; index < humanPlayers.Length; index++)
                if (humanPlayers[index] != null && ReferenceEquals(humanPlayers[index], player)) return index;
            return -1;
        }

        public static GameObject Spawn(GameObject prefab, Transform playerHudRoot)
        {
            return Object.Instantiate(prefab, playerHudRoot, false);
        }
    }

    public sealed class NativeInsanityHudSource : MonoBehaviour
    {
        public GameObject hudPrefab;
        public string sourceHudRootName;
        public string sourceComponentId;
        public string sourceSiblingPath;
        public int SpawnCount { get; private set; }
        public GameObject LastSpawn { get; private set; }

        public GameObject Spawn(Transform playerHudRoot)
        {
            if (string.IsNullOrEmpty(sourceHudRootName)) throw new System.InvalidOperationException("Original Insanity HUD root name missing");
            LastSpawn = NativeInsanityHud.Spawn(hudPrefab, playerHudRoot);
            LastSpawn.name = sourceHudRootName;
            SpawnCount++;
            return LastSpawn;
        }
    }
}
