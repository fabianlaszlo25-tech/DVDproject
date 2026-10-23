Shader "Custom/AdjustableACESShader"
{
    Properties
    {
        _MainTex ("Screen Frame", 2D) = "white" {}
        _ACESAmount ("ACES Tonemapping Amount", Range(0, 1)) = 1.0
        _PostExposure ("Post Exposure (EV)", Float) = 0.0
        _Contrast ("Extra Contrast", Float) = 1.0
        _Saturation ("Extra Saturation", Float) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Overlay+200" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "AdjustableACESTonemapping"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _ACESAmount;
            float _PostExposure;
            float _Contrast;
            float _Saturation;

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

            // Exact Stephen Hill / Unity URP ACES Input Matrix (sRGB/Rec.709 -> ACES AP0/AP1)
            static const float3x3 ACESInputMat =
            {
                { 0.59719, 0.35458, 0.04823 },
                { 0.07600, 0.90834, 0.01566 },
                { 0.02840, 0.13383, 0.83777 }
            };

            // Exact Stephen Hill / Unity URP ACES Output Matrix (ODT -> sRGB/Rec.709)
            static const float3x3 ACESOutputMat =
            {
                {  1.60475, -0.53108, -0.07367 },
                { -0.10208,  1.10813, -0.00605 },
                { -0.00327, -0.07276,  1.07602 }
            };

            float3 RRTAndODTFit(float3 v)
            {
                float3 a = v * (v + 0.0245786f) - 0.000090537f;
                float3 b = v * (0.983729f * v + 0.4329510f) + 0.238081f;
                return a / b;
            }

            float3 UnityACESFitted(float3 colorLinear)
            {
                colorLinear = mul(ACESInputMat, colorLinear);
                colorLinear = RRTAndODTFit(colorLinear);
                colorLinear = mul(ACESOutputMat, colorLinear);
                return saturate(colorLinear);
            }

            float3 SRGBToLinear(float3 c)
            {
                return pow(max(0.0, c), 2.2);
            }

            float3 LinearToSRGB(float3 c)
            {
                return pow(max(0.0, c), 1.0 / 2.2);
            }

            float4 frag (v2f i) : SV_Target
            {
                if (_ACESAmount <= 0.0001)
                {
                    return float4(0, 0, 0, 0);
                }

                float3 rawSample = tex2D(_MainTex, i.uv).rgb;

                // Convert to linear space if project is in Gamma, or work directly in Linear space
                #if defined(UNITY_COLORSPACE_GAMMA)
                    float3 linearCol = SRGBToLinear(rawSample);
                #else
                    float3 linearCol = rawSample;
                #endif

                // Apply pre-tonemap exposure (in stops/EV, just like URP Color Adjustments)
                float exposureMult = exp2(_PostExposure);
                float3 exposedLinear = linearCol * exposureMult;

                // Run full Unity ACES Filmic RRT+ODT curve
                float3 acesLinear = UnityACESFitted(exposedLinear);

                // Optional saturation & contrast fine-tuning on the ACES result
                float luma = dot(acesLinear, float3(0.2126, 0.7152, 0.0722));
                acesLinear = lerp(float3(luma, luma, luma), acesLinear, _Saturation);
                acesLinear = saturate((acesLinear - 0.5) * _Contrast + 0.5);

                // Smoothly blend between ungraded original and full ACES tonemapped color!
                float3 blendedLinear = lerp(linearCol, acesLinear, saturate(_ACESAmount));

                #if defined(UNITY_COLORSPACE_GAMMA)
                    float3 finalOut = LinearToSRGB(blendedLinear);
                #else
                    float3 finalOut = blendedLinear;
                #endif

                return float4(saturate(finalOut), saturate(_ACESAmount));
            }
            ENDCG
        }
    }
}