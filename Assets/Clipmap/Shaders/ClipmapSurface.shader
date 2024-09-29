Shader "Unlit/ClipmapSurface"
{
    Properties
    {
        _ClipmapStack ("Clipmap Stack", 2DArray) = "white" {}
        _ClipmapPyramid ("Clipmap Pyramid", 2D) = "white" {}
        
        _Mip0TextureSize("Base Texture Size", Integer) = 2048
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            SAMPLER(sampler_BaseMap);

            // To ensure that the Unity shader is SRP Batcher compatible, 
            // declare all Material properties inside
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                int _Mip0TextureSize;
            CBUFFER_END

            // data structure : vertex shader to pixel shader
            // also called interpolants because values interpolates through the triangle
            // from one vertex to another
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 color : TEXCOORD1;
            };

            // The vertex shader definition with properties defined in the Varyings
            // structure. The type of the vert function must match the type (struct)
            // that it returns.
            
            v2f vert (
                float4 vertex : POSITION, // vertex position input
                float2 uv : TEXCOORD0 // first texture coordinate input
                )
            {
                v2f o;
                o.pos = TransformObjectToHClip(vertex);
                o.uv = uv;
                return o;
            }

            Texture2DArray _ClipmapStack;
            SamplerState sampler_ClipmapStack;
            float4 frag (v2f i) : SV_Target
            {
                float mipLevel = max(abs(ddx(i.uv.x)), abs(ddy(i.uv.y))) * _Mip0TextureSize;
                mipLevel = log2(mipLevel);
                int mipLevelCoarse = floor(mipLevel);
                int mipLevelFine = mipLevelCoarse + 1;
                float mipFract = frac(mipLevel);

                // return float4(1,1,1,1);
                float4 col1 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(i.uv, mipLevelCoarse));
                float4 col2 = _ClipmapStack.Sample(sampler_ClipmapStack, float3(i.uv, mipLevelFine));
                return lerp(col1, col2, mipFract);
            }
            ENDHLSL
        }
    }
}
