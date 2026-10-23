// Additive glow shader used by InteractVisualizer.
// Works on both Opaque and Transparent meshes by allowing the Fill pass (Queue Transparent+10)
// to write to BOTH the Depth buffer (_ZWrite = 1) and Stencil buffer (Ref 173) after the
// transparent object (Queue 3000) has already rendered, physically and logically blocking
// the back-facing Outline pass (Queue Transparent+11) from ever drawing inside the silhouette.
Shader "Custom/TrashOutline"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.8, 0.2, 1)
        _Thickness ("Thickness (world units)", Float) = 0.03
        _ThicknessScale ("Thickness Scale", Float) = 1
        _Alpha ("Alpha", Range(0, 1)) = 0
        _Brightness ("Brightness", Float) = 1
        _RadialBlend ("Radial Blend", Range(0, 1)) = 0.5
        _Center ("Local Center", Vector) = (0, 0, 0, 0)

        _SwirlAmount ("Swirl Amount", Range(0, 1)) = 0
        _SwirlPhase ("Swirl Phase", Float) = 0
        _SwirlBrightness ("Swirl Peak Brightness", Float) = 2.2
        _SwirlThicknessMultiplier ("Swirl Thickness Multiplier", Float) = 1.6
        _SwirlBands ("Swirl Bands", Float) = 2
        _SwirlTwist ("Swirl Vertical Twist", Float) = 8

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0

        _StencilRef ("Stencil Ref", Float) = 173
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comp", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil Pass", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Name "Glow"
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest LEqual
            Offset [_OffsetFactor], [_OffsetUnits]
            Blend SrcAlpha One

            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass [_StencilPass]
                ZFail Keep
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Thickness;
            float _ThicknessScale;
            float _Alpha;
            float _Brightness;
            float _RadialBlend;
            float4 _Center;

            float _SwirlAmount;
            float _SwirlPhase;
            float _SwirlBrightness;
            float _SwirlThicknessMultiplier;
            float _SwirlBands;
            float _SwirlTwist;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 relWorld : TEXCOORD0;
            };

            float ComputeSwirlWave(float3 relWorld)
            {
                float angle = atan2(relWorld.z, relWorld.x);
                float raw = 0.5 + 0.5 * sin(angle * _SwirlBands + relWorld.y * _SwirlTwist - _SwirlPhase);
                return smoothstep(0.1, 0.9, raw);
            }

            v2f vert (appdata v)
            {
                v2f o;

                float3 rel = v.vertex.xyz - _Center.xyz;
                float3 radial = normalize(rel + 0.00001);

                // Mix surface normal with "away from center" so hard edges don't open gaps.
                float3 dirObj = normalize(lerp(v.normal, radial, _RadialBlend));

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 dirWorld = normalize(mul((float3x3)unity_ObjectToWorld, dirObj));
                float3 relWorld = mul((float3x3)unity_ObjectToWorld, rel);

                float swirl = ComputeSwirlWave(relWorld);
                float thicknessMult = lerp(1.0, lerp(1.0, _SwirlThicknessMultiplier, swirl), _SwirlAmount);

                worldPos += dirWorld * (_Thickness * _ThicknessScale * thicknessMult);

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.relWorld = relWorld;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float swirl = ComputeSwirlWave(i.relWorld);
                float brightness = lerp(_Brightness, lerp(_Brightness, _SwirlBrightness, swirl), _SwirlAmount);
                float swirlAlpha = lerp(1.0, lerp(0.25, 1.0, swirl), _SwirlAmount);

                float4 col = _Color;
                col.rgb *= brightness;
                col.a = _Color.a * _Alpha * swirlAlpha;
                return col;
            }
            ENDCG
        }
    }
}