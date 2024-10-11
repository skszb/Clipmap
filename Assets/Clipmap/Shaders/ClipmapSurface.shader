Shader "Unlit/ClipmapSurface"
{

    Properties
    {
        _ClipmapLevel ("Clipmap Levels", 2DArray) = "red" {}
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


            #define CLIPMAP_MAX_SIZE 6           

            // To ensure that the Unity shader is SRP Batcher compatible, 
            // declare all Material properties inside
            CBUFFER_START(UnityPerMaterial)
                // Properties
                Texture2DArray _ClipmapLevel;
            
                // Uniforms
                float _WorldGridSize;
                int _InvalidBorder;
                int _ClipSize;
                int _ClipmapStackLevelCount;

                float2 _ClipmapCenter[CLIPMAP_MAX_SIZE];
                float _MipSize[CLIPMAP_MAX_SIZE];
                float _MipHalfSize[CLIPMAP_MAX_SIZE];
                float _ClipSizeScaleToMip[CLIPMAP_MAX_SIZE];       

                SamplerState sampler_ClipmapLevel;
            CBUFFER_END
            

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

            
            // transform the uv in mip0 to the toroidal uv in the clipmap stack 
            void GetClipmapUV(int clipmapStackLevel, inout float2 uv) 
            {
                float scale = _ClipSizeScaleToMip[clipmapStackLevel];
                uv = frac(uv * scale);
            }

            
            void GetClipmapStackLevels(in float2 uv, out int coarse, out int fine, out float fraction) 
            {
                // To be changed to forloop with step() to define coarse and fine (ref CalculateMacroClipmapLevel in o3de)
                float2 homogeneousCoord = (uv - 0.5) * _MipSize[0];
                int clipmapLevelincludeCount = 0;
                for (int levelIndex = 0; levelIndex < _ClipmapStackLevelCount; ++levelIndex) {
                    // check mip coordinate within clipmap
                    // if not contain, go to next corser level
                    float2 diff = homogeneousCoord - (_ClipmapCenter[levelIndex] + 0.5) * _ClipSizeScaleToMip[levelIndex];
                    float2 sqrDiff = diff * diff;

                    float2 halfSize = 0.5 * _MipSize[levelIndex] * _ClipSizeScaleToMip[levelIndex];
                    float2 sqrHalfSize = halfSize * halfSize;

                    float2 containXY = step(sqrDiff, sqrHalfSize);
                    float contain = step(1.5, containXY.x + containXY.y); // x+y = [0, 1, 2], 2 means within clipmap
                    clipmapLevelincludeCount += contain;
                }
                fine = _ClipmapStackLevelCount - clipmapLevelincludeCount;
                coarse = min(fine + 1, _ClipmapStackLevelCount); 
                fraction = 0;
            }

            
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vPos);
                o.uv = 1.0 - v.uv;
                return o;
            }


            #define BLEND 0
            float4 frag (v2f i) : SV_Target
            {
                int mipLevelCoarse = 0;                                
                int mipLevelFine = 0;
                float mipFract = 0;
                GetClipmapStackLevels(i.uv, mipLevelCoarse, mipLevelFine, mipFract);

                float2 newUV = i.uv;
                GetClipmapUV(mipLevelFine, newUV);
                
                float4 col1 = _ClipmapLevel.Sample(sampler_ClipmapLevel, float3(newUV, mipLevelFine));
                
                #if BLEND 
                    float4 col2 = _ClipmapLevel.Sample(sampler_ClipmapLevel, float3(i.uv, mipLevelCoarse));
                    return lerp(col1, col2, mipFract);
                #else
                    return col1;
                #endif
            }
            ENDHLSL
        }
    }
}
