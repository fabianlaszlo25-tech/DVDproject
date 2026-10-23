Shader "Custom/DrunkScreenShader"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "white" {}
        _EchoTex1 ("Echo Frame 1 (Recent)", 2D) = "black" {}
        _EchoTex2 ("Echo Frame 2 (Middle)", 2D) = "black" {}
        _EchoTex3 ("Echo Frame 3 (Oldest)", 2D) = "black" {}

        _Intensity ("Overall Intensity", Range(0, 1)) = 0
        _WobbleAmplitude ("Wobble Amplitude", Float) = 0.012
        _WobbleFrequency ("Wobble Frequency", Float) = 4.0
        _WobbleSpeed ("Wobble Speed", Float) = 2.2
        _BlurAmount ("Blur Amount", Float) = 0.0035
        _ChromaticSplit ("Chromatic Aberration", Float) = 0.0045
        _TripleEchoStrength ("Triple Vision Echo Strength", Range(0, 1)) = 0.85

        _GhostOffset1 ("Ghost Offset 1", Vector) = (0, 0, 0, 0)
        _GhostOffset2 ("Ghost Offset 2", Vector) = (0, 0, 0, 0)
        _GhostOffset3 ("Ghost Offset 3", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Overlay+100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "DrunkFullscreenOverlay"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _EchoTex1;
            sampler2D _EchoTex2;
            sampler2D _EchoTex3;

            float _Intensity;
            float _WobbleAmplitude;
            float _WobbleFrequency;
            float _WobbleSpeed;
            float _BlurAmount;
            float _ChromaticSplit;
            float _TripleEchoStrength;

            float4 _GhostOffset1;
            float4 _GhostOffset2;
            float4 _GhostOffset3;

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

            float3 SampleBlurred(sampler2D tex, float2 uv, float blurRadius, float chromaOffset)
            {
                float2 cUV = clamp(uv, 0.002, 0.998);
                float2 offX = float2(blurRadius, 0.0);
                float2 offY = float2(0.0, blurRadius);
                float2 chroma = float2(chromaOffset, chromaOffset * 0.5);

                float3 center = tex2D(tex, cUV).rgb;
                float3 t1 = tex2D(tex, clamp(cUV + offX, 0.002, 0.998)).rgb;
                float3 t2 = tex2D(tex, clamp(cUV - offX, 0.002, 0.998)).rgb;
                float3 t3 = tex2D(tex, clamp(cUV + offY, 0.002, 0.998)).rgb;
                float3 t4 = tex2D(tex, clamp(cUV - offY, 0.002, 0.998)).rgb;

                float3 blurred = (center * 2.0 + t1 + t2 + t3 + t4) / 6.0;

                float r = tex2D(tex, clamp(cUV + chroma, 0.002, 0.998)).r;
                float b = tex2D(tex, clamp(cUV - chroma, 0.002, 0.998)).b;

                return float3(lerp(blurred.r, r, 0.5), blurred.g, lerp(blurred.b, b, 0.5));
            }

            float4 frag (v2f i) : SV_Target
            {
                if (_Intensity <= 0.001)
                {
                    return float4(0, 0, 0, 0);
                }

                float t = _Time.y * _WobbleSpeed;

                float2 centered = i.uv - 0.5;
                float edgeFactor = dot(centered, centered) * 1.8 + 0.4;

                float waveX = sin(i.uv.y * _WobbleFrequency + t) + cos(i.uv.x * (_WobbleFrequency * 0.8) - t * 0.9);
                float waveY = cos(i.uv.x * _WobbleFrequency + t * 1.15) + sin(i.uv.y * (_WobbleFrequency * 0.85) + t * 0.7);

                float zoomSafe = 1.0 - (_WobbleAmplitude * 2.4 * _Intensity);
                float2 warpedUV = (centered * zoomSafe) + 0.5 + float2(waveX, waveY) * (_WobbleAmplitude * 0.55 * _Intensity * edgeFactor);

                float blur = _BlurAmount * _Intensity;
                float chroma = _ChromaticSplit * _Intensity;

                float3 currentFrame = SampleBlurred(_MainTex, warpedUV, blur, chroma);

                float2 uvEcho1 = warpedUV + _GhostOffset1.xy * _Intensity;
                float2 uvEcho2 = warpedUV + _GhostOffset2.xy * _Intensity;
                float2 uvEcho3 = warpedUV + _GhostOffset3.xy * _Intensity;

                float3 echo1 = SampleBlurred(_EchoTex1, uvEcho1, blur * 1.4, chroma * 1.3);
                float3 echo2 = SampleBlurred(_EchoTex2, uvEcho2, blur * 1.9, chroma * 1.7);
                float3 echo3 = SampleBlurred(_EchoTex3, uvEcho3, blur * 2.5, chroma * 2.1);

                float motionWeight = saturate(_GhostOffset1.z * _TripleEchoStrength * _Intensity);
                float3 tripleEchoVision = echo1 * 0.45 + echo2 * 0.33 + echo3 * 0.22;

                float2 idleDrift = float2(cos(t * 1.4), sin(t * 1.0)) * (0.005 * _Intensity);
                float3 idleDouble = SampleBlurred(_MainTex, warpedUV + idleDrift, blur * 1.5, chroma * 1.2);

                float3 finalRGB = lerp(currentFrame, idleDouble, 0.32 * _Intensity);
                finalRGB = lerp(finalRGB, tripleEchoVision, motionWeight);

                return float4(finalRGB, saturate(_Intensity * 1.2));
            }
            ENDCG
        }
    }
}