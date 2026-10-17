Shader "Fumo/RestoredPyramid"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        [HDR] _Color2 ("Color2", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        _Height ("Height", Range(-10,10)) = 0
        _Base ("Base", Range(-10,10)) = 0
        _Speed ("Speed", Range(-10,10)) = 0
        [HDR] _RimColor ("RimColor", Color) = (1,1,1,1)
        _RimPower ("RimPower", Range(0,20)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "DisableBatching"="True" }
        LOD 100
        Cull [_Cull]
        ZWrite Off
        ColorMask RGB
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard alpha:premul vertex:vert
        // Original forward DXBC uses the NHxRoughness lookup of BRDF3.
        #define UNITY_BRDF_PBS BRDF3_Unity_PBS
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex;
        float4 _Color, _Color2, _RimColor;
        float _Glossiness, _Metallic, _Height, _Speed, _RimPower;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
            float localZ;
        };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.localZ = vertex.vertex.z;
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float4 sampleColor = tex2D(_MainTex, input.uv_MainTex + _Time.y * _Speed) * _Color;
            // The original gradient uses local Z, applies _Color twice, and extrapolates.
            output.Albedo = sampleColor.rgb * lerp(_Color.rgb, _Color2.rgb, input.localZ / _Height);
            output.Alpha = sampleColor.a;
            output.Metallic = _Metallic;
            output.Smoothness = _Glossiness;
            float3 view = normalize(UnityWorldSpaceViewDir(input.worldPos));
            float rim = 1 - saturate(dot(view, input.worldNormal));
            output.Emission = _RimColor.rgb * pow(rim, _RimPower);
        }
        ENDCG
    }
    FallBack Off
}
