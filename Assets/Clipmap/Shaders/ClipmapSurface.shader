Shader "Unlit/ClipmapSurface"
{

    Properties
    {
        _ClipmapLevel ("Clipmap Levels", 2DArray) = "red" {}

        _EnableTransitionRegionOverlay ("_ShowTransitionRegion", float) = 0

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
            #pragma enable_d3d11_debug_symbols
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define CLIPMAP_MAX_SIZE 6           

            // To ensure that the Unity shader is SRP Batcher compatible, 
            // declare all Material properties inside
            CBUFFER_START(UnityPerMaterial)
                // Properties
                Texture2DArray _ClipmapLevel;
                float _EnableTransitionRegionOverlay;
            
                // Uniforms
                float _WorldScale;
                int _InvalidBorder;
                int _ClipSize;
                int _ClipHalfSize;
                int _ClipmapStackLevelCount;

                float2 _ClipmapCenter[CLIPMAP_MAX_SIZE];
                float _MipSize[CLIPMAP_MAX_SIZE];
                float _MipHalfSize[CLIPMAP_MAX_SIZE];
                float _ClipScaleToMip[CLIPMAP_MAX_SIZE];  
                float _MipScaleToWorld[CLIPMAP_MAX_SIZE];     
                
            CBUFFER_END
            
            SamplerState sampler_ClipmapLevel{
                MinLOD = 0;
                MaxLOD = 0;
                BorderColor = {1,1,1,1};
                AddressU = D3D11_TEXTURE_ADDRESS_BORDER;
                AddressV = D3D11_TEXTURE_ADDRESS_BORDER;
            };

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

            // ========== Helper Function =============================================================================
            // transform the uv in mip0 to the toroidal uv in the clipmap stack 
            void GetClipmapUV(int clipmapStackLevel, inout float2 uv) 
            {
                uv = frac(uv * _ClipScaleToMip[clipmapStackLevel]);
            }

            
            void GetClipmapStackLevels(in float2 uv, out int coarseLevelIndex, out int fineLevelIndex, out float fraction) 
            {
                float2 homogeneousCoord = (uv - 0.5) * _MipSize[0];
                int clipmapLevelincludeCount = 0;
                for (int levelIndex = 0; levelIndex < _ClipmapStackLevelCount; ++levelIndex) {
                    float2 diff = homogeneousCoord - (_ClipmapCenter[levelIndex]) * _MipScaleToWorld[levelIndex];
                    float2 sqrDiff = diff * diff;

                    float2 sqrHalfSize = pow((_ClipHalfSize) * _MipScaleToWorld[levelIndex], 2);
                    float2 containXY = step(sqrDiff, sqrHalfSize);
                    // x+y = [0, 1, 2], 2 means the coordinates in both axis are within the current clipmap level
                    float contain = step(1.5, containXY.x + containXY.y); 
                    clipmapLevelincludeCount += contain;
                }
                fineLevelIndex = _ClipmapStackLevelCount - clipmapLevelincludeCount;
                coarseLevelIndex = min(fineLevelIndex + 1, _ClipmapStackLevelCount); 
                // Blending algorithm from: https://hhoppe.com/proj/geomclipmap/
                
                // To be optimized VVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV 
                float w = 0.1;
                float2 diff = homogeneousCoord - (_ClipmapCenter[fineLevelIndex]) * _MipScaleToWorld[fineLevelIndex];
                float2 halfSize = _ClipHalfSize * _MipScaleToWorld[fineLevelIndex];
                float2 proportion = (abs(diff) + 1) / halfSize; 
                proportion = (proportion - (1 - w)) / w;
                fraction = max(proportion.x, proportion.y);
                fraction = clamp(fraction, 0, 1);
            }

            // ========== Shader Stage =============================================================================
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vPos);
                o.uv = 1.0 - v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 retCol;
                int mipLevelCoarse = 0;                                
                int mipLevelFine = 0;
                float mipFract = 0;
                GetClipmapStackLevels(i.uv, mipLevelCoarse, mipLevelFine, mipFract);

                float2 fineUV = i.uv;
                GetClipmapUV(mipLevelFine, fineUV);
                float2 coarseUV = i.uv;
                GetClipmapUV(mipLevelCoarse, coarseUV);
                
                float4 col1 = col1 = _ClipmapLevel.SampleLevel(sampler_ClipmapLevel, float3(fineUV, mipLevelFine), 0);
                float4 col2 = _ClipmapLevel.SampleLevel(sampler_ClipmapLevel, float3(coarseUV, mipLevelCoarse), 0);
                
                retCol = lerp(col1, col2, mipFract);

                float3 transitionRegionOverlayColors[8] = {{1,0,0}, {0,1,0}, {0,0,1}, {1,1,1}, {1,0,0}, {0,1,0}, {0,0,1}, {1,1,1}};
                retCol += float4(transitionRegionOverlayColors[mipLevelFine].rgb * mipFract, 1) * _EnableTransitionRegionOverlay;

                return retCol;
            }
            ENDHLSL
        }
    }
}
