using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public enum NativeItemImpactKind { Damage, Freeze, Insanity, StopSpeed }

    [DefaultExecutionOrder(-40)]
    public sealed class NativeKartItemEffects : MonoBehaviour
    {
        sealed class Active
        {
            public NativeItemCatalog.Entry entry;
            public float remaining;
            public float dropDistanceSquared;
            public int drops;
            public GameObject visual;
        }

        public NativeKartMotor motor;
        public NativeKartHit hit;
        public event Action<string> Completed;
        public event Action<NativeItemCatalog.Entry, Vector3> WindDropRequested;
        public int ActiveCount => active.Count + (motor ? Mathf.Max(insanityRemaining.Count, motor.ControlReverseCount) : insanityRemaining.Count);
        public bool Insane => motor && motor.ControlsReversed;

        readonly List<Active> active = new List<Active>();
        readonly List<float> insanityRemaining = new List<float>();
        NativeKartSettings baseStats;
        NativeKartSettings appliedStats;

        readonly Dictionary<string, GameObject> networkVisuals = new Dictionary<string, GameObject>();
        public string[] NetworkEffectIds => active.Select(a => a.entry.id).ToArray();
        public void ApplyNetworkEffects(string[] ids, NativeItemCatalog catalog)
        {
            ids = ids ?? new string[0];
            foreach (string id in networkVisuals.Keys.Where(id => !ids.Contains(id)).ToArray())
            { if (networkVisuals[id]) Destroy(networkVisuals[id]); networkVisuals.Remove(id); }
            foreach (string id in ids)
            {
                if (networkVisuals.ContainsKey(id)) continue;
                var entry = catalog.Find(id); if (entry == null || !entry.effectPrefab) continue;
                var anchor = motor.driftPivot ? motor.driftPivot : motor.transform;
                var visual = Instantiate(entry.effectPrefab, motor.transform.position, anchor.rotation, anchor);
                foreach (var script in visual.GetComponentsInChildren<MonoBehaviour>()) script.enabled = false;
                foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
                NativeParticleQualityRegistry.RegisterHierarchy(visual);
                networkVisuals[id] = visual;
            }
        }

        void Awake()
        {
            if (!motor) motor = GetComponent<NativeKartMotor>();
            if (!hit) hit = GetComponent<NativeKartHit>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (appliedStats != null && motor && motor.stats != appliedStats)
            {
                foreach (var effect in active) DestroyVisual(effect);
                active.Clear();
                appliedStats = null;
                baseStats = null;
            }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var effect = active[i];
                if (effect.entry.family == NativeItemFamily.WindBoost && motor && effect.entry.distanceInterval > 0f)
                {
                    effect.dropDistanceSquared += motor.DriveVelocity.sqrMagnitude * dt;
                    float intervalSquared = effect.entry.distanceInterval * effect.entry.distanceInterval;
                    if (effect.drops < effect.entry.maxItems && effect.dropDistanceSquared >= intervalSquared)
                    {
                        effect.dropDistanceSquared -= intervalSquared;
                        effect.drops++;
                        WindDropRequested?.Invoke(effect.entry, transform.position);
                    }
                }
                effect.remaining -= dt;
                if (effect.remaining > 0f) continue;
                string id = effect.entry.id;
                DestroyVisual(effect);
                active.RemoveAt(i);
                RebuildStats();
                Completed?.Invoke(id);
            }
            for (int i = insanityRemaining.Count - 1; i >= 0; i--)
            {
                insanityRemaining[i] -= dt;
                if (insanityRemaining[i] > 0f) continue;
                insanityRemaining.RemoveAt(i);
                if (motor) motor.RemoveControlReverse();
            }
        }

        public bool IsActive(string itemId)
        {
            if (itemId == "Insanity" && (insanityRemaining.Count > 0 || (motor && motor.ControlReverseCount > 0))) return true;
            foreach (var effect in active)
                if (string.Equals(effect.entry.id, itemId, StringComparison.Ordinal)) return true;
            return false;
        }

        public bool TryUse(NativeItemCatalog.Entry entry, NativeKartItemInventory inventory)
        {
            if (entry == null || !motor || motor.stats == null || entry.duration <= 0f) return false;
            if (entry.family != NativeItemFamily.Boost && entry.family != NativeItemFamily.FullPower
                && entry.family != NativeItemFamily.WindBoost) return false;
            if (IsActive(entry.id)) return false;
            if (active.Count == 0)
            {
                baseStats = motor.stats;
                appliedStats = null;
            }
            var next = new Active { entry = entry, remaining = entry.duration };
            if (entry.effectPrefab)
            {
                var anchor = motor.driftPivot ? motor.driftPivot : motor.transform;
                next.visual = Instantiate(entry.effectPrefab, motor.transform.position,
                    anchor.rotation, anchor);
                next.visual.SetActive(true);
                NativeParticleQualityRegistry.RegisterHierarchy(next.visual);
            }
            active.Add(next);
            RebuildStats();
            if (entry.family == NativeItemFamily.FullPower && hit) hit.MakeInvulnerable(entry.duration);
            var actions = GetComponent<NativeKartAnimationActions>();
            if (actions) actions.SetTrigger("boost");
            return true;
        }

        public bool TryReceiveHit(string itemId, NativeKartMotor source, float duration, NativeItemImpactKind kind)
        {
            if (NativeRaceNetwork.ClientRace || !motor) return false;
            switch (kind)
            {
                case NativeItemImpactKind.Damage:
                    return hit && hit.TakeDamage(duration > 0f ? duration : hit.defaultDuration);
                case NativeItemImpactKind.Freeze:
                    return hit && duration > 0f && hit.FreezeFor(duration);
                case NativeItemImpactKind.StopSpeed:
                    motor.StopCurrentSpeed();
                    return true;
                case NativeItemImpactKind.Insanity:
                    if (duration <= 0f) return false;
                    motor.AddControlReverse();
                    insanityRemaining.Add(duration);
                    return true;
                default:
                    return false;
            }
        }

        public void ResetEffects()
        {
            foreach (var visual in networkVisuals.Values) if (visual) Destroy(visual);
            networkVisuals.Clear();
            foreach (var effect in active) DestroyVisual(effect);
            active.Clear();
            if (motor && appliedStats != null && motor.stats == appliedStats) motor.stats = baseStats;
            baseStats = appliedStats = null;
            insanityRemaining.Clear();
            if (motor) motor.ClearControlReverse();
        }

        void OnDestroy() { ResetEffects(); }

        static void DestroyVisual(Active effect)
        {
            if (effect.visual) Destroy(effect.visual);
            effect.visual = null;
        }

        void RebuildStats()
        {
            if (!motor) return;
            if (baseStats == null) return;
            if (active.Count == 0)
            {
                if (motor.stats == appliedStats) motor.stats = baseStats;
                baseStats = appliedStats = null;
                return;
            }
            var copy = CopySettings(baseStats);
            foreach (var effect in active)
            {
                copy._topSpeed += effect.entry.topSpeedAdd;
                copy._topSpeedGrass += effect.entry.topSpeedAdd;
                copy._acceleration += effect.entry.accelAdd;
                copy._driftSteerGain += effect.entry.driftSteerGain;
            }
            appliedStats = copy;
            motor.stats = copy;
        }

        static NativeKartSettings CopySettings(NativeKartSettings source)
        {
            var copy = new NativeKartSettings();
            foreach (var field in typeof(NativeKartSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
                field.SetValue(copy, field.GetValue(source));
            return copy;
        }
    }
}
