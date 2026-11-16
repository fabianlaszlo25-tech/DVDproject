using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
        TowerStack = 4,
        FishFrenzy = 5,
        BlockBlast = 6,
        SubwayRunner = 7,
        HillClimb = 8,
        ColorShooter = 9
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
    [Tooltip("Remote tier index that activates the Fish Eat Fish / Feeding Frenzy game when held in TV Focus Mode.")]
    public int fishFrenzyRemoteTier = 8;
    [Tooltip("Remote tier index that activates the Block Blast DVD Grid game when held in TV Focus Mode.")]
    public int blockBlastRemoteTier = 9;
    [Tooltip("Remote tier index that activates the 3-Lane Curved Subway Runner when held in TV Focus Mode.")]
    public int subwayRunnerRemoteTier = 10;
    [Tooltip("Remote tier index that activates the Hill Climb Racing game when held in TV Focus Mode.")]
    public int hillClimbRemoteTier = 11;
    [Tooltip("Remote tier index that activates the Slingshot DVD Color Shooter game when held in TV Focus Mode.")]
    public int colorShooterRemoteTier = 12;

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

    [Header("Minigame 5: Fish Frenzy (Eat Smaller Logos) Settings")]
    [Tooltip("Starting scale multiplier for the player's DVD logo in Fish Frenzy mode.")]
    public float fishInitialPlayerSize = 0.44f;
    [Tooltip("How quickly the player's DVD logo follows the cursor in Fish Frenzy mode.")]
    public float fishPlayerFollowSpeed = 14.5f;
    [Tooltip("Maximum number of NPC DVD fish swimming on screen at once.")]
    public int fishMaxActiveNpcCount = 11;
    [Tooltip("Seconds between spawning new swimming DVD logos from the 4 screen edges.")]
    public Vector2 fishSpawnIntervalRange = new Vector2(0.45f, 0.95f);
    [Tooltip("Swimming speed range (in screen units/sec) for NPC DVD logos.")]
    public Vector2 fishSwimSpeedRange = new Vector2(1.8f, 4.2f);
    [Tooltip("Fraction of an eaten fish's size added to the player's size upon eating it.")]
    [Range(0.05f, 0.45f)]
    public float fishGrowthPerEatFactor = 0.16f;
    [Tooltip("Duration of the red screen flash when eaten by a larger fish.")]
    public float fishRedFlashDuration = 0.40f;
    [Tooltip("Additional seconds after the red flash finishes before the player regains control of the fish that ate them (default 0.5s).")]
    public float fishPostFlashLockDuration = 0.50f;

    [Header("Minigame 6: Block Blast DVD Grid Settings")]
    [Tooltip("Number of columns in the full-screen Block Blast grid.")]
    [Range(6, 10)]
    public int blockBlastGridCols = 8;
    [Tooltip("Number of rows in the full-screen Block Blast grid.")]
    [Range(6, 10)]
    public int blockBlastGridRows = 8;
    [Tooltip("Alpha transparency (0..1) of the Block Blast shape while following the cursor before clicking.")]
    [Range(0.25f, 0.85f)]
    public float blockBlastGhostAlpha = 0.54f;
    [Tooltip("Duration in seconds of the board shatter animation when no space remains for the next piece.")]
    public float blockBlastShatterDuration = 0.68f;

    [Header("Shared Screen Red Flash Settings (Damage / Penalty / Loss)")]
    [Tooltip("Default duration in seconds of the semi-transparent red screen flash when losing money or resetting.")]
    public float screenRedFlashDuration = 0.38f;
    [Tooltip("Maximum alpha opacity (0..1) of the semi-transparent red screen flash (never fully opaque).")]
    [Range(0.15f, 0.75f)]
    public float screenRedFlashMaxAlpha = 0.42f;
    public Color screenRedFlashColor = new Color(1.0f, 0.12f, 0.12f, 1.0f);

    [Header("Minigame Audio")]
    public AudioSource audioSource;
    public AudioClip jumpSound;
    public AudioClip enemyPassSound;
    public AudioClip damageHitSound;
    public AudioClip pongPaddleHitSound;
    public AudioClip suikaMergeSound;
    public AudioClip towerLandSound;
    public AudioClip fishEatSound;
    public AudioClip blockBlastPlaceSound;
    public AudioClip blockBlastClearSound;
    [Range(0f, 2f)]
    public float minigameSoundVolume = 0.9f;

    // Runtime state
    private MinigameMode currentMode = MinigameMode.None;
    public MinigameMode CurrentMode => currentMode;
    public bool IsDinoRunnerActive => currentMode == MinigameMode.DinoRunner;
    public bool IsPongActive => currentMode == MinigameMode.OneSidedPong;
    public bool IsSuikaActive => currentMode == MinigameMode.SuikaMerge;
    public bool IsTowerActive => currentMode == MinigameMode.TowerStack;
    public bool IsFishFrenzyActive => currentMode == MinigameMode.FishFrenzy;
    public bool IsBlockBlastActive => currentMode == MinigameMode.BlockBlast;
    public bool IsSubwayRunnerActive => currentMode == MinigameMode.SubwayRunner;
    public bool IsHillClimbActive => currentMode == MinigameMode.HillClimb;
    public bool IsColorShooterActive => currentMode == MinigameMode.ColorShooter;
    public bool OverridesMainLogoMovement =>
        currentMode == MinigameMode.DinoRunner ||
        currentMode == MinigameMode.SuikaMerge ||
        currentMode == MinigameMode.TowerStack ||
        currentMode == MinigameMode.FishFrenzy ||
        currentMode == MinigameMode.BlockBlast ||
        currentMode == MinigameMode.SubwayRunner ||
        currentMode == MinigameMode.HillClimb ||
        currentMode == MinigameMode.ColorShooter;

    // Dino Runner runtime state
    private float dinoVerticalVelocity = 0f;
    private bool dinoIsGrounded = true;
    private float dinoSpawnTimer = 0f;
    private float dinoElapsed = 0f;
    private float fishEatenShrinkBeginTime = -1f;
    // Player stops visibly growing once it is this many times its starting size (camera zooms out instead)
    private const float FishMaxVisibleSizeFactor = 2.6f;
    // Tower is a bit harder than the inspector value (multiplier applied on top, so scene overrides still work)
    private float EffectiveTowerForgiveness => towerBalanceForgiveness * 0.84f;
    private float dinoSpeedMult = 1f;
    private const float DinoMaxSpeedMult = 2.4f;
    private const float DinoSpeedRampPerSecond = 0.012f;
    private const float DinoBaseGapMin = 8.5f;
    private const float DinoBaseGapMax = 14.5f;
    private const float DinoShortHopGravityMult = 3.4f;
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
        public bool isCircularWheel = false;

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
            Vector3 offset = isCircularWheel ? Vector3.zero : (baseCenterOffset * overallScaleFactor);
            rootObj.transform.position = new Vector3(
                worldCenterPos.x - offset.x,
                worldCenterPos.y - offset.y,
                worldCenterPos.z);
        }

        public Vector3 GetCenterPosition()
        {
            if (rootObj == null) return Vector3.zero;
            Vector3 offset = isCircularWheel ? Vector3.zero : (baseCenterOffset * overallScaleFactor);
            return rootObj.transform.position + offset;
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

                    if (isCircularWheel)
                    {
                        float halfW = Mathf.Max(0.05f, baseHalfSize.x / Mathf.Max(0.01f, baseTransformScale.x));
                        float halfH = Mathf.Max(0.05f, baseHalfSize.y / Mathf.Max(0.01f, baseTransformScale.y));
                        float nx = Mathf.Clamp(dx / halfW, -1.15f, 1.15f);
                        float ny = Mathf.Clamp(dy / halfH, -1.15f, 1.15f);
                        float nxClamped = Mathf.Clamp(nx, -1f, 1f);
                        float nyClamped = Mathf.Clamp(ny, -1f, 1f);
                        float discU = nx * Mathf.Sqrt(Mathf.Max(0.15f, 1f - 0.45f * nyClamped * nyClamped));
                        float discV = ny * Mathf.Sqrt(Mathf.Max(0.15f, 1f - 0.45f * nxClamped * nxClamped));
                        float wheelRadius = Mathf.Sqrt(halfW * halfH) * 0.96f;
                        shapedDx = discU * wheelRadius * shapeWidthScale;
                        shapedDy = discV * wheelRadius * shapeHeightScale;
                    }

                    // 2. Apply dynamic impact squash & stretch along squashNormal
                    float dn = (shapedDx * n.x + shapedDy * n.y) * parallelScale;
                    float dtProj = (shapedDx * t.x + shapedDy * t.y) * perpScale;

                    // When used as a circular wheel, center deformed vertices directly at root origin (0,0)
                    // so rotating rootObj.transform.localRotation spins the wheel in place without orbiting off-center!
                    float centerX = isCircularWheel ? 0f : rootDeformCenter.x;
                    float centerY = isCircularWheel ? 0f : rootDeformCenter.y;

                    Vector3 deformedRoot = new Vector3(
                        centerX + n.x * dn + t.x * dtProj,
                        centerY + n.y * dn + t.y * dtProj,
                        rv.z);

                    defVerts[i] = rootToPart.MultiplyPoint3x4(deformedRoot);
                }

                part.meshInstance.vertices = defVerts;
                part.meshInstance.RecalculateBounds();
            }
        }

        public void SetColorsAndAlpha(Color colA, Color colB, float alpha = 1f)
        {
            primaryColor = new Color(colA.r, colA.g, colA.b, alpha);
            secondaryColor = new Color(colB.r, colB.g, colB.b, alpha);

            for (int i = 0; i < primaryRenderers.Count; i++)
            {
                Renderer r = primaryRenderers[i];
                if (r != null && r.sharedMaterial != null)
                {
                    if (r.sharedMaterial.HasProperty("_BaseColor")) r.sharedMaterial.SetColor("_BaseColor", primaryColor);
                    if (r.sharedMaterial.HasProperty("_Color")) r.sharedMaterial.SetColor("_Color", primaryColor);
                }
            }

            for (int i = 0; i < secondaryRenderers.Count; i++)
            {
                Renderer r = secondaryRenderers[i];
                if (r != null && r.sharedMaterial != null)
                {
                    if (r.sharedMaterial.HasProperty("_BaseColor")) r.sharedMaterial.SetColor("_BaseColor", secondaryColor);
                    if (r.sharedMaterial.HasProperty("_Color")) r.sharedMaterial.SetColor("_Color", secondaryColor);
                }
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

    // Tower Stack Persistence
    private struct SavedTowerBlockState
    {
        public int shapeIndex;
        public float towerRelX;
        public float towerRelY;
        public float halfWidth;
        public float halfHeight;
    }

    private bool hasSavedTowerState = false;
    private readonly List<SavedTowerBlockState> savedTowerBlocks = new List<SavedTowerBlockState>();
    private float savedTowerScrollY = 0f;
    private float savedTargetTowerScrollY = 0f;
    private float savedTowerInstability = 0f;

    // =========================================================================
    // MINIGAME 5: FISH FRENZY (EAT SMALLER LOGOS) STATE
    // =========================================================================

    private class FishEntity
    {
        public DeformableLogoPiece visual;
        public Vector2 pos;
        public Vector2 vel;
        public float rawSize;
        public float bobPhase;
        public bool isBeingMerged;
        public float mergeTimer;
        public float mergeDuration;
        public Vector2 mergeStartPos;
        public float mergeStartSize;
    }

    private DeformableLogoPiece fishPlayerVisual;
    private Vector2 fishPlayerPos;
    private Vector2 fishPlayerVel;
    private float fishPlayerRawSize = 0.44f;
    private float fishWorldZoomScale = 1f;
    private float fishTargetWorldZoomScale = 1f;
    private float fishSpawnTimer = 0f;
    private readonly List<FishEntity> activeFishList = new List<FishEntity>();
    private readonly List<FishEntity> mergingFishAnimations = new List<FishEntity>();

    // Being-eaten-by-bigger-fish sequence state
    private bool isFishPlayerBeingEaten = false;
    private float fishEatenSequenceTimer = 0f;
    private FishEntity fishPredatorThatAtePlayer;
    private Vector2 fishPlayerEatenStartPos;
    private float fishPlayerEatenStartScale;
    private float fishPredatorShrinkStartSize;
    private float fishZoomBeforeReset;

    // Fish Frenzy Persistence
    private struct SavedNpcFishState
    {
        public Vector2 pos;
        public Vector2 vel;
        public float rawSize;
        public Color primaryColor;
        public Color secondaryColor;
        public float bobPhase;
    }

    private bool hasSavedFishState = false;
    private float savedFishPlayerRawSize = 0.44f;
    private float savedFishWorldZoomScale = 1f;
    private float savedFishTargetWorldZoomScale = 1f;
    private Vector2 savedFishPlayerPos;
    private Color savedFishPlayerPrimaryColor = new Color(0.20f, 0.96f, 1.00f, 1f);
    private Color savedFishPlayerSecondaryColor = new Color(1.00f, 0.88f, 0.22f, 1f);
    private readonly List<SavedNpcFishState> savedNpcFishList = new List<SavedNpcFishState>();

    // =========================================================================
    // MINIGAME 6: BLOCK BLAST DVD GRID STATE
    // =========================================================================

    private struct BlockBlastShapeDef
    {
        public string name;
        public Vector2Int[] offsets;
        public Color primaryColor;
        public Color secondaryColor;
    }

    private static readonly BlockBlastShapeDef[] BlockBlastShapes = new BlockBlastShapeDef[]
    {
        new BlockBlastShapeDef {
            name = "Single1x1",
            offsets = new Vector2Int[] { new Vector2Int(0, 0) },
            primaryColor = new Color(1.00f, 0.86f, 0.18f, 1f), secondaryColor = new Color(1.00f, 0.96f, 0.65f, 1f)
        },
        new BlockBlastShapeDef {
            name = "DominoH2",
            offsets = new Vector2Int[] { new Vector2Int(0, 0), new Vector2Int(1, 0) },
            primaryColor = new Color(0.20f, 0.90f, 1.00f, 1f), secondaryColor = new Color(0.16f, 0.48f, 0.98f, 1f)
        },
        new BlockBlastShapeDef {
            name = "DominoV2",
            offsets = new Vector2Int[] { new Vector2Int(0, 0), new Vector2Int(0, 1) },
            primaryColor = new Color(0.20f, 0.90f, 1.00f, 1f), secondaryColor = new Color(0.16f, 0.48f, 0.98f, 1f)
        },
        new BlockBlastShapeDef {
            name = "BarH3",
            offsets = new Vector2Int[] { new Vector2Int(-1, 0), new Vector2Int(0, 0), new Vector2Int(1, 0) },
            primaryColor = new Color(1.00f, 0.52f, 0.16f, 1f), secondaryColor = new Color(1.00f, 0.85f, 0.34f, 1f)
        },
        new BlockBlastShapeDef {
            name = "BarV3",
            offsets = new Vector2Int[] { new Vector2Int(0, -1), new Vector2Int(0, 0), new Vector2Int(0, 1) },
            primaryColor = new Color(1.00f, 0.52f, 0.16f, 1f), secondaryColor = new Color(1.00f, 0.85f, 0.34f, 1f)
        },
        new BlockBlastShapeDef {
            name = "BarH4",
            offsets = new Vector2Int[] { new Vector2Int(-1, 0), new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) },
            primaryColor = new Color(1.00f, 0.25f, 0.42f, 1f), secondaryColor = new Color(1.00f, 0.72f, 0.82f, 1f)
        },
        new BlockBlastShapeDef {
            name = "BarV4",
            offsets = new Vector2Int[] { new Vector2Int(0, -1), new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) },
            primaryColor = new Color(1.00f, 0.25f, 0.42f, 1f), secondaryColor = new Color(1.00f, 0.72f, 0.82f, 1f)
        },
        new BlockBlastShapeDef {
            name = "Square2x2",
            offsets = new Vector2Int[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
            primaryColor = new Color(0.26f, 0.96f, 0.46f, 1f), secondaryColor = new Color(0.75f, 1.00f, 0.82f, 1f)
        },
        new BlockBlastShapeDef {
            name = "CornerSmallL",
            offsets = new Vector2Int[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1) },
            primaryColor = new Color(0.78f, 0.32f, 1.00f, 1f), secondaryColor = new Color(0.92f, 0.74f, 1.00f, 1f)
        },
        new BlockBlastShapeDef {
            name = "CornerSmallJ",
            offsets = new Vector2Int[] { new Vector2Int(0, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1) },
            primaryColor = new Color(0.78f, 0.32f, 1.00f, 1f), secondaryColor = new Color(0.92f, 0.74f, 1.00f, 1f)
        },
        new BlockBlastShapeDef {
            name = "BigL3x3",
            offsets = new Vector2Int[] { new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(-1, 0), new Vector2Int(-1, 1) },
            primaryColor = new Color(1.00f, 0.34f, 0.78f, 1f), secondaryColor = new Color(0.62f, 0.22f, 0.96f, 1f)
        },
        new BlockBlastShapeDef {
            name = "TeePiece",
            offsets = new Vector2Int[] { new Vector2Int(-1, 0), new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1) },
            primaryColor = new Color(0.32f, 0.98f, 0.78f, 1f), secondaryColor = new Color(0.14f, 0.56f, 0.96f, 1f)
        },
        new BlockBlastShapeDef {
            name = "ZPiece",
            offsets = new Vector2Int[] { new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(0, 0), new Vector2Int(1, 0) },
            primaryColor = new Color(1.00f, 0.68f, 0.16f, 1f), secondaryColor = new Color(1.00f, 0.30f, 0.25f, 1f)
        },
        new BlockBlastShapeDef {
            name = "MegaSquare3x3",
            offsets = new Vector2Int[] {
                new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
                new Vector2Int(-1,  0), new Vector2Int(0,  0), new Vector2Int(1,  0),
                new Vector2Int(-1,  1), new Vector2Int(0,  1), new Vector2Int(1,  1)
            },
            primaryColor = new Color(1.00f, 0.92f, 0.28f, 1f), secondaryColor = new Color(0.18f, 0.95f, 0.52f, 1f)
        }
    };

    private class FlyingBlockBlastTile
    {
        public DeformableLogoPiece visual;
        public Vector2 pos;
        public Vector2 vel;
        public float angularVelZ;
        public float angleZ;
        public float timer;
        public float duration;
        public float startScale;
    }

    private DeformableLogoPiece[,] blockBlastGrid;
    private GameObject blockBlastBoardBgRoot;
    private readonly List<DeformableLogoPiece> blockBlastGhostTiles = new List<DeformableLogoPiece>();
    private readonly List<FlyingBlockBlastTile> activeBlastingTiles = new List<FlyingBlockBlastTile>();
    private int currentBlockBlastShapeIdx = 0;
    private int[] blockBlastQueue = new int[] { -1, -1 };
    private int[] savedBlockBlastQueue = new int[] { -1, -1 };
    private readonly List<DeformableLogoPiece> blockBlastPreviewTiles = new List<DeformableLogoPiece>();
    private Vector2Int currentSnappedGridCell = new Vector2Int(3, 3);
    private bool currentGhostPlacementValid = false;
    private bool isBlockBlastShatteringReset = false;
    private float blockBlastShatterTimer = 0f;
    private Vector2 targetCursorOrthoPos;

    // Block Blast Persistence
    private struct SavedBlockBlastCell
    {
        public bool isOccupied;
        public Color primaryColor;
        public Color secondaryColor;
    }

    private bool hasSavedBlockBlastState = false;
    private SavedBlockBlastCell[,] savedBlockBlastGrid;
    private int savedBlockBlastShapeIdx = 0;

    // Shared in-screen semi-transparent Red Screen Flash state
    private GameObject redFlashQuadObj;
    private Material redFlashMaterial;
    private float redFlashTimer = -1f;
    private float activeRedFlashDuration = 0.38f;
    private float activeRedFlashMaxAlpha = 0.42f;

    private Material sharedUnlitTemplate;

    // Converge animation tracking
    private Coroutine activeConvergeCoroutine = null;
    private readonly List<DeformableLogoPiece> currentlyConvergingPieces = new List<DeformableLogoPiece>();

    private void StopAnyActiveConvergeAnimation()
    {
        if (activeConvergeCoroutine != null)
        {
            StopCoroutine(activeConvergeCoroutine);
            activeConvergeCoroutine = null;
        }

        for (int i = 0; i < currentlyConvergingPieces.Count; i++)
        {
            if (currentlyConvergingPieces[i] != null)
            {
                currentlyConvergingPieces[i].DestroyPiece();
            }
        }
        currentlyConvergingPieces.Clear();
    }

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
        if (fishEatSound == null && mainBouncer != null)
            fishEatSound = mainBouncer.recombinePopSound != null ? mainBouncer.recombinePopSound : mainBouncer.cornerHitSound;
        if (blockBlastPlaceSound == null && mainBouncer != null) blockBlastPlaceSound = mainBouncer.edgeHitSound;
        if (blockBlastClearSound == null && mainBouncer != null) blockBlastClearSound = mainBouncer.cornerHitSound;
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
        UpdateRedScreenFlash(dt);

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
        else if (currentMode == MinigameMode.FishFrenzy)
        {
            UpdateFishFrenzy(dt);
        }
        else if (currentMode == MinigameMode.BlockBlast)
        {
            UpdateBlockBlast(dt);
        }
        else if (currentMode == MinigameMode.SubwayRunner)
        {
            UpdateSubwayRunner(dt);
        }
        else if (currentMode == MinigameMode.HillClimb)
        {
            UpdateHillClimb(dt);
        }
        else if (currentMode == MinigameMode.ColorShooter)
        {
            UpdateColorShooter(dt);
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
        if (tier == fishFrenzyRemoteTier) return MinigameMode.FishFrenzy;
        if (tier == blockBlastRemoteTier) return MinigameMode.BlockBlast;
        if (tier == subwayRunnerRemoteTier) return MinigameMode.SubwayRunner;
        if (tier == hillClimbRemoteTier) return MinigameMode.HillClimb;
        if (tier == colorShooterRemoteTier) return MinigameMode.ColorShooter;

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
        else if (currentMode == MinigameMode.FishFrenzy)
        {
            ExitFishFrenzyMode();
        }
        else if (currentMode == MinigameMode.BlockBlast)
        {
            ExitBlockBlastMode();
        }
        else if (currentMode == MinigameMode.SubwayRunner)
        {
            ExitSubwayRunnerMode();
        }
        else if (currentMode == MinigameMode.HillClimb)
        {
            ExitHillClimbMode();
        }
        else if (currentMode == MinigameMode.ColorShooter)
        {
            ExitColorShooterMode();
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
        else if (currentMode == MinigameMode.FishFrenzy)
        {
            EnterFishFrenzyMode();
        }
        else if (currentMode == MinigameMode.BlockBlast)
        {
            EnterBlockBlastMode();
        }
        else if (currentMode == MinigameMode.SubwayRunner)
        {
            EnterSubwayRunnerMode();
        }
        else if (currentMode == MinigameMode.HillClimb)
        {
            EnterHillClimbMode();
        }
        else if (currentMode == MinigameMode.ColorShooter)
        {
            EnterColorShooterMode();
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
        targetCursorOrthoPos = new Vector2(orthoWorldPoint.x, orthoWorldPoint.y);
    }

    /// <summary>
    /// Called by TVFocusController when the player left-clicks inside the TV screen.
    /// Returns true if the active minigame consumed the click.
    /// </summary>
    public bool OnScreenClicked(Vector3 orthoWorldPoint)
    {
        targetPaddleX = orthoWorldPoint.x;
        targetCursorOrthoPos = new Vector2(orthoWorldPoint.x, orthoWorldPoint.y);

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

        if (currentMode == MinigameMode.FishFrenzy)
        {
            return true;
        }

        if (currentMode == MinigameMode.BlockBlast)
        {
            TryPlaceCurrentBlockBlastShape();
            return true;
        }

        if (currentMode == MinigameMode.SubwayRunner ||
            currentMode == MinigameMode.HillClimb ||
            currentMode == MinigameMode.ColorShooter)
        {
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

        StopAnyActiveConvergeAnimation();
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
        dinoSpawnTimer = 0.9f;
        dinoElapsed = 0f;
        dinoSpeedMult = 1f;
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

            // Variable jump height (like the Chrome dino): holding the click keeps full gravity-limited ascent,
            // releasing early while still rising cuts the jump short with much stronger gravity.
            bool jumpHeld = UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
            float gravityMult = (dinoVerticalVelocity > 0f && !jumpHeld) ? DinoShortHopGravityMult : 1f;
            dinoVerticalVelocity -= dinoGravity * gravityMult * dt;

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

        dinoElapsed += dt;
        dinoSpeedMult = Mathf.Min(DinoMaxSpeedMult, 1f + dinoElapsed * DinoSpeedRampPerSecond);
        float currentObstacleSpeed = dinoObstacleSpeed * dinoSpeedMult;

        dinoSpawnTimer -= dt;
        if (dinoSpawnTimer <= 0f)
        {
            SpawnRunnerObstacle(camMaxX + 1.1f, groundY, camMaxY);
            // Fixed gap in world units, widening as the game speeds up so it stays fair
            float gapScale = 1f + (dinoSpeedMult - 1f) * 0.65f;
            float gap = Random.Range(DinoBaseGapMin, DinoBaseGapMax) * gapScale;
            dinoSpawnTimer = gap / currentObstacleSpeed;
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
            pos.x -= currentObstacleSpeed * dt;

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
                    TriggerScreenRedFlash();
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
                // Reward grows with game speed. No particle effect when safely passing an obstacle in Dino Runner!
                mainBouncer.GetLogoTwoColors(out Color rewardA, out Color rewardB);
                mainBouncer.AwardMinigameScaledCornerReward(dinoSpeedMult, mainBouncer.GetLogoVisualCenter(), rewardA, rewardB, false);
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

        StopAnyActiveConvergeAnimation();
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

        StopAnyActiveConvergeAnimation();
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
            StopAnyActiveConvergeAnimation();
            activeConvergeCoroutine = StartCoroutine(AnimateConvergePiecesIntoMainLogo(piecesToConverge));
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
        currentlyConvergingPieces.Clear();
        if (convergingVisuals != null)
        {
            currentlyConvergingPieces.AddRange(convergingVisuals);
        }

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
        currentlyConvergingPieces.Clear();
        activeConvergeCoroutine = null;
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

        TriggerScreenRedFlash();
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

        StopAnyActiveConvergeAnimation();
        mainBouncer.DespawnAllChildLogosForMinigame();
        savedLogoPositionBeforeRunner = mainBouncer.transform.position;
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();
        mainBouncer.SetMainLogoRenderersVisible(false);

        if (hasSavedTowerState && savedTowerBlocks.Count > 0)
        {
            ClearAllTowerBlocks();

            currentTowerScrollY = savedTowerScrollY;
            targetTowerScrollY = savedTargetTowerScrollY;
            towerCumulativeInstability = savedTowerInstability;
            towerSwayPhase = 0f;
            isTowerToppling = false;
            towerToppleTimer = 0f;

            for (int i = 0; i < savedTowerBlocks.Count; i++)
            {
                SavedTowerBlockState st = savedTowerBlocks[i];
                TowerBlock b = CreateTowerBlockOfShape(st.shapeIndex);
                b.towerRelX = st.towerRelX;
                b.towerRelY = st.towerRelY;
                b.halfWidth = st.halfWidth;
                b.halfHeight = st.halfHeight;
                b.visual.TriggerPop(0.30f, 0.28f);
                stackedTowerBlocks.Add(b);
            }

            SyncTowerBlockWorldPositions(0f);
            SpawnNextSwingingTowerBlock();
            PlaySound(suikaMergeSound, 1.08f);
        }
        else
        {
            ResetAndStartNewTower();
        }
    }

    private void ExitTowerStackMode()
    {
        if (!isTowerToppling && stackedTowerBlocks.Count > 0)
        {
            savedTowerBlocks.Clear();
            for (int i = 0; i < stackedTowerBlocks.Count; i++)
            {
                TowerBlock b = stackedTowerBlocks[i];
                if (b == null || b.visual == null) continue;
                savedTowerBlocks.Add(new SavedTowerBlockState
                {
                    shapeIndex = b.shapeIndex,
                    towerRelX = b.towerRelX,
                    towerRelY = b.towerRelY,
                    halfWidth = b.halfWidth,
                    halfHeight = b.halfHeight
                });
            }
            savedTowerScrollY = currentTowerScrollY;
            savedTargetTowerScrollY = targetTowerScrollY;
            savedTowerInstability = towerCumulativeInstability;
            hasSavedTowerState = savedTowerBlocks.Count > 0;
        }

        List<DeformableLogoPiece> piecesToConverge = new List<DeformableLogoPiece>();
        if (activeSwingingBlock != null && activeSwingingBlock.visual != null)
        {
            piecesToConverge.Add(activeSwingingBlock.visual);
            activeSwingingBlock = null;
        }
        isActiveBlockFalling = false;

        for (int i = 0; i < stackedTowerBlocks.Count; i++)
        {
            if (stackedTowerBlocks[i] != null && stackedTowerBlocks[i].visual != null)
            {
                piecesToConverge.Add(stackedTowerBlocks[i].visual);
            }
        }
        stackedTowerBlocks.Clear();

        if (piecesToConverge.Count > 0 && mainBouncer != null && gameObject.activeInHierarchy)
        {
            StopAnyActiveConvergeAnimation();
            activeConvergeCoroutine = StartCoroutine(AnimateConvergePiecesIntoMainLogo(piecesToConverge));
        }
        else
        {
            if (mainBouncer != null)
            {
                mainBouncer.SetMainLogoRenderersVisible(true);
                mainBouncer.TriggerRecombineBounce();
                mainBouncer.TriggerSquashStretch(Vector2.up, mainBouncer.maxSquashAmount * 0.85f);
                mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
            }
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
                float swingSpeed = towerTopSwingSpeed * (1f + Mathf.Min(stackedTowerBlocks.Count, 30) * 0.02f);
                activeSwingingBlock.towerRelX += activeSwingDirection * swingSpeed * dt;
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
            float maxSupportSpan = (supportBlock.halfWidth + activeSwingingBlock.halfWidth * 0.45f) * EffectiveTowerForgiveness;
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
        if (Mathf.Abs(towerCumulativeInstability) > 1.15f * EffectiveTowerForgiveness)
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
            // Taller tower = more money per landed block
            float heightRewardMult = 1f + stackedTowerBlocks.Count * 0.25f;
            mainBouncer.AwardMinigameScaledCornerReward(heightRewardMult, new Vector3(landWorldX, landWorldY, zPlane), cA, cB);

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
        hasSavedTowerState = false;
        savedTowerBlocks.Clear();
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
        TriggerScreenRedFlash();
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
    // MINIGAME 5: FISH FRENZY / EAT SMALLER LOGOS
    // =========================================================================

    private void EnterFishFrenzyMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        StopAnyActiveConvergeAnimation();
        mainBouncer.DespawnAllChildLogosForMinigame();
        savedLogoPositionBeforeRunner = mainBouncer.transform.position;
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();
        mainBouncer.SetMainLogoRenderersVisible(false);

        ClearAllFishFrenzyEntities();

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        Vector2 screenCenter = new Vector2((camMinX + camMaxX) * 0.5f, (camMinY + camMaxY) * 0.5f);
        float zPlane = mainBouncer.transform.position.z;

        if (hasSavedFishState)
        {
            // Restore saved Fish Frenzy state!
            fishPlayerRawSize = savedFishPlayerRawSize;
            fishWorldZoomScale = savedFishWorldZoomScale;
            fishTargetWorldZoomScale = savedFishTargetWorldZoomScale;
            fishPlayerPos = savedFishPlayerPos;
            fishPlayerVel = Vector2.zero;
            isFishPlayerBeingEaten = false;
            fishEatenSequenceTimer = 0f;
            fishPredatorThatAtePlayer = null;
            fishSpawnTimer = 0.55f;

            float renderedPlayerScale = fishPlayerRawSize * fishWorldZoomScale;
            fishPlayerVisual = CreateDeformableLogoClone("FishPlayer_Logo", renderedPlayerScale, savedFishPlayerPrimaryColor, savedFishPlayerSecondaryColor);
            if (fishPlayerVisual != null)
            {
                fishPlayerVisual.SetCenterPosition(new Vector3(fishPlayerPos.x, fishPlayerPos.y, zPlane));
                fishPlayerVisual.TriggerPop(0.35f, 0.32f);
                fishPlayerVisual.TriggerSquash(Vector2.up, 0.22f, 0.42f);
                fishPlayerVisual.UpdateVisuals(0f);
            }

            for (int i = 0; i < savedNpcFishList.Count; i++)
            {
                SavedNpcFishState sn = savedNpcFishList[i];
                float renderedNpcScale = sn.rawSize * fishWorldZoomScale;
                DeformableLogoPiece vis = CreateDeformableLogoClone("FishNPC_Logo", renderedNpcScale, sn.primaryColor, sn.secondaryColor);
                if (vis != null)
                {
                    vis.SetCenterPosition(new Vector3(sn.pos.x, sn.pos.y, zPlane));
                    vis.TriggerPop(0.28f, 0.28f);
                    vis.UpdateVisuals(0f);
                    activeFishList.Add(new FishEntity
                    {
                        visual = vis,
                        pos = sn.pos,
                        vel = sn.vel,
                        rawSize = sn.rawSize,
                        bobPhase = sn.bobPhase,
                        isBeingMerged = false
                    });
                }
            }

            PlaySound(suikaMergeSound, 1.10f);
        }
        else
        {
            fishPlayerRawSize = fishInitialPlayerSize;
            fishWorldZoomScale = 1f;
            fishTargetWorldZoomScale = 1f;
            fishPlayerPos = screenCenter;
            fishPlayerVel = Vector2.zero;
            isFishPlayerBeingEaten = false;
            fishEatenSequenceTimer = 0f;
            fishPredatorThatAtePlayer = null;
            fishSpawnTimer = 0.15f;

            Color pCol = new Color(0.20f, 0.96f, 1.00f, 1f);
            Color sCol = new Color(1.00f, 0.88f, 0.22f, 1f);
            fishPlayerVisual = CreateDeformableLogoClone("FishPlayer_Logo", fishPlayerRawSize, pCol, sCol);
            if (fishPlayerVisual != null)
            {
                fishPlayerVisual.SetCenterPosition(new Vector3(fishPlayerPos.x, fishPlayerPos.y, zPlane));
                fishPlayerVisual.TriggerPop(0.32f, 0.30f);
                fishPlayerVisual.UpdateVisuals(0f);
            }

            // Pre-populate a few fish swimming around so the screen feels alive right away
            for (int i = 0; i < 4; i++)
            {
                SpawnSwimmingFishFromEdge(i < 3); // guarantee at least 3 smaller edible fish initially
            }
        }
    }

    private void ExitFishFrenzyMode()
    {
        // Save current Fish Frenzy state if player wasn't currently being eaten
        if (!isFishPlayerBeingEaten && fishPlayerVisual != null)
        {
            hasSavedFishState = true;
            savedFishPlayerRawSize = fishPlayerRawSize;
            savedFishWorldZoomScale = fishWorldZoomScale;
            savedFishTargetWorldZoomScale = fishTargetWorldZoomScale;
            savedFishPlayerPos = fishPlayerPos;
            savedFishPlayerPrimaryColor = fishPlayerVisual.primaryColor;
            savedFishPlayerSecondaryColor = fishPlayerVisual.secondaryColor;

            savedNpcFishList.Clear();
            for (int i = 0; i < activeFishList.Count; i++)
            {
                FishEntity f = activeFishList[i];
                if (f == null || f.visual == null || f.isBeingMerged) continue;
                savedNpcFishList.Add(new SavedNpcFishState
                {
                    pos = f.pos,
                    vel = f.vel,
                    rawSize = f.rawSize,
                    primaryColor = f.visual.primaryColor,
                    secondaryColor = f.visual.secondaryColor,
                    bobPhase = f.bobPhase
                });
            }
        }

        List<DeformableLogoPiece> piecesToConverge = new List<DeformableLogoPiece>();
        if (fishPlayerVisual != null)
        {
            piecesToConverge.Add(fishPlayerVisual);
            fishPlayerVisual = null;
        }

        for (int i = 0; i < activeFishList.Count; i++)
        {
            if (activeFishList[i] != null && activeFishList[i].visual != null)
            {
                piecesToConverge.Add(activeFishList[i].visual);
            }
        }
        activeFishList.Clear();

        for (int i = 0; i < mergingFishAnimations.Count; i++)
        {
            if (mergingFishAnimations[i] != null && mergingFishAnimations[i].visual != null)
            {
                piecesToConverge.Add(mergingFishAnimations[i].visual);
            }
        }
        mergingFishAnimations.Clear();
        fishPredatorThatAtePlayer = null;
        isFishPlayerBeingEaten = false;

        if (piecesToConverge.Count > 0 && mainBouncer != null && gameObject.activeInHierarchy)
        {
            StopAnyActiveConvergeAnimation();
            activeConvergeCoroutine = StartCoroutine(AnimateConvergePiecesIntoMainLogo(piecesToConverge));
        }
        else
        {
            if (mainBouncer != null)
            {
                mainBouncer.SetMainLogoRenderersVisible(true);
                mainBouncer.TriggerRecombineBounce();
                mainBouncer.TriggerSquashStretch(Vector2.up, mainBouncer.maxSquashAmount * 0.85f);
                mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
            }
        }
    }

    private void ClearAllFishFrenzyEntities()
    {
        if (fishPlayerVisual != null)
        {
            fishPlayerVisual.DestroyPiece();
            fishPlayerVisual = null;
        }

        for (int i = 0; i < activeFishList.Count; i++)
        {
            if (activeFishList[i] != null && activeFishList[i].visual != null)
            {
                activeFishList[i].visual.DestroyPiece();
            }
        }
        activeFishList.Clear();

        for (int i = 0; i < mergingFishAnimations.Count; i++)
        {
            if (mergingFishAnimations[i] != null && mergingFishAnimations[i].visual != null)
            {
                mergingFishAnimations[i].visual.DestroyPiece();
            }
        }
        mergingFishAnimations.Clear();
        fishPredatorThatAtePlayer = null;
        isFishPlayerBeingEaten = false;
    }

    private void SpawnSwimmingFishFromEdge(bool forceSmallerThanPlayer = false)
    {
        if (mainBouncer == null || orthoCamera == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float zPlane = mainBouncer.transform.position.z;

        // Choose size relative to the player's current raw size so the game scales infinitely!
        bool isSmaller = forceSmallerThanPlayer || (Random.value < 0.58f);
        float sizeRatio = isSmaller ? Random.Range(0.42f, 0.86f) : Random.Range(1.18f, 1.85f);
        float fishRawSize = Mathf.Max(0.22f, fishPlayerRawSize * sizeRatio);

        // Pick a vibrant two-tone color pair from the Suika palette + extra ocean hues
        int colorIdx = Random.Range(0, suikaTierPrimaryColors.Length);
        Color colA = suikaTierPrimaryColors[colorIdx];
        Color colB = suikaTierSecondaryColors[colorIdx];

        float renderedScale = fishRawSize * fishWorldZoomScale;
        DeformableLogoPiece vis = CreateDeformableLogoClone("FishNPC_Logo", renderedScale, colA, colB);
        if (vis == null) return;

        float marginX = vis.EffectiveHalfWidth + 0.35f;
        float marginY = vis.EffectiveHalfHeight + 0.35f;

        // Pick one of the 4 sides: 0 = Left, 1 = Right, 2 = Bottom, 3 = Top
        int edge = Random.Range(0, 4);
        Vector2 spawnPos = Vector2.zero;
        Vector2 targetAcross = Vector2.zero;

        if (edge == 0)
        {
            spawnPos = new Vector2(camMinX - marginX, Random.Range(camMinY + 0.5f, camMaxY - 0.5f));
            targetAcross = new Vector2(camMaxX + marginX, Random.Range(camMinY + 0.5f, camMaxY - 0.5f));
        }
        else if (edge == 1)
        {
            spawnPos = new Vector2(camMaxX + marginX, Random.Range(camMinY + 0.5f, camMaxY - 0.5f));
            targetAcross = new Vector2(camMinX - marginX, Random.Range(camMinY + 0.5f, camMaxY - 0.5f));
        }
        else if (edge == 2)
        {
            spawnPos = new Vector2(Random.Range(camMinX + 0.5f, camMaxX - 0.5f), camMinY - marginY);
            targetAcross = new Vector2(Random.Range(camMinX + 0.5f, camMaxX - 0.5f), camMaxY + marginY);
        }
        else
        {
            spawnPos = new Vector2(Random.Range(camMinX + 0.5f, camMaxX - 0.5f), camMaxY + marginY);
            targetAcross = new Vector2(Random.Range(camMinX + 0.5f, camMaxX - 0.5f), camMinY - marginY);
        }

        Vector2 dir = (targetAcross - spawnPos).normalized;
        float swimSpeed = Random.Range(fishSwimSpeedRange.x, fishSwimSpeedRange.y);

        vis.SetCenterPosition(new Vector3(spawnPos.x, spawnPos.y, zPlane));
        vis.UpdateVisuals(0f);

        activeFishList.Add(new FishEntity
        {
            visual = vis,
            pos = spawnPos,
            vel = dir * swimSpeed,
            rawSize = fishRawSize,
            bobPhase = Random.Range(0f, 10f),
            isBeingMerged = false
        });
    }

    private void UpdateFishFrenzy(float dt)
    {
        if (mainBouncer == null || orthoCamera == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float zPlane = mainBouncer.transform.position.z;

        // 1. Update any eaten smaller fish converging into the player (merge animation)
        for (int i = mergingFishAnimations.Count - 1; i >= 0; i--)
        {
            FishEntity mf = mergingFishAnimations[i];
            if (mf == null || mf.visual == null)
            {
                mergingFishAnimations.RemoveAt(i);
                continue;
            }

            mf.mergeTimer += dt;
            float t = Mathf.Clamp01(mf.mergeTimer / Mathf.Max(0.05f, mf.mergeDuration));
            // Merge-game feel: slowly gets sucked in, then accelerates into the player while shrinking
            float pull = t * t * t;
            float shrink = 1f - (1f - t) * (1f - t);

            Vector2 targetPos = fishPlayerPos;
            mf.pos = Vector2.Lerp(mf.mergeStartPos, targetPos, pull);
            mf.visual.overallScaleFactor = Mathf.Lerp(mf.mergeStartSize, mf.mergeStartSize * 0.12f, shrink);
            mf.visual.SetCenterPosition(new Vector3(mf.pos.x, mf.pos.y, zPlane));
            mf.visual.UpdateVisuals(dt);

            if (t >= 1f)
            {
                // Satisfying "merge" payoff: the player bulges and squashes as it absorbs the fish
                if (fishPlayerVisual != null && !isFishPlayerBeingEaten)
                {
                    fishPlayerVisual.TriggerPop(0.30f, 0.26f);
                    Vector2 impact = (mf.pos - fishPlayerPos);
                    fishPlayerVisual.TriggerSquash(impact.sqrMagnitude > 0.0001f ? impact.normalized : Vector2.up, 0.20f, 0.32f);
                }
                mf.visual.DestroyPiece();
                mergingFishAnimations.RemoveAt(i);
            }
        }

        // 2. Handle sequence when a BIGGER fish eats the player!
        //    - Screen flashes red (semi-transparent)
        //    - Player loses control during flash + 0.5s after flash
        //    - Player visual merges into the eating fish
        //    - Then player gains control of the fish that ate it, which shrinks back to smallest starting size & zooms back in!
        if (isFishPlayerBeingEaten)
        {
            fishEatenSequenceTimer += dt;
            float lockDuration = fishRedFlashDuration + fishPostFlashLockDuration; // e.g., 0.40s flash + 0.50s wait = 0.90s
            float shrinkPhaseDuration = 0.36f;

            // Animate player visual merging into the predator during the first 0.24s
            if (fishPlayerVisual != null && fishPredatorThatAtePlayer != null && fishPredatorThatAtePlayer.visual != null)
            {
                float mergeT = Mathf.Clamp01(fishEatenSequenceTimer / 0.24f);
                float smoothM = mergeT * mergeT * (3f - 2f * mergeT);
                Vector2 curP = Vector2.Lerp(fishPlayerEatenStartPos, fishPredatorThatAtePlayer.pos, smoothM);
                fishPlayerVisual.overallScaleFactor = Mathf.Lerp(fishPlayerEatenStartScale, 0.06f, smoothM);
                fishPlayerVisual.SetCenterPosition(new Vector3(curP.x, curP.y, zPlane));
                fishPlayerVisual.UpdateVisuals(dt);

                if (mergeT >= 1f)
                {
                    fishPlayerVisual.DestroyPiece();
                    fishPlayerVisual = null;
                }
            }

            // Keep the predator fish gently drifting while locked, then shrinking back to starting size as camera zooms back in
            if (fishPredatorThatAtePlayer != null && fishPredatorThatAtePlayer.visual != null)
            {
                if (fishEatenSequenceTimer < lockDuration || (fishEatenShrinkBeginTime < 0f && activeFishList.Count > 0 && fishEatenSequenceTimer < lockDuration + 2.0f))
                {
                    fishPredatorThatAtePlayer.pos += fishPredatorThatAtePlayer.vel * 0.25f * dt;
                    float hw = fishPredatorThatAtePlayer.visual.EffectiveHalfWidth;
                    float hh = fishPredatorThatAtePlayer.visual.EffectiveHalfHeight;
                    fishPredatorThatAtePlayer.pos.x = Mathf.Clamp(fishPredatorThatAtePlayer.pos.x, camMinX + hw, camMaxX - hw);
                    fishPredatorThatAtePlayer.pos.y = Mathf.Clamp(fishPredatorThatAtePlayer.pos.y, camMinY + hh, camMaxY - hh);
                    fishPredatorThatAtePlayer.visual.SetCenterPosition(new Vector3(fishPredatorThatAtePlayer.pos.x, fishPredatorThatAtePlayer.pos.y, zPlane));
                    fishPredatorThatAtePlayer.visual.UpdateVisuals(dt);
                }
                else
                {
                    // Everything else has been pushed off-screen: NOW shrink back to starting size and zoom back in
                    if (fishEatenShrinkBeginTime < 0f) fishEatenShrinkBeginTime = fishEatenSequenceTimer;
                    float shrinkT = Mathf.Clamp01((fishEatenSequenceTimer - fishEatenShrinkBeginTime) / shrinkPhaseDuration);
                    float smoothS = shrinkT * shrinkT * (3f - 2f * shrinkT);

                    fishWorldZoomScale = Mathf.Lerp(fishZoomBeforeReset, 1.0f, smoothS);
                    fishTargetWorldZoomScale = 1.0f;

                    float currentPredatorScale = Mathf.Lerp(fishPredatorShrinkStartSize, fishInitialPlayerSize, smoothS);
                    fishPredatorThatAtePlayer.visual.overallScaleFactor = currentPredatorScale;

                    // Allow the newly controlled fish to start moving toward the cursor as it finishes shrinking
                    Vector2 clampedCursor = new Vector2(
                        Mathf.Clamp(targetCursorOrthoPos.x, camMinX + 0.5f, camMaxX - 0.5f),
                        Mathf.Clamp(targetCursorOrthoPos.y, camMinY + 0.5f, camMaxY - 0.5f));
                    fishPredatorThatAtePlayer.pos = Vector2.Lerp(fishPredatorThatAtePlayer.pos, clampedCursor, dt * fishPlayerFollowSpeed * 0.65f);
                    fishPredatorThatAtePlayer.visual.SetCenterPosition(new Vector3(fishPredatorThatAtePlayer.pos.x, fishPredatorThatAtePlayer.pos.y, zPlane));
                    fishPredatorThatAtePlayer.visual.UpdateVisuals(dt);

                    if (shrinkT >= 1f)
                    {
                        // Hand full control of the predator fish to the player!
                        fishPlayerVisual = fishPredatorThatAtePlayer.visual;
                        fishPlayerVisual.rootObj.name = "FishPlayer_Logo";
                        fishPlayerPos = fishPredatorThatAtePlayer.pos;
                        fishPlayerVel = Vector2.zero;
                        fishPlayerRawSize = fishInitialPlayerSize;
                        fishWorldZoomScale = 1.0f;
                        fishTargetWorldZoomScale = 1.0f;
                        fishPlayerVisual.overallScaleFactor = fishInitialPlayerSize;
                        fishPlayerVisual.TriggerPop(0.28f, 0.28f);

                        fishPredatorThatAtePlayer = null;
                        isFishPlayerBeingEaten = false;

                        // Rescale remaining NPC fish so there are still smaller ones around the reset player
                        for (int i = 0; i < activeFishList.Count; i++)
                        {
                            if (activeFishList[i] != null)
                            {
                                activeFishList[i].rawSize = fishInitialPlayerSize * Random.Range(0.48f, 1.35f);
                            }
                        }
                    }
                }
            }
            else
            {
                isFishPlayerBeingEaten = false;
            }
        }
        else if (fishPlayerVisual != null)
        {
            // 3. Normal Fish Frenzy gameplay: the player visibly grows until FishMaxVisiblePlayerScale, after that the
            //    "camera" zooms out so the player stops visibly growing and only the surrounding fish look smaller.
            float maxVisible = fishInitialPlayerSize * FishMaxVisibleSizeFactor;
            fishTargetWorldZoomScale = Mathf.Clamp(maxVisible / Mathf.Max(0.1f, fishPlayerRawSize), 0.05f, 1.0f);
            fishWorldZoomScale = Mathf.Lerp(fishWorldZoomScale, fishTargetWorldZoomScale, dt * 4.5f);

            float playerRenderedScale = fishPlayerRawSize * fishWorldZoomScale;
            fishPlayerVisual.overallScaleFactor = playerRenderedScale;

            float hw = fishPlayerVisual.EffectiveHalfWidth;
            float hh = fishPlayerVisual.EffectiveHalfHeight;

            Vector2 targetPos = new Vector2(
                Mathf.Clamp(targetCursorOrthoPos.x, camMinX + hw, camMaxX - hw),
                Mathf.Clamp(targetCursorOrthoPos.y, camMinY + hh, camMaxY - hh));

            Vector2 prevPos = fishPlayerPos;
            fishPlayerPos = Vector2.Lerp(fishPlayerPos, targetPos, dt * fishPlayerFollowSpeed);
            if (dt > 0.0001f)
            {
                fishPlayerVel = (fishPlayerPos - prevPos) / dt;
            }

            fishPlayerVisual.SetCenterPosition(new Vector3(fishPlayerPos.x, fishPlayerPos.y, zPlane));
            fishPlayerVisual.UpdateVisuals(dt);
        }

        // 4. Spawn new swimming NPC fish from the 4 edges
        fishSpawnTimer -= dt;
        if (fishSpawnTimer <= 0f && activeFishList.Count < fishMaxActiveNpcCount && !isFishPlayerBeingEaten)
        {
            SpawnSwimmingFishFromEdge(false);
            fishSpawnTimer = Random.Range(fishSpawnIntervalRange.x, fishSpawnIntervalRange.y);
        }

        // 5. Update all swimming NPC fish & check collisions with the player
        for (int i = activeFishList.Count - 1; i >= 0; i--)
        {
            FishEntity f = activeFishList[i];
            if (f == null || f.visual == null)
            {
                activeFishList.RemoveAt(i);
                continue;
            }

            f.bobPhase += dt * 3.8f;
            Vector2 perp = new Vector2(-f.vel.y, f.vel.x).normalized;
            float swimWave = Mathf.Sin(f.bobPhase) * 0.35f;

            f.pos += (f.vel + perp * swimWave) * dt;
            f.visual.overallScaleFactor = f.rawSize * fishWorldZoomScale;
            f.visual.SetCenterPosition(new Vector3(f.pos.x, f.pos.y, zPlane));
            f.visual.UpdateVisuals(dt);

            // Check collision with player if player is active and not currently being eaten
            if (!isFishPlayerBeingEaten && fishPlayerVisual != null)
            {
                float sumRx = (fishPlayerVisual.EffectiveHalfWidth + f.visual.EffectiveHalfWidth) * 0.82f;
                float sumRy = (fishPlayerVisual.EffectiveHalfHeight + f.visual.EffectiveHalfHeight) * 0.82f;
                float dx = Mathf.Abs(f.pos.x - fishPlayerPos.x);
                float dy = Mathf.Abs(f.pos.y - fishPlayerPos.y);

                if (dx <= sumRx && dy <= sumRy)
                {
                    if (fishPlayerRawSize >= f.rawSize * 0.96f)
                    {
                        // PLAYER IS BIGGER -> Player eats the smaller logo with the merge animation!
                        EatSmallerFish(i);
                        continue;
                    }
                    else
                    {
                        // OTHER LOGO IS BIGGER -> Bigger logo eats the player!
                        TriggerFishPlayerEaten(i);
                        break;
                    }
                }
            }

            // Despawn fish once it swims completely off the far side of the screen
            float outMargin = 2.5f;
            if (isFishPlayerBeingEaten)
            {
                outMargin = Mathf.Max(2.5f, Mathf.Max(f.visual.EffectiveHalfWidth, f.visual.EffectiveHalfHeight) + 0.6f);
            }
            if (f.pos.x < camMinX - outMargin || f.pos.x > camMaxX + outMargin ||
                f.pos.y < camMinY - outMargin || f.pos.y > camMaxY + outMargin)
            {
                f.visual.DestroyPiece();
                activeFishList.RemoveAt(i);
            }
        }
    }

    private void EatSmallerFish(int fishIndex)
    {
        if (fishIndex < 0 || fishIndex >= activeFishList.Count) return;
        FishEntity eaten = activeFishList[fishIndex];
        activeFishList.RemoveAt(fishIndex);

        // Start merge animation of the eaten fish into the player logo
        eaten.isBeingMerged = true;
        eaten.mergeTimer = 0f;
        eaten.mergeDuration = 0.28f;
        eaten.mergeStartPos = eaten.pos;
        eaten.mergeStartSize = eaten.visual.overallScaleFactor;
        eaten.visual.TriggerSquash((fishPlayerPos - eaten.pos).normalized, 0.24f, 0.24f);
        mergingFishAnimations.Add(eaten);

        // Grow player size. Grows faster at the start; once past the visible-size cap, growth is normal and the
        // camera zoom-out (fishTargetWorldZoomScale) keeps the player's on-screen size constant.
        float visibleCap = fishInitialPlayerSize * FishMaxVisibleSizeFactor;
        float earlyBoost = fishPlayerRawSize < visibleCap ? 2.1f : 1f;
        fishPlayerRawSize += eaten.rawSize * fishGrowthPerEatFactor * earlyBoost;

        if (fishPlayerVisual != null)
        {
            // The big pop happens when the eaten fish actually arrives (see merge animation update)
            fishPlayerVisual.TriggerSquash(Vector2.up, 0.10f, 0.25f);
        }

        // Award money based on the size of the eaten fish!
        float sizeRewardMultiplier = Mathf.Clamp(eaten.rawSize / Mathf.Max(0.1f, fishInitialPlayerSize), 0.5f, 25f);
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : 5f;
        mainBouncer.AwardMinigameScaledCornerReward(
            sizeRewardMultiplier,
            new Vector3(eaten.pos.x, eaten.pos.y, zPlane),
            eaten.visual.primaryColor,
            eaten.visual.secondaryColor,
            true);

        PlaySound(fishEatSound, Random.Range(0.96f, 1.22f));
    }

    private void TriggerFishPlayerEaten(int predatorIndex)
    {
        if (predatorIndex < 0 || predatorIndex >= activeFishList.Count) return;

        // Player was eaten; clear saved state so looking away doesn't restore invalid state
        hasSavedFishState = false;
        savedNpcFishList.Clear();

        FishEntity predator = activeFishList[predatorIndex];
        activeFishList.RemoveAt(predatorIndex);

        isFishPlayerBeingEaten = true;
        fishEatenSequenceTimer = 0f;
        fishPredatorThatAtePlayer = predator;
        fishPlayerEatenStartPos = fishPlayerPos;
        fishPlayerEatenStartScale = fishPlayerVisual != null ? fishPlayerVisual.overallScaleFactor : fishInitialPlayerSize;
        fishPredatorShrinkStartSize = predator.visual.overallScaleFactor * 1.12f;
        fishZoomBeforeReset = fishWorldZoomScale;
        fishEatenShrinkBeginTime = -1f;

        // Push every other fish off the screen BEFORE the reset/scale-back so the rescale is never visible
        for (int i = 0; i < activeFishList.Count; i++)
        {
            FishEntity other = activeFishList[i];
            if (other == null) continue;
            Vector2 away = other.pos - predator.pos;
            if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitCircle;
            away.Normalize();
            other.vel = away * Random.Range(17f, 24f);
        }

        // Predator pops & squashes as it eats the player with the merge animation!
        predator.visual.overallScaleFactor = fishPredatorShrinkStartSize;
        predator.visual.TriggerPop(0.42f, 0.36f);
        predator.visual.TriggerSquash(Vector2.up, 0.26f, 0.48f);

        // Semi-transparent red screen flash + lose 10 * CornerHitValue!
        TriggerScreenRedFlash(fishRedFlashDuration, screenRedFlashMaxAlpha);
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : 5f;
        mainBouncer.ApplyMinigameCornerPenalty(
            10,
            new Vector3(predator.pos.x, predator.pos.y, zPlane),
            predator.visual.primaryColor,
            predator.visual.secondaryColor);

        PlaySound(damageHitSound, 0.72f);
    }

    // =========================================================================
    // MINIGAME 6: BLOCK BLAST DVD GRID
    // =========================================================================

    private void EnterBlockBlastMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        StopAnyActiveConvergeAnimation();
        mainBouncer.DespawnAllChildLogosForMinigame();
        savedLogoPositionBeforeRunner = mainBouncer.transform.position;
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();
        mainBouncer.SetMainLogoRenderersVisible(false);

        ClearAllBlockBlastObjects();

        int cols = Mathf.Clamp(blockBlastGridCols, 6, 10);
        int rows = Mathf.Clamp(blockBlastGridRows, 6, 10);
        blockBlastGrid = new DeformableLogoPiece[cols, rows];
        isBlockBlastShatteringReset = false;
        blockBlastShatterTimer = 0f;

        BuildBlockBlastGridBackground(cols, rows);

        if (hasSavedBlockBlastState && savedBlockBlastGrid != null)
        {
            GetBlockBlastGridMetrics(out _, out _, out float cellSize, out Vector2 origin);
            float zPlane = mainBouncer.transform.position.z;
            int savedCols = savedBlockBlastGrid.GetLength(0);
            int savedRows = savedBlockBlastGrid.GetLength(1);

            for (int x = 0; x < Mathf.Min(cols, savedCols); x++)
            {
                for (int y = 0; y < Mathf.Min(rows, savedRows); y++)
                {
                    if (savedBlockBlastGrid[x, y].isOccupied)
                    {
                        DeformableLogoPiece placedTile = CreateSquareBlockBlastTile(
                            $"PlacedTile_{x}_{y}",
                            cellSize,
                            savedBlockBlastGrid[x, y].primaryColor,
                            savedBlockBlastGrid[x, y].secondaryColor,
                            false);

                        if (placedTile != null)
                        {
                            Vector2 cellWorld = GetBlockBlastCellCenter(x, y, cellSize, origin);
                            placedTile.SetCenterPosition(new Vector3(cellWorld.x, cellWorld.y, zPlane));
                            placedTile.TriggerPop(0.32f, 0.28f);
                            placedTile.UpdateVisuals(0f);
                            blockBlastGrid[x, y] = placedTile;
                        }
                    }
                }
            }

            blockBlastQueue = new int[] { savedBlockBlastQueue[0], savedBlockBlastQueue[1] };
            SpawnGhostTilesForShape(savedBlockBlastShapeIdx);
            RebuildBlockBlastPreviews();
            PlaySound(suikaMergeSound, 1.08f);
        }
        else
        {
            blockBlastQueue = new int[] { -1, -1 };
            RollNextBlockBlastShape();
        }
    }

    private void ExitBlockBlastMode()
    {
        if (!isBlockBlastShatteringReset && blockBlastGrid != null)
        {
            int cols = blockBlastGrid.GetLength(0);
            int rows = blockBlastGrid.GetLength(1);
            savedBlockBlastGrid = new SavedBlockBlastCell[cols, rows];

            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    if (blockBlastGrid[x, y] != null)
                    {
                        savedBlockBlastGrid[x, y] = new SavedBlockBlastCell
                        {
                            isOccupied = true,
                            primaryColor = blockBlastGrid[x, y].primaryColor,
                            secondaryColor = blockBlastGrid[x, y].secondaryColor
                        };
                    }
                    else
                    {
                        savedBlockBlastGrid[x, y] = new SavedBlockBlastCell { isOccupied = false };
                    }
                }
            }

            savedBlockBlastShapeIdx = currentBlockBlastShapeIdx;
            savedBlockBlastQueue = new int[] { blockBlastQueue[0], blockBlastQueue[1] };
            hasSavedBlockBlastState = true;
        }

        List<DeformableLogoPiece> piecesToConverge = new List<DeformableLogoPiece>();

        if (blockBlastGrid != null)
        {
            int cols = blockBlastGrid.GetLength(0);
            int rows = blockBlastGrid.GetLength(1);
            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    if (blockBlastGrid[x, y] != null)
                    {
                        piecesToConverge.Add(blockBlastGrid[x, y]);
                        blockBlastGrid[x, y] = null;
                    }
                }
            }
        }

        for (int i = 0; i < blockBlastGhostTiles.Count; i++)
        {
            if (blockBlastGhostTiles[i] != null)
            {
                piecesToConverge.Add(blockBlastGhostTiles[i]);
            }
        }
        blockBlastGhostTiles.Clear();

        for (int i = 0; i < blockBlastPreviewTiles.Count; i++)
        {
            if (blockBlastPreviewTiles[i] != null)
            {
                piecesToConverge.Add(blockBlastPreviewTiles[i]);
            }
        }
        blockBlastPreviewTiles.Clear();

        for (int i = 0; i < activeBlastingTiles.Count; i++)
        {
            if (activeBlastingTiles[i] != null && activeBlastingTiles[i].visual != null)
            {
                piecesToConverge.Add(activeBlastingTiles[i].visual);
            }
        }
        activeBlastingTiles.Clear();

        if (blockBlastBoardBgRoot != null)
        {
            Destroy(blockBlastBoardBgRoot);
            blockBlastBoardBgRoot = null;
        }

        if (piecesToConverge.Count > 0 && mainBouncer != null && gameObject.activeInHierarchy)
        {
            StopAnyActiveConvergeAnimation();
            activeConvergeCoroutine = StartCoroutine(AnimateConvergePiecesIntoMainLogo(piecesToConverge));
        }
        else
        {
            if (mainBouncer != null)
            {
                mainBouncer.SetMainLogoRenderersVisible(true);
                mainBouncer.TriggerRecombineBounce();
                mainBouncer.TriggerSquashStretch(Vector2.up, mainBouncer.maxSquashAmount * 0.85f);
                mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
            }
        }
    }

    private void ClearAllBlockBlastObjects()
    {
        if (blockBlastGrid != null)
        {
            int cols = blockBlastGrid.GetLength(0);
            int rows = blockBlastGrid.GetLength(1);
            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    if (blockBlastGrid[x, y] != null)
                    {
                        blockBlastGrid[x, y].DestroyPiece();
                        blockBlastGrid[x, y] = null;
                    }
                }
            }
        }

        for (int i = 0; i < blockBlastGhostTiles.Count; i++)
        {
            if (blockBlastGhostTiles[i] != null) blockBlastGhostTiles[i].DestroyPiece();
        }
        blockBlastGhostTiles.Clear();

        ClearBlockBlastPreviews();

        for (int i = 0; i < activeBlastingTiles.Count; i++)
        {
            if (activeBlastingTiles[i] != null && activeBlastingTiles[i].visual != null)
            {
                activeBlastingTiles[i].visual.DestroyPiece();
            }
        }
        activeBlastingTiles.Clear();

        if (blockBlastBoardBgRoot != null)
        {
            Destroy(blockBlastBoardBgRoot);
            blockBlastBoardBgRoot = null;
        }
    }

    private void GetBlockBlastGridMetrics(out int cols, out int rows, out float cellSize, out Vector2 gridOriginBottomLeft)
    {
        cols = blockBlastGrid != null ? blockBlastGrid.GetLength(0) : Mathf.Clamp(blockBlastGridCols, 6, 10);
        rows = blockBlastGrid != null ? blockBlastGrid.GetLength(1) : Mathf.Clamp(blockBlastGridRows, 6, 10);

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float screenW = (camMaxX - camMinX) * 0.90f;
        float screenH = (camMaxY - camMinY) * 0.90f;

        cellSize = Mathf.Min(screenW / cols, screenH / rows);
        float totalW = cols * cellSize;
        float totalH = rows * cellSize;

        float centerX = (camMinX + camMaxX) * 0.5f;
        float centerY = (camMinY + camMaxY) * 0.5f;
        gridOriginBottomLeft = new Vector2(centerX - totalW * 0.5f, centerY - totalH * 0.5f);
    }

    private Vector2 GetBlockBlastCellCenter(int gx, int gy, float cellSize, Vector2 gridOriginBottomLeft)
    {
        return new Vector2(
            gridOriginBottomLeft.x + (gx + 0.5f) * cellSize,
            gridOriginBottomLeft.y + (gy + 0.5f) * cellSize);
    }

    private void BuildBlockBlastGridBackground(int cols, int rows)
    {
        if (blockBlastBoardBgRoot != null) Destroy(blockBlastBoardBgRoot);

        GetBlockBlastGridMetrics(out _, out _, out float cellSize, out Vector2 origin);
        int layer = mainBouncer != null ? mainBouncer.gameObject.layer : 0;
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z + 0.12f : 5.12f;

        blockBlastBoardBgRoot = new GameObject("BlockBlast_GridBackground");
        blockBlastBoardBgRoot.layer = layer;
        if (orthoCamera != null && orthoCamera.transform.parent != null)
        {
            blockBlastBoardBgRoot.transform.SetParent(orthoCamera.transform.parent, true);
        }

        Material cellMatA = CreateUnlitColorMaterial(new Color(0.12f, 0.16f, 0.24f, 0.42f), true);
        Material cellMatB = CreateUnlitColorMaterial(new Color(0.16f, 0.21f, 0.30f, 0.42f), true);

        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                GameObject cellQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cellQuad.name = $"Cell_{x}_{y}";
                cellQuad.layer = layer;
                Collider c = cellQuad.GetComponent<Collider>();
                if (c != null) Destroy(c);

                cellQuad.transform.SetParent(blockBlastBoardBgRoot.transform, true);
                Vector2 center = GetBlockBlastCellCenter(x, y, cellSize, origin);
                cellQuad.transform.position = new Vector3(center.x, center.y, zPlane);
                cellQuad.transform.localScale = new Vector3(cellSize * 0.92f, cellSize * 0.92f, 1f);
                cellQuad.GetComponent<Renderer>().sharedMaterial = ((x + y) % 2 == 0) ? cellMatA : cellMatB;
            }
        }
    }

    private DeformableLogoPiece CreateSquareBlockBlastTile(string name, float cellSize, Color colA, Color colB, bool isGhostTransparent)
    {
        if (mainBouncer == null) return null;

        Vector2 baseHalf = mainBouncer.GetBaseHalfSize();
        float targetHalf = (cellSize * 0.44f);
        float baseScaleMult = targetHalf / Mathf.Max(0.05f, baseHalf.y);
        float squareWidthCompensation = baseHalf.y / Mathf.Max(0.05f, baseHalf.x);

        DeformableLogoPiece tile = CreateDeformableLogoClone(
            name,
            baseScaleMult,
            colA,
            colB,
            squareWidthCompensation,
            1.0f,
            1.0f,
            0f,
            isGhostTransparent);

        return tile;
    }

    private void SpawnGhostTilesForShape(int shapeIdx)
    {
        for (int i = 0; i < blockBlastGhostTiles.Count; i++)
        {
            if (blockBlastGhostTiles[i] != null) blockBlastGhostTiles[i].DestroyPiece();
        }
        blockBlastGhostTiles.Clear();

        currentBlockBlastShapeIdx = Mathf.Clamp(shapeIdx, 0, BlockBlastShapes.Length - 1);
        BlockBlastShapeDef shape = BlockBlastShapes[currentBlockBlastShapeIdx];

        GetBlockBlastGridMetrics(out _, out _, out float cellSize, out _);

        Color ghostPrimary = new Color(shape.primaryColor.r, shape.primaryColor.g, shape.primaryColor.b, blockBlastGhostAlpha);
        Color ghostSecondary = new Color(shape.secondaryColor.r, shape.secondaryColor.g, shape.secondaryColor.b, blockBlastGhostAlpha);

        for (int i = 0; i < shape.offsets.Length; i++)
        {
            DeformableLogoPiece ghostTile = CreateSquareBlockBlastTile($"GhostTile_{i}", cellSize, ghostPrimary, ghostSecondary, true);
            if (ghostTile != null)
            {
                ghostTile.TriggerPop(0.24f, 0.24f);
                blockBlastGhostTiles.Add(ghostTile);
            }
        }
    }

    private void RollNextBlockBlastShape()
    {
        int shapeCount = BlockBlastShapes.Length;
        if (blockBlastQueue == null || blockBlastQueue.Length < 2) blockBlastQueue = new int[] { -1, -1 };
        if (blockBlastQueue[0] < 0) blockBlastQueue[0] = Random.Range(0, shapeCount);
        if (blockBlastQueue[1] < 0) blockBlastQueue[1] = Random.Range(0, shapeCount);

        // Current piece comes from the queue; the queue shifts and gets a fresh random piece at the back
        int nextIdx = blockBlastQueue[0];
        blockBlastQueue[0] = blockBlastQueue[1];
        blockBlastQueue[1] = Random.Range(0, shapeCount);

        SpawnGhostTilesForShape(nextIdx);
        RebuildBlockBlastPreviews();

        GetBlockBlastGridMetrics(out int cols, out int rows, out _, out _);
        BlockBlastShapeDef shape = BlockBlastShapes[currentBlockBlastShapeIdx];

        // Check if this newly rolled piece can fit ANYWHERE on the board!
        if (!CanShapeFitAnywhereOnBoard(shape, cols, rows))
        {
            TriggerBlockBlastNoSpaceReset();
        }
    }

    private void ClearBlockBlastPreviews()
    {
        for (int i = 0; i < blockBlastPreviewTiles.Count; i++)
        {
            if (blockBlastPreviewTiles[i] != null) blockBlastPreviewTiles[i].DestroyPiece();
        }
        blockBlastPreviewTiles.Clear();
    }

    /// <summary>
    /// Shows the next two upcoming shapes (shrunk) in the empty space left and right of the grid:
    /// left = the piece after the current one, right = the one after that.
    /// </summary>
    private void RebuildBlockBlastPreviews()
    {
        ClearBlockBlastPreviews();
        if (mainBouncer == null || orthoCamera == null || blockBlastQueue == null || blockBlastQueue.Length < 2) return;

        GetBlockBlastGridMetrics(out int cols, out _, out float cellSize, out Vector2 origin);
        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);

        float gridLeft = origin.x;
        float gridRight = origin.x + cols * cellSize;
        float zPlane = mainBouncer.transform.position.z;
        float centerY = (camMinY + camMaxY) * 0.5f;

        for (int slot = 0; slot < 2; slot++)
        {
            int idx = blockBlastQueue[slot];
            if (idx < 0 || idx >= BlockBlastShapes.Length) continue;
            BlockBlastShapeDef shape = BlockBlastShapes[idx];

            float marginMin = slot == 0 ? camMinX : gridRight;
            float marginMax = slot == 0 ? gridLeft : camMaxX;
            float marginW = Mathf.Max(0.2f, marginMax - marginMin);
            float marginCenterX = (marginMin + marginMax) * 0.5f;

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < shape.offsets.Length; i++)
            {
                minX = Mathf.Min(minX, shape.offsets[i].x); maxX = Mathf.Max(maxX, shape.offsets[i].x);
                minY = Mathf.Min(minY, shape.offsets[i].y); maxY = Mathf.Max(maxY, shape.offsets[i].y);
            }
            int spanX = maxX - minX + 1;
            int spanY = maxY - minY + 1;

            // Shrink so the shape always fits inside the side margin
            float previewCell = Mathf.Min(cellSize * 0.62f, (marginW * 0.78f) / Mathf.Max(1, spanX));
            previewCell = Mathf.Min(previewCell, (cellSize * 4f) / Mathf.Max(1, spanY));
            Vector2 shapeCenterCells = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            for (int i = 0; i < shape.offsets.Length; i++)
            {
                DeformableLogoPiece tile = CreateSquareBlockBlastTile($"PreviewTile_{slot}_{i}", previewCell, shape.primaryColor, shape.secondaryColor, false);
                if (tile == null) continue;
                Vector2 rel = (new Vector2(shape.offsets[i].x, shape.offsets[i].y) - shapeCenterCells) * previewCell;
                tile.SetCenterPosition(new Vector3(marginCenterX + rel.x, centerY + rel.y, zPlane));
                tile.TriggerPop(0.20f, 0.22f);
                tile.UpdateVisuals(0f);
                blockBlastPreviewTiles.Add(tile);
            }
        }
    }

    private bool CanShapeFitAtCell(BlockBlastShapeDef shape, int anchorX, int anchorY, int cols, int rows)
    {
        if (blockBlastGrid == null) return false;
        for (int i = 0; i < shape.offsets.Length; i++)
        {
            int gx = anchorX + shape.offsets[i].x;
            int gy = anchorY + shape.offsets[i].y;
            if (gx < 0 || gx >= cols || gy < 0 || gy >= rows) return false;
            if (blockBlastGrid[gx, gy] != null) return false;
        }
        return true;
    }

    private bool CanShapeFitAnywhereOnBoard(BlockBlastShapeDef shape, int cols, int rows)
    {
        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                if (CanShapeFitAtCell(shape, x, y, cols, rows))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void TryPlaceCurrentBlockBlastShape()
    {
        if (isBlockBlastShatteringReset || blockBlastGrid == null) return;

        GetBlockBlastGridMetrics(out int cols, out int rows, out float cellSize, out Vector2 origin);
        BlockBlastShapeDef shape = BlockBlastShapes[currentBlockBlastShapeIdx];

        if (!CanShapeFitAtCell(shape, currentSnappedGridCell.x, currentSnappedGridCell.y, cols, rows))
        {
            // Invalid spot (overlapping or out of bounds) -> subtle feedback wiggle
            for (int i = 0; i < blockBlastGhostTiles.Count; i++)
            {
                if (blockBlastGhostTiles[i] != null) blockBlastGhostTiles[i].TriggerSquash(Vector2.right, 0.16f, 0.28f);
            }
            return;
        }

        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : 5f;
        Vector2 placedCenterSum = Vector2.zero;

        // Place each square DVD logo tile onto the grid!
        for (int i = 0; i < shape.offsets.Length; i++)
        {
            int gx = currentSnappedGridCell.x + shape.offsets[i].x;
            int gy = currentSnappedGridCell.y + shape.offsets[i].y;
            Vector2 cellWorld = GetBlockBlastCellCenter(gx, gy, cellSize, origin);
            placedCenterSum += cellWorld;

            DeformableLogoPiece placedTile = CreateSquareBlockBlastTile(
                $"PlacedTile_{gx}_{gy}",
                cellSize,
                shape.primaryColor,
                shape.secondaryColor,
                false);

            if (placedTile != null)
            {
                placedTile.SetCenterPosition(new Vector3(cellWorld.x, cellWorld.y, zPlane));
                placedTile.TriggerPop(0.32f, 0.28f);
                placedTile.TriggerSquash(Vector2.up, 0.18f, 0.36f);
                placedTile.UpdateVisuals(0f);
                blockBlastGrid[gx, gy] = placedTile;
            }
        }

        Vector2 avgPlacedPos = placedCenterSum / Mathf.Max(1, shape.offsets.Length);
        mainBouncer.AwardMinigameCornerReward(1, new Vector3(avgPlacedPos.x, avgPlacedPos.y, zPlane), shape.primaryColor, shape.secondaryColor, true);
        PlaySound(blockBlastPlaceSound, 1.06f);

        // Check for completed full rows and full columns!
        CheckAndBlastCompletedLines(cols, rows, cellSize, origin, zPlane);

        // Roll next shape (and automatically check if space remains!)
        RollNextBlockBlastShape();
    }

    private void CheckAndBlastCompletedLines(int cols, int rows, float cellSize, Vector2 origin, float zPlane)
    {
        List<int> fullRows = new List<int>();
        List<int> fullCols = new List<int>();

        for (int y = 0; y < rows; y++)
        {
            bool rowFull = true;
            for (int x = 0; x < cols; x++)
            {
                if (blockBlastGrid[x, y] == null)
                {
                    rowFull = false;
                    break;
                }
            }
            if (rowFull) fullRows.Add(y);
        }

        for (int x = 0; x < cols; x++)
        {
            bool colFull = true;
            for (int y = 0; y < rows; y++)
            {
                if (blockBlastGrid[x, y] == null)
                {
                    colFull = false;
                    break;
                }
            }
            if (colFull) fullCols.Add(x);
        }

        int totalLinesCleared = fullRows.Count + fullCols.Count;
        if (totalLinesCleared <= 0) return;

        bool[,] toBlast = new bool[cols, rows];
        for (int r = 0; r < fullRows.Count; r++)
        {
            int y = fullRows[r];
            for (int x = 0; x < cols; x++) toBlast[x, y] = true;
        }
        for (int c = 0; c < fullCols.Count; c++)
        {
            int x = fullCols[c];
            for (int y = 0; y < rows; y++) toBlast[x, y] = true;
        }

        Vector2 boardCenter = GetBlockBlastCellCenter(cols / 2, rows / 2, cellSize, origin);
        Color lastPrimary = Color.cyan;
        Color lastSecondary = Color.yellow;

        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                if (!toBlast[x, y]) continue;
                DeformableLogoPiece tile = blockBlastGrid[x, y];
                blockBlastGrid[x, y] = null;
                if (tile == null) continue;

                lastPrimary = tile.primaryColor;
                lastSecondary = tile.secondaryColor;
                Vector2 cellPos = GetBlockBlastCellCenter(x, y, cellSize, origin);

                // Spawn dual-color circular particles along the blasted line
                if ((x + y) % 2 == 0)
                {
                    mainBouncer.TriggerHitParticlesWithColors(new Vector3(cellPos.x, cellPos.y, zPlane), tile.primaryColor, tile.secondaryColor);
                }

                // Launch the tile into a satisfying outward blast animation!
                Vector2 outDir = (cellPos - boardCenter);
                if (outDir.sqrMagnitude < 0.01f) outDir = Random.insideUnitCircle;
                outDir.Normalize();

                tile.TriggerPop(0.55f, 0.42f);
                tile.TriggerSquash(outDir, 0.30f, 0.42f);

                activeBlastingTiles.Add(new FlyingBlockBlastTile
                {
                    visual = tile,
                    pos = cellPos,
                    vel = outDir * Random.Range(3.5f, 7.5f) + new Vector2(0f, Random.Range(2.0f, 4.5f)),
                    angularVelZ = Random.Range(-360f, 360f),
                    angleZ = 0f,
                    timer = 0f,
                    duration = 0.46f,
                    startScale = tile.overallScaleFactor
                });
            }
        }

        // Combo cash reward for blasting 1 or more lines!
        int comboCornerMult = totalLinesCleared * totalLinesCleared * 3;
        mainBouncer.AwardMinigameCornerReward(comboCornerMult, new Vector3(boardCenter.x, boardCenter.y, zPlane), lastPrimary, lastSecondary, true);
        PlaySound(blockBlastClearSound, Mathf.Clamp(1.05f + totalLinesCleared * 0.12f, 1.0f, 1.6f));
    }

    private void TriggerBlockBlastNoSpaceReset()
    {
        if (isBlockBlastShatteringReset) return;
        hasSavedBlockBlastState = false;
        isBlockBlastShatteringReset = true;
        blockBlastShatterTimer = 0f;

        GetBlockBlastGridMetrics(out int cols, out int rows, out float cellSize, out Vector2 origin);
        Vector2 boardCenter = GetBlockBlastCellCenter(cols / 2, rows / 2, cellSize, origin);
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z : 5f;

        // Flash the screen semi-transparent red & apply -3x CornerHitValue penalty
        TriggerScreenRedFlash(0.52f, screenRedFlashMaxAlpha);
        mainBouncer.ApplyMinigameCornerPenalty(3, new Vector3(boardCenter.x, boardCenter.y, zPlane));
        PlaySound(damageHitSound, 0.76f);

        // Shatter every placed block on the board outward!
        if (blockBlastGrid != null)
        {
            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    DeformableLogoPiece tile = blockBlastGrid[x, y];
                    blockBlastGrid[x, y] = null;
                    if (tile == null) continue;

                    Vector2 cellPos = GetBlockBlastCellCenter(x, y, cellSize, origin);
                    Vector2 shatterDir = (cellPos - boardCenter) + Random.insideUnitCircle * 0.65f;
                    if (shatterDir.sqrMagnitude < 0.01f) shatterDir = Vector2.up;
                    shatterDir.Normalize();

                    tile.TriggerPop(0.38f, 0.36f);
                    tile.TriggerSquash(shatterDir, 0.28f, 0.50f);

                    activeBlastingTiles.Add(new FlyingBlockBlastTile
                    {
                        visual = tile,
                        pos = cellPos,
                        vel = shatterDir * Random.Range(4.0f, 9.2f) + new Vector2(0f, Random.Range(1.5f, 4.0f)),
                        angularVelZ = Random.Range(-420f, 420f),
                        angleZ = 0f,
                        timer = 0f,
                        duration = blockBlastShatterDuration,
                        startScale = tile.overallScaleFactor
                    });
                }
            }
        }

        // Also shatter the ghost tiles that couldn't fit
        for (int i = 0; i < blockBlastGhostTiles.Count; i++)
        {
            if (blockBlastGhostTiles[i] != null)
            {
                blockBlastGhostTiles[i].DestroyPiece();
            }
        }
        blockBlastGhostTiles.Clear();
    }

    private void UpdateBlockBlast(float dt)
    {
        if (mainBouncer == null || orthoCamera == null || blockBlastGrid == null) return;

        GetBlockBlastGridMetrics(out int cols, out int rows, out float cellSize, out Vector2 origin);
        float zPlane = mainBouncer.transform.position.z;

        // 1. Update any blasting / shattering tiles flying across the screen
        for (int i = activeBlastingTiles.Count - 1; i >= 0; i--)
        {
            FlyingBlockBlastTile ft = activeBlastingTiles[i];
            if (ft == null || ft.visual == null)
            {
                activeBlastingTiles.RemoveAt(i);
                continue;
            }

            ft.timer += dt;
            float norm = Mathf.Clamp01(ft.timer / Mathf.Max(0.1f, ft.duration));
            ft.vel.y -= 18.0f * dt;
            ft.pos += ft.vel * dt;
            ft.angleZ += ft.angularVelZ * dt;

            ft.visual.overallScaleFactor = Mathf.Lerp(ft.startScale, 0.05f, norm * norm);
            ft.visual.SetCenterPosition(new Vector3(ft.pos.x, ft.pos.y, zPlane - 0.05f));
            if (ft.visual.rootObj != null)
            {
                ft.visual.rootObj.transform.rotation = Quaternion.Euler(0f, 0f, ft.angleZ);
            }
            ft.visual.UpdateVisuals(dt);

            if (norm >= 1f)
            {
                ft.visual.DestroyPiece();
                activeBlastingTiles.RemoveAt(i);
            }
        }

        // 2. If shattering the whole board due to no space left, wait for shatter to finish then roll a fresh piece
        if (isBlockBlastShatteringReset)
        {
            blockBlastShatterTimer += dt;
            if (blockBlastShatterTimer >= blockBlastShatterDuration)
            {
                isBlockBlastShatteringReset = false;
                RollNextBlockBlastShape();
            }
            return;
        }

        // 3. Update placed grid tiles' idle squash/pop animations
        for (int i = 0; i < blockBlastPreviewTiles.Count; i++)
        {
            if (blockBlastPreviewTiles[i] != null) blockBlastPreviewTiles[i].UpdateVisuals(dt);
        }
        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                if (blockBlastGrid[x, y] != null)
                {
                    blockBlastGrid[x, y].UpdateVisuals(dt);
                }
            }
        }

        // 4. Snap the cursor-following semi-transparent shape to the grid!
        BlockBlastShapeDef shape = BlockBlastShapes[currentBlockBlastShapeIdx];

        // Compute bounding span of the shape in grid cells so we can clamp its anchor inside the 8x8 board
        int minOx = 0, maxOx = 0, minOy = 0, maxOy = 0;
        for (int i = 0; i < shape.offsets.Length; i++)
        {
            Vector2Int off = shape.offsets[i];
            if (off.x < minOx) minOx = off.x;
            if (off.x > maxOx) maxOx = off.x;
            if (off.y < minOy) minOy = off.y;
            if (off.y > maxOy) maxOy = off.y;
        }

        int rawGx = Mathf.FloorToInt((targetCursorOrthoPos.x - origin.x) / cellSize);
        int rawGy = Mathf.FloorToInt((targetCursorOrthoPos.y - origin.y) / cellSize);

        int clampedGx = Mathf.Clamp(rawGx, -minOx, (cols - 1) - maxOx);
        int clampedGy = Mathf.Clamp(rawGy, -minOy, (rows - 1) - maxOy);

        currentSnappedGridCell = new Vector2Int(clampedGx, clampedGy);
        currentGhostPlacementValid = CanShapeFitAtCell(shape, clampedGx, clampedGy, cols, rows);

        Color validPrimary = shape.primaryColor;
        Color validSecondary = shape.secondaryColor;
        Color invalidTint = new Color(1.0f, 0.22f, 0.22f, 1f);

        for (int i = 0; i < blockBlastGhostTiles.Count && i < shape.offsets.Length; i++)
        {
            DeformableLogoPiece ghost = blockBlastGhostTiles[i];
            if (ghost == null) continue;

            int gx = clampedGx + shape.offsets[i].x;
            int gy = clampedGy + shape.offsets[i].y;
            Vector2 cellPos = GetBlockBlastCellCenter(gx, gy, cellSize, origin);

            if (currentGhostPlacementValid)
            {
                ghost.SetColorsAndAlpha(validPrimary, validSecondary, blockBlastGhostAlpha);
            }
            else
            {
                ghost.SetColorsAndAlpha(invalidTint, invalidTint, blockBlastGhostAlpha * 0.65f);
            }

            ghost.SetCenterPosition(new Vector3(cellPos.x, cellPos.y, zPlane - 0.04f));
            ghost.UpdateVisuals(dt);
        }
    }

    // =========================================================================
    // SHARED IN-SCREEN SEMI-TRANSPARENT RED FLASH (ALL MINIGAMES)
    // =========================================================================

    /// <summary>
    /// Flashes the TV screen with a semi-transparent red overlay inside SCREENTRANSFER
    /// whenever the player takes damage, loses money, or resets a minigame board.
    /// </summary>
    public void TriggerScreenRedFlash(float customDuration = -1f, float customMaxAlpha = -1f)
    {
        if (orthoCamera == null) return;

        activeRedFlashDuration = customDuration > 0.05f ? customDuration : screenRedFlashDuration;
        activeRedFlashMaxAlpha = customMaxAlpha > 0.05f ? customMaxAlpha : screenRedFlashMaxAlpha;
        redFlashTimer = 0f;

        EnsureRedFlashQuadExists();
        if (redFlashQuadObj != null)
        {
            redFlashQuadObj.SetActive(true);
            UpdateRedFlashQuadTransformAndAlpha(activeRedFlashMaxAlpha);
        }
    }

    private void EnsureRedFlashQuadExists()
    {
        if (redFlashQuadObj != null) return;
        if (orthoCamera == null) return;

        int layer = mainBouncer != null ? mainBouncer.gameObject.layer : 0;
        redFlashQuadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        redFlashQuadObj.name = "TVMinigame_RedScreenFlash";
        redFlashQuadObj.layer = layer;
        Collider c = redFlashQuadObj.GetComponent<Collider>();
        if (c != null) Destroy(c);

        if (orthoCamera.transform.parent != null)
        {
            redFlashQuadObj.transform.SetParent(orthoCamera.transform.parent, true);
        }

        redFlashMaterial = CreateUnlitColorMaterial(new Color(screenRedFlashColor.r, screenRedFlashColor.g, screenRedFlashColor.b, 0f), true);
        redFlashMaterial.renderQueue = 3250;
        redFlashQuadObj.GetComponent<Renderer>().sharedMaterial = redFlashMaterial;
        redFlashQuadObj.SetActive(false);
    }

    private void UpdateRedScreenFlash(float dt)
    {
        if (redFlashTimer < 0f) return;

        redFlashTimer += dt;
        float norm = redFlashTimer / Mathf.Max(0.05f, activeRedFlashDuration);
        if (norm >= 1f)
        {
            redFlashTimer = -1f;
            if (redFlashQuadObj != null) redFlashQuadObj.SetActive(false);
            return;
        }

        // Quick punch in, smooth fade out
        float pulse = norm < 0.2f ? (norm / 0.2f) : (1f - ((norm - 0.2f) / 0.8f));
        float alpha = Mathf.Clamp01(pulse) * activeRedFlashMaxAlpha;
        UpdateRedFlashQuadTransformAndAlpha(alpha);
    }

    private void UpdateRedFlashQuadTransformAndAlpha(float alpha)
    {
        if (redFlashQuadObj == null || orthoCamera == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float zPlane = mainBouncer != null ? mainBouncer.transform.position.z - 0.18f : (orthoCamera.transform.position.z + 2f);

        redFlashQuadObj.transform.position = new Vector3((camMinX + camMaxX) * 0.5f, (camMinY + camMaxY) * 0.5f, zPlane);
        redFlashQuadObj.transform.localScale = new Vector3((camMaxX - camMinX) + 1.5f, (camMaxY - camMinY) + 1.5f, 1f);

        if (redFlashMaterial != null)
        {
            Color c = new Color(screenRedFlashColor.r, screenRedFlashColor.g, screenRedFlashColor.b, alpha);
            if (redFlashMaterial.HasProperty("_BaseColor")) redFlashMaterial.SetColor("_BaseColor", c);
            if (redFlashMaterial.HasProperty("_Color")) redFlashMaterial.SetColor("_Color", c);
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
        float shapeSkewX = 0f,
        bool useTransparentMaterial = false)
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
                primaryRend.sharedMaterial = CreateUnlitColorMaterial(primaryColor, useTransparentMaterial);
                piece.primaryRenderers.Add(primaryRend);
            }

            Transform torusClone = dvdClone.transform.Find("Torus");
            if (torusClone != null)
            {
                Renderer secondaryRend = torusClone.GetComponent<Renderer>();
                if (secondaryRend != null)
                {
                    secondaryRend.enabled = true;
                    secondaryRend.sharedMaterial = CreateUnlitColorMaterial(secondaryColor, useTransparentMaterial);
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
    // MINIGAME 7: SUBWAY RUNNER (3 CURVED GLOBE LANES, HOLD-SWIPE, TRAINS)
    // =========================================================================

    private enum SubwayEntityType
    {
        Coin,
        SmallObstacle,
        Ramp,
        Train
    }

    private class SubwayEntity
    {
        public SubwayEntityType type;
        public GameObject rootObj;
        public int laneIndex;          // -1, 0, +1
        public float depth01;          // 0 (horizon) -> 1 (player at 0.88) -> 1.2 (offscreen)
        public float depthLength;      // depth span along track (e.g. 0.28 for trains)
        public float elevationOffset;  // e.g. coins placed on top of trains
        public float heightOrtho;      // physical height in ortho units at player scale
        public bool collectedOrHit;
        public float spinAngle;
    }

    private GameObject subwayTrackRoot;
    private readonly List<Transform> subwayRailSegments = new List<Transform>();
    private readonly List<Transform> subwaySleeperSegments = new List<Transform>();
    private readonly List<SubwayEntity> subwayEntities = new List<SubwayEntity>();
    private DeformableLogoPiece subwayPlayerPiece;
    private int subwayTargetLane = 0;        // -1, 0, 1
    private float subwayCurrentLaneFloat = 0f;
    private float subwayPlayerHeight = 0f;   // Height above track floor (0 = ground, ~0.95 = small obstacle top, ~1.95 = train roof)
    private float subwayVerticalVel = 0f;
    private bool subwayIsGrounded = true;
    private float subwayScrollPhase = 0f;
    private float subwayCurvePhase = 0f;
    private float subwayCurrentTrackSpeed = 0.21f;
    private float subwayDistanceSinceLastWave = 0f;
    private int subwayWaveCounter = 0;
    private int subwayLastTrainLane = -99;
    private bool subwaySwipeHeldPrev = false;
    private Vector2 subwaySwipeAnchor = Vector2.zero;
    private const float SubwayPlayerDepth01 = 0.88f;
    private const float SubwaySmallObstacleHeight = 0.85f;
    private const float SubwayTrainHeight = 2.10f;

    private void EnterSubwayRunnerMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        StopAnyActiveConvergeAnimation();
        mainBouncer.DespawnAllChildLogosForMinigame();
        mainBouncer.SetMainLogoRenderersVisible(false);
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();

        ClearSubwayEntities();
        DestroySubwayTrackVisuals();

        subwayTargetLane = 0;
        subwayCurrentLaneFloat = 0f;
        subwayPlayerHeight = 0f;
        subwayVerticalVel = 0f;
        subwayIsGrounded = true;
        subwayScrollPhase = 0f;
        subwayCurvePhase = 0f;
        subwayCurrentTrackSpeed = 0.21f; // Starts slower and builds up smoothly over time!
        subwayDistanceSinceLastWave = 0f;
        subwayWaveCounter = 0;
        subwayLastTrainLane = -99;
        subwaySwipeHeldPrev = false;

        BuildSubwayTrackVisuals();

        Color pCol = new Color(0.20f, 0.95f, 1.00f, 1f);
        Color sCol = new Color(1.00f, 0.32f, 0.78f, 1f);
        subwayPlayerPiece = CreateDeformableLogoClone("SubwayPlayer_DVD", 0.56f, pCol, sCol);
        if (subwayPlayerPiece != null)
        {
            subwayPlayerPiece.TriggerPop(0.32f, 0.30f);
        }

        // Pre-seed two well-spaced opening waves so the track starts readable and fair
        SpawnSubwayPatternAtDepth(0.10f);
        SpawnSubwayPatternAtDepth(0.44f);
    }

    private void ExitSubwayRunnerMode()
    {
        ClearSubwayEntities();
        DestroySubwayTrackVisuals();

        if (subwayPlayerPiece != null)
        {
            subwayPlayerPiece.DestroyPiece();
            subwayPlayerPiece = null;
        }

        if (mainBouncer != null)
        {
            mainBouncer.SetMainLogoRenderersVisible(true);
            mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
        }
    }

    private void BuildSubwayTrackVisuals()
    {
        if (mainBouncer == null) return;
        int layer = mainBouncer.gameObject.layer;

        subwayTrackRoot = new GameObject("SubwayCurvedTrackRoot");
        subwayTrackRoot.layer = layer;
        if (mainBouncer.transform.parent != null)
        {
            subwayTrackRoot.transform.SetParent(mainBouncer.transform.parent, false);
        }

        Material railMat = CreateUnlitColorMaterial(new Color(0.28f, 0.85f, 1.0f, 0.55f), true);
        Material sleeperMat = CreateUnlitColorMaterial(new Color(0.95f, 0.75f, 0.25f, 0.38f), true);

        // 4 lane divider rails (-1.5, -0.5, +0.5, +1.5), each with 14 segments along the curved globe
        const int segsPerRail = 14;
        for (int r = 0; r < 4; r++)
        {
            for (int s = 0; s < segsPerRail; s++)
            {
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Quad);
                seg.name = $"SubwayRail_{r}_{s}";
                seg.layer = layer;
                seg.transform.SetParent(subwayTrackRoot.transform, false);
                Collider col = seg.GetComponent<Collider>();
                if (col != null) Destroy(col);
                Renderer rend = seg.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = railMat;
                subwayRailSegments.Add(seg.transform);
            }
        }

        // 3 lanes x 9 scrolling horizontal sleepers
        for (int lane = 0; lane < 3; lane++)
        {
            for (int k = 0; k < 9; k++)
            {
                GameObject tie = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tie.name = $"SubwayTie_{lane}_{k}";
                tie.layer = layer;
                tie.transform.SetParent(subwayTrackRoot.transform, false);
                Collider col = tie.GetComponent<Collider>();
                if (col != null) Destroy(col);
                Renderer rend = tie.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = sleeperMat;
                subwaySleeperSegments.Add(tie.transform);
            }
        }
    }

    private void DestroySubwayTrackVisuals()
    {
        subwayRailSegments.Clear();
        subwaySleeperSegments.Clear();
        if (subwayTrackRoot != null)
        {
            Destroy(subwayTrackRoot);
            subwayTrackRoot = null;
        }
    }

    private void ClearSubwayEntities()
    {
        for (int i = 0; i < subwayEntities.Count; i++)
        {
            if (subwayEntities[i] != null && subwayEntities[i].rootObj != null)
            {
                Destroy(subwayEntities[i].rootObj);
            }
        }
        subwayEntities.Clear();
    }

    /// <summary>
    /// Projects a (laneFloat, depth01, elevationOrtho) coordinate onto the curved 3-lane globe track.
    /// depth01 = 0 is the curved horizon at the top; depth01 = 1 is the foreground bottom.
    /// </summary>
    private void ProjectSubwayPoint(float laneFloat, float depth01, float elevationOrtho, out Vector3 worldPos, out float perspScale)
    {
        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float centerX = (camMinX + camMaxX) * 0.5f;
        float spanW = camMaxX - camMinX;
        float spanH = camMaxY - camMinY;
        float baseZ = mainBouncer != null ? mainBouncer.transform.position.z : 0f;

        float d = Mathf.Clamp(depth01, 0f, 1.25f);
        // Non-linear globe curvature: compressed near the top horizon, accelerating toward the camera
        float curvedDepth = Mathf.Pow(Mathf.Max(0.001f, d), 1.48f);
        perspScale = Mathf.Lerp(0.20f, 1.08f, curvedDepth);

        // Horizon sits at 78% screen height; foreground sits at 14% screen height, with a subtle globe arch in the middle
        float horizonY = camMinY + spanH * 0.78f;
        float nearY = camMinY + spanH * 0.13f;
        float globeArch = Mathf.Sin(Mathf.Clamp01(d) * Mathf.PI) * (spanH * 0.065f);
        float trackY = Mathf.Lerp(horizonY, nearY, curvedDepth) + globeArch;

        // Curved inward bend from afar (oscillates gently as the track scrolls)
        float farFactor = 1f - Mathf.Clamp01(d);
        float horizonBendX = Mathf.Sin(subwayCurvePhase) * (spanW * 0.14f) * (farFactor * farFactor);

        float laneSpacingNear = spanW * 0.23f;
        float laneSpacing = laneSpacingNear * perspScale;

        float x = centerX + horizonBendX + laneFloat * laneSpacing;
        float y = trackY + elevationOrtho * perspScale;
        float z = baseZ + (1f - d) * 0.08f;

        worldPos = new Vector3(x, y, z);
    }

    private void UpdateSubwayRunner(float dt)
    {
        if (mainBouncer == null || orthoCamera == null || subwayPlayerPiece == null) return;

        // 1. Handle Hold-to-Swipe Input (ONLY detects swipes while left-click is held!)
        bool leftHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (leftHeld)
        {
            if (!subwaySwipeHeldPrev)
            {
                subwaySwipeHeldPrev = true;
                subwaySwipeAnchor = targetCursorOrthoPos;
            }
            else
            {
                Vector2 delta = targetCursorOrthoPos - subwaySwipeAnchor;
                const float swipeHorizontalThreshold = 0.78f;
                const float swipeVerticalThreshold = 0.72f;

                if (Mathf.Abs(delta.x) >= swipeHorizontalThreshold && Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) * 0.85f)
                {
                    int dir = delta.x > 0f ? 1 : -1;
                    int nextLane = Mathf.Clamp(subwayTargetLane + dir, -1, 1);
                    if (nextLane != subwayTargetLane)
                    {
                        subwayTargetLane = nextLane;
                        subwayPlayerPiece.TriggerSquash(Vector2.right, 0.22f, 0.32f);
                        PlaySound(jumpSound, 1.12f);
                    }
                    subwaySwipeAnchor = targetCursorOrthoPos;
                }
                else if (delta.y >= swipeVerticalThreshold)
                {
                    TriggerSubwayJump();
                    subwaySwipeAnchor = targetCursorOrthoPos;
                }
                else if (delta.y <= -swipeVerticalThreshold && !subwayIsGrounded)
                {
                    // Fast drop when swiping down in mid-air
                    subwayVerticalVel = Mathf.Min(subwayVerticalVel, -16f);
                    subwayPlayerPiece.TriggerSquash(Vector2.up, 0.20f, 0.28f);
                    subwaySwipeAnchor = targetCursorOrthoPos;
                }
            }
        }
        else
        {
            subwaySwipeHeldPrev = false;
        }

        // 2. Smooth lane interpolation & gradual track speed build-up
        subwayCurrentLaneFloat = Mathf.Lerp(subwayCurrentLaneFloat, subwayTargetLane, Mathf.Clamp01(dt * 15f));
        subwayCurrentTrackSpeed = Mathf.Min(0.46f, subwayCurrentTrackSpeed + 0.0042f * dt);
        float trackSpeed = subwayCurrentTrackSpeed;
        subwayScrollPhase = (subwayScrollPhase + dt * trackSpeed * 9f) % 1f;
        subwayCurvePhase += dt * 0.55f;

        UpdateSubwayTrackGeometry();

        // 3. Structured distance-based wave spawning (consistent spacing, never too crowded or too sparse!)
        subwayDistanceSinceLastWave += trackSpeed * dt;
        const float waveSpacingDepth = 0.48f;
        if (subwayDistanceSinceLastWave >= waveSpacingDepth)
        {
            subwayDistanceSinceLastWave -= waveSpacingDepth;
            SpawnSubwayPatternAtDepth(0.02f);
        }

        // 4. Determine active support floor under player (0 = ground, small obstacle top if stepping on one, or train roof!)
        float activeSupportFloor = 0f;
        bool onRampNow = false;

        for (int i = 0; i < subwayEntities.Count; i++)
        {
            SubwayEntity ent = subwayEntities[i];
            if (ent == null || ent.collectedOrHit) continue;
            if (Mathf.Abs(subwayCurrentLaneFloat - ent.laneIndex) > 0.42f) continue;

            float frontDepth = ent.depth01 + ent.depthLength * 0.5f;
            float backDepth = ent.depth01 - ent.depthLength * 0.5f;

            if (SubwayPlayerDepth01 >= backDepth - 0.04f && SubwayPlayerDepth01 <= frontDepth + 0.04f)
            {
                if (ent.type == SubwayEntityType.Ramp)
                {
                    onRampNow = true;
                    float rampProgress = Mathf.InverseLerp(frontDepth, backDepth, SubwayPlayerDepth01);
                    float rampTop = Mathf.Lerp(0.2f, SubwayTrainHeight, Mathf.Clamp01(rampProgress));
                    if (rampTop > activeSupportFloor) activeSupportFloor = rampTop;
                }
                else if (ent.type == SubwayEntityType.Train)
                {
                    // Can ONLY stand/ride on top of the train if elevated onto it via a Ramp or double-jump off a SmallObstacle!
                    if (subwayPlayerHeight >= SubwayTrainHeight - 0.22f)
                    {
                        activeSupportFloor = Mathf.Max(activeSupportFloor, SubwayTrainHeight);
                    }
                }
                else if (ent.type == SubwayEntityType.SmallObstacle)
                {
                    // Can land on top of a small obstacle and jump again from there onto a train!
                    if (subwayPlayerHeight >= SubwaySmallObstacleHeight - 0.28f && subwayVerticalVel <= 2.2f)
                    {
                        activeSupportFloor = Mathf.Max(activeSupportFloor, SubwaySmallObstacleHeight);
                    }
                }
            }
        }

        // 5. Update vertical jump physics
        if (onRampNow && subwayPlayerHeight < activeSupportFloor)
        {
            subwayPlayerHeight = activeSupportFloor;
            subwayVerticalVel = Mathf.Max(subwayVerticalVel, 4.5f);
            subwayIsGrounded = true;
        }
        else
        {
            subwayVerticalVel -= 26.5f * dt;
            subwayPlayerHeight += subwayVerticalVel * dt;

            if (subwayPlayerHeight <= activeSupportFloor)
            {
                bool wasAirborne = !subwayIsGrounded && subwayVerticalVel < -3f;
                subwayPlayerHeight = activeSupportFloor;
                subwayVerticalVel = 0f;
                subwayIsGrounded = true;
                if (wasAirborne)
                {
                    subwayPlayerPiece.TriggerSquash(Vector2.up, 0.16f, 0.28f);
                }
            }
            else
            {
                subwayIsGrounded = false;
            }
        }

        // 6. Update entities & check collisions (despawning immediately once past the player!)
        for (int i = subwayEntities.Count - 1; i >= 0; i--)
        {
            SubwayEntity ent = subwayEntities[i];
            if (ent == null || ent.rootObj == null)
            {
                subwayEntities.RemoveAt(i);
                continue;
            }

            ent.depth01 += trackSpeed * dt;
            float trailingEdge = ent.depth01 - ent.depthLength * 0.35f;
            if (trailingEdge > SubwayPlayerDepth01 + 0.045f)
            {
                Destroy(ent.rootObj);
                subwayEntities.RemoveAt(i);
                continue;
            }

            UpdateSubwayEntityVisualTransform(ent, dt);

            if (ent.collectedOrHit) continue;

            // Check overlap with player
            float laneDist = Mathf.Abs(subwayCurrentLaneFloat - ent.laneIndex);
            float halfDepth = Mathf.Max(0.045f, ent.depthLength * 0.5f);
            bool depthOverlap = Mathf.Abs(ent.depth01 - SubwayPlayerDepth01) <= halfDepth;

            if (laneDist < 0.42f && depthOverlap)
            {
                if (ent.type == SubwayEntityType.Coin)
                {
                    float coinBottom = ent.elevationOffset - 0.45f;
                    float coinTop = ent.elevationOffset + 0.75f;
                    if (subwayPlayerHeight >= coinBottom && subwayPlayerHeight <= coinTop)
                    {
                        ent.collectedOrHit = true;
                        Vector3 coinWorldPos = ent.rootObj.transform.position;
                        mainBouncer.AwardMinigameCornerReward(
                            1,
                            coinWorldPos,
                            new Color(1f, 0.88f, 0.18f, 1f),
                            new Color(1f, 0.55f, 0.10f, 1f));
                        subwayPlayerPiece.TriggerPop(0.18f, 0.22f);
                        PlaySound(enemyPassSound, 1.25f);
                        Destroy(ent.rootObj);
                        subwayEntities.RemoveAt(i);
                        continue;
                    }
                }
                else if (ent.type == SubwayEntityType.SmallObstacle)
                {
                    // Safe if jumped cleanly over OR landed on top of it as a stepping stone
                    if (subwayPlayerHeight < SubwaySmallObstacleHeight - 0.24f)
                    {
                        ent.collectedOrHit = true;
                        TriggerSubwayObstacleHit(ent);
                        Destroy(ent.rootObj);
                        subwayEntities.RemoveAt(i);
                        continue;
                    }
                }
                else if (ent.type == SubwayEntityType.Train)
                {
                    // Safe ONLY if riding on top of the train (reached via Ramp or SmallObstacle double-jump)!
                    if (subwayPlayerHeight < SubwayTrainHeight - 0.22f)
                    {
                        ent.collectedOrHit = true;
                        TriggerSubwayObstacleHit(ent);
                        Destroy(ent.rootObj);
                        subwayEntities.RemoveAt(i);
                        continue;
                    }
                }
            }
        }

        // 7. Position & deform player DVD logo
        ProjectSubwayPoint(subwayCurrentLaneFloat, SubwayPlayerDepth01, subwayPlayerHeight + 0.36f, out Vector3 playerWorldPos, out float playerPersp);
        playerWorldPos.z -= 0.12f;
        subwayPlayerPiece.overallScaleFactor = 0.54f * playerPersp;
        subwayPlayerPiece.SetCenterPosition(playerWorldPos);
        float tiltZ = (subwayCurrentLaneFloat - subwayTargetLane) * 18f;
        subwayPlayerPiece.rootObj.transform.localRotation = mainBouncer.transform.localRotation * Quaternion.Euler(0f, 0f, tiltZ);
        subwayPlayerPiece.UpdateVisuals(dt);
    }

    private void TriggerSubwayJump()
    {
        if (subwayIsGrounded)
        {
            // Peak jump height = (8.4^2)/(2*26.5) = 1.33f:
            // - High enough to jump over or land on a SmallObstacle (0.85f)
            // - NOT high enough to jump onto a Train (2.10f) from flat ground!
            // - Jumping again from the top of a SmallObstacle reaches 0.85 + 1.33 = 2.18f >= 2.10f (Train roof)!
            subwayVerticalVel = 8.4f;
            subwayIsGrounded = false;
            if (subwayPlayerPiece != null)
            {
                subwayPlayerPiece.TriggerSquash(Vector2.right, 0.20f, 0.32f);
            }
            PlaySound(jumpSound, 1.0f);
        }
    }

    private void TriggerSubwayObstacleHit(SubwayEntity ent)
    {
        if (mainBouncer == null) return;
        Vector3 hitPos = ent != null && ent.rootObj != null ? ent.rootObj.transform.position : Vector3.zero;
        mainBouncer.ApplyMinigameCornerPenalty(3, hitPos);
        mainBouncer.TriggerHitParticlesWithColors(
            hitPos,
            new Color(1f, 0.2f, 0.2f, 1f),
            new Color(1f, 0.65f, 0.15f, 1f));
        TriggerScreenRedFlash(screenRedFlashDuration, screenRedFlashMaxAlpha);
        if (subwayPlayerPiece != null)
        {
            subwayPlayerPiece.TriggerSquash(Vector2.up, 0.32f, 0.44f);
        }
        PlaySound(damageHitSound, 0.82f);
    }

    private void UpdateSubwayTrackGeometry()
    {
        if (subwayTrackRoot == null) return;

        const int segsPerRail = 14;
        float[] railLanes = new float[] { -1.5f, -0.5f, 0.5f, 1.5f };

        int segIdx = 0;
        for (int r = 0; r < 4; r++)
        {
            float railLane = railLanes[r];
            for (int s = 0; s < segsPerRail; s++)
            {
                if (segIdx >= subwayRailSegments.Count) break;
                Transform seg = subwayRailSegments[segIdx++];
                float d0 = s / (float)segsPerRail;
                float d1 = (s + 1) / (float)segsPerRail;
                ProjectSubwayPoint(railLane, d0, 0f, out Vector3 p0, out float sc0);
                ProjectSubwayPoint(railLane, d1, 0f, out Vector3 p1, out float sc1);

                Vector3 mid = (p0 + p1) * 0.5f;
                mid.z += 0.05f;
                Vector3 dir = p1 - p0;
                float len = dir.magnitude;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                float thickness = Mathf.Lerp(0.035f, 0.11f, (sc0 + sc1) * 0.5f);

                seg.position = mid;
                seg.rotation = Quaternion.Euler(0f, 0f, angle);
                seg.localScale = new Vector3(thickness, len + 0.02f, 1f);
            }
        }

        int tieIdx = 0;
        for (int lane = 0; lane < 3; lane++)
        {
            float laneVal = lane - 1;
            for (int k = 0; k < 9; k++)
            {
                if (tieIdx >= subwaySleeperSegments.Count) break;
                Transform tie = subwaySleeperSegments[tieIdx++];
                float d = ((k + subwayScrollPhase) / 9f);
                ProjectSubwayPoint(laneVal, d, 0f, out Vector3 pos, out float sc);
                pos.z += 0.06f;
                tie.position = pos;
                tie.rotation = Quaternion.identity;
                tie.localScale = new Vector3(1.15f * sc, 0.08f * sc, 1f);
            }
        }
    }

    private void SpawnSubwayPatternAtDepth(float startDepth)
    {
        if (subwayTrackRoot == null) return;

        int waveIdx = subwayWaveCounter++;
        int pattern = waveIdx % 5;

        // Avoid spawning a train in the exact same lane twice in a row
        int PickTrainLane()
        {
            int l = Random.Range(-1, 2);
            if (l == subwayLastTrainLane) l = (l == 1) ? -1 : (l + 1);
            subwayLastTrainLane = l;
            return l;
        }

        if (pattern == 0)
        {
            // Single 3D hurdle in one lane + clear coin line in adjacent lane
            int hurdleLane = Random.Range(-1, 2);
            int coinLane = hurdleLane == 0 ? (Random.value > 0.5f ? -1 : 1) : 0;
            CreateSubwayEntity(SubwayEntityType.SmallObstacle, hurdleLane, startDepth + 0.04f, 0.07f, 0f);
            CreateSubwayEntity(SubwayEntityType.Coin, hurdleLane, startDepth + 0.04f, 0.05f, SubwaySmallObstacleHeight + 0.45f);
            CreateSubwayEntity(SubwayEntityType.Coin, coinLane, startDepth - 0.04f, 0.05f, 0.35f);
            CreateSubwayEntity(SubwayEntityType.Coin, coinLane, startDepth + 0.05f, 0.05f, 0.35f);
            CreateSubwayEntity(SubwayEntityType.Coin, coinLane, startDepth + 0.14f, 0.05f, 0.35f);
        }
        else if (pattern == 1)
        {
            // Longer 3D Train in one lane with a 3D Ramp in front of it + coins along the roof!
            int trainLane = PickTrainLane();
            CreateSubwayEntity(SubwayEntityType.Train, trainLane, startDepth, 0.42f, 0f);
            CreateSubwayEntity(SubwayEntityType.Ramp, trainLane, startDepth + 0.26f, 0.11f, 0f);

            CreateSubwayEntity(SubwayEntityType.Coin, trainLane, startDepth - 0.14f, 0.05f, SubwayTrainHeight + 0.38f);
            CreateSubwayEntity(SubwayEntityType.Coin, trainLane, startDepth - 0.04f, 0.05f, SubwayTrainHeight + 0.38f);
            CreateSubwayEntity(SubwayEntityType.Coin, trainLane, startDepth + 0.06f, 0.05f, SubwayTrainHeight + 0.38f);
            CreateSubwayEntity(SubwayEntityType.Coin, trainLane, startDepth + 0.16f, 0.05f, SubwayTrainHeight + 0.38f);

            int sideLane = trainLane == 0 ? 1 : 0;
            CreateSubwayEntity(SubwayEntityType.SmallObstacle, sideLane, startDepth + 0.08f, 0.07f, 0f);
        }
        else if (pattern == 2)
        {
            // Two small jumpable 3D hurdles + open lane with coins
            int freeLane = Random.Range(-1, 2);
            for (int lane = -1; lane <= 1; lane++)
            {
                if (lane == freeLane)
                {
                    CreateSubwayEntity(SubwayEntityType.Coin, lane, startDepth, 0.05f, 0.35f);
                    CreateSubwayEntity(SubwayEntityType.Coin, lane, startDepth + 0.09f, 0.05f, 0.35f);
                }
                else
                {
                    CreateSubwayEntity(SubwayEntityType.SmallObstacle, lane, startDepth + 0.04f, 0.07f, 0f);
                    CreateSubwayEntity(SubwayEntityType.Coin, lane, startDepth + 0.04f, 0.05f, SubwaySmallObstacleHeight + 0.45f);
                }
            }
        }
        else if (pattern == 3)
        {
            // Longer 3D Train with a small stepping-stone obstacle in front so player can jump onto the small obstacle and jump again onto the train!
            int trainLane = PickTrainLane();
            CreateSubwayEntity(SubwayEntityType.Train, trainLane, startDepth - 0.02f, 0.42f, 0f);
            CreateSubwayEntity(SubwayEntityType.SmallObstacle, trainLane, startDepth + 0.26f, 0.08f, 0f);

            CreateSubwayEntity(SubwayEntityType.Coin, trainLane, startDepth - 0.15f, 0.05f, SubwayTrainHeight + 0.38f);
            CreateSubwayEntity(SubwayEntityType.Coin, trainLane, startDepth - 0.05f, 0.05f, SubwayTrainHeight + 0.38f);
            CreateSubwayEntity(SubwayEntityType.Coin, trainLane, startDepth + 0.05f, 0.05f, SubwayTrainHeight + 0.38f);
            CreateSubwayEntity(SubwayEntityType.Coin, trainLane, startDepth + 0.15f, 0.05f, SubwayTrainHeight + 0.38f);

            int otherLane = trainLane == -1 ? 1 : -1;
            CreateSubwayEntity(SubwayEntityType.Coin, otherLane, startDepth + 0.02f, 0.05f, 0.35f);
            CreateSubwayEntity(SubwayEntityType.Coin, otherLane, startDepth + 0.12f, 0.05f, 0.35f);
        }
        else
        {
            // Two parallel longer trains (only after early warm-up waves) with a ramp on one and stepping-stone hurdle in middle
            if (waveIdx < 4)
            {
                CreateSubwayEntity(SubwayEntityType.Coin, -1, startDepth + 0.10f, 0.05f, 0.35f);
                CreateSubwayEntity(SubwayEntityType.Coin, 0, startDepth + 0.04f, 0.05f, 0.35f);
                CreateSubwayEntity(SubwayEntityType.Coin, 1, startDepth - 0.02f, 0.05f, 0.35f);
                CreateSubwayEntity(SubwayEntityType.SmallObstacle, 0, startDepth - 0.08f, 0.07f, 0f);
            }
            else
            {
                int rampTrainLane = Random.value > 0.5f ? -1 : 1;
                int secondTrainLane = -rampTrainLane;
                CreateSubwayEntity(SubwayEntityType.Train, rampTrainLane, startDepth, 0.42f, 0f);
                CreateSubwayEntity(SubwayEntityType.Ramp, rampTrainLane, startDepth + 0.26f, 0.11f, 0f);
                CreateSubwayEntity(SubwayEntityType.Train, secondTrainLane, startDepth, 0.42f, 0f);
                CreateSubwayEntity(SubwayEntityType.SmallObstacle, 0, startDepth + 0.24f, 0.08f, 0f);
                CreateSubwayEntity(SubwayEntityType.Coin, rampTrainLane, startDepth - 0.08f, 0.05f, SubwayTrainHeight + 0.38f);
                CreateSubwayEntity(SubwayEntityType.Coin, rampTrainLane, startDepth + 0.06f, 0.05f, SubwayTrainHeight + 0.38f);
                CreateSubwayEntity(SubwayEntityType.Coin, secondTrainLane, startDepth - 0.08f, 0.05f, SubwayTrainHeight + 0.38f);
                CreateSubwayEntity(SubwayEntityType.Coin, secondTrainLane, startDepth + 0.06f, 0.05f, SubwayTrainHeight + 0.38f);
            }
        }
    }

    private GameObject CreateSubwayBoxPart(string name, Transform parent, int layer, Vector3 localPos, Vector3 localScale, Color color, Vector3 localEuler = default)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.layer = layer;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localRotation = Quaternion.Euler(localEuler);
        part.transform.localScale = localScale;
        Collider col = part.GetComponent<Collider>();
        if (col != null) Destroy(col);
        Renderer r = part.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = CreateUnlitColorMaterial(color);
        return part;
    }

    private void CreateSubwayEntity(SubwayEntityType type, int laneIndex, float depth01, float depthLength, float elevationOffset)
    {
        if (subwayTrackRoot == null || mainBouncer == null) return;
        int layer = mainBouncer.gameObject.layer;

        GameObject root = new GameObject($"Subway_{type}");
        root.layer = layer;
        root.transform.SetParent(subwayTrackRoot.transform, false);

        float heightOrtho = 0.5f;

        if (type == SubwayEntityType.Coin)
        {
            heightOrtho = 0.55f;
            // Thick 3D golden coin with outer rim + brighter inner emblem
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.layer = layer;
            disc.transform.SetParent(root.transform, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(0.64f, 0.12f, 0.64f);
            Collider col = disc.GetComponent<Collider>();
            if (col != null) Destroy(col);
            Renderer r = disc.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = CreateUnlitColorMaterial(new Color(1.0f, 0.84f, 0.12f, 1f));

            CreateSubwayBoxPart("CoinCore", root.transform, layer, Vector3.zero, new Vector3(0.32f, 0.32f, 0.28f), new Color(1.0f, 0.96f, 0.55f, 1f), new Vector3(0f, 0f, 45f));
        }
        else if (type == SubwayEntityType.SmallObstacle)
        {
            heightOrtho = SubwaySmallObstacleHeight;
            // Multi-part 3D Roadblock Hurdle: Left/Right dark 3D posts + 3D Main Beam + Bright Top Cap + Side Depth Caps + Hazard Stripes
            CreateSubwayBoxPart("LeftPost", root.transform, layer, new Vector3(-0.52f, 0.42f, 0f), new Vector3(0.18f, 0.84f, 0.42f), new Color(0.22f, 0.25f, 0.32f, 1f));
            CreateSubwayBoxPart("RightPost", root.transform, layer, new Vector3(0.52f, 0.42f, 0f), new Vector3(0.18f, 0.84f, 0.42f), new Color(0.22f, 0.25f, 0.32f, 1f));
            CreateSubwayBoxPart("MainBeamBody", root.transform, layer, new Vector3(0f, 0.54f, 0f), new Vector3(1.30f, 0.44f, 0.46f), new Color(0.95f, 0.34f, 0.12f, 1f));
            CreateSubwayBoxPart("TopBrightBevel", root.transform, layer, new Vector3(0f, 0.80f, -0.01f), new Vector3(1.34f, 0.14f, 0.48f), new Color(1.0f, 0.88f, 0.22f, 1f));
            CreateSubwayBoxPart("FrontHazardStripeL", root.transform, layer, new Vector3(-0.30f, 0.54f, -0.24f), new Vector3(0.22f, 0.38f, 0.04f), new Color(1.0f, 0.92f, 0.28f, 1f));
            CreateSubwayBoxPart("FrontHazardStripeR", root.transform, layer, new Vector3(0.30f, 0.54f, -0.24f), new Vector3(0.22f, 0.38f, 0.04f), new Color(1.0f, 0.92f, 0.28f, 1f));
            CreateSubwayBoxPart("SideShadeWall", root.transform, layer, new Vector3(0.66f, 0.54f, 0f), new Vector3(0.05f, 0.44f, 0.46f), new Color(0.62f, 0.18f, 0.06f, 1f));
        }
        else if (type == SubwayEntityType.Ramp)
        {
            heightOrtho = SubwayTrainHeight;
            // 3D Sloped Boarding Ramp with distinct side walls, bright sloped deck, and chevron boost strips
            CreateSubwayBoxPart("RampBase", root.transform, layer, new Vector3(0f, 0.42f, 0.12f), new Vector3(1.26f, 0.82f, 0.95f), new Color(0.12f, 0.56f, 0.36f, 1f));
            CreateSubwayBoxPart("RampSlopeDeck", root.transform, layer, new Vector3(0f, 0.88f, 0f), new Vector3(1.24f, 0.22f, 1.38f), new Color(0.26f, 0.98f, 0.58f, 1f), new Vector3(-34f, 0f, 0f));
            CreateSubwayBoxPart("RampLeftRail", root.transform, layer, new Vector3(-0.62f, 0.96f, 0f), new Vector3(0.10f, 0.34f, 1.40f), new Color(0.16f, 0.76f, 0.45f, 1f), new Vector3(-34f, 0f, 0f));
            CreateSubwayBoxPart("RampRightRail", root.transform, layer, new Vector3(0.62f, 0.96f, 0f), new Vector3(0.10f, 0.34f, 1.40f), new Color(0.16f, 0.76f, 0.45f, 1f), new Vector3(-34f, 0f, 0f));
            CreateSubwayBoxPart("RampStripe1", root.transform, layer, new Vector3(0f, 0.65f, -0.32f), new Vector3(0.88f, 0.08f, 0.18f), new Color(0.88f, 1.0f, 0.42f, 1f), new Vector3(-34f, 0f, 0f));
            CreateSubwayBoxPart("RampStripe2", root.transform, layer, new Vector3(0f, 1.12f, 0.22f), new Vector3(0.88f, 0.08f, 0.18f), new Color(0.88f, 1.0f, 0.42f, 1f), new Vector3(-34f, 0f, 0f));
        }
        else if (type == SubwayEntityType.Train)
        {
            heightOrtho = SubwayTrainHeight;
            // Long, straight-aligned 3D Subway Train Car: Shaded Side Trim, Long Bright Roof Deck, Front Cab Face, Bumper, Headlights!
            CreateSubwayBoxPart("TrainMainHull", root.transform, layer, new Vector3(0f, 1.05f, 0.42f), new Vector3(1.36f, 1.88f, 2.85f), new Color(0.20f, 0.44f, 0.86f, 1f));
            CreateSubwayBoxPart("TrainSideWallL", root.transform, layer, new Vector3(-0.69f, 1.05f, 0.42f), new Vector3(0.06f, 1.86f, 2.84f), new Color(0.13f, 0.30f, 0.64f, 1f));
            CreateSubwayBoxPart("TrainSideWallR", root.transform, layer, new Vector3(0.69f, 1.05f, 0.42f), new Vector3(0.06f, 1.86f, 2.84f), new Color(0.13f, 0.30f, 0.64f, 1f));
            CreateSubwayBoxPart("TrainRoofDeck", root.transform, layer, new Vector3(0f, 2.02f, 0.40f), new Vector3(1.42f, 0.22f, 2.88f), new Color(0.58f, 0.84f, 1.0f, 1f));
            CreateSubwayBoxPart("TrainRoofStripe", root.transform, layer, new Vector3(0f, 2.14f, 0.40f), new Vector3(0.52f, 0.06f, 2.65f), new Color(0.82f, 0.94f, 1.0f, 1f));
            CreateSubwayBoxPart("TrainFrontCab", root.transform, layer, new Vector3(0f, 1.02f, -1.02f), new Vector3(1.34f, 1.82f, 0.14f), new Color(0.28f, 0.56f, 0.96f, 1f));
            CreateSubwayBoxPart("TrainWindshield", root.transform, layer, new Vector3(0f, 1.38f, -1.10f), new Vector3(1.06f, 0.54f, 0.06f), new Color(0.95f, 0.98f, 0.52f, 1f));
            CreateSubwayBoxPart("TrainBumper", root.transform, layer, new Vector3(0f, 0.26f, -1.12f), new Vector3(1.40f, 0.28f, 0.14f), new Color(0.85f, 0.22f, 0.22f, 1f));
            CreateSubwayBoxPart("HeadlightL", root.transform, layer, new Vector3(-0.42f, 0.60f, -1.11f), new Vector3(0.24f, 0.18f, 0.06f), new Color(1.0f, 0.95f, 0.35f, 1f));
            CreateSubwayBoxPart("HeadlightR", root.transform, layer, new Vector3(0.42f, 0.60f, -1.11f), new Vector3(0.24f, 0.18f, 0.06f), new Color(1.0f, 0.95f, 0.35f, 1f));
        }

        SubwayEntity ent = new SubwayEntity
        {
            type = type,
            rootObj = root,
            laneIndex = laneIndex,
            depth01 = depth01,
            depthLength = depthLength,
            elevationOffset = elevationOffset,
            heightOrtho = heightOrtho,
            collectedOrHit = false,
            spinAngle = Random.Range(0f, 360f)
        };
        subwayEntities.Add(ent);
        UpdateSubwayEntityVisualTransform(ent, 0f);
    }

    private void UpdateSubwayEntityVisualTransform(SubwayEntity ent, float dt)
    {
        if (ent == null || ent.rootObj == null) return;
        ProjectSubwayPoint(ent.laneIndex, ent.depth01, ent.elevationOffset, out Vector3 worldPos, out float sc);
        worldPos.z -= ent.depth01 * 0.06f;

        // Rapidly scale down right as the entity passes the player so it leaves the screen cleanly without lingering!
        float passFade = 1f;
        float passThreshold = SubwayPlayerDepth01 + ent.depthLength * 0.15f;
        if (ent.depth01 > passThreshold)
        {
            passFade = Mathf.Clamp01(1f - (ent.depth01 - passThreshold) / 0.06f);
        }

        ent.rootObj.transform.position = worldPos;
        ent.rootObj.transform.localScale = Vector3.one * (sc * passFade);

        if (ent.type == SubwayEntityType.Coin)
        {
            ent.spinAngle += dt * 210f;
            ent.rootObj.transform.localRotation = Quaternion.Euler(0f, ent.spinAngle, 0f);
        }
        else
        {
            // Keep obstacles, ramps, and trains aligned straight forward down the lane (zero yaw/roll twist)
            // with a clean forward pitch so both the front face and 3D top roof are clearly visible in the Ortho camera!
            ent.rootObj.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
        }
    }

    // =========================================================================
    // MINIGAME 8: HILL CLIMB RACING (RIGHT SCREEN GAS / LEFT SCREEN BRAKE, 360 FLIPS, CIRCULAR DVD WHEELS)
    // =========================================================================

    private class HillPickupCoin
    {
        public GameObject obj;
        public float worldTrackX;
        public float heightOffset;
        public float spinAngle;
    }

    private GameObject hillRootObj;
    private readonly List<Transform> hillLineSegments = new List<Transform>();
    private readonly List<Transform> hillStrutSegments = new List<Transform>();
    private readonly List<HillPickupCoin> hillCoins = new List<HillPickupCoin>();
    private DeformableLogoPiece hillCarPiece;
    private DeformableLogoPiece hillFrontWheelPiece;
    private DeformableLogoPiece hillRearWheelPiece;
    private Transform hillBrakePedalBar;
    private Transform hillGasPedalBar;
    private Renderer hillBrakePedalRend;
    private Renderer hillGasPedalRend;
    private float hillCarTrackX = 0f;
    private float hillCarWorldY = 0f;
    private float hillCarVelX = 0f;
    private float hillCarVelY = 0f;
    private float hillCarAngleDeg = 0f;
    private float hillCarAngularVelDeg = 0f;
    private float hillAirFlipAccumDeg = 0f;
    private bool hillCarGrounded = true;
    private float hillNextCoinSpawnX = 6f;
    private float hillWheelSpinAngle = 0f;

    private void EnterHillClimbMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        StopAnyActiveConvergeAnimation();
        mainBouncer.DespawnAllChildLogosForMinigame();
        mainBouncer.SetMainLogoRenderersVisible(false);
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();

        DestroyHillClimbVisuals();

        hillCarTrackX = 0f;
        hillCarVelX = 0f;
        hillCarVelY = 0f;
        hillCarAngleDeg = 0f;
        hillCarAngularVelDeg = 0f;
        hillAirFlipAccumDeg = 0f;
        hillCarGrounded = true;
        hillNextCoinSpawnX = 4.5f;
        hillWheelSpinAngle = 0f;
        hillCarWorldY = SampleHillTerrainY(hillCarTrackX) + 0.55f;

        BuildHillClimbVisuals();
    }

    private void ExitHillClimbMode()
    {
        DestroyHillClimbVisuals();

        if (mainBouncer != null)
        {
            mainBouncer.SetMainLogoRenderersVisible(true);
            mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
        }
    }

    private void BuildHillClimbVisuals()
    {
        if (mainBouncer == null) return;
        int layer = mainBouncer.gameObject.layer;

        hillRootObj = new GameObject("HillClimbRoot");
        hillRootObj.layer = layer;
        if (mainBouncer.transform.parent != null)
        {
            hillRootObj.transform.SetParent(mainBouncer.transform.parent, false);
        }

        Material lineMat = CreateUnlitColorMaterial(new Color(0.35f, 1.0f, 0.52f, 0.95f));
        Material strutMat = CreateUnlitColorMaterial(new Color(0.18f, 0.68f, 0.38f, 0.28f), true);

        const int segCount = 48;
        for (int i = 0; i < segCount; i++)
        {
            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            seg.name = $"HillLineSeg_{i}";
            seg.layer = layer;
            seg.transform.SetParent(hillRootObj.transform, false);
            Collider col = seg.GetComponent<Collider>();
            if (col != null) Destroy(col);
            Renderer r = seg.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = lineMat;
            hillLineSegments.Add(seg.transform);

            if (i % 2 == 0)
            {
                GameObject strut = GameObject.CreatePrimitive(PrimitiveType.Quad);
                strut.name = $"HillStrut_{i}";
                strut.layer = layer;
                strut.transform.SetParent(hillRootObj.transform, false);
                Collider c2 = strut.GetComponent<Collider>();
                if (c2 != null) Destroy(c2);
                Renderer r2 = strut.GetComponent<Renderer>();
                if (r2 != null) r2.sharedMaterial = strutMat;
                hillStrutSegments.Add(strut.transform);
            }
        }

        // Create deformable DVD logo car body
        hillCarPiece = CreateDeformableLogoClone(
            "HillClimbCar_DVD",
            0.54f,
            new Color(1.0f, 0.26f, 0.22f, 1f),
            new Color(1.0f, 0.88f, 0.22f, 1f));

        // Create two circularly-distorted original DVD logo wheels!
        hillFrontWheelPiece = CreateDeformableLogoClone(
            "HillFrontWheel_DVD",
            0.34f,
            new Color(0.88f, 0.94f, 1.0f, 1f),
            new Color(0.24f, 0.82f, 0.98f, 1f));
        if (hillFrontWheelPiece != null)
        {
            hillFrontWheelPiece.isCircularWheel = true;
            hillFrontWheelPiece.UpdateVisuals(0f);
        }

        hillRearWheelPiece = CreateDeformableLogoClone(
            "HillRearWheel_DVD",
            0.34f,
            new Color(0.88f, 0.94f, 1.0f, 1f),
            new Color(0.24f, 0.82f, 0.98f, 1f));
        if (hillRearWheelPiece != null)
        {
            hillRearWheelPiece.isCircularWheel = true;
            hillRearWheelPiece.UpdateVisuals(0f);
        }

        // Visual Left (Brake) and Right (Gas) Pedal Indicators at bottom corners of the TV screen
        GameObject brakeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        brakeObj.name = "HillLeftBrakePedal";
        brakeObj.layer = layer;
        brakeObj.transform.SetParent(hillRootObj.transform, false);
        Collider bc = brakeObj.GetComponent<Collider>();
        if (bc != null) Destroy(bc);
        hillBrakePedalRend = brakeObj.GetComponent<Renderer>();
        if (hillBrakePedalRend != null) hillBrakePedalRend.sharedMaterial = CreateUnlitColorMaterial(new Color(0.95f, 0.28f, 0.28f, 0.45f), true);
        hillBrakePedalBar = brakeObj.transform;

        GameObject gasObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gasObj.name = "HillRightGasPedal";
        gasObj.layer = layer;
        gasObj.transform.SetParent(hillRootObj.transform, false);
        Collider gc = gasObj.GetComponent<Collider>();
        if (gc != null) Destroy(gc);
        hillGasPedalRend = gasObj.GetComponent<Renderer>();
        if (hillGasPedalRend != null) hillGasPedalRend.sharedMaterial = CreateUnlitColorMaterial(new Color(0.25f, 0.96f, 0.48f, 0.45f), true);
        hillGasPedalBar = gasObj.transform;
    }

    private void DestroyHillClimbVisuals()
    {
        hillLineSegments.Clear();
        hillStrutSegments.Clear();
        for (int i = 0; i < hillCoins.Count; i++)
        {
            if (hillCoins[i] != null && hillCoins[i].obj != null)
            {
                Destroy(hillCoins[i].obj);
            }
        }
        hillCoins.Clear();

        if (hillCarPiece != null)
        {
            hillCarPiece.DestroyPiece();
            hillCarPiece = null;
        }
        if (hillFrontWheelPiece != null)
        {
            hillFrontWheelPiece.DestroyPiece();
            hillFrontWheelPiece = null;
        }
        if (hillRearWheelPiece != null)
        {
            hillRearWheelPiece.DestroyPiece();
            hillRearWheelPiece = null;
        }

        if (hillRootObj != null)
        {
            Destroy(hillRootObj);
            hillRootObj = null;
        }
        hillBrakePedalBar = null;
        hillGasPedalBar = null;
        hillBrakePedalRend = null;
        hillGasPedalRend = null;
    }

    /// <summary>
    /// Procedural curved hill height at track coordinate x.
    /// Smoothly blends rolling hills with steeper crests as the player drives further.
    /// </summary>
    private float SampleHillTerrainY(float trackX)
    {
        GetOrthoBounds(out _, out _, out float camMinY, out float camMaxY);
        float baseLineY = Mathf.Lerp(camMinY, camMaxY, 0.30f);

        float difficultyAmp = 1f + Mathf.Clamp01(Mathf.Abs(trackX) / 80f) * 0.72f;
        float h =
            Mathf.Sin(trackX * 0.44f) * 1.22f +
            Mathf.Sin(trackX * 0.19f + 1.3f) * 0.88f +
            Mathf.Cos(trackX * 0.84f - 0.6f) * 0.38f;

        return baseLineY + h * difficultyAmp;
    }

    private float SampleHillTerrainSlope(float trackX)
    {
        float difficultyAmp = 1f + Mathf.Clamp01(Mathf.Abs(trackX) / 80f) * 0.72f;
        float dh =
            Mathf.Cos(trackX * 0.44f) * (1.22f * 0.44f) +
            Mathf.Cos(trackX * 0.19f + 1.3f) * (0.88f * 0.19f) -
            Mathf.Sin(trackX * 0.84f - 0.6f) * (0.38f * 0.84f);
        return dh * difficultyAmp;
    }

    private void UpdateHillClimb(float dt)
    {
        if (mainBouncer == null || orthoCamera == null || hillCarPiece == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float baseZ = mainBouncer.transform.position.z;
        float screenMidX = (camMinX + camMaxX) * 0.5f;

        // Controls: Holding click on Right side of screen = Gas (Speed Up + Pitch Up / Backflip torque)
        //           Holding click on Left side of screen (or Right-Click) = Brake / Reverse (Brake + Pitch Down / Frontflip torque)
        bool leftMouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool rightMouseHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;

        bool gasHeld = leftMouseHeld && targetCursorOrthoPos.x >= screenMidX;
        bool brakeHeld = (leftMouseHeld && targetCursorOrthoPos.x < screenMidX) || rightMouseHeld;

        // Update visual pedal bars at bottom-left and bottom-right of screen
        if (hillBrakePedalBar != null)
        {
            float pScaleY = brakeHeld ? 0.18f : 0.30f;
            hillBrakePedalBar.position = new Vector3(Mathf.Lerp(camMinX, screenMidX, 0.38f), camMinY + 0.34f, baseZ - 0.06f);
            hillBrakePedalBar.localScale = new Vector3((camMaxX - camMinX) * 0.28f, pScaleY, 0.1f);
            if (hillBrakePedalRend != null && hillBrakePedalRend.sharedMaterial != null)
            {
                Color c = brakeHeld ? new Color(1.0f, 0.25f, 0.25f, 0.88f) : new Color(0.85f, 0.22f, 0.22f, 0.32f);
                if (hillBrakePedalRend.sharedMaterial.HasProperty("_BaseColor")) hillBrakePedalRend.sharedMaterial.SetColor("_BaseColor", c);
                if (hillBrakePedalRend.sharedMaterial.HasProperty("_Color")) hillBrakePedalRend.sharedMaterial.SetColor("_Color", c);
            }
        }
        if (hillGasPedalBar != null)
        {
            float pScaleY = gasHeld ? 0.18f : 0.30f;
            hillGasPedalBar.position = new Vector3(Mathf.Lerp(screenMidX, camMaxX, 0.62f), camMinY + 0.34f, baseZ - 0.06f);
            hillGasPedalBar.localScale = new Vector3((camMaxX - camMinX) * 0.28f, pScaleY, 0.1f);
            if (hillGasPedalRend != null && hillGasPedalRend.sharedMaterial != null)
            {
                Color c = gasHeld ? new Color(0.25f, 1.0f, 0.48f, 0.88f) : new Color(0.20f, 0.78f, 0.38f, 0.32f);
                if (hillGasPedalRend.sharedMaterial.HasProperty("_BaseColor")) hillGasPedalRend.sharedMaterial.SetColor("_BaseColor", c);
                if (hillGasPedalRend.sharedMaterial.HasProperty("_Color")) hillGasPedalRend.sharedMaterial.SetColor("_Color", c);
            }
        }

        float wheelBase = 1.12f;
        float halfWheelBase = wheelBase * 0.5f;
        const float rideClearance = 0.50f;

        float rearSampleX = hillCarTrackX - halfWheelBase;
        float frontSampleX = hillCarTrackX + halfWheelBase;
        float hRear = SampleHillTerrainY(rearSampleX);
        float hFront = SampleHillTerrainY(frontSampleX);

        float terrainAngleRad = Mathf.Atan2(hFront - hRear, wheelBase);
        float terrainAngleDeg = terrainAngleRad * Mathf.Rad2Deg;
        float groundCenterY = (hRear + hFront) * 0.5f + rideClearance;

        if (hillCarGrounded)
        {
            // Drive along curved hill
            float driveAccel = 0f;
            if (gasHeld) driveAccel += 14.5f;
            if (brakeHeld) driveAccel -= 12.5f;

            // Gravity component along slope
            float slopeGravity = -16.5f * Mathf.Sin(terrainAngleRad);
            hillCarVelX += (driveAccel * Mathf.Cos(terrainAngleRad) + slopeGravity) * dt;
            hillCarVelX *= Mathf.Pow(0.66f, dt); // Smooth rolling resistance
            hillCarVelX = Mathf.Clamp(hillCarVelX, -10.5f, 14.5f);

            // Realistic wheelie & flip pitch torque! Holding gas on steep hills can wheelie & flip the car!
            float angleDiffFromSlope = Mathf.DeltaAngle(terrainAngleDeg, hillCarAngleDeg);
            float throttleTorque = 0f;
            if (gasHeld) throttleTorque += 205f + Mathf.Max(0f, Mathf.Sin(terrainAngleRad)) * 85f;
            if (brakeHeld) throttleTorque -= 205f + Mathf.Max(0f, -Mathf.Sin(terrainAngleRad)) * 85f;

            // Suspension spring smoothly aligns wheels to slope within normal wheelie range (< 58 deg)
            float suspensionRestore = 0f;
            if (Mathf.Abs(angleDiffFromSlope) < 58f && !gasHeld && !brakeHeld)
            {
                suspensionRestore = -angleDiffFromSlope * 11.0f;
            }
            else if (Mathf.Abs(angleDiffFromSlope) < 44f)
            {
                suspensionRestore = -angleDiffFromSlope * 4.2f;
            }

            hillCarAngularVelDeg += (throttleTorque + suspensionRestore) * dt;
            hillCarAngularVelDeg *= Mathf.Pow(0.15f, dt);
            hillCarAngleDeg += hillCarAngularVelDeg * dt;

            // Check if the car flipped upside-down onto its roof while on the ground!
            if (Mathf.Abs(Mathf.DeltaAngle(hillCarAngleDeg, terrainAngleDeg)) > 108f)
            {
                TriggerHillRoofCrash(camMinX, camMaxX, baseZ, terrainAngleDeg);
            }

            float slopeBefore = SampleHillTerrainSlope(hillCarTrackX);
            hillCarTrackX += hillCarVelX * dt;
            float slopeAfter = SampleHillTerrainSlope(hillCarTrackX);

            float nextHRear = SampleHillTerrainY(hillCarTrackX - halfWheelBase);
            float nextHFront = SampleHillTerrainY(hillCarTrackX + halfWheelBase);
            float nextGroundY = (nextHRear + nextHFront) * 0.5f + rideClearance;
            float analyticalVelY = hillCarVelX * slopeAfter;

            // Smooth crest launch detection using analytical curvature (never glitches on frame dt spikes!)
            float downwardCurvatureDrop = (hillCarVelX * slopeBefore) - analyticalVelY;
            if ((Mathf.Abs(hillCarVelX) > 6.4f && downwardCurvatureDrop > 1.85f && slopeBefore > 0.15f) || Mathf.Abs(angleDiffFromSlope) > 68f)
            {
                hillCarGrounded = false;
                hillAirFlipAccumDeg = 0f;
                hillCarVelY = hillCarVelX * slopeBefore + 0.65f;
                hillCarWorldY = nextGroundY + 0.04f;
            }
            else
            {
                hillCarWorldY = nextGroundY;
                hillCarVelY = analyticalVelY;
            }
        }
        else
        {
            // Airborne physics: full 360-degree flips!
            if (gasHeld) hillCarAngularVelDeg += 290f * dt;
            if (brakeHeld) hillCarAngularVelDeg -= 290f * dt;
            hillCarAngularVelDeg = Mathf.Clamp(hillCarAngularVelDeg, -420f, 420f);
            float stepAng = hillCarAngularVelDeg * dt;
            hillCarAngleDeg += stepAng;
            hillAirFlipAccumDeg += stepAng;

            // Award flip bonus if player completes a full 360-degree flip in the air!
            if (Mathf.Abs(hillAirFlipAccumDeg) >= 325f)
            {
                hillAirFlipAccumDeg = 0f;
                Vector3 flipPos = new Vector3(Mathf.Lerp(camMinX, camMaxX, 0.32f), hillCarWorldY + 0.5f, baseZ);
                mainBouncer.AwardMinigameCornerReward(
                    2,
                    flipPos,
                    new Color(0.25f, 1.0f, 0.88f, 1f),
                    new Color(1.0f, 0.88f, 0.22f, 1f));
                hillCarPiece.TriggerPop(0.26f, 0.28f);
                PlaySound(suikaMergeSound, 1.22f);
            }

            hillCarVelY -= 18.5f * dt;
            hillCarTrackX += hillCarVelX * dt;
            hillCarWorldY += hillCarVelY * dt;

            float nextHRear = SampleHillTerrainY(hillCarTrackX - halfWheelBase);
            float nextHFront = SampleHillTerrainY(hillCarTrackX + halfWheelBase);
            groundCenterY = (nextHRear + nextHFront) * 0.5f + rideClearance;
            terrainAngleRad = Mathf.Atan2(nextHFront - nextHRear, wheelBase);
            terrainAngleDeg = terrainAngleRad * Mathf.Rad2Deg;

            if (hillCarWorldY <= groundCenterY)
            {
                hillCarWorldY = groundCenterY;
                hillCarGrounded = true;
                hillAirFlipAccumDeg = 0f;

                // Check if car landed upside-down (roof crash!)
                float angleDiff = Mathf.Abs(Mathf.DeltaAngle(hillCarAngleDeg, terrainAngleDeg));
                if (angleDiff > 100f)
                {
                    TriggerHillRoofCrash(camMinX, camMaxX, baseZ, terrainAngleDeg);
                }
                else
                {
                    float impact = Mathf.Clamp(Mathf.Abs(hillCarVelY) * 0.025f, 0.10f, 0.30f);
                    hillCarPiece.TriggerSquash(Vector2.up, impact, 0.35f);
                    if (hillFrontWheelPiece != null) hillFrontWheelPiece.TriggerSquash(Vector2.up, impact * 0.8f, 0.30f);
                    if (hillRearWheelPiece != null) hillRearWheelPiece.TriggerSquash(Vector2.up, impact * 0.8f, 0.30f);
                    hillCarAngleDeg = Mathf.LerpAngle(hillCarAngleDeg, terrainAngleDeg, 0.72f);
                    hillCarAngularVelDeg *= 0.20f;
                    PlaySound(towerLandSound, 1.05f);
                }
                hillCarVelY = hillCarVelX * SampleHillTerrainSlope(hillCarTrackX);
            }
        }

        // Camera X follows car with car positioned at 32% from left of screen
        float carScreenX = Mathf.Lerp(camMinX, camMaxX, 0.32f);
        float trackToWorldOffsetX = carScreenX - hillCarTrackX;

        // Spawn coins ahead on the hill
        float visibleMaxTrackX = hillCarTrackX + (camMaxX - carScreenX) + 3f;
        while (hillNextCoinSpawnX < visibleMaxTrackX)
        {
            SpawnHillCoinCluster(hillNextCoinSpawnX);
            hillNextCoinSpawnX += Random.Range(4.8f, 8.2f);
        }

        // Update curved hill line segments & vertical struts anchored in world track coordinates so struts scroll smoothly with the terrain!
        int segCount = hillLineSegments.Count;
        float spanW = (camMaxX - camMinX) + 2.0f;
        float stepX = spanW / segCount;
        float leftWorldTrackX = (camMinX - 1.0f) - trackToWorldOffsetX;
        float snappedStartTrackX = Mathf.Floor(leftWorldTrackX / stepX) * stepX;

        for (int i = 0; i < segCount; i++)
        {
            float tx0 = snappedStartTrackX + i * stepX;
            float tx1 = tx0 + stepX;
            float sx0 = tx0 + trackToWorldOffsetX;
            float sx1 = tx1 + trackToWorldOffsetX;
            float y0 = SampleHillTerrainY(tx0);
            float y1 = SampleHillTerrainY(tx1);

            Vector3 p0 = new Vector3(sx0, y0, baseZ + 0.04f);
            Vector3 p1 = new Vector3(sx1, y1, baseZ + 0.04f);
            Vector3 mid = (p0 + p1) * 0.5f;
            Vector3 diff = p1 - p0;
            float len = diff.magnitude;
            float ang = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg - 90f;

            Transform seg = hillLineSegments[i];
            seg.position = mid;
            seg.rotation = Quaternion.Euler(0f, 0f, ang);
            seg.localScale = new Vector3(0.13f, len + 0.04f, 1f);
        }

        float strutSpacing = stepX * 2f;
        float snappedStrutStartTrackX = Mathf.Floor(leftWorldTrackX / strutSpacing) * strutSpacing;
        for (int s = 0; s < hillStrutSegments.Count; s++)
        {
            Transform strut = hillStrutSegments[s];
            float stx = snappedStrutStartTrackX + s * strutSpacing;
            float ssx = stx + trackToWorldOffsetX;
            float sy = SampleHillTerrainY(stx);
            float bottomY = camMinY - 0.5f;
            float h = Mathf.Max(0.1f, sy - bottomY);
            strut.position = new Vector3(ssx, bottomY + h * 0.5f, baseZ + 0.08f);
            strut.rotation = Quaternion.identity;
            strut.localScale = new Vector3(0.05f, h, 1f);
        }

        // Update & collect hill coins
        Vector3 carWorldCenter = new Vector3(carScreenX, hillCarWorldY, baseZ - 0.08f);
        for (int i = hillCoins.Count - 1; i >= 0; i--)
        {
            HillPickupCoin coin = hillCoins[i];
            if (coin == null || coin.obj == null)
            {
                hillCoins.RemoveAt(i);
                continue;
            }

            if (coin.worldTrackX < hillCarTrackX - 10f)
            {
                Destroy(coin.obj);
                hillCoins.RemoveAt(i);
                continue;
            }

            float coinScreenX = coin.worldTrackX + trackToWorldOffsetX;
            float coinY = SampleHillTerrainY(coin.worldTrackX) + coin.heightOffset;
            Vector3 coinPos = new Vector3(coinScreenX, coinY, baseZ - 0.05f);
            coin.obj.transform.position = coinPos;
            coin.spinAngle += dt * 200f;
            coin.obj.transform.localRotation = Quaternion.Euler(0f, coin.spinAngle, 0f) * Quaternion.Euler(90f, 0f, 0f);

            if (Vector2.Distance(new Vector2(carWorldCenter.x, carWorldCenter.y), new Vector2(coinPos.x, coinPos.y)) <= 1.05f)
            {
                mainBouncer.AwardMinigameCornerReward(
                    1,
                    coinPos,
                    new Color(1f, 0.88f, 0.18f, 1f),
                    new Color(0.35f, 1f, 0.52f, 1f));
                hillCarPiece.TriggerPop(0.16f, 0.22f);
                PlaySound(enemyPassSound, 1.2f);
                Destroy(coin.obj);
                hillCoins.RemoveAt(i);
            }
        }

        // Update Car Visuals & Circularly-Distorted DVD Logo Wheels (spinning cleanly in place around their geometric center!)
        Quaternion carRot = Quaternion.Euler(0f, 0f, hillCarAngleDeg);
        hillCarPiece.SetCenterPosition(carWorldCenter);
        hillCarPiece.rootObj.transform.localRotation = mainBouncer.transform.localRotation * carRot;
        hillCarPiece.UpdateVisuals(dt);

        hillWheelSpinAngle -= hillCarVelX * dt * 250f;
        Quaternion wheelSpinRot = mainBouncer.transform.localRotation * Quaternion.Euler(0f, 0f, hillWheelSpinAngle);
        Vector3 rightDir = carRot * Vector3.right;
        Vector3 upDir = carRot * Vector3.up;

        if (hillFrontWheelPiece != null && hillFrontWheelPiece.rootObj != null)
        {
            Vector3 fwPos = carWorldCenter + rightDir * halfWheelBase - upDir * 0.28f + new Vector3(0f, 0f, -0.05f);
            hillFrontWheelPiece.SetCenterPosition(fwPos);
            hillFrontWheelPiece.rootObj.transform.localRotation = wheelSpinRot;
            hillFrontWheelPiece.UpdateVisuals(dt);
        }
        if (hillRearWheelPiece != null && hillRearWheelPiece.rootObj != null)
        {
            Vector3 rwPos = carWorldCenter - rightDir * halfWheelBase - upDir * 0.28f + new Vector3(0f, 0f, -0.05f);
            hillRearWheelPiece.SetCenterPosition(rwPos);
            hillRearWheelPiece.rootObj.transform.localRotation = wheelSpinRot;
            hillRearWheelPiece.UpdateVisuals(dt);
        }
    }

    private void TriggerHillRoofCrash(float camMinX, float camMaxX, float baseZ, float terrainAngleDeg)
    {
        Vector3 crashWorldPos = new Vector3(Mathf.Lerp(camMinX, camMaxX, 0.32f), hillCarWorldY, baseZ);
        mainBouncer.ApplyMinigameCornerPenalty(3, crashWorldPos);
        mainBouncer.TriggerHitParticlesWithColors(
            crashWorldPos,
            new Color(1f, 0.25f, 0.2f, 1f),
            new Color(1f, 0.85f, 0.2f, 1f));
        TriggerScreenRedFlash(screenRedFlashDuration, screenRedFlashMaxAlpha);
        hillCarPiece.TriggerSquash(Vector2.up, 0.38f, 0.48f);
        if (hillFrontWheelPiece != null) hillFrontWheelPiece.TriggerPop(0.25f, 0.30f);
        if (hillRearWheelPiece != null) hillRearWheelPiece.TriggerPop(0.25f, 0.30f);
        PlaySound(damageHitSound, 0.78f);
        hillCarVelX *= 0.20f;
        hillCarAngleDeg = terrainAngleDeg;
        hillCarAngularVelDeg = 0f;
    }

    private void SpawnHillCoinCluster(float startTrackX)
    {
        if (hillRootObj == null || mainBouncer == null) return;
        int layer = mainBouncer.gameObject.layer;
        Material coinMat = CreateUnlitColorMaterial(new Color(1.0f, 0.86f, 0.16f, 1f));

        int count = Random.Range(3, 6);
        for (int i = 0; i < count; i++)
        {
            float tx = startTrackX + i * 0.95f;
            GameObject cObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cObj.name = "HillMoneyCoin";
            cObj.layer = layer;
            cObj.transform.SetParent(hillRootObj.transform, false);
            cObj.transform.localScale = new Vector3(0.55f, 0.07f, 0.55f);
            Collider col = cObj.GetComponent<Collider>();
            if (col != null) Destroy(col);
            Renderer r = cObj.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = coinMat;

            hillCoins.Add(new HillPickupCoin
            {
                obj = cObj,
                worldTrackX = tx,
                heightOffset = 0.68f,
                spinAngle = i * 35f
            });
        }
    }

    // =========================================================================
    // MINIGAME 9: COLOR MATCHER / SLINGSHOT SHOOTER (SUIKA JIGGLE & POP)
    // =========================================================================

    private class ColorShooterNode
    {
        public DeformableLogoPiece visual;
        public int colorIndex;
        public int mergedCount = 1;
        public Vector2 pos;
        public Vector2 vel;
        public float baseRadiusX;
        public float baseRadiusY;
        public float radiusX;
        public float radiusY;
        public bool isShotProjectile;
        public float shotImpactEnergyTimer;
        public float age;
    }

    private const int ColorShooterPaletteCount = 8;
    private static readonly Color[,] ColorShooterPalette = new Color[,]
    {
        { new Color(1.00f, 0.22f, 0.28f, 1f), new Color(1.00f, 0.72f, 0.20f, 1f) }, // 0: Crimson Red / Gold
        { new Color(0.18f, 0.95f, 0.42f, 1f), new Color(0.82f, 1.00f, 0.25f, 1f) }, // 1: Emerald Green / Lime
        { new Color(0.20f, 0.62f, 1.00f, 1f), new Color(0.28f, 0.98f, 0.96f, 1f) }, // 2: Cobalt Blue / Cyan
        { new Color(0.95f, 0.28f, 0.92f, 1f), new Color(0.56f, 0.25f, 1.00f, 1f) }, // 3: Neon Magenta / Violet
        { new Color(1.00f, 0.88f, 0.16f, 1f), new Color(1.00f, 0.46f, 0.10f, 1f) }, // 4: Solar Yellow / Amber
        { new Color(0.15f, 0.96f, 0.88f, 1f), new Color(0.18f, 0.48f, 0.92f, 1f) }, // 5: Electric Mint / Deep Azure
        { new Color(1.00f, 0.48f, 0.16f, 1f), new Color(0.95f, 0.18f, 0.52f, 1f) }, // 6: Tangerine Orange / Hot Pink
        { new Color(0.94f, 0.96f, 1.00f, 1f), new Color(0.45f, 0.58f, 0.78f, 1f) }  // 7: Pure White / Frosted Silver
    };

    private GameObject colorShooterRoot;
    private readonly List<ColorShooterNode> floatingPileNodes = new List<ColorShooterNode>();
    private readonly List<Transform> shooterAimDots = new List<Transform>();
    private DeformableLogoPiece shooterLoadedPiece;
    private int shooterLoadedColorIndex = 0;
    private bool isDraggingSlingshot = false;
    private Vector2 slingshotAnchorPos;
    private float shooterReplenishTimer = 0f;
    private const float ColorShooterLogoScale = 0.48f;
    private const int TargetFloatingPileCount = 16;

    /// <summary>
    /// Graph-coloring / anti-neighbor spawn algorithm:
    /// Examines all existing nodes within neighborRadius of spawnPos and picks a color from the 8-color palette
    /// that does NOT match any nearby neighbor (with only a rare ~3.5% chance on replenish of allowing a neighbor color).
    /// </summary>
    private int PickSmartNonMatchingPileColor(Vector2 spawnPos, bool allowRareMatch)
    {
        if (allowRareMatch && floatingPileNodes.Count > 0 && Random.value < 0.035f)
        {
            return floatingPileNodes[Random.Range(0, floatingPileNodes.Count)].colorIndex;
        }

        const float neighborCheckRadius = 2.45f;
        int[] nearbyCounts = new int[ColorShooterPaletteCount];
        int[] totalCounts = new int[ColorShooterPaletteCount];

        for (int i = 0; i < floatingPileNodes.Count; i++)
        {
            ColorShooterNode n = floatingPileNodes[i];
            if (n == null) continue;
            int c = Mathf.Clamp(n.colorIndex, 0, ColorShooterPaletteCount - 1);
            totalCounts[c]++;
            if (Vector2.Distance(spawnPos, n.pos) <= neighborCheckRadius)
            {
                nearbyCounts[c]++;
            }
        }

        // Collect all colors that have ZERO nearby neighbors
        List<int> safeColors = new List<int>(ColorShooterPaletteCount);
        for (int c = 0; c < ColorShooterPaletteCount; c++)
        {
            if (nearbyCounts[c] == 0)
            {
                safeColors.Add(c);
            }
        }

        if (safeColors.Count > 0)
        {
            // Prefer colors that are least represented in the overall pile so all 8 colors stay balanced
            safeColors.Sort((a, b) => totalCounts[a].CompareTo(totalCounts[b]));
            int poolSize = Mathf.Min(3, safeColors.Count);
            return safeColors[Random.Range(0, poolSize)];
        }

        // Fallback: pick the color with the minimum nearby count
        int bestColor = 0;
        int bestNear = int.MaxValue;
        for (int c = 0; c < ColorShooterPaletteCount; c++)
        {
            if (nearbyCounts[c] < bestNear)
            {
                bestNear = nearbyCounts[c];
                bestColor = c;
            }
        }
        return bestColor;
    }

    private void EnterColorShooterMode()
    {
        if (mainBouncer == null || orthoCamera == null) return;

        StopAnyActiveConvergeAnimation();
        mainBouncer.DespawnAllChildLogosForMinigame();
        mainBouncer.SetMainLogoRenderersVisible(false);
        savedLogoDirectionBeforeRunner = mainBouncer.GetDirectionVector();

        ClearColorShooterState();

        int layer = mainBouncer.gameObject.layer;
        colorShooterRoot = new GameObject("ColorShooterRoot");
        colorShooterRoot.layer = layer;
        if (mainBouncer.transform.parent != null)
        {
            colorShooterRoot.transform.SetParent(mainBouncer.transform.parent, false);
        }

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        slingshotAnchorPos = new Vector2((camMinX + camMaxX) * 0.5f, camMinY + (camMaxY - camMinY) * 0.18f);

        // Create aim line dots
        Material dotMat = CreateUnlitColorMaterial(new Color(1f, 1f, 1f, 0.85f), true);
        for (int i = 0; i < 14; i++)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = $"SlingshotAimDot_{i}";
            dot.layer = layer;
            dot.transform.SetParent(colorShooterRoot.transform, false);
            Collider col = dot.GetComponent<Collider>();
            if (col != null) Destroy(col);
            Renderer r = dot.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = dotMat;
            dot.SetActive(false);
            shooterAimDots.Add(dot.transform);
        }

        // Spawn initial floating pile of DVD logos in upper/middle screen using non-adjacent color placement
        Vector2 pileCenter = new Vector2((camMinX + camMaxX) * 0.5f, Mathf.Lerp(camMinY, camMaxY, 0.64f));
        for (int i = 0; i < TargetFloatingPileCount; i++)
        {
            float angle = i * (Mathf.PI * 2f / TargetFloatingPileCount) + Random.Range(-0.15f, 0.15f);
            float dist = (i < 5) ? Random.Range(0.55f, 1.25f) : Random.Range(1.45f, 2.85f);
            Vector2 spawnPos = pileCenter + new Vector2(Mathf.Cos(angle) * dist * 1.35f, Mathf.Sin(angle) * dist * 0.75f);
            int smartColor = PickSmartNonMatchingPileColor(spawnPos, false);
            SpawnFloatingPileLogo(spawnPos, smartColor, false);
        }

        SpawnNextSlingshotLogo();
    }

    private void ExitColorShooterMode()
    {
        ClearColorShooterState();

        if (mainBouncer != null)
        {
            mainBouncer.SetMainLogoRenderersVisible(true);
            mainBouncer.EnsureValidBounceDirectionAfterMinigame(savedLogoDirectionBeforeRunner);
        }
    }

    private void ClearColorShooterState()
    {
        for (int i = 0; i < floatingPileNodes.Count; i++)
        {
            if (floatingPileNodes[i] != null && floatingPileNodes[i].visual != null)
            {
                floatingPileNodes[i].visual.DestroyPiece();
            }
        }
        floatingPileNodes.Clear();
        shooterAimDots.Clear();

        if (shooterLoadedPiece != null)
        {
            shooterLoadedPiece.DestroyPiece();
            shooterLoadedPiece = null;
        }

        if (colorShooterRoot != null)
        {
            Destroy(colorShooterRoot);
            colorShooterRoot = null;
        }
        isDraggingSlingshot = false;
    }

    private void SpawnNextSlingshotLogo()
    {
        if (shooterLoadedPiece != null)
        {
            shooterLoadedPiece.DestroyPiece();
            shooterLoadedPiece = null;
        }

        // Pick a color present in the floating pile so the player always has valid matches
        if (floatingPileNodes.Count > 0)
        {
            shooterLoadedColorIndex = floatingPileNodes[Random.Range(0, floatingPileNodes.Count)].colorIndex;
        }
        else
        {
            shooterLoadedColorIndex = Random.Range(0, ColorShooterPaletteCount);
        }

        Color pCol = ColorShooterPalette[shooterLoadedColorIndex, 0];
        Color sCol = ColorShooterPalette[shooterLoadedColorIndex, 1];
        shooterLoadedPiece = CreateDeformableLogoClone("SlingshotLoaded_DVD", ColorShooterLogoScale, pCol, sCol);
        if (shooterLoadedPiece != null)
        {
            float baseZ = mainBouncer != null ? mainBouncer.transform.position.z - 0.1f : 0f;
            shooterLoadedPiece.SetCenterPosition(new Vector3(slingshotAnchorPos.x, slingshotAnchorPos.y, baseZ));
            shooterLoadedPiece.TriggerPop(0.28f, 0.26f);
        }
    }

    private ColorShooterNode SpawnFloatingPileLogo(Vector2 pos, int colorIdx, bool isProjectile)
    {
        colorIdx = Mathf.Clamp(colorIdx, 0, ColorShooterPaletteCount - 1);
        Color pCol = ColorShooterPalette[colorIdx, 0];
        Color sCol = ColorShooterPalette[colorIdx, 1];
        DeformableLogoPiece piece = CreateDeformableLogoClone($"ColorPile_DVD_{colorIdx}", ColorShooterLogoScale, pCol, sCol);
        if (piece == null) return null;

        float baseZ = mainBouncer != null ? mainBouncer.transform.position.z - 0.06f : 0f;
        piece.SetCenterPosition(new Vector3(pos.x, pos.y, baseZ));
        piece.TriggerPop(0.22f, 0.25f);

        float baseRx = Mathf.Max(0.45f, piece.EffectiveHalfWidth * 0.92f);
        float baseRy = Mathf.Max(0.28f, piece.EffectiveHalfHeight * 0.92f);
        ColorShooterNode node = new ColorShooterNode
        {
            visual = piece,
            colorIndex = colorIdx,
            mergedCount = 1,
            pos = pos,
            vel = Vector2.zero,
            baseRadiusX = baseRx,
            baseRadiusY = baseRy,
            radiusX = baseRx,
            radiusY = baseRy,
            isShotProjectile = isProjectile,
            shotImpactEnergyTimer = isProjectile ? 0.55f : 0f,
            age = 0f
        };
        floatingPileNodes.Add(node);
        return node;
    }

    private void ApplyMergedScaleToColorNode(ColorShooterNode node)
    {
        if (node == null || node.visual == null) return;
        int count = Mathf.Max(1, node.mergedCount);
        float growthMult = Mathf.Min(2.35f, 1f + 0.24f * Mathf.Sqrt(count - 1));
        node.visual.overallScaleFactor = ColorShooterLogoScale * growthMult;
        node.radiusX = node.baseRadiusX * growthMult;
        node.radiusY = node.baseRadiusY * growthMult;
    }

    private int GetTotalFloatingPileOriginalUnitCount()
    {
        int totalUnits = 0;
        for (int i = 0; i < floatingPileNodes.Count; i++)
        {
            ColorShooterNode n = floatingPileNodes[i];
            if (n == null || n.visual == null) continue;
            totalUnits += Mathf.Max(1, n.mergedCount);
        }
        return totalUnits;
    }

    private void UpdateColorShooter(float dt)
    {
        if (mainBouncer == null || orthoCamera == null) return;

        GetOrthoBounds(out float camMinX, out float camMaxX, out float camMinY, out float camMaxY);
        float baseZ = mainBouncer.transform.position.z - 0.06f;
        slingshotAnchorPos = new Vector2((camMinX + camMaxX) * 0.5f, camMinY + (camMaxY - camMinY) * 0.19f);

        // 1. Slingshot Hold-Drag Down Aiming & Shooting
        bool leftHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (shooterLoadedPiece == null)
        {
            SpawnNextSlingshotLogo();
        }

        if (leftHeld && shooterLoadedPiece != null)
        {
            if (!isDraggingSlingshot)
            {
                isDraggingSlingshot = true;
            }

            // Compute pull vector from anchor to cursor (dragging downward pulls the slingshot back to aim upward!)
            Vector2 pullVec = slingshotAnchorPos - targetCursorOrthoPos;
            // Ensure pulling down shoots upward into the floating pile
            if (pullVec.y < 0.15f)
            {
                pullVec.y = Mathf.Max(0.25f, Mathf.Abs(pullVec.y));
            }

            float maxPullDist = 2.6f;
            float pullDist = Mathf.Clamp(pullVec.magnitude, 0.15f, maxPullDist);
            float power01 = Mathf.Clamp01(pullDist / maxPullDist);
            Vector2 aimDir = pullVec.normalized;

            // Visually displace the loaded DVD logo slightly along the pullback vector & squash it with tension
            Vector2 loadedVisualPos = slingshotAnchorPos - aimDir * (power01 * 0.65f);
            shooterLoadedPiece.SetCenterPosition(new Vector3(loadedVisualPos.x, loadedVisualPos.y, baseZ - 0.05f));
            shooterLoadedPiece.shapeWidthScale = 1f + power01 * 0.16f;
            shooterLoadedPiece.shapeHeightScale = 1f - power01 * 0.14f;
            shooterLoadedPiece.UpdateVisuals(dt);

            // Update Aim Line Dots (length and spacing reflect launch strength!)
            float previewLength = Mathf.Lerp(1.2f, 6.2f, power01);
            for (int i = 0; i < shooterAimDots.Count; i++)
            {
                Transform dot = shooterAimDots[i];
                if (dot == null) continue;
                dot.gameObject.SetActive(true);
                float t = (i + 1) / (float)shooterAimDots.Count;
                Vector2 dotPos = loadedVisualPos + aimDir * (previewLength * t);
                dot.position = new Vector3(dotPos.x, dotPos.y, baseZ - 0.08f);
                float dotScale = Mathf.Lerp(0.18f, 0.07f, t) * (0.7f + 0.5f * power01);
                dot.localScale = Vector3.one * dotScale;
            }
        }
        else if (isDraggingSlingshot && !leftHeld && shooterLoadedPiece != null)
        {
            // Release Slingshot -> Shoot the DVD Logo into the floating pile!
            isDraggingSlingshot = false;
            for (int i = 0; i < shooterAimDots.Count; i++)
            {
                if (shooterAimDots[i] != null) shooterAimDots[i].gameObject.SetActive(false);
            }

            Vector2 pullVec = slingshotAnchorPos - targetCursorOrthoPos;
            if (pullVec.y < 0.15f) pullVec.y = Mathf.Max(0.35f, Mathf.Abs(pullVec.y));
            float maxPullDist = 2.6f;
            float power01 = Mathf.Clamp01(pullVec.magnitude / maxPullDist);
            Vector2 aimDir = pullVec.sqrMagnitude > 0.001f ? pullVec.normalized : Vector2.up;
            float launchSpeed = Mathf.Lerp(7.5f, 19.5f, power01);

            shooterLoadedPiece.shapeWidthScale = 1f;
            shooterLoadedPiece.shapeHeightScale = 1f;
            shooterLoadedPiece.TriggerSquash(aimDir, 0.28f, 0.38f);

            float shotBaseRx = Mathf.Max(0.45f, shooterLoadedPiece.EffectiveHalfWidth * 0.92f);
            float shotBaseRy = Mathf.Max(0.28f, shooterLoadedPiece.EffectiveHalfHeight * 0.92f);
            ColorShooterNode shotNode = new ColorShooterNode
            {
                visual = shooterLoadedPiece,
                colorIndex = shooterLoadedColorIndex,
                mergedCount = 1,
                pos = slingshotAnchorPos,
                vel = aimDir * launchSpeed,
                baseRadiusX = shotBaseRx,
                baseRadiusY = shotBaseRy,
                radiusX = shotBaseRx,
                radiusY = shotBaseRy,
                isShotProjectile = true,
                shotImpactEnergyTimer = 0.65f,
                age = 0f
            };
            floatingPileNodes.Add(shotNode);
            shooterLoadedPiece = null;
            PlaySound(jumpSound, 1.18f);
            SpawnNextSlingshotLogo();
        }
        else if (shooterLoadedPiece != null)
        {
            shooterLoadedPiece.shapeWidthScale = 1f;
            shooterLoadedPiece.shapeHeightScale = 1f;
            shooterLoadedPiece.SetCenterPosition(new Vector3(slingshotAnchorPos.x, slingshotAnchorPos.y, baseZ - 0.05f));
            shooterLoadedPiece.UpdateVisuals(dt);
        }

        // 2. Replenish floating pile based on TOTAL original units (so combining doesn't add new ones,
        //    and destroying a combined logo of N original logos replenishes all N back to TargetFloatingPileCount!)
        shooterReplenishTimer -= dt;
        int currentTotalUnits = GetTotalFloatingPileOriginalUnitCount();
        if (currentTotalUnits < TargetFloatingPileCount && shooterReplenishTimer <= 0f)
        {
            int deficit = TargetFloatingPileCount - currentTotalUnits;
            shooterReplenishTimer = deficit > 2 ? 0.16f : 0.45f;
            float spawnX = Random.Range(camMinX + 1.6f, camMaxX - 1.6f);
            float spawnY = camMaxY - 0.85f;
            Vector2 repPos = new Vector2(spawnX, spawnY);
            int smartColor = PickSmartNonMatchingPileColor(repPos, true);
            ColorShooterNode added = SpawnFloatingPileLogo(repPos, smartColor, false);
            if (added != null)
            {
                added.vel = new Vector2(Random.Range(-0.9f, 0.9f), Random.Range(-1.2f, -0.5f));
            }
        }

        // 3. Physics & Suika Jiggle / Same-Color Pop Solver for the Floating Pile
        Vector2 pileAttractor = new Vector2((camMinX + camMaxX) * 0.5f, Mathf.Lerp(camMinY, camMaxY, 0.65f));
        float minPileY = camMinY + (camMaxY - camMinY) * 0.34f;

        for (int i = 0; i < floatingPileNodes.Count; i++)
        {
            ColorShooterNode n = floatingPileNodes[i];
            if (n == null || n.visual == null) continue;
            n.age += dt;
            if (n.shotImpactEnergyTimer > 0f)
            {
                n.shotImpactEnergyTimer = Mathf.Max(0f, n.shotImpactEnergyTimer - dt);
                if (n.shotImpactEnergyTimer <= 0f) n.isShotProjectile = false;
            }

            // Gentle floating attraction toward the upper-middle cluster center
            Vector2 toCenter = pileAttractor - n.pos;
            float attractStrength = n.isShotProjectile && n.age < 0.45f ? 0.5f : 2.6f;
            n.vel += toCenter * (attractStrength * dt);

            // Damping so the pile settles after impacts
            float damping = (n.isShotProjectile && n.age < 0.35f) ? 0.92f : 0.14f;
            n.vel *= Mathf.Pow(damping, dt);

            n.pos += n.vel * dt;

            // Wall bounces (Left, Right, Top, and lower floating boundary)
            if (n.pos.x - n.radiusX < camMinX + 0.25f)
            {
                n.pos.x = camMinX + 0.25f + n.radiusX;
                n.vel.x = Mathf.Abs(n.vel.x) * 0.78f;
                n.visual.TriggerSquash(Vector2.right, 0.16f, 0.32f);
            }
            else if (n.pos.x + n.radiusX > camMaxX - 0.25f)
            {
                n.pos.x = camMaxX - 0.25f - n.radiusX;
                n.vel.x = -Mathf.Abs(n.vel.x) * 0.78f;
                n.visual.TriggerSquash(Vector2.right, 0.16f, 0.32f);
            }

            if (n.pos.y + n.radiusY > camMaxY - 0.25f)
            {
                n.pos.y = camMaxY - 0.25f - n.radiusY;
                n.vel.y = -Mathf.Abs(n.vel.y) * 0.78f;
                n.visual.TriggerSquash(Vector2.up, 0.16f, 0.32f);
            }
            else if (n.pos.y - n.radiusY < minPileY && (!n.isShotProjectile || n.age > 0.35f))
            {
                n.pos.y = minPileY + n.radiusY;
                n.vel.y = Mathf.Abs(n.vel.y) * 0.65f;
                n.isShotProjectile = false;
            }
        }

        // Pairwise collision:
        // - Same-color + shotDriven -> Pop/Destroy each other!
        // - Same-color + NOT shotDriven -> Combine into a larger node (grow in size, do NOT pop, do NOT add new one)!
        // - Different-color -> Push & Suika Jiggle!
        HashSet<int> indicesToDestroy = null;
        HashSet<int> indicesAbsorbedByMerge = null;

        for (int iter = 0; iter < 3; iter++)
        {
            int count = floatingPileNodes.Count;
            for (int i = 0; i < count; i++)
            {
                if (indicesAbsorbedByMerge != null && indicesAbsorbedByMerge.Contains(i)) continue;
                ColorShooterNode a = floatingPileNodes[i];
                if (a == null || a.visual == null) continue;

                for (int j = i + 1; j < count; j++)
                {
                    if (indicesAbsorbedByMerge != null && indicesAbsorbedByMerge.Contains(j)) continue;
                    ColorShooterNode b = floatingPileNodes[j];
                    if (b == null || b.visual == null) continue;

                    float sumRx = (a.radiusX + b.radiusX) * 0.94f;
                    float sumRy = (a.radiusY + b.radiusY) * 0.94f;
                    Vector2 delta = b.pos - a.pos;
                    float nx = delta.x / Mathf.Max(0.001f, sumRx);
                    float ny = delta.y / Mathf.Max(0.001f, sumRy);
                    float normDistSq = nx * nx + ny * ny;

                    if (normDistSq < 1f)
                    {
                        bool shotDriven = a.isShotProjectile || b.isShotProjectile || a.shotImpactEnergyTimer > 0f || b.shotImpactEnergyTimer > 0f;

                        if (a.colorIndex == b.colorIndex)
                        {
                            if (shotDriven)
                            {
                                // Shot hits same-color -> Pop/Destroy!
                                if (indicesToDestroy == null) indicesToDestroy = new HashSet<int>();
                                indicesToDestroy.Add(i);
                                indicesToDestroy.Add(j);
                            }
                            else
                            {
                                // Touching same-color in the pile without being shot -> Combine & grow!
                                int totalMerged = a.mergedCount + b.mergedCount;
                                a.pos = (a.pos * a.mergedCount + b.pos * b.mergedCount) / Mathf.Max(1, totalMerged);
                                a.vel = (a.vel * a.mergedCount + b.vel * b.mergedCount) / Mathf.Max(1, totalMerged);
                                a.mergedCount = totalMerged;
                                ApplyMergedScaleToColorNode(a);
                                Vector2 mergeNormal = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.up;
                                a.visual.TriggerSquash(mergeNormal, 0.22f, 0.36f);
                                a.visual.TriggerPop(0.28f, 0.28f);
                                PlaySound(towerLandSound, 1.18f);

                                if (indicesAbsorbedByMerge == null) indicesAbsorbedByMerge = new HashSet<int>();
                                indicesAbsorbedByMerge.Add(j);
                                if (indicesToDestroy != null && indicesToDestroy.Remove(j))
                                {
                                    indicesToDestroy.Add(i);
                                }
                            }
                        }
                        else
                        {
                            // Different color: Suika-style push apart + squash jiggle!
                            float normDist = Mathf.Sqrt(Mathf.Max(0.0001f, normDistSq));
                            Vector2 normal = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.up;
                            float avgRadius = (sumRx + sumRy) * 0.5f;
                            float penetration = (1f - normDist) * avgRadius;

                            a.pos -= normal * (penetration * 0.52f);
                            b.pos += normal * (penetration * 0.52f);

                            Vector2 relVel = b.vel - a.vel;
                            float velAlongNormal = Vector2.Dot(relVel, normal);
                            if (velAlongNormal < 0f)
                            {
                                float restitution = 0.72f;
                                float impulseMag = -(1f + restitution) * velAlongNormal * 0.5f;
                                Vector2 impulse = normal * impulseMag;
                                a.vel -= impulse;
                                b.vel += impulse;

                                // If a shot projectile slammed into a different-color node hard, transfer a brief knock timer so skill bank-shots work!
                                if (shotDriven && Mathf.Abs(velAlongNormal) > 3.8f)
                                {
                                    a.shotImpactEnergyTimer = Mathf.Max(a.shotImpactEnergyTimer, 0.22f);
                                    b.shotImpactEnergyTimer = Mathf.Max(b.shotImpactEnergyTimer, 0.22f);
                                }

                                if (Mathf.Abs(velAlongNormal) > 1.3f)
                                {
                                    float jiggle = Mathf.Clamp(Mathf.Abs(velAlongNormal) * 0.032f, 0.08f, 0.32f);
                                    a.visual.TriggerSquash(normal, jiggle, 0.42f);
                                    b.visual.TriggerSquash(-normal, jiggle, 0.42f);
                                    if (Mathf.Abs(velAlongNormal) > 3.5f)
                                    {
                                        PlaySound(towerLandSound, Random.Range(0.95f, 1.15f));
                                    }
                                }
                            }
                            a.isShotProjectile = false;
                            b.isShotProjectile = false;
                        }
                    }
                }
            }
        }

        // Remove nodes that were absorbed into a larger combined same-color node
        if (indicesAbsorbedByMerge != null && indicesAbsorbedByMerge.Count > 0)
        {
            // If we also have indicesToDestroy, we'll clean up absorbed nodes right after or remap indices
            if (indicesToDestroy == null || indicesToDestroy.Count == 0)
            {
                for (int i = floatingPileNodes.Count - 1; i >= 0; i--)
                {
                    if (!indicesAbsorbedByMerge.Contains(i)) continue;
                    ColorShooterNode absorbed = floatingPileNodes[i];
                    if (absorbed != null && absorbed.visual != null)
                    {
                        absorbed.visual.DestroyPiece();
                    }
                    floatingPileNodes.RemoveAt(i);
                }
            }
        }

        // Expand same-color shot pop chain reaction to any directly touching neighbors of the same color!
        if (indicesToDestroy != null && indicesToDestroy.Count > 0)
        {
            bool addedMore = true;
            while (addedMore)
            {
                addedMore = false;
                for (int i = 0; i < floatingPileNodes.Count; i++)
                {
                    if (!indicesToDestroy.Contains(i)) continue;
                    if (indicesAbsorbedByMerge != null && indicesAbsorbedByMerge.Contains(i)) continue;
                    ColorShooterNode a = floatingPileNodes[i];
                    if (a == null) continue;

                    for (int j = 0; j < floatingPileNodes.Count; j++)
                    {
                        if (indicesToDestroy.Contains(j)) continue;
                        if (indicesAbsorbedByMerge != null && indicesAbsorbedByMerge.Contains(j)) continue;
                        ColorShooterNode b = floatingPileNodes[j];
                        if (b == null || b.colorIndex != a.colorIndex) continue;

                        float sumRx = (a.radiusX + b.radiusX) * 1.12f;
                        float sumRy = (a.radiusY + b.radiusY) * 1.12f;
                        Vector2 d = b.pos - a.pos;
                        if ((d.x * d.x) / (sumRx * sumRx) + (d.y * d.y) / (sumRy * sumRy) <= 1.05f)
                        {
                            indicesToDestroy.Add(j);
                            addedMore = true;
                        }
                    }
                }
            }

            PlaySound(suikaMergeSound, 1.15f);

            for (int i = floatingPileNodes.Count - 1; i >= 0; i--)
            {
                if (indicesAbsorbedByMerge != null && indicesAbsorbedByMerge.Contains(i))
                {
                    ColorShooterNode absorbed = floatingPileNodes[i];
                    if (absorbed != null && absorbed.visual != null) absorbed.visual.DestroyPiece();
                    floatingPileNodes.RemoveAt(i);
                    continue;
                }
                if (!indicesToDestroy.Contains(i)) continue;
                ColorShooterNode popped = floatingPileNodes[i];
                if (popped != null && popped.visual != null)
                {
                    Vector3 worldPopPos = new Vector3(popped.pos.x, popped.pos.y, baseZ);
                    int rewardCount = Mathf.Max(1, popped.mergedCount);
                    mainBouncer.AwardMinigameCornerReward(
                        rewardCount,
                        worldPopPos,
                        popped.visual.primaryColor,
                        popped.visual.secondaryColor);

                    // Shockwave jiggle on nearby different-color logos!
                    for (int k = 0; k < floatingPileNodes.Count; k++)
                    {
                        if (indicesToDestroy.Contains(k)) continue;
                        if (indicesAbsorbedByMerge != null && indicesAbsorbedByMerge.Contains(k)) continue;
                        ColorShooterNode other = floatingPileNodes[k];
                        if (other == null || other.visual == null) continue;
                        Vector2 diff = other.pos - popped.pos;
                        float dist = diff.magnitude;
                        if (dist > 0.01f && dist < 2.6f)
                        {
                            Vector2 pushDir = diff / dist;
                            float pushFactor = 1f - (dist / 2.6f);
                            other.vel += pushDir * (4.5f * pushFactor);
                            other.visual.TriggerSquash(pushDir, 0.22f * pushFactor, 0.42f);
                        }
                    }

                    popped.visual.DestroyPiece();
                }
                floatingPileNodes.RemoveAt(i);
            }
        }

        // Update visual positions & vertex squash animations
        for (int i = 0; i < floatingPileNodes.Count; i++)
        {
            ColorShooterNode n = floatingPileNodes[i];
            if (n == null || n.visual == null) continue;
            n.visual.SetCenterPosition(new Vector3(n.pos.x, n.pos.y, baseZ));
            n.visual.UpdateVisuals(dt);
        }
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

    private Material CreateUnlitColorMaterial(Color c, bool transparent = false)
    {
        if (sharedUnlitTemplate == null && mainBouncer != null)
        {
            Renderer r = mainBouncer.GetComponentInChildren<Renderer>(true);
            if (r != null && r.sharedMaterial != null)
            {
                sharedUnlitTemplate = r.sharedMaterial;
            }
        }

        Shader s = null;
        if (transparent)
        {
            s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
        }
        if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null && sharedUnlitTemplate != null) s = sharedUnlitTemplate.shader;
        if (s == null) s = Shader.Find("Unlit/Color");
        if (s == null) s = Shader.Find("Standard");

        Material m = new Material(s);
        if (transparent || c.a < 0.99f)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3050;
        }
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
