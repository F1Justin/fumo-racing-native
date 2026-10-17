Shader "Fumo/RestoredRetroWater"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = .5
        _Metallic ("Metallic", Range(0,1)) = 0
        _Speed ("Speed", Range(-10,10)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        float4 _Color;
        float _Speed, _Glossiness, _Metallic;
        struct Input { float2 uv_MainTex; };
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float4 color = tex2D(_MainTex, input.uv_MainTex + _Time.y * _Speed) * _Color;
            output.Albedo = color.rgb;
            output.Alpha = color.a;
            output.Smoothness = _Glossiness;
            output.Metallic = _Metallic;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
