using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartItemVisual : MonoBehaviour
    {
        struct LiveEffect { public GameObject gameObject; public NativeKartMotor owner; }
        static readonly List<LiveEffect> effects = new List<LiveEffect>();
        public Vector3 rotationPerSecond = new Vector3(0f, 200f, 0f);
        public GameObject destroyEffect;
        public float effectDuration = 1f;
        public NativeKartMotor owner;
        bool closed, suppress;
        public static int EffectsCreated { get; private set; }

        void Update() { transform.Rotate(rotationPerSecond * Time.deltaTime, Space.Self); }
        void OnApplicationQuit() { closed = true; }
        public void SuppressDestroyEffect() { suppress = true; }
        void OnDestroy()
        {
            if (!Application.isPlaying || closed || suppress || !destroyEffect) return;
            var effect = Instantiate(destroyEffect, transform.position, transform.rotation);
            effect.SetActive(true);
            effects.RemoveAll(value => !value.gameObject);
            effects.Add(new LiveEffect { gameObject = effect, owner = owner });
            EffectsCreated++;
            Destroy(effect, effectDuration);
        }

        public static void ClearEffectsForOwner(NativeKartMotor owner)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
                if (!effects[i].gameObject || effects[i].owner == owner)
                {
                    if (effects[i].gameObject) Destroy(effects[i].gameObject);
                    effects.RemoveAt(i);
                }
        }
    }
}
