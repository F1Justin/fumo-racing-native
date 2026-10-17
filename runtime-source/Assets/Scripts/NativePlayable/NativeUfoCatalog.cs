using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeUfoCatalog : MonoBehaviour
    {
        [Serializable] public sealed class Exclusive { public string characterId, itemId; }
        public int recipeLength, randomColor;
        public Color[] colors;
        public Color randomDisplayColor = Color.gray;
        public NativeUfoRules.Recipe[] recipes;
        public Exclusive[] exclusives;
        public string defaultItemId;
        public float switchTime, warnTime, warnRate, respawnTime;
        public bool respawns;
        public NativeItemCatalog itemCatalog;
        public string ExclusiveFor(string character)
        {
            if (exclusives != null) foreach (var entry in exclusives) if (string.Equals(entry.characterId, character, StringComparison.OrdinalIgnoreCase)) return entry.itemId;
            return null;
        }
    }
}
