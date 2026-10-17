Shader "Fumo/NativeGrass"
{
    Properties
    {
        _MainTex ("Grass silhouette", 2D) = "white" {}
        _TopColor ("Top Color", Color) = (1,1,1,1)
        _BottomColor ("Bottom Color", Color) = (0,0,0,1)
        _Cutoff ("Cutoff", Range(0,1)) = 0.25
        _MipScale ("Mip Level Alpha Scale", Range(0,1)) = 0.25
        _GrassHeight ("Height in Source Space", Float) = 0.0025
        _GrassWidth ("Width in Source Space", Float) = 0.025
        _WindTex ("Wind Texture", 2D) = "white" {}
        _WindColor ("Wind Color", Color) = (0,0,0,1)
        _featureWind ("Wind", Float) = 0
        _WindData ("Direction XZ, Strength, Speed", Vector) = (0,-1,0,0)
        _Shininess ("Specular Shininess", Float) = 10
        _Sharpness ("Translucency Sharpness", Float) = 10
        _SpecularColour ("Specular Colour", Color) = (1,1,1,1)
        _TranslucentColour ("Translucent Colour", Color) = (1,1,1,1)
        _MaxRenderDistance ("Max Distance in Source Space", Float) = 0.89
        _BillboardDistance ("Billboard Distance in Source Space", Float) = 1
        _LODStart ("LOD Distance in Source Space", Float) = 0.34
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "DisableBatching"="True" }
        Cull Off
        ZWrite On
        // Deferred Metal renders these forward cards without an MSAA coverage mask.
        AlphaToMask Off
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile __ FUMO_GRASS_EARLY_VISIBILITY
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex, _WindTex;
            float4 _MainTex_ST, _MainTex_TexelSize, _WindTex_ST, _WindData;
            float4 _TopColor, _BottomColor, _WindColor, _SpecularColour, _TranslucentColour;
            float _Cutoff, _MipScale, _GrassHeight, _GrassWidth, _featureWind;
            float _Shininess, _Sharpness, _MaxRenderDistance, _BillboardDistance, _LODStart;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 card : TEXCOORD1;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 world : TEXCOORD2;
                float windColor : COLOR0;
                float visible : TEXCOORD5;
                UNITY_SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                float3 root = v.vertex.xyz;
                float3 camera = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                float distanceToCamera = distance(root, camera);
#if defined(FUMO_GRASS_EARLY_VISIBILITY)
                // NativeGrassBuild emits four identical roots and card IDs per quad.
                // Rejected cards already collapse to this root in the legacy path.
                // Skip wind sampling and basis rotations for those degenerate quads.
                bool cardVisible = distanceToCamera <= _MaxRenderDistance;
                if (distanceToCamera >= _BillboardDistance)
                    cardVisible = cardVisible && v.card.x < 0.5;
                else if (v.card.x > 1.5)
                    cardVisible = cardVisible && distanceToCamera <= _LODStart;
                if (!cardVisible)
                {
                    o.pos = UnityObjectToClipPos(v.vertex);
                    o.visible = 0;
                    return o;
                }
#endif
                float3 worldRoot = mul(unity_ObjectToWorld, v.vertex).xyz;
                float random = frac(sin(dot(root, float3(12.9898005, 78.2330017, 53.5390015))) * 43758.5469);
                float angle = random * 6.28318548;
                float height = _GrassHeight * v.color.r;
                float halfWidth = _GrassWidth * v.color.b * 0.5;
                float2 windSample = 0;
                float3 bend = 0;
                if (_featureWind > 0.5)
                {
                    float2 windUV = worldRoot.xz * _WindTex_ST.xy + _Time.y * _WindData.xy * _WindData.w;
                    windSample = tex2Dlod(_WindTex, float4(windUV, 0, 0)).rg;
                    bend.xz = (windSample * 2 - 1) * _WindData.z;
                }
                float3 local;
                float2 uv = v.uv;
                bool visible = distanceToCamera <= _MaxRenderDistance;
                if (distanceToCamera >= _BillboardDistance)
                {
                    float3 right = mul(unity_WorldToObject, mul(UNITY_MATRIX_I_V, float4(1,0,0,0))).xyz;
                    float3 up = mul(unity_WorldToObject, mul(UNITY_MATRIX_I_V, float4(0,1,0,0))).xyz;
                    right *= random > 0.5 ? 1 : -1;
                    local = root + right * ((v.uv.x * 2 - 1) * halfWidth) + (up + bend) * (height * v.uv.y);
                    uv.x = 1 - uv.x;
                    visible = visible && v.card.x < 0.5;
                }
                else
                {
                    float3 tangent = v.tangent.xyz;
                    float3 bitangent = cross(v.normal, tangent) * v.tangent.w;
                    float3 t = tangent;
                    float3 b = bitangent;
                    if (v.card.x > 1.5)
                    {
                        t = tangent * 0.647878766 + bitangent * 0.761743426;
                        b = bitangent * 0.647878766 - tangent * 0.761743426;
                        visible = visible && distanceToCamera <= _LODStart;
                    }
                    else if (v.card.x > 0.5)
                    {
                        if (distanceToCamera > _LODStart)
                        {
                            t = bitangent - tangent * 4.37113883e-08;
                            b = -tangent - bitangent * 4.37113883e-08;
                        }
                        else
                        {
                            t = tangent * 0.666011810 - bitangent * 0.745941222;
                            b = bitangent * 0.666011810 + tangent * 0.745941222;
                        }
                    }
                    // The original GS uses these asymmetric basis rotations for its three close cards.
                    float3 offset = halfWidth * (-sin(angle) * t + cos(angle) * b);
                    local = root + offset * (v.uv.x * 2 - 1) + (v.normal * height + bend) * v.uv.y;
                }
                if (!visible) local = root;
                v.vertex = float4(local, 1);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                // The source GS passes the authored object normal unchanged to its fragment program.
                o.normal = v.normal;
                o.uv = TRANSFORM_TEX(uv, _MainTex);
                o.windColor = length(windSample) * _featureWind;
                o.visible = visible ? 1 : 0;
                UNITY_TRANSFER_SHADOW(o, float2(0, 0));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(i.visible - 0.5);
                float4 textureColor = tex2D(_MainTex, i.uv);
                float2 texelUV = i.uv * _MainTex_TexelSize.zw;
                float2 dx = ddx(texelUV), dy = ddy(texelUV);
                float mip = 0.5 * log2(max(1, max(dot(dx, dx), dot(dy, dy))));
                float adjustedAlpha = textureColor.a * (1 + mip * _MipScale);
                clip(adjustedAlpha - _Cutoff);

                float3 normal = normalize(i.normal);
                float3 light = normalize(_WorldSpaceLightPos0).xyz;
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float diffuse = max(0, dot(normal, light));
                float backDiffuse = max(0, dot(-normal, light));
                float specular = pow(saturate(dot(reflect(-light, normal), view)), _Shininess);
                float translucency = pow(saturate(dot(-light, view)), _Sharpness);
                float3 ambient = SHEvalLinearL0L1(float4(i.normal, 1)) + SHEvalLinearL2(float4(i.normal, 1));
                float3 lighting = _LightColor0.rgb * (diffuse + i.uv.y *
                    (specular * _SpecularColour.rgb + (backDiffuse + translucency) * _TranslucentColour.rgb));
                UNITY_LIGHT_ATTENUATION(attenuation, i, i.world);
                float3 tint = lerp(_BottomColor.rgb, _TopColor.rgb, i.uv.y) + i.windColor * _WindColor.rgb * i.uv.y;
                // Original SHADOWS_SCREEN PS attenuates the direct term and retains SH under shadows.
                fixed4 color = fixed4(textureColor.rgb * tint * lighting * (ambient + _LightColor0.rgb * attenuation), 1);
                UNITY_APPLY_FOG(i.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
