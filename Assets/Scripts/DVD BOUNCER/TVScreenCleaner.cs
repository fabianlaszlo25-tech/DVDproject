using System.Collections;
using UnityEngine;

/// <summary>
/// System 5: Screen Dust & Towel Cleaning System.
/// Slowly accumulates a high-resolution dust texture/mask overlay on a duplicate otherwise-invisible
/// screen mesh (TVDustOverlay) positioned slightly in front of the TV screen glass.
/// Debuffs DVD logo base speed and remote hit impulse strength up to 50% at max dust.
/// Supports dynamic UV brush erasing when wiping with the Towel in TV Focus Mode,
/// and triggers a sparkle/flash + chime payoff once >= 90% of the screen is cleaned.
/// </summary>
public class TVScreenCleaner : MonoBehaviour
{
    public static TVScreenCleaner Instance { get; private set; }

    [Header("Screen References")]
    [Tooltip("The MeshRenderer of the TV screen quad (DVDSetup/Televisor_80/screen).")]
    public MeshRenderer tvScreenRenderer;
    [Tooltip("Dedicated duplicate screen overlay renderer slightly in front of tvScreenRenderer. Auto-created if null.")]
    public MeshRenderer dustOverlayRenderer;

    [Header("High-Resolution Dust Texture Settings")]
    [Tooltip("Resolution of the dust mask texture on the duplicate front screen mesh (512 = crisp HD, 1024 = Ultra HD).")]
    [Range(128, 1024)]
    public int dustTextureResolution = 512;
    [Tooltip("Optional custom tileable dust texture. If left null, a high-res multi-layered organic dust pattern is generated.")]
    public Texture2D customDustPatternTexture;
    [Tooltip("Base tint color of the dust layer on the screen.")]
    public Color dustTintColor = new Color(0.82f, 0.79f, 0.72f, 1f);
    [Tooltip("Local Z offset on Televisor_80 to place the duplicate dust screen slightly in front of the TV screen glass.")]
    public float overlayFrontOffset = 0.0012f;

    [Header("Dust Accumulation & Debuff Settings")]
    [Tooltip("Time in seconds for dust to reach 100% accumulation from a clean screen.")]
    public float secondsToFullDust = 500f;
    [Tooltip("Starting dust level (0 to 1) so you can test/see dust early if desired.")]
    [Range(0f, 1f)]
    public float initialDustRatio = 0.08f;
    [Tooltip("Maximum visual opacity of the dust layer at 100% dust.")]
    [Range(0.2f, 0.95f)]
    public float maxDustVisualAlpha = 0.78f;
    [Tooltip("Maximum speed & hit-impulse penalty at 100% dust (0.5 = 50% speed at max dust).")]
    [Range(0.2f, 1.0f)]
    public float minSpeedMultiplierAtFullDust = 0.50f;

    [Header("Towel Wiping & Clean Payoff Settings")]
    [Tooltip("Brush radius in UV space (0..1) when wiping the screen with the towel.")]
    [Range(0.04f, 0.30f)]
    public float wipeBrushRadiusUV = 0.13f;
    [Tooltip("Softness of the towel brush edge (0 = hard edge, 1 = feathered cloth edge).")]
    [Range(0.1f, 0.95f)]
    public float wipeBrushFeather = 0.65f;
    [Tooltip("Threshold of screen cleanliness (0.90 = 90% clean) to trigger the Clean Payoff.")]
    [Range(0.80f, 0.98f)]
    public float cleanPayoffThreshold = 0.90f;
    [Tooltip("Optional custom particle prefab spawned when >= 90% of the screen is cleaned.")]
    public GameObject cleanSparkleParticlePrefab;

    [Header("Audio")]
    public AudioSource wipeAudioSource;
    [Tooltip("Crisp squeegee/rubbing sound played while wiping the screen.")]
    public AudioClip wipeRubSound;
    [Tooltip("Rewarding chime played when >= 90% of the screen is cleaned.")]
    public AudioClip cleanChimeSound;

    // High-res texture mask state
    private int activeResolution = 512;
    private Texture2D dustTexture;
    private Color32[] pixelBuffer;
    private float[] pixelDustLevels;
    private byte[] grainR;
    private byte[] grainG;
    private byte[] grainB;
    private float[] grainAlphaVar;

    private Material dustMaterial;
    private double totalDustSum = 0.0;
    private int dirtyPixelCount = 0;
    private float currentDustRatio = 0f;
    private float dirtySessionPeakDust = 0f;
    private bool hasWipedInCurrentSession = false;
    private float accumUpdateTimer = 0f;
    private float wipeSoundCooldown = 0f;
    private bool isPlayingPayoffFlash = false;
    private AudioSource wipeLoopAudioSource;
    private float lastWipeTime = -10f;

    public float CurrentDustRatio => currentDustRatio;
    public float CurrentCleanlinessRatio => 1f - currentDustRatio;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        EnsureOverlayAndTexture();
        EnsureWipeLoopAudioSource();
        InitializeDustState(initialDustRatio);

        if (wipeRubSound == null)
        {
#if UNITY_EDITOR
            wipeRubSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/cleaningscreen.mp3");
#endif
        }
        if (cleanChimeSound == null)
        {
#if UNITY_EDITOR
            cleanChimeSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/cleansparkle.mp3");
            if (cleanChimeSound == null) cleanChimeSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/9.mp3");
#endif
        }
    }

    private void EnsureWipeLoopAudioSource()
    {
        if (wipeLoopAudioSource == null)
        {
            Transform existing = transform.Find("TVScreenCleaner_WipeAudioLoop");
            if (existing != null)
            {
                wipeLoopAudioSource = existing.GetComponent<AudioSource>();
            }
            if (wipeLoopAudioSource == null)
            {
                GameObject loopObj = new GameObject("TVScreenCleaner_WipeAudioLoop");
                loopObj.transform.SetParent(transform, false);
                wipeLoopAudioSource = loopObj.AddComponent<AudioSource>();
            }
            wipeLoopAudioSource.playOnAwake = false;
            wipeLoopAudioSource.loop = true;
            wipeLoopAudioSource.volume = 0f;
            wipeLoopAudioSource.spatialBlend = 0f; // 2D clean audio
        }
    }

    private void EnsureOverlayAndTexture()
    {
        if (tvScreenRenderer == null)
        {
            GameObject screenObj = GameObject.Find("DVDSetup/Televisor_80/screen");
            if (screenObj != null) tvScreenRenderer = screenObj.GetComponent<MeshRenderer>();
        }

        if (tvScreenRenderer != null)
        {
            if (dustOverlayRenderer == null)
            {
                Transform existing = tvScreenRenderer.transform.parent.Find("TVDustOverlay");
                if (existing != null)
                {
                    dustOverlayRenderer = existing.GetComponent<MeshRenderer>();
                }
            }

            if (dustOverlayRenderer == null)
            {
                GameObject overlayObj = new GameObject("TVDustOverlay");
                overlayObj.transform.SetParent(tvScreenRenderer.transform.parent, false);
                dustOverlayRenderer = overlayObj.AddComponent<MeshRenderer>();
            }

            // Position the duplicate otherwise-invisible quad/mesh slightly in front of the TV screen glass
            Transform overlayTr = dustOverlayRenderer.transform;
            overlayTr.localPosition = tvScreenRenderer.transform.localPosition + new Vector3(0f, 0f, overlayFrontOffset);
            overlayTr.localRotation = tvScreenRenderer.transform.localRotation;
            overlayTr.localScale = tvScreenRenderer.transform.localScale;

            MeshFilter srcMf = tvScreenRenderer.GetComponent<MeshFilter>();
            if (srcMf != null && srcMf.sharedMesh != null)
            {
                MeshFilter dstMf = dustOverlayRenderer.GetComponent<MeshFilter>();
                if (dstMf == null) dstMf = dustOverlayRenderer.gameObject.AddComponent<MeshFilter>();
                dstMf.sharedMesh = CreatePlanarUVScreenMesh(srcMf.sharedMesh, tvScreenRenderer.transform, tvScreenRenderer.bounds);
            }

            dustOverlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dustOverlayRenderer.receiveShadows = false;
        }

        activeResolution = Mathf.Clamp(dustTextureResolution, 128, 1024);
        int totalPixels = activeResolution * activeResolution;

        dustTexture = new Texture2D(activeResolution, activeResolution, TextureFormat.RGBA32, false);
        dustTexture.name = "HighResTVDustMask";
        dustTexture.wrapMode = TextureWrapMode.Clamp;
        dustTexture.filterMode = FilterMode.Bilinear;

        pixelBuffer = new Color32[totalPixels];
        pixelDustLevels = new float[totalPixels];
        grainR = new byte[totalPixels];
        grainG = new byte[totalPixels];
        grainB = new byte[totalPixels];
        grainAlphaVar = new float[totalPixels];

        BuildHighResDustGrainPattern();

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        dustMaterial = new Material(shader);
        dustMaterial.mainTexture = dustTexture;
        if (dustMaterial.HasProperty("_BaseMap")) dustMaterial.SetTexture("_BaseMap", dustTexture);
        if (dustMaterial.HasProperty("_BaseColor")) dustMaterial.SetColor("_BaseColor", Color.white);
        if (dustMaterial.HasProperty("_Color")) dustMaterial.SetColor("_Color", Color.white);

        if (dustOverlayRenderer != null)
        {
            dustOverlayRenderer.sharedMaterial = dustMaterial;
        }
    }

    private void BuildHighResDustGrainPattern()
    {
        int res = activeResolution;
        int totalPixels = res * res;
        bool useCustomTex = customDustPatternTexture != null && customDustPatternTexture.isReadable;

        Random.State prevState = Random.state;
        Random.InitState(421337);

        float invRes = 1f / res;
        for (int y = 0; y < res; y++)
        {
            float v = y * invRes;
            int rowOffset = y * res;
            for (int x = 0; x < res; x++)
            {
                float u = x * invRes;
                int idx = rowOffset + x;

                if (useCustomTex)
                {
                    Color c = customDustPatternTexture.GetPixelBilinear(u, v) * dustTintColor;
                    grainR[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                    grainG[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                    grainB[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                    grainAlphaVar[idx] = Mathf.Clamp(c.a, 0.35f, 1f);
                }
                else
                {
                    // Multi-scale organic dust clouds + fine micro-fibers/motes so 512x512 looks crisp and natural
                    float cloudLarge = Mathf.PerlinNoise(u * 4.5f + 11.3f, v * 4.5f + 7.1f);
                    float cloudMed = Mathf.PerlinNoise(u * 14.0f + 33.7f, v * 14.0f + 19.4f);
                    float cloudFine = Mathf.PerlinNoise(u * 48.0f + 71.2f, v * 48.0f + 53.8f);
                    float microMote = Random.Range(0.78f, 1.04f);

                    // Subtle extra dust settling near screen edges/corners
                    float edgeDistX = Mathf.Min(u, 1f - u) * 2f;
                    float edgeDistY = Mathf.Min(v, 1f - v) * 2f;
                    float edgeVignette = 1f + (1f - Mathf.Clamp01(edgeDistX * edgeDistY)) * 0.18f;

                    float combined = (cloudLarge * 0.42f + cloudMed * 0.35f + cloudFine * 0.23f) * microMote;
                    if (Random.value < 0.018f)
                    {
                        // Tiny bright static dust speck
                        combined = Mathf.Min(1.15f, combined + Random.Range(0.22f, 0.42f));
                    }

                    float brightness = Mathf.Clamp(0.68f + combined * 0.38f, 0.45f, 1.08f);
                    grainR[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(dustTintColor.r * 235f * brightness), 60, 250);
                    grainG[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(dustTintColor.g * 235f * brightness), 55, 245);
                    grainB[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(dustTintColor.b * 235f * brightness), 50, 235);
                    grainAlphaVar[idx] = Mathf.Clamp((0.58f + combined * 0.48f) * edgeVignette, 0.38f, 1.0f);
                }
            }
        }

        Random.state = prevState;
    }

    public static Mesh CreatePlanarUVScreenMesh(Mesh sourceMesh, Transform screenTransform, Bounds worldBounds)
    {
        if (sourceMesh == null || !sourceMesh.isReadable) return sourceMesh;

        Mesh m = Instantiate(sourceMesh);
        m.name = "TVScreen_PlanarUV";
        Vector3[] verts = m.vertices;
        Vector2[] uvs = new Vector2[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 wp = screenTransform.TransformPoint(verts[i]);
            float u = Mathf.InverseLerp(worldBounds.min.x, worldBounds.max.x, wp.x);
            float v = Mathf.InverseLerp(worldBounds.min.y, worldBounds.max.y, wp.y);
            uvs[i] = new Vector2(u, v);
        }
        m.uv = uvs;
        return m;
    }

    private void InitializeDustState(float startRatio)
    {
        startRatio = Mathf.Clamp01(startRatio);
        int totalPixels = activeResolution * activeResolution;
        for (int i = 0; i < totalPixels; i++)
        {
            pixelDustLevels[i] = startRatio;
        }
        dirtySessionPeakDust = startRatio;
        RebuildFullTextureAndStats();
    }

    void Update()
    {
        if (PauseManager.IsGamePaused || DVDLogoBouncer.GlobalStaticHalt || isPlayingPayoffFlash)
        {
            if (wipeLoopAudioSource != null && wipeLoopAudioSource.isPlaying)
            {
                wipeLoopAudioSource.volume = Mathf.MoveTowards(wipeLoopAudioSource.volume, 0f, 8f * Time.deltaTime);
                if (wipeLoopAudioSource.volume <= 0.005f) wipeLoopAudioSource.Stop();
            }
            return;
        }

        // Smoothly loop the cleaning sound while scrubbing and fade it out quickly when stopping
        bool isWipingNow = (Time.time - lastWipeTime < 0.20f) && (TVFocusController.Instance != null && TVFocusController.Instance.IsInFocusMode);
        float targetVol = isWipingNow ? 0.70f : 0f;

        if (wipeLoopAudioSource != null)
        {
            if (isWipingNow && !wipeLoopAudioSource.isPlaying && wipeRubSound != null)
            {
                wipeLoopAudioSource.clip = wipeRubSound;
                wipeLoopAudioSource.pitch = 1.0f;
                wipeLoopAudioSource.Play();
            }

            if (wipeLoopAudioSource.isPlaying)
            {
                float fadeSpeed = isWipingNow ? 12f : 5.0f; // Quick fade in (0.08s), smooth quick fade out (~0.2s)
                wipeLoopAudioSource.volume = Mathf.MoveTowards(wipeLoopAudioSource.volume, targetVol, fadeSpeed * Time.deltaTime);
                if (!isWipingNow && wipeLoopAudioSource.volume <= 0.005f)
                {
                    wipeLoopAudioSource.Stop();
                    wipeLoopAudioSource.volume = 0f;
                }
            }
        }

        // Accumulate dust gradually over time
        if (secondsToFullDust > 0.1f && currentDustRatio < 1f)
        {
            accumUpdateTimer += Time.deltaTime;
            if (accumUpdateTimer >= 0.65f)
            {
                float deltaDust = accumUpdateTimer / secondsToFullDust;
                accumUpdateTimer = 0f;
                AddUniformDust(deltaDust);
            }
        }
    }

    private void AddUniformDust(float delta)
    {
        if (pixelDustLevels == null) return;
        int totalPixels = pixelDustLevels.Length;
        for (int i = 0; i < totalPixels; i++)
        {
            pixelDustLevels[i] = Mathf.Clamp01(pixelDustLevels[i] + delta);
        }
        RebuildFullTextureAndStats();
        if (currentDustRatio > dirtySessionPeakDust)
        {
            dirtySessionPeakDust = currentDustRatio;
        }
    }

    /// <summary>
    /// Erases dust along a UV stroke segment when the player wipes the screen with the towel.
    /// Uses fast bounding-box pixel updates so 512x512+ wiping is instant.
    /// </summary>
    public void WipeStrokeUV(Vector2 fromUV, Vector2 toUV)
    {
        if (pixelDustLevels == null || isPlayingPayoffFlash) return;

        float dist = Vector2.Distance(fromUV, toUV);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (wipeBrushRadiusUV * 0.28f)));
        bool erasedAny = false;

        int minModX = activeResolution;
        int maxModX = -1;
        int minModY = activeResolution;
        int maxModY = -1;

        for (int s = 0; s <= steps; s++)
        {
            float t = steps == 0 ? 1f : (float)s / steps;
            Vector2 uv = Vector2.Lerp(fromUV, toUV, t);
            if (EraseCircleAtUV(uv, wipeBrushRadiusUV, ref minModX, ref maxModX, ref minModY, ref maxModY))
            {
                erasedAny = true;
            }
        }

        if (erasedAny)
        {
            hasWipedInCurrentSession = true;
            PlayWipeRubAudio();
            ApplyModifiedPixelsAndStats(minModX, maxModX, minModY, maxModY);
            CheckCleanPayoffCondition();
        }
        else if (dist > 0.004f)
        {
            PlayWipeRubAudio();
        }
    }

    private bool EraseCircleAtUV(
        Vector2 uv,
        float radiusUV,
        ref int minModX,
        ref int maxModX,
        ref int minModY,
        ref int maxModY)
    {
        int centerX = Mathf.RoundToInt(uv.x * (activeResolution - 1));
        int centerY = Mathf.RoundToInt(uv.y * (activeResolution - 1));
        int radiusPx = Mathf.Max(3, Mathf.RoundToInt(radiusUV * activeResolution));
        int rSq = radiusPx * radiusPx;
        float invRadiusPx = 1f / radiusPx;

        int minX = Mathf.Max(0, centerX - radiusPx);
        int maxX = Mathf.Min(activeResolution - 1, centerX + radiusPx);
        int minY = Mathf.Max(0, centerY - radiusPx);
        int maxY = Mathf.Min(activeResolution - 1, centerY + radiusPx);

        bool modified = false;
        float featherStart = Mathf.Clamp(1f - wipeBrushFeather, 0.05f, 0.9f);

        for (int y = minY; y <= maxY; y++)
        {
            int dy = y - centerY;
            int rowOffset = y * activeResolution;
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - centerX;
                int dSq = dx * dx + dy * dy;
                if (dSq <= rSq)
                {
                    int idx = rowOffset + x;
                    float oldDust = pixelDustLevels[idx];
                    if (oldDust > 0.001f)
                    {
                        float normDist = Mathf.Sqrt(dSq) * invRadiusPx;
                        float strength = normDist <= featherStart
                            ? 1f
                            : 1f - Mathf.SmoothStep(0f, 1f, (normDist - featherStart) / (1f - featherStart));

                        float newDust = Mathf.Max(0f, oldDust - strength * 0.90f);
                        if (newDust < 0.05f) newDust = 0f;

                        if (newDust != oldDust)
                        {
                            pixelDustLevels[idx] = newDust;
                            totalDustSum -= (oldDust - newDust);
                            if (oldDust > 0.05f && newDust <= 0.05f)
                            {
                                dirtyPixelCount = Mathf.Max(0, dirtyPixelCount - 1);
                            }

                            byte alpha = (byte)Mathf.Clamp(
                                Mathf.RoundToInt(newDust * grainAlphaVar[idx] * maxDustVisualAlpha * 255f),
                                0,
                                255);
                            pixelBuffer[idx] = new Color32(grainR[idx], grainG[idx], grainB[idx], alpha);
                            modified = true;
                        }
                    }
                }
            }
        }

        if (modified)
        {
            if (minX < minModX) minModX = minX;
            if (maxX > maxModX) maxModX = maxX;
            if (minY < minModY) minModY = minY;
            if (maxY > maxModY) maxModY = maxY;
        }
        return modified;
    }

    private void ApplyModifiedPixelsAndStats(int minX, int maxX, int minY, int maxY)
    {
        if (dustTexture == null || pixelBuffer == null) return;

        dustTexture.SetPixels32(pixelBuffer);
        dustTexture.Apply(false);

        int totalPixels = pixelDustLevels.Length;
        float meanDust = Mathf.Clamp01((float)(totalDustSum / totalPixels));
        float coverage = (float)dirtyPixelCount / totalPixels;
        currentDustRatio = Mathf.Clamp01(meanDust * 0.5f + coverage * 0.5f);

        DVDLogoBouncer.GlobalDustSpeedMultiplier = Mathf.Lerp(1.0f, minSpeedMultiplierAtFullDust, currentDustRatio);
    }

    private void PlayWipeRubAudio()
    {
        lastWipeTime = Time.time;
    }

    private void RebuildFullTextureAndStats()
    {
        if (pixelDustLevels == null || dustTexture == null) return;

        int totalPixels = pixelDustLevels.Length;
        double sumDust = 0.0;
        int dirtyCount = 0;

        for (int i = 0; i < totalPixels; i++)
        {
            float d = pixelDustLevels[i];
            sumDust += d;
            if (d > 0.05f) dirtyCount++;

            byte alpha = (byte)Mathf.Clamp(
                Mathf.RoundToInt(d * grainAlphaVar[i] * maxDustVisualAlpha * 255f),
                0,
                255);
            pixelBuffer[i] = new Color32(grainR[i], grainG[i], grainB[i], alpha);
        }

        totalDustSum = sumDust;
        dirtyPixelCount = dirtyCount;

        dustTexture.SetPixels32(pixelBuffer);
        dustTexture.Apply(false);

        float meanDust = (float)(sumDust / totalPixels);
        float coverage = (float)dirtyCount / totalPixels;
        currentDustRatio = Mathf.Clamp01(meanDust * 0.5f + coverage * 0.5f);

        // Update System 5 debuff on DVDLogoBouncer (down to minSpeedMultiplierAtFullDust at max dust)
        DVDLogoBouncer.GlobalDustSpeedMultiplier = Mathf.Lerp(1.0f, minSpeedMultiplierAtFullDust, currentDustRatio);
    }

    private void CheckCleanPayoffCondition()
    {
        if (!hasWipedInCurrentSession || dirtySessionPeakDust < 0.05f) return;

        // Clean payoff triggers once >= 90% of the screen is clean (or >= 90% of accumulated dust is wiped)
        float cleanFraction = 1f - currentDustRatio;
        float wipedFractionOfPeak = dirtySessionPeakDust > 0.001f ? (1f - (currentDustRatio / dirtySessionPeakDust)) : 1f;

        if (cleanFraction >= cleanPayoffThreshold || wipedFractionOfPeak >= cleanPayoffThreshold)
        {
            TriggerCleanPayoff();
        }
    }

    /// <summary>
    /// System 5 Clean Payoff: Clears all remaining dust, resets speeds to 100%,
    /// plays a rewarding chime, and triggers a clean screen sparkle/flash animation.
    /// </summary>
    public void TriggerCleanPayoff()
    {
        hasWipedInCurrentSession = false;
        dirtySessionPeakDust = 0f;

        int totalPixels = pixelDustLevels.Length;
        for (int i = 0; i < totalPixels; i++)
        {
            pixelDustLevels[i] = 0f;
        }
        RebuildFullTextureAndStats();
        DVDLogoBouncer.GlobalDustSpeedMultiplier = 1.0f;

        lastWipeTime = -10f;
        if (wipeLoopAudioSource != null)
        {
            wipeLoopAudioSource.volume = 0f;
            wipeLoopAudioSource.Stop();
        }

        if (wipeAudioSource != null && cleanChimeSound != null)
        {
            float prevPitch = wipeAudioSource.pitch;
            wipeAudioSource.pitch = 1.0f;
            wipeAudioSource.PlayOneShot(cleanChimeSound, 0.90f);
            wipeAudioSource.pitch = prevPitch;
        }

        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(PlayCleanSparkleFlashRoutine());
        }
    }

    private IEnumerator PlayCleanSparkleFlashRoutine()
    {
        isPlayingPayoffFlash = true;

        // Flash the screen overlay with a clean white-gold sheen that fades out smoothly (particles removed)
        float duration = 0.42f;
        float elapsed = 0f;
        int totalPixels = pixelBuffer.Length;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float flashAlpha = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.6f) * 0.50f;
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(flashAlpha * 255f), 0, 255);
            Color32 flashCol = new Color32(235, 250, 255, a);

            for (int i = 0; i < totalPixels; i++)
            {
                pixelBuffer[i] = flashCol;
            }
            dustTexture.SetPixels32(pixelBuffer);
            dustTexture.Apply(false);
            yield return null;
        }

        // Restore completely clear dust texture
        RebuildFullTextureAndStats();
        isPlayingPayoffFlash = false;
    }
}
