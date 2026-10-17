using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeItemWorld : MonoBehaviour
    {
        public NativeItemCatalog catalog;
        public NativeKartMotor[] racers;
        public Vector3[] route;
        public int ActiveActorCount => actors.Count;

        readonly List<NativeItemActor> actors = new List<NativeItemActor>();
        readonly List<GameObject> effects = new List<GameObject>();
        readonly Dictionary<GameObject, NativeKartMotor> effectOwners = new Dictionary<GameObject, NativeKartMotor>();
        readonly Dictionary<GameObject, GameObject> effectPrefabs = new Dictionary<GameObject, GameObject>();
        internal IEnumerable<KeyValuePair<GameObject, GameObject>> NetworkVisualEffects => effectPrefabs;
        readonly Dictionary<NativeItemActor, HashSet<NativeKartMotor>> meleeHits =
            new Dictionary<NativeItemActor, HashSet<NativeKartMotor>>();
        readonly Dictionary<NativeKartItemEffects, Action<NativeItemCatalog.Entry, Vector3>> windHandlers =
            new Dictionary<NativeKartItemEffects, Action<NativeItemCatalog.Entry, Vector3>>();
        NativeRace race;
        readonly Dictionary<NativeKartMotor, Vector3> previousCenters = new Dictionary<NativeKartMotor, Vector3>();
        int motionFrame = -1;

        void LateUpdate()
        {
            if (racers == null) return;
            foreach (var racer in racers)
                if (racer) previousCenters[racer] = RacerCenter(racer);
            motionFrame = Time.frameCount;
        }

        static Vector3 RacerCenter(NativeKartMotor racer)
        {
            var body = racer.GetComponent<CharacterController>();
            return racer.transform.position + Vector3.up * (body ? body.center.y : 1f);
        }

        public void Configure(NativeItemCatalog itemCatalog, NativeKartMotor[] raceMotors, Vector3[] courseRoute)
        {
            ResetWorld();
            UnsubscribeWind();
            catalog = itemCatalog;
            racers = raceMotors;
            route = courseRoute;
            race = FindObjectOfType<NativeRace>();
            if (racers == null) return;
            foreach (var racer in racers)
            {
                if (!racer) continue;
                var itemEffects = racer.GetComponent<NativeKartItemEffects>();
                if (!itemEffects) continue;
                NativeKartMotor source = racer;
                Action<NativeItemCatalog.Entry, Vector3> handler = (entry, position) =>
                    SpawnWindDrop(entry, source, position);
                itemEffects.WindDropRequested += handler;
                windHandlers[itemEffects] = handler;
            }
        }

        void UnsubscribeWind()
        {
            foreach (var pair in windHandlers)
                if (pair.Key) pair.Key.WindDropRequested -= pair.Value;
            windHandlers.Clear();
        }

        void SpawnWindDrop(NativeItemCatalog.Entry entry, NativeKartMotor source, Vector3 position)
        {
            if (entry == null || !entry.itemToDropPrefab || !source) return;
            CreateActor(entry, source, entry.itemToDropPrefab, entry.dropProjectile,
                entry.dropFrog != null ? NativeItemActorMode.Frog : NativeItemActorMode.Projectile,
                position, source.transform.rotation, Vector3.zero, false, 0, entry.dropFrog);
        }

        public bool HasShield(NativeKartMotor motor)
        {
            if (!motor) return false;
            foreach (var actor in actors)
                if (actor && !actor.Finished && actor.Owner == motor && actor.Mode == NativeItemActorMode.Shield)
                    return true;
            return false;
        }

        public NativeItemActor Spawn(string itemId, NativeKartMotor owner, Vector3 position,
            Quaternion rotation, Vector3 initialVelocity, bool carried = false)
        {
            if (!catalog) { NativeTelemetry.Event("item.spawn", "failed", "catalog-missing"); return null; }
            var entry = catalog.Find(itemId);
            if (entry == null) { NativeTelemetry.Event("item.spawn", "failed", "item-unknown", itemId); Debug.LogError("NATIVE_ITEM_UNKNOWN id=" + itemId); return null; }
            if (entry.family == NativeItemFamily.Bomb)
            {
                ResolveArea(entry, owner, position);
                SpawnEffect(entry.effectPrefab, position, rotation, 2f, owner);
                NativeTelemetry.Count("item.area.applied");
                return null;
            }
            if (entry.family == NativeItemFamily.Shield)
            {
                NativeItemActor first = null;
                GameObject[] prefabs = entry.shieldPrefabs != null && entry.shieldPrefabs.Length > 0
                    ? entry.shieldPrefabs : new[] { entry.shieldPrefab };
                for (int i = 0; i < prefabs.Length; i++)
                {
                    var actor = CreateActor(entry, owner, prefabs[i], entry.shieldProjectiles != null
                        && i < entry.shieldProjectiles.Length ? entry.shieldProjectiles[i] : entry.projectile,
                        NativeItemActorMode.Shield, position, rotation, Vector3.zero, false, i);
                    if (!first) first = actor;
                }
                return first;
            }
            if (entry.family == NativeItemFamily.Melee)
                return CreateActor(entry, owner, entry.meleePrefab, null, NativeItemActorMode.Melee,
                    position, rotation, Vector3.zero, false);
            if (entry.family == NativeItemFamily.Frog)
                return CreateActor(entry, owner, entry.frogPrefab, null, NativeItemActorMode.Frog,
                    position, rotation, initialVelocity, carried);
            if (entry.family != NativeItemFamily.Throw) return null;
            GameObject prefab = carried ? entry.carryPrefab : entry.throwPrefab;
            var spec = carried ? entry.carryProjectile : entry.projectile;
            return CreateActor(entry, owner, prefab, spec, NativeItemActorMode.Projectile,
                position, rotation, initialVelocity, carried);
        }

        NativeItemActor CreateActor(NativeItemCatalog.Entry entry, NativeKartMotor owner,
            GameObject prefab, NativeItemCatalog.ProjectileSpec spec, NativeItemActorMode mode,
            Vector3 position, Quaternion rotation, Vector3 velocity, bool carried, int ordinal = 0,
            NativeItemCatalog.FrogSpec frogSpec = null)
        {
            if (!prefab) { NativeTelemetry.Event("item.spawn", "failed", "prefab-missing", entry.id); Debug.LogError("NATIVE_ITEM_PREFAB_MISSING id=" + entry.id); return null; }
            var instance = Instantiate(prefab, position, rotation);
            instance.name = "NativeItem " + entry.id;
            instance.SetActive(true);
            var actor = instance.GetComponent<NativeItemActor>();
            if (!actor) actor = instance.AddComponent<NativeItemActor>();
            actor.Init(this, entry, owner, mode, spec, velocity, carried, ordinal, frogSpec);
            NativeParticleQualityRegistry.RegisterHierarchy(instance);
            actor.NetworkVisualPrefab = prefab;
            if (mode == NativeItemActorMode.Melee && owner)
            {
                var inventory = owner.GetComponent<NativeKartItemInventory>();
                actor.SetFollowAnchor(inventory && inventory.animAttachPoint
                    ? inventory.animAttachPoint : owner.transform);
            }
            actor.Destroyed += ActorDestroyed;
            actors.Add(actor);
            NativeTelemetry.Count("item.actor.created");
            NativeTelemetry.Metric("item.actors.active", actors.Count);
            return actor;
        }

        void ActorDestroyed(NativeItemActor actor)
        {
            actor.Destroyed -= ActorDestroyed;
            actors.Remove(actor);
            NativeTelemetry.Count("item.actor.destroyed");
            NativeTelemetry.Metric("item.actors.active", actors.Count);
            meleeHits.Remove(actor);
        }

        public void ResetWorld()
        {
            if (actors.Count > 0 || effects.Count > 0) NativeTelemetry.Event("item.world.reset", "applied", detail: "actors=" + actors.Count + ";effects=" + effects.Count);
            previousCenters.Clear();
            motionFrame = -1;
            var existing = actors.ToArray();
            foreach (var actor in existing) if (actor) actor.DestroyNow();
            actors.Clear();
            meleeHits.Clear();
            foreach (var fx in effects) if (fx) Destroy(fx);
            effects.Clear();
            effectOwners.Clear();
            effectPrefabs.Clear();
            NativeTelemetry.Metric("item.actors.active", 0);
        }

        public void ClearForOwner(NativeKartMotor source)
        {
            if (!source) return;
            var current = actors.ToArray();
            foreach (var actor in current)
                if (actor && actor.Owner == source) actor.DestroyNow();
            var currentEffects = effects.ToArray();
            foreach (var effect in currentEffects)
            {
                if (!effect || !effectOwners.TryGetValue(effect, out var owner) || owner != source) continue;
                effects.Remove(effect);
                effectOwners.Remove(effect);
                effectPrefabs.Remove(effect);
                Destroy(effect);
            }
        }

        public void ClearItemForOwner(string itemId, NativeKartMotor source)
        {
            if (string.IsNullOrEmpty(itemId) || !source) return;
            var current = actors.ToArray();
            foreach (var actor in current)
                if (actor && actor.Owner == source && actor.ItemId == itemId) actor.DestroyNow();
        }

        void OnDestroy() { ResetWorld(); UnsubscribeWind(); }

        internal bool IsHoming(NativeItemCatalog.Entry entry)
        {
            if (entry == null) return false;
            if (entry.id == "YinYangHoming" || entry.id == "YinYangHoming2" ||
                entry.id == "Shangai" || entry.id == "Insanity" || entry.id == "SanaeSnakes" ||
                entry.id == "FantasySeal" || entry.id == "Ofuda") return true;
            return entry.projectile != null && entry.projectile.homingIntensity > 0f;
        }

        internal NativeKartMotor SelectTarget(NativeKartMotor owner, bool seeksFirst)
        {
            if (racers == null || owner == null) return null;
            return RacerAtRank(seeksFirst ? 1 : race ? race.RankOf(owner) - 1 : 0);
        }

        NativeKartMotor RacerAtRank(int rank)
        {
            if (!race || racers == null || rank < 1) return null;
            foreach (var candidate in racers)
                if (candidate && race.RankOf(candidate) == rank) return candidate;
            return null;
        }

        internal NativeKartMotor SelectOfudaTarget(NativeKartMotor owner, bool seeksFirst)
        {
            if(!owner||!race)return null;
            var next=seeksFirst?race.SourceMostFirstRacer():race.SourceRacerAtRank(race.SourceRankIndex(owner)-1);
            if(!next||next==owner)next=race.SourceRacerAtRank(race.SourceRankIndex(next)+1);
            return next?next:owner;
        }

        internal NativeKartMotor SelectHomingRetarget(NativeKartMotor owner, NativeKartMotor current)
        {
            if (!current || !race) return null;
            int rank = race.RankOf(current);
            var next = RacerAtRank(rank - 1);
            return next == owner ? RacerAtRank(rank - 2) : next;
        }

        internal bool IsFinished(NativeKartMotor motor) => !motor || (race && race.HasFinished(motor));

        internal Vector3 RouteTarget(Vector3 position)
        {
            if (route == null || route.Length == 0) return position + Vector3.forward * 30f;
            int nearest = 0;
            float distance = float.MaxValue;
            for (int i = 0; i < route.Length; i++)
            {
                float next = Vector3.ProjectOnPlane(route[i] - position, Vector3.up).sqrMagnitude;
                if (next < distance) { nearest = i; distance = next; }
            }
            return route[(nearest + 1) % route.Length];
        }

        internal void CheckHits(NativeItemActor actor, Vector3 from, Vector3 to, float radius)
        {
            if (!actor || actor.Finished || actor.Carried || racers == null) return;
            float hitRadius = Mathf.Clamp(radius, 0.5f, 3f);
            foreach (var racer in racers)
            {
                if (!racer || (racer == actor.Owner && !actor.CanHitOwner) || (actor.ItemId != "Insanity" && IsFinished(racer))) continue;
                if (actor.Mode == NativeItemActorMode.Effect) continue;
                if (actor.Mode == NativeItemActorMode.Melee)
                {
                    if (!IntersectsMeleeHurt(actor, racer)) continue;
                    ResolveHit(actor, racer, 0f);
                    continue;
                }
                var body = racer.GetComponent<CharacterController>();
                float racerRadius = body ? body.radius : 1f;
                Vector3 center = RacerCenter(racer);
                Vector3 startCenter = center;
                if (actor.SpawnFrame != Time.frameCount && motionFrame == Time.frameCount - 1 &&
                    racer.LastResetFrame != Time.frameCount && previousCenters.TryGetValue(racer, out var previous))
                    startCenter = previous;
                // Compare both trajectories over the same frame; a moving car can occupy the shot's old path.
                if (SegmentDistanceSquared(from - startCenter, to - center, Vector3.zero) >
                    (hitRadius + racerRadius) * (hitRadius + racerRadius)) continue;
                ResolveHit(actor, racer, 0f);
                if (actor.Finished) return;
            }
        }

        static bool IntersectsMeleeHurt(NativeItemActor actor, NativeKartMotor racer)
        {
            var body = racer.GetComponent<CharacterController>();
            float radius = body ? body.radius : 1f;
            Vector3 center = racer.transform.position + (body ? body.center : Vector3.up);
            float halfSegment = body ? Mathf.Max(0f, body.height * 0.5f - radius) : 0f;
            foreach (var hurt in actor.GetComponentsInChildren<Collider>(true))
            {
                if (!hurt || !hurt.enabled || !hurt.gameObject.activeInHierarchy || !hurt.isTrigger) continue;
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 capsulePoint = center + Vector3.up * halfSegment * i;
                    if ((hurt.ClosestPoint(capsulePoint) - capsulePoint).sqrMagnitude <= radius * radius)
                        return true;
                }
            }
            return false;
        }

        internal void CheckCarriedContact(NativeItemActor actor, float radius)
        {
            if (!actor || actor.Finished || !actor.Carried || racers == null) return;
            Vector3 position = actor.transform.position;
            foreach (var racer in racers)
            {
                if (!racer || (racer == actor.Owner && !actor.CanHitOwner) || (actor.ItemId != "Insanity" && IsFinished(racer))) continue;
                var body = racer.GetComponent<CharacterController>();
                Vector3 center = racer.transform.position + Vector3.up * (body ? body.center.y : 1f);
                float reach = Mathf.Clamp(radius, 0.5f, 3f) + (body ? body.radius : 1f);
                if ((center - position).sqrMagnitude > reach * reach) continue;
                ResolveHit(actor, racer, 0f);
                if (actor.Finished) return;
            }
        }

        internal void CheckProjectileInterceptions(NativeItemActor projectile, Vector3 from, Vector3 to, float radius)
        {
            if (!projectile || projectile.Finished ||
                (projectile.Mode != NativeItemActorMode.Projectile && projectile.Mode != NativeItemActorMode.Frog)) return;
            var current = actors.ToArray();
            foreach (var barrier in current)
            {
                if (!barrier || barrier == projectile || barrier.Finished || barrier.Owner == projectile.Owner ||
                    !(barrier.Carried || barrier.Mode == NativeItemActorMode.Shield)) continue;
                float reach = Mathf.Clamp(radius, 0.5f, 3f) + 1.5f;
                if (SegmentDistanceSquared(from, to, barrier.transform.position) > reach * reach) continue;
                barrier.DestroyNow();
                projectile.DestroyNow();
                return;
            }
        }

        internal void CheckShieldCollisions(NativeItemActor shield, Vector3 from, Vector3 to, float radius)
        {
            if (!shield || shield.Finished) return;
            var current = actors.ToArray();
            foreach (var actor in current)
            {
                if (!actor || actor == shield || actor.Finished || actor.Carried ||
                    (actor.Mode != NativeItemActorMode.Projectile && actor.Mode != NativeItemActorMode.Frog) ||
                    actor.Owner == shield.Owner) continue;
                if (SegmentDistanceSquared(from, to, actor.transform.position) >
                    (radius + 1.5f) * (radius + 1.5f)) continue;
                actor.DestroyNow();
                shield.DestroyNow();
                return;
            }
        }

        internal void ResolveHit(NativeItemActor actor, NativeKartMotor target, float duration, string consumer = "world-sweep")
        {
            if (!actor || actor.Finished || !target || (target == actor.Owner && !actor.CanHitOwner)) return;
            if (actor.ItemId == "Insanity")
            {
                actor.BeginInsanity(target, catalog.Find("Insanity").projectile.effectDuration, consumer);
                return;
            }
            if (IsFinished(target)) return;
            if (actor.Mode == NativeItemActorMode.Melee)
            {
                if (!meleeHits.TryGetValue(actor, out var hitSet))
                {
                    hitSet = new HashSet<NativeKartMotor>();
                    meleeHits.Add(actor, hitSet);
                }
                if (!hitSet.Add(target)) return;
            }
            var entry = catalog.Find(actor.ItemId);
            if (entry == null) return;
            NativeItemImpactKind impact = entry.id == "Insanity" ? NativeItemImpactKind.Insanity
                : NativeItemImpactKind.Damage;
            var effect = target.GetComponent<NativeKartItemEffects>();
            float actualDuration = duration > 0f ? duration
                : entry.id == "Insanity" ? entry.projectile.effectDuration
                : entry.projectile != null && entry.projectile.useDamageDuration ? entry.projectile.damageDuration : 0f;
            if (effect) { bool accepted = effect.TryReceiveHit(entry.id, actor.Owner, actualDuration, impact); NativeTelemetry.Count(accepted ? "item.hit.accepted" : "item.hit.ignored"); }
            else NativeTelemetry.Count("item.hit.consumer-missing");
            if (entry.id == "Shangai")
            {
                ResolveExplosion(entry, actor.Owner, actor.transform.position, 6.5f, target);
                SpawnEffect(entry.projectile == null ? null : entry.projectile.explosionPrefab,
                    actor.transform.position, Quaternion.identity, 2f, actor.Owner);
            }
            if (actor.Mode == NativeItemActorMode.Frog)
                SpawnEffect(actor.FrogExplosion,
                    actor.transform.position, Quaternion.identity, 2f, actor.Owner);
            if (actor.Mode != NativeItemActorMode.Melee) actor.DestroyNow();
        }

        void ResolveExplosion(NativeItemCatalog.Entry entry, NativeKartMotor owner,
            Vector3 position, float range, NativeKartMotor primary)
        {
            if (racers == null) return;
            float rangeSq = range * range;
            foreach (var racer in racers)
            {
                if (!racer || racer == owner || racer == primary || IsFinished(racer) ||
                    (racer.transform.position - position).sqrMagnitude > rangeSq) continue;
                var effect = racer.GetComponent<NativeKartItemEffects>();
                if (effect) effect.TryReceiveHit(entry.id, owner, 0f, NativeItemImpactKind.Damage);
            }
        }

        void ResolveArea(NativeItemCatalog.Entry entry, NativeKartMotor owner, Vector3 position)
        {
            if (racers == null) return;
            float rangeSq = entry.range * entry.range;
            foreach (var racer in racers)
            {
                if (!racer || racer == owner || IsFinished(racer) ||
                    (racer.transform.position - position).sqrMagnitude > rangeSq) continue;
                racer.StopCurrentSpeed();
                if (entry.id == "FreezeBomb")
                {
                    var effect = racer.GetComponent<NativeKartItemEffects>();
                    if (effect) effect.TryReceiveHit(entry.id, owner, entry.duration, NativeItemImpactKind.Freeze);
                }
                SpawnEffect(entry.affectedEffectPrefab, racer.transform.position, racer.transform.rotation,
                    Mathf.Max(0.75f, entry.duration), owner);
            }
        }

        internal void SpawnEffect(GameObject prefab, Vector3 position, Quaternion rotation, float duration,
            NativeKartMotor owner = null)
        {
            if (!prefab) return;
            var effect = Instantiate(prefab, position, rotation);
            NativeAudioEmitter.ReapplyHierarchy(effect);
            foreach (var listener in effect.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            effect.SetActive(true);
            NativeParticleQualityRegistry.RegisterHierarchy(effect);
            effects.Add(effect);
            effectOwners[effect] = owner;
            effectPrefabs[effect] = prefab;
            StartCoroutine(ClearEffect(effect, duration));
        }

        IEnumerator ClearEffect(GameObject effect, float duration)
        {
            var originalLifetime = effect ? effect.GetComponent<NativeItemPrefabLifetime>() : null;
            if (originalLifetime && originalLifetime.enabled)
            {
                while (effect) yield return null;
            }
            else yield return new WaitForSeconds(Mathf.Max(0.1f, duration));
            effects.Remove(effect);
            effectOwners.Remove(effect);
            effectPrefabs.Remove(effect);
            if (effect) Destroy(effect);
        }

        static float SegmentDistanceSquared(Vector3 from, Vector3 to, Vector3 point)
        {
            Vector3 segment = to - from;
            float t = segment.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(point - from, segment) / segment.sqrMagnitude) : 0f;
            return (point - (from + segment * t)).sqrMagnitude;
        }
    }
}
