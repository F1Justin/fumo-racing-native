using UnityEngine;
namespace Fumo.NativePlayable
{
    // Keep the image-effect callback off the normal PostProcessLayer camera.
    [RequireComponent(typeof(Camera))]
    public sealed class NativeFallbackPostEffect : MonoBehaviour
    {
        NativeSceneSettings.EffectReport report;
        Material material;
        bool legacyCopy;
        public void Configure(NativeSceneSettings.EffectReport value, bool copy)
        { report = value; legacyCopy = copy; }
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (report == null || (legacyCopy && report.realPostProcessing))
            { Graphics.Blit(source, destination); return; }
            if (!material)
            {
                var shader = NativeContent.Load<Shader>("NativeSettings/post");
                if (shader) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (!material) { Graphics.Blit(source, destination); return; }
            material.SetFloat("_Bloom", report.bloom ? 1 : 0);
            material.SetFloat("_AO", report.ao ? 1 : 0);
            material.SetFloat("_CC", report.cc ? 1 : 0);
            material.SetFloat("_Quality", report.highPostQuality ? 1 : 0);
            Graphics.Blit(source, destination, material);
        }
        void OnDestroy() { if (material) Destroy(material); }
    }
}
