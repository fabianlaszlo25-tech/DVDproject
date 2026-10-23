Shader "Custom/DepressionBreakdownShader"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "white" {}
        _EchoTex1 ("Echo Frame 1", 2D) = "black" {}
        _EchoTex2 ("Echo Frame 2", 2D) = "black" {}
        _Intensity ("Ramp Intensity (0..1)", Range(0, 1)) = 0
        _BlackCut ("Hard Cut To Black (0 or 1)", Range(0, 1)) = 0
        _PulsePhase ("Accelerating Heartbeat Phase", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Overlay+300" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "DepressionBreakdownOverlay"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _EchoTex1;
            sampler2D _EchoTex2;

            float _Intensity;
            float _BlackCut;
            float _PulsePhase;

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

            float Hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float SmoothNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Hash21(i), Hash21(i + float2(1.0, 0.0)), u.x),
                    lerp(Hash21(i + float2(0.0, 1.0)), Hash21(i + float2(1.0, 1.0)), u.x),
                    u.y);
            }

            float3 SampleRadialZoomBlur(sampler2D tex, float2 uv, float radialStrength, float chromaStrength)
            {
                float2 dir = uv - 0.5;
                float3 accum = float3(0, 0, 0);
                const int taps = 5;
                for (int s = 0; s < taps; s++)
                {
                    float scale = 1.0 - radialStrength * ((float)s / (float)(taps - 1));
                    float2 sampleUV = clamp(0.5 + dir * scale, 0.002, 0.998);
                    float2 chroma = dir * (chromaStrength * (1.0 + s * 0.25));

                    float r = tex2D(tex, clamp(sampleUV + chroma, 0.002, 0.998)).r;
                    float g = tex2D(tex, sampleUV).g;
                    float b = tex2D(tex, clamp(sampleUV - chroma, 0.002, 0.998)).b;
                    accum += float3(r, g, b);
                }
                return accum / (float)taps;
            }

            float4 frag (v2f i) : SV_Target
            {
                if (_BlackCut >= 0.999)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                if (_Intensity <= 0.001 && _BlackCut <= 0.001)
                {
                    return float4(0, 0, 0, 0);
                }

                float2 uv = i.uv;
                float2 centered = uv - 0.5;
                float dist = length(centered);
                float t = _Time.y;

                // 1. Accelerating systolic/diastolic cardiac slam driven by _PulsePhase
                float cycle = frac(_PulsePhase);
                float systole = exp(-pow((cycle - 0.12) * 18.0, 2.0));
                float diastole = exp(-pow((cycle - 0.28) * 14.0, 2.0)) * 0.78;
                float tremor = sin(t * 44.0) * exp(-pow((cycle - 0.62) * 8.0, 2.0)) * 0.35;
                float cardiacSlam = saturate(systole + diastole + max(0.0, tremor)) * _Intensity;

                // 2. Heavy sinking sway + violent heartbeat jolt
                float2 sway = float2(sin(t * 2.2), cos(t * 1.8) - 0.35 * _Intensity) * (0.016 * _Intensity);
                float2 jolt = float2(
                    (Hash21(float2(floor(t * 50.0), 3.7)) - 0.5) * 0.018,
                    -cardiacSlam * 0.032
                ) * cardiacSlam;

                float zoom = 1.0 - (0.085 * cardiacSlam) - (0.035 * _Intensity);
                float2 heartUV = clamp(centered * zoom + 0.5 + sway + jolt, 0.002, 0.998);

                // 3. Multi-tap radial zoom blur + 3-frame ghost splitting on every heartbeat
                float3 slammedFrame = SampleRadialZoomBlur(
                    _MainTex,
                    heartUV,
                    (0.02 + 0.095 * cardiacSlam) * _Intensity,
                    (0.006 + 0.022 * cardiacSlam) * _Intensity
                );

                float split = (0.012 + 0.018 * cardiacSlam) * _Intensity;
                float3 echo1 = tex2D(_EchoTex1, clamp(heartUV + float2(split, -split * 0.5), 0.002, 0.998)).rgb;
                float3 echo2 = tex2D(_EchoTex2, clamp(heartUV - float2(split, split * 0.5), 0.002, 0.998)).rgb;

                float3 finalRGB = lerp(slammedFrame, (echo1 * 0.55 + echo2 * 0.45), saturate(0.35 + 0.55 * cardiacSlam) * _Intensity);

                // 4. Cold, hopeless greyscale drain + stark contrast
                float luma = dot(finalRGB, float3(0.299, 0.587, 0.114));
                float3 drained = float3(luma, luma, luma) * float3(0.88, 0.92, 1.0);
                drained = saturate((drained - 0.5) * (1.0 + 0.45 * _Intensity) + 0.5);
                finalRGB = lerp(finalRGB, drained, saturate(0.65 + 0.35 * _Intensity) * _Intensity);

                // 5. Crushing dark crimson & pitch-black tunnel vision closing in as _Intensity approaches 1
                float tunnelRadius = lerp(0.65, 0.14, pow(_Intensity, 1.25)) - cardiacSlam * 0.08;
                float veinNoise = SmoothNoise(centered * 9.0 + float2(t * 0.5, -t * 0.5)) * 0.12;
                float tunnelMask = smoothstep(max(0.02, tunnelRadius * 0.35), max(0.08, tunnelRadius + veinNoise), dist);

                float3 darkVeinColor = lerp(float3(0.32, 0.0, 0.01) * (1.0 - _Intensity * 0.6), float3(0.0, 0.0, 0.0), smoothstep(0.25, 0.65, dist));
                finalRGB = lerp(finalRGB, darkVeinColor, tunnelMask * saturate(0.75 + 0.25 * cardiacSlam) * _Intensity);

                // Apply hard cut to black
                finalRGB = lerp(finalRGB, float3(0.0, 0.0, 0.0), saturate(_BlackCut));

                float finalAlpha = max(saturate(_Intensity * 1.25), saturate(_BlackCut));
                return float4(saturate(finalRGB), finalAlpha);
            }
            ENDCG
        }
    }
}