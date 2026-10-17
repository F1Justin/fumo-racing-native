using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeRenderingQuality
    {
        public static int EffectiveVSyncCount(int savedValue)
        {
            // macOS uses the configured frame-rate clock; Metal present synchronization stalls the startup loop.
            return Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor ? 0 : savedValue;
        }

        public static void Apply()
        {
            int medium = Array.IndexOf(QualitySettings.names, "Medium");
            if (medium < 0) throw new InvalidOperationException("Original Medium quality preset missing");
            QualitySettings.SetQualityLevel(medium);
            QualitySettings.pixelLightCount = 3;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowDistance = 75;
            QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
            QualitySettings.skinWeights = SkinWeights.FourBones;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            QualitySettings.antiAliasing = 0;
            QualitySettings.softParticles = false;
            QualitySettings.softVegetation = true;
            QualitySettings.realtimeReflectionProbes = true;
            QualitySettings.billboardsFaceCameraPosition = true;
            QualitySettings.lodBias = 1;
            QualitySettings.particleRaycastBudget = 4096;
            QualitySettings.vSyncCount = 0;
        }
    }
}
