using System;
using System.Collections.Generic;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public enum NativeVehicleModuleEffect { Ready, Use, UseMax, Overheat }
    [Serializable] public sealed class NativeVehicleModuleEffectBinding
    {
        public NativeVehicleModuleKind family;
        public GameObject readyPrefab, usePrefab, useMaxPrefab, overheatPrefab;
        public string sourceModuleId;
        public string[] sourcePrefabIds;
    }
    public sealed class NativeVehicleModuleEffects : MonoBehaviour
    {
        [Serializable] public sealed class SpawnRecord
        {
            public string family, effect, sourceModuleId, sourcePrefabId;
            public int frame, instanceId, motorStep;
            public float scaledTime, timeScale, lifetime, charge;
            public Vector3 position;
            public Quaternion rotation;
            public bool followsVehicle;
            [NonSerialized] public GameObject instance;
        }
        public NativeVehicleModuleEffectBinding binding;
        public readonly List<SpawnRecord> records = new List<SpawnRecord>();
        public GameObject Spawn(NativeVehicleModuleEffect effect)
        {
            var motor = GetComponent<NativeKartMotor>();
            if (!motor || motor.stats == null || binding == null || binding.family != motor.stats._moduleKind)
                throw new InvalidOperationException("Original module FX binding differs from selected vehicle");
            GameObject prefab = effect == NativeVehicleModuleEffect.Ready ? binding.readyPrefab
                : effect == NativeVehicleModuleEffect.Use ? binding.usePrefab
                : effect == NativeVehicleModuleEffect.UseMax ? binding.useMaxPrefab : binding.overheatPrefab;
            if (!prefab) throw new InvalidOperationException("Original module FX prefab missing: " + effect);
            bool follow = binding.family != NativeVehicleModuleKind.ShoppingCartDriftCap;
            var instance = Instantiate(prefab, transform.position, transform.rotation);
            if (follow) instance.transform.SetParent(transform, true);
            var lifetime = instance.GetComponent<NativeItemPrefabLifetime>();
            if (!lifetime) throw new InvalidOperationException("Original module FX lifetime missing");
            int slot = (int)effect;
            records.Add(new SpawnRecord { family = binding.family.ToString(), effect = effect.ToString(),
                sourceModuleId = binding.sourceModuleId, sourcePrefabId = binding.sourcePrefabIds[slot],
                frame = Time.frameCount, instanceId = instance.GetInstanceID(), motorStep = motor.FixedStepCount,
                scaledTime = Time.time, timeScale = Time.timeScale, lifetime = lifetime.duration,
                charge = motor.ModuleCharge, position = instance.transform.position, rotation = instance.transform.rotation,
                followsVehicle = follow, instance = instance });
            return instance;
        }
    }
}
