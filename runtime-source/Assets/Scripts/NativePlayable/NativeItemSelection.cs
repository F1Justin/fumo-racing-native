using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeItemSelection
    {
        public static int PoolIndex(int zeroBasedRank, int players)
        {
            return Mathf.Clamp((int)((float)zeroBasedRank / Mathf.Max(1, players) * 8), 0, 7);
        }

        public static bool Eligible(NativeItemCatalog.Entry entry, int bucket, string characterId,
            float characterCooldown, double elapsed, double nextOfuda, float chanceRoll, float firstRacerTime = 0f, System.Func<NativeItemCatalog.Entry,double> readOfudaNext = null)
        {
            if (entry == null || bucket < entry.minRank || bucket > entry.maxRank) return false;
            if (entry.id == "Ofuda")
            {
                double next=readOfudaNext!=null?readOfudaNext(entry):nextOfuda;
                return firstRacerTime>=entry.firstRacerTimeCondition&&elapsed>=next;
            }
            return string.IsNullOrEmpty(entry.characterConditionId) ||
                (entry.characterConditionId == characterId && characterCooldown <= 0f && chanceRoll < entry.characterFixedChance);
        }

        public static NativeItemCatalog.Entry Choose(NativeItemCatalog catalog, int zeroBasedRank,
            int players, string characterId, float cooldown, double elapsed, double nextOfuda, float firstRacerTime = 0f, System.Func<NativeItemCatalog.Entry,double> readOfudaNext = null)
        {
            if (!catalog) return null;
            int bucket = PoolIndex(zeroBasedRank, players);
            var ordinary = new List<NativeItemCatalog.Entry>();
            var exclusive = new List<NativeItemCatalog.Entry>();
            foreach (var entry in catalog.entries)
            {
                if (!Eligible(entry, bucket, characterId, cooldown, elapsed, nextOfuda, Random.Range(0f, 100f), firstRacerTime, readOfudaNext)) continue;
                if (!string.IsNullOrEmpty(entry.characterConditionId) && entry.characterConditionId == characterId)
                    exclusive.Add(entry);
                else ordinary.Add(entry);
            }
            var choices = exclusive.Count > 0 ? exclusive : ordinary;
            return choices.Count == 0 ? null : choices[Random.Range(0, choices.Count)];
        }
    }
}
