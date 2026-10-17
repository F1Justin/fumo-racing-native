#if UNITY_6000_0_OR_NEWER
using PhysicMaterial = UnityEngine.PhysicsMaterial;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeSurfaceParticleBinding
    {
        public string key;
        public PhysicMaterial material;
        public ParticleSystem prefab;
    }

    public sealed class NativeKartSurfaceParticles : MonoBehaviour
    {
        public const float SpeedThreshold = 0.688f;
        public ParticleSystem speed, water;
        public Transform dirtPoint;
        public NativeSurfaceParticleBinding[] surfaces = new NativeSurfaceParticleBinding[0];
        public bool SpeedActive { get; private set; }
        public bool WaterActive { get; private set; }
        public float SpeedRatio { get; private set; }
        public int SpeedStarts { get; private set; }
        public int SpeedStops { get; private set; }
        public int WaterStarts { get; private set; }
        public int WaterStops { get; private set; }
        public string ActiveDirtKey { get; private set; }
        public ParticleSystem ActiveDirt { get; private set; }
        public int CachedDirtCount => dirt.Count;
        readonly Dictionary<string, ParticleSystem> dirt = new Dictionary<string, ParticleSystem>();
        bool previousOffroad, previousWater;

        public void UpdateSpeed(float horizontalSpeed, float baseTopSpeed)
        {
            SpeedRatio = baseTopSpeed > 0f ? horizontalSpeed / baseTopSpeed : 0f;
            bool active = SpeedRatio >= SpeedThreshold;
            if (active != SpeedActive)
            {
                if (active) SpeedStarts++;
                else SpeedStops++;
            }
            SpeedActive = active;
            if (!speed) return;
            if (active && !speed.isPlaying) speed.Play(true);
            else if (!active && speed.isPlaying) speed.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void UpdateSurface(bool grounded, bool offroad, bool inWater, string surfaceKey)
        {
            Cache(surfaceKey);
            // The original switches emitters on offroad entry or a water transition, not on every material change.
            if (!previousOffroad || previousWater != inWater)
            {
                if (offroad)
                {
                    if (inWater)
                    {
                        SetWater(true);
                        StopDirt();
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(surfaceKey) && dirt.TryGetValue(surfaceKey, out var effect))
                        {
                            StopDirt();
                            ActiveDirt = effect;
                            ActiveDirtKey = surfaceKey;
                            effect.Play(true);
                        }
                        SetWater(false);
                    }
                }
            }
            if (!grounded || (previousOffroad && !offroad))
            {
                StopDirt();
                SetWater(false);
            }
            previousOffroad = offroad;
            previousWater = inWater;
        }

        void Cache(string key)
        {
            if (string.IsNullOrEmpty(key) || dirt.ContainsKey(key) || !dirtPoint) return;
            foreach (var binding in surfaces)
            {
                if (binding.key != key || !binding.prefab) continue;
                var instance = Instantiate(binding.prefab, dirtPoint, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                NativeParticleQualityRegistry.RegisterHierarchy(instance.gameObject);
                dirt.Add(key, instance);
                return;
            }
        }

        void SetWater(bool active)
        {
            if (active != WaterActive)
            {
                if (active) WaterStarts++;
                else WaterStops++;
            }
            WaterActive = active;
            if (!water) return;
            if (active) water.Play(true);
            else water.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void StopDirt()
        {
            foreach (var effect in dirt.Values)
                if (effect) effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            ActiveDirt = null;
            ActiveDirtKey = null;
        }

        public void ResetState()
        {
            if (speed) speed.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (water) water.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var effect in dirt.Values)
                if (effect) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            dirt.Clear();
            ActiveDirt = null;
            ActiveDirtKey = null;
            previousOffroad = previousWater = SpeedActive = WaterActive = false;
            SpeedRatio = 0f;
            SpeedStarts = SpeedStops = WaterStarts = WaterStops = 0;
        }
    }
}
