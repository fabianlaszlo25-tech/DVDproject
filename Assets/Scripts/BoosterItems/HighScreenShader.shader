Shader "Custom/HighScreenShader"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "white" {}
        _EchoTex ("Dream Trail Frame", 2D) = "black" {}
        _Intensity ("Overall Intensity", Range(0, 1)) = 0
        _BreathSpeed ("Breathing Wave Speed", Float) = 1.1
        _WaveAmplitude ("Dreamy Wave Amplitude", Float) = 0.009
        _HueShiftSpeed ("Rainbow Hue Shift Speed", Float) = 0.35
        _HueShiftStrength ("Psychedelic Color Strength", Range(0, 1)) = 0.55
        _DreamBloom ("Soft Dreamy Glow", Float) = 0.0055
        _WarmVignetteColor ("Warm Vignette Tint", Color) = (0.95, 0.35, 0.85, 1.0)
        _TrailBlend ("Slow Motion Trail Blend", Range(0, 1)) = 0.42
    }

    SubShader
    {
        Tags { "Queue"="Overlay+102" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "HighFullscreenOverlay"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _EchoTex;

            float _Intensity;
            float _BreathSpeed;
            float _WaveAmplitude;
            float _HueShiftSpeed;
            float _HueShiftStrength;
            float _DreamBloom;
            float4 _WarmVignetteColor;
            float _TrailBlend;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Fast RGB Hue rotation using Rodrigues' rotation formula around (1,1,1)
            float3 ShiftHue(float3 col, float hueAngle)
            {
                float3 k = float3(0.57735, 0.57735, 0.57735);
                float cosAngle = cos(hueAngle);
                return saturate(col * cosAngle + cross(k, col) * sin(hueAngle) + k * dot(k, col) * (1.0 - cosAngle));
            }

            float3 SampleDreamyGlow(sampler2D tex, float2 uv, float radius)
            {
                float2 cUV = clamp(uv, 0.002, 0.998);
                float3 center = tex2D(tex, cUV).rgb;

                float2 d1 = float2(radius, radius * 0.7);
                float2 d2 = float2(-radius, radius * 0.7);

                float3 s1 = tex2D(tex, clamp(cUV + d1, 0.002, 0.998)).rgb;
                float3 s2 = tex2D(tex, clamp(cUV - d1, 0.002, 0.998)).rgb;
                float3 s3 = tex2D(tex, clamp(cUV + d2, 0.002, 0.998)).rgb;
                float3 s4 = tex2D(tex, clamp(cUV - d2, 0.002, 0.998)).rgb;

                float3 soft = (s1 + s2 + s3 + s4) * 0.25;
                // Boost highlights for a hazy euphoric bloom
                float3 highlights = max(0.0, soft - 0.35) * 1.45;
                return lerp(center, soft, 0.65) + highlights * 0.45;
            }

            float4 frag (v2f i) : SV_Target
            {
                if (_Intensity <= 0.001)
                {
                    return float4(0, 0, 0, 0);
                }

                float t = _Time.y * _BreathSpeed;
                float2 centered = i.uv - 0.5;
                float dist = length(centered);

                // 1. Slow, symmetric "breathing walls" expansion & ripple
                float breath = sin(t) * 0.018 * _Intensity;
                float rippleX = sin(i.uv.y * 5.0 + t * 1.2) * cos(i.uv.x * 4.0 - t * 0.8);
                float rippleY = cos(i.uv.x * 5.0 - t * 1.1) * sin(i.uv.y * 4.0 + t * 0.9);

                float safeScale = 1.0 - (_WaveAmplitude * 2.2 * _Intensity) - abs(breath);
                float2 dreamUV = centered * (safeScale + breath * dist) + 0.5
                               + float2(rippleX, rippleY) * (_WaveAmplitude * _Intensity);

                float glowRad = _DreamBloom * _Intensity;

                // 2. Sample current frame + slow dreamy afterimage trail
                float3 mainCol = SampleDreamyGlow(_MainTex, dreamUV, glowRad);
                float3 echoCol = SampleDreamyGlow(_EchoTex, dreamUV + float2(rippleY, -rippleX) * (0.004 * _Intensity), glowRad * 1.5);

                float3 blended = lerp(mainCol, echoCol, _TrailBlend * _Intensity);

                // 3. Psychedelic spatially-varying rainbow hue shift (stronger toward periphery)
                float hueWave = (_Time.y * _HueShiftSpeed + dist * 1.6 + sin(i.uv.x * 3.0 + t) * 0.25) * 6.28318;
                float3 trippyCol = ShiftHue(blended, hueWave);

                float hueWeight = _HueShiftStrength * _Intensity * smoothstep(0.05, 0.65, dist + 0.2);
                float3 finalRGB = lerp(blended, trippyCol, hueWeight);

                // 4. Warm, lifted saturation & mellow magenta/gold peripheral haze
                float luma = dot(finalRGB, float3(0.299, 0.587, 0.114));
                finalRGB = lerp(float3(luma, luma, luma), finalRGB, 1.0 + 0.45 * _Intensity);

                float vignette = smoothstep(0.22, 0.78, dist);
                float3 tintPulse = ShiftHue(_WarmVignetteColor.rgb, _Time.y * 0.4);
                finalRGB = lerp(finalRGB, finalRGB * (0.85 + tintPulse * 0.55), vignette * 0.55 * _Intensity);

                return float4(saturate(finalRGB), saturate(_Intensity * 1.2));
            }
            ENDCG
        }
    }
}