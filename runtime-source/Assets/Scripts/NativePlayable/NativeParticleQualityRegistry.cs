using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fumo.NativePlayable
{
    // Materials remain per renderer: authored effects can change their own colors/properties.
    public sealed class NativeParticleQualityRegistry : MonoBehaviour
    {
        static readonly Dictionary<int, NativeParticleQualityRegistry> registries = new Dictionary<int, NativeParticleQualityRegistry>();
        readonly List<GameObject> roots = new List<GameObject>();
        readonly List<ParticleSystemRenderer> discovered = new List<ParticleSystemRenderer>();
        readonly List<NativeParticleQualityRegistration> entries = new List<NativeParticleQualityRegistration>();
        Scene scene;
        NativeSceneSettings.EffectReport report;
        float nextDiscovery;
        int quality = -1;
        bool lighting;
        public int DiscoveryCount { get; private set; }
        public int SettingsApplyCount { get; private set; }
        public int RegisteredRendererCount => entries.Count;
        public int LiveRendererCount { get; private set; }
        public int TemporaryMaterialCount { get; private set; }
        public int TotalRegistrations { get; private set; }

        public static NativeParticleQualityRegistry ApplyScene(Scene scene, NativeSceneSettings.EffectReport report)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            if (!registries.TryGetValue(scene.handle, out var registry) || !registry)
            {
                var node = new GameObject("Native Particle Quality Registry");
                SceneManager.MoveGameObjectToScene(node, scene);
                registry = node.AddComponent<NativeParticleQualityRegistry>();
                registry.scene = scene;
                registries[scene.handle] = registry;
                NativeUserSettings.Changed += registry.SettingsChanged;
                registry.ReadSettings();
                registry.Discover();
            }
            registry.report = report;
            registry.SettingsChanged();
            registry.RefreshReport(report);
            return registry;
        }
        public static void RegisterHierarchy(GameObject root)
        {
            if (!root || !registries.TryGetValue(root.scene.handle, out var registry) || !registry) return;
            registry.discovered.Clear();
            root.GetComponentsInChildren(true, registry.discovered);
            registry.RegisterDiscovered();
            registry.RefreshReport(registry.report);
        }
        void ReadSettings()
        {
            quality = NativeUserSettings.GetInt("video_particlequality");
            lighting = NativeUserSettings.GetInt("video_particlelighting") == 0;
        }
        void SettingsChanged()
        {
            int previousQuality = quality; bool previousLighting = lighting;
            ReadSettings();
            if (quality != previousQuality || lighting != previousLighting)
            {
                SettingsApplyCount++;
                for (int i = 0; i < entries.Count; i++) if (entries[i]) entries[i].Apply(quality, lighting);
            }
            RefreshReport(report);
        }
        void Update()
        {
            // Safety discovery catches effects created outside the registered item spawn paths.
            if (Time.unscaledTime < nextDiscovery) return;
            Discover();
            RefreshReport(report);
        }
        void Discover()
        {
            nextDiscovery = Time.unscaledTime + 5f;
            DiscoveryCount++;
            roots.Clear(); scene.GetRootGameObjects(roots);
            for (int i = 0; i < roots.Count; i++)
            {
                discovered.Clear(); roots[i].GetComponentsInChildren(true, discovered);
                RegisterDiscovered();
            }
        }
        void RegisterDiscovered()
        {
            for (int i = 0; i < discovered.Count; i++)
            {
                var renderer = discovered[i];
                if (!renderer || !renderer.GetComponent<ParticleSystem>()) continue;
                var registration = renderer.GetComponent<NativeParticleQualityRegistration>();
                if (!registration) registration = renderer.gameObject.AddComponent<NativeParticleQualityRegistration>();
                if (registration.IsRegistered) continue;
                registration.Initialize(this, renderer);
                entries.Add(registration); TotalRegistrations++;
                registration.Apply(quality, lighting);
            }
        }
        internal void Removed(NativeParticleQualityRegistration registration)
        {
            entries.Remove(registration);
            RefreshReport(report);
        }
        public void RefreshReport(NativeSceneSettings.EffectReport target)
        {
            int count = 0, live = 0, materials = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; if (!entry) continue;
                count++; if (entry.isActiveAndEnabled) live++;
                materials += entry.TemporaryMaterialCount;
            }
            LiveRendererCount = live; TemporaryMaterialCount = materials;
            if (target != null) { target.particles = count; target.litParticles = lighting ? count : 0; }
        }
        void OnDestroy()
        {
            NativeUserSettings.Changed -= SettingsChanged;
            if (registries.TryGetValue(scene.handle, out var current) && current == this) registries.Remove(scene.handle);
        }
    }
}
