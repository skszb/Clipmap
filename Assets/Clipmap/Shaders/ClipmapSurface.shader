Shader "Unlit/ClipmapSurface"
{

    Properties
    {
        _ClipmapStack ("Clipmap Stack", 2DArray) = "white" {}

        _ClipmapPyramid ("Clipmap Pyramid", 2D) = "white" {}
        
        _BaseMapSize("Mip0 Texture Size", Integer) = 4096
        
        _ClipSize("ClipSize", Integer) = 128
        
        _InvalidBorder("Invalid Border", Integer) = 4

        _WorldGridSize("World Grid Size", float) = 10.0
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
                SamplerState sampler_ClipmapStack;
                int _ClipSize;
                int _BaseMapSize;
                int _InvalidBorder;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vPos);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 dx = ddx(i.uv);
                float2 dy = ddy(i.uv);
                float mipLevel = 0.5 * max(dot(dx, dx), dot(dy, dy)) * _BaseMapSize;
                mipLevel = log2(mipLevel);
                
                int mipLevelCoarse = floor(mipLevel);
                int mipLevelFine = mipLevelCoarse + 1;
                float mipFract = frac(mipLevel);

                float4 col1 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(i.uv, mipLevelCoarse));
                float4 col2 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(i.uv, mipLevelFine));
                return lerp(col1, col2, mipFract);
            }
            ENDHLSL
        }
    }
}z
