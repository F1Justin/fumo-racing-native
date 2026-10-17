using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartParticles : MonoBehaviour
    {
        public ParticleSystem drift;
        public GameObject flipPrefab, landingPrefab;
        public float flipLifetime = 3f, landingLifetime = 3f;
        public bool Drifting { get; private set; }
        public int DriftStarts { get; private set; }
        public int DriftStops { get; private set; }
        public int FlipSpawns { get; private set; }
        public int LandingSpawns { get; private set; }
        public GameObject LastFlip { get; private set; }
        public GameObject LastLanding { get; private set; }
        readonly List<GameObject> effects = new List<GameObject>();
        public int ActiveEffects => effects.FindAll(effect => effect).Count;

        public void SetDrifting(bool active)
        {
            if (Drifting == active) return;
            Drifting = active;
            if (active)
            {
                DriftStarts++;
                if (drift) drift.Play(true);
            }
            else
            {
                DriftStops++;
                if (drift) drift.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        public void Flip()
        {
            if (!flipPrefab) return;
            LastFlip = Spawn(flipPrefab, flipLifetime);
            FlipSpawns++;
        }

        public void Land()
        {
            if (!landingPrefab) return;
            LastLanding = Spawn(landingPrefab, landingLifetime);
            LandingSpawns++;
        }

        GameObject Spawn(GameObject prefab, float lifetime)
        {
            var effect = Instantiate(prefab, transform.position, transform.rotation);
            effect.transform.SetParent(transform, true);
            effect.AddComponent<NativeKartParticleLifetime>().duration = lifetime;
            NativeParticleQualityRegistry.RegisterHierarchy(effect);
            effects.Add(effect);
            return effect;
        }

        void LateUpdate() => effects.RemoveAll(effect => !effect);

        public void ResetState()
        {
            Drifting = false;
            if (drift) drift.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var effect in effects)
                if (effect) { effect.SetActive(false); Destroy(effect); }
            effects.Clear();
            LastFlip = LastLanding = null;
            DriftStarts = DriftStops = FlipSpawns = LandingSpawns = 0;
        }
    }
}
