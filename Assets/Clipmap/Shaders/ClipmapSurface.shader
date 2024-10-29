Shader "Unlit/ClipmapSurface"
{

    Properties
    {
        _ClipmapStack ("Clipmap Stack", 2DArray) = "white" {}
        _ClipmapPyramid ("Clipmap Pyramid", 2D) = "white" {}

        _EnableTransitionRegionOverlay ("_ShowTransitionRegion", float) = 0

    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"
        }

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
                Texture2DArray _ClipmapStack;
                Texture2D _ClipmapPyramid;
                float _EnableTransitionRegionOverlay;

                // Uniforms
                float _WorldScale;
                int _InvalidBorder;
                int _ClipSize;
                int _ClipHalfSize;
                int _ClipmapStackLevelCount;
                int _MaxTextureLOD;

                float2 _ClipCenter[CLIPMAP_MAX_SIZE];
                float _MipSize[CLIPMAP_MAX_SIZE];
                float _MipHalfSize[CLIPMAP_MAX_SIZE];
                float _ClipScaleToMip[CLIPMAP_MAX_SIZE];
                float _MipScaleToWorld[CLIPMAP_MAX_SIZE];

            CBUFFER_END

            SamplerState sampler_ClipmapStack;

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
            void GetClipmapUV(inout float2 uv, in int clipmapStackLevel)
            {
                uv = frac(uv * _ClipScaleToMip[clipmapStackLevel]);
            }

            void GetClipmapStackLevels(in float2 uv, out int coarseLevelIndex, out int fineLevelIndex,
                                                                   out float fraction)
            {
                // mip calculation by world space
                float2 homogeneousCoord = (uv - 0.5) * _MipSize[0];
                int clipmapLevelincludeCount = 0;
                for (int levelIndex = _MaxTextureLOD; levelIndex < _ClipmapStackLevelCount; ++levelIndex)
                {
                    float2 diff = homogeneousCoord - _ClipCenter[levelIndex] * _MipScaleToWorld[levelIndex];
                    float2 sqrDiff = diff * diff;

                    float2 sqrHalfSize = pow((_ClipHalfSize) * _MipScaleToWorld[levelIndex], 2);
                    float2 containXY = step(sqrDiff, sqrHalfSize);
                    // x+y = [0, 1, 2], 2 means the coordinates in both axis are within the current clipmap level
                    float contain = step(1.5, containXY.x + containXY.y);
                    clipmapLevelincludeCount += contain;
                }
                fineLevelIndex = _ClipmapStackLevelCount - clipmapLevelincludeCount;

                // isotropic sampling 
                // To be updated to anisotropic VVVVVVVVVVVVVVVVVVVVVVVVVVVVVV
                float2 dx = ddx(uv);
                float2 dy = ddy(uv);
                float maxSqrPixelDiff = max(dot(dx, dx), dot(dy, dy)) * _MipSize[0] * _MipSize[0];
                float mipLevelScreenSpace = 0.5 * log2(maxSqrPixelDiff);
                int mipLevelScreenSpaceFine = floor(mipLevelScreenSpace);
                float mipLevelScreenSpaceFract = frac(mipLevelScreenSpace);

                // combine world space and screen space
                fineLevelIndex = min(max(fineLevelIndex, mipLevelScreenSpaceFine), _ClipmapStackLevelCount);
                coarseLevelIndex = fineLevelIndex + 1;

                // Blending algorithm from: https://hhoppe.com/proj/geomclipmap/
                // To be optimized VVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV 
                float w = 0.1;
                float2 diff = homogeneousCoord - (_ClipCenter[fineLevelIndex]) * _MipScaleToWorld[fineLevelIndex];
                float2 halfSize = _ClipHalfSize * _MipScaleToWorld[fineLevelIndex];
                float2 proportion = (abs(diff) + 1) / halfSize;
                proportion = (proportion - (1 - w)) / w;
                fraction = max(proportion.x, proportion.y);

                fraction = clamp(fraction, 0, 1);
                fineLevelIndex = clamp(fineLevelIndex, 0, _ClipmapStackLevelCount);
                coarseLevelIndex = clamp(coarseLevelIndex, 0, _ClipmapStackLevelCount);
            }

            float4 SampleClipmap(float2 uv, int depth)
            {
                if (depth >= _ClipmapStackLevelCount)
                {
                    return _ClipmapPyramid.Sample(sampler_ClipmapStack, uv);
                }
                else
                {
                    GetClipmapUV(uv, depth);
                    return _ClipmapStack.SampleLevel(sampler_ClipmapStack, float3(uv, depth), 0);
                }
            }

            // ========== Shader Stage =============================================================================
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vPos);
                o.uv = 1.0 - v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 retCol;
                int mipLevelCoarse = 0;
                int mipLevelFine = 0;
                float mipFract = 0;
                GetClipmapStackLevels(i.uv, mipLevelCoarse, mipLevelFine, mipFract);

                float4 col1 = SampleClipmap(i.uv, mipLevelFine);
                float4 col2 = SampleClipmap(i.uv, mipLevelCoarse);

                retCol = lerp(col1, col2, mipFract);

                float3 transitionRegionOverlayColors[8] = {
                    {1, 0, 0}, {0, 1, 0}, {0, 0, 1}, {1, 1, 0}, {1, 0, 0}, {0, 1, 0}, {0, 0, 1}, {1, 1, 1}
                };
                retCol = lerp(
                    retCol, float4(transitionRegionOverlayColors[mipLevelFine].rgb * _EnableTransitionRegionOverlay, 1),
                    mipFract * _EnableTransitionRegionOverlay);
                // retCol = float4(transitionRegionOverlayColors[mipLevelFine], 1);
                return retCol;
            }
            ENDHLSL
        }
    }
}