Shader "Fumo/NativeSurface"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutout", Range(0,1)) = 0
        _VertexColor ("Vertex colors", Range(0,1)) = 0
        _KartMask ("Kart color keys", Range(0,1)) = 0
        _Color2 ("Secondary paint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color, _Color2;
            float _Cutoff, _VertexColor, _KartMask;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; float4 color:COLOR; UNITY_FOG_COORDS(2) };
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = lerp(float4(1,1,1,1), v.color, _VertexColor);
                UNITY_TRANSFER_FOG(o,o.position);
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                if (_KartMask > 0.5)
                {
                    float first = saturate((c.g - max(c.r, c.b)) * 4);
                    float second = saturate((min(c.g, c.b) - c.r) * 4);
                    c.rgb = lerp(c.rgb, _Color.rgb, first);
                    c.rgb = lerp(c.rgb, _Color2.rgb, second);
                }
                else c *= _Color;
                c *= i.color;
                clip(c.a - _Cutoff);
                c.rgb *= 0.72 + 0.28 * saturate(dot(normalize(i.normal), normalize(_WorldSpaceLightPos0.xyz)));
                c.a = 1;
                UNITY_APPLY_FOG(i.fogCoord,c);
                return c;
            }
            ENDCG
        }
    }
}
