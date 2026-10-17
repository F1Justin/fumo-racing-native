using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeParticleQualityRegistration : MonoBehaviour
    {
        NativeParticleQualityRegistry registry;
        ParticleSystemRenderer particleRenderer;
        ParticleSystem system;
        Material[] original, adapted;
        int maxParticles, appliedQuality = -1;
        bool appliedLighting, itemScope;
        public bool IsRegistered => registry && particleRenderer;
        public int TemporaryMaterialCount { get; private set; }
        internal void Initialize(NativeParticleQualityRegistry owner, ParticleSystemRenderer renderer)
        {
            registry = owner; particleRenderer = renderer; system = renderer.GetComponent<ParticleSystem>();
            maxParticles = system.main.maxParticles; original = renderer.sharedMaterials;
            adapted = new Material[original.Length];
            for (int i = 0; i < original.Length; i++) if (original[i])
            {
                adapted[i] = new Material(original[i]) { name = original[i].name + " Native Settings" };
                TemporaryMaterialCount++;
            }
            itemScope = GetComponentInParent<NativeItemPrefabQualityScope>(true) != null;
            renderer.sharedMaterials = adapted;
        }
        internal void Apply(int quality, bool lighting)
        {
            if (!system || !particleRenderer || (quality == appliedQuality && lighting == appliedLighting)) return;
            var main = system.main;
            int desired = Math.Max(1, Mathf.RoundToInt(maxParticles * (itemScope ? 1f : quality == 0 ? 1f : quality == 1 ? .5f : .25f)));
            if (main.maxParticles != desired) main.maxParticles = desired;
            for (int i = 0; i < adapted.Length; i++)
            {
                var material = adapted[i]; var source = original[i];
                if (!material || !source || !source.shader || source.shader.name != "Particles/Standard Surface") continue;
                var unlit = lighting ? null : Shader.Find("Particles/Standard Unlit");
                var shader = unlit ? unlit : source.shader;
                if (material.shader != shader)
                {
                    material.shader = shader;
                    material.CopyPropertiesFromMaterial(source);
                }
                if (material.renderQueue != source.renderQueue) material.renderQueue = source.renderQueue;
            }
            appliedQuality = quality; appliedLighting = lighting;
        }
        void OnEnable() { if (registry) registry.RefreshReport(null); }
        void OnDisable() { if (registry) registry.RefreshReport(null); }
        void OnTransformParentChanged()
        {
            if (!registry) return;
            bool nextScope = GetComponentInParent<NativeItemPrefabQualityScope>(true) != null;
            if (nextScope == itemScope) return;
            itemScope = nextScope; appliedQuality = -1;
            Apply(NativeUserSettings.GetInt("video_particlequality"), NativeUserSettings.GetInt("video_particlelighting") == 0);
        }
        void OnDestroy()
        {
            if (particleRenderer && original != null) particleRenderer.sharedMaterials = original;
            if (adapted != null) foreach (var material in adapted) if (material) Destroy(material);
            if (registry) registry.Removed(this);
        }
    }
}
