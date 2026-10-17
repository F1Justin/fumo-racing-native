using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public enum NativeItemFamily { Throw, Boost, FullPower, WindBoost, Bomb, Frog, Shield, Melee }

    public sealed class NativeItemCatalog : MonoBehaviour
    {
        [Serializable] public sealed class ProjectileSpec
        {
            public bool onRadar;
            public bool ofudaBehaviour;
            public Sprite radarIcon;
            public bool collidesOwner, destroyOnImpact, hasCharacterController, hasTriggerHurt, useDamageDuration;
            public float bounce, gravity, stickForce, duration;
            public float homingIntensity, waypointIntensity, homingDistance, turnSpeed, damageDuration;
            public bool seeksFirst;
            public float flyHeight, targetHeight, damagedDuration, timeBeforeExplosion, effectDuration;
            public GameObject explosionPrefab;
        }

        [Serializable] public sealed class FrogSpec
        {
            public float duration, gravity, jumpHeight, jumpForward, jumpWait, changeDirectionChance;
            public float turnSpeed, runJumpHeight, runJumpForward, runJumpWait, steeringDuration;
            public int jumpsToChangeDirection;
            public GameObject explosionPrefab;
        }

        [Serializable] public sealed class ShieldMotionSpec
        {
            public float rotation, rotationSpeed, distanceSpeed, distanceMax, distanceMin, rotOffset;
        }

        [Serializable] public sealed class Entry
        {
            public string id, sourceScript, giveOnDoneId, characterConditionId, trigger;
            public NativeItemFamily family;
            public Sprite icon;
            public bool useAtStart, twoAction;
            public int minRank, maxRank, count, maxItems, hitLayers;
            public float characterCooldown, characterFixedChance;
            public float force, forceUp, fastSpeedCondition, forceFast, forceFastUp;
            public Vector3[] offsets, spread;
            public float firstRacerTimeCondition;
            public double ofudaCooldown;
            public float accelAdd, topSpeedAdd, driftSteerGain, duration, range, distanceInterval;
            public GameObject throwPrefab, carryPrefab, frogPrefab, effectPrefab, affectedEffectPrefab;
            public GameObject shieldPrefab, meleePrefab, itemToDropPrefab;
            public GameObject[] shieldPrefabs;
            public ProjectileSpec projectile, carryProjectile, dropProjectile;
            public ProjectileSpec[] shieldProjectiles;
            public ShieldMotionSpec[] shieldMotions;
            public FrogSpec frog, dropFrog;
        }

        public Entry[] entries = Array.Empty<Entry>();
        public int Count => entries == null ? 0 : entries.Length;

        public Entry Find(string id)
        {
            if (string.IsNullOrEmpty(id) || entries == null) return null;
            foreach (var entry in entries)
                if (entry != null && string.Equals(entry.id, id, StringComparison.Ordinal)) return entry;
            return null;
        }

        public Entry Require(string id)
        {
            var entry = Find(id);
            if (entry == null) throw new InvalidOperationException("Original item is missing from catalog: " + id);
            return entry;
        }
    }
}
