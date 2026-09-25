Shader "Hidden/OverscanCrop"
{
    Properties
    {
        _ZoomFactor ("Zoom Factor", Range(0.5, 1.0)) = 0.95
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "OverscanCrop"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // URP automatically pushes the screen image into _BlitTexture during Full Screen passes
            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            // CBUFFER is required in URP for properties to show in the Inspector and work with SRP
            CBUFFER_START(UnityPerMaterial)
                float _ZoomFactor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Generate a full-screen triangle mathematically (no physical mesh required)
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Remap UVs to zoom in toward the center of the screen
                float2 uv = (input.uv - 0.5) * _ZoomFactor + 0.5;
                
                // Output the zoomed image
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
            }
            ENDHLSL
        }
    }
}