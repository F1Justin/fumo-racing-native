Shader "Fumo/RestoredLeaves"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        [HDR] _Color2 ("Color 2", Color) = (1,1,1,1)
        [HDR] _Color3 ("Color 3", Color) = (1,1,1,1)
        [HDR] _ColorSpecular ("Specular color", Color) = (0,0,0,1)
        _ClipVa ("Leaf alpha clip", Range(0,1)) = 0.5
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        _Height ("Height", Range(-200,200)) = 0
        _HeightOffset ("Height offset", Range(-100,100)) = 0
        _Intensity ("Emission intensity", Range(0,10)) = 0
        _SpecularOff ("Specular off", Range(0,1)) = 1
        _HeightOff ("Height off", Range(0,1)) = 1
        _WindSpeed ("Wind speed", Range(-10,10)) = 0
        _Noise ("Noise", 2D) = "white" {}
        _WindIntensity ("Wind intensity", Range(-10,10)) = 0
        [HideInInspector] _Cutoff ("Surface alpha clip", Range(0,1)) = 0.5
        [HideInInspector] _KartMask ("Kart paint masks", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 200
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf StandardSpecular fullforwardshadows vertex:vert addshadow alphatest:_Cutoff dithercrossfade
        #pragma target 3.0
        sampler2D _MainTex, _Noise;
        float4 _Color, _Color2, _Color3, _ColorSpecular;
        float _ClipVa, _Glossiness, _Height, _HeightOffset, _Intensity;
        float _SpecularOff, _HeightOff, _WindSpeed, _WindIntensity;
        float _MeltingFactor;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float originalHeight;
        };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.originalHeight = vertex.vertex.y;
            float3 originalWorld = mul(unity_ObjectToWorld, vertex.vertex).xyz;
            float2 windUV = originalWorld.xy + float2(_Time.x * _WindSpeed, 0);
            float wind = (tex2Dlod(_Noise, float4(windUV, 0, 0)).r - 0.5) * _WindIntensity;
            vertex.vertex.xyz += wind * vertex.texcoord.y;
        }

        void surf(Input input, inout SurfaceOutputStandardSpecular output)
        {
            float4 textureColor = tex2D(_MainTex, input.uv_MainTex);
            clip(textureColor.a - _ClipVa);
            float heightBlend = saturate((input.originalHeight + _HeightOffset) * (1 - _HeightOff) /
                (abs(_Height) > 0.0001 ? _Height : 1));
            float3 baseColor = lerp(textureColor.rgb, _Color.rgb, heightBlend);
            float3 noise = tex2D(_Noise, input.worldPos.xz * 0.003).rgb;
            float firstMask = saturate(noise.r);
            float tintBlend = max(firstMask, min(pow(noise.b, 1.5), 1));
            float3 tinted = baseColor * lerp(_Color.rgb, _Color2.rgb, tintBlend);
            float snowBlend = max(firstMask, min(noise.g * noise.g, 1) * _MeltingFactor) * _MeltingFactor;
            float3 albedo = lerp(tinted, tinted * _Color3.rgb, snowBlend);
            output.Albedo = albedo;
            output.Specular = _ColorSpecular.rgb * (1 - _SpecularOff);
            output.Smoothness = _Glossiness;
            output.Occlusion = 1;
            output.Emission = albedo * _Intensity;
            output.Alpha = textureColor.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
