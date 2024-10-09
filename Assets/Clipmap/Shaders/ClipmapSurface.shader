Shader "Unlit/ClipmapSurface"
{

    Properties
    {
        _BaseMapSize("Mip0 Texture Size", Integer) = 1024

        _ClipmapStack ("Clipmap Stack", 2DArray) = "white" {}

        _ClipmapStackSize("Clipmap Stack Size", Integer) = 4
        
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
                const int _ClipmapStackSize;
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
            void GetClipmapUV(int clipmapStackLevel, inout float2 uv) 
            {
                float scale = 1.0;
                for (int i = clipmapStackLevel; i < _ClipmapStackSize - 1; i++) {
                    scale *= 0.5;
                }
                uv = uv % scale / scale;
            }

            void GetClipmapStackLevels(in float2 uv, out int coarse, out int fine, out float fraction) 
            {
                float2 dx = ddx(uv);
                float2 dy = ddy(uv);
                float maxSqrPixelDiff = max(dot(dx, dx), dot(dy, dy)) * _BaseMapSize * _BaseMapSize * _WorldGridSize * _WorldGridSize;
                float mipLevel = 0.5 * log2(maxSqrPixelDiff);
                int mipLevelFine = floor(mipLevel);
                int mipLevelCoarse = mipLevelFine + 1;
                float mipFract = frac(mipLevel);

                coarse = mipLevelCoarse;
                fine = mipLevelFine;
                fraction = mipFract;

                // To be changed to forloop with step() to define coarse and fine (ref CalculateMacroClipmapLevel in O3de)
            }

            #define BLEND 0
            float4 frag (v2f i) : SV_Target
            {
                int mipLevelCoarse;                                
                int mipLevelFine;
                float mipFract;
                GetClipmapStackLevels(i.uv, mipLevelCoarse, mipLevelFine, mipFract);

                float2 newUV = i.uv;
                GetClipmapUV(mipLevelFine, newUV);
                
                // float4 col1 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(i.uv, mipLevelFine));
                float4 col1 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(newUV, mipLevelFine));
                
                #if BLEND 
                    float4 col2 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(i.uv, mipLevelCoarse));
                    return lerp(col1, col2, mipFract);
                #else
                    return col1;
                #endif
            }
            ENDHLSL
        }
    }
}
