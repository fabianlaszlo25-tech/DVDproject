Shader "Custom/NicotineScreenShader"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "white" {}
        _EchoTex ("Subtle Echo Frame", 2D) = "black" {}
        _Intensity ("Overall Intensity", Range(0, 1)) = 0
        _BuzzSwimAmplitude ("Subtle Swim Amplitude (Alcohol Half)", Float) = 0.0055
        _BuzzSwimSpeed ("Swim Speed", Float) = 1.8
        _MicroTremor ("Subtle Micro-Jitter (Caffeine Half)", Float) = 0.0012
        _PulseSpeed ("Head-Rush Pulse Speed", Float) = 7.5
        _FocusContrast ("Crisp Focus Contrast", Float) = 1.12
        _ChromaticSplit ("Subtle RGB Split", Float) = 0.003
        _EchoOffset ("Motion Echo Offset XY & Weight Z", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Overlay+103" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "NicotineFullscreenOverlay"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _EchoTex;

            float _Intensity;
            float _BuzzSwimAmplitude;
            float _BuzzSwimSpeed;
            float _MicroTremor;
            float _PulseSpeed;
            float _FocusContrast;
            float _ChromaticSplit;
            float4 _EchoOffset;

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

            float HashNoise(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            float4 frag (v2f i) : SV_Target
            {
                if (_Intensity <= 0.001)
                {
                    return float4(0, 0, 0, 0);
                }

                float t = _Time.y * _BuzzSwimSpeed;
                float2 centered = i.uv - 0.5;
                float dist = length(centered);

                // 1. Subtle Caffeine-like head-rush pulse & micro-tremor
                float pulse = pow(max(0.0, sin(_Time.y * _PulseSpeed)), 4.0) * _Intensity;
                float jitterStep = floor(_Time.y * 42.0);
                float2 jitter = float2(
                    HashNoise(float2(jitterStep, i.uv.y * 6.0)) * 2.0 - 1.0,
                    HashNoise(float2(i.uv.x * 6.0, jitterStep + 11.7)) * 2.0 - 1.0
                ) * (_MicroTremor * _Intensity * (0.3 + dist));

                // 2. Subtle Alcohol-like lightheaded swimming wave
                float swimX = sin(i.uv.y * 3.5 + t) * (_BuzzSwimAmplitude * _Intensity * (0.4 + dist));
                float swimY = cos(i.uv.x * 3.5 + t * 0.9) * (_BuzzSwimAmplitude * _Intensity * (0.4 + dist));

                float zoomSafe = 1.0 - (_BuzzSwimAmplitude * 2.0 * _Intensity) - (0.008 * pulse);
                float2 buzzUV = centered * zoomSafe + 0.5 + float2(swimX, swimY) + jitter;

                // 3. Subtle Chromatic edge split
                float2 chroma = normalize(centered + 0.0001) * (dist * _ChromaticSplit * _Intensity);
                float r = tex2D(_MainTex, clamp(buzzUV + chroma, 0.002, 0.998)).r;
                float g = tex2D(_MainTex, clamp(buzzUV, 0.002, 0.998)).g;
                float b = tex2D(_MainTex, clamp(buzzUV - chroma, 0.002, 0.998)).b;
                float3 baseCol = float3(r, g, b);

                // 4. Center sharpening (Caffeine focus) + Peripheral softness (Alcohol haze)
                float2 texel = _MainTex_TexelSize.xy * 1.35;
                float3 surround = (
                    tex2D(_MainTex, clamp(buzzUV + float2(texel.x, 0), 0.002, 0.998)).rgb +
                    tex2D(_MainTex, clamp(buzzUV - float2(texel.x, 0), 0.002, 0.998)).rgb +
                    tex2D(_MainTex, clamp(buzzUV + float2(0, texel.y), 0.002, 0.998)).rgb +
                    tex2D(_MainTex, clamp(buzzUV - float2(0, texel.y), 0.002, 0.998)).rgb
                ) * 0.25;

                // Sharpen in center, soften at edges
                float centerMask = 1.0 - smoothstep(0.1, 0.5, dist);
                float3 sharpened = baseCol + (baseCol - surround) * (0.55 * _Intensity);
                float3 peripheralSoft = lerp(baseCol, surround, 0.65 * _Intensity);
                float3 hybridCol = lerp(peripheralSoft, sharpened, centerMask);

                // 5. Subtle 2-tap motion echo when moving camera
                float2 echoUV = clamp(buzzUV + _EchoOffset.xy * _Intensity, 0.002, 0.998);
                float3 echoCol = tex2D(_EchoTex, echoUV).rgb;
                float echoWeight = saturate(_EchoOffset.z * 0.45 * _Intensity);
                hybridCol = lerp(hybridCol, echoCol, echoWeight);

                // 6. Crisp smoky contrast & subtle cool/amber vignette
                hybridCol = saturate((hybridCol - 0.5) * lerp(1.0, _FocusContrast, _Intensity) + 0.5);
                float vignette = smoothstep(0.32, 0.82, dist);
                hybridCol *= 1.0 - (vignette * (0.22 + 0.08 * pulse) * _Intensity);

                return float4(hybridCol, saturate(_Intensity * 1.2));
            }
            ENDCG
        }
    }
}