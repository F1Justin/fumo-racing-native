using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeMenuCharacter
    {
        public string name;
        public GameObject model;
        public Color primary, secondary;
    }

    [Serializable]
    public sealed class NativeMenuVehicle
    {
        public string name;
        public GameObject model;
        public float height;
    }

    public sealed class NativeMenuRandomDisplay : MonoBehaviour
    {
        public Transform[] anchors;
        public NativeMenuCharacter[] characters;
        public NativeMenuVehicle[] vehicles;
        public string[] excludedCharacters = Array.Empty<string>();
        public int Generation { get; private set; }
        public string[] DisplayedCharacters { get; private set; }
        public string[] DisplayedVehicles { get; private set; }
        bool initialized;

        public void Populate()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool displayTest = args.Contains("--menu-display-test");
            if (!initialized && int.TryParse(Argument(args, "--menu-seed"), out int seed)) UnityEngine.Random.InitState(seed);
            initialized = true;
            string[] forced = displayTest ? (Argument(args, "--menu-characters") ?? "").Split(',') : Array.Empty<string>();
            string[] forcedVehicles = displayTest ? (Argument(args, "--menu-vehicles") ?? "").Split(',') : Array.Empty<string>();
            var eligible = characters.Where(character => !excludedCharacters.Contains(character.name)
                && ((character.name != "Aya" && character.name != "Frog")
                    || NativeSaveStore.IsUnlocked(character.name.ToLowerInvariant()))).ToList();
            if (eligible.Count < anchors.Length || vehicles.Length != 4) throw new InvalidOperationException("Original menu selection pool is incomplete");
            DisplayedCharacters = new string[anchors.Length];
            DisplayedVehicles = new string[anchors.Length];
            int poolSize = eligible.Count;
            for (int slot = 0; slot < anchors.Length; slot++)
            {
                var character = eligible[UnityEngine.Random.Range(0, eligible.Count)];
                var vehicle = vehicles[UnityEngine.Random.Range(0, 4)];
                if (slot < forced.Length && !string.IsNullOrEmpty(forced[slot]))
                    character = eligible.Single(candidate => candidate.name == forced[slot]);
                if (slot < forcedVehicles.Length && !string.IsNullOrEmpty(forcedVehicles[slot]))
                    vehicle = vehicles.Single(candidate => candidate.name == forcedVehicles[slot]);
                eligible.Remove(character);
                var anchor = anchors[slot];
                for (int i = anchor.childCount - 1; i >= 0; i--)
                {
                    var previous = anchor.GetChild(i).gameObject;
                    previous.SetActive(false);
                    Destroy(previous);
                }
                Spawn(character.model, anchor, vehicle.height);
                var kart = Spawn(vehicle.model, anchor, vehicle.height);
                Paint(kart, character.primary, character.secondary);
                DisplayedCharacters[slot] = character.name;
                DisplayedVehicles[slot] = vehicle.name;
            }
            Generation++;
            var report = new DisplayReport { generation = Generation, poolSize = poolSize, characters = DisplayedCharacters,
                vehicles = DisplayedVehicles, forced = displayTest && forced.Any(value => !string.IsNullOrEmpty(value)) };
            string json = JsonUtility.ToJson(report, true);
            Debug.Log("NATIVE_MENU_RANDOMIZED " + JsonUtility.ToJson(report));
            string directory = Argument(args, "--report-dir");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "menu-display-" + Generation + ".json"), json);
            }
        }

        static GameObject Spawn(GameObject prefab, Transform anchor, float height)
        {
            var instance = Instantiate(prefab, anchor);
            instance.transform.localPosition = Vector3.up * height;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        static void Paint(GameObject kart, Color primary, Color secondary)
        {
            primary.a = secondary.a = 1;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in kart.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    var material = materials[index];
                    if (!material || !material.HasProperty("_KartMask") || material.GetFloat("_KartMask") < 0.5f) continue;
                    renderer.GetPropertyBlock(block, index);
                    block.SetColor("_Color", primary);
                    block.SetColor("_Color2", secondary);
                    renderer.SetPropertyBlock(block, index);
                    block.Clear();
                }
            }
        }

        static string Argument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        [Serializable]
        sealed class DisplayReport { public int generation, poolSize; public string[] characters, vehicles; public bool forced; }
    }
}
