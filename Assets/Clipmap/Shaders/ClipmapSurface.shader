Shader "Unlit/ClipmapSurface"
{

    Properties
    {
        _BaseMapSize("Mip0 Texture Size", Integer) = 1024

        _ClipmapStack ("Clipmap Stack", 2DArray) = "white" {}

        _ClipmapStackSize("Clipmap Stack Size", Integer) = 1 
        
        _ClipSize("ClipSize", Integer) = 16
        
        _ClipmapUpdateGridSize("_ClipmapUpdateGridSize", Integer) = 2

        _InvalidBorder("Invalid Border", Integer) = 1

        _WorldGridSize("World Grid Size", float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}

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
                Texture2DArray _ClipmapStack;
                const int _MaxCount;

                int2 _ClipmapCenter[2];

                int _ClipSize;
                int _BaseMapSize;
                int _InvalidBorder;
                float _WorldGridSize;
                SamplerState sampler_ClipmapStack;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vPos);
                o.uv = 1.0 - v.uv;
                return o;
            }

            // transform the uv in mip0 to the toroidal uv in the clipmap stack 
            void getUV(inout int clipmapStackLevel, inout float2 baseUV) 
            {
                // TODO: pass required variables
                float3 centerInWorld;
                // end
                
                float2 coordInHomogeneousSpace = (baseUV - 0.5) * _BaseMapSize / _WorldGridSize;
                // TODO
            }

            #define blend 0
            float4 frag (v2f i) : SV_Target
            {
                float2 dx = ddx(i.uv);
                float2 dy = ddy(i.uv);
                float maxSqrPixelDiff = max(dot(dx, dx), dot(dy, dy));
                float mipLevel = 0.5 * log2(maxSqrPixelDiff * _BaseMapSize * _BaseMapSize * _WorldGridSize * _WorldGridSize);
                int mipLevelCoarse = floor(mipLevel);
                int mipLevelFine = mipLevelCoarse + 1;
                float mipFract = frac(mipLevel);

                // if (mipLevelCoarse < _ClipmapStackSize) 
                // {   

                // }
                // else
                // {

                // }

                // if (mipLevelFine < _ClipmapStackSize) 
                // {

                // }
                // else 
                // {

                // }

                float4 col1 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(i.uv, mipLevelCoarse));
                #if blend 
                    float4 col2 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(i.uv, mipLevelFine));
                    return lerp(col1, col2, mipFract);
                #else
                    return col1;
                #endif
            }
            ENDHLSL
        }
    }
}
