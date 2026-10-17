Shader "Fumo/RestoredKart"
{
    Properties
    {
        _Color ("Color", Vector) = (1,1,1,1)
        _Color2 ("Color2", Vector) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _MetallicTex ("Metallic (RA)", 2D) = "white" {}
        _EmissiveTex ("Emissive tex", 2D) = "black" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        _Border ("BorderSetting", Range(0,1000)) = 100
        _KeyColor ("Key Color", Vector) = (0,1,0,1)
        _KeyColor2 ("Key Color 2", Vector) = (0,1,0,1)
        _Color2Enabled ("Color 2 Enabled", Range(0,1000)) = 0
        _SpeedIntensity ("Speed Intensity", Range(0,10)) = 0
        _Near ("Near", Range(0,2)) = 0.01
        _DirtMask ("Dirt Mask", 2D) = "white" {}
        _MaskWheelKey ("Mask Wheel Key", Vector) = (1,1,1,1)
        _WheelDirtAmount ("Wheel Dirt Amount", Range(0,1)) = 0
        _WheelDirtTile ("Wheel Dirt Tile", Range(-10,10)) = 0
        _DirtTex ("Dirt (RGB)", 2D) = "black" {}
        _DirtAmount ("Dirt Amount", Range(0,1)) = 0
        _DirtColor ("Dirt Color", Vector) = (1,1,1,1)
        _DirtColorEnabled ("Dirt Color Enabled", Range(0,1)) = 0
        _AOMap ("AO Map", 2D) = "white" {}
        [HDR] _WheelHeatColor ("Wheel Heat Color", Vector) = (1,1,1,1)
        _WheelHeatAmount ("Wheel Heat Amount", Range(0,1)) = 0
        [HideInInspector] _KartMask ("Kart paint masks", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull [_Cull]
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows vertex:vert
        // Original forward programs use the NHxRoughness lookup of BRDF3.
        #define UNITY_BRDF_PBS BRDF3_Unity_PBS
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex, _MetallicTex, _EmissiveTex, _DirtTex, _DirtMask, _AOMap;
        float4 _Color, _Color2, _KeyColor, _KeyColor2, _MaskWheelKey, _DirtColor, _WheelHeatColor;
        float4 _DirtTex_ST, _AOMap_ST;
        float _Glossiness, _Metallic, _Color2Enabled, _SpeedIntensity, _Near;
        float _DirtAmount, _DirtColorEnabled, _WheelDirtAmount, _WheelHeatAmount;

        struct Input
        {
            float2 uv_MainTex;
            float2 dirtUV;
            float2 aoUV;
        };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.dirtUV = vertex.texcoord1.xy * _DirtTex_ST.xy + _DirtTex_ST.zw;
            output.aoUV = vertex.texcoord2.xy * _AOMap_ST.xy + _AOMap_ST.zw;
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float4 baseColor = tex2D(_MainTex, input.uv_MainTex);
            float3 paint = lerp(_Color.rgb, baseColor.rgb, min(distance(baseColor.rgb, _KeyColor.rgb) * 10, 1));
            paint = lerp(_Color2.rgb, paint, min(distance(paint, _KeyColor2.rgb) * 10, 1));
            // Skate sets the grid to zero; bypass its original undefined 0/0 UV sample on Metal.
            if (_Color2Enabled > 0)
            {
                float2 gridUV = floor(input.uv_MainTex * _Color2Enabled) / _Color2Enabled;
                clip(distance(tex2D(_MainTex, gridUV), _KeyColor2) - _Near);
            }

            float4 dirt = tex2D(_DirtTex, input.dirtUV);
            float3 dirtMask = tex2D(_DirtMask, input.uv_MainTex).rgb;
            float wheelMask = (distance(dirtMask, _MaskWheelKey.rgb) - 1) * 10;
            float bodyDirt = saturate((_DirtAmount - min(dirt.a, _DirtAmount)) * dirtMask.r * _DirtAmount * 4);
            float wheelDirt = saturate((_WheelDirtAmount - min(dirt.a, _WheelDirtAmount)) * wheelMask * _WheelDirtAmount * 4);
            float3 dirtColor = lerp(dirt.rgb, _DirtColor.rgb, _DirtColorEnabled);
            paint = lerp(paint, dirtColor, wheelDirt);
            paint = lerp(paint, dirtColor, bodyDirt);
            output.Albedo = paint * tex2D(_AOMap, input.aoUV).rgb;
            output.Occlusion = 1;

            // The original metal, emission and dirt-mask samples share transformed MainTex UV.
            float4 metal = tex2D(_MetallicTex, input.uv_MainTex);
            output.Metallic = metal.r * _Metallic * (1 - bodyDirt);
            output.Smoothness = metal.a * _Glossiness * (1 - bodyDirt);
            output.Emission = baseColor.rgb * saturate(wheelMask) * _WheelHeatColor.rgb * _WheelHeatAmount
                + tex2D(_EmissiveTex, input.uv_MainTex).rgb * _SpeedIntensity;
            output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
