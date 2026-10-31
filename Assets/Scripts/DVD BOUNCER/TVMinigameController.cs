using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the 4 Remote-activated TV Minigames inside the SCREENTRANSFER RenderTexture:
/// - Mode 1 (Remote Tier 4 - "Dino Jump Remote"):
///     Google No-Internet style runner with Ground Obstacles and Flying Birds!
///     Clicking the TV screen jumps the DVDLogo.
///     Passing an enemy safely awards +1x CornerHitValue.
///     Hitting an enemy does NOT end the game; it flashes the logo, squishes it, and deducts 3x CornerHitValue!
/// - Mode 2 (Remote Tier 5 - "Pong Paddle Remote"):
///     One-sided Pong defender! Top and side walls remain regular bounces.
///     Moving/dragging the cursor horizontally moves a paddle bar along the bottom of the screen.
///     Defending with the paddle bounces the logo upward and awards +1x CornerHitValue.
///     Missing the paddle (hitting the bottom wall) flashes the logo, bounces it back up, and deducts 3x CornerHitValue!
/// - Mode 3 (Remote Tier 6 - "Suika Merger Remote"):
///     Suika / Fruit Merger game! Drop DVD logos from the top; each size tier has its own fixed scale & two-tone color pair.
///     Next piece to drop is randomly chosen from tiers the player has already unlocked.
///     Same-size logos combine with a punchy pop, squash/stretch, circular dual-color particles, and exponential money (CornerHitValue * 2^tier).
///     If the screen fills up to the top overflow line, all logos merge back into the smallest logo and the game resets.
///     Putting down the remote before game over remembers the board state and merges all logos back into the regular bouncing logo;
///     picking the remote back up splits the main logo back out into the remembered board!
/// - Mode 4 (Remote Tier 7 - "Tower Stack Remote"):
///     Deformed-logo Tetris / Tower Stacker! Logos of different vertex-deformed shapes & sizes swing back and forth at the top.
///     Clicking drops the piece onto the tower (+1x CornerHitValue per stacked block).
///     As the tower grows, the stack scrolls downward out of the bottom of the screen so it never hits the top while keeping all blocks alive below.
///     When the huge tower is about to fall, all logos shrink and shift into view (keeping bgvideo untouched!) to show the entire tower topple, then resets!
/// </summary>
public class TVMinigameController : MonoBehaviour
{
    public static TVMinigameController Instance { get; private set; }

    public enum MinigameMode
    {
        None = 0,
        DinoRunner = 1,
        OneSidedPong = 2,
        SuikaMerge = 3,
        TowerStack = 4
    }

    [Header("References")]
    public DVDLogoBouncer mainBouncer;
    public Camera orthoCamera;

    [Header("Remote Tier Mapping")]
    [Tooltip("Remote tier index that activates the Google No-Internet Dino & Bird Runner when held in TV Focus Mode.")]
    public int dinoRunnerRemoteTier = 4;
    [Tooltip("Remote tier index that activates One-Sided Pong when held in TV Focus Mode.")]
    public int pongRemoteTier = 5;
    [Tooltip("Remote tier index that activates the Suika Logo Merger game when held in TV Focus Mode.")]
    public int suikaRemoteTier = 6;
    [Tooltip("Remote tier index that activates the Tower Stack / Deform Tetris game when held in TV Focus Mode.")]
    public int towerRemoteTier = 7;

    [Header("Minigame 1: Dino & Bird Runner Settings")]
    [Tooltip("Optional custom 3D prefab for ground obstacles (cacti/blocks). Uses procedural primitive cactus if left empty.")]
    public GameObject customGroundObstaclePrefab;
    [Tooltip("Optional custom 3D prefab for flying bird obstacles. Uses procedural primitive flapping bird if left empty.")]
    public GameObject customBirdObstaclePrefab;
    [Tooltip("Horizontal X offset from the left edge of the TV screen where the DVDLogo runs.")]
    public float dinoLogoXOffsetFromLeft = 2.4f;
    [Tooltip("Vertical Y offset from the bottom edge of the TV screen for the runner ground line.")]
    public float dinoGroundOffsetFromBottom = 1.15f;
    [Tooltip("Upward jump velocity applied when clicking the TV screen in Dino Runner mode.")]
    public float dinoJumpVelocity = 12.8f;
    [Tooltip("Downward gravity acceleration in Dino Runner mode.")]
    public float dinoGravity = 30.0f;
    [Tooltip("Squash & stretch intensity when jumping in Dino Runner mode (lower = subtler hop stretch).")]
    [Range(0.02f, 0.5f)]
    public float dinoJumpSquashIntensity = 0.12f;
    [Tooltip("Squash & stretch intensity when landing back on the ground in Dino Runner mode (lower = subtler landing settle).")]
    [Range(0.02f, 0.5f)]
    public float dinoLandSquashIntensity = 0.10f;
    [Tooltip("Squash & stretch intensity when hitting an obstacle in Dino Runner mode.")]
    [Range(0.05f, 0.6f)]
    public float dinoDamageSquashIntensity = 0.24f;
    [Tooltip("Horizontal scroll speed of incoming ground obstacles and birds.")]
    public float dinoObstacleSpeed = 5.6f;
    [Tooltip("Minimum and maximum seconds between obstacle spawns.")]
    public Vector2 dinoSpawnIntervalRange = new Vector2(1.15f, 2.15f);
    [Tooltip("Chance (0 to 1) that a spawned obstacle is a flying bird instead of a ground cactus.")]
    [Range(0f, 1f)]
    public float birdSpawnChance = 0.42f;
    [Tooltip("Scale multiplier for spawned ground obstacles.")]
    public Vector3 groundObstacleScale = new Vector3(0.45f, 0.85f, 0.35f);
    [Tooltip("Scale multiplier for spawned bird obstacles.")]
    public Vector3 birdObstacleScale = new Vector3(0.65f, 0.32f, 0.35f);
    public Color groundObstacleColor = new Color(0.25f, 0.95f, 0.45f, 1f);
    public Color birdObstacleColor = new Color(1.0f, 0.65f, 0.2f, 1f);
    public Color groundLineColor = new Color(0.75f, 0.85f, 1.0f, 0.65f);

    [Header("Minigame 2: One-Sided Pong Settings")]
    [Tooltip("Optional custom 3D prefab for the Pong paddle bar. Uses procedural glowing bar if left empty.")]
    public GameObject customPongPaddlePrefab;
    [Tooltip("Width of the draggable Pong paddle bar in DVDSetup orthographic units.")]
    public float pongPaddleWidth = 2.45f;
    [Tooltip("Height/thickness of the Pong paddle bar.")]
    public float pongPaddleHeight = 0.28f;
    [Tooltip("Vertical Y offset from the bottom edge of the TV screen for the Pong paddle bar.")]
    public float pongPaddleOffsetFromBottom = 0.72f;
    [Tooltip("How fast the Pong paddle bar follows the cursor horizontally.")]
    public float pongPaddleFollowSpeed = 28f;
    public Color pongPaddleColor = new Color(0.3f, 0.95f, 1.0f, 1f);

    [Header("Minigame 3: Suika Logo Merger Settings")]
    [Tooltip("Downward gravity acceleration for dropped Suika logos.")]
    public float suikaGravity = 18.5f;
    [Tooltip("Cooldown in seconds between dropping Suika logos.")]
    public float suikaDropCooldown = 0.36f;
    [Tooltip("Highest tier index that can randomly appear in the drop spawner once unlocked (0..4 means up to Tier 4 can drop once unlocked).")]
    [Range(1, 6)]
    public int suikaMaxRandomDropTier = 4;
    [Tooltip("Vertical margin below the top of the screen for the Suika overflow/reset line.")]
    public float suikaTopOverflowMargin = 1.25f;
    [Tooltip("Seconds a settled logo must stay above the top overflow line before triggering the board merge-reset.")]
    public float suikaOverflowGraceTime = 0.95f;
    [Tooltip("Bounciness when Suika logos collide with walls or each other.")]
    [Range(0.05f, 0.6f)]
    public float suikaRestitution = 0.22f;
    [Tooltip("Fixed scale multipliers for each Suika size tier (each tier has its own fixed size and fixed colors).")]
    public float[] suikaTierScales = new float[] { 0.38f, 0.52f, 0.68f, 0.86f, 1.06f, 1.28f, 1.52f, 1.80f };
    [Tooltip("Fixed primary ('dvd' letters) colors for each Suika size tier.")]
    public Color[] suikaTierPrimaryColors = new Color[]
    {
        new Color(1.00f, 0.24f, 0.34f, 1f), // Tier 0: Cherry Red
        new Color(1.00f, 0.54f, 0.16f, 1f), // Tier 1: Tangerine Orange
        new Color(0.74f, 0.30f, 1.00f, 1f), // Tier 2: Grape Purple
        new Color(1.00f, 0.82f, 0.16f, 1f), // Tier 3: Sunny Gold
        new Color(0.18f, 0.88f, 1.00f, 1f), // Tier 4: Sky Cyan
        new Color(0.22f, 0.95f, 0.44f, 1f), // Tier 5: Apple Green
        new Color(1.00f, 0.32f, 0.78f, 1f), // Tier 6: Peach Magenta
        new Color(1.00f, 0.95f, 0.32f, 1f)  // Tier 7: Watermelon Gold
    };
    [Tooltip("Fixed secondary ('dvd/Torus' ring) colors for each Suika size tier.")]
    public Color[] suikaTierSecondaryColors = new Color[]
    {
        new Color(1.00f, 0.68f, 0.75f, 1f), // Tier 0: Soft Rose
        new Color(1.00f, 0.88f, 0.38f, 1f), // Tier 1: Warm Amber
        new Color(0.90f, 0.72f, 1.00f, 1f), // Tier 2: Lavender
        new Color(1.00f, 0.96f, 0.78f, 1f), // Tier 3: Cream White
        new Color(0.18f, 0.46f, 0.98f, 1f), // Tier 4: Deep Azure
        new Color(0.72f, 1.00f, 0.84f, 1f), // Tier 5: Mint Glow
        new Color(0.62f, 0.22f, 0.96f, 1f), // Tier 6: Royal Violet
        new Color(0.16f, 0.94f, 0.52f, 1f)  // Tier 7: Watermelon Rind Green
    };

    [Header("Minigame 4: Tower Stack / Deform Tetris Settings")]
    [Tooltip("Horizontal speed of the swinging logo at the top of the screen.")]
    public float towerTopSwingSpeed = 5.2f;
    [Tooltip("Downward gravity acceleration when a tower piece is dropped.")]
    public float towerDropGravity = 26.0f;
    [Tooltip("Fraction of screen height (0..1) above which the stacked tower scrolls downward so it never reaches the top.")]
    [Range(0.30f, 0.68f)]
    public float towerScrollTriggerHeightRatio = 0.45f;
    [Tooltip("How fast the tower scrolls downward as new blocks are stacked.")]
    public float towerScrollLerpSpeed = 6.5f;
    [Tooltip("Duration in seconds of the shrink-and-topple animation when the huge tower collapses.")]
    public float towerToppleDuration = 1.55f;
    [Tooltip("How forgiving the horizontal center-of-mass balance is before the tower tips over (higher = easier to stack tall).")]
    [Range(0.55f, 1.35f)]
    public float towerBalanceForgiveness = 0.92f;

    [Header("Minigame Audio")]
    public AudioSource audioSource;
    public AudioClip jumpSound;
    public AudioClip enemyPassSound;
    public AudioClip damageHitSound;
    public AudioClip pongPaddleHitSound;
    public AudioClip suikaMergeSound;
    public AudioClip towerLandSound;
    [Range(0f, 2f)]
    public float minigameSoundVolume = 0.9f;

    // Runtime state
    private MinigameMode currentMode = MinigameMode.None;
    public MinigameMode CurrentMode => currentMode;
    public bool IsDinoRunnerActive => currentMode == MinigameMode.DinoRunner;
    public bool IsPongActive => currentMode == MinigameMode.OneSidedPong;
    public bool IsSuikaActive => currentMode == MinigameMode.SuikaMerge;
    public bool IsTowerActive => currentMode == MinigameMode.TowerStack;
    public bool OverridesMainLogoMovement =>
        currentMode == MinigameMode.DinoRunner ||
        currentMode == MinigameMode.SuikaMerge ||
        currentMode == MinigameMode.TowerStack;

    // Dino Runner runtime state
    private float dinoVerticalVelocity = 0f;
    private bool dinoIsGrounded = true;
    private float dinoSpawnTimer = 0f;
    private GameObject dinoGroundLineObj;
    private Vector3 savedLogoPositionBeforeRunner;
    private Vector2 savedLogoDirectionBeforeRunner;

    private class RunnerObstacle
    {
        public GameObject root;
        public Transform leftWing;
        public Transform rightWing;
        public bool isBird;
        public float baseY;
        public float halfWidth;
        public float halfHeight;
        public bool passedRewardGiven;
        public bool hitTriggered;
        public float animSeed;
    }

    private readonly List<RunnerObstacle> activeObstacles = new List<RunnerObstacle>();

    // One-Sided Pong runtime state
    private GameObject pongPaddleObj;
    private float targetPaddleX = 0f;
    private float currentPaddleX = 0f;
    private float lastPaddleBounceTime = -10f;

    // =========================================================================
    // SHARED DEFORMABLE TWO-COLOR LOGO VISUAL PIECE (USED BY SUIKA & TOWER)
    // =========================================================================

    private class DeformableLogoPiece
    {
        public GameObject rootObj;
        public Vector3 rootDeformCenter;
        public Vector3 baseTransformScale;
        public Vector2 baseHalfSize;
        public Vector3 baseCenterOffset;
        public Color primaryColor;
        public Color secondaryColor;

        // Shape parameters (1,1,1,0 for normal logo; modified for Tower Stack shapes)
        public float shapeWidthScale = 1f;
        public float shapeHeightScale = 1f;
        public float shapeTopTaper = 1f;
        public float shapeSkewX = 0f;

        // Dynamic squash & stretch state
        public float squashTimer = -1f;
        public float squashDuration = 0.48f;
        public float squashIntensity = 0.22f;
        public Vector2 squashNormal = Vector2.up;

        // Pop animation state
        public float popTimer = -1f;
        public float popDuration = 0.32f;
        public float popPeakAdd = 0.36f;
        public float overallScaleFactor = 1f;

        public class ClonedPart
        {
            public MeshFilter meshFilter;
            public Mesh meshInstance;
            public Vector3[] originalLocalVerts;
            public Vector3[] rootSpaceVerts;
            public Vector3[] deformedLocalVerts;
            public Matrix4x4 rootToPartMatrix;
        }

        public readonly List<ClonedPart> parts = new List<ClonedPart>();
        public readonly List<Renderer> primaryRenderers = new List<Renderer>();
        public readonly List<Renderer> secondaryRenderers = new List<Renderer>();
        private float minRootY = -0.25f;
        private float maxRootY = 0.25f;

        public void TriggerSquash(Vector2 normal, float intensity = 0.22f, float duration = 0.46f)
        {
            squashNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.up;
            squashIntensity = Mathf.Clamp(intensity, 0.03f, 0.55f);
            squashDuration = Mathf.Max(0.15f, duration);
            squashTimer = 0f;
        }

        public void TriggerPop(float peakAdd = 0.36f, float duration = 0.32f)
        {
            popPeakAdd = peakAdd;
            popDuration = Mathf.Max(0.12f, duration);
            popTimer = 0f;
        }

        public void SetCenterPosition(Vector3 worldCenterPos)
        {
            if (rootObj == null) return;
            Vector3 offset = baseCenterOffset * overallScaleFactor;
            rootObj.transform.position = new Vector3(
                worldCenterPos.x - offset.x,
                worldCenterPos.y - offset.y,
                worldCenterPos.z);
        }

        public Vector3 GetCenterPosition()
        {
            if (rootObj == null) return Vector3.zero;
            return rootObj.transform.position + baseCenterOffset * overallScaleFactor;
        }

        public float EffectiveHalfWidth => baseHalfSize.x * overallScaleFactor * shapeWidthScale * Mathf.Max(1f, shapeTopTaper);
        public float EffectiveHalfHeight => baseHalfSize.y * overallScaleFactor * shapeHeightScale;

        public void ComputeVerticalSpan()
        {
            minRootY = rootDeformCenter.y - 0.1f;
            maxRootY = rootDeformCenter.y + 0.1f;
            for (int p = 0; p < parts.Count; p++)
            {
                Vector3[] rv = parts[p].rootSpaceVerts;
                for (int i = 0; i < rv.Length; i++)
                {
                    if (rv[i].y < minRootY) minRootY = rv[i].y;
                    if (rv[i].y > maxRootY) maxRootY = rv[i].y;
                }
            }
        }

        public void UpdateVisuals(float dt)
        {
            if (rootObj == null) return;

            float popMult = 1f;
            if (popTimer >= 0f)
            {
                popTimer += dt;
                float pNorm = popTimer / popDuration;
                if (pNorm >= 1f)
                {
                    popTimer = -1f;
                }
                else
                {
                    popMult = 1f + popPeakAdd * Mathf.Sin(pNorm * Mathf.PI * 2f) * (1f - pNorm);
                }
            }

            rootObj.transform.localScale = new Vector3(
                baseTransformScale.x * overallScaleFactor * popMult,
                baseTransformScale.y * overallScaleFactor * popMult,
                baseTransformScale.z * overallScaleFactor);

            float deform = 0f;
            if (squashTimer >= 0f)
            {
                squashTimer += dt;
                float normT = squashTimer / squashDuration;
                if (normT >= 1f)
                {
                    squashTimer = -1f;
                }
                else
                {
                    float envelope = Mathf.Exp(-2.6f * normT) * (1f - normT);
                    float wave = Mathf.Cos(normT * Mathf.PI * 2f * 2.0f);
                    deform = squashIntensity * envelope * wave;
                }
            }

            float parallelScale = Mathf.Clamp(1f - deform, 0.45f, 1.65f);
            float perpScale = 1f / parallelScale;

            Vector2 n = squashNormal.sqrMagnitude > 0.0001f ? squashNormal.normalized : Vector2.up;
            Vector2 t = new Vector2(-n.y, n.x);
            float spanY = Mathf.Max(0.01f, maxRootY - minRootY);

            for (int p = 0; p < parts.Count; p++)
            {
                ClonedPart part = parts[p];
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

                    // 1. Apply persistent block shape deformation (width, height, top taper, horizontal skew)
                    float v01 = Mathf.Clamp01((rv.y - minRootY) / spanY);
                    float taper = Mathf.Lerp(1f, shapeTopTaper, v01);
                    float shapedDx = dx * shapeWidthScale * taper + dy * shapeSkewX;
                    float shapedDy = dy * shapeHeightScale;

                    // 2. Apply dynamic impact squash & stretch along squashNormal
                    float dn = (shapedDx * n.x + shapedDy * n.y) * parallelScale;
                    float dtProj = (shapedDx * t.x + shapedDy * t.y) * perpScale;

                    Vector3 deformedRoot = new Vector3(
                        rootDeformCenter.x + n.x * dn + t.x * dtProj,
                        rootDeformCenter.y + n.y * dn + t.y * dtProj,
                        rv.z);

                    defVerts[i] = rootToPart.MultiplyPoint3x4(deformedRoot);
                }

                part.meshInstance.vertices = defVerts;
                part.meshInstance.RecalculateBounds();
            }
        }

        public void DestroyPiece()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].meshInstance != null)
                {
                    Destroy(parts[i].meshInstance);
                }
            }
            parts.Clear();
            if (rootObj != null)
            {
                Destroy(rootObj);
                rootObj = null;
            }
        }
    }

    // =========================================================================
    // MINIGAME 3: SUIKA LOGO MERGER STATE & PERSISTENCE
    // =========================================================================

    private struct SavedSuikaPieceState
    {
        public int tier;
        public Vector2 centerPos;
        public Vector2 velocity;
    }

    private class SuikaPiece
    {
        public int tier;
        public DeformableLogoPiece visual;
        public Vector2 centerPos;
        public Vector2 velocity;
        public float radiusX;
        public float radiusY;
        public float aliveTime;
        public float aboveOverflowTimer;
        public bool isMergingOrAnimating;
        public Vector2 splitStartPos;
        public Vector2 splitTargetPos;
    }

    private readonly List<SuikaPiece> activeSuikaPieces = new List<SuikaPiece>();
    private readonly List<SavedSuikaPieceState> savedSuikaBoard = new List<SavedSuikaPieceState>();
    private bool hasSavedSuikaState = false;
    private int highestUnlockedSuikaTier = 0;
    private int currentSuikaPreviewTier = 0;
    private DeformableLogoPiece suikaPreviewVisual;
    private GameObject suikaOverflowLineObj;
    private float suikaDropCooldownTimer = 0f;
    private float suikaSplitIntroTimer = 0f;
    private const float SuikaSplitIntroDuration = 0.32f;
    private bool isSuikaResettingBoard = false;
    private float suikaResetAnimTimer = 0f;
    private Vector2 suikaResetMergeTarget;

    // =========================================================================
    // MINIGAME 4: TOWER STACK / DEFORM TETRIS STATE
    // =========================================================================

    private struct TowerShapePreset
    {
        public string name;
        public float widthScale;
        public float heightScale;
        public float topTaper;
        public float skewX;
        public Color primaryColor;
        public Color secondaryColor;
    }

    private static readonly TowerShapePreset[] TowerShapes = new TowerShapePreset[]
    {
        // 0: Classic Brick
        new TowerShapePreset
        {
            name = "ClassicBrick", widthScale = 1.05f, heightScale = 0.95f, topTaper = 1.0f, skewX = 0f,
            primaryColor = new Color(0.22f, 0.92f, 1.0f, 1f), secondaryColor = new Color(0.15f, 0.45f, 0.98f, 1f)
        },
        // 1: Wide Plank
        new TowerShapePreset
        {
            name = "WidePlank", widthScale = 1.48f, heightScale = 0.68f, topTaper = 1.0f, skewX = 0f,
            primaryColor = new Color(0.28f, 0.98f, 0.48f, 1f), secondaryColor = new Color(0.82f, 1.0f, 0.35f, 1f)
        },
        // 2: Tall Pillar
        new TowerShapePreset
        {
            name = "TallPillar", widthScale = 0.68f, heightScale = 1.42f, topTaper = 1.0f, skewX = 0f,
            primaryColor = new Color(1.0f, 0.56f, 0.18f, 1f), secondaryColor = new Color(1.0f, 0.88f, 0.32f, 1f)
        },
        // 3: Heavy Mega Block
        new TowerShapePreset
        {
            name = "HeavyMega", widthScale = 1.32f, heightScale = 1.22f, topTaper = 1.0f, skewX = 0f,
            primaryColor = new Color(0.82f, 0.32f, 1.0f, 1f), secondaryColor = new Color(1.0f, 0.65f, 0.92f, 1f)
        },
        // 4: Trapezoid Wedge (Deformed narrower top / wider base)
        new TowerShapePreset
        {
            name = "TrapezoidWedge", widthScale = 1.24f, heightScale = 1.02f, topTaper = 0.78f, skewX = 0f,
            primaryColor = new Color(1.0f, 0.28f, 0.46f, 1f), secondaryColor = new Color(1.0f, 0.75f, 0.35f, 1f)
        },
        // 5: Skewed Tetris Slant (Vertex-sheared horizontal slant)
        new TowerShapePreset
        {
            name = "SkewedSlant", widthScale = 1.12f, heightScale = 0.96f, topTaper = 1.0f, skewX = 0.22f,
            primaryColor = new Color(1.0f, 0.92f, 0.24f, 1f), secondaryColor = new Color(0.24f, 0.96f, 0.68f, 1f)
        }
    };

    private class TowerBlock
    {
        public DeformableLogoPiece visual;
        public int shapeIndex;
        public float towerRelX;   // X relative to screen center
        public float towerRelY;   // Y height above the tower base floor (unscrolled)
        public float halfWidth;
        public float halfHeight;
        public float toppleVelX;
        public float toppleVelY;
        public float toppleAngularVel;
        public float currentAngleZ;
    }

    private readonly List<TowerBlock> stackedTowerBlocks = new List<TowerBlock>();
    private TowerBlock activeSwingingBlock;
    private bool isActiveBlockFalling = false;
    private float activeBlockFallVelY = 0f;
    private float activeSwingDirection = 1f;
    private float currentTowerScrollY = 0f;
    private float targetTowerScrollY = 0f;
    private float towerSwayPhase = 0f;
    private float towerCumulativeInstability = 0f;
    private bool isTowerToppling = false;
    private float towerToppleTimer = 0f;
    private float towerToppleDirection = 1f;
    private float towerToppleTargetFitScale = 1f;
    private float towerToppleStartScrollY = 0f;

    private Material sharedUnlitTemplate;

    private void Awake()
    {
        Instance = this;
        AutoResolveReferences();
    }

    private void AutoResolveReferences()
    {
        if (mainBouncer == null)
        {
            DVDLogoBouncer[] bouncers = FindObjectsOfType<DVDLogoBouncer>(true);
            for (int i = 0; i < bouncers.Length; i++)
            {
                if (!bouncers[i].isChildLogo)
                {
                    mainBouncer = bouncers[i];
                    break;
                }
            }
        }

        if (orthoCamera == null && mainBouncer != null)
        {
            orthoCamera = mainBouncer.renderCamera;
        }

        if (audioSource == null && mainBouncer != null)
        {
            audioSource = mainBouncer.audioSource;
        }

        if (jumpSound == null && mainBouncer != null) jumpSound = mainBouncer.edgeHitSound;
        if (enemyPassSound == null && mainBouncer != null) enemyPassSound = mainBouncer.cornerHitSound;
        if (damageHitSound == null && mainBouncer != null) damageHitSound = mainBouncer.directHitSound;
        if (pongPaddleHitSound == null && mainBouncer != null) pongPaddleHitSound = mainBouncer.cornerHitSound;
        if (suikaMergeSound == null && mainBouncer != null)
            suikaMergeSound = mainBouncer.recombinePopSound != null ? mainBouncer.recombinePopSound : mainBouncer.cornerHitSound;
        if (towerLandSound == null && mainBouncer != null) towerLandSound = mainBouncer.edgeHitSound;
    }

    private void Update()
    {
        if (Time.timeScale <= 0.0001f || (PauseManager.HasInstance && PauseManager.IsGamePaused))
        {
            return;
        }

        AutoResolveReferences();

        MinigameMode desiredMode = DetermineDesiredMinigameMode();
        if (desiredMode != currentMode)
        {
            SwitchMinigameMode(desiredMode);
        }

        float dt = Time.deltaTime;
        if (currentMode == MinigameMode.DinoRunner)
        {
            UpdateDinoRunner(dt);
        }
        else if (currentMode == MinigameMode.OneSidedPong)
        {
            UpdateOneSidedPong(dt);
        }
        else if (currentMode == MinigameMode.SuikaMerge)
        {
            UpdateSuikaMerge(dt);
        }
        else if (currentMode == MinigameMode.TowerStack)
        {
            UpdateTowerStack(dt);
        }
    }

    private MinigameMode DetermineDesiredMinigameMode()
    {
        if (TVFocusController.Instance == null || !TVFocusController.Instance.IsHoldingRemote)
        {
            return MinigameMode.None;
        }

        int tier = UpgradeManager.Instance != null ? UpgradeManager.Instance.ActiveRemoteTier : 0;
        if (tier == dinoRunnerRemoteTier) return MinigameMode.DinoRunner;
        if (tier == pongRemoteTier) return MinigameMode.OneSidedPong;
        if (tier == suikaRemoteTier) return MinigameMode.SuikaMerge;
        if (tier == towerRemoteTier) return MinigameMode.TowerStack;

        return MinigameMode.None;
    }

    private void SwitchMinigameMode(MinigameMode nextMode)
    {
        // Clean up previous mode
        if (currentMode == MinigameMode.DinoRunner)
        {
            ExitDinoRunnerMode();
        }
        else if (currentMode == MinigameMode.OneSidedPong)
        {
            ExitOneSidedPongMode();
        }
        else if (currentMode == MinigameMode.SuikaMerge)
        {
            ExitSuikaMergeMode();
        }
        else if (currentMode == MinigameMode.TowerStack)
        {
            ExitTowerStackMode();
        }

        currentMode = nextMode;

        // Enter new mode
        if (currentMode == MinigameMode.DinoRunner)
        {
            EnterDinoRunnerMode();
        }
        else if (currentMode == MinigameMode.OneSidedPong)
        {
            EnterOneSidedPongMode();
        }
        else if (currentMode == MinigameMode.SuikaMerge)
        {
            EnterSuikaMergeMode();
        }
        else if (currentMode == MinigameMode.TowerStack)
        {
            EnterTowerStackMode();
        }
    }

    // =========================================================================
    // CURSOR & CLICK INPUT FROM TVFocusController
    // =========================================================================

    /// <summary>
    /// Called every frame by TVFocusController while the cursor is on the TV screen.
    /// </summary>
    public void OnScreenCursorMoved(Vector3 orthoWorldPoint)
    {
        targetPaddleX = orthoWorldPoint.x;
    }

    /// <summary>
    /// Called by TVFocusController when the player left-clicks inside the TV screen.
    /// Returns true if the active minigame consumed the click.
    /// </summary>
    public bool OnScreenClicked(Vector3 orthoWorldPoint)
    {
        targetPaddleX = orthoWorldPoint.x;

        if (currentMode == MinigameMode.DinoRunner)
        {
            TriggerDinoJump();
            return true;
        }

        if (currentMode == MinigameMode.OneSidedPong)
        {
            return true;
        }

        if (currentMode == MinigameMode.SuikaMerge)
        {
            TryDropCurrentSuikaPiece();
            return true;
        }

        if (currentMode == MinigameMode.TowerStack)
        {
            TryDropCurrentTowerBlock();
            return true;
        }

        return false;
    }

    // =========================================================================
    // MINIGAME 1: DINO & BIRD RUNNER
    // =========================================================================

    private void EnterDinoRunnerMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        mainBouncer.DespawnAllChildLogosForMinigame();
        mainBouncer.SetMainLogoRenderersVisible(true);

        savedLogoPositionBeforeRunner = mainBouncer.transform.position;
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float groundY = camMinY + dinoGroundOffsetFromBottom;

        CreateDinoGroundLine(camMinX, camMaxX, groundY);

        Bounds logoBounds = mainBouncer.GetLogoWorldBounds();
        Vector3 pivotToMinOffset = logoBounds.min - mainBouncer.transform.position;
        Vector3 pivotToCenterOffset = logoBounds.center - mainBouncer.transform.position;

        float targetX = camMinX + dinoLogoXOffsetFromLeft - pivotToCenterOffset.x;
        float targetY = groundY - pivotToMinOffset.y;

        mainBouncer.transform.position = new Vector3(targetX, targetY, savedLogoPositionBeforeRunner.z);
        dinoVerticalVelocity = 0f;
        dinoIsGrounded = true;
        dinoSpawnTimer = 0.55f;
    }

    private void ExitDinoRunnerMode()
    {
        ClearAllRunnerObstacles();
        if (dinoGroundLineObj != null)
        {
            Destroy(dinoGroundLineObj);
            dinoGroundLineObj = null;
        }

        if (mainBouncer != null)
        {
            mainBouncer.SetMainLogoRenderersVisible(true);
            mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
        }
    }

    private void TriggerDinoJump()
    {
        if (mainBouncer == null) return;

        GetOrthoBounds(out _, out _, out float camMinY, out _);
        float groundY = camMinY + dinoGroundOffsetFromBottom;
        Bounds logoBounds = mainBouncer.GetLogoWorldBounds();

        if (dinoIsGrounded || logoBounds.min.y <= groundY + 0.35f)
        {
            dinoVerticalVelocity = dinoJumpVelocity;
            dinoIsGrounded = false;
            mainBouncer.TriggerSquashStretch(Vector2.up, dinoJumpSquashIntensity);
            PlaySound(jumpSound, 1.12f);
        }
    }

    private void UpdateDinoRunner(float dt)
    {
        if (mainBouncer == null || orthoCamera == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float groundY = camMinY + dinoGroundOffsetFromBottom;

        Bounds logoBounds = mainBouncer.GetLogoWorldBounds();
        Vector3 pivotToMinOffset = logoBounds.min - mainBouncer.transform.position;
        Vector3 pivotToMaxOffset = logoBounds.max - mainBouncer.transform.position;
        Vector3 pivotToCenterOffset = logoBounds.center - mainBouncer.transform.position;

        float runnerX = camMinX + dinoLogoXOffsetFromLeft - pivotToCenterOffset.x;
        float currentY = mainBouncer.transform.position.y;

        if (!dinoIsGrounded || dinoVerticalVelocity > 0f)
        {
            currentY += dinoVerticalVelocity * dt;
            dinoVerticalVelocity -= dinoGravity * dt;

            float minPivotY = groundY - pivotToMinOffset.y;
            float maxPivotY = (camMaxY - 0.25f) - pivotToMaxOffset.y;

            if (currentY >= maxPivotY && dinoVerticalVelocity > 0f)
            {
                currentY = maxPivotY;
                dinoVerticalVelocity = 0f;
            }

            if (currentY <= minPivotY)
            {
                currentY = minPivotY;
                dinoVerticalVelocity = 0f;
                if (!dinoIsGrounded)
                {
                    dinoIsGrounded = true;
                    mainBouncer.TriggerSquashStretch(Vector2.up, dinoLandSquashIntensity);
                }
            }
        }
        else
        {
            currentY = groundY - pivotToMinOffset.y;
        }

        mainBouncer.transform.position = new Vector3(runnerX, currentY, mainBouncer.transform.position.z);
        logoBounds = mainBouncer.GetLogoWorldBounds();

        dinoSpawnTimer -= dt;
        if (dinoSpawnTimer <= 0f)
        {
            SpawnRunnerObstacle(camMaxX + 1.1f, groundY, camMaxY);
            dinoSpawnTimer = Random.Range(dinoSpawnIntervalRange.x, dinoSpawnIntervalRange.y);
        }

        for (int i = activeObstacles.Count - 1; i >= 0; i--)
        {
            RunnerObstacle obs = activeObstacles[i];
            if (obs == null || obs.root == null)
            {
                activeObstacles.RemoveAt(i);
                continue;
            }

            Vector3 pos = obs.root.transform.position;
            pos.x -= dinoObstacleSpeed * dt;

            if (obs.isBird)
            {
                float bob = Mathf.Sin(Time.time * 7.5f + obs.animSeed) * 0.14f;
                pos.y = obs.baseY + bob;

                float flapAngle = Mathf.Sin(Time.time * 18f + obs.animSeed) * 32f;
                if (obs.leftWing != null) obs.leftWing.localRotation = Quaternion.Euler(0f, 0f, flapAngle);
                if (obs.rightWing != null) obs.rightWing.localRotation = Quaternion.Euler(0f, 0f, -flapAngle);
            }

            obs.root.transform.position = pos;

            if (!obs.hitTriggered)
            {
                float obsMinX = pos.x - obs.halfWidth;
                float obsMaxX = pos.x + obs.halfWidth;
                float obsMinY = pos.y - obs.halfHeight;
                float obsMaxY = pos.y + obs.halfHeight;

                float padX = 0.06f;
                float padY = 0.05f;

                bool overlapX = (logoBounds.max.x - padX) >= obsMinX && (logoBounds.min.x + padX) <= obsMaxX;
                bool overlapY = (logoBounds.max.y - padY) >= obsMinY && (logoBounds.min.y + padY) <= obsMaxY;

                if (overlapX && overlapY)
                {
                    obs.hitTriggered = true;
                    obs.passedRewardGiven = true;

                    mainBouncer.TriggerDamageFlash();
                    mainBouncer.TriggerSquashStretch(Vector2.right, dinoDamageSquashIntensity);
                    mainBouncer.ApplyMinigameCornerPenalty(3, pos);
                    PlaySound(damageHitSound, 0.78f);

                    Destroy(obs.root);
                    activeObstacles.RemoveAt(i);
                    continue;
                }
            }

            if (!obs.passedRewardGiven && (pos.x + obs.halfWidth) < logoBounds.min.x - 0.1f)
            {
                obs.passedRewardGiven = true;
                mainBouncer.AwardMinigameCornerReward(1, mainBouncer.GetLogoVisualCenter());
                PlaySound(enemyPassSound, 1.15f);
            }

            if (pos.x < camMinX - 1.5f)
            {
                Destroy(obs.root);
                activeObstacles.RemoveAt(i);
            }
        }
    }

    private void SpawnRunnerObstacle(float spawnX, float groundY, float camMaxY)
    {
        if (orthoCamera == null) return;

        bool spawnBird = Random.value < birdSpawnChance;
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : (orthoCamera.transform.position.z + 5f);
        int renderLayer = mainBouncer != null ? mainBouncer.gameObject.layer : 0;

        RunnerObstacle obs = new RunnerObstacle
        {
            isBird = spawnBird,
            animSeed = Random.Range(0f, 10f)
        };

        if (!spawnBird)
        {
            obs.halfWidth = groundObstacleScale.x * 0.5f;
            obs.halfHeight = groundObstacleScale.y * 0.5f;
            obs.baseY = groundY + obs.halfHeight;

            if (customGroundObstaclePrefab != null)
            {
                obs.root = Instantiate(customGroundObstaclePrefab, new Vector3(spawnX, obs.baseY, zPlane), Quaternion.identity, orthoCamera.transform.parent);
                obs.root.transform.localScale = groundObstacleScale;
                SetLayerRecursively(obs.root, renderLayer);
            }
            else
            {
                obs.root = BuildProceduralCactusObstacle(spawnX, obs.baseY, zPlane, renderLayer);
            }
        }
        else
        {
            obs.halfWidth = birdObstacleScale.x * 0.5f;
            obs.halfHeight = birdObstacleScale.y * 0.5f;

            int heightTier = Random.Range(0, 3);
            if (heightTier == 0) obs.baseY = groundY + 0.65f;
            else if (heightTier == 1) obs.baseY = groundY + 1.45f;
            else obs.baseY = Mathf.Min(camMaxY - 0.8f, groundY + 2.25f);

            if (customBirdObstaclePrefab != null)
            {
                obs.root = Instantiate(customBirdObstaclePrefab, new Vector3(spawnX, obs.baseY, zPlane), Quaternion.identity, orthoCamera.transform.parent);
                obs.root.transform.localScale = birdObstacleScale;
                SetLayerRecursively(obs.root, renderLayer);
            }
            else
            {
                obs.root = BuildProceduralBirdObstacle(spawnX, obs.baseY, zPlane, renderLayer, out obs.leftWing, out obs.rightWing);
            }
        }

        activeObstacles.Add(obs);
    }

    private GameObject BuildProceduralCactusObstacle(float x, float y, float z, int layer)
    {
        GameObject root = new GameObject("DinoObstacle_Ground");
        root.layer = layer;
        if (orthoCamera != null && orthoCamera.transform.parent != null)
        {
            root.transform.SetParent(orthoCamera.transform.parent, true);
        }
        root.transform.position = new Vector3(x, y, z);

        Material mat = CreateUnlitColorMaterial(groundObstacleColor);

        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trunk.name = "Trunk";
        trunk.layer = layer;
        Collider tc = trunk.GetComponent<Collider>();
        if (tc != null) Destroy(tc);
        trunk.transform.SetParent(root.transform, false);
        trunk.transform.localPosition = Vector3.zero;
        trunk.transform.localScale = new Vector3(groundObstacleScale.x * 0.55f, groundObstacleScale.y, groundObstacleScale.z);
        trunk.GetComponent<Renderer>().sharedMaterial = mat;

        GameObject leftArm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftArm.name = "LeftArm";
        leftArm.layer = layer;
        Collider lc = leftArm.GetComponent<Collider>();
        if (lc != null) Destroy(lc);
        leftArm.transform.SetParent(root.transform, false);
        leftArm.transform.localPosition = new Vector3(-groundObstacleScale.x * 0.38f, groundObstacleScale.y * 0.1f, 0f);
        leftArm.transform.localScale = new Vector3(groundObstacleScale.x * 0.35f, groundObstacleScale.y * 0.45f, groundObstacleScale.z * 0.8f);
        leftArm.GetComponent<Renderer>().sharedMaterial = mat;

        GameObject rightArm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightArm.name = "RightArm";
        rightArm.layer = layer;
        Collider rc = rightArm.GetComponent<Collider>();
        if (rc != null) Destroy(rc);
        rightArm.transform.SetParent(root.transform, false);
        rightArm.transform.localPosition = new Vector3(groundObstacleScale.x * 0.38f, groundObstacleScale.y * 0.2f, 0f);
        rightArm.transform.localScale = new Vector3(groundObstacleScale.x * 0.35f, groundObstacleScale.y * 0.4f, groundObstacleScale.z * 0.8f);
        rightArm.GetComponent<Renderer>().sharedMaterial = mat;

        return root;
    }

    private GameObject BuildProceduralBirdObstacle(float x, float y, float z, int layer, out Transform leftWing, out Transform rightWing)
    {
        GameObject root = new GameObject("DinoObstacle_Bird");
        root.layer = layer;
        if (orthoCamera != null && orthoCamera.transform.parent != null)
        {
            root.transform.SetParent(orthoCamera.transform.parent, true);
        }
        root.transform.position = new Vector3(x, y, z);

        Material mat = CreateUnlitColorMaterial(birdObstacleColor);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "BirdBody";
        body.layer = layer;
        Collider bc = body.GetComponent<Collider>();
        if (bc != null) Destroy(bc);
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(birdObstacleScale.x, birdObstacleScale.y * 0.65f, birdObstacleScale.z);
        body.GetComponent<Renderer>().sharedMaterial = mat;

        GameObject lw = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lw.name = "Wing_A";
        lw.layer = layer;
        Collider lwc = lw.GetComponent<Collider>();
        if (lwc != null) Destroy(lwc);
        lw.transform.SetParent(root.transform, false);
        lw.transform.localPosition = new Vector3(0f, birdObstacleScale.y * 0.35f, -0.05f);
        lw.transform.localScale = new Vector3(birdObstacleScale.x * 0.55f, birdObstacleScale.y * 0.85f, birdObstacleScale.z * 0.5f);
        lw.GetComponent<Renderer>().sharedMaterial = mat;
        leftWing = lw.transform;

        GameObject rw = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rw.name = "Wing_B";
        rw.layer = layer;
        Collider rwc = rw.GetComponent<Collider>();
        if (rwc != null) Destroy(rwc);
        rw.transform.SetParent(root.transform, false);
        rw.transform.localPosition = new Vector3(0.08f, -birdObstacleScale.y * 0.35f, 0.05f);
        rw.transform.localScale = new Vector3(birdObstacleScale.x * 0.5f, birdObstacleScale.y * 0.75f, birdObstacleScale.z * 0.5f);
        rw.GetComponent<Renderer>().sharedMaterial = mat;
        rightWing = rw.transform;

        return root;
    }

    private void CreateDinoGroundLine(float camMinX, float camMaxX, float groundY)
    {
        if (dinoGroundLineObj != null) Destroy(dinoGroundLineObj);

        int layer = mainBouncer != null ? mainBouncer.gameObject.layer : 0;
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z + 0.05f : 5f;

        dinoGroundLineObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        dinoGroundLineObj.name = "DinoRunner_GroundLine";
        dinoGroundLineObj.layer = layer;
        Collider c = dinoGroundLineObj.GetComponent<Collider>();
        if (c != null) Destroy(c);

        if (orthoCamera != null && orthoCamera.transform.parent != null)
        {
            dinoGroundLineObj.transform.SetParent(orthoCamera.transform.parent, true);
        }

        float width = (camMaxX - camMinX) + 1f;
        dinoGroundLineObj.transform.position = new Vector3((camMinX + camMaxX) * 0.5f, groundY - 0.05f, zPlane);
        dinoGroundLineObj.transform.localScale = new Vector3(width, 0.07f, 0.1f);
        dinoGroundLineObj.GetComponent<Renderer>().sharedMaterial = CreateUnlitColorMaterial(groundLineColor);
    }

    private void ClearAllRunnerObstacles()
    {
        for (int i = 0; i < activeObstacles.Count; i++)
        {
            if (activeObstacles[i] != null && activeObstacles[i].root != null)
            {
                Destroy(activeObstacles[i].root);
            }
        }
        activeObstacles.Clear();
    }

    // =========================================================================
    // MINIGAME 2: ONE-SIDED PONG (PADDLE DEFENDER)
    // =========================================================================

    private void EnterOneSidedPongMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        mainBouncer.SetMainLogoRenderersVisible(true);

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out _);
        float centerX = (camMinX + camMaxX) * 0.5f;
        float paddleY = camMinY + pongPaddleOffsetFromBottom;
        float zPlane = mainBouncer.transform.position.z;
        int layer = mainBouncer.gameObject.layer;

        currentPaddleX = centerX;
        targetPaddleX = centerX;

        if (pongPaddleObj != null) Destroy(pongPaddleObj);

        if (customPongPaddlePrefab != null)
        {
            pongPaddleObj = Instantiate(customPongPaddlePrefab, new Vector3(centerX, paddleY, zPlane), Quaternion.identity, orthoCamera.transform.parent);
            pongPaddleObj.name = "PongPaddle_InScreen";
            pongPaddleObj.transform.localScale = new Vector3(pongPaddleWidth, pongPaddleHeight, 0.35f);
            SetLayerRecursively(pongPaddleObj, layer);
        }
        else
        {
            pongPaddleObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pongPaddleObj.name = "PongPaddle_InScreen";
            pongPaddleObj.layer = layer;
            Collider c = pongPaddleObj.GetComponent<Collider>();
            if (c != null) Destroy(c);

            if (orthoCamera.transform.parent != null)
            {
                pongPaddleObj.transform.SetParent(orthoCamera.transform.parent, true);
            }
            pongPaddleObj.transform.position = new Vector3(centerX, paddleY, zPlane);
            pongPaddleObj.transform.localScale = new Vector3(pongPaddleWidth, pongPaddleHeight, 0.35f);
            pongPaddleObj.GetComponent<Renderer>().sharedMaterial = CreateUnlitColorMaterial(pongPaddleColor);
        }
    }

    private void ExitOneSidedPongMode()
    {
        if (pongPaddleObj != null)
        {
            Destroy(pongPaddleObj);
            pongPaddleObj = null;
        }
    }

    private void UpdateOneSidedPong(float dt)
    {
        if (pongPaddleObj == null || orthoCamera == null || mainBouncer == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out _);
        float halfPaddleW = pongPaddleWidth * 0.5f;
        float clampedTargetX = Mathf.Clamp(targetPaddleX, camMinX + halfPaddleW + 0.1f, camMaxX - halfPaddleW - 0.1f);

        currentPaddleX = Mathf.Lerp(currentPaddleX, clampedTargetX, dt * pongPaddleFollowSpeed);
        float paddleY = camMinY + pongPaddleOffsetFromBottom;
        float zPlane = mainBouncer.transform.position.z;

        pongPaddleObj.transform.position = new Vector3(currentPaddleX, paddleY, zPlane);

        CheckPongPaddleCollisionForLogo(mainBouncer, currentPaddleX, paddleY, halfPaddleW, pongPaddleHeight * 0.5f);
    }

    private void CheckPongPaddleCollisionForLogo(DVDLogoBouncer logo, float paddleX, float paddleY, float halfW, float halfH)
    {
        if (logo == null) return;
        if (Time.time - lastPaddleBounceTime < 0.08f) return;

        Vector2 dir = logo.GetDirectionVector();
        if (dir.y >= 0f) return;

        Bounds b = logo.GetLogoWorldBounds();
        float paddleTop = paddleY + halfH;
        float paddleBottom = paddleY - halfH;
        float paddleLeft = paddleX - halfW;
        float paddleRight = paddleX + halfW;

        bool overlapX = b.max.x >= paddleLeft && b.min.x <= paddleRight;
        bool overlapY = b.min.y <= paddleTop && b.max.y >= paddleBottom;

        if (overlapX && overlapY)
        {
            lastPaddleBounceTime = Time.time;

            float hitOffsetNormalized = Mathf.Clamp((b.center.x - paddleX) / Mathf.Max(0.1f, halfW), -0.85f, 0.85f);
            Vector2 bounceDir = new Vector2(hitOffsetNormalized, Mathf.Sqrt(Mathf.Max(0.2f, 1f - hitOffsetNormalized * hitOffsetNormalized))).normalized;

            float lift = (paddleTop - b.min.y) + 0.03f;
            if (lift > 0f)
            {
                logo.transform.position += new Vector3(0f, lift, 0f);
            }

            logo.ReflectFromPongPaddle(bounceDir);
            logo.AwardMinigameCornerReward(1, b.center);
            PlaySound(pongPaddleHitSound, 1.08f);
        }
    }

    // =========================================================================
    // MINIGAME 3: SUIKA LOGO MERGER (WITH STATE PERSISTENCE ON REMOTE PUT-DOWN)
    // =========================================================================

    private void EnterSuikaMergeMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        mainBouncer.DespawnAllChildLogosForMinigame();
        savedLogoPositionBeforeRunner = mainBouncer.transform.position;
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();

        // Hide the single bouncing logo renderer while Suika pieces represent the logos on screen
        mainBouncer.SetMainLogoRenderersVisible(false);

        GetOrthoBounds(out float camMinX, out float camMaxX, out _, out float camMaxY);
        float overflowY = camMaxY - suikaTopOverflowMargin;
        CreateSuikaOverflowLine(camMinX, camMaxX, overflowY);

        isSuikaResettingBoard = false;
        suikaDropCooldownTimer = 0.15f;

        Vector2 originCenter = mainBouncer.GetLogoCenter();

        if (hasSavedSuikaState && savedSuikaBoard.Count > 0)
        {
            // Restore saved board state: split the main logo out into all remembered Suika pieces!
            suikaSplitIntroTimer = SuikaSplitIntroDuration;
            for (int i = 0; i < savedSuikaBoard.Count; i++)
            {
                SavedSuikaPieceState st = savedSuikaBoard[i];
                SuikaPiece piece = SpawnSuikaPieceInternal(st.tier, originCenter);
                piece.splitStartPos = originCenter;
                piece.splitTargetPos = st.centerPos;
                piece.velocity = st.velocity;
                piece.isMergingOrAnimating = true;
                piece.visual.TriggerPop(0.32f, 0.34f);
                piece.visual.TriggerSquash((st.centerPos - originCenter).normalized, 0.22f, 0.42f);
            }
            PlaySound(suikaMergeSound, 1.15f);
        }
        else
        {
            highestUnlockedSuikaTier = 0;
            currentSuikaPreviewTier = 0;
            suikaSplitIntroTimer = 0f;
        }

        SpawnOrRefreshSuikaPreviewPiece();
    }

    private void ExitSuikaMergeMode()
    {
        // Save the current Suika board if the game wasn't resetting/over!
        if (!isSuikaResettingBoard && activeSuikaPieces.Count > 0)
        {
            savedSuikaBoard.Clear();
            for (int i = 0; i < activeSuikaPieces.Count; i++)
            {
                SuikaPiece p = activeSuikaPieces[i];
                if (p == null || p.visual == null) continue;
                Vector2 savePos = suikaSplitIntroTimer > 0f ? p.splitTargetPos : p.centerPos;
                savedSuikaBoard.Add(new SavedSuikaPieceState
                {
                    tier = p.tier,
                    centerPos = savePos,
                    velocity = Vector2.zero
                });
            }
            hasSavedSuikaState = savedSuikaBoard.Count > 0;
        }

        if (suikaOverflowLineObj != null)
        {
            Destroy(suikaOverflowLineObj);
            suikaOverflowLineObj = null;
        }

        if (suikaPreviewVisual != null)
        {
            suikaPreviewVisual.DestroyPiece();
            suikaPreviewVisual = null;
        }

        // Satisfyingly animate the active Suika pieces merging back into the main bouncing DVD logo!
        if (activeSuikaPieces.Count > 0 && mainBouncer != null && gameObject.activeInHierarchy)
        {
            List<DeformableLogoPiece> piecesToConverge = new List<DeformableLogoPiece>();
            for (int i = 0; i < activeSuikaPieces.Count; i++)
            {
                if (activeSuikaPieces[i] != null && activeSuikaPieces[i].visual != null)
                {
                    piecesToConverge.Add(activeSuikaPieces[i].visual);
                }
            }
            activeSuikaPieces.Clear();
            StartCoroutine(AnimateConvergePiecesIntoMainLogo(piecesToConverge));
        }
        else
        {
            ClearAllActiveSuikaPiecesImmediate();
            if (mainBouncer != null)
            {
                mainBouncer.SetMainLogoRenderersVisible(true);
                mainBouncer.TriggerRecombineBounce();
                mainBouncer.TriggerSquashStretch(Vector2.up, mainBouncer.maxSquashAmount * 0.85f);
                mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
            }
        }
    }

    private IEnumerator AnimateConvergePiecesIntoMainLogo(List<DeformableLogoPiece> convergingVisuals)
    {
        if (mainBouncer != null)
        {
            mainBouncer.SetMainLogoRenderersVisible(true);
            mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
            mainBouncer.TriggerRecombineBounce();
            mainBouncer.TriggerSquashStretch(Vector2.up, mainBouncer.maxSquashAmount * 0.9f);
            PlaySound(suikaMergeSound, 0.95f);
        }

        Vector3[] startPositions = new Vector3[convergingVisuals.Count];
        float[] startScales = new float[convergingVisuals.Count];
        for (int i = 0; i < convergingVisuals.Count; i++)
        {
            if (convergingVisuals[i] != null)
            {
                startPositions[i] = convergingVisuals[i].GetCenterPosition();
                startScales[i] = convergingVisuals[i].overallScaleFactor;
            }
        }

        const float duration = 0.24f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * (3f - 2f * t);
            Vector3 targetCenter = mainBouncer != null ? mainBouncer.GetLogoCenter() : Vector3.zero;

            for (int i = 0; i < convergingVisuals.Count; i++)
            {
                DeformableLogoPiece v = convergingVisuals[i];
                if (v == null || v.rootObj == null) continue;
                v.overallScaleFactor = Mathf.Lerp(startScales[i], 0.25f, smooth);
                v.SetCenterPosition(Vector3.Lerp(startPositions[i], targetCenter, smooth));
                v.UpdateVisuals(Time.deltaTime);
            }
            yield return null;
        }

        for (int i = 0; i < convergingVisuals.Count; i++)
        {
            if (convergingVisuals[i] != null) convergingVisuals[i].DestroyPiece();
        }
    }

    private void ClearAllActiveSuikaPiecesImmediate()
    {
        for (int i = 0; i < activeSuikaPieces.Count; i++)
        {
            if (activeSuikaPieces[i] != null && activeSuikaPieces[i].visual != null)
            {
                activeSuikaPieces[i].visual.DestroyPiece();
            }
        }
        activeSuikaPieces.Clear();
    }

    private void SpawnOrRefreshSuikaPreviewPiece()
    {
        if (suikaPreviewVisual != null)
        {
            suikaPreviewVisual.DestroyPiece();
            suikaPreviewVisual = null;
        }

        if (mainBouncer == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out _, out float camMaxY);
        float dropY = camMaxY - suikaTopOverflowMargin * 0.48f;
        float zPlane = mainBouncer.transform.position.z;

        GetSuikaTierProperties(currentSuikaPreviewTier, out float scaleMult, out Color colA, out Color colB);
        suikaPreviewVisual = CreateDeformableLogoClone($"SuikaPreview_Tier{currentSuikaPreviewTier}", scaleMult, colA, colB);
        if (suikaPreviewVisual != null)
        {
            float clampedX = Mathf.Clamp(targetPaddleX, camMinX + suikaPreviewVisual.EffectiveHalfWidth + 0.08f, camMaxX - suikaPreviewVisual.EffectiveHalfWidth - 0.08f);
            suikaPreviewVisual.SetCenterPosition(new Vector3(clampedX, dropY, zPlane));
            suikaPreviewVisual.TriggerPop(0.28f, 0.26f);
            suikaPreviewVisual.UpdateVisuals(0f);
        }
    }

    private void TryDropCurrentSuikaPiece()
    {
        if (isSuikaResettingBoard || suikaSplitIntroTimer > 0f || suikaDropCooldownTimer > 0f) return;
        if (mainBouncer == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out _, out float camMaxY);
        float overflowY = camMaxY - suikaTopOverflowMargin;
        float dropY = camMaxY - suikaTopOverflowMargin * 0.48f;

        GetSuikaTierProperties(currentSuikaPreviewTier, out float scaleMult, out _, out _);
        Vector2 baseHalf = mainBouncer.GetBaseHalfSize();
        float halfW = baseHalf.x * scaleMult;
        float halfH = baseHalf.y * scaleMult;

        float dropX = Mathf.Clamp(targetPaddleX, camMinX + halfW + 0.08f, camMaxX - halfW - 0.08f);

        // Check if the board is already packed right up to the drop point ("no more room in the screen")
        for (int i = 0; i < activeSuikaPieces.Count; i++)
        {
            SuikaPiece existing = activeSuikaPieces[i];
            if (existing == null || existing.aliveTime < 0.35f) continue;
            float dx = Mathf.Abs(dropX - existing.centerPos.x);
            float dy = Mathf.Abs(dropY - existing.centerPos.y);
            if (dx < (halfW + existing.radiusX) * 0.85f && dy < (halfH + existing.radiusY) * 0.85f &&
                (existing.centerPos.y + existing.radiusY) >= overflowY)
            {
                TriggerSuikaBoardOverflowReset();
                return;
            }
        }

        // Spawn the dropped piece
        SuikaPiece dropped = SpawnSuikaPieceInternal(currentSuikaPreviewTier, new Vector2(dropX, dropY));
        dropped.velocity = new Vector2(Random.Range(-0.25f, 0.25f), -1.8f);
        dropped.visual.TriggerSquash(Vector2.up, 0.14f, 0.36f);
        PlaySound(jumpSound, 1.05f);

        suikaDropCooldownTimer = suikaDropCooldown;

        // Roll the next piece to drop from sizes the player has ALREADY UNLOCKED!
        int maxAllowed = Mathf.Clamp(Mathf.Min(highestUnlockedSuikaTier, suikaMaxRandomDropTier), 0, suikaTierScales.Length - 1);
        currentSuikaPreviewTier = Random.Range(0, maxAllowed + 1);

        if (suikaPreviewVisual != null)
        {
            suikaPreviewVisual.DestroyPiece();
            suikaPreviewVisual = null;
        }
    }

    private SuikaPiece SpawnSuikaPieceInternal(int tier, Vector2 worldCenterPos)
    {
        GetSuikaTierProperties(tier, out float scaleMult, out Color colA, out Color colB);
        DeformableLogoPiece vis = CreateDeformableLogoClone($"SuikaLogo_Tier{tier}", scaleMult, colA, colB);
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : 5f;
        vis.SetCenterPosition(new Vector3(worldCenterPos.x, worldCenterPos.y, zPlane));
        vis.UpdateVisuals(0f);

        SuikaPiece piece = new SuikaPiece
        {
            tier = tier,
            visual = vis,
            centerPos = worldCenterPos,
            velocity = Vector2.zero,
            radiusX = vis.EffectiveHalfWidth,
            radiusY = vis.EffectiveHalfHeight,
            aliveTime = 0f,
            aboveOverflowTimer = 0f,
            isMergingOrAnimating = false
        };

        activeSuikaPieces.Add(piece);
        return piece;
    }

    private void UpdateSuikaMerge(float dt)
    {
        if (mainBouncer == null || orthoCamera == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float overflowY = camMaxY - suikaTopOverflowMargin;
        float dropY = camMaxY - suikaTopOverflowMargin * 0.48f;
        float zPlane = mainBouncer.transform.position.z;

        // 1. Handle Board Overflow Reset Animation ("all the logos merge back into the smallest logo and the game resets")
        if (isSuikaResettingBoard)
        {
            suikaResetAnimTimer += dt;
            float norm = Mathf.Clamp01(suikaResetAnimTimer / 0.42f);
            float smooth = norm * norm * (3f - 2f * norm);

            for (int i = 0; i < activeSuikaPieces.Count; i++)
            {
                SuikaPiece p = activeSuikaPieces[i];
                if (p == null || p.visual == null) continue;
                Vector2 cur = Vector2.Lerp(p.centerPos, suikaResetMergeTarget, smooth);
                GetSuikaTierProperties(0, out float smallestScale, out _, out _);
                GetSuikaTierProperties(p.tier, out float startScale, out _, out _);
                p.visual.overallScaleFactor = Mathf.Lerp(startScale, smallestScale, smooth);
                p.visual.SetCenterPosition(new Vector3(cur.x, cur.y, zPlane));
                p.visual.UpdateVisuals(dt);
            }

            if (norm >= 1f)
            {
                isSuikaResettingBoard = false;
                ClearAllActiveSuikaPiecesImmediate();
                savedSuikaBoard.Clear();
                hasSavedSuikaState = false;
                highestUnlockedSuikaTier = 0;
                currentSuikaPreviewTier = 0;

                // Spawn the single merged smallest logo (Tier 0) on the reset board with a satisfying pop!
                SuikaPiece starter = SpawnSuikaPieceInternal(0, suikaResetMergeTarget);
                starter.visual.TriggerPop(0.45f, 0.38f);
                starter.visual.TriggerSquash(Vector2.up, 0.25f, 0.50f);
                GetSuikaTierProperties(0, out _, out Color c0A, out Color c0B);
                mainBouncer.TriggerHitParticlesWithColors(new Vector3(suikaResetMergeTarget.x, suikaResetMergeTarget.y, zPlane), c0A, c0B);
                PlaySound(suikaMergeSound, 0.88f);

                SpawnOrRefreshSuikaPreviewPiece();
            }
            return;
        }

        // 2. Handle Split-From-Main-Logo Intro Animation when resuming a saved board
        if (suikaSplitIntroTimer > 0f)
        {
            suikaSplitIntroTimer -= dt;
            float t = 1f - Mathf.Clamp01(suikaSplitIntroTimer / SuikaSplitIntroDuration);
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);

            for (int i = 0; i < activeSuikaPieces.Count; i++)
            {
                SuikaPiece p = activeSuikaPieces[i];
                if (p == null || p.visual == null) continue;
                p.centerPos = Vector2.Lerp(p.splitStartPos, p.splitTargetPos, easeOut);
                p.visual.SetCenterPosition(new Vector3(p.centerPos.x, p.centerPos.y, zPlane));
                p.visual.UpdateVisuals(dt);
                if (suikaSplitIntroTimer <= 0f)
                {
                    p.isMergingOrAnimating = false;
                }
            }
            return;
        }

        // 3. Update Top Preview Piece & Drop Cooldown
        if (suikaDropCooldownTimer > 0f)
        {
            suikaDropCooldownTimer -= dt;
            if (suikaDropCooldownTimer <= 0f && suikaPreviewVisual == null)
            {
                SpawnOrRefreshSuikaPreviewPiece();
            }
        }

        if (suikaPreviewVisual != null)
        {
            float halfW = suikaPreviewVisual.EffectiveHalfWidth;
            float previewX = Mathf.Clamp(targetPaddleX, camMinX + halfW + 0.08f, camMaxX - halfW - 0.08f);
            suikaPreviewVisual.SetCenterPosition(new Vector3(previewX, dropY, zPlane));
            suikaPreviewVisual.UpdateVisuals(dt);
        }

        // 4. Integrate Gravity & Velocity for All Active Suika Pieces
        for (int i = 0; i < activeSuikaPieces.Count; i++)
        {
            SuikaPiece p = activeSuikaPieces[i];
            if (p == null || p.visual == null) continue;

            p.aliveTime += dt;
            p.velocity.y -= suikaGravity * dt;
            p.velocity.x *= Mathf.Clamp01(1f - 1.2f * dt); // gentle horizontal damping
            p.centerPos += p.velocity * dt;
        }

        // 5. Iterative 2D Collision Resolution + Same-Size Merging
        const int solverIterations = 4;
        for (int iter = 0; iter < solverIterations; iter++)
        {
            // Wall & Floor constraints
            for (int i = 0; i < activeSuikaPieces.Count; i++)
            {
                SuikaPiece p = activeSuikaPieces[i];
                if (p == null) continue;

                float minX = camMinX + p.radiusX;
                float maxX = camMaxX - p.radiusX;
                float minY = camMinY + p.radiusY;

                if (p.centerPos.x < minX)
                {
                    p.centerPos.x = minX;
                    if (p.velocity.x < -1.5f && iter == 0) p.visual.TriggerSquash(Vector2.right, 0.14f, 0.35f);
                    p.velocity.x = Mathf.Abs(p.velocity.x) * suikaRestitution;
                }
                else if (p.centerPos.x > maxX)
                {
                    p.centerPos.x = maxX;
                    if (p.velocity.x > 1.5f && iter == 0) p.visual.TriggerSquash(Vector2.right, 0.14f, 0.35f);
                    p.velocity.x = -Mathf.Abs(p.velocity.x) * suikaRestitution;
                }

                if (p.centerPos.y < minY)
                {
                    p.centerPos.y = minY;
                    if (p.velocity.y < -2.8f && iter == 0)
                    {
                        float impactSquash = Mathf.Clamp(Mathf.Abs(p.velocity.y) / 48f, 0.08f, 0.22f);
                        p.visual.TriggerSquash(Vector2.up, impactSquash, 0.42f);
                    }
                    p.velocity.y = Mathf.Abs(p.velocity.y) * (suikaRestitution * 0.65f);
                    p.velocity.x *= 0.92f;
                }
            }

            // Piece-to-Piece Collisions & Same-Size Merging
            for (int i = 0; i < activeSuikaPieces.Count; i++)
            {
                SuikaPiece a = activeSuikaPieces[i];
                if (a == null || a.visual == null) continue;

                for (int j = i + 1; j < activeSuikaPieces.Count; j++)
                {
                    SuikaPiece b = activeSuikaPieces[j];
                    if (b == null || b.visual == null) continue;

                    // Elliptical / rounded-box distance check matching logo aspect ratio
                    float sumRx = (a.radiusX + b.radiusX) * 0.92f;
                    float sumRy = (a.radiusY + b.radiusY) * 0.92f;
                    float dx = b.centerPos.x - a.centerPos.x;
                    float dy = b.centerPos.y - a.centerPos.y;

                    float nx = dx / Mathf.Max(0.01f, sumRx);
                    float ny = dy / Mathf.Max(0.01f, sumRy);
                    float normDistSq = nx * nx + ny * ny;

                    if (normDistSq < 1f)
                    {
                        // SAME SIZE COMBINES!
                        if (a.tier == b.tier)
                        {
                            PerformSuikaMerge(i, j);
                            goto EndSolverStep;
                        }

                        // Different sizes push apart & roll off each other like Suika Game
                        float normDist = Mathf.Sqrt(Mathf.Max(0.0001f, normDistSq));
                        float overlap = 1f - normDist;

                        Vector2 pushDir = new Vector2(dx / sumRx, dy / sumRy);
                        if (pushDir.sqrMagnitude < 0.0001f)
                        {
                            pushDir = new Vector2(Random.Range(-0.25f, 0.25f), 1f);
                        }
                        pushDir.Normalize();

                        Vector2 worldSeparation = new Vector2(pushDir.x * sumRx, pushDir.y * sumRy) * (overlap * 0.52f);
                        float massA = (a.tier + 1f);
                        float massB = (b.tier + 1f);
                        float totalMass = massA + massB;
                        float ratioA = massB / totalMass;
                        float ratioB = massA / totalMass;

                        a.centerPos -= worldSeparation * ratioA;
                        b.centerPos += worldSeparation * ratioB;

                        // Slight horizontal slip so stacked logos roll into gaps naturally
                        if (Mathf.Abs(pushDir.x) < 0.35f && Mathf.Abs(dy) > 0.02f)
                        {
                            float slip = (dx >= 0f ? 1f : -1f) * 0.35f * dt;
                            a.velocity.x -= slip * ratioA;
                            b.velocity.x += slip * ratioB;
                        }

                        Vector2 relVel = b.velocity - a.velocity;
                        float velAlongNormal = Vector2.Dot(relVel, pushDir);
                        if (velAlongNormal < 0f)
                        {
                            float jImpulse = -(1f + suikaRestitution) * velAlongNormal;
                            Vector2 impulse = pushDir * jImpulse;
                            a.velocity -= impulse * ratioA;
                            b.velocity += impulse * ratioB;

                            if (iter == 0 && Mathf.Abs(velAlongNormal) > 3.2f)
                            {
                                float sq = Mathf.Clamp(Mathf.Abs(velAlongNormal) / 45f, 0.06f, 0.18f);
                                a.visual.TriggerSquash(pushDir, sq, 0.36f);
                                b.visual.TriggerSquash(-pushDir, sq, 0.36f);
                            }
                        }
                    }
                }
            }
        }
    EndSolverStep:

        // 6. Sync visual transforms, check overflow line, and update deformations
        for (int i = 0; i < activeSuikaPieces.Count; i++)
        {
            SuikaPiece p = activeSuikaPieces[i];
            if (p == null || p.visual == null) continue;

            p.visual.SetCenterPosition(new Vector3(p.centerPos.x, p.centerPos.y, zPlane));
            p.visual.UpdateVisuals(dt);

            // Check if a settled piece is above the top overflow line
            if (p.aliveTime > 0.75f && (p.centerPos.y + p.radiusY * 0.65f) > overflowY && Mathf.Abs(p.velocity.y) < 2.2f)
            {
                p.aboveOverflowTimer += dt;
                if (p.aboveOverflowTimer >= suikaOverflowGraceTime)
                {
                    TriggerSuikaBoardOverflowReset();
                    return;
                }
            }
            else
            {
                p.aboveOverflowTimer = Mathf.Max(0f, p.aboveOverflowTimer - dt * 2f);
            }
        }
    }

    private void PerformSuikaMerge(int indexA, int indexB)
    {
        if (indexA < 0 || indexB < 0 || indexA >= activeSuikaPieces.Count || indexB >= activeSuikaPieces.Count) return;

        SuikaPiece a = activeSuikaPieces[indexA];
        SuikaPiece b = activeSuikaPieces[indexB];
        int mergedTier = a.tier;
        int nextTier = Mathf.Min(mergedTier + 1, suikaTierScales.Length - 1);

        Vector2 midPos = (a.centerPos + b.centerPos) * 0.5f;
        Vector2 midVel = (a.velocity + b.velocity) * 0.4f;

        // Destroy the two smaller pieces
        a.visual.DestroyPiece();
        b.visual.DestroyPiece();
        if (indexB > indexA)
        {
            activeSuikaPieces.RemoveAt(indexB);
            activeSuikaPieces.RemoveAt(indexA);
        }
        else
        {
            activeSuikaPieces.RemoveAt(indexA);
            activeSuikaPieces.RemoveAt(indexB);
        }

        // Unlock this new tier so it can appear in future random drops!
        if (nextTier > highestUnlockedSuikaTier)
        {
            highestUnlockedSuikaTier = nextTier;
        }

        // Spawn the combined larger piece (or pop two max-tier watermelons for massive reward!)
        SuikaPiece combined = SpawnSuikaPieceInternal(nextTier, midPos);
        combined.velocity = midVel + new Vector2(0f, 1.2f);
        combined.visual.TriggerPop(0.42f, 0.36f);
        combined.visual.TriggerSquash(Vector2.up, 0.25f, 0.48f);

        // Exponential money reward: CornerHitValue * 2^mergedTier (1x, 2x, 4x, 8x, 16x, 32x, 64x, 128x!)
        int exponentialCornerMult = 1 << Mathf.Clamp(mergedTier, 0, 10);
        GetSuikaTierProperties(nextTier, out _, out Color colA, out Color colB);
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : 5f;
        mainBouncer.AwardMinigameCornerReward(exponentialCornerMult, new Vector3(midPos.x, midPos.y, zPlane), colA, colB);

        float pitch = Mathf.Clamp(0.92f + nextTier * 0.08f, 0.85f, 1.65f);
        PlaySound(suikaMergeSound, pitch);
    }

    private void TriggerSuikaBoardOverflowReset()
    {
        if (isSuikaResettingBoard) return;
        isSuikaResettingBoard = true;
        suikaResetAnimTimer = 0f;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        suikaResetMergeTarget = new Vector2((camMinX + camMaxX) * 0.5f, camMinY + (camMaxY - camMinY) * 0.35f);

        if (suikaPreviewVisual != null)
        {
            suikaPreviewVisual.DestroyPiece();
            suikaPreviewVisual = null;
        }

        PlaySound(damageHitSound, 0.88f);
    }

    private void GetSuikaTierProperties(int tier, out float scaleMult, out Color colA, out Color colB)
    {
        int clamped = Mathf.Clamp(tier, 0, suikaTierScales.Length - 1);
        scaleMult = suikaTierScales[clamped];
        colA = suikaTierPrimaryColors != null && suikaTierPrimaryColors.Length > clamped
            ? suikaTierPrimaryColors[clamped]
            : Color.white;
        colB = suikaTierSecondaryColors != null && suikaTierSecondaryColors.Length > clamped
            ? suikaTierSecondaryColors[clamped]
            : Color.cyan;
    }

    private void CreateSuikaOverflowLine(float camMinX, float camMaxX, float overflowY)
    {
        if (suikaOverflowLineObj != null) Destroy(suikaOverflowLineObj);

        int layer = mainBouncer != null ? mainBouncer.gameObject.layer : 0;
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z + 0.06f : 5f;

        suikaOverflowLineObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        suikaOverflowLineObj.name = "Suika_TopOverflowLine";
        suikaOverflowLineObj.layer = layer;
        Collider c = suikaOverflowLineObj.GetComponent<Collider>();
        if (c != null) Destroy(c);

        if (orthoCamera != null && orthoCamera.transform.parent != null)
        {
            suikaOverflowLineObj.transform.SetParent(orthoCamera.transform.parent, true);
        }

        float width = (camMaxX - camMinX) + 0.5f;
        suikaOverflowLineObj.transform.position = new Vector3((camMinX + camMaxX) * 0.5f, overflowY, zPlane);
        suikaOverflowLineObj.transform.localScale = new Vector3(width, 0.045f, 0.05f);
        suikaOverflowLineObj.GetComponent<Renderer>().sharedMaterial = CreateUnlitColorMaterial(new Color(1f, 0.35f, 0.35f, 0.55f));
    }

    // =========================================================================
    // MINIGAME 4: TOWER STACK / DEFORM TETRIS
    // =========================================================================

    private void EnterTowerStackMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        mainBouncer.DespawnAllChildLogosForMinigame();
        savedLogoPositionBeforeRunner = mainBouncer.transform.position;
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();
        mainBouncer.SetMainLogoRenderersVisible(false);

        ResetAndStartNewTower();
    }

    private void ExitTowerStackMode()
    {
        ClearAllTowerBlocks();
        if (mainBouncer != null)
        {
            mainBouncer.SetMainLogoRenderersVisible(true);
            mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
        }
    }

    private void ClearAllTowerBlocks()
    {
        if (activeSwingingBlock != null && activeSwingingBlock.visual != null)
        {
            activeSwingingBlock.visual.DestroyPiece();
        }
        activeSwingingBlock = null;
        isActiveBlockFalling = false;

        for (int i = 0; i < stackedTowerBlocks.Count; i++)
        {
            if (stackedTowerBlocks[i] != null && stackedTowerBlocks[i].visual != null)
            {
                stackedTowerBlocks[i].visual.DestroyPiece();
            }
        }
        stackedTowerBlocks.Clear();
    }

    private void ResetAndStartNewTower()
    {
        ClearAllTowerBlocks();

        currentTowerScrollY = 0f;
        targetTowerScrollY = 0f;
        towerSwayPhase = 0f;
        towerCumulativeInstability = 0f;
        isTowerToppling = false;
        towerToppleTimer = 0f;

        // Spawn the sturdy foundation pedestal block at the bottom center of the screen
        TowerBlock foundation = CreateTowerBlockOfShape(1); // Wide Plank base
        foundation.towerRelX = 0f;
        foundation.towerRelY = foundation.halfHeight + 0.15f;
        foundation.visual.TriggerPop(0.32f, 0.32f);
        stackedTowerBlocks.Add(foundation);

        SyncTowerBlockWorldPositions(0f);
        SpawnNextSwingingTowerBlock();
    }

    private TowerBlock CreateTowerBlockOfShape(int shapeIdx)
    {
        int idx = Mathf.Clamp(shapeIdx, 0, TowerShapes.Length - 1);
        TowerShapePreset preset = TowerShapes[idx];

        DeformableLogoPiece vis = CreateDeformableLogoClone(
            $"TowerLogo_{preset.name}",
            0.78f,
            preset.primaryColor,
            preset.secondaryColor,
            preset.widthScale,
            preset.heightScale,
            preset.topTaper,
            preset.skewX);

        return new TowerBlock
        {
            visual = vis,
            shapeIndex = idx,
            towerRelX = 0f,
            towerRelY = 0f,
            halfWidth = vis.EffectiveHalfWidth,
            halfHeight = vis.EffectiveHalfHeight
        };
    }

    private void SpawnNextSwingingTowerBlock()
    {
        if (activeSwingingBlock != null && activeSwingingBlock.visual != null)
        {
            activeSwingingBlock.visual.DestroyPiece();
        }

        int shapeIdx = Random.Range(0, TowerShapes.Length);
        activeSwingingBlock = CreateTowerBlockOfShape(shapeIdx);
        isActiveBlockFalling = false;
        activeBlockFallVelY = 0f;
        activeSwingDirection = Random.value < 0.5f ? 1f : -1f;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float screenCenterX = (camMinX + camMaxX) * 0.5f;
        float topHoverWorldY = camMaxY - activeSwingingBlock.halfHeight - 0.38f;
        float baseFloorY = camMinY + 0.35f;

        activeSwingingBlock.towerRelX = Random.Range(-1.8f, 1.8f);
        activeSwingingBlock.towerRelY = (topHoverWorldY - baseFloorY) + currentTowerScrollY;
        activeSwingingBlock.visual.TriggerPop(0.28f, 0.28f);

        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : 5f;
        activeSwingingBlock.visual.SetCenterPosition(new Vector3(screenCenterX + activeSwingingBlock.towerRelX, topHoverWorldY, zPlane));
        activeSwingingBlock.visual.UpdateVisuals(0f);
    }

    private void TryDropCurrentTowerBlock()
    {
        if (isTowerToppling || activeSwingingBlock == null || isActiveBlockFalling) return;

        isActiveBlockFalling = true;
        activeBlockFallVelY = -2.5f;
        activeSwingingBlock.visual.TriggerSquash(Vector2.up, 0.14f, 0.32f);
        PlaySound(jumpSound, 1.08f);
    }

    private void UpdateTowerStack(float dt)
    {
        if (mainBouncer == null || orthoCamera == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float screenCenterX = (camMinX + camMaxX) * 0.5f;
        float baseFloorY = camMinY + 0.35f;
        float screenHeight = camMaxY - camMinY;
        float zPlane = mainBouncer.transform.position.z;

        // 1. Handle Tower Topple & Shrink-to-Fit Spectacle (keeps bgvideo untouched while revealing the whole falling tower!)
        if (isTowerToppling)
        {
            towerToppleTimer += dt;
            float norm = Mathf.Clamp01(towerToppleTimer / towerToppleDuration);
            float shrinkProgress = Mathf.Clamp01(norm / 0.42f);
            float smoothShrink = shrinkProgress * shrinkProgress * (3f - 2f * shrinkProgress);

            float currentFitScale = Mathf.Lerp(1f, towerToppleTargetFitScale, smoothShrink);
            currentTowerScrollY = Mathf.Lerp(towerToppleStartScrollY, 0f, smoothShrink);

            for (int i = 0; i < stackedTowerBlocks.Count; i++)
            {
                TowerBlock b = stackedTowerBlocks[i];
                if (b == null || b.visual == null) continue;

                // Higher blocks tip and accelerate faster as the tower topples
                float heightRatio = (float)i / Mathf.Max(1, stackedTowerBlocks.Count - 1);
                if (norm > 0.15f)
                {
                    b.toppleVelX += towerToppleDirection * (3.5f + heightRatio * 12.5f) * dt;
                    b.toppleVelY -= (8.0f + heightRatio * 10.0f) * dt;
                    b.towerRelX += b.toppleVelX * dt;
                    b.towerRelY += b.toppleVelY * dt;
                    b.currentAngleZ += b.toppleAngularVel * dt;
                }

                b.visual.overallScaleFactor = 0.78f * currentFitScale;
                float worldX = screenCenterX + b.towerRelX * currentFitScale;
                float worldY = baseFloorY + (b.towerRelY - currentTowerScrollY) * currentFitScale;

                b.visual.SetCenterPosition(new Vector3(worldX, worldY, zPlane));
                if (b.visual.rootObj != null)
                {
                    b.visual.rootObj.transform.rotation = Quaternion.Euler(0f, 0f, b.currentAngleZ);
                }
                b.visual.UpdateVisuals(dt);
            }

            if (norm >= 1f)
            {
                ResetAndStartNewTower();
            }
            return;
        }

        // 2. Smoothly scroll the stacked tower downward so it never reaches the top of the screen
        currentTowerScrollY = Mathf.Lerp(currentTowerScrollY, targetTowerScrollY, dt * towerScrollLerpSpeed);
        towerSwayPhase += dt * 2.4f;

        // 3. Update the top swinging / falling block
        if (activeSwingingBlock != null && activeSwingingBlock.visual != null)
        {
            if (!isActiveBlockFalling)
            {
                float maxSwingOffset = ((camMaxX - camMinX) * 0.5f) - activeSwingingBlock.halfWidth - 0.18f;
                activeSwingingBlock.towerRelX += activeSwingDirection * towerTopSwingSpeed * dt;
                if (activeSwingingBlock.towerRelX >= maxSwingOffset)
                {
                    activeSwingingBlock.towerRelX = maxSwingOffset;
                    activeSwingDirection = -1f;
                }
                else if (activeSwingingBlock.towerRelX <= -maxSwingOffset)
                {
                    activeSwingingBlock.towerRelX = -maxSwingOffset;
                    activeSwingDirection = 1f;
                }

                float topHoverWorldY = camMaxY - activeSwingingBlock.halfHeight - 0.38f;
                activeSwingingBlock.towerRelY = (topHoverWorldY - baseFloorY) + currentTowerScrollY;

                activeSwingingBlock.visual.SetCenterPosition(new Vector3(screenCenterX + activeSwingingBlock.towerRelX, topHoverWorldY, zPlane));
                activeSwingingBlock.visual.UpdateVisuals(dt);
            }
            else
            {
                activeBlockFallVelY -= towerDropGravity * dt;
                activeSwingingBlock.towerRelY += activeBlockFallVelY * dt;

                TowerBlock topStacked = stackedTowerBlocks.Count > 0 ? stackedTowerBlocks[stackedTowerBlocks.Count - 1] : null;
                float supportTopRelY = topStacked != null ? (topStacked.towerRelY + topStacked.halfHeight) : 0f;
                float fallingBottomRelY = activeSwingingBlock.towerRelY - activeSwingingBlock.halfHeight;

                if (fallingBottomRelY <= supportTopRelY)
                {
                    EvaluateTowerBlockLanding(topStacked, supportTopRelY, baseFloorY, screenHeight, screenCenterX, zPlane);
                }
                else
                {
                    float worldX = screenCenterX + activeSwingingBlock.towerRelX;
                    float worldY = baseFloorY + (activeSwingingBlock.towerRelY - currentTowerScrollY);
                    activeSwingingBlock.visual.SetCenterPosition(new Vector3(worldX, worldY, zPlane));
                    activeSwingingBlock.visual.UpdateVisuals(dt);
                }
            }
        }

        // 4. Update all stacked blocks (including those scrolled below the bottom of the screen)
        SyncTowerBlockWorldPositions(dt);
    }

    private void EvaluateTowerBlockLanding(TowerBlock supportBlock, float supportTopRelY, float baseFloorY, float screenHeight, float screenCenterX, float zPlane)
    {
        if (activeSwingingBlock == null) return;

        bool landedSafely = false;
        float overhangRatio = 0f;

        if (supportBlock != null)
        {
            float dx = activeSwingingBlock.towerRelX - supportBlock.towerRelX;
            float maxSupportSpan = (supportBlock.halfWidth + activeSwingingBlock.halfWidth * 0.45f) * towerBalanceForgiveness;
            overhangRatio = dx / Mathf.Max(0.1f, supportBlock.halfWidth);

            if (Mathf.Abs(dx) <= maxSupportSpan)
            {
                landedSafely = true;
            }
        }
        else
        {
            landedSafely = true;
        }

        // Accumulate overhang instability across the tower
        towerCumulativeInstability = towerCumulativeInstability * 0.78f + overhangRatio * 0.42f;
        if (Mathf.Abs(towerCumulativeInstability) > 1.15f * towerBalanceForgiveness)
        {
            landedSafely = false;
        }

        if (landedSafely)
        {
            // Snap cleanly onto the top of the tower!
            activeSwingingBlock.towerRelY = supportTopRelY + activeSwingingBlock.halfHeight;
            activeSwingingBlock.visual.TriggerSquash(Vector2.up, 0.22f, 0.48f);

            // Ripple a subtle squash into the top 2 supporting blocks underneath
            for (int i = stackedTowerBlocks.Count - 1; i >= Mathf.Max(0, stackedTowerBlocks.Count - 2); i--)
            {
                stackedTowerBlocks[i].visual.TriggerSquash(Vector2.up, 0.11f, 0.35f);
            }

            stackedTowerBlocks.Add(activeSwingingBlock);

            float landWorldX = screenCenterX + activeSwingingBlock.towerRelX;
            float landWorldY = baseFloorY + (activeSwingingBlock.towerRelY - currentTowerScrollY);
            Color cA = activeSwingingBlock.visual.primaryColor;
            Color cB = activeSwingingBlock.visual.secondaryColor;
            mainBouncer.AwardMinigameCornerReward(1, new Vector3(landWorldX, landWorldY, zPlane), cA, cB);

            float pitch = Mathf.Clamp(0.95f + stackedTowerBlocks.Count * 0.03f, 0.95f, 1.55f);
            PlaySound(towerLandSound, pitch);

            // Check if the tower top exceeds the scroll trigger height—if so, scroll the tower down!
            float newTowerTopRelY = activeSwingingBlock.towerRelY + activeSwingingBlock.halfHeight;
            float maxVisibleRelHeight = screenHeight * towerScrollTriggerHeightRatio;
            if (newTowerTopRelY > maxVisibleRelHeight)
            {
                targetTowerScrollY = newTowerTopRelY - maxVisibleRelHeight;
            }

            activeSwingingBlock = null;
            isActiveBlockFalling = false;
            SpawnNextSwingingTowerBlock();
        }
        else
        {
            // Missed or tipped the tower! Include the falling block in the topple & trigger shrink-and-topple!
            activeSwingingBlock.towerRelY = supportTopRelY + activeSwingingBlock.halfHeight;
            stackedTowerBlocks.Add(activeSwingingBlock);
            activeSwingingBlock = null;
            isActiveBlockFalling = false;

            TriggerTowerToppleAndShrinkReveal(overhangRatio, baseFloorY, screenHeight, screenCenterX, zPlane);
        }
    }

    private void TriggerTowerToppleAndShrinkReveal(float overhangDir, float baseFloorY, float screenHeight, float screenCenterX, float zPlane)
    {
        isTowerToppling = true;
        towerToppleTimer = 0f;
        towerToppleStartScrollY = currentTowerScrollY;
        towerToppleDirection = Mathf.Abs(overhangDir) > 0.01f ? Mathf.Sign(overhangDir) : (Random.value < 0.5f ? 1f : -1f);

        // Compute total tower height from the very first block (even if scrolled far below the screen) to the top block
        float totalTowerHeight = 1f;
        for (int i = 0; i < stackedTowerBlocks.Count; i++)
        {
            float blockTop = stackedTowerBlocks[i].towerRelY + stackedTowerBlocks[i].halfHeight;
            if (blockTop > totalTowerHeight) totalTowerHeight = blockTop;
        }

        // Compute shrink factor so the ENTIRE huge tower fits inside the TV screen as it topples (without shrinking bgvideo!)
        float availableVerticalSpace = Mathf.Max(2.5f, screenHeight * 0.82f);
        towerToppleTargetFitScale = Mathf.Clamp(availableVerticalSpace / totalTowerHeight, 0.16f, 1.0f);

        for (int i = 0; i < stackedTowerBlocks.Count; i++)
        {
            TowerBlock b = stackedTowerBlocks[i];
            float heightRatio = (float)i / Mathf.Max(1, stackedTowerBlocks.Count - 1);
            b.toppleVelX = towerToppleDirection * (0.8f + heightRatio * 2.8f);
            b.toppleVelY = 0.5f + heightRatio * 1.4f;
            b.toppleAngularVel = -towerToppleDirection * (35f + heightRatio * 110f);
            b.currentAngleZ = 0f;
            b.visual.TriggerSquash(Vector2.right, 0.22f, 0.5f);
        }

        TowerBlock topBlock = stackedTowerBlocks[stackedTowerBlocks.Count - 1];
        Vector3 penaltyPos = new Vector3(screenCenterX + topBlock.towerRelX, baseFloorY + (topBlock.towerRelY - currentTowerScrollY), zPlane);
        mainBouncer.ApplyMinigameCornerPenalty(3, penaltyPos, topBlock.visual.primaryColor, topBlock.visual.secondaryColor);
        PlaySound(damageHitSound, 0.76f);
    }

    private void SyncTowerBlockWorldPositions(float dt)
    {
        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out _);
        float screenCenterX = (camMinX + camMaxX) * 0.5f;
        float baseFloorY = camMinY + 0.35f;
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : 5f;

        for (int i = 0; i < stackedTowerBlocks.Count; i++)
        {
            TowerBlock b = stackedTowerBlocks[i];
            if (b == null || b.visual == null) continue;

            // Subtle organic tower sway proportional to height and instability
            float heightFactor = Mathf.Clamp01(b.towerRelY / 10f);
            float swayX = Mathf.Sin(towerSwayPhase) * towerCumulativeInstability * heightFactor * 0.22f;

            float worldX = screenCenterX + b.towerRelX + swayX;
            float worldY = baseFloorY + (b.towerRelY - currentTowerScrollY);

            b.visual.SetCenterPosition(new Vector3(worldX, worldY, zPlane));
            b.visual.UpdateVisuals(dt);
        }
    }

    // =========================================================================
    // FACTORY FOR DEFORMABLE TWO-COLOR DVD LOGO CLONES
    // =========================================================================

    private DeformableLogoPiece CreateDeformableLogoClone(
        string cloneName,
        float scaleMultiplier,
        Color primaryColor,
        Color secondaryColor,
        float shapeWidth = 1f,
        float shapeHeight = 1f,
        float shapeTopTaper = 1f,
        float shapeSkewX = 0f)
    {
        if (mainBouncer == null) return null;

        List<DVDLogoBouncer.DeformableMeshPart> sourceParts = mainBouncer.GetDeformableParts();
        Vector3 baseScale = mainBouncer.GetInitialLocalScale();

        GameObject rootObj = new GameObject(cloneName);
        int layer = mainBouncer.gameObject.layer;
        rootObj.layer = layer;
        if (mainBouncer.transform.parent != null)
        {
            rootObj.transform.SetParent(mainBouncer.transform.parent, false);
        }
        rootObj.transform.localRotation = mainBouncer.transform.localRotation;
        rootObj.transform.localScale = baseScale * scaleMultiplier;

        DeformableLogoPiece piece = new DeformableLogoPiece
        {
            rootObj = rootObj,
            rootDeformCenter = mainBouncer.GetRootDeformCenter(),
            baseTransformScale = baseScale,
            baseHalfSize = mainBouncer.GetBaseHalfSize(),
            baseCenterOffset = mainBouncer.GetBaseCenterOffset(),
            primaryColor = primaryColor,
            secondaryColor = secondaryColor,
            shapeWidthScale = shapeWidth,
            shapeHeightScale = shapeHeight,
            shapeTopTaper = shapeTopTaper,
            shapeSkewX = shapeSkewX,
            overallScaleFactor = scaleMultiplier
        };

        // Clone the 'dvd' visual child hierarchy from mainBouncer
        Transform srcDvd = mainBouncer.transform.Find("dvd");
        if (srcDvd != null)
        {
            GameObject dvdClone = Instantiate(srcDvd.gameObject, rootObj.transform);
            dvdClone.name = "dvd";
            dvdClone.transform.localPosition = srcDvd.localPosition;
            dvdClone.transform.localRotation = srcDvd.localRotation;
            dvdClone.transform.localScale = srcDvd.localScale;
            SetLayerRecursively(dvdClone, layer);

            // Strip any accidental colliders on the visual clone
            Collider[] cols = dvdClone.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) Destroy(cols[i]);
            }

            // Assign two-tone materials ('dvd' letters = primaryColor, 'dvd/Torus' ring = secondaryColor)
            Renderer primaryRend = dvdClone.GetComponent<Renderer>();
            if (primaryRend != null)
            {
                primaryRend.enabled = true;
                primaryRend.sharedMaterial = CreateUnlitColorMaterial(primaryColor);
                piece.primaryRenderers.Add(primaryRend);
            }

            Transform torusClone = dvdClone.transform.Find("Torus");
            if (torusClone != null)
            {
                Renderer secondaryRend = torusClone.GetComponent<Renderer>();
                if (secondaryRend != null)
                {
                    secondaryRend.enabled = true;
                    secondaryRend.sharedMaterial = CreateUnlitColorMaterial(secondaryColor);
                    piece.secondaryRenderers.Add(secondaryRend);
                }
            }

            // Bind pristine deformable vertex buffers from sourceParts
            MeshFilter[] cloneFilters = dvdClone.GetComponentsInChildren<MeshFilter>(false);
            for (int i = 0; i < cloneFilters.Length && i < sourceParts.Count; i++)
            {
                MeshFilter mf = cloneFilters[i];
                DVDLogoBouncer.DeformableMeshPart srcPart = sourceParts[i];
                if (mf == null || srcPart == null || srcPart.meshInstance == null) continue;

                Mesh meshInst = Instantiate(srcPart.meshInstance);
                meshInst.name = srcPart.meshInstance.name + "_MinigameClone";
                meshInst.MarkDynamic();

                int count = srcPart.originalLocalVerts.Length;
                Vector3[] origCopy = new Vector3[count];
                Vector3[] rootCopy = new Vector3[count];
                Vector3[] defCopy = new Vector3[count];

                System.Array.Copy(srcPart.originalLocalVerts, origCopy, count);
                System.Array.Copy(srcPart.rootSpaceVerts, rootCopy, count);
                System.Array.Copy(srcPart.originalLocalVerts, defCopy, count);

                meshInst.vertices = defCopy;
                meshInst.RecalculateBounds();
                mf.mesh = meshInst;

                piece.parts.Add(new DeformableLogoPiece.ClonedPart
                {
                    meshFilter = mf,
                    meshInstance = meshInst,
                    originalLocalVerts = origCopy,
                    rootSpaceVerts = rootCopy,
                    deformedLocalVerts = defCopy,
                    rootToPartMatrix = srcPart.rootToPartMatrix
                });
            }
        }

        piece.ComputeVerticalSpan();
        return piece;
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY)
    {
        if (orthoCamera == null)
        {
            camMinX = -8f; camMaxX = 8f; camMinY = -4.5f; camMaxY = 4.5f;
            return;
        }

        float camHeight = orthoCamera.orthographicSize;
        float camWidth = camHeight * orthoCamera.aspect;
        Vector3 camPos = orthoCamera.transform.position;
        Vector2 offset = mainBouncer != null ? mainBouncer.manualOffset : Vector2.zero;

        camMinX = camPos.x - camWidth + offset.x;
        camMaxX = camPos.x + camWidth - offset.x;
        camMinY = camPos.y - camHeight + offset.y;
        camMaxY = camPos.y + camHeight - offset.y;
    }

    private Material CreateUnlitColorMaterial(Color c)
    {
        if (sharedUnlitTemplate == null && mainBouncer != null)
        {
            Renderer r = mainBouncer.GetComponentInChildren<Renderer>(true);
            if (r != null && r.sharedMaterial != null)
            {
                sharedUnlitTemplate = r.sharedMaterial;
            }
        }

        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null && sharedUnlitTemplate != null) s = sharedUnlitTemplate.shader;
        if (s == null) s = Shader.Find("Unlit/Color");
        if (s == null) s = Shader.Find("Standard");

        Material m = new Material(s);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        return m;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            if (child != null) SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void PlaySound(AudioClip clip, float pitch = 1f)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, minigameSoundVolume);
    }
}
