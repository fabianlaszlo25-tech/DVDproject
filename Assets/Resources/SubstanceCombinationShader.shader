Shader "Custom/SubstanceCombinationShader"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "white" {}
        _EchoTex1 ("Echo Frame 1 (Recent)", 2D) = "black" {}
        _EchoTex2 ("Echo Frame 2 (Middle)", 2D) = "black" {}
        _EchoTex3 ("Echo Frame 3 (Oldest)", 2D) = "black" {}
        _SmearAccumTex ("Persistent Smear Buffer", 2D) = "black" {}
        _VideoTex ("Hallucination Video", 2D) = "black" {}

        _Intensity ("Effect Intensity", Range(0, 1)) = 0
        _ComboMode ("Combo Mode (1..6)", Int) = 1
        _VideoFade ("Hallucination Video Fade", Range(0, 1)) = 0
        _BlackoutFade ("Blackout Fade", Range(0, 1)) = 0

        _GhostOffset1 ("Camera Echo Offset 1 (XY) & Motion Weight (Z)", Vector) = (0, 0, 0, 0)
        _GhostOffset2 ("Camera Echo Offset 2 (XY)", Vector) = (0, 0, 0, 0)
        _GhostOffset3 ("Camera Echo Offset 3 (XY)", Vector) = (0, 0, 0, 0)

        _ParamA ("Combo Params A", Vector) = (1, 1, 1, 1)
        _ParamB ("Combo Params B", Vector) = (1, 1, 1, 1)
        _ParamC ("Combo Params C", Vector) = (1, 1, 1, 1)
        _ColorA ("Combo Color A", Color) = (1, 1, 1, 1)
        _ColorB ("Combo Color B", Color) = (1, 1, 1, 1)
        _SmearParams ("Smear Retention (X), Flow Speed (Y), Flow Strength (Z), Hue Drift (W)", Vector) = (0.86, 0.85, 0.0085, 0.025)
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _EchoTex1;
    sampler2D _EchoTex2;
    sampler2D _EchoTex3;
    sampler2D _SmearAccumTex;
    sampler2D _VideoTex;

    float _Intensity;
    int _ComboMode;
    float _VideoFade;
    float _BlackoutFade;

    float4 _GhostOffset1;
    float4 _GhostOffset2;
    float4 _GhostOffset3;

    float4 _ParamA;
    float4 _ParamB;
    float4 _ParamC;
    float4 _ColorA;
    float4 _ColorB;
    float4 _SmearParams;

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

        float a = Hash21(i);
        float b = Hash21(i + float2(1.0, 0.0));
        float c = Hash21(i + float2(0.0, 1.0));
        float d = Hash21(i + float2(1.0, 1.0));

        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    float FBM(float2 p)
    {
        float v = 0.0;
        float amp = 0.5;
        float2 shift = float2(100.0, 100.0);
        for (int i = 0; i < 3; i++)
        {
            v += amp * SmoothNoise(p);
            p = p * 2.02 + shift;
            amp *= 0.5;
        }
        return v;
    }

    float2 OrganicFlowField(float2 uv, float time)
    {
        float n1 = FBM(uv * 3.2 + float2(time * 0.35, -time * 0.25));
        float n2 = FBM(uv * 3.2 + float2(-time * 0.3, time * 0.4) + 17.3);
        float angle = (n1 - n2) * 6.28318;
        return float2(cos(angle), sin(angle)) * (n1 * 0.5 + 0.5);
    }

    float3 ShiftHue(float3 col, float hueAngle)
    {
        float3 k = float3(0.57735, 0.57735, 0.57735);
        float cosA = cos(hueAngle);
        return saturate(col * cosA + cross(k, col) * sin(hueAngle) + k * dot(k, col) * (1.0 - cosA));
    }

    float3 SampleBlurredChromatic(sampler2D tex, float2 uv, float blurRadius, float2 chromaVec)
    {
        float2 cUV = clamp(uv, 0.002, 0.998);
        float2 offX = float2(blurRadius, 0.0);
        float2 offY = float2(0.0, blurRadius);

        float3 center = tex2D(tex, cUV).rgb;
        float3 t1 = tex2D(tex, clamp(cUV + offX, 0.002, 0.998)).rgb;
        float3 t2 = tex2D(tex, clamp(cUV - offX, 0.002, 0.998)).rgb;
        float3 t3 = tex2D(tex, clamp(cUV + offY, 0.002, 0.998)).rgb;
        float3 t4 = tex2D(tex, clamp(cUV - offY, 0.002, 0.998)).rgb;

        float3 blurred = (center * 2.0 + t1 + t2 + t3 + t4) / 6.0;

        float r = tex2D(tex, clamp(cUV + chromaVec, 0.002, 0.998)).r;
        float b = tex2D(tex, clamp(cUV - chromaVec, 0.002, 0.998)).b;

        return float3(lerp(blurred.r, r, 0.55), blurred.g, lerp(blurred.b, b, 0.55));
    }

    float3 SampleRadialZoomBlur(sampler2D tex, float2 uv, float2 centerPoint, float radialStrength, float chromaStrength)
    {
        float2 dir = uv - centerPoint;
        float3 accum = float3(0, 0, 0);
        const int taps = 5;
        for (int s = 0; s < taps; s++)
        {
            float scale = 1.0 - radialStrength * ((float)s / (float)(taps - 1));
            float2 sampleUV = clamp(centerPoint + dir * scale, 0.002, 0.998);
            float2 chroma = dir * (chromaStrength * (1.0 + s * 0.25));

            float r = tex2D(tex, clamp(sampleUV + chroma, 0.002, 0.998)).r;
            float g = tex2D(tex, sampleUV).g;
            float b = tex2D(tex, clamp(sampleUV - chroma, 0.002, 0.998)).b;
            accum += float3(r, g, b);
        }
        return accum / (float)taps;
    }

    float4 fragSmearAccum (v2f i) : SV_Target
    {
        float2 uv = i.uv;
        float t = _Time.y;

        float retention    = clamp(_SmearParams.x, 0.0, 0.98);
        float flowSpeed    = _SmearParams.y;
        float flowStrength = _SmearParams.z;
        float hueDrift     = _SmearParams.w;

        float2 flow = OrganicFlowField(uv, t * flowSpeed) * flowStrength * _Intensity;
        float2 camDrag = _GhostOffset1.xy * 0.35 * _Intensity;

        float2 centered = uv - 0.5;
        float swirlAngle = 0.014 * sin(t * 0.9) * _Intensity;
        float s = sin(swirlAngle);
        float c = cos(swirlAngle);
        float2 rotatedCentered = float2(centered.x * c - centered.y * s, centered.x * s + centered.y * c);

        float2 prevUV = clamp(rotatedCentered * 0.992 + 0.5 + flow + camDrag, 0.002, 0.998);

        float3 prevSmear = SampleBlurredChromatic(_SmearAccumTex, prevUV, 0.0025, flow * 0.8);
        prevSmear = ShiftHue(prevSmear, hueDrift * _Intensity);

        float3 liveFrame = tex2D(_MainTex, uv).rgb;
        float3 echoFrame = tex2D(_EchoTex2, clamp(uv + _GhostOffset2.xy * _Intensity, 0.002, 0.998)).rgb;

        float activeRetention = lerp(0.25, retention, saturate(_Intensity));
        float3 combined = lerp(lerp(liveFrame, echoFrame, 0.4), prevSmear, activeRetention);

        return float4(saturate(combined), 1.0);
    }

    float4 fragMain (v2f i) : SV_Target
    {
        if (_Intensity <= 0.001 && _BlackoutFade <= 0.001)
        {
            return float4(0, 0, 0, 0);
        }

        float2 uv = i.uv;
        float2 centered = uv - 0.5;
        float distSq = dot(centered, centered);
        float dist = sqrt(distSq);
        float t = _Time.y;
        float motionWeight = saturate(_GhostOffset1.z * _Intensity);

        float3 finalRGB = tex2D(_MainTex, uv).rgb;

        if (_ComboMode == 1)
        {
            float beatT = t * _ParamA.x;
            float lub = pow(max(0.0, sin(beatT)), 6.0);
            float dub = pow(max(0.0, sin(beatT - 0.8)), 8.0) * 0.7;
            float heartPulse = (lub + dub) * _Intensity;

            float peripheralMask = smoothstep(0.12, 0.58, dist);
            float jitterClock = floor(t * 60.0);
            float2 microTremor = float2(
                Hash21(float2(jitterClock, uv.y * 10.0)) * 2.0 - 1.0,
                Hash21(float2(uv.x * 10.0, jitterClock + 7.7)) * 2.0 - 1.0
            ) * (_ParamB.x * _Intensity * peripheralMask);

            float zoom = 1.0 - (_ParamA.y * (0.4 + 0.6 * heartPulse) * _Intensity);
            float2 focusUV = clamp(centered * zoom + 0.5 + microTremor, 0.002, 0.998);

            float2 texel = _MainTex_TexelSize.xy * 1.4;
            float3 centerCol = tex2D(_MainTex, focusUV).rgb;
            float3 surround = (
                tex2D(_MainTex, clamp(focusUV + float2(texel.x, 0), 0.002, 0.998)).rgb +
                tex2D(_MainTex, clamp(focusUV - float2(texel.x, 0), 0.002, 0.998)).rgb +
                tex2D(_MainTex, clamp(focusUV + float2(0, texel.y), 0.002, 0.998)).rgb +
                tex2D(_MainTex, clamp(focusUV - float2(0, texel.y), 0.002, 0.998)).rgb
            ) * 0.25;
            float3 hyperSharp = centerCol + (centerCol - surround) * (_ParamA.z * _Intensity);

            float3 radialPeriphery = SampleRadialZoomBlur(
                _MainTex,
                focusUV,
                0.5 - _GhostOffset1.xy * 0.5,
                _ParamA.w * _Intensity * (0.5 + 0.5 * heartPulse),
                _ParamB.y * _Intensity
            );

            float3 echo1 = tex2D(_EchoTex1, clamp(focusUV + _GhostOffset1.xy * 0.65 * _Intensity, 0.002, 0.998)).rgb;
            float3 echo2 = tex2D(_EchoTex2, clamp(focusUV + _GhostOffset2.xy * 0.95 * _Intensity, 0.002, 0.998)).rgb;
            float3 motionTrail = lerp(radialPeriphery, (echo1 * 0.6 + echo2 * 0.4), motionWeight * 0.65);

            finalRGB = lerp(hyperSharp, motionTrail, peripheralMask * 0.85);

            float luma = dot(finalRGB, float3(0.299, 0.587, 0.114));
            finalRGB = lerp(float3(luma, luma, luma), finalRGB, 1.0 + _ParamB.z * _Intensity);
            finalRGB = saturate((finalRGB - 0.5) * (1.0 + _ParamB.w * _Intensity) + 0.5);

            float angle = atan2(centered.y, centered.x);
            float rays = pow(sin(angle * 42.0 + jitterClock * 2.5) * 0.5 + 0.5, 4.0);
            float3 rimColor = lerp(_ColorA.rgb, _ColorB.rgb, sin(angle * 2.0 + t * 4.0) * 0.5 + 0.5);
            finalRGB += rimColor * (rays * smoothstep(0.28, 0.68, dist) * (0.4 + 0.6 * heartPulse) * _ParamC.x * _Intensity);
        }
        else if (_ComboMode == 2)
        {
            float cycle = fmod(t * _ParamA.x, 1.0);
            float systole = exp(-pow((cycle - 0.12) * 18.0, 2.0));
            float diastole = exp(-pow((cycle - 0.28) * 14.0, 2.0)) * 0.75;
            float fibrillation = sin(t * 38.0) * exp(-pow((cycle - 0.65) * 8.0, 2.0)) * 0.35;
            float cardiacSlam = saturate(systole + diastole + max(0.0, fibrillation)) * _Intensity;

            float2 sway = float2(sin(t * 2.1), cos(t * 1.7)) * (0.014 * _Intensity);
            float2 thumpJolt = float2(
                (Hash21(float2(floor(t * 45.0), 1.3)) - 0.5),
                -cardiacSlam * 0.028
            ) * cardiacSlam;

            float zoom = 1.0 - (_ParamA.y * cardiacSlam) - (0.02 * _Intensity);
            float2 heartUV = clamp(centered * zoom + 0.5 + sway + thumpJolt, 0.002, 0.998);

            float3 slammedFrame = SampleRadialZoomBlur(
                _MainTex,
                heartUV,
                0.5,
                (0.015 + _ParamA.z * cardiacSlam) * _Intensity,
                (0.004 + _ParamA.w * cardiacSlam) * _Intensity
            );

            float2 chromaVec = normalize(centered + 0.0001) * (0.01 * _Intensity);
            float splitAmt = _ParamB.x * cardiacSlam;
            float3 ghost1 = SampleBlurredChromatic(_EchoTex1, heartUV + _GhostOffset1.xy * _Intensity + float2(splitAmt, 0.0), 0.004 * _Intensity, chromaVec);
            float3 ghost2 = SampleBlurredChromatic(_EchoTex2, heartUV + _GhostOffset2.xy * _Intensity - float2(splitAmt, 0.0), 0.006 * _Intensity, -chromaVec);
            float3 ghost3 = SampleBlurredChromatic(_EchoTex3, heartUV + _GhostOffset3.xy * _Intensity + float2(0.0, splitAmt * 1.2), 0.008 * _Intensity, chromaVec);

            float3 tripleSpasm = ghost1 * 0.45 + ghost2 * 0.35 + ghost3 * 0.20;
            finalRGB = lerp(slammedFrame, tripleSpasm, saturate(0.35 + 0.55 * cardiacSlam + 0.4 * motionWeight) * _Intensity);

            float luma = dot(finalRGB, float3(0.299, 0.587, 0.114));
            float3 paleAsh = float3(luma, luma, luma) * float3(0.92, 0.95, 1.02);
            finalRGB = lerp(finalRGB, paleAsh, saturate(_ParamB.y + 0.2 * cardiacSlam) * _Intensity);

            float tunnelRadius = lerp(_ParamB.z, _ParamB.w, cardiacSlam);
            float veinNoise = FBM(centered * 8.0 + float2(t * 0.4, -t * 0.4)) * 0.12;
            float tunnelMask = smoothstep(tunnelRadius * 0.45, tunnelRadius + veinNoise, dist);

            float3 bloodDarkness = lerp(_ColorA.rgb * (0.5 + cardiacSlam), float3(0.02, 0.0, 0.0), smoothstep(0.35, 0.72, dist));
            finalRGB = lerp(finalRGB, bloodDarkness, tunnelMask * (0.82 + 0.18 * cardiacSlam) * _Intensity);
        }
        else if (_ComboMode == 3)
        {
            float spinPhase = t * _ParamA.x;
            float rotAngle = (sin(spinPhase) * 0.62 + cos(spinPhase * 0.65) * 0.38) * _ParamA.y * _Intensity;
            float s = sin(rotAngle);
            float c = cos(rotAngle);
            float2 spunCentered = float2(centered.x * c - centered.y * s, centered.x * s + centered.y * c);

            float heaveAmp = _ParamA.z * _Intensity;
            float2 flowWave = OrganicFlowField(uv * 0.8, t * 0.9) * (heaveAmp * 1.1);
            float heaveX = sin(uv.y * 6.5 + t * 2.4) * cos(uv.x * 4.5 - t * 1.7) * heaveAmp * 0.8;
            float heaveY = cos(uv.x * 6.5 + t * 2.1) * sin(uv.y * 5.0 + t * 1.5) * heaveAmp * 1.05;

            float safeZoom = 1.0 - saturate(_ParamA.y * 0.55 + heaveAmp * 2.5) * _Intensity;
            float2 spinUV = clamp(spunCentered * safeZoom + 0.5 + flowWave + float2(heaveX, heaveY), 0.002, 0.998);

            float blur = _ParamA.w * _Intensity;
            float2 chroma = float2(heaveX, heaveY) * 0.35 + 0.004 * _Intensity;

            float3 baseSpin = SampleBlurredChromatic(_MainTex, spinUV, blur, chroma);
            float3 echo1 = SampleBlurredChromatic(_EchoTex1, spinUV + _GhostOffset1.xy * 1.2 * _Intensity, blur * 1.4, chroma * 1.3);
            float3 echo2 = SampleBlurredChromatic(_EchoTex2, spinUV + _GhostOffset2.xy * 1.6 * _Intensity, blur * 1.9, -chroma * 1.6);
            float3 echo3 = SampleBlurredChromatic(_EchoTex3, spinUV + _GhostOffset3.xy * 2.1 * _Intensity, blur * 2.5, chroma * 2.0);
            float3 smear = tex2D(_SmearAccumTex, spinUV).rgb;

            float3 spinningTrail = echo1 * 0.4 + echo2 * 0.3 + echo3 * 0.15 + smear * 0.15;
            finalRGB = lerp(baseSpin, spinningTrail, saturate(_ParamB.x + 0.4 * motionWeight) * _Intensity);

            float bileWave = sin(t * 2.2 + dist * 4.0) * 0.5 + 0.5;
            float3 bileGrade = lerp(_ColorA.rgb, _ColorB.rgb, bileWave);
            float luma = dot(finalRGB, float3(0.299, 0.587, 0.114));
            float3 sicklyCol = lerp(float3(luma, luma, luma), finalRGB, 0.65) * bileGrade;
            finalRGB = lerp(finalRGB, sicklyCol, _ParamB.y * _Intensity);

            float nauseaVignette = smoothstep(0.22, 0.72, dist);
            finalRGB = lerp(finalRGB, _ColorA.rgb * 0.15, nauseaVignette * _ParamB.z * _Intensity);
        }
        else if (_ComboMode == 4)
        {
            float panicPulse = pow(sin(t * _ParamA.x) * 0.5 + 0.5, 3.0) * _Intensity;
            float nerveClock = floor(t * 52.0);
            float suddenSnap = step(0.76, Hash21(float2(nerveClock, 9.1)));

            float barrelWarp = 1.0 + (_ParamA.y * distSq * (0.5 + 0.5 * panicPulse) * _Intensity);
            float safeZoom = 1.0 - (0.06 * _Intensity);

            float2 nerveJitter = float2(
                Hash21(float2(nerveClock, uv.y * 14.0)) - 0.5,
                Hash21(float2(uv.x * 14.0, nerveClock + 3.3)) - 0.5
            ) * (_ParamA.z * (1.0 + 2.0 * suddenSnap)) * _Intensity;

            float2 panicUV = clamp(centered * barrelWarp * safeZoom + 0.5 + nerveJitter, 0.002, 0.998);

            float2 splitVec = normalize(centered + 0.0001) * (_ParamA.w * (0.6 + 0.8 * panicPulse + suddenSnap)) * _Intensity;
            float3 liveCol = SampleBlurredChromatic(_MainTex, panicUV, 0.0018 * _Intensity, splitVec);

            float3 ghost1 = SampleBlurredChromatic(_EchoTex1, panicUV + _GhostOffset1.xy * 1.1 * _Intensity, 0.003 * _Intensity, -splitVec);
            float3 ghost2 = SampleBlurredChromatic(_EchoTex3, panicUV - _GhostOffset2.xy * 0.9 * _Intensity, 0.005 * _Intensity, splitVec * 1.5);

            float peripheryWeight = smoothstep(0.1, 0.52, dist);
            finalRGB = lerp(liveCol, (ghost1 * 0.55 + ghost2 * 0.45), peripheryWeight * saturate(0.4 + 0.6 * motionWeight) * _Intensity);

            float luma = dot(finalRGB, float3(0.299, 0.587, 0.114));
            float3 coldBleach = lerp(finalRGB, float3(luma, luma, luma), _ParamB.y) * _ColorA.rgb;
            coldBleach = saturate((coldBleach - 0.5) * (1.0 + _ParamB.x * _Intensity) + 0.5);
            finalRGB = lerp(finalRGB, coldBleach, 0.85 * _Intensity);

            float shadowNoise = FBM(centered * 6.5 + float2(nerveClock * 0.15, t * 2.5));
            float paranoiaTunnel = smoothstep(0.18, 0.62 - panicPulse * 0.12 - shadowNoise * 0.15, dist);
            finalRGB *= (1.0 - paranoiaTunnel * _ParamB.z * _Intensity);
        }
        else if (_ComboMode == 5)
        {
            float headRushWave = sin(t * _ParamA.x) * 0.5 + 0.5;
            float2 gravitySink = float2(sin(t * (_ParamA.x * 0.75)) * 0.8, cos(t * (_ParamA.x * 0.55))) * (_ParamA.y * _Intensity);
            float zoom = 1.0 - (0.045 * _Intensity);
            float2 buzzUV = clamp(centered * zoom + 0.5 + gravitySink, 0.002, 0.998);

            float blur = _ParamA.z * _Intensity;
            float2 chroma = float2(_ParamA.w, _ParamA.w * 0.45) * _Intensity;

            float3 baseCol = SampleBlurredChromatic(_MainTex, buzzUV, blur, chroma);
            float3 echo1 = SampleBlurredChromatic(_EchoTex1, buzzUV + _GhostOffset1.xy * _Intensity, blur * 1.4, -chroma);
            float3 echo2 = SampleBlurredChromatic(_EchoTex2, buzzUV + _GhostOffset2.xy * 1.3 * _Intensity, blur * 1.9, chroma * 1.4);
            float3 echo3 = SampleBlurredChromatic(_EchoTex3, buzzUV + _GhostOffset3.xy * 1.6 * _Intensity, blur * 2.4, -chroma * 1.8);

            float3 buzzTrail = echo1 * 0.45 + echo2 * 0.33 + echo3 * 0.22;
            finalRGB = lerp(baseCol, buzzTrail, saturate(_ParamB.x + 0.45 * motionWeight) * _Intensity);

            finalRGB *= lerp(float3(1.0, 1.0, 1.0), _ColorA.rgb, _Intensity);
            float smokeVignette = smoothstep(0.25, 0.75, dist);
            finalRGB = lerp(finalRGB, finalRGB * (_ColorA.rgb * 0.25), smokeVignette * (_ParamB.y + 0.15 * headRushWave) * _Intensity);
        }
        else if (_ComboMode == 6)
        {
            float flowSpeed = _ParamA.x;
            float2 fluidFlow1 = OrganicFlowField(uv * 1.1, t * flowSpeed);
            float2 fluidFlow2 = OrganicFlowField(uv * 2.3 + fluidFlow1 * 0.4, -t * (flowSpeed * 0.8));
            float2 totalFlow = (fluidFlow1 * 0.65 + fluidFlow2 * 0.35) * (_ParamA.y * _Intensity);

            float vortexAngle = sin(t * 0.8 + dist * 3.5) * _ParamA.z * _Intensity;
            float vs = sin(vortexAngle);
            float vc = cos(vortexAngle);
            float2 warpedCentered = float2(centered.x * vc - centered.y * vs, centered.x * vs + centered.y * vc);

            float safeZoom = 1.0 - saturate(_ParamA.y * 2.2 + _ParamA.z * 0.35) * _Intensity;
            float2 meltedUV = clamp(warpedCentered * safeZoom + 0.5 + totalFlow, 0.002, 0.998);

            float blur = _ParamA.w * _Intensity;
            float2 chroma = totalFlow * 0.45 + normalize(centered + 0.0001) * (_ParamB.z * _Intensity);

            float3 liveMelt  = SampleBlurredChromatic(_MainTex, meltedUV, blur, chroma);
            float3 echoMelt1 = SampleBlurredChromatic(_EchoTex1, meltedUV + _GhostOffset1.xy * 1.2 * _Intensity + totalFlow * 0.5, blur * 1.4, -chroma);
            float3 echoMelt2 = SampleBlurredChromatic(_EchoTex2, meltedUV + _GhostOffset2.xy * 1.7 * _Intensity - totalFlow * 0.5, blur * 1.9, chroma * 1.4);
            float3 echoMelt3 = SampleBlurredChromatic(_EchoTex3, meltedUV + _GhostOffset3.xy * 2.2 * _Intensity + totalFlow * 0.8, blur * 2.5, -chroma * 1.8);
            float3 persistentSmear = SampleBlurredChromatic(_SmearAccumTex, meltedUV - _GhostOffset1.xy * 0.5 * _Intensity, blur * 1.6, chroma);

            float3 multiFrameMelt = liveMelt * 0.30 + echoMelt1 * 0.30 + echoMelt2 * 0.22 + echoMelt3 * 0.18;
            float3 smearedWorld = lerp(multiFrameMelt, persistentSmear, saturate(_ParamB.x));

            float flowLuma = dot(smearedWorld, float3(0.299, 0.587, 0.114));
            smearedWorld = ShiftHue(smearedWorld, (flowLuma * 2.2 + t * 0.6) * _Intensity * _ParamB.y);

            float2 videoUV = clamp(
                warpedCentered * (0.94 + 0.04 * sin(t * 1.5)) + 0.5 + totalFlow * 0.65 + _GhostOffset1.xy * 0.3 * _Intensity,
                0.002, 0.998
            );
            float3 videoCol = SampleBlurredChromatic(_VideoTex, videoUV, blur * 0.8, chroma * 0.6);

            float fluidMask = smoothstep(0.15, 0.85, FBM(uv * 3.5 + totalFlow * 8.0 + t * 0.4));
            float organicVideoBlend = saturate(_VideoFade * (0.75 + 0.5 * fluidMask));

            finalRGB = lerp(smearedWorld, lerp(smearedWorld * 0.35 + videoCol * 0.85, videoCol, _VideoFade), organicVideoBlend);
        }

        // Apply Blackout Fade
        finalRGB = lerp(finalRGB, float3(0.0, 0.0, 0.0), saturate(_BlackoutFade));

        float finalAlpha = max(saturate(_Intensity * 1.2), saturate(_BlackoutFade));
        return float4(saturate(finalRGB), finalAlpha);
    }
    ENDCG

    SubShader
    {
        Tags { "Queue"="Overlay+120" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "SmearAccumulationPass"
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragSmearAccum
            ENDCG
        }

        Pass
        {
            Name "SubstanceCombinationMainPass"
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragMain
            ENDCG
        }
    }
}