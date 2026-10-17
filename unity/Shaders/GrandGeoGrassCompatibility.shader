// Independently authored compatibility pass for materials using this shader identifier.
// Texture content is supplied only by the player's local original-package import.
Shader "Custom/Grand Geo Grass"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex TransformVertex
            #pragma fragment ReadTexture
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            struct VertexInput { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct PixelInput { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

            PixelInput TransformVertex(VertexInput input)
            {
                PixelInput output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return output;
            }

            float4 ReadTexture(PixelInput input) : SV_Target
            {
                return tex2D(_MainTex, input.uv);
            }
            ENDHLSL
        }
    }
}
