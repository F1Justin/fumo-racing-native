using UnityEngine;

namespace Fumo.NativePlayable
{
    [RequireComponent(typeof(Camera))]
    public sealed class NativeKartFirstPersonHiding : MonoBehaviour
    {
        NativeKartCamera rig;
        bool[] previous;
        bool hidden;
        public Renderer[] CharacterRenderers { get; private set; } = new Renderer[0];
        public int PreCullCount { get; private set; }
        public int PostRenderCount { get; private set; }
        public int LastHiddenCount { get; private set; }

        public void Bind(NativeKartCamera cameraRig, Transform fumo)
        {
            Restore();
            rig = cameraRig;
            CharacterRenderers = fumo ? fumo.GetComponentsInChildren<Renderer>() : new Renderer[0];
            previous = new bool[CharacterRenderers.Length];
        }

        void OnPreCull()
        {
            Restore();
            PreCullCount++;
            LastHiddenCount = 0;
            if (!rig || !rig.FirstPerson) return;
            for (int i = 0; i < CharacterRenderers.Length; i++)
            {
                var renderer = CharacterRenderers[i];
                if (!renderer) continue;
                previous[i] = renderer.enabled;
                if (renderer.enabled) LastHiddenCount++;
                renderer.enabled = false;
            }
            hidden = true;
        }

        void OnPostRender()
        {
            PostRenderCount++;
            Restore();
        }

        void OnDisable() => Restore();

        void Restore()
        {
            if (!hidden) return;
            for (int i = 0; i < CharacterRenderers.Length; i++)
                if (CharacterRenderers[i]) CharacterRenderers[i].enabled = previous[i];
            hidden = false;
        }
    }
}
