Shader "Fumo/NativeSettingsParticles" {
Properties { _MainTex("Particle texture",2D)="white"{} _TintColor("Tint",Color)=(1,1,1,1) _Lighting("Lighting",Float)=1 }
SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent"} Blend SrcAlpha OneMinusSrcAlpha Cull Off ZWrite Off Pass {
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
#include "Lighting.cginc"
sampler2D _MainTex; float4 _MainTex_ST; fixed4 _TintColor; float _Lighting;
struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;float3 normal:NORMAL;};
struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;float3 normal:TEXCOORD1;};
v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.color=v.color*_TintColor;o.normal=UnityObjectToWorldNormal(v.normal);return o;}
fixed4 frag(v2f i):SV_Target {fixed4 c=tex2D(_MainTex,i.uv)*i.color;float3 normal=normalize(i.normal+float3(0,0.0001,0));float3 light=ShadeSH9(float4(normal,1))+_LightColor0.rgb*saturate(dot(normal,normalize(_WorldSpaceLightPos0.xyz)));c.rgb*=lerp(float3(1,1,1),max(light,0.2),_Lighting);return c;}
ENDCG
} } }
