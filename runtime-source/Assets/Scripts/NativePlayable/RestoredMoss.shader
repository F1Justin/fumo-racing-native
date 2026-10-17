Shader "Fumo/RestoredMoss"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _MeltTex ("Melt Albedo", 2D) = "white" {}
        _NormalTex ("Normal", 2D) = "bump" {}
        _NormalIntensity ("Normal Intensity", Float) = 1
        _AOTex ("Occlusion", 2D) = "white" {}
        _BlendMaskTex ("Blend Mask", 2D) = "white" {}
        _MainTex2 ("Layer Albedo", 2D) = "white" {}
        _NormalTex2 ("Layer Normal", 2D) = "bump" {}
        _NormalIntensity2 ("Layer Normal Intensity", Float) = 1
        _AOTex2 ("Layer Occlusion", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossinessMelt ("Melt Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        _Height ("Height", Float) = 0
        _BlendDistance ("Blend Distance", Float) = 0
        _IsLocal ("Is Local Height", Float) = 0
        [Toggle] _IsSnowMelting ("Is Snow Melting", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert dithercrossfade
        #pragma target 3.0
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex, _MainTex2, _BlendMaskTex, _NormalTex, _NormalTex2, _AOTex, _AOTex2, _MeltTex;
        float4 _MainTex_ST, _MainTex2_ST;
        float _Height, _BlendDistance, _NormalIntensity, _NormalIntensity2;
        half _Glossiness, _GlossinessMelt, _Metallic, _IsSnowMelting, _MeltingFactor;

        struct Input
        {
            float2 primaryUV;
            float3 worldPos;
        };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.primaryUV = vertex.texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            // Both original IS_LOCAL variants use world height; layer UV is transformed a second time.
            float2 layerUV = input.primaryUV * _MainTex2_ST.xy + _MainTex2_ST.zw;
            half mask = tex2D(_BlendMaskTex, input.primaryUV).r;
            half blend = saturate(mask * ((input.worldPos.y - _Height) * _BlendDistance));
            half3 albedo = lerp(tex2D(_MainTex, input.primaryUV).rgb, tex2D(_MainTex2, layerUV).rgb, blend);
            half3 occlusion = lerp(tex2D(_AOTex, input.primaryUV).rgb, tex2D(_AOTex2, layerUV).rgb, blend);
            half3 normal = UnpackScaleNormal(tex2D(_NormalTex, input.primaryUV), _NormalIntensity);
            half3 layerNormal = UnpackScaleNormal(tex2D(_NormalTex2, layerUV), _NormalIntensity2);
            half smoothness = _Glossiness;
            if (_IsSnowMelting > 0.5)
            {
                half meltBlend = saturate(mask * 2 - _MeltingFactor);
                albedo = lerp(tex2D(_MeltTex, input.primaryUV).rgb, albedo, meltBlend);
                smoothness = lerp(_Glossiness, _GlossinessMelt, saturate(_MeltingFactor - mask));
            }
            // Original pixel programs multiply RGB AO into the surface color before lighting.
            output.Albedo = albedo * occlusion;
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
