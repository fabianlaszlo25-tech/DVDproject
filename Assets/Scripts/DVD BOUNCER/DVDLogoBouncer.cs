using UnityEngine;
using UnityEngine.Events;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class DVDLogoBouncer : MonoBehaviour
{
    public static readonly List<DVDLogoBouncer> ActiveLogos = new List<DVDLogoBouncer>();

    [Header("References")]
    public Camera renderCamera;
    public Transform displayQuad;
    public RenderTexture tvRenderTexture;
    private Collider logoCollider;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip edgeHitSound;
    public AudioClip cornerHitSound;
    [Tooltip("Sound played when the logo is directly struck by the remote reticle.")]
    public AudioClip directHitSound;
    [Tooltip("Punchy pop sound played when two split child logos recombine.")]
    public AudioClip recombinePopSound;
    [Tooltip("Sound played periodically while shaking the logo with the Grab & Shake remote.")]
    public AudioClip grabShakeSound;

    [Header("Visual Bounce Settings")]
    [Tooltip("Drag the actual TV 3D model here. If left empty, the logo itself will bounce.")]
    public Transform tvTransform;
    public float edgeBounceMultiplier = 1.05f;
    public float cornerBounceMultiplier = 1.15f;
    public float visualBounceDuration = 0.2f;
    private Vector3 originalScale;
    private Vector3 originalTvLocalPos;
    private Coroutine activeBounceCoroutine;

    [Header("Resolution Settings")]
    public int baseVerticalResolution = 1080;
    [Range(0.1f, 4f)] public float resolutionScale = 1f;

    [Header("Movement Settings")]
    public float baseSpeed = 2.5f;
    public float stateMultiplier = 1f;
    public Vector2 manualOffset = new Vector2(0f, 0f);

    [Header("Stacking Boost Settings")]
    public float speedAddedPerClick = 10f;
    public float boostRampUpTime = 0.2f;
    public float boostRampDownTime = 1.0f;

    private class BoostInstance { public float timer; }
    private List<BoostInstance> activeBoosts = new List<BoostInstance>();

    [Header("Anti-Loop & Bounce Settings")]
    public float bounceVariation = 0.15f;
    public float minAxisVelocity = 0.2f;

    [Header("Economy & UI")]
    [Tooltip("Starting money for a new game / fresh day.")]
    public float startingMoney = 25f;
    public decimal edgeReward = 0.01m;
    public decimal cornerReward = 0.05m;
    public decimal currentMoney = 0m;
    public decimal allTimeMoneyEarned = 0m;
    public float moneyMultiplier = 1f;
    [HideInInspector] public float billSpeedMultiplier = 1f;

    public decimal AllTimeMoneyEarned => RunSaveManager.HasInstance ? RunSaveManager.Instance.TotalMoneyEarned : allTimeMoneyEarned;

    public TMP_Text totalMoneyText;
    public Transform popupCanvasParent;
    public GameObject moneyPopupPrefab;
    public GameObject negativeMoneyPopupPrefab;

    [Header("Hit Effects")]
    public GameObject edgeEffectTemplate;
    public GameObject cornerEffectTemplate;
    public float effectLifetime = 1.5f;

    [Header("System 2: Direct Hit & Impulse Physics")]
    [Tooltip("Initial bonus speed added when the logo is struck directly by the remote.")]
    public float directHitImpulseSpeed = 14f;
    [Tooltip("Fraction of impulse bonus speed retained after each wall bounce (decelerates back to base speed).")]
    [Range(0.2f, 0.9f)]
    public float bounceImpulseRetention = 0.62f;
    [Tooltip("Gentle continuous drag applied to impulse speed between bounces.")]
    public float impulseContinuousDrag = 1.5f;
    [Tooltip("Multiplier of edgeReward awarded on a direct remote hit.")]
    public float directHitBonusMultiplier = 1.5f;

    [Header("System 2: Directional Squash & Stretch (Fine-Tune Here)")]
    [Tooltip("Total duration in seconds for the squash, stretch overshoot, and spring settle.")]
    public float squashStretchDuration = 0.55f;
    [Tooltip("Maximum deformation intensity (higher = more pronounced squish and stretch).")]
    [Range(0.05f, 0.65f)]
    public float maxSquashAmount = 0.24f;
    [Tooltip("Number of spring oscillation cycles over the duration (e.g. 2.0 = squash -> stretch -> squash -> settle).")]
    [Range(1.0f, 4.0f)]
    public float squashOscillations = 2.0f;
    [Tooltip("Exponential damping of the spring (lower = bouncier/longer wobble, higher = settles faster).")]
    [Range(1.0f, 6.0f)]
    public float squashDamping = 2.6f;
    [Tooltip("Relative squash intensity on regular wall bounces compared to direct remote hits.")]
    [Range(0.3f, 1.2f)]
    public float wallBounceSquashMultiplier = 0.78f;

    [Header("System 2: Particle Hook (Rendered Inside TV Screen)")]
    [Tooltip("Invoked with the in-screen ortho hit coordinate whenever the logo is struck.")]
    public UnityEvent<Vector3> onLogoHit = new UnityEvent<Vector3>();
    [Tooltip("Optional custom particle prefab spawned inside the TV camera space at the hit coordinate.")]
    public GameObject hitParticlePrefab;
    [Tooltip("Scale multiplier applied to hitParticlePrefab when spawned inside the TV camera.")]
    public float hitParticleScale = 1.0f;
    [Tooltip("Number of sparks emitted by the built-in procedural hit burst (used when hitParticlePrefab is empty).")]
    public int proceduralSparkCount = 18;
    [Tooltip("Speed of procedural sparks in TV ortho space.")]
    public float proceduralSparkSpeed = 4.5f;
    [Tooltip("Size of procedural sparks in TV ortho space.")]
    public float proceduralSparkSize = 0.18f;
    public Color sparkColorPrimary = new Color(1f, 0.92f, 0.35f, 1f);
    public Color sparkColorSecondary = new Color(0.3f, 0.95f, 1f, 1f);

    [Header("System 4: Mitosis / Splitter Remote (Tier 2)")]
    [Tooltip("Maximum number of times a logo can split (Generation 0 -> 1 -> 2).")]
    public int maxSplitGenerations = 2;
    [Tooltip("Scale multiplier for Generation 1 child logos.")]
    public float gen1ScaleMultiplier = 0.68f;
    [Tooltip("Scale multiplier for Generation 2 child logos (minimum scale limit).")]
    public float gen2ScaleMultiplier = 0.48f;
    [Tooltip("Divergence angle in degrees when splitting into two child logos.")]
    public float splitDivergenceAngle = 25f;
    [Tooltip("Grace period in seconds after splitting before same-generation children can recombine.")]
    public float splitRecombineGracePeriod = 0.65f;

    [Header("System 4: Grab & Shake Remote (Tier 3)")]
    [Tooltip("Minimum cursor shake speed (in ortho units/sec) required to earn shake cash ticks.")]
    public float shakeEarnSpeedThreshold = 11f;
    [Tooltip("Minimum interval in seconds between cash payouts while rapidly shaking the grabbed logo.")]
    public float shakeMoneyInterval = 0.16f;
    [Tooltip("Multiplier of edgeReward earned per shake tick.")]
    public float shakeBonusMultiplier = 1.0f;
    [Tooltip("How much of the cursor release velocity is transferred to the logo when letting go.")]
    public float grabThrowSpeedMultiplier = 0.65f;
    [Tooltip("Maximum bonus impulse speed when flinging the logo upon release.")]
    public float maxGrabThrowImpulse = 22f;

    // Runtime state for child/split logos
    [HideInInspector] public bool isChildLogo = false;
    [HideInInspector] public DVDLogoBouncer parentBouncer;
    [HideInInspector] public int splitGeneration = 0;

    // Runtime state for Grab & Shake
    [HideInInspector] public bool isGrabbedByPlayer = false;
    private Vector2 smoothedGrabVelocity = Vector2.zero;
    private float shakeMoneyCooldown = 0f;
    private float grabWallBounceCooldown = 0f;
    private Coroutine activeDamageFlashCoroutine;

    // System 5 & System 6 state hooks
    private static float globalDustSpeedMultiplier = 1f;
    private static bool globalStaticHalt = false;

    public static float GlobalDustSpeedMultiplier
    {
        get => globalDustSpeedMultiplier;
        set => globalDustSpeedMultiplier = Mathf.Clamp(value, 0.5f, 1f);
    }

    public static bool GlobalStaticHalt
    {
        get => globalStaticHalt;
        set => globalStaticHalt = value;
    }

    public Camera RenderCamera => renderCamera;
    public DVDLogoBouncer MainBouncer => (isChildLogo && parentBouncer != null) ? parentBouncer : this;

    private Vector2 direction;
    private int lastWidth = -1;
    private int lastHeight = -1;

    private float camMinX, camMaxX, camMinY, camMaxY;
    private float baseHalfWidth, baseHalfHeight;
    private Vector3 baseCenterOffsetFromTransform;
    private bool boundsInitialized = false;
    private Vector3 initialLocalScale;

    // Directional Squash & Stretch runtime state (mesh vertex deformation + child transform fallback)
    private float currentImpulseBonusSpeed = 0f;
    private float squashTimer = -1f;
    private float activeSquashDuration = 0.55f;
    private float activeSquashIntensity = 0.45f;
    private Vector2 squashNormal = Vector2.right;
    private float recombineBounceTimer = -1f;
    private float splitGraceTimer = 0f;

    public class DeformableMeshPart
    {
        public Transform partTransform;
        public MeshFilter meshFilter;
        public Mesh meshInstance;
        public Vector3[] originalLocalVerts;
        public Vector3[] rootSpaceVerts;
        public Vector3[] deformedLocalVerts;
        public Matrix4x4 rootToPartMatrix;
    }

    private readonly List<DeformableMeshPart> deformableParts = new List<DeformableMeshPart>();
    private Vector3 rootDeformCenter = new Vector3(0.394f, 0.155f, 0f);
    private Transform visualChildTransform;
    private Vector3 visualChildInitialLocalPos;
    private Vector3 visualChildInitialLocalScale;
    private bool canDeformVertices = false;
    private bool wasDeformingLastFrame = false;
    private bool deformableMeshInitialized = false;

    private static Texture2D cachedCircleParticleTexture;

    void Awake()
    {
        initialLocalScale = transform.localScale;
        logoCollider = GetComponent<Collider>();
        if (!ActiveLogos.Contains(this))
        {
            ActiveLogos.Add(this);
        }

        // Capture pristine, un-deformed mesh vertices immediately in Awake on the root logo
        // before any Update frame can ever deform them.
        if (!isChildLogo)
        {
            InitializeDeformableVisualMesh();
        }
    }

    void OnEnable()
    {
        if (!ActiveLogos.Contains(this))
        {
            ActiveLogos.Add(this);
        }
    }

    void OnDisable()
    {
        ActiveLogos.Remove(this);
    }

    void OnDestroy()
    {
        ActiveLogos.Remove(this);
        for (int i = 0; i < deformableParts.Count; i++)
        {
            if (deformableParts[i] != null && deformableParts[i].meshInstance != null)
            {
                Destroy(deformableParts[i].meshInstance);
            }
        }
        deformableParts.Clear();
    }

    void Start()
    {
        if (renderCamera == null) renderCamera = Camera.main;
        if (logoCollider == null) logoCollider = GetComponent<Collider>();

        if (tvTransform != null)
        {
            originalScale = tvTransform.localScale;
            originalTvLocalPos = tvTransform.localPosition;
        }
        else
        {
            originalScale = transform.localScale;
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        if (direction.sqrMagnitude < 0.01f)
        {
            direction = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
            if (Mathf.Abs(direction.x) < minAxisVelocity) direction.x = minAxisVelocity;
            if (Mathf.Abs(direction.y) < minAxisVelocity) direction.y = minAxisVelocity;
            direction.Normalize();
        }

        if (!isChildLogo)
        {
            if (!RunSaveManager.HasInstance || !RunSaveManager.Instance.HasSavedBouncerState)
            {
                currentMoney = (decimal)startingMoney;
            }
            SyncCameraAndTextureAspect();
            UpdateMoneyUI();
        }

        InitializeLogoBounds();
        InitializeDeformableVisualMesh();
    }

    /// <summary>
    /// Collects ALL visual mesh parts of the DVD logo (e.g., both 'dvd' letters and 'dvd/Torus' ring)
    /// and precomputes their vertices in the parent/collider (DVDLogo) local space around BoxCollider.center
    /// so every part of the logo deforms together as one unified fluid shape.
    /// </summary>
    private void InitializeDeformableVisualMesh()
    {
        if (deformableMeshInitialized && deformableParts.Count > 0)
        {
            return;
        }

        // If this is a child logo whose Start() ran before InitializeChildDeformableMeshesFromParent,
        // copy pristine rest vertices from MainBouncer rather than reading potentially mid-deform sharedMesh!
        if (isChildLogo && parentBouncer != null && parentBouncer != this && parentBouncer.deformableParts.Count > 0)
        {
            InitializeChildDeformableMeshesFromParent(parentBouncer);
            return;
        }

        for (int i = 0; i < deformableParts.Count; i++)
        {
            if (deformableParts[i] != null && deformableParts[i].meshInstance != null)
            {
                Destroy(deformableParts[i].meshInstance);
            }
        }
        deformableParts.Clear();

        // Use the parent BoxCollider.center as the unified deformation pivot reference in DVDLogo local space
        if (logoCollider is BoxCollider boxCol)
        {
            rootDeformCenter = boxCol.center;
        }
        else if (logoCollider != null)
        {
            rootDeformCenter = transform.InverseTransformPoint(logoCollider.bounds.center);
        }

        Transform dvdChild = transform.Find("dvd");
        if (dvdChild != null)
        {
            visualChildTransform = dvdChild;
            visualChildInitialLocalPos = dvdChild.localPosition;
            visualChildInitialLocalScale = dvdChild.localScale;
        }

        // Gather all active MeshFilters under DVDLogo (both 'dvd' and 'dvd/Torus', excluding disabled edge/corner templates)
        MeshFilter[] allMeshFilters = GetComponentsInChildren<MeshFilter>(false);
        for (int i = 0; i < allMeshFilters.Length; i++)
        {
            MeshFilter mf = allMeshFilters[i];
            if (mf == null || mf.sharedMesh == null) continue;

            GameObject go = mf.gameObject;
            if (go == edgeEffectTemplate || go == cornerEffectTemplate) continue;
            string lowerName = go.name.ToLowerInvariant();
            if (lowerName == "edge" || lowerName == "corner") continue;

            if (visualChildTransform == null && mf.transform != transform)
            {
                visualChildTransform = mf.transform;
                visualChildInitialLocalPos = visualChildTransform.localPosition;
                visualChildInitialLocalScale = visualChildTransform.localScale;
            }

            if (mf.sharedMesh.isReadable)
            {
                Mesh inst = Instantiate(mf.sharedMesh);
                inst.name = mf.sharedMesh.name + "_UnifiedDeform";
                inst.MarkDynamic();

                Vector3[] origVerts = inst.vertices;
                Vector3[] rootVerts = new Vector3[origVerts.Length];
                Vector3[] defVerts = new Vector3[origVerts.Length];

                // Matrix converting from this sub-mesh's local space into root DVDLogo (parent/collider) local space
                Matrix4x4 partToRoot = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                // Inverse matrix converting deformed root-space vertices back into this sub-mesh's local space
                Matrix4x4 rootToPart = mf.transform.worldToLocalMatrix * transform.localToWorldMatrix;

                for (int v = 0; v < origVerts.Length; v++)
                {
                    rootVerts[v] = partToRoot.MultiplyPoint3x4(origVerts[v]);
                    defVerts[v] = origVerts[v];
                }

                mf.mesh = inst;

                deformableParts.Add(new DeformableMeshPart
                {
                    partTransform = mf.transform,
                    meshFilter = mf,
                    meshInstance = inst,
                    originalLocalVerts = origVerts,
                    rootSpaceVerts = rootVerts,
                    deformedLocalVerts = defVerts,
                    rootToPartMatrix = rootToPart
                });
            }
        }

        canDeformVertices = deformableParts.Count > 0;
        wasDeformingLastFrame = false;
        deformableMeshInitialized = deformableParts.Count > 0;
    }

    /// <summary>
    /// Initializes a newly split child logo's deformable meshes directly from the parent's pristine
    /// un-deformed vertex arrays so a split logo can NEVER inherit or freeze mid-squash vertices.
    /// </summary>
    private void InitializeChildDeformableMeshesFromParent(DVDLogoBouncer sourceBouncer)
    {
        if (sourceBouncer == null) return;
        sourceBouncer.InitializeDeformableVisualMesh();

        for (int i = 0; i < deformableParts.Count; i++)
        {
            if (deformableParts[i] != null && deformableParts[i].meshInstance != null &&
                !sourceBouncer.OwnsMeshInstance(deformableParts[i].meshInstance))
            {
                Destroy(deformableParts[i].meshInstance);
            }
        }
        deformableParts.Clear();

        rootDeformCenter = sourceBouncer.rootDeformCenter;

        Transform dvdChild = transform.Find("dvd");
        if (dvdChild != null)
        {
            visualChildTransform = dvdChild;
            visualChildInitialLocalPos = sourceBouncer.visualChildTransform != null
                ? sourceBouncer.visualChildInitialLocalPos
                : dvdChild.localPosition;
            visualChildInitialLocalScale = sourceBouncer.visualChildTransform != null
                ? sourceBouncer.visualChildInitialLocalScale
                : dvdChild.localScale;
            dvdChild.localPosition = visualChildInitialLocalPos;
            dvdChild.localScale = visualChildInitialLocalScale;
        }

        MeshFilter[] childFilters = GetComponentsInChildren<MeshFilter>(false);
        int sourceIdx = 0;
        for (int i = 0; i < childFilters.Length && sourceIdx < sourceBouncer.deformableParts.Count; i++)
        {
            MeshFilter mf = childFilters[i];
            if (mf == null || mf.sharedMesh == null) continue;

            GameObject go = mf.gameObject;
            if (go == edgeEffectTemplate || go == cornerEffectTemplate) continue;
            string lowerName = go.name.ToLowerInvariant();
            if (lowerName == "edge" || lowerName == "corner") continue;

            DeformableMeshPart srcPart = sourceBouncer.deformableParts[sourceIdx];
            sourceIdx++;

            if (srcPart == null || srcPart.meshInstance == null || srcPart.originalLocalVerts == null) continue;

            Mesh childInst = Instantiate(srcPart.meshInstance);
            childInst.name = srcPart.meshInstance.name + "_Child";
            childInst.MarkDynamic();

            int vertCount = srcPart.originalLocalVerts.Length;
            Vector3[] origCopy = new Vector3[vertCount];
            Vector3[] rootCopy = new Vector3[vertCount];
            Vector3[] defCopy = new Vector3[vertCount];

            System.Array.Copy(srcPart.originalLocalVerts, origCopy, vertCount);
            System.Array.Copy(srcPart.rootSpaceVerts, rootCopy, vertCount);
            System.Array.Copy(srcPart.originalLocalVerts, defCopy, vertCount);

            childInst.vertices = defCopy;
            childInst.RecalculateBounds();
            mf.mesh = childInst;

            deformableParts.Add(new DeformableMeshPart
            {
                partTransform = mf.transform,
                meshFilter = mf,
                meshInstance = childInst,
                originalLocalVerts = origCopy,
                rootSpaceVerts = rootCopy,
                deformedLocalVerts = defCopy,
                rootToPartMatrix = srcPart.rootToPartMatrix
            });
        }

        canDeformVertices = deformableParts.Count > 0;
        wasDeformingLastFrame = false;
        deformableMeshInitialized = deformableParts.Count > 0;
    }

    private bool OwnsMeshInstance(Mesh m)
    {
        if (m == null) return false;
        for (int i = 0; i < deformableParts.Count; i++)
        {
            if (deformableParts[i] != null && deformableParts[i].meshInstance == m) return true;
        }
        return false;
    }

    /// <summary>
    /// Immediately snaps all deformable mesh vertices and fallback transforms back to their pristine rest state.
    /// </summary>
    public void ResetDeformationImmediate()
    {
        squashTimer = -1f;
        wasDeformingLastFrame = false;

        for (int p = 0; p < deformableParts.Count; p++)
        {
            DeformableMeshPart part = deformableParts[p];
            if (part == null || part.meshInstance == null || part.originalLocalVerts == null) continue;
            System.Array.Copy(part.originalLocalVerts, part.deformedLocalVerts, part.originalLocalVerts.Length);
            part.meshInstance.vertices = part.deformedLocalVerts;
            part.meshInstance.RecalculateBounds();
        }

        if (visualChildTransform != null)
        {
            visualChildTransform.localPosition = visualChildInitialLocalPos;
            visualChildTransform.localScale = visualChildInitialLocalScale;
        }
    }

    public List<DeformableMeshPart> GetDeformableParts()
    {
        InitializeDeformableVisualMesh();
        return deformableParts;
    }

    public Vector3 GetRootDeformCenter() => rootDeformCenter;
    public Vector3 GetInitialLocalScale() => initialLocalScale;

    private void InitializeLogoBounds()
    {
        if (renderCamera == null || logoCollider == null) return;

        if (!boundsInitialized)
        {
            float genFactor = GetGenerationScaleFactor(splitGeneration);
            transform.localScale = initialLocalScale;
            Physics.SyncTransforms();

            if (logoCollider is BoxCollider boxCol)
            {
                baseHalfWidth = Mathf.Abs(boxCol.size.x * initialLocalScale.x) * 0.5f;
                baseHalfHeight = Mathf.Abs(boxCol.size.y * initialLocalScale.y) * 0.5f;
                baseCenterOffsetFromTransform = new Vector3(
                    boxCol.center.x * initialLocalScale.x,
                    boxCol.center.y * initialLocalScale.y,
                    boxCol.center.z * initialLocalScale.z);
            }
            else
            {
                Bounds b = logoCollider.bounds;
                baseHalfWidth = b.extents.x;
                baseHalfHeight = b.extents.y;
                baseCenterOffsetFromTransform = b.center - transform.position;
            }
            boundsInitialized = true;

            transform.localScale = initialLocalScale * genFactor;
            Physics.SyncTransforms();
        }

        UpdateCameraBounds();
    }

    private void UpdateCameraBounds()
    {
        if (renderCamera == null || !renderCamera.orthographic) return;
        Vector3 camPos = renderCamera.transform.position;
        float halfHeight = renderCamera.orthographicSize;
        float halfWidth = halfHeight * renderCamera.aspect;

        camMaxX = camPos.x + halfWidth - manualOffset.x;
        camMinX = camPos.x - halfWidth + manualOffset.x;
        camMaxY = camPos.y + halfHeight - manualOffset.y;
        camMinY = camPos.y - halfHeight + manualOffset.y;
    }

    public float GetGenerationScaleFactor(int gen)
    {
        if (gen <= 0) return 1f;
        if (gen == 1) return gen1ScaleMultiplier;
        return gen2ScaleMultiplier;
    }

    public float HalfWidth => baseHalfWidth * GetGenerationScaleFactor(splitGeneration);
    public float HalfHeight => baseHalfHeight * GetGenerationScaleFactor(splitGeneration);

    /// <summary>
    /// Returns the un-deformed physics center of the DVD logo in renderCamera world space.
    /// </summary>
    public Vector3 GetLogoCenter()
    {
        if (!boundsInitialized && logoCollider != null)
        {
            return logoCollider.bounds.center;
        }
        float scaleFactor = GetGenerationScaleFactor(splitGeneration);
        return transform.position + baseCenterOffsetFromTransform * scaleFactor;
    }

    /// <summary>
    /// Moves the logo's transform so its physics center is placed at the specified ortho world position,
    /// clamped cleanly inside the camera's bouncing bounds.
    /// </summary>
    public Vector3 SetLogoCenterClamped(Vector3 desiredCenterWorld)
    {
        UpdateCameraBounds();
        float hw = HalfWidth;
        float hh = HalfHeight;

        float clampedX = Mathf.Clamp(desiredCenterWorld.x, camMinX + hw, camMaxX - hw);
        float clampedY = Mathf.Clamp(desiredCenterWorld.y, camMinY + hh, camMaxY - hh);

        float scaleFactor = GetGenerationScaleFactor(splitGeneration);
        Vector3 offset = baseCenterOffsetFromTransform * scaleFactor;
        transform.position = new Vector3(
            clampedX - offset.x,
            clampedY - offset.y,
            transform.position.z);

        return new Vector3(clampedX, clampedY, transform.position.z);
    }

    public void SetDirectionAndImpulse(Vector2 newDir, float impulseSpeed)
    {
        if (newDir.sqrMagnitude > 0.0001f)
        {
            direction = newDir.normalized;
        }
        currentImpulseBonusSpeed = impulseSpeed;
    }

    void Update()
    {
        if (Time.timeScale <= 0.0001f || (PauseManager.HasInstance && PauseManager.IsGamePaused))
        {
            return;
        }

        // System 6: Complete halt while CRT Static Glitch is active
        if (globalStaticHalt)
        {
            return;
        }

        if (splitGraceTimer > 0f)
        {
            splitGraceTimer -= Time.deltaTime;
        }

        if (shakeMoneyCooldown > 0f)
        {
            shakeMoneyCooldown -= Time.deltaTime;
        }

        if (grabWallBounceCooldown > 0f)
        {
            grabWallBounceCooldown -= Time.deltaTime;
        }

        if (!isChildLogo)
        {
            SyncCameraAndTextureAspect();
        }

        UpdateCameraBounds();

        // In Dino Runner, Suika Merger, and Tower Stack modes, TVMinigameController drives the logo(s)
        bool minigameOverridesMovement = TVMinigameController.Instance != null && TVMinigameController.Instance.OverridesMainLogoMovement;
        if (!isGrabbedByPlayer && !minigameOverridesMovement)
        {
            MoveLogo();
        }

        UpdateProceduralDeformation();

        // Main bouncer checks for child recombination (System 4 Tier 2)
        if (!isChildLogo && ActiveLogos.Count > 1)
        {
            CheckChildRecombinations();
        }
    }

    private void SyncCameraAndTextureAspect()
    {
        if (displayQuad == null || renderCamera == null) return;

        float quadAspect = displayQuad.lossyScale.x / displayQuad.lossyScale.y;
        renderCamera.aspect = quadAspect;

        int targetHeight = Mathf.RoundToInt(baseVerticalResolution * resolutionScale);
        int targetWidth = Mathf.RoundToInt(targetHeight * quadAspect);

        if (tvRenderTexture != null && (tvRenderTexture.width != targetWidth || tvRenderTexture.height != targetHeight))
        {
            tvRenderTexture.Release();
            tvRenderTexture.width = targetWidth;
            tvRenderTexture.height = targetHeight;
            tvRenderTexture.Create();

            lastWidth = targetWidth;
            lastHeight = targetHeight;
        }
    }

    private void MoveLogo()
    {
        float totalBoostAdder = 0f;
        for (int i = activeBoosts.Count - 1; i >= 0; i--)
        {
            activeBoosts[i].timer += Time.deltaTime;
            float t = activeBoosts[i].timer;

            if (t <= boostRampUpTime)
            {
                totalBoostAdder += Mathf.Lerp(0f, speedAddedPerClick, t / boostRampUpTime);
            }
            else if (t <= boostRampUpTime + boostRampDownTime)
            {
                totalBoostAdder += Mathf.Lerp(speedAddedPerClick, 0f, (t - boostRampUpTime) / boostRampDownTime);
            }
            else
            {
                activeBoosts.RemoveAt(i);
            }
        }

        // Decay continuous direct-hit impulse gently over time
        if (currentImpulseBonusSpeed > 0f)
        {
            currentImpulseBonusSpeed = Mathf.Max(0f, currentImpulseBonusSpeed - impulseContinuousDrag * Time.deltaTime);
        }

        DVDLogoBouncer master = MainBouncer;
        float effectiveStateMult = master != null ? master.stateMultiplier : stateMultiplier;
        float effectiveBillMult = master != null ? master.billSpeedMultiplier : billSpeedMultiplier;
        float effectiveBaseSpeed = master != null ? master.baseSpeed : baseSpeed;

        // Calculate active speed with System 5 dust debuff and System 2 impulse velocity
        float activeSpeed = (((effectiveBaseSpeed * effectiveStateMult) + totalBoostAdder + currentImpulseBonusSpeed) * effectiveBillMult) * globalDustSpeedMultiplier;
        transform.position += (Vector3)(direction * activeSpeed * Time.deltaTime);

        CheckBounds();
    }

    private void CheckBounds()
    {
        if (renderCamera == null || !renderCamera.orthographic) return;

        Vector3 center = GetLogoCenter();
        float hw = HalfWidth;
        float hh = HalfHeight;

        float maxX = center.x + hw;
        float minX = center.x - hw;
        float maxY = center.y + hh;
        float minY = center.y - hh;

        bool hitX = false;
        bool hitY = false;
        Vector3 correction = Vector3.zero;

        if (maxX >= camMaxX && direction.x > 0f)
        {
            correction.x = camMaxX - maxX;
            direction.x = -Mathf.Abs(direction.x);
            hitX = true;
        }
        else if (minX <= camMinX && direction.x < 0f)
        {
            correction.x = camMinX - minX;
            direction.x = Mathf.Abs(direction.x);
            hitX = true;
        }

        if (maxY >= camMaxY && direction.y > 0f)
        {
            correction.y = camMaxY - maxY;
            direction.y = -Mathf.Abs(direction.y);
            hitY = true;
        }
        else if (minY <= camMinY && direction.y < 0f)
        {
            correction.y = camMinY - minY;
            direction.y = Mathf.Abs(direction.y);

            // In One-Sided Pong mode, hitting the bottom wall is a missed ball (-3x CornerHitValue + damage flash)
            if (TVMinigameController.Instance != null && TVMinigameController.Instance.IsPongActive)
            {
                transform.position += correction;
                if (Mathf.Abs(direction.x) < minAxisVelocity) direction.x = Mathf.Sign(direction.x == 0f ? 1f : direction.x) * minAxisVelocity;
                direction.Normalize();
                TriggerSquashStretch(Vector2.up, maxSquashAmount * 1.15f);
                TriggerDamageFlash();
                ApplyMinigameCornerPenalty(3, GetLogoCenter());
                if (hitX) HandleBounce(true, false);
                return;
            }

            hitY = true;
        }

        transform.position += correction;
        if (hitX || hitY) HandleBounce(hitX, hitY);
    }

    private void HandleBounce(bool hitX, bool hitY)
    {
        if (hitX) direction.y += Random.Range(-bounceVariation, bounceVariation);
        if (hitY) direction.x += Random.Range(-bounceVariation, bounceVariation);

        if (Mathf.Abs(direction.x) < minAxisVelocity) direction.x = Mathf.Sign(direction.x) * minAxisVelocity;
        if (Mathf.Abs(direction.y) < minAxisVelocity) direction.y = Mathf.Sign(direction.y) * minAxisVelocity;

        direction.Normalize();
        bool isCorner = hitX && hitY;

        // Decelerate impulse across subsequent wall bounces back to base speed (System 2)
        if (currentImpulseBonusSpeed > 0f)
        {
            currentImpulseBonusSpeed *= bounceImpulseRetention;
            if (currentImpulseBonusSpeed < 0.15f)
            {
                currentImpulseBonusSpeed = 0f;
            }
        }

        // Trigger directional squash & stretch along wall normal (System 2)
        if (isCorner)
        {
            TriggerSquashStretch(new Vector2(Mathf.Sign(direction.x), Mathf.Sign(direction.y)).normalized, maxSquashAmount * 1.15f);
        }
        else if (hitX)
        {
            TriggerSquashStretch(Vector2.right, maxSquashAmount * wallBounceSquashMultiplier);
        }
        else if (hitY)
        {
            TriggerSquashStretch(Vector2.up, maxSquashAmount * wallBounceSquashMultiplier);
        }

        DVDLogoBouncer master = MainBouncer;
        decimal rewardScale = (splitGeneration == 0) ? 1.0m : (splitGeneration == 1 ? 0.75m : 0.55m);
        decimal edgeAmount = master.edgeReward * (decimal)master.moneyMultiplier * rewardScale;

        if (isCorner)
        {
            if (master.audioSource != null && master.cornerHitSound != null) master.audioSource.PlayOneShot(master.cornerHitSound);
            master.TriggerVisualBounce(master.cornerBounceMultiplier);

            decimal reward = System.Math.Round(edgeAmount * 5m, 2);
            if (reward < 0.01m) reward = 0.01m;

            master.currentMoney += reward;
            master.allTimeMoneyEarned += reward;
            if (RunSaveManager.HasInstance) RunSaveManager.Instance.RecordMoneyEarned(reward);
            SpawnHitEffect(cornerEffectTemplate != null ? cornerEffectTemplate : master.cornerEffectTemplate);
            master.SpawnPopup(reward, false);
        }
        else
        {
            if (master.audioSource != null && master.edgeHitSound != null) master.audioSource.PlayOneShot(master.edgeHitSound);
            master.TriggerVisualBounce(master.edgeBounceMultiplier);

            decimal reward = System.Math.Round(edgeAmount, 2);
            if (reward < 0.01m) reward = 0.01m;

            master.currentMoney += reward;
            master.allTimeMoneyEarned += reward;
            if (RunSaveManager.HasInstance) RunSaveManager.Instance.RecordMoneyEarned(reward);
            SpawnHitEffect(edgeEffectTemplate != null ? edgeEffectTemplate : master.edgeEffectTemplate);
            master.SpawnPopup(reward, false);
        }

        master.UpdateMoneyUI();
    }

    // =========================================================================
    // GRAB & SHAKE REMOTE MECHANICS
    // =========================================================================

    public void BeginGrab()
    {
        isGrabbedByPlayer = true;
        smoothedGrabVelocity = Vector2.zero;
        currentImpulseBonusSpeed = 0f;
        TriggerSquashStretch(Vector2.up, maxSquashAmount * 0.7f);
    }

    public void UpdateGrabbedPosition(Vector3 targetOrthoWorldPos, float dt)
    {
        if (dt <= 0.00001f) return;

        Vector3 prevCenter = GetLogoCenter();
        Vector3 newCenter = SetLogoCenterClamped(targetOrthoWorldPos);

        Vector2 frameVel = new Vector2((newCenter.x - prevCenter.x) / dt, (newCenter.y - prevCenter.y) / dt);
        smoothedGrabVelocity = Vector2.Lerp(smoothedGrabVelocity, frameVel, Mathf.Clamp01(dt * 18f));

        float speed = smoothedGrabVelocity.magnitude;
        if (speed > 1.5f)
        {
            Vector2 moveDir = smoothedGrabVelocity / speed;
            direction = moveDir;

            // Directional stretch along the shake movement vector!
            Vector2 perpNormal = new Vector2(-moveDir.y, moveDir.x);
            float stretchStrength = Mathf.Clamp(speed / 34f, 0.08f, maxSquashAmount * 1.1f);
            squashNormal = perpNormal;
            activeSquashDuration = Mathf.Max(0.25f, squashStretchDuration);
            activeSquashIntensity = stretchStrength;
            squashTimer = 0f;
        }

        // While holding the logo, hitting a wall or corner triggers a real bounce & edge/corner reward!
        float hw = HalfWidth;
        float hh = HalfHeight;
        float eps = 0.03f;

        bool hitRight = (targetOrthoWorldPos.x >= camMaxX - hw - eps) && (newCenter.x >= camMaxX - hw - eps);
        bool hitLeft = (targetOrthoWorldPos.x <= camMinX + hw + eps) && (newCenter.x <= camMinX + hw + eps);
        bool hitTop = (targetOrthoWorldPos.y >= camMaxY - hh - eps) && (newCenter.y >= camMaxY - hh - eps);
        bool hitBottom = (targetOrthoWorldPos.y <= camMinY + hh + eps) && (newCenter.y <= camMinY + hh + eps);

        bool hitX = hitRight || hitLeft;
        bool hitY = hitTop || hitBottom;

        if ((hitX || hitY) && grabWallBounceCooldown <= 0f)
        {
            grabWallBounceCooldown = 0.14f;
            if (hitRight) direction.x = -Mathf.Abs(direction.x == 0f ? 1f : direction.x);
            else if (hitLeft) direction.x = Mathf.Abs(direction.x == 0f ? 1f : direction.x);

            if (hitTop) direction.y = -Mathf.Abs(direction.y == 0f ? 1f : direction.y);
            else if (hitBottom) direction.y = Mathf.Abs(direction.y == 0f ? 1f : direction.y);

            HandleBounce(hitX, hitY);
        }
    }

    private void AwardShakeTick(Vector3 orthoCenterPos)
    {
        DVDLogoBouncer master = MainBouncer;
        decimal rewardScale = (splitGeneration == 0) ? 1.0m : (splitGeneration == 1 ? 0.75m : 0.55m);
        decimal earned = System.Math.Round(master.edgeReward * (decimal)master.shakeBonusMultiplier * (decimal)master.moneyMultiplier * rewardScale, 2);
        if (earned < 0.01m) earned = 0.01m;

        master.currentMoney += earned;
        master.allTimeMoneyEarned += earned;
        if (RunSaveManager.HasInstance) RunSaveManager.Instance.RecordMoneyEarned(earned);
        master.UpdateMoneyUI();
        master.SpawnPopup(earned, false);

        AudioSource src = master.audioSource != null ? master.audioSource : audioSource;
        AudioClip clip = master.grabShakeSound != null ? master.grabShakeSound : (master.directHitSound != null ? master.directHitSound : master.edgeHitSound);
        if (src != null && clip != null)
        {
            src.PlayOneShot(clip, 0.65f);
        }

        TriggerHitParticles(orthoCenterPos);
    }

    public void EndGrab()
    {
        if (!isGrabbedByPlayer) return;
        isGrabbedByPlayer = false;

        DVDLogoBouncer master = MainBouncer;
        float speed = smoothedGrabVelocity.magnitude;
        if (speed > 1.0f)
        {
            direction = smoothedGrabVelocity.normalized;
            currentImpulseBonusSpeed = Mathf.Clamp(speed * master.grabThrowSpeedMultiplier, 0f, master.maxGrabThrowImpulse) * globalDustSpeedMultiplier;
        }

        if (Mathf.Abs(direction.x) < minAxisVelocity) direction.x = Mathf.Sign(direction.x == 0f ? 1f : direction.x) * minAxisVelocity;
        if (Mathf.Abs(direction.y) < minAxisVelocity) direction.y = Mathf.Sign(direction.y == 0f ? 1f : direction.y) * minAxisVelocity;
        direction.Normalize();

        TriggerSquashStretch(direction, maxSquashAmount);
    }

    // =========================================================================
    // SYSTEM 2 & SYSTEM 4: DIRECT HIT, BUMPER RICOCHET & TACTICAL SLAP
    // =========================================================================

    /// <summary>
    /// System 2 & System 4: Applies a direct hit impulse to the DVD logo from a click on the TV screen.
    /// Calculates directional knockback from the click coordinate toward the logo's center,
    /// triggers directional squash & stretch, awards bonus money, spawns in-screen particles,
    /// and optionally splits the logo (Tier 2 & Ultimate Remote).
    /// </summary>
    public void ApplyDirectHit(Vector3 orthoHitWorldPoint, Vector3 tvSurfaceWorldPoint, bool allowMitosisSplit)
    {
        if (globalStaticHalt) return;

        Vector3 center = GetLogoCenter();
        Vector2 knockback = new Vector2(center.x - orthoHitWorldPoint.x, center.y - orthoHitWorldPoint.y);

        // If clicked nearly dead-center, push with a slight deflection
        if (knockback.sqrMagnitude < 0.0004f)
        {
            float fallbackAngle = Random.Range(-35f, 35f);
            knockback = RotateVector2(direction, fallbackAngle);
        }

        Vector2 hitNormal = knockback.normalized;
        direction = hitNormal;
        if (Mathf.Abs(direction.x) < minAxisVelocity * 0.5f) direction.x = Mathf.Sign(direction.x == 0f ? 1f : direction.x) * minAxisVelocity * 0.5f;
        if (Mathf.Abs(direction.y) < minAxisVelocity * 0.5f) direction.y = Mathf.Sign(direction.y == 0f ? 1f : direction.y) * minAxisVelocity * 0.5f;
        direction.Normalize();

        // Apply high initial impulse velocity scaled by dust debuff (System 5)
        currentImpulseBonusSpeed = directHitImpulseSpeed * globalDustSpeedMultiplier;

        // Award bonus cash
        DVDLogoBouncer master = MainBouncer;
        decimal rewardScale = (splitGeneration == 0) ? 1.0m : (splitGeneration == 1 ? 0.75m : 0.55m);
        decimal bonusEarned = System.Math.Round(master.edgeReward * (decimal)master.directHitBonusMultiplier * (decimal)master.moneyMultiplier * rewardScale, 2);
        if (bonusEarned < 0.01m) bonusEarned = 0.01m;

        master.currentMoney += bonusEarned;
        master.allTimeMoneyEarned += bonusEarned;
        if (RunSaveManager.HasInstance) RunSaveManager.Instance.RecordMoneyEarned(bonusEarned);
        master.UpdateMoneyUI();
        master.SpawnPopup(bonusEarned, false);

        // Audio feedback
        AudioSource src = master.audioSource != null ? master.audioSource : audioSource;
        AudioClip clip = master.directHitSound != null ? master.directHitSound : master.edgeHitSound;
        if (src != null && clip != null)
        {
            src.PlayOneShot(clip, 0.9f);
        }

        // True directional Squash & Stretch along the exact hit normal
        TriggerSquashStretch(hitNormal, maxSquashAmount);

        // Spawn particles INSIDE the TV renderCamera space so they stream to SCREENTRANSFER
        TriggerHitParticles(orthoHitWorldPoint);

        // System 4 Tier 2 / Ultimate: Mitosis / Splitter Remote
        if (allowMitosisSplit && splitGeneration < maxSplitGenerations)
        {
            SplitIntoTwo(direction);
        }
    }

    /// <summary>
    /// System 4: Ricochets the logo violently off a placed Pinball Bumper inside the TV screen.
    /// </summary>
    public void ApplyBumperRicochet(Vector3 bumperOrthoPos, Vector3 bumperSurfaceWorldPos)
    {
        if (globalStaticHalt) return;

        Vector3 center = GetLogoCenter();
        Vector2 away = new Vector2(center.x - bumperOrthoPos.x, center.y - bumperOrthoPos.y);
        if (away.sqrMagnitude < 0.0001f)
        {
            away = -direction;
        }
        Vector2 hitNormal = away.normalized;
        direction = hitNormal;
        currentImpulseBonusSpeed = directHitImpulseSpeed * 1.15f * globalDustSpeedMultiplier;

        DVDLogoBouncer master = MainBouncer;
        decimal rewardScale = (splitGeneration == 0) ? 1.0m : (splitGeneration == 1 ? 0.75m : 0.55m);
        decimal bonusEarned = System.Math.Round(master.edgeReward * (decimal)master.moneyMultiplier * rewardScale, 2);
        if (bonusEarned < 0.01m) bonusEarned = 0.01m;

        master.currentMoney += bonusEarned;
        master.allTimeMoneyEarned += bonusEarned;
        if (RunSaveManager.HasInstance) RunSaveManager.Instance.RecordMoneyEarned(bonusEarned);
        master.UpdateMoneyUI();
        master.SpawnPopup(bonusEarned, false);

        TriggerSquashStretch(hitNormal, maxSquashAmount * 1.15f);
        TriggerHitParticles(bumperOrthoPos);
    }

    /// <summary>
    /// System 6: Tactical Slap While Working.
    /// If any active DVD logo is inside the top 1/5th (20%) of the screen, violently hammers it downward.
    /// Returns true if at least one logo was in the top 20% and hammered downward.
    /// </summary>
    public bool TryApplyTacticalSlapHammer(Vector3 tvSurfaceWorldPos)
    {
        if (globalStaticHalt) return false;

        UpdateCameraBounds();
        bool hammeredAny = false;
        float totalScreenHeight = camMaxY - camMinY;
        float topFifthThresholdY = camMaxY - (totalScreenHeight * 0.20f);

        for (int i = 0; i < ActiveLogos.Count; i++)
        {
            DVDLogoBouncer logo = ActiveLogos[i];
            if (logo == null || !logo.isActiveAndEnabled) continue;

            Vector3 center = logo.GetLogoCenter();
            if (center.y >= topFifthThresholdY)
            {
                float horizCarry = Mathf.Clamp(logo.direction.x * 0.22f, -0.35f, 0.35f);
                if (Mathf.Abs(horizCarry) < 0.08f) horizCarry = (logo.direction.x >= 0f ? 0.12f : -0.12f);
                logo.direction = new Vector2(horizCarry, -1f).normalized;
                logo.currentImpulseBonusSpeed = logo.directHitImpulseSpeed * 1.45f * globalDustSpeedMultiplier;
                logo.TriggerSquashStretch(Vector2.up, logo.maxSquashAmount * 1.3f);
                logo.TriggerHitParticles(center);

                DVDLogoBouncer master = logo.MainBouncer;
                decimal slapBonus = System.Math.Round(master.edgeReward * 2.0m * (decimal)master.moneyMultiplier, 2);
                if (slapBonus < 0.01m) slapBonus = 0.01m;

                master.currentMoney += slapBonus;
                master.allTimeMoneyEarned += slapBonus;
                if (RunSaveManager.HasInstance) RunSaveManager.Instance.RecordMoneyEarned(slapBonus);
                master.UpdateMoneyUI();
                master.SpawnPopup(slapBonus, false);

                hammeredAny = true;
            }
        }

        return hammeredAny;
    }

    // =========================================================================
    // TRUE DIRECTIONAL SQUASH & STRETCH DEFORMATION
    // =========================================================================

    /// <summary>
    /// System 2: Triggers procedural directional squash & stretch along the specified 2D normal.
    /// </summary>
    public void TriggerSquashStretch(Vector2 normal, float intensity = -1f)
    {
        DVDLogoBouncer master = MainBouncer;
        squashNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.right;
        activeSquashDuration = Mathf.Max(0.15f, master != null ? master.squashStretchDuration : squashStretchDuration);
        float baseMax = master != null ? master.maxSquashAmount : maxSquashAmount;
        activeSquashIntensity = intensity > 0f ? Mathf.Clamp(intensity, 0.05f, 0.75f) : baseMax;
        squashTimer = 0f;
    }

    public void TriggerRecombineBounce()
    {
        recombineBounceTimer = 0f;
    }

    private void UpdateProceduralDeformation()
    {
        float genScale = GetGenerationScaleFactor(splitGeneration);
        Vector3 rootBaseScale = initialLocalScale * genScale;

        DVDLogoBouncer master = MainBouncer;
        float oscCount = master != null ? master.squashOscillations : squashOscillations;
        float damping = master != null ? master.squashDamping : squashDamping;

        float deform = 0f;
        bool hasActiveSquash = false;

        if (squashTimer >= 0f)
        {
            squashTimer += Time.deltaTime;
            float normT = squashTimer / activeSquashDuration;
            if (normT >= 1f)
            {
                squashTimer = -1f;
            }
            else
            {
                hasActiveSquash = true;
                // Smooth damped cosine wave:
                // At t=0: wave = +1 (squashes along impact normal, stretches perpendicular).
                // Then rebounds to negative (stretches along movement normal, squashes perpendicular),
                // oscillating smoothly back to rest over squashStretchDuration (~0.55s).
                float envelope = Mathf.Exp(-damping * normT) * (1f - normT);
                float wave = Mathf.Cos(normT * Mathf.PI * 2f * oscCount);
                deform = activeSquashIntensity * envelope * wave;
            }
        }

        float recombinePop = 1f;
        if (recombineBounceTimer >= 0f)
        {
            recombineBounceTimer += Time.deltaTime;
            float bNorm = recombineBounceTimer / 0.38f;
            if (bNorm >= 1f)
            {
                recombineBounceTimer = -1f;
            }
            else
            {
                recombinePop = 1f + 0.42f * Mathf.Sin(bNorm * Mathf.PI * 2f) * (1f - bNorm);
            }
        }

        // Keep root transform at clean generation scale * recombinePop so physics collider never distorts
        transform.localScale = new Vector3(rootBaseScale.x * recombinePop, rootBaseScale.y * recombinePop, rootBaseScale.z);

        // Apply unified 2D directional deformation to ALL visual mesh parts ('dvd' AND 'dvd/Torus')
        // in the parent/collider (DVDLogo) local space around BoxCollider.center!
        if (canDeformVertices && deformableParts.Count > 0)
        {
            if (!hasActiveSquash)
            {
                if (wasDeformingLastFrame)
                {
                    wasDeformingLastFrame = false;
                    for (int p = 0; p < deformableParts.Count; p++)
                    {
                        DeformableMeshPart part = deformableParts[p];
                        if (part == null || part.meshInstance == null) continue;
                        System.Array.Copy(part.originalLocalVerts, part.deformedLocalVerts, part.originalLocalVerts.Length);
                        part.meshInstance.vertices = part.deformedLocalVerts;
                        part.meshInstance.RecalculateBounds();
                    }
                    if (visualChildTransform != null)
                    {
                        visualChildTransform.localPosition = visualChildInitialLocalPos;
                    }
                }
                return;
            }

            wasDeformingLastFrame = true;

            if (logoCollider is BoxCollider liveBox)
            {
                rootDeformCenter = liveBox.center;
            }

            // parallelScale: < 1 when squashing along normal, > 1 when stretching along normal
            float parallelScale = Mathf.Clamp(1f - deform, 0.38f, 1.85f);
            // perpScale: inverse of parallelScale so area is conserved
            float perpScale = 1f / parallelScale;

            // Convert world/camera 2D squashNormal into parent/collider (DVDLogo) local space
            Vector3 worldNormal = new Vector3(squashNormal.x, squashNormal.y, 0f);
            Vector3 rootLocalNormal = transform.InverseTransformDirection(worldNormal);
            Vector2 n = new Vector2(rootLocalNormal.x, rootLocalNormal.y);
            if (n.sqrMagnitude < 0.0001f) n = Vector2.right;
            else n.Normalize();
            Vector2 t = new Vector2(-n.y, n.x);

            for (int p = 0; p < deformableParts.Count; p++)
            {
                DeformableMeshPart part = deformableParts[p];
                if (part == null || part.meshInstance == null) continue;

                Vector3[] rootVerts = part.rootSpaceVerts;
                Vector3[] defVerts = part.deformedLocalVerts;
                Matrix4x4 rootToPart = part.rootToPartMatrix;
                int count = rootVerts.Length;

                for (int i = 0; i < count; i++)
                {
                    Vector3 rv = rootVerts[i];
                    float dx = rv.x - rootDeformCenter.x;
                    float dy = rv.y - rootDeformCenter.y;

                    // Project vertex offset onto directional normal (n) and perpendicular tangent (t) in parent space
                    float dn = (dx * n.x + dy * n.y) * parallelScale;
                    float dt = (dx * t.x + dy * t.y) * perpScale;

                    Vector3 deformedRoot = new Vector3(
                        rootDeformCenter.x + n.x * dn + t.x * dt,
                        rootDeformCenter.y + n.y * dn + t.y * dt,
                        rv.z);

                    // Transform deformed vertex back into this sub-mesh's own local space
                    defVerts[i] = rootToPart.MultiplyPoint3x4(deformedRoot);
                }

                part.meshInstance.vertices = defVerts;
                part.meshInstance.RecalculateBounds();
            }
        }
        else if (visualChildTransform != null)
        {
            // Fallback if mesh is not readable: scale visualChildTransform with center-pivot position compensation
            if (!hasActiveSquash)
            {
                visualChildTransform.localScale = visualChildInitialLocalScale;
                visualChildTransform.localPosition = visualChildInitialLocalPos;
                return;
            }

            float parallelScale = Mathf.Clamp(1f - deform, 0.38f, 1.85f);
            float perpScale = 1f / parallelScale;

            float nx2 = squashNormal.x * squashNormal.x;
            float ny2 = squashNormal.y * squashNormal.y;
            float sx = Mathf.Lerp(perpScale, parallelScale, nx2);
            float sy = Mathf.Lerp(perpScale, parallelScale, ny2);

            visualChildTransform.localScale = new Vector3(
                visualChildInitialLocalScale.x * sx,
                visualChildInitialLocalScale.y * sy,
                visualChildInitialLocalScale.z);

            // Compensate localPosition so scaling happens around the logo's center instead of bottom-left corner
            Vector3 localCenter = (logoCollider is BoxCollider bc) ? bc.center : new Vector3(0.394f, 0.155f, 0f);
            visualChildTransform.localPosition = visualChildInitialLocalPos + new Vector3(
                localCenter.x * (1f - sx),
                localCenter.y * (1f - sy),
                0f);
        }
    }

    // =========================================================================
    // IN-SCREEN PARTICLE HOOK (CAPTURED BY RENDER CAMERA INTO SCREENTRANSFER)
    // =========================================================================

    /// <summary>
    /// Extracts the current two colors of this DVDLogo ('dvd' primary letters color and 'dvd/Torus' secondary ring color).
    /// </summary>
    public void GetLogoTwoColors(out Color primaryColor, out Color secondaryColor)
    {
        DVDLogoBouncer master = MainBouncer != null ? MainBouncer : this;
        primaryColor = master.sparkColorPrimary;
        secondaryColor = master.sparkColorSecondary;

        Transform dvdTr = transform.Find("dvd");
        if (dvdTr != null)
        {
            Renderer r1 = dvdTr.GetComponent<Renderer>();
            if (TryExtractRendererColor(r1, out Color c1))
            {
                primaryColor = c1;
            }

            Transform torusTr = dvdTr.Find("Torus");
            if (torusTr != null)
            {
                Renderer r2 = torusTr.GetComponent<Renderer>();
                if (TryExtractRendererColor(r2, out Color c2))
                {
                    secondaryColor = c2;
                }
            }
        }
        else
        {
            Renderer[] rends = GetComponentsInChildren<Renderer>(false);
            if (rends.Length > 0 && TryExtractRendererColor(rends[0], out Color c1))
            {
                primaryColor = c1;
            }
            if (rends.Length > 1 && TryExtractRendererColor(rends[1], out Color c2))
            {
                secondaryColor = c2;
            }
        }
    }

    private static bool TryExtractRendererColor(Renderer r, out Color c)
    {
        c = Color.white;
        if (r == null) return false;

        Material mat = r.sharedMaterial;
        if (mat == null) return false;

        if (mat.HasProperty("_BaseColor"))
        {
            c = mat.GetColor("_BaseColor");
            c.a = 1f;
            return true;
        }
        if (mat.HasProperty("_Color"))
        {
            c = mat.GetColor("_Color");
            c.a = 1f;
            return true;
        }
        return false;
    }

    public void SetMainLogoRenderersVisible(bool visible)
    {
        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            Renderer r = rends[i];
            if (r == null) continue;
            if (r.gameObject == edgeEffectTemplate || r.gameObject == cornerEffectTemplate) continue;
            string lower = r.gameObject.name.ToLowerInvariant();
            if (lower == "edge" || lower == "corner") continue;
            r.enabled = visible;
        }
    }

    /// <summary>
    /// System 2: Fires the onLogoHit UnityEvent and spawns circular dual-color hit sparks INSIDE the TV renderCamera's view
    /// matching the logo's two colors ('dvd' + 'dvd/Torus').
    /// </summary>
    public void TriggerHitParticles(Vector3 orthoHitWorldPoint)
    {
        GetLogoTwoColors(out Color colA, out Color colB);
        TriggerHitParticlesWithColors(orthoHitWorldPoint, colA, colB);
    }

    public void TriggerHitParticlesWithColors(Vector3 orthoHitWorldPoint, Color colorA, Color colorB)
    {
        DVDLogoBouncer master = MainBouncer;
        Camera cam = renderCamera != null ? renderCamera : (master != null ? master.renderCamera : null);

        // Place particle burst slightly in front of the DVD logo inside renderCamera space
        Vector3 inScreenPos = orthoHitWorldPoint;
        inScreenPos.z = transform.position.z - 0.15f;

        master.onLogoHit?.Invoke(inScreenPos);
        if (isChildLogo)
        {
            onLogoHit?.Invoke(inScreenPos);
        }

        GameObject prefabToSpawn = hitParticlePrefab != null ? hitParticlePrefab : master.hitParticlePrefab;
        Transform parentTransform = cam != null ? cam.transform : transform.parent;

        if (prefabToSpawn != null)
        {
            GameObject p = Instantiate(prefabToSpawn, inScreenPos, Quaternion.identity, parentTransform);
            float scaleMult = master != null ? master.hitParticleScale : hitParticleScale;
            p.transform.localScale = prefabToSpawn.transform.localScale * scaleMult;
            SetLayerRecursively(p, gameObject.layer);
            Destroy(p, 1.5f);
        }
        else
        {
            SpawnProceduralHitSparksInScreen(inScreenPos, parentTransform, master != null ? master : this, colorA, colorB);
        }
    }

    private static Texture2D GetOrCreateCircleParticleTexture()
    {
        if (cachedCircleParticleTexture != null) return cachedCircleParticleTexture;

        const int size = 64;
        cachedCircleParticleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        cachedCircleParticleTexture.name = "ProceduralCircleSparkTex";
        cachedCircleParticleTexture.wrapMode = TextureWrapMode.Clamp;
        cachedCircleParticleTexture.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float maxRadius = center - 1.0f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float normDist = dist / maxRadius;
                // Smooth anti-aliased circular edge
                float alpha = Mathf.Clamp01((1f - normDist) * 5.5f);
                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        cachedCircleParticleTexture.SetPixels32(pixels);
        cachedCircleParticleTexture.Apply(false, false);
        return cachedCircleParticleTexture;
    }

    private void SpawnProceduralHitSparksInScreen(Vector3 inScreenWorldPos, Transform camParent, DVDLogoBouncer settingsSource, Color colorA, Color colorB)
    {
        GameObject burstObj = new GameObject("DVDHitSparks_InScreen");
        if (camParent != null)
        {
            burstObj.transform.SetParent(camParent, true);
        }
        burstObj.transform.position = inScreenWorldPos;
        burstObj.layer = gameObject.layer;

        ParticleSystem ps = burstObj.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
        float baseSpd = Mathf.Max(0.5f, settingsSource.proceduralSparkSpeed);
        main.startSpeed = new ParticleSystem.MinMaxCurve(baseSpd * 0.55f, baseSpd * 1.35f);
        float baseSz = Mathf.Max(0.02f, settingsSource.proceduralSparkSize);
        main.startSize = new ParticleSystem.MinMaxCurve(baseSz * 0.65f, baseSz * 1.35f);

        // Use a Fixed 2-key gradient so each circular particle is distinctly either Color A (letters) or Color B (ring)
        Color cA = colorA; cA.a = 1f;
        Color cB = colorB; cB.a = 1f;
        Gradient twoColorGrad = new Gradient();
        twoColorGrad.mode = GradientMode.Fixed;
        twoColorGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(cA, 0.49f), new GradientColorKey(cB, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        var minMaxGrad = new ParticleSystem.MinMaxGradient(twoColorGrad);
        minMaxGrad.mode = ParticleSystemGradientMode.RandomColor;
        main.startColor = minMaxGrad;

        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        short count = (short)Mathf.Clamp(settingsSource.proceduralSparkCount, 4, 60);
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.12f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve shrinkCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, shrinkCurve);

        var psRenderer = burstObj.GetComponent<ParticleSystemRenderer>();
        if (psRenderer != null)
        {
            Texture2D circleTex = GetOrCreateCircleParticleTexture();
            // Sprites/Default natively multiplies ParticleSystem vertex colors (Color A / Color B) with our anti-aliased circle texture
            Shader circleShader = Shader.Find("Sprites/Default");
            if (circleShader == null) circleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (circleShader == null) circleShader = Shader.Find("Particles/Standard Unlit");
            if (circleShader != null)
            {
                Material sparkMat = new Material(circleShader);
                sparkMat.color = Color.white;
                sparkMat.mainTexture = circleTex;
                if (sparkMat.HasProperty("_MainTex")) sparkMat.SetTexture("_MainTex", circleTex);
                if (sparkMat.HasProperty("_BaseMap")) sparkMat.SetTexture("_BaseMap", circleTex);
                if (sparkMat.HasProperty("_BaseColor")) sparkMat.SetColor("_BaseColor", Color.white);
                psRenderer.material = sparkMat;
            }
        }

        ps.Play();
        Destroy(burstObj, 0.75f);
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            if (child != null) SetLayerRecursively(child.gameObject, layer);
        }
    }

    // =========================================================================
    // SYSTEM 4 TIER 2: MITOSIS SPLIT & RECOMBINATION
    // =========================================================================

    /// <summary>
    /// System 4 Tier 2: Splits this logo into two smaller child logos traveling in diverging trajectories.
    /// </summary>
    private void SplitIntoTwo(Vector2 baseDir)
    {
        if (splitGeneration >= maxSplitGenerations) return;

        InitializeDeformableVisualMesh();

        int nextGen = splitGeneration + 1;
        this.splitGeneration = nextGen;
        this.splitGraceTimer = splitRecombineGracePeriod;
        this.recombineBounceTimer = -1f;

        float nextScaleFactor = GetGenerationScaleFactor(nextGen);
        transform.localScale = initialLocalScale * nextScaleFactor;

        Vector2 dirA = RotateVector2(baseDir, splitDivergenceAngle).normalized;
        Vector2 dirB = RotateVector2(baseDir, -splitDivergenceAngle).normalized;

        this.direction = dirA;

        // Immediately restore this logo's meshes to pristine rest vertices before cloning
        ResetDeformationImmediate();

        // Instantiate sibling child logo in the same parent hierarchy (DVDSetup/Camera)
        GameObject cloneObj = Instantiate(gameObject, transform.position, transform.rotation, transform.parent);
        cloneObj.name = $"{MainBouncer.gameObject.name}_Child_Gen{nextGen}";

        DVDLogoBouncer child = cloneObj.GetComponent<DVDLogoBouncer>();
        if (child != null)
        {
            child.isChildLogo = true;
            child.isGrabbedByPlayer = false;
            child.parentBouncer = MainBouncer;
            child.splitGeneration = nextGen;
            child.initialLocalScale = this.initialLocalScale;
            child.baseHalfWidth = this.baseHalfWidth;
            child.baseHalfHeight = this.baseHalfHeight;
            child.baseCenterOffsetFromTransform = this.baseCenterOffsetFromTransform;
            child.boundsInitialized = true;
            child.camMinX = this.camMinX;
            child.camMaxX = this.camMaxX;
            child.camMinY = this.camMinY;
            child.camMaxY = this.camMaxY;
            child.splitGraceTimer = splitRecombineGracePeriod;
            child.recombineBounceTimer = -1f;

            // Give the child its own independent mesh instances copied directly from MainBouncer's pristine rest vertices
            // so even when this parent deforms later in the same frame, the child can NEVER get stuck in a deformed state!
            child.InitializeChildDeformableMeshesFromParent(MainBouncer);

            child.SetDirectionAndImpulse(dirB, this.currentImpulseBonusSpeed);
            child.TriggerSquashStretch(dirB, maxSquashAmount * 0.9f);
        }

        // Re-trigger squash & stretch on the parent along its new diverging trajectory
        this.TriggerSquashStretch(dirA, maxSquashAmount * 0.9f);
    }

    /// <summary>
    /// System 4 Tier 2: Checks if two child logos of the same size collide, recombining them into one larger logo.
    /// </summary>
    private void CheckChildRecombinations()
    {
        for (int i = 0; i < ActiveLogos.Count; i++)
        {
            DVDLogoBouncer a = ActiveLogos[i];
            if (a == null || !a.isActiveAndEnabled || a.splitGeneration <= 0 || a.splitGraceTimer > 0f) continue;

            for (int j = i + 1; j < ActiveLogos.Count; j++)
            {
                DVDLogoBouncer b = ActiveLogos[j];
                if (b == null || !b.isActiveAndEnabled || b.splitGeneration != a.splitGeneration || b.splitGraceTimer > 0f) continue;

                Vector3 centerA = a.GetLogoCenter();
                Vector3 centerB = b.GetLogoCenter();

                float overlapX = (a.HalfWidth + b.HalfWidth) * 0.85f;
                float overlapY = (a.HalfHeight + b.HalfHeight) * 0.85f;

                if (Mathf.Abs(centerA.x - centerB.x) <= overlapX && Mathf.Abs(centerA.y - centerB.y) <= overlapY)
                {
                    DVDLogoBouncer survivor = !a.isChildLogo ? a : (!b.isChildLogo ? b : a);
                    DVDLogoBouncer absorbed = (survivor == a) ? b : a;

                    if (absorbed.isGrabbedByPlayer)
                    {
                        survivor.isGrabbedByPlayer = true;
                        absorbed.isGrabbedByPlayer = false;
                    }

                    Vector3 midPos = (survivor.transform.position + absorbed.transform.position) * 0.5f;
                    survivor.transform.position = midPos;
                    survivor.splitGeneration = Mathf.Max(0, survivor.splitGeneration - 1);
                    survivor.splitGraceTimer = splitRecombineGracePeriod * 0.5f;

                    Vector2 mergedDir = (survivor.direction + absorbed.direction);
                    if (mergedDir.sqrMagnitude < 0.01f) mergedDir = survivor.direction;
                    survivor.direction = mergedDir.normalized;
                    survivor.currentImpulseBonusSpeed = Mathf.Max(survivor.currentImpulseBonusSpeed, absorbed.currentImpulseBonusSpeed);

                    survivor.ResetDeformationImmediate();
                    survivor.CheckBounds();
                    survivor.TriggerRecombineBounce();
                    survivor.TriggerSquashStretch(Vector2.up, survivor.maxSquashAmount * 0.85f);

                    // Play punchy pop sound
                    DVDLogoBouncer master = MainBouncer;
                    AudioSource src = master.audioSource != null ? master.audioSource : audioSource;
                    AudioClip popClip = master.recombinePopSound != null ? master.recombinePopSound : master.cornerHitSound;
                    if (src != null && popClip != null)
                    {
                        src.PlayOneShot(popClip, 0.95f);
                    }

                    ActiveLogos.Remove(absorbed);
                    Destroy(absorbed.gameObject);
                    return;
                }
            }
        }
    }

    private static Vector2 RotateVector2(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(rad);
        float cos = Mathf.Cos(rad);
        return new Vector2(cos * v.x - sin * v.y, sin * v.x + cos * v.y);
    }

    private void TriggerVisualBounce(float multiplier)
    {
        if (tvTransform == null && isChildLogo) return;
        if (activeBounceCoroutine != null) StopCoroutine(activeBounceCoroutine);
        activeBounceCoroutine = StartCoroutine(VisualBounceRoutine(multiplier));
    }

    private IEnumerator VisualBounceRoutine(float scaleMultiplier)
    {
        Transform targetTransform = tvTransform != null ? tvTransform : transform;
        Vector3 peakScale = originalScale * scaleMultiplier;
        float halfDuration = visualBounceDuration / 2f;

        // Calculate local pivot offset so TV bounce is centered horizontally on the screen instead of pulling right
        Vector3 localPivotOffset = Vector3.zero;
        if (tvTransform != null && displayQuad != null && tvTransform.parent != null)
        {
            Renderer quadRend = displayQuad.GetComponent<Renderer>();
            Vector3 screenWorldCenter = quadRend != null ? quadRend.bounds.center : displayQuad.position;
            Vector3 worldDiff = screenWorldCenter - tvTransform.position;
            localPivotOffset = new Vector3(worldDiff.x, 0f, 0f);
        }

        float time = 0;
        while (time < halfDuration)
        {
            if (Time.timeScale <= 0.0001f || (PauseManager.HasInstance && PauseManager.IsGamePaused))
            {
                yield return null;
                continue;
            }
            float t = time / halfDuration;
            Vector3 curScale = Vector3.Lerp(originalScale, peakScale, t);
            targetTransform.localScale = curScale;
            if (tvTransform != null)
            {
                float ratioX = originalScale.x != 0f ? (curScale.x / originalScale.x) : 1f;
                targetTransform.localPosition = originalTvLocalPos - new Vector3(localPivotOffset.x * (ratioX - 1f), 0f, 0f);
            }
            time += Time.deltaTime;
            yield return null;
        }

        time = 0;
        while (time < halfDuration)
        {
            if (Time.timeScale <= 0.0001f || (PauseManager.HasInstance && PauseManager.IsGamePaused))
            {
                yield return null;
                continue;
            }
            float t = time / halfDuration;
            Vector3 curScale = Vector3.Lerp(peakScale, originalScale, t);
            targetTransform.localScale = curScale;
            if (tvTransform != null)
            {
                float ratioX = originalScale.x != 0f ? (curScale.x / originalScale.x) : 1f;
                targetTransform.localPosition = originalTvLocalPos - new Vector3(localPivotOffset.x * (ratioX - 1f), 0f, 0f);
            }
            time += Time.deltaTime;
            yield return null;
        }

        targetTransform.localScale = originalScale;
        if (tvTransform != null)
        {
            targetTransform.localPosition = originalTvLocalPos;
        }
        activeBounceCoroutine = null;
    }

    private void SpawnHitEffect(GameObject template)
    {
        if (template == null) return;

        GameObject clone = Instantiate(template, template.transform.parent);
        clone.transform.localPosition = template.transform.localPosition;
        clone.transform.localRotation = template.transform.localRotation;
        clone.transform.localScale = template.transform.localScale;

        clone.SetActive(true);
        Destroy(clone, effectLifetime);
    }

    public void SpawnPopup(decimal amount, bool isNegative)
    {
        GameObject prefabToUse = isNegative ? negativeMoneyPopupPrefab : moneyPopupPrefab;
        if (prefabToUse == null || popupCanvasParent == null) return;

        GameObject popup = Instantiate(prefabToUse, popupCanvasParent);
        MoneyPopup popupScript = popup.GetComponent<MoneyPopup>();

        if (popupScript != null)
        {
            popupScript.AnimatePopup(isNegative ? -amount : amount);
        }
    }

    public bool TrySpendMoney(decimal amount)
    {
        if (currentMoney >= amount)
        {
            currentMoney -= amount;
            UpdateMoneyUI();
            SpawnPopup(amount, true);
            return true;
        }
        return false;
    }

    private void UpdateMoneyUI()
    {
        if (totalMoneyText != null) totalMoneyText.text = $"${currentMoney:0.00}";
    }

    public void RefreshMoneyUIFromSave()
    {
        UpdateMoneyUI();
    }

    public Vector2 GetDirection()
    {
        return direction;
    }

    public void SetDirection(Vector2 newDirection)
    {
        if (newDirection.sqrMagnitude > 0.001f)
        {
            direction = newDirection.normalized;
        }
    }

    public void TriggerRemoteBoost()
    {
        activeBoosts.Add(new BoostInstance { timer = 0f });
    }

    public void SetSpeedMultiplier(float newMultiplier)
    {
        stateMultiplier = newMultiplier;
    }

    public void SetMoneyMultiplier(float newMultiplier)
    {
        moneyMultiplier = newMultiplier;
    }

    // =========================================================================
    // MINIGAME HELPERS (DINO RUNNER & ONE-SIDED PONG)
    // =========================================================================

    public Bounds GetLogoWorldBounds()
    {
        Vector3 center = GetLogoCenter();
        return new Bounds(center, new Vector3(HalfWidth * 2f, HalfHeight * 2f, 0.25f));
    }

    public Vector3 GetLogoVisualCenter()
    {
        return GetLogoCenter();
    }

    public Vector2 GetDirectionVector()
    {
        return direction;
    }

    public void EnsureValidBounceDirectionAfterMinigame(Vector2 fallbackDir)
    {
        if (fallbackDir.sqrMagnitude > 0.01f)
        {
            direction = fallbackDir.normalized;
        }
        if (Mathf.Abs(direction.x) < minAxisVelocity) direction.x = Mathf.Sign(direction.x == 0f ? 1f : direction.x) * minAxisVelocity;
        if (Mathf.Abs(direction.y) < minAxisVelocity) direction.y = Mathf.Sign(direction.y == 0f ? 1f : direction.y) * minAxisVelocity;
        direction.Normalize();
        CheckBounds();
    }

    public Vector2 GetBaseHalfSize()
    {
        InitializeLogoBounds();
        return new Vector2(baseHalfWidth, baseHalfHeight);
    }

    public Vector3 GetBaseCenterOffset()
    {
        InitializeLogoBounds();
        return baseCenterOffsetFromTransform;
    }

    public void DespawnAllChildLogosForMinigame()
    {
        for (int i = ActiveLogos.Count - 1; i >= 0; i--)
        {
            DVDLogoBouncer logo = ActiveLogos[i];
            if (logo != null && logo.isChildLogo)
            {
                ActiveLogos.RemoveAt(i);
                Destroy(logo.gameObject);
            }
        }
        splitGeneration = 0;
        transform.localScale = initialLocalScale;
        ResetDeformationImmediate();
    }

    public void ReflectFromPongPaddle(Vector2 newBounceDir)
    {
        direction = newBounceDir.normalized;
        if (Mathf.Abs(direction.x) < minAxisVelocity) direction.x = Mathf.Sign(direction.x == 0f ? 1f : direction.x) * minAxisVelocity;
        if (direction.y < minAxisVelocity) direction.y = Mathf.Max(minAxisVelocity, Mathf.Abs(direction.y));
        direction.Normalize();
        TriggerSquashStretch(Vector2.up, maxSquashAmount * 0.95f);
    }

    public decimal GetCornerHitRewardAmount()
    {
        DVDLogoBouncer master = MainBouncer;
        decimal rewardScale = (splitGeneration == 0) ? 1.0m : (splitGeneration == 1 ? 0.75m : 0.55m);
        decimal edgeAmount = master.edgeReward * (decimal)master.moneyMultiplier * rewardScale;
        decimal cornerAmount = System.Math.Round(edgeAmount * 5m, 2);
        if (cornerAmount < 0.01m) cornerAmount = 0.01m;
        return cornerAmount;
    }

    public void AwardMinigameCornerReward(int cornerMultiplier, Vector3 orthoPos)
    {
        GetLogoTwoColors(out Color cA, out Color cB);
        AwardMinigameCornerReward(cornerMultiplier, orthoPos, cA, cB);
    }

    public void AwardMinigameCornerReward(int cornerMultiplier, Vector3 orthoPos, Color particleColorA, Color particleColorB)
    {
        DVDLogoBouncer master = MainBouncer;
        decimal amount = GetCornerHitRewardAmount() * Mathf.Max(1, cornerMultiplier);
        master.currentMoney += amount;
        master.allTimeMoneyEarned += amount;
        if (RunSaveManager.HasInstance) RunSaveManager.Instance.RecordMoneyEarned(amount);
        master.UpdateMoneyUI();
        master.SpawnPopup(amount, false);
        master.TriggerVisualBounce(master.cornerBounceMultiplier);
        TriggerHitParticlesWithColors(orthoPos, particleColorA, particleColorB);
    }

    public void ApplyMinigameCornerPenalty(int cornerMultiplier, Vector3 orthoPos)
    {
        GetLogoTwoColors(out Color cA, out Color cB);
        ApplyMinigameCornerPenalty(cornerMultiplier, orthoPos, cA, cB);
    }

    public void ApplyMinigameCornerPenalty(int cornerMultiplier, Vector3 orthoPos, Color particleColorA, Color particleColorB)
    {
        DVDLogoBouncer master = MainBouncer;
        decimal penalty = GetCornerHitRewardAmount() * Mathf.Max(1, cornerMultiplier);
        master.currentMoney -= penalty;
        if (master.currentMoney < 0m) master.currentMoney = 0m;
        master.UpdateMoneyUI();
        master.SpawnPopup(penalty, true);
        TriggerHitParticlesWithColors(orthoPos, particleColorA, particleColorB);
    }

    public void TriggerDamageFlash()
    {
        if (activeDamageFlashCoroutine != null)
        {
            StopCoroutine(activeDamageFlashCoroutine);
        }
        activeDamageFlashCoroutine = StartCoroutine(DamageFlashRoutine());
    }

    private IEnumerator DamageFlashRoutine()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(false);
        List<Renderer> validRenderers = new List<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            if (r.gameObject == edgeEffectTemplate || r.gameObject == cornerEffectTemplate) continue;
            string n = r.gameObject.name.ToLowerInvariant();
            if (n == "edge" || n == "corner") continue;
            validRenderers.Add(r);
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Color flashRed = new Color(1f, 0.15f, 0.15f, 1f);
        Color flashWhite = Color.white;

        for (int pulse = 0; pulse < 3; pulse++)
        {
            Color c = (pulse % 2 == 0) ? flashRed : flashWhite;
            for (int i = 0; i < validRenderers.Count; i++)
            {
                Renderer r = validRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", c);
                block.SetColor("_Color", c);
                r.SetPropertyBlock(block);
            }
            yield return new WaitForSeconds(0.065f);

            for (int i = 0; i < validRenderers.Count; i++)
            {
                if (validRenderers[i] != null) validRenderers[i].SetPropertyBlock(null);
            }
            yield return new WaitForSeconds(0.045f);
        }

        for (int i = 0; i < validRenderers.Count; i++)
        {
            if (validRenderers[i] != null) validRenderers[i].SetPropertyBlock(null);
        }

        activeDamageFlashCoroutine = null;
    }
}
