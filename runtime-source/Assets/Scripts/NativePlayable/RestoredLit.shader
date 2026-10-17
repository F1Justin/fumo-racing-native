Shader "Fumo/RestoredLit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _MetallicGlossMap ("Metallic and smoothness", 2D) = "white" {}
        _HasMetallicMap ("Use metallic texture", Float) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        _EmissionMap ("Emission", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission tint", Color) = (0,0,0,0)
        _Cutoff ("Alpha clip", Range(0,1)) = 0
        _VertexColor ("Vertex colors", Float) = 0
        _KartMask ("Kart paint masks", Float) = 0
        _SpeedIntensity ("Exhaust heat", Float) = 0
        _Color2 ("Secondary paint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert dithercrossfade
        #pragma target 3.0
        #include "UnityStandardUtils.cginc"
        sampler2D _MainTex, _BumpMap, _OcclusionMap, _MetallicGlossMap, _EmissionMap;
        fixed4 _Color, _Color2, _EmissionColor;
        half _BumpScale, _Glossiness, _Metallic, _HasMetallicMap, _KartMask, _VertexColor, _SpeedIntensity, _Cutoff;
        float4 _OcclusionMap_ST;
        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
            float2 uv_MetallicGlossMap;
            float2 uv_EmissionMap;
            float4 color : COLOR;
            float2 occlusionUV;
        };
        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            float2 aoUV = _KartMask > 0.5 ? vertex.texcoord2.xy : vertex.texcoord.xy;
            output.occlusionUV = aoUV * _OcclusionMap_ST.xy + _OcclusionMap_ST.zw;
        }
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 albedo = tex2D(_MainTex, input.uv_MainTex);
            clip(albedo.a - _Cutoff);
            if (_KartMask > 0.5)
            {
                half primary = saturate((albedo.g - max(albedo.r, albedo.b)) * 4);
                half secondary = saturate((min(albedo.g, albedo.b) - albedo.r) * 4);
                albedo.rgb = lerp(albedo.rgb, _Color.rgb, primary);
                albedo.rgb = lerp(albedo.rgb, _Color2.rgb, secondary);
            }
            else albedo *= _Color;
            albedo *= lerp(fixed4(1,1,1,1), input.color, _VertexColor);
            output.Albedo = albedo.rgb;
            output.Alpha = albedo.a;
            output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_BumpMap), _BumpScale);
            if (_KartMask > 0.5)
            {
                // The original kart shader samples AO from TEXCOORD2 and multiplies its RGB into albedo.
                output.Albedo *= tex2D(_OcclusionMap, input.occlusionUV).rgb;
                output.Occlusion = 1;
            }
            else output.Occlusion = tex2D(_OcclusionMap, input.occlusionUV).g;
            fixed4 metal = lerp(fixed4(1,1,1,1), tex2D(_MetallicGlossMap, input.uv_MetallicGlossMap), _HasMetallicMap);
            output.Metallic = _Metallic * metal.r;
            output.Smoothness = _Glossiness * metal.a;
            output.Emission = tex2D(_EmissionMap, input.uv_EmissionMap).rgb * _EmissionColor.rgb
                * (_KartMask > 0.5 ? _SpeedIntensity : 1);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
