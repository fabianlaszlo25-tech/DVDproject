Shader "Hidden/Custom/URP_CRT"
{
    Properties
    {
        [Header(Screen Distortion and Color Vignette)]
        _Curvature ("Curvature", Range(0, 0.5)) = 0.15
        _Vignette ("Vignette Multiplier", Range(0, 50)) = 15.0
        _VignettePower ("Vignette Power", Range(0, 1)) = 0.25
        
        [Header(Scanline Vignette)]
        _ScanlineCount ("Scanline Count", Float) = 800.0
        _ScanlineIntensity ("Max Edge Intensity", Range(0, 1)) = 0.3
        _ScanlineCenterAlpha ("Center Opacity (Multiplier)", Range(0, 1)) = 0.0
        _ScanlineVignettePower ("Vignette Falloff", Range(0.1, 10)) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        
        // ZTest Always ensures the camera doesn't cull the effect behind 3D objects
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "CRT Pass"

            HLSLPROGRAM
            #pragma vertex FullScreenVert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);

            float _Curvature;
            float _Vignette;
            float _VignettePower;
            
            float _ScanlineCount;
            float _ScanlineIntensity;
            float _ScanlineCenterAlpha;
            float _ScanlineVignettePower;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings FullScreenVert(Attributes input)
            {
                Varyings output;
                // Generates a perfect full-screen triangle without relying on external macros
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // Barrel Distortion
                float2 centeredUV = uv * 2.0 - 1.0;
                float r2 = dot(centeredUV, centeredUV);
                centeredUV *= 1.0 + _Curvature * r2;
                uv = centeredUV * 0.5 + 0.5;

                // Out of bounds check (Masking edges)
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return float4(0, 0, 0, 1);

                // Sample screen using the built-in URP sampler
                float4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Standard Color Vignette
                float2 vignetteUV = uv * (1.0 - uv.yx);
                float vignette = vignetteUV.x * vignetteUV.y * _Vignette;
                vignette = saturate(pow(vignette, _VignettePower));
                col.rgb *= vignette;

                // --- NEW: Scanline Vignette ---
                // 1. Calculate distance from center (0 at center, reaches 1 near edges)
                float distFromCenter = distance(uv, float2(0.5, 0.5)) * 2.0;
                
                // 2. Apply falloff curve
                float scanVignetteMask = saturate(pow(distFromCenter, _ScanlineVignettePower));
                
                // 3. Blend between center opacity and full edge intensity
                float currentScanlineStrength = lerp(_ScanlineCenterAlpha, 1.0, scanVignetteMask) * _ScanlineIntensity;

                // Apply Scanlines using the dynamic strength
                float scanline = sin(uv.y * _ScanlineCount * PI);
                scanline = (scanline * 0.5 + 0.5) * currentScanlineStrength;
                col.rgb -= scanline;

                return col;
            }
            ENDHLSL
        }
    }
}