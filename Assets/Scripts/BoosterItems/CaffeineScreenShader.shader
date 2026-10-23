Shader "Custom/CaffeineScreenShader"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "white" {}
        _Intensity ("Overall Intensity", Range(0, 1)) = 0
        _JitterAmount ("High-Freq Jitter Amount", Float) = 0.0028
        _JitterSpeed ("Jitter Speed", Float) = 55.0
        _HeartbeatSpeed ("Heartbeat Pulse Speed", Float) = 11.0
        _PulseZoom ("Heartbeat Zoom Strength", Float) = 0.022
        _ChromaticEdge ("Edge Chromatic Split", Float) = 0.008
        _ContrastBoost ("Hyper Focus Contrast", Float) = 1.22
        _SaturationBoost ("Hyper Saturation", Float) = 1.35
        _EdgeTint ("Electric Edge Tint", Color) = (0.15, 1.0, 0.55, 1.0)
        _CameraVelocity ("Camera Velocity XY & Speed Z", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Overlay+101" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "CaffeineFullscreenOverlay"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float _Intensity;
            float _JitterAmount;
            float _JitterSpeed;
            float _HeartbeatSpeed;
            float _PulseZoom;
            float _ChromaticEdge;
            float _ContrastBoost;
            float _SaturationBoost;
            float4 _EdgeTint;
            float4 _CameraVelocity;

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

                float2 centered = i.uv - 0.5;
                float distSq = dot(centered, centered);
                float dist = sqrt(distSq);

                float beatTime = _Time.y * _HeartbeatSpeed;
                float lub = pow(max(0.0, sin(beatTime)), 6.0);
                float dub = pow(max(0.0, sin(beatTime - 0.85)), 8.0) * 0.65;
                float heartbeat = (lub + dub) * _Intensity;

                float jitterTime = floor(_Time.y * _JitterSpeed);
                float2 tremor = float2(
                    HashNoise(float2(jitterTime, i.uv.y * 8.0)) * 2.0 - 1.0,
                    HashNoise(float2(i.uv.x * 8.0, jitterTime + 19.3)) * 2.0 - 1.0
                ) * (_JitterAmount * _Intensity * (0.4 + dist * 1.4));

                float zoomFactor = 1.0 - (_PulseZoom * (0.35 + heartbeat * 0.65)) - (_JitterAmount * 2.0 * _Intensity);
                float2 zoomedUV = centered * zoomFactor + 0.5 + tremor;

                float2 radialDir = normalize(centered + 0.0001) * distSq * 2.2;
                float2 streakDir = radialDir * (_ChromaticEdge * (1.0 + heartbeat * 0.8) * _Intensity)
                                 + _CameraVelocity.xy * (0.35 * _Intensity);

                float2 uvR = clamp(zoomedUV + streakDir, 0.002, 0.998);
                float2 uvG = clamp(zoomedUV, 0.002, 0.998);
                float2 uvB = clamp(zoomedUV - streakDir, 0.002, 0.998);

                float r = tex2D(_MainTex, uvR).r;
                float g = tex2D(_MainTex, uvG).g;
                float b = tex2D(_MainTex, uvB).b;
                float3 col = float3(r, g, b);

                float2 texel = _MainTex_TexelSize.xy * 1.5;
                float3 neighbors = (
                    tex2D(_MainTex, clamp(uvG + float2(texel.x, 0), 0.002, 0.998)).rgb +
                    tex2D(_MainTex, clamp(uvG - float2(texel.x, 0), 0.002, 0.998)).rgb +
                    tex2D(_MainTex, clamp(uvG + float2(0, texel.y), 0.002, 0.998)).rgb +
                    tex2D(_MainTex, clamp(uvG - float2(0, texel.y), 0.002, 0.998)).rgb
                ) * 0.25;
                col += (col - neighbors) * (0.85 * _Intensity);

                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(float3(luma, luma, luma), col, lerp(1.0, _SaturationBoost, _Intensity));
                col = saturate((col - 0.5) * lerp(1.0, _ContrastBoost, _Intensity) + 0.5);

                float angle = atan2(centered.y, centered.x);
                float speedLines = sin(angle * 48.0 + jitterTime * 3.7) * 0.5 + 0.5;
                speedLines = pow(speedLines, 4.0) * smoothstep(0.28, 0.68, dist) * (0.35 + 0.65 * heartbeat);

                col += _EdgeTint.rgb * (speedLines * 0.32 * _Intensity);

                return float4(col, saturate(_Intensity * 1.2));
            }
            ENDCG
        }
    }
}