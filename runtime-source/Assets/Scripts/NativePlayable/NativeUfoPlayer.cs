using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeUfoPlayer : MonoBehaviour
    {
        public NativeUfoCatalog catalog;
        public NativeKartItemInventory inventory;
        public NativeUfoRules.State State { get; private set; }
        public bool active;
        public Sprite PreviewIcon => catalog && catalog.itemCatalog && State != null ? catalog.itemCatalog.Find(State.previewItemId)?.icon : null;
        public void ResetState() { State = new NativeUfoRules.State(catalog.recipeLength); }
        public bool Collect(int color)
        {
            if (!active || !catalog || !inventory) return false;
            if (State == null) ResetState();
            int collectedIndex = State.count;
            bool collected = NativeUfoRules.Collect(State, color, catalog.colors.Length, catalog.recipes, catalog.ExclusiveFor(inventory.CharacterId), catalog.defaultItemId);
            // PlrUFOsHUD indexes pickup clips by the occupied collection slot.
            if (collected) { if (HasHumanHud()) NativeAudioRuntime.PlayEvent("ufo.collect." + collectedIndex, transform); NativeRaceNetwork.RecordSourceAudio(transform,"ufo.collect."+collectedIndex); }
            return collected;
        }
        public bool Use()
        {
            if (!active || !inventory || State == null) return false;
            bool used = NativeUfoRules.Use(State, inventory.HasItem, id => inventory.TryGive(id, false));
            if (used) { if (HasHumanHud()) NativeAudioRuntime.PlayEvent("ufo.use", transform); NativeRaceNetwork.RecordSourceAudio(transform,"ufo.use"); }
            return used;
        }
        bool HasHumanHud()
        {
            var race = FindObjectOfType<NativeRace>();
            return race && inventory && inventory.motor && race.IsHumanRacer(inventory.motor);
        }
    }
}
