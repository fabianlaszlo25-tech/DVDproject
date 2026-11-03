using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// System 6: CRT Static Glitch & TV Slap (Percussive Maintenance).
/// Periodically glitches the TV into the user's static video (StreamingAssets/static.mp4) at full opacity,
/// halting all DVD bounces and money generation.
/// Allows the player (when hands-free) to click directly on the TV casing ("Hit TV") to slap it:
/// - Fixes static malfunction and restores normal DVD video/music operation.
/// - If slapped while working, checks if any DVD logo is in the top 1/5th (20%) of the screen and violently hammers it downward.
/// - Compensates for Televisor_80's off-center pivot so the slap squish is visually centered on the TV screen.
/// </summary>
public class TVScreenHazard : MonoBehaviour
{
    public static TVScreenHazard Instance { get; private set; }

    [Header("TV & Bouncer References")]
    [Tooltip("The physical TV casing transform (DVDSetup/Televisor_80) that jiggles on slap.")]
    public Transform tvCasingTransform;
    [Tooltip("The TV screen MeshRenderer (DVDSetup/Televisor_80/screen) used to center the slap squish.")]
    public MeshRenderer tvScreenRenderer;
    [Tooltip("Reference to the main DVDLogoBouncer.")]
    public DVDLogoBouncer bouncer;
    [Tooltip("InteractableObject on the TV casing labeled 'Hit TV'.")]
    public InteractableObject tvCasingInteractable;

    [Header("In-Screen Static Video (DVDSetup/Camera/bgvideo)")]
    [Tooltip("The VideoPlayer on DVDSetup/Camera/bgvideo that plays static.mp4 into SCREENTRANSFER.")]
    public VideoPlayer bgVideoPlayer;
    [Tooltip("The MeshRenderer on DVDSetup/Camera/bgvideo whose material alpha is boosted to 1.0 during static.")]
    public Renderer bgVideoRenderer;
    [Tooltip("Video file name inside Assets/StreamingAssets/ to blast at full opacity during static malfunction.")]
    public string staticVideoFileName = "static.mp4";
    [Tooltip("Alpha value of bgvideo during static malfunction (1.0 = full blast).")]
    [Range(0.5f, 1.0f)]
    public float staticVideoFullAlpha = 1.0f;
    [Tooltip("Normal background alpha of bgvideo when playing DVD background videos (default 0.2745).")]
    [Range(0.05f, 1.0f)]
    public float normalBgVideoAlpha = 0.27450982f;

    [Header("Static Malfunction Timing")]
    [Tooltip("Minimum interval in seconds between CRT static malfunctions (default 120s = 2 min).")]
    public float minMalfunctionInterval = 120f;
    [Tooltip("Maximum interval in seconds between CRT static malfunctions (default 240s = 4 min).")]
    public float maxMalfunctionInterval = 240f;

    [Header("Centered Slap Recoil Animation")]
    [Tooltip("Duration of the physical TV scale jiggle/recoil animation when slapped.")]
    public float slapJiggleDuration = 0.36f;
    [Tooltip("Intensity of the physical scale jiggle when the TV is slapped.")]
    public float slapJiggleIntensity = 0.14f;
    [Tooltip("Automatically compensates for Televisor_80's right-shifted pivot so the squish is centered on the screen.")]
    public bool centerSquishOnScreen = true;
    [Tooltip("Additional manual local offset (X, Y, Z) if you want to shift the slap squish center further.")]
    public Vector3 manualSquishCenterOffset = Vector3.zero;

    [Header("Audio")]
    public AudioSource slapAudioSource;
    public AudioSource staticLoopAudioSource;
    [Tooltip("Punchy impact sound played when the player slaps the TV casing.")]
    public AudioClip slapImpactSound;
    [Tooltip("Looping CRT static noise (static.mp3) played while the TV is malfunctioning.")]
    public AudioClip staticNoiseSound;

    private bool isStaticActive = false;
    private float malfunctionTimer = 150f;
    private Vector3 initialCasingLocalPos;
    private Vector3 initialCasingScale;
    private Quaternion initialCasingRotation;
    private Vector3 casingToScreenParentOffset;
    private Coroutine jiggleRoutine;

    // Saved bgvideo state before static malfunction
    private Vector3 savedBgVideoLocalPos;
    private bool hasSavedBgVideoState = false;
    private bool hasSavedBgQueue = false;
    private int savedBgRenderQueue = -1;

    public bool IsStaticActive => isStaticActive;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Secret Good Day: TV operates peacefully with serene video and no static glitches
        if (RunSaveManager.HasInstance && RunSaveManager.Instance.isGoodDayActive)
        {
            enabled = false;
            return;
        }

        if (tvCasingTransform == null)
        {
            GameObject tvObj = GameObject.Find("DVDSetup/Televisor_80");
            if (tvObj != null) tvCasingTransform = tvObj.transform;
            else tvCasingTransform = transform;
        }

        if (tvScreenRenderer == null && tvCasingTransform != null)
        {
            Transform scr = tvCasingTransform.Find("screen");
            if (scr != null) tvScreenRenderer = scr.GetComponent<MeshRenderer>();
        }

        // Remove legacy procedural static overlay if left over in scene
        if (tvCasingTransform != null)
        {
            Transform oldProceduralStatic = tvCasingTransform.Find("TVStaticMalfunctionOverlay");
            if (oldProceduralStatic != null)
            {
                Destroy(oldProceduralStatic.gameObject);
            }
        }

        if (bouncer == null)
        {
            bouncer = FindObjectOfType<DVDLogoBouncer>();
        }

        // Locate DVDSetup/Camera/bgvideo for the user's existing static.mp4 video
        if (bgVideoPlayer == null)
        {
            GameObject bgObj = GameObject.Find("DVDSetup/Camera/bgvideo");
            if (bgObj != null)
            {
                bgVideoPlayer = bgObj.GetComponent<VideoPlayer>();
                bgVideoRenderer = bgObj.GetComponent<Renderer>();
            }
        }
        else if (bgVideoRenderer == null)
        {
            bgVideoRenderer = bgVideoPlayer.GetComponent<Renderer>();
        }

        if (bgVideoPlayer != null && !hasSavedBgVideoState)
        {
            savedBgVideoLocalPos = bgVideoPlayer.transform.localPosition;
            hasSavedBgVideoState = true;

            if (bgVideoRenderer != null && bgVideoRenderer.sharedMaterial != null)
            {
                Material mat = bgVideoRenderer.sharedMaterial;
                if (mat.HasProperty("_BaseColor"))
                {
                    normalBgVideoAlpha = mat.GetColor("_BaseColor").a;
                }
                else if (mat.HasProperty("_Color"))
                {
                    normalBgVideoAlpha = mat.GetColor("_Color").a;
                }
            }
        }

        if (tvCasingTransform != null)
        {
            initialCasingLocalPos = tvCasingTransform.localPosition;
            initialCasingScale = tvCasingTransform.localScale;
            initialCasingRotation = tvCasingTransform.localRotation;
            CacheScreenCenterPivotOffset();

            if (tvCasingInteractable == null)
            {
                tvCasingInteractable = tvCasingTransform.GetComponent<InteractableObject>();
            }
            if (tvCasingInteractable != null)
            {
                tvCasingInteractable.hoverText = "Hit TV";
                tvCasingInteractable.onClick.RemoveListener(SlapTV);
                tvCasingInteractable.onClick.AddListener(SlapTV);
            }
        }

        if (slapImpactSound == null)
        {
#if UNITY_EDITOR
            slapImpactSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tvhit.mp3");
            if (slapImpactSound == null) slapImpactSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/6.mp3");
#endif
        }

        ScheduleNextMalfunction();
    }

    private void CacheScreenCenterPivotOffset()
    {
        if (tvCasingTransform == null) return;

        // Anchor the slap squish at the horizontal center of the TV screen and the BOTTOM base of the TV casing
        // so slapping the TV compresses straight down from the top (-Y) onto the desk without shifting sideways!
        Renderer casingRenderer = tvCasingTransform.GetComponent<Renderer>();
        float bottomWorldY = casingRenderer != null
            ? casingRenderer.bounds.min.y
            : (tvScreenRenderer != null ? tvScreenRenderer.bounds.min.y : tvCasingTransform.position.y);

        float centerWorldX = tvScreenRenderer != null
            ? tvScreenRenderer.bounds.center.x
            : (casingRenderer != null ? casingRenderer.bounds.center.x : tvCasingTransform.position.x - 0.1372f);

        float centerWorldZ = tvScreenRenderer != null
            ? tvScreenRenderer.bounds.center.z
            : tvCasingTransform.position.z;

        Vector3 targetAnchorWorld = new Vector3(centerWorldX, bottomWorldY, centerWorldZ);
        Vector3 worldDelta = targetAnchorWorld - tvCasingTransform.position;

        if (tvCasingTransform.parent != null)
        {
            casingToScreenParentOffset = tvCasingTransform.parent.InverseTransformVector(worldDelta);
        }
        else
        {
            casingToScreenParentOffset = worldDelta;
        }
    }

    void OnDisable()
    {
        if (isStaticActive)
        {
            DVDLogoBouncer.GlobalStaticHalt = false;
            SetBgVideoAlphaAndDepth(normalBgVideoAlpha, false);
        }
    }

    private void ScheduleNextMalfunction()
    {
        float min = Mathf.Max(15f, minMalfunctionInterval);
        float max = Mathf.Max(min + 5f, maxMalfunctionInterval);
        malfunctionTimer = Random.Range(min, max);
    }

    void Update()
    {
        if (PauseManager.IsGamePaused) return;

        if (!isStaticActive)
        {
            malfunctionTimer -= Time.deltaTime;
            if (malfunctionTimer <= 0f)
            {
                TriggerStaticMalfunction();
            }
        }
    }

    /// <summary>
    /// Glitches the TV into the existing static.mp4 video at full opacity (alpha = 1) in front of the DVD logos,
    /// halting all DVD bounces and money generation until the player slaps the TV.
    /// </summary>
    public void TriggerStaticMalfunction()
    {
        if (isStaticActive) return;
        isStaticActive = true;
        DVDLogoBouncer.GlobalStaticHalt = true;

        // Stop DVD music while static is blasting
        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.PauseDvdMusicForStatic();
        }

        // Blast static.mp4 on bgvideo with full opacity in front of the DVD logos
        SetBgVideoAlphaAndDepth(staticVideoFullAlpha, true);
        if (bgVideoPlayer != null && !string.IsNullOrEmpty(staticVideoFileName))
        {
            bgVideoPlayer.source = VideoSource.Url;
            bgVideoPlayer.url = Path.Combine(Application.streamingAssetsPath, staticVideoFileName);
            bgVideoPlayer.isLooping = true;
            bgVideoPlayer.Play();
        }

        if (staticLoopAudioSource != null && staticNoiseSound != null)
        {
            staticLoopAudioSource.clip = staticNoiseSound;
            staticLoopAudioSource.loop = true;
            staticLoopAudioSource.volume = 0.45f;
            if (!staticLoopAudioSource.isPlaying)
            {
                staticLoopAudioSource.Play();
            }
        }
    }

    private void SetBgVideoAlphaAndDepth(float alpha, bool bringInFrontOfLogos)
    {
        if (bgVideoPlayer != null)
        {
            if (!hasSavedBgVideoState)
            {
                savedBgVideoLocalPos = bgVideoPlayer.transform.localPosition;
                hasSavedBgVideoState = true;
            }

            if (bringInFrontOfLogos)
            {
                // DVDLogo is at local z ~ 1.096 in front of DVDSetup/Camera; z = 0.65 places static.mp4 in front of all logos/bumpers
                // Place static.mp4 right at the ortho camera's near plane so it covers EVERYTHING rendered in the TV
                // (logos, bumpers, guide lines, minigame tiles, reticle...). Nothing can end up in front of it.
                Camera tvCam = bgVideoPlayer.GetComponentInParent<Camera>();
                float frontZ = (tvCam != null ? tvCam.nearClipPlane : 0.3f) + 0.01f;
                bgVideoPlayer.transform.localPosition = new Vector3(
                    savedBgVideoLocalPos.x,
                    savedBgVideoLocalPos.y,
                    frontZ);
            }
            else
            {
                bgVideoPlayer.transform.localPosition = savedBgVideoLocalPos;
            }
        }

        if (bgVideoRenderer != null)
        {
            Material mat = Application.isPlaying ? bgVideoRenderer.material : bgVideoRenderer.sharedMaterial;
            if (mat != null)
            {
                // Minigame visuals (guide lines, ghost tiles...) use transparent queue 3050, which would draw on top
                // of the static regardless of depth. Push the static to the very end of the queue while active.
                if (!hasSavedBgQueue)
                {
                    savedBgRenderQueue = mat.renderQueue;
                    hasSavedBgQueue = true;
                }
                mat.renderQueue = bringInFrontOfLogos ? 4500 : savedBgRenderQueue;

                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = alpha;
                    mat.SetColor("_Color", c);
                }
            }
        }
    }

    /// <summary>
    /// Triggered when the player clicks directly on the TV casing ("Hit TV") while hands-free.
    /// Plays a punchy impact sound, triggers a centered physical TV scale jiggle/recoil animation,
    /// fixes static malfunction if broken, or hammers top-1/5th DVD logos downward if working.
    /// </summary>
    public void SlapTV()
    {
        // Enforce hands-free requirement (cannot slap TV while holding Remote or Towel in Focus Mode)
        if (TVFocusController.Instance != null && TVFocusController.Instance.IsInFocusMode)
        {
            return;
        }

        // 1. Play punchy slap impact sound
        if (slapAudioSource != null && slapImpactSound != null)
        {
            float prevPitch = slapAudioSource.pitch;
            slapAudioSource.pitch = Random.Range(0.92f, 1.08f);
            slapAudioSource.PlayOneShot(slapImpactSound, 1.0f);
            slapAudioSource.pitch = prevPitch;
        }

        // 2. Trigger centered physical TV scale jiggle/recoil animation
        if (tvCasingTransform != null && gameObject.activeInHierarchy)
        {
            if (jiggleRoutine != null) StopCoroutine(jiggleRoutine);
            jiggleRoutine = StartCoroutine(PlayTVSlapJiggleRoutine());
        }

        // 3. Fix Static Malfunction OR Execute Tactical Downward Hammer
        if (isStaticActive)
        {
            RestoreNormalOperation();
        }
        else if (bouncer != null)
        {
            Vector3 screenCenter = tvScreenRenderer != null ? tvScreenRenderer.bounds.center : tvCasingTransform.position;
            bouncer.TryApplyTacticalSlapHammer(screenCenter);
        }
    }

    private void RestoreNormalOperation()
    {
        isStaticActive = false;
        DVDLogoBouncer.GlobalStaticHalt = false;

        // Restore bgvideo alpha and Z depth behind the DVD logos
        SetBgVideoAlphaAndDepth(normalBgVideoAlpha, false);

        // Resume the equipped DVD's background video & music
        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.ResumeCurrentDvdVideoAndMusic();
        }

        if (staticLoopAudioSource != null && staticLoopAudioSource.isPlaying)
        {
            staticLoopAudioSource.Stop();
        }

        ScheduleNextMalfunction();
    }

    private IEnumerator PlayTVSlapJiggleRoutine()
    {
        // Reset to clean baseline before starting jiggle
        tvCasingTransform.localPosition = initialCasingLocalPos;
        tvCasingTransform.localScale = initialCasingScale;
        tvCasingTransform.localRotation = initialCasingRotation;

        CacheScreenCenterPivotOffset();
        Vector3 pivotOffset = (centerSquishOnScreen ? casingToScreenParentOffset : Vector3.zero) + manualSquishCenterOffset;

        float elapsed = 0f;
        while (elapsed < slapJiggleDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slapJiggleDuration);
            float damper = Mathf.Exp(-5.5f * t) * (1f - t);
            float wave = Mathf.Sin(t * Mathf.PI * 4.5f);

            // Top-down compression: squash vertically (Y) downward toward the desk and bulge slightly outward (X/Z)
            float squashY = 1f - slapJiggleIntensity * damper * wave;
            float stretchXZ = 1f + (slapJiggleIntensity * 0.45f) * damper * wave;

            tvCasingTransform.localScale = new Vector3(
                initialCasingScale.x * stretchXZ,
                initialCasingScale.y * squashY,
                initialCasingScale.z * stretchXZ
            );

            // Compensate localPosition around the bottom-center anchor + add a crisp downward (-Y) thump at impact
            float downwardThumpY = -0.025f * Mathf.Max(0f, wave) * damper;
            Vector3 scaleCompensation = new Vector3(
                pivotOffset.x * (1f - stretchXZ),
                pivotOffset.y * (1f - squashY) + downwardThumpY,
                pivotOffset.z * (1f - stretchXZ)
            );

            // Purely vertical/forward pitch recoil (zero Z sideways tilt so it never looks hit from the right side)
            float pitchX = 1.6f * damper * wave;
            Quaternion deltaRot = Quaternion.Euler(pitchX, 0f, 0f);

            tvCasingTransform.localPosition = initialCasingLocalPos + scaleCompensation;
            tvCasingTransform.localRotation = initialCasingRotation * deltaRot;
            yield return null;
        }

        tvCasingTransform.localPosition = initialCasingLocalPos;
        tvCasingTransform.localScale = initialCasingScale;
        tvCasingTransform.localRotation = initialCasingRotation;
        jiggleRoutine = null;
    }
}
