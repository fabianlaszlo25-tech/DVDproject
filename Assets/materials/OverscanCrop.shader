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
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _ZoomFactor;

            half4 Frag(Varyings input) : SV_Target
            {
                // Remap UVs to zoom in toward the center of the screen
                float2 uv = input.texcoord;
                uv = (uv - 0.5) * _ZoomFactor + 0.5;

                // Sample the screen 
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            }
            ENDHLSL
        }
    }
}