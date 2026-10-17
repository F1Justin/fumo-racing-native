Shader "Fumo/RestoredSnowSurface"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _NormalTex ("Normal", 2D) = "bump" {}
        _SnowMask ("Layer Mask", 2D) = "white" {}
        _SnowMainTex ("Layer Albedo", 2D) = "white" {}
        _SnowAOTex ("Layer Occlusion", 2D) = "white" {}
        _SnowNormalTex ("Layer Normal", 2D) = "bump" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossinessMelt ("Melt Smoothness", Range(0,1)) = 0.5
        _GlossinessSnow ("Layer Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        _SnowNoiseMask ("Layer Noise", 2D) = "black" {}
        _SnowNoiseSize ("Noise Scale", Float) = 1
        _SnowNoiseSizeRemove ("Removal Noise Scale", Float) = 1
        _SnowNoiseOffset ("Noise Offset", Float) = 0
        _SnowNoiseIntensity ("Noise Intensity", Float) = 1
        _SnowNoiseRemoveIntensity ("Removal Noise Intensity", Float) = 1
        [Toggle] _IsSnowMelting ("Is Snow Melting", Float) = 0
        _MeltWetColor ("Melt Wet Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #pragma target 3.0
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex, _NormalTex, _SnowMask, _SnowMainTex, _SnowAOTex, _SnowNormalTex, _SnowNoiseMask;
        float4 _MainTex_ST, _NormalTex_ST, _SnowMask_ST;
        float _SnowNoiseSize, _SnowNoiseSizeRemove, _SnowNoiseIntensity, _SnowNoiseRemoveIntensity;
        half _Glossiness, _GlossinessMelt, _GlossinessSnow, _Metallic, _IsSnowMelting, _MeltingFactor;
        half4 _MeltWetColor;

        struct Input
        {
            float2 primaryUV;
            float2 layerUV;
            float3 worldPos;
        };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.primaryUV = vertex.texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
            output.layerUV = vertex.texcoord.xy * _SnowMask_ST.xy + _SnowMask_ST.zw;
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            half noise = tex2D(_SnowNoiseMask, input.worldPos.xz * _SnowNoiseSize).r;
            half removal = tex2D(_SnowNoiseMask, input.worldPos.xz * _SnowNoiseSizeRemove).r;
            half mask = tex2D(_SnowMask, input.layerUV).r;
            // The two saturation steps and shared layer UV are explicit in the original DXBC.
            half blend = saturate(mask - removal * _SnowNoiseRemoveIntensity);
            blend = saturate(blend + noise * _SnowNoiseIntensity);
            half smoothness = lerp(_Glossiness, _GlossinessSnow, blend);
            half3 baseColor = tex2D(_MainTex, input.primaryUV).rgb;
            if (_IsSnowMelting > 0.5)
            {
                smoothness = lerp(smoothness, _GlossinessMelt, min(blend * 5, _MeltingFactor));
                baseColor *= lerp(half3(1,1,1), _MeltWetColor.rgb, min(blend * 10, 1));
                blend = saturate(blend - _MeltingFactor * 0.5);
            }
            half3 layerColor = tex2D(_SnowMainTex, input.layerUV).rgb * tex2D(_SnowAOTex, input.layerUV).rgb;
            float2 normalUV = input.primaryUV * _NormalTex_ST.xy + _NormalTex_ST.zw;
            half3 normal = UnpackNormal(tex2D(_NormalTex, normalUV));
            half3 layerNormal = UnpackNormal(tex2D(_SnowNormalTex, input.layerUV));
            output.Albedo = lerp(baseColor, layerColor, blend);
            output.Normal = lerp(normal, layerNormal, blend);
            output.Occlusion = 1;
            output.Metallic = _Metallic;
            output.Smoothness = smoothness;
            output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
