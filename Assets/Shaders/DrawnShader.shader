// ============================================================================
// DrawnShader.shader
// Seamless World-Space Triplanar Unity URP Painted Shader ("Custom/DrawnShader")
//
// Key Guarantees:
//   1. 100% World-Space Locked (Zero Camera-Distance / View-Angle Popping):
//      All brushstroke facets, specular dabs, and rim accents are computed in
//      pure 3D world space without depending on camera viewZ Z-bins, screen
//      tiles, or camera view angle, so the effect NEVER changes or shows a
//      separation line as you move closer or further!
//   2. Smooth Multi-Light Blending (Zero Room-Transition Seams):
//      Accumulates all scene lights via continuous weighted vector sums instead
//      of a winner-take-all light switch.
//   3. Seamless World-Space Triplanar Mapping across all objects and walls.
//   4. Single-pass, fast, and 100% color-preserving (Blend DstColor SrcColor).
// ============================================================================
Shader "Custom/DrawnShader"
{
    Properties
    {
        [Header(1. Overall Drawn Effect and WebGL Toggle)]
        [Toggle] _EffectEnabled ("Enable Drawn Shader Effect", Float) = 1.0
        _EffectIntensity ("Overall Drawn Shader Intensity", Range(0.0, 1.5)) = 0.5964

        [Header(2. Seamless World Triplanar Brush Mapping)]
        _Stroke3DScale ("World Triplanar Brush Scale", Float) = 0.72
        _TriplanarSharpness ("Triplanar Plane Blend Sharpness", Range(1.0, 12.0)) = 4.5
        _BrushAspect ("Directional Brush Stroke Elongation", Range(1.0, 3.0)) = 1.0
        _BrushFlowSweep ("Organic Wrist-Sweep Flow Curvature", Range(0.0, 1.0)) = 0.0
        _MappingScale ("Global Scale Multiplier (X, Y, Z)", Vector) = (1.0, 1.0, 1.0, 1.0)
        _MappingLocation ("Global 3D Offset (X, Y, Z)", Vector) = (0.0, 0.0, 0.0, 0.0)

        [Header(3. Blender Node Graph  3D fBM Noise and Linear Light)]
        _NoiseScale ("Noise Texture Scale (Blender: 5.0)", Float) = 4.8
        _NoiseDetail ("Noise Texture Detail (Blender: 2.0)", Range(0.0, 3.0)) = 2.0
        _NoiseRoughness ("Noise Texture Roughness (Blender: 0.5)", Range(0.0, 1.0)) = 1.0
        _NoiseLacunarity ("Noise Texture Lacunarity (Blender: 2.0)", Range(1.0, 4.0)) = 2.145
        _LinearLightFactor ("Linear Light Factor (Blender: 0.175)", Range(0.0, 0.5)) = 0.3018
        _FlatSurfaceSpread ("Flat Surface Macro Brush Spread", Range(0.0, 1.0)) = 0.42

        [Header(4. Blender Node Graph  Minkowski Voronoi Position to Normal)]
        _VoronoiScale ("Voronoi Scale (Blender: 8.8)", Float) = 7.4
        _VoronoiExponent ("Voronoi Minkowski Exponent (Blender: 0.5)", Range(0.3, 1.5)) = 1.5
        _VoronoiRandomness ("Voronoi Randomness (Blender: 1.0)", Range(0.0, 1.0)) = 1.0
        _PainterlyNormalStrength ("Voronoi Position -> Normal Tilt Strength", Range(0.0, 3.0)) = 1.798

        [Header(5. Painted Light Color Switches and Gouache Impasto)]
        _PaintedLightStrength ("Painted Light Color-Switch Contrast", Range(0.0, 2.0)) = 0.804
        _SmoothFlattenStrength ("Flatten Underlying Smooth Gradient Inside Strokes", Range(0.0, 1.0)) = 0.0
        _LightSteps ("Painted Light Color-Switch Steps", Range(2.0, 8.0)) = 4.0
        _LightStepSharpness ("Brushstroke Step Sharpness", Range(1.0, 25.0)) = 14.0
        _Roughness ("Principled BSDF Roughness (Blender: 0.5)", Range(0.15, 0.95)) = 0.48
        _PaintedSpecularStrength ("Principled BSDF Specular Dab Boost", Range(0.0, 1.5)) = 0.85
        _FresnelRimStrength ("Principled BSDF Fresnel Rim & Bounce Dab", Range(0.0, 1.5)) = 0.108
        _BrushOverlapImpasto ("Directional Brush-Edge Overlap Relief", Range(0.0, 0.5)) = 0.0
        _PerStrokeVariation ("Per-Brushstroke Value Switch", Range(0.0, 0.4)) = 0.18
        _WarmCoolShift ("Sunlit Warm / Shadow Cool Pigment Switch", Range(0.0, 0.45)) = 0.24
        _ShadowProtection ("Keep Deep Unlit Shadows Rich & Dark", Range(0.0, 1.0)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+5"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "DrawnShaderForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            // Full slope-scaled depth bias eliminates all Z-fighting flicker during camera movement,
            // while the 8-bit Stencil guard below prevents overlapping floor/wall slabs from double-blending.
            Offset -1, -2
            Blend DstColor SrcColor

            // Stencil guard ensures DrawnShader executes at most ONCE per pixel (requires Depth+Stencil attachment)
            Stencil
            {
                Ref 77
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vertDrawn
            #pragma fragment fragDrawn

            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float _DrawnShaderGlobalDisabled;

            CBUFFER_START(UnityPerMaterial)
                float _EffectEnabled;
                float _EffectIntensity;

                float _Stroke3DScale;
                float _TriplanarSharpness;
                float _BrushAspect;
                float _BrushFlowSweep;
                float4 _MappingScale;
                float4 _MappingLocation;

                float _NoiseScale;
                float _NoiseDetail;
                float _NoiseRoughness;
                float _NoiseLacunarity;
                float _LinearLightFactor;
                float _FlatSurfaceSpread;

                float _VoronoiScale;
                float _VoronoiExponent;
                float _VoronoiRandomness;
                float _PainterlyNormalStrength;

                float _PaintedLightStrength;
                float _SmoothFlattenStrength;
                float _LightSteps;
                float _LightStepSharpness;
                float _Roughness;
                float _PaintedSpecularStrength;
                float _FresnelRimStrength;
                float _BrushOverlapImpasto;
                float _PerStrokeVariation;
                float _WarmCoolShift;
                float _ShadowProtection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vertDrawn(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Match URP DepthOnlyPass.hlsl exactly (TransformObjectToHClip) for bit-consistent depth
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // ----------------------------------------------------------------
            // Fast, Artifact-Free 3D Hash & Quintic 3D Perlin Gradient Noise
            // ----------------------------------------------------------------
            float3 Hash33(float3 p3)
            {
                p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yxx) * p3.zyx);
            }

            float GradientNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

                float3 g000 = Hash33(i + float3(0, 0, 0)) * 2.0 - 1.0;
                float3 g100 = Hash33(i + float3(1, 0, 0)) * 2.0 - 1.0;
                float3 g010 = Hash33(i + float3(0, 1, 0)) * 2.0 - 1.0;
                float3 g110 = Hash33(i + float3(1, 1, 0)) * 2.0 - 1.0;
                float3 g001 = Hash33(i + float3(0, 0, 1)) * 2.0 - 1.0;
                float3 g101 = Hash33(i + float3(1, 0, 1)) * 2.0 - 1.0;
                float3 g011 = Hash33(i + float3(0, 1, 1)) * 2.0 - 1.0;
                float3 g111 = Hash33(i + float3(1, 1, 1)) * 2.0 - 1.0;

                float n000 = dot(g000, f - float3(0, 0, 0));
                float n100 = dot(g100, f - float3(1, 0, 0));
                float n010 = dot(g010, f - float3(0, 1, 0));
                float n110 = dot(g110, f - float3(1, 1, 0));
                float n001 = dot(g001, f - float3(0, 0, 1));
                float n101 = dot(g101, f - float3(1, 0, 1));
                float n011 = dot(g011, f - float3(0, 1, 1));
                float n111 = dot(g111, f - float3(1, 1, 1));

                float nx00 = lerp(n000, n100, u.x);
                float nx10 = lerp(n010, n110, u.x);
                float nx01 = lerp(n001, n101, u.x);
                float nx11 = lerp(n011, n111, u.x);

                float nxy0 = lerp(nx00, nx10, u.y);
                float nxy1 = lerp(nx01, nx11, u.y);

                return clamp(lerp(nxy0, nxy1, u.z) * 1.45, -1.0, 1.0);
            }

            // ----------------------------------------------------------------
            // Seamless World-Space Triplanar Brush Coordinate Generator
            // ----------------------------------------------------------------
            float3 GetSeamlessTriplanarCoords(float3 positionWS, float3 normalWS, out float3 outTriWeights)
            {
                float3 wp = (positionWS * _MappingScale.xyz + _MappingLocation.xyz) * _Stroke3DScale;

                float3 absN = max(abs(normalWS), float3(0.0001, 0.0001, 0.0001));
                float3 w = pow(absN, clamp(_TriplanarSharpness, 1.0, 12.0));
                outTriWeights = w / max(w.x + w.y + w.z, 0.0001);

                float sweepAngle = GradientNoise3D(wp * 0.35 + float3(11.7, 23.1, 37.9)) * 1.57 * _BrushFlowSweep;
                float cs = cos(sweepAngle);
                float sn = sin(sweepAngle);
                float aspect = max(_BrushAspect, 1.0);

                // Plane X (YZ plane)
                float2 uvX = float2(wp.z * cs - wp.y * sn, wp.z * sn + wp.y * cs);
                uvX.x /= aspect;
                float3 coordX = float3(wp.x * 0.5, uvX.y, uvX.x);

                // Plane Y (XZ plane)
                float2 uvY = float2(wp.x * cs - wp.z * sn, wp.x * sn + wp.z * cs);
                uvY.x /= aspect;
                float3 coordY = float3(uvY.x, wp.y * 0.5, uvY.y);

                // Plane Z (XY plane)
                float2 uvZ = float2(wp.x * cs - wp.y * sn, wp.x * sn + wp.y * cs);
                uvZ.x /= aspect;
                float3 coordZ = float3(uvZ.x, uvZ.y, wp.z * 0.5);

                return coordX * outTriWeights.x + coordY * outTriWeights.y + coordZ * outTriWeights.z;
            }

            // Fixed 3-Octave Blender 3D fBM
            float BlenderNoiseFBM(float3 p, float roughness, float lacunarity)
            {
                float n0 = GradientNoise3D(p);
                float n1 = GradientNoise3D(p * lacunarity) * roughness;
                float n2 = GradientNoise3D(p * (lacunarity * lacunarity)) * (roughness * roughness);
                float maxAmp = (1.0 + roughness + roughness * roughness) * 0.64;
                return saturate((n0 + n1 + n2) / max(maxAmp, 0.001) * 0.5 + 0.5);
            }

            float3 BlenderNoiseTextureRGB(float3 triCoord)
            {
                float3 p = triCoord * _NoiseScale;
                float r = BlenderNoiseFBM(p, _NoiseRoughness, _NoiseLacunarity);
                float g = BlenderNoiseFBM(p + float3(19.19, 47.73, 97.31), _NoiseRoughness, _NoiseLacunarity);
                float b = BlenderNoiseFBM(p + float3(73.37, 13.79, 53.91), _NoiseRoughness, _NoiseLacunarity);
                return float3(r, g, b);
            }

            // ----------------------------------------------------------------
            // Exact Blender F1 Minkowski Voronoi -> Position Output
            // ----------------------------------------------------------------
            void EvaluateBlenderVoronoiPosition(
                float3 vec,
                float scale,
                float exponent,
                float randomness,
                out float3 outVoronoiPosition,
                out float3 outCellHash,
                out float3 outCellDelta,
                out float outBorderDist)
            {
                float safeScale = max(abs(scale), 0.001);
                float3 x3 = vec * safeScale;
                float3 baseCell3 = floor(x3);

                float minDist1 = 1e9;
                float minDist2 = 1e9;
                float3 closestPos = x3;
                float3 closestHash = float3(0.5, 0.5, 0.5);
                float3 closestDelta = float3(0, 0, 0);
                bool isHalfExp = (abs(exponent - 0.5) < 0.05);
                float p = clamp(exponent, 0.3, 1.5);
                float invP = 1.0 / p;

                [unroll]
                for (int k = -1; k <= 1; k++)
                {
                    [unroll]
                    for (int j = -1; j <= 1; j++)
                    {
                        [unroll]
                        for (int i = -1; i <= 1; i++)
                        {
                            float3 cellCoord = baseCell3 + float3((float)i, (float)j, (float)k);
                            float3 rand3 = Hash33(cellCoord);
                            float3 pt3 = cellCoord + lerp(float3(0.5, 0.5, 0.5), rand3, randomness);
                            float3 delta = x3 - pt3;
                            float3 ad = abs(delta) + 1e-5;

                            float dist;
                            if (isHalfExp)
                            {
                                float3 sq = sqrt(ad);
                                float s = sq.x + sq.y + sq.z;
                                dist = s * s;
                            }
                            else
                            {
                                float3 pw = pow(ad, p);
                                dist = pow(max(pw.x + pw.y + pw.z, 1e-5), invP);
                            }

                            if (dist < minDist1)
                            {
                                minDist2 = minDist1;
                                minDist1 = dist;
                                closestPos = pt3;
                                closestHash = rand3;
                                closestDelta = delta;
                            }
                            else if (dist < minDist2)
                            {
                                minDist2 = dist;
                            }
                        }
                    }
                }

                outVoronoiPosition = closestPos / safeScale;
                outCellHash = closestHash;
                outCellDelta = closestDelta;
                outBorderDist = max(minDist2 - minDist1, 0.0);
            }

            // World-Space Locked Principled BSDF GGX Highlight Evaluation
            // Uses a fixed world-space reference half-vector (lightDir + normalWS)
            // instead of camera viewDirWS so highlights never pop or shift as you walk closer/further!
            float EvaluateWorldLockedPrincipledGGX(float3 paintedNormalWS, float3 normalWS, float3 lightDir, float ndotl, float roughness)
            {
                if (ndotl <= 0.0)
                    return 0.0;
                float3 worldRefHalf = normalize(lightDir + normalWS * 0.75 + float3(0.12, 0.65, -0.12));
                float ndoth = saturate(dot(paintedNormalWS, worldRefHalf));
                float a = max(roughness * roughness, 0.02);
                float a2 = a * a;
                float d = (ndoth * ndoth) * (a2 - 1.0) + 1.0;
                float ggx = a2 / max(3.14159265 * d * d, 1e-4);
                return smoothstep(0.16, 0.50, ggx * ndotl);
            }

            float QuantizeSignedSteps(float signedVal, float steps, float sharpness)
            {
                float s = max(steps, 2.0);
                float normalized = saturate(signedVal * 0.5 + 0.5);
                float scaled = normalized * s;
                float idx = floor(scaled);
                float f = frac(scaled);
                float smoothF = saturate((f - 0.5) * clamp(sharpness, 1.0, 25.0) + 0.5);
                return ((idx + smoothF) / s) * 2.0 - 1.0;
            }

            half4 fragDrawn(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_DrawnShaderGlobalDisabled > 0.5 || _EffectEnabled < 0.5 || _EffectIntensity <= 0.001)
                {
                    return half4(0.5, 0.5, 0.5, 1.0);
                }

                float3 normalWS = normalize(input.normalWS);

                // ------------------------------------------------------------
                // 1. Seamless World-Space Triplanar Coordinates
                // ------------------------------------------------------------
                float3 triWeights;
                float3 triCoord = GetSeamlessTriplanarCoords(input.positionWS, normalWS, triWeights);

                // ------------------------------------------------------------
                // 2. Exact @a_iwaac Blender Node Tree:
                //    Triplanar 3D fBM Noise -> Linear Light with Normal ->
                //    Minkowski Voronoi -> Position -> Normal
                // ------------------------------------------------------------
                float3 noiseRGB = BlenderNoiseTextureRGB(triCoord);

                float3 macroDrift = float3(
                    GradientNoise3D(triCoord * 0.62 + float3(5.3, 13.7, 29.1)),
                    GradientNoise3D(triCoord * 0.62 + float3(41.9, 7.1, 63.7)),
                    GradientNoise3D(triCoord * 0.62 + float3(79.3, 53.1, 17.9))
                ) * _FlatSurfaceSpread;

                float3 referenceNormalWS = normalize(normalWS + macroDrift);
                float3 linearLightVec = referenceNormalWS + _LinearLightFactor * (2.0 * noiseRGB - 1.0);

                float3 voronoiPos;
                float3 cellHash;
                float3 cellDelta;
                float vorBorder;
                EvaluateBlenderVoronoiPosition(
                    linearLightVec,
                    _VoronoiScale,
                    _VoronoiExponent,
                    _VoronoiRandomness,
                    voronoiPos,
                    cellHash,
                    cellDelta,
                    vorBorder
                );

                float vorLenSq = dot(voronoiPos, voronoiPos);
                float3 cellFlatNormal = (vorLenSq > 1e-6) ? (voronoiPos * rsqrt(vorLenSq)) : referenceNormalWS;
                float3 paintedNormalWS = normalize(referenceNormalWS + (cellFlatNormal - referenceNormalWS) * _PainterlyNormalStrength);

                // ------------------------------------------------------------
                // 3. 100% World-Space Locked Multi-Light Evaluation
                //    - Evaluates all scene lights directly in world space using
                //      GetAdditionalPerObjectLight (bypassing camera-depth viewZ
                //      Z-bins and screen-tile boundaries completely!).
                //    - Smoothly accumulates weighted light directions & colors
                //      instead of a discontinuous winner-take-all light switch!
                // ------------------------------------------------------------
                Light mainLight = GetMainLight();
                float mainAtten = mainLight.distanceAttenuation;
                float mainLum = dot(mainLight.color, float3(0.299, 0.587, 0.114));
                float mainWeight = mainAtten * mainLum;

                float smoothDiffuse = saturate(dot(normalWS, mainLight.direction)) * mainWeight;
                float paintedNdotL = saturate(dot(paintedNormalWS, mainLight.direction));
                float paintedDiffuse = paintedNdotL * mainWeight;
                float paintedSpec = EvaluateWorldLockedPrincipledGGX(paintedNormalWS, normalWS, mainLight.direction, paintedNdotL, _Roughness) * mainWeight;
                float totalLightEnergy = mainWeight * saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);

                // Smoothly accumulated light direction and color (seeded with gentle overhead room ambient vector)
                float3 overheadRoomDir = normalize(float3(0.28, 0.88, -0.28));
                float accumWeight = 0.25 + mainWeight;
                float3 accumLightDir = overheadRoomDir * 0.25 + mainLight.direction * mainWeight;
                float3 accumLightColor = float3(1.0, 0.96, 0.90) * 0.25
                                       + ((mainLum > 0.01) ? (mainLight.color / max(mainLum, 0.1)) : float3(1.0, 0.96, 0.90)) * mainWeight;

                #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                {
                    #if USE_CLUSTER_LIGHT_LOOP
                    // In Forward+ mode, _FPParams0.z is local light count and _FPParams0.w is additional directional light count.
                    // Looping directly over the visible lights in world space completely eliminates
                    // viewZ Z-Bin and 2D screen-tile boundary lines!
                    uint totalLights = min((uint)_FPParams0.z + (uint)_FPParams0.w, 16u);
                    [loop]
                    for (uint lightIndex = 0u; lightIndex < totalLights; lightIndex++)
                    {
                        Light addLight = GetAdditionalPerObjectLight(lightIndex, input.positionWS);
                    #else
                    uint pixelLightCount = GetAdditionalLightsCount();
                    for (uint lightIndex = 0u; lightIndex < pixelLightCount; ++lightIndex)
                    {
                        Light addLight = GetAdditionalLight(lightIndex, input.positionWS);
                    #endif
                        float addAtten = addLight.distanceAttenuation;
                        float addLum = dot(addLight.color, float3(0.299, 0.587, 0.114));
                        float w = addAtten * addLum;

                        float sNdotL = saturate(dot(normalWS, addLight.direction));
                        float pNdotL = saturate(dot(paintedNormalWS, addLight.direction));

                        smoothDiffuse += sNdotL * w;
                        paintedDiffuse += pNdotL * w;
                        paintedSpec += EvaluateWorldLockedPrincipledGGX(paintedNormalWS, normalWS, addLight.direction, pNdotL, _Roughness) * w;
                        totalLightEnergy += w * saturate(dot(normalWS, addLight.direction) * 0.5 + 0.5);

                        accumLightDir += addLight.direction * w;
                        accumLightColor += (addLight.color / max(addLum, 0.1)) * w;
                        accumWeight += w;
                    }
                }
                #endif

                float3 effectiveLightDir = normalize(accumLightDir / max(accumWeight, 0.001));
                float3 closestLightColor = accumLightColor / max(accumWeight, 0.001);

                // Global world-space ambient hemisphere (replaces per-object SampleSH so adjacent
                // or overlapping floor/wall slabs produce identical shading with zero seam!)
                float3 ambSky = max(unity_AmbientSky.rgb, float3(0.24, 0.22, 0.20));
                float3 ambGround = max(unity_AmbientGround.rgb, float3(0.10, 0.09, 0.11));
                float3 shSmooth = lerp(ambGround, ambSky, normalWS.y * 0.5 + 0.5);
                float3 shPainted = lerp(ambGround, ambSky, paintedNormalWS.y * 0.5 + 0.5);
                float shLumSmooth = dot(shSmooth, float3(0.299, 0.587, 0.114));
                float shLumPainted = dot(shPainted, float3(0.299, 0.587, 0.114));

                // Flat cell lighting difference relative to the smooth multi-light direction
                float facetLightDiff = dot(paintedNormalWS, effectiveLightDir) - dot(referenceNormalWS, effectiveLightDir);

                float effSpec = EvaluateWorldLockedPrincipledGGX(
                    paintedNormalWS,
                    normalWS,
                    effectiveLightDir,
                    saturate(dot(paintedNormalWS, effectiveLightDir)),
                    _Roughness
                );

                // ------------------------------------------------------------
                // 4. World-Locked Principled BSDF Fresnel Rim & Bounce Dabs
                //    Evaluated against surface normal referenceNormalWS instead
                //    of camera viewDirWS so rim dabs stay 100% locked to the object
                //    and never pop as you walk closer or further!
                // ------------------------------------------------------------
                float paintedNdotRef = saturate(dot(paintedNormalWS, referenceNormalWS));
                float oneMinusNdotRef = 1.0 - paintedNdotRef;
                float schlickFresnel = oneMinusNdotRef * oneMinusNdotRef;
                float fresnelDab = smoothstep(0.14, 0.42, schlickFresnel);

                float litRimMask = fresnelDab * smoothstep(-0.05, 0.42, dot(paintedNormalWS, effectiveLightDir));
                float shadowBounceMask = fresnelDab * smoothstep(0.05, -0.42, dot(paintedNormalWS, effectiveLightDir))
                                       * step(0.42, cellHash.y);

                // ------------------------------------------------------------
                // 5. Directional Overlapping Brush-Dab Impasto
                // ------------------------------------------------------------
                float edgeMask = 1.0 - smoothstep(0.01, 0.13, vorBorder);
                float edgeDirection = clamp(dot(normalize(cellDelta + 1e-5), effectiveLightDir), -1.0, 1.0);
                float directionalImpasto = edgeMask * edgeDirection * _BrushOverlapImpasto;

                float directDelta = paintedDiffuse - smoothDiffuse;
                float combinedDelta = directDelta * 1.55 + facetLightDiff * 1.05 + (shLumPainted - shLumSmooth) * 0.85;
                float cellValueOffset = (cellHash.x - 0.5) * 2.0 * _PerStrokeVariation;

                float steppedBrush = QuantizeSignedSteps(
                    clamp(combinedDelta * 1.45 + cellValueOffset, -1.0, 1.0),
                    _LightSteps,
                    _LightStepSharpness
                );

                float smoothTotal = clamp(smoothDiffuse + shLumSmooth + 0.35, 0.30, 1.75);
                float cellTargetFlat = clamp(lerp(smoothTotal, 0.78, 0.50), 0.35, 1.45);
                float flattenFactor = lerp(1.0, clamp(cellTargetFlat / smoothTotal, 0.68, 1.42), _SmoothFlattenStrength);

                float rawLightPresence = saturate(totalLightEnergy * 2.4 + shLumSmooth * 1.9 + 0.38);
                float lightGate = lerp(1.0, smoothstep(0.05, 0.35, rawLightPresence), _ShadowProtection);

                float specBoost = max(paintedSpec, effSpec * 0.75) * _PaintedSpecularStrength;
                float rimBoost = (litRimMask * 0.90 + shadowBounceMask * 0.50) * _FresnelRimStrength;

                float strokeSignal = (steppedBrush * _PaintedLightStrength + directionalImpasto + specBoost + rimBoost) * lightGate;

                // ------------------------------------------------------------
                // 6. Rich Gouache Warm-Lit / Terminator / Cool-Shadow Pigment Switch
                // ------------------------------------------------------------
                float3 normalizedLightTint = clamp(closestLightColor, float3(0.84, 0.84, 0.84), float3(1.24, 1.18, 1.14));
                float tempAmount = clamp(strokeSignal * _WarmCoolShift, -0.28, 0.28);

                float cellHueJitter = (cellHash.z - 0.5) * _WarmCoolShift * 0.45 * lightGate;
                float totalTemp = tempAmount + cellHueJitter;

                float3 strokeColorShift = lerp(
                    float3(1.0, 1.0, 1.0),
                    normalizedLightTint * float3(1.12, 1.02, 0.88),
                    saturate(totalTemp * 2.8)
                );
                strokeColorShift = lerp(
                    strokeColorShift,
                    float3(0.85, 0.89, 1.13),
                    saturate(-totalTemp * 2.8)
                );

                strokeColorShift = lerp(
                    strokeColorShift,
                    float3(1.06, 0.95, 1.20),
                    saturate(shadowBounceMask * _FresnelRimStrength * lightGate)
                );

                float3 strokeMultiplier = max((1.0 + strokeSignal) * flattenFactor, 0.32) * strokeColorShift;
                float3 finalMultiplier = lerp(float3(1.0, 1.0, 1.0), strokeMultiplier, _EffectIntensity);

                float3 srcBlendValue = max(finalMultiplier * 0.5, float3(0.05, 0.05, 0.05));
                return half4(srcBlendValue, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
