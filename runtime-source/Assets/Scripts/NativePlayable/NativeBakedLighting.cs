using UnityEngine;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(-1000)]
    public sealed class NativeBakedLighting : MonoBehaviour
    {
        public Texture2D[] colors;
        public Texture2D[] directions;
        public Texture2D[] shadowMasks;
        public LightmapsMode mode;
        public LightProbes probes;
        public Renderer[] renderers;
        public int[] indices;
        public Vector4[] scaleOffsets;
        public Light[] bakedLights;
        public int[] probeOcclusionIndices, occlusionMaskChannels;
        public LightmapBakeType[] bakeTypes;
        public MixedLightingMode[] mixedLightingModes;
        public bool[] isBaked;

        void Awake() => Apply();

        public void Apply()
        {
            var maps = new LightmapData[colors.Length];
            for (int i = 0; i < maps.Length; i++)
                maps[i] = new LightmapData { lightmapColor = colors[i], lightmapDir = directions[i], shadowMask = shadowMasks[i] };
            LightmapSettings.lightmapsMode = mode;
            LightmapSettings.lightmaps = maps;
            LightmapSettings.lightProbes = probes;
            Shader.SetGlobalFloat("_MeltingFactor", 0);
            int mapped = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                renderers[i].lightmapIndex = indices[i];
                if (!renderers[i].isPartOfStaticBatch)
                    renderers[i].lightmapScaleOffset = scaleOffsets[i];
                if (indices[i] >= 0 && indices[i] < maps.Length) mapped++;
            }
            for (int i = 0; i < bakedLights.Length; i++)
            {
                if (!bakedLights[i]) continue;
                bakedLights[i].bakingOutput = new LightBakingOutput
                {
                    probeOcclusionLightIndex = probeOcclusionIndices[i],
                    occlusionMaskChannel = occlusionMaskChannels[i],
                    lightmapBakeType = bakeTypes[i],
                    mixedLightingMode = mixedLightingModes[i],
                    isBaked = isBaked[i]
                };
            }
            Debug.Log("NATIVE_BAKED_LIGHTING maps=" + maps.Length + " renderers=" + mapped + " probes=" + (probes ? probes.count : 0));
        }
    }
}
