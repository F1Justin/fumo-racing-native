Shader "Fumo/NativeSettingsPost" {
Properties { _MainTex("Source",2D)="white"{} _Bloom("Bloom",Float)=0 _AO("AO",Float)=0 _CC("Color correction",Float)=0 _Quality("High quality",Float)=1 }
SubShader { Cull Off ZWrite Off ZTest Always Pass {
CGPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
sampler2D _MainTex; float4 _MainTex_TexelSize; UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture); float _Bloom,_AO,_CC,_Quality;
fixed4 frag(v2f_img i):SV_Target {
 float4 color=tex2D(_MainTex,i.uv); float2 delta=_MainTex_TexelSize.xy*(_Quality>0.5?2:4);
 float3 blur=tex2D(_MainTex,i.uv+float2(delta.x,0)).rgb+tex2D(_MainTex,i.uv-float2(delta.x,0)).rgb;
 if(_Quality>0.5) blur+=tex2D(_MainTex,i.uv+float2(0,delta.y)).rgb+tex2D(_MainTex,i.uv-float2(0,delta.y)).rgb;
 blur/=(_Quality>0.5?4:2); color.rgb+=max(blur-0.7,0)*0.22*_Bloom;
 float depth=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,i.uv));
 float neighbour=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,i.uv+delta));
 float occlusion=saturate((depth-neighbour)/max(depth*0.02,0.01)); color.rgb*=1-occlusion*0.18*_AO;
 float luminance=dot(color.rgb,float3(0.2126,0.7152,0.0722));float3 graded=lerp(luminance.xxx,color.rgb,1.08)*float3(1.02,1.00,0.98);color.rgb=lerp(color.rgb,graded,_CC);
 return color;
}
ENDCG
} } }
