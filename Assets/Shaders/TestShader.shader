Shader "Unlit/TestShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

       Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            SAMPLER(sampler_BaseMap);
        
            struct appdata
            {
                float3 vPos : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // To ensure that the Unity shader is SRP Batcher compatible, 
            // declare all Material properties inside
            CBUFFER_START(UnityPerMaterial)
                // Texture2DArray _ClipmapStack;
                // const int _MaxCount;

                // int2 _ClipmapCenter[2];

                // int _ClipSize;
                // int _BaseMapSize;
                // int _InvalidBorder;
                // float _WorldGridSize;
                // SamplerState sampler_ClipmapStack;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vPos);
                o.uv = 1.0 - v.uv;
                return o;
            }


                int clipmapL = 3;
                int clipmapS = 4;
            // transform the uv in mip0 to the toroidal uv in the clipmap stack 
            float2 GetClipmapUV(int clipmapStackLevel, inout float2 uv) 
            {
                float scale = 1.0;
                for (int i = clipmapStackLevel; i < clipmapS; i++) {
                    scale *= 0.5;
                }

                float2 coordInMip = fmod(uv, scale);
                return coordInMip;
            }

            void GetClipmapStackLevels(in float2 uv, out int coarse, out int fine, out float fraction) 
            {
                
            }


            float4 frag (v2f i) : SV_Target
            {
                float2 mag = GetClipmapUV(clipmapL, i.uv);
                return float4(mag, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}
