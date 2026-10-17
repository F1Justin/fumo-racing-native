Shader "Fumo/RestoredWaterSurface" {
Properties {

		[HDR] _Color ("Color", Vector) = (1,1,1,1)
		[HDR] _DepthColor ("Depth Color", Vector) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_FoamTex ("Foam (RGB)", 2D) = "white" {}
		_FoamNormalTex ("Foam Normal", 2D) = "black" {}
		_NormalTex ("Normal", 2D) = "black" {}
		_NormalIntensity ("Normal Intensity", Float) = 1
		_DistortionTex ("Normal Distortion", 2D) = "black" {}
		_DistortionIntensity ("Distortion Intensity", Float) = 1
		_Glossiness ("Smoothness", Range(0, 1)) = 0.5
		_Metallic ("Metallic", Range(0, 1)) = 0
		[HDR] _GlowColor ("Glow Color", Vector) = (1,1,1,1)
		_FadeLength ("Fade Length", Range(0, 100)) = 1
		_DepthLength ("Depth Length", Range(0, 100)) = 1
		_ReflectionIntensity ("Reflection Intesnity", Range(0, 2)) = 1
		_Speed ("Speed", Vector) = (0,0,0,0)
		_FoamSpeed ("Foam Speed", Vector) = (0,0,0,0)
		_DistortSpeed ("Distort Speed", Vector) = (0,0,0,0)
	}
SubShader
{
    Tags { "Queue"="Transparent" "RenderType"="Transparent" }
    GrabPass { "_BackgroundTexture" }
    LOD 200
    CGPROGRAM
    #pragma surface surf Standard alpha:fade fullforwardshadows vertex:vert
    #pragma target 3.0
    #include "UnityStandardUtils.cginc"
    sampler2D _MainTex, _FoamTex, _FoamNormalTex, _NormalTex, _DistortionTex, _BackgroundTexture;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float4 _Color, _DepthColor, _GlowColor, _Speed, _FoamSpeed, _DistortSpeed, _DistortionTex_ST;
    float _NormalIntensity, _DistortionIntensity, _Glossiness, _Metallic, _FadeLength, _DepthLength, _ReflectionIntensity;
    struct Input { float2 uv_MainTex; float4 screenPos; float4 grabPos; float3 worldPos; float3 worldNormal; float3 viewDir; INTERNAL_DATA };
    void vert(inout appdata_full vertex, out Input output)
    {
        UNITY_INITIALIZE_OUTPUT(Input, output);
        output.grabPos = ComputeGrabScreenPos(UnityObjectToClipPos(vertex.vertex));
    }
    void surf(Input input, inout SurfaceOutputStandard output)
    {
        float2 uv = input.uv_MainTex + _Time.y * _Speed.xy;
        float2 foamUV = input.uv_MainTex + _Time.y * _FoamSpeed.xy;
        float2 distortionUV = input.uv_MainTex * _DistortionTex_ST.xy + _DistortionTex_ST.zw + _Time.y * _DistortSpeed.xy;
        float4 distortion = tex2D(_DistortionTex, distortionUV);
        float amount = distortion.x * _DistortionIntensity;
        float3 projected = input.grabPos.xyw;
        float3 distorted = lerp(projected, (projected + float3(distortion.xy * projected.xy, projected.z)) * _DistortionIntensity, distortion.x);
        float3 background = tex2D(_BackgroundTexture, distorted.xy / distorted.z).rgb;
        float rawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(input.screenPos));
        float difference = LinearEyeDepth(rawDepth) + UnityWorldToViewPos(input.worldPos).z + amount * 2;
        float foam = pow(1 - saturate(difference / (_FadeLength + amount)) + amount, 4);
        float depth = pow(saturate(difference / (_DepthLength + amount)) + amount, 4);
        float4 color = lerp(tex2D(_MainTex, uv) * _Color, tex2D(_FoamTex, foamUV) * _GlowColor, foam);
        color = lerp(color, float4(_Color.rgb * _Color.rgb, _Color.a), depth);
        float alpha = saturate(1 + amount - color.a);
        background = saturate(background * (1 - .5 * depth)) * _DepthColor.rgb;
        float3 direction = normalize(input.worldPos - _WorldSpaceCameraPos);
        float3 reflection = DecodeHDR(UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, direction), unity_SpecCube0_HDR);
        output.Albedo = lerp(color.rgb, background * color.rgb, alpha) + reflection * _ReflectionIntensity;
        output.Normal = lerp(UnpackScaleNormal(tex2D(_NormalTex, uv), _NormalIntensity), UnpackScaleNormal(tex2D(_FoamNormalTex, uv), _NormalIntensity), foam);
        output.Alpha = alpha;
        output.Smoothness = _Glossiness;
        output.Metallic = _Metallic;
    }
    ENDCG
}
FallBack "Transparent/Diffuse"
}
