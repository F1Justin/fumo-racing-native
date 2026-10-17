using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeVehicleSkinEntry
    {
        public string id, displayName;
        public GameObject driverModel;
        public bool vehicleColorsOverride;
        public Color kartColorA, kartColorB;
    }

    [Serializable]
    public sealed class NativeVehicleCharacterEntry
    {
        public string id;
        public Sprite icon;
        public GameObject driverModel;
        public NativeVehicleSkinEntry[] skins = Array.Empty<NativeVehicleSkinEntry>();
        public Color kartColorA = Color.white;
        public Color kartColorB = Color.white;
        public GameObject[] vehicleModels = new GameObject[7];
        public float[] vehicleHeights = new float[7];
    }

    [Serializable]
    public sealed class NativeVehicleClassEntry
    {
        public string id;
        public NativeKartSettings settings;
        public NativeVehicleModuleEffectBinding moduleEffects;
    }

    public sealed class NativeVehicleRoster : MonoBehaviour
    {
        public NativeVehicleCharacterEntry[] characters = new NativeVehicleCharacterEntry[0];
        public NativeVehicleClassEntry[] vehicles = new NativeVehicleClassEntry[0];
        public NativeKartSettings RealSkateSettings;
        public GameObject RealSkateModel;
        public int DefaultCharacterIndex = 11;
        public int CharacterCount => characters != null ? characters.Length : 0;
        public int VehicleCount => vehicles != null ? vehicles.Length : 0;
        public string CharacterNameAt(int index) => characters[ClampCharacter(index)].id;
        public string VehicleNameAt(int index) => vehicles[ClampVehicle(index)].id;
        public Sprite CharacterIconAt(int index) => characters[ClampCharacter(index)].icon;
        public string CharacterIdAt(int index) => CharacterNameAt(index).ToLowerInvariant();
        public string VehicleIdAt(int index) => VehicleNameAt(index).ToLowerInvariant();
        public int CharacterIndexOf(string id) => Array.FindIndex(characters, c => string.Equals(c.id, id, StringComparison.OrdinalIgnoreCase));
        public int VehicleIndexOf(string id) => Array.FindIndex(vehicles, v => string.Equals(v.id, id, StringComparison.OrdinalIgnoreCase));
        public int SkinCountAt(int character) => (characters[ClampCharacter(character)].skins?.Length ?? 0) + 1;
        public int ClampSkin(int character, int skin) => Mathf.Clamp(skin, 0, SkinCountAt(character) - 1);
        public string SkinPhraseAt(int character, int skin) => skin == 0 ? "CHAR_DEFAULT" : SkinAt(character, skin).displayName;
        public GameObject DriverModelAt(int character, int skin) => skin == 0 ? characters[ClampCharacter(character)].driverModel : SkinAt(character, skin).driverModel;
        NativeVehicleSkinEntry SkinAt(int character, int skin) => characters[ClampCharacter(character)].skins[ClampSkin(character, skin) - 1];
        public void ColorsAt(int character, int skin, out Color colorA, out Color colorB)
        {
            var entry = characters[ClampCharacter(character)];
            var variant = skin > 0 ? SkinAt(character, skin) : null;
            colorA = variant != null && variant.vehicleColorsOverride ? variant.kartColorA : entry.kartColorA;
            colorB = variant != null && variant.vehicleColorsOverride ? variant.kartColorB : entry.kartColorB;
        }
        public int[] SelectableCharacters() => NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi").nodes
            .Where(n => n.scene == "selection" && n.path.StartsWith("Canvas/CharSelection/CharacterGrid/") && !string.IsNullOrEmpty(n.characterId)).OrderBy(n => n.selectionOrder)
            .Select(n => CharacterIndexOf(n.characterId)).Where(i => i >= 0).Where(i =>
            (CharacterIdAt(i) != "aya" && CharacterIdAt(i) != "frog") || NativeSaveStore.IsUnlocked(CharacterIdAt(i))).ToArray();

        NativeKartMotor activeMotor;
        int activeCharacter = -1, activeVehicle = -1, activeSkin = -1;
        public Color CurrentKartColor
        {
            get
            {
                ColorsAt(activeCharacter >= 0 ? activeCharacter : DefaultCharacterIndex, Mathf.Max(0, activeSkin), out Color color, out Color unused);
                return color;
            }
        }
        GameObject originalVehicle, originalDriver, replacementVehicle, replacementDriver;
        NativeKartWheelVisual wheelVisual;
        NativeKartFeedback kartFeedback;
        float originalSlopeLimit;

        public void ApplySelection(int characterIndex, int vehicleIndex, NativeKartMotor player)
            => ApplySelection(characterIndex, vehicleIndex, 0, player);

        public void ApplySelection(int characterIndex, int vehicleIndex, int skinIndex, NativeKartMotor player)
        {
            if (!player) throw new ArgumentNullException(nameof(player));
            int ci = ClampCharacter(characterIndex), vi = ClampVehicle(vehicleIndex);
            int si = ClampSkin(ci, skinIndex);
            if (activeMotor != player)
            {
                activeMotor = player;
                activeCharacter = DefaultCharacterIndex;
                activeVehicle = 0;
                activeSkin = 0;
                var kart = player.driftPivot.Find("AnimPivot/Kart");
                var fumo = player.driftPivot.Find("AnimPivot/AccelPivot/Accel2Pivot/FumoPivot");
                if (!kart || !fumo || kart.childCount == 0 || fumo.childCount == 0)
                    throw new InvalidOperationException("Original racer model hierarchy is incomplete.");
                originalVehicle = kart.GetChild(0).gameObject;
                originalDriver = fumo.GetChild(0).gameObject;
                wheelVisual = player.GetComponent<NativeKartWheelVisual>();
                kartFeedback = player.GetComponent<NativeKartFeedback>();
                originalSlopeLimit = player.GetComponent<CharacterController>().slopeLimit;
            }
            if (activeCharacter == ci && activeVehicle == vi && activeSkin == si)
            {
                ApplyPhysics(player, vehicles[vi].settings);
                return;
            }
            var character = characters[ci];
            var driverModel = DriverModelAt(ci, si);
            var vehicle = vehicles[vi];
            if (!driverModel || !character.vehicleModels[vi] || vehicle.settings == null)
                throw new InvalidOperationException("Selected original character or vehicle model is missing.");
            if (replacementVehicle) { replacementVehicle.SetActive(false); Destroy(replacementVehicle); }
            if (replacementDriver) { replacementDriver.SetActive(false); Destroy(replacementDriver); }
            bool kartSelected = vi == 0;
            bool originalDriverSelected = ci == DefaultCharacterIndex && kartSelected && si == 0;
            originalVehicle.SetActive(kartSelected);
            originalDriver.SetActive(originalDriverSelected);
            if (!kartSelected)
            {
                var kart = player.driftPivot.Find("AnimPivot/Kart");
                replacementVehicle = Instantiate(character.vehicleModels[vi], kart, false);
                replacementVehicle.name = character.vehicleModels[vi].name;
                replacementVehicle.transform.localPosition = Vector3.up * character.vehicleHeights[vi];
                replacementVehicle.transform.localRotation = Quaternion.identity;
                foreach (var wheels in replacementVehicle.GetComponentsInChildren<NativeKartWheelVisual>(true))
                {
                    wheels.motor = player;
                    wheels.enabled = true;
                }
            }
            if (!originalDriverSelected)
            {
                var fumo = player.driftPivot.Find("AnimPivot/AccelPivot/Accel2Pivot/FumoPivot");
                replacementDriver = Instantiate(driverModel, fumo, false);
                replacementDriver.name = driverModel.name;
                replacementDriver.transform.localPosition = Vector3.up * character.vehicleHeights[vi];
                replacementDriver.transform.localRotation = Quaternion.identity;
            }
            ColorsAt(ci, si, out var colorA, out var colorB);
            ApplyKartColors(kartSelected ? originalVehicle : replacementVehicle, colorA, colorB);
            if (kartSelected)
            {
                if (kartFeedback && kartFeedback.settings != null)
                {
                    kartFeedback.settings._color = colorA;
                    kartFeedback.settings._color2 = colorB;
                    kartFeedback.Reset();
                }
            }
            if (wheelVisual) wheelVisual.enabled = kartSelected;
            if (kartFeedback) kartFeedback.enabled = kartSelected;
            // Register after model configuration/colors so authored particle baselines are complete.
            if (replacementVehicle) NativeParticleQualityRegistry.RegisterHierarchy(replacementVehicle);
            if (replacementDriver) NativeParticleQualityRegistry.RegisterHierarchy(replacementDriver);
            ApplyPhysics(player, vehicle.settings);
            activeCharacter = ci;
            activeVehicle = vi;
            activeSkin = si;
        }

        void ApplyPhysics(NativeKartMotor player, NativeKartSettings settings)
        {
            if (player.stats != settings)
            {
                player.StopWheelie();
                player.ResetWheelieInput();
                player.ResetVehicleModule();
            }
            var moduleFx = player.GetComponent<NativeVehicleModuleEffects>();
            if (!moduleFx) moduleFx = player.gameObject.AddComponent<NativeVehicleModuleEffects>();
            var selected = vehicles.FirstOrDefault(entry => entry.settings == settings);
            moduleFx.binding = selected?.moduleEffects;
            player.stats = settings;
            player.OriginalHudBaseTopSpeed = settings._topSpeed;
            var body = player.GetComponent<CharacterController>();
            if (body) body.slopeLimit = Mathf.Max(originalSlopeLimit, settings._slopeLimit);
            var ai = player.GetComponent<NativeKartOriginalAI>();
            if (ai) ai.defTopSpeed = settings._topSpeed;
        }

        static void ApplyKartColors(GameObject model, Color colorA, Color colorB)
        {
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (!source || !source.HasProperty("_KartMask") || source.GetFloat("_KartMask") < 0.5f) continue;
                    var colors = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(colors, i);
                    colors.SetColor("_Color", colorA);
                    colors.SetColor("_Color2", colorB);
                    renderer.SetPropertyBlock(colors, i);
                }
            }
        }

        int ClampCharacter(int index)
        {
            if (CharacterCount == 0) throw new InvalidOperationException("Character roster is empty.");
            return Mathf.Clamp(index, 0, CharacterCount - 1);
        }

        int ClampVehicle(int index)
        {
            if (VehicleCount == 0) throw new InvalidOperationException("Vehicle roster is empty.");
            return Mathf.Clamp(index, 0, VehicleCount - 1);
        }
    }
}
