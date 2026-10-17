Shader "Unlit/UIMovingPattern"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        [Toggle(USES_MASK)] _UsesMask ("Uses Mask", Float) = 0
        _MaskXMin ("MaskXMin", Range(0,1)) = 0
        _MaskXMax ("MaskXMax", Range(0,1)) = 0
        _Speed ("Speed", Range(-10,10)) = 0
        _Speed2 ("Speed2", Range(-10,10)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        ColorMask [_ColorMask]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local __ USES_MASK
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color;
            float _Speed, _Speed2, _MaskXMin, _MaskXMax;
            struct AppData { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(AppData input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                float4 color = tex2D(_MainTex, input.uv + _Time.y * float2(_Speed, _Speed2)) * _Color;
                #if defined(USES_MASK)
                color.a = saturate(color.a * (1 - input.uv.x + _MaskXMin - _MaskXMax));
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
