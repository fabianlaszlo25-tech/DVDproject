using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// System 1, System 2, System 4 & System 5:
/// Manages picking up the Remote or Towel, entering/exiting First-Person TV Focus Mode,
/// projecting the Remote cursor as a 3D sphere INSIDE the TV RenderTexture (captured by renderCamera),
/// hovering the actual 3D Towel model OUTSIDE on the TV glass in 3D world space when cleaning,
/// handling all 5 Remote tiers (Base, Mitosis, Grab & Shake, Pinball Bumpers, Ultimate),
/// and wiping screen dust with the Towel.
/// </summary>
public class TVFocusController : MonoBehaviour
{
    public static TVFocusController Instance { get; private set; }

    public enum HeldTool
    {
        None = 0,
        Remote = 1,
        Towel = 2
    }

    [Header("Core References")]
    public GridCrawlerController playerController;
    public SmoothCameraFollow smoothCamera;
    public DVDLogoBouncer bouncer;
    public UpgradeManager upgradeManager;
    public TVScreenCleaner screenCleaner;
    public Renderer tvScreenRenderer;
    public Collider tvScreenCollider;

    [Header("Desk & Room Tool Interactables")]
    public InteractableObject remoteInteractable;
    public InteractableObject towelInteractable;

    [Header("Camera Focus Framing (~85-90% Viewport Fill)")]
    [Tooltip("Distance in front of the TV screen center where the camera positions itself during Focus Mode.")]
    public float focusDistanceFromScreen = 1.45f;
    [Tooltip("Target fraction of the viewport filled by the TV screen (0.85 to 0.90).")]
    [Range(0.75f, 0.95f)]
    public float targetViewportFill = 0.88f;

    [Header("In-Screen 3D Remote Cursor & Hitbox Settings (Captured by TV Camera)")]
    [Tooltip("Optional custom 3D object/prefab for the in-screen Remote cursor sphere. Auto-created inside DVDSetup/Camera if empty.")]
    public Transform screenReticleTransform;
    [Tooltip("Diameter of the 3D cursor sphere inside the TV ortho camera space when holding the Remote.")]
    public float remoteReticleOrthoSize = 0.42f;
    public Color remoteReticleColor = new Color(1f, 0.92f, 0.28f, 1f);
    [Tooltip("Extra clickable padding (in ortho units) around the DVD logo collider for forgiving direct hits.")]
    public float logoHitboxPadding = 0.25f;

    [Header("3D Hovering Towel Settings (Outside Screen on TV Glass)")]
    [Tooltip("Optional custom 3D towel model/prefab to hover on the TV glass. If empty, clones the desk towel model automatically.")]
    public GameObject towelHoverModelOverride;
    [Tooltip("Distance (in meters) in front of the TV screen glass where the 3D towel hovers.")]
    public float towelHoverSurfaceOffset = 0.032f;
    [Tooltip("How much closer (in meters) the towel presses into the glass while holding Left-Click to wipe.")]
    public float towelPressDepthOffset = -0.014f;
    [Tooltip("Scale of the hovering 3D towel model while cleaning the TV.")]
    public Vector3 towelHoverScale = new Vector3(0.85f, 0.85f, 0.85f);
    [Tooltip("Euler rotation offset so the flat broad side of the towel faces flush against the TV screen.")]
    public Vector3 towelHoverRotationOffset = new Vector3(0f, 0f, 0f);
    [Tooltip("How much the 3D towel tilts/sways dynamically as you scrub across the screen.")]
    public float towelScrubTiltAmount = 18f;
    [Tooltip("Smoothing speed for the hovering 3D towel position and scrubbing tilt.")]
    public float towelHoverFollowSpeed = 28f;

    [Header("Pinball Bumpers Settings (Tier 4 & Ultimate Remote)")]
    [Tooltip("Optional custom 3D prefab for spawned bumpers inside the TV screen. Defaults to a glowing 3D sphere if empty.")]
    public GameObject customBumperPrefab;
    [Tooltip("Radius of spawned pinball bumpers inside the TV ortho camera space.")]
    public float bumperOrthoRadius = 0.42f;
    [Tooltip("Maximum number of active bumpers on screen at once (FIFO despawn).")]
    public int maxActiveBumpers = 5;
    public Color bumperColor = new Color(0.2f, 0.95f, 1f, 1f);

    [Header("Ultimate Remote Options")]
    [Tooltip("By default false (Ultimate Remote combines Base + Mitosis + Bumpers). Enable if you also want Ultimate Remote to grab & shake on hold.")]
    public bool ultimateIncludesGrabShake = false;

    [Header("UI Hover Prompts")]
    public string pickUpRemoteText = "Pick Up Remote";
    public string putDownRemoteText = "[Click] Put Down Remote";
    public string pickUpTowelText = "Pick Up Towel";
    public string putDownTowelText = "[Click] Put Down Towel";

    [Header("Audio")]
    public AudioSource focusAudioSource;
    public AudioClip pickUpSound;
    public AudioClip putDownSound;
    public AudioClip bumperSpawnSound;
    public AudioClip bumperPingSound;
    public AudioClip grabStartSound;
    public AudioClip grabShakeTickSound;
    public AudioClip grabReleaseSound;

    public HeldTool CurrentTool { get; private set; } = HeldTool.None;
    public bool IsInFocusMode => CurrentTool != HeldTool.None;
    public bool IsHoldingRemote => CurrentTool == HeldTool.Remote;

    private Renderer screenReticleRenderer;
    private Transform towelHoverTransform;
    private Vector3 lastTowelWorldPos;
    private Vector2 smoothedTowelScrubVel;
    private bool isCursorOnScreen = false;
    private Vector2 prevWipeUV = new Vector2(-1f, -1f);
    private DVDLogoBouncer grabbedLogo = null;
    private Vector2 grabCenterOffset = Vector2.zero;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        AutoResolveReferences();

        // Migrate legacy flat towelHoverRotationOffset (-90, 0, 0) to upright (0, 0, 0)
        if (Mathf.Approximately(towelHoverRotationOffset.x, -90f) &&
            Mathf.Approximately(towelHoverRotationOffset.y, 0f) &&
            Mathf.Approximately(towelHoverRotationOffset.z, 0f))
        {
            towelHoverRotationOffset = Vector3.zero;
        }

        EnsureScreenReticle();
        EnsureHoverTowelModel();
        EnsureMinigameController();
        SetScreenReticleVisible(false);
        SetHoverTowelVisible(false);

        if (tvScreenCollider != null)
        {
            tvScreenCollider.enabled = false;
        }
    }

    private void EnsureMinigameController()
    {
        if (TVMinigameController.Instance == null)
        {
            TVMinigameController existing = GetComponent<TVMinigameController>();
            if (existing == null) existing = FindObjectOfType<TVMinigameController>(true);
            if (existing == null)
            {
                existing = gameObject.AddComponent<TVMinigameController>();
            }
            existing.mainBouncer = bouncer;
            if (bouncer != null) existing.orthoCamera = bouncer.RenderCamera;
        }
    }

    private void AutoResolveReferences()
    {
        if (playerController == null) playerController = FindObjectOfType<GridCrawlerController>();
        if (smoothCamera == null) smoothCamera = FindObjectOfType<SmoothCameraFollow>();
        if (bouncer == null) bouncer = FindObjectOfType<DVDLogoBouncer>();
        if (upgradeManager == null) upgradeManager = FindObjectOfType<UpgradeManager>();
        if (screenCleaner == null) screenCleaner = FindObjectOfType<TVScreenCleaner>();

        if (tvScreenRenderer == null)
        {
            GameObject screenObj = GameObject.Find("DVDSetup/Televisor_80/screen");
            if (screenObj != null) tvScreenRenderer = screenObj.GetComponent<Renderer>();
        }

        if (tvScreenCollider == null && tvScreenRenderer != null)
        {
            tvScreenCollider = tvScreenRenderer.GetComponent<Collider>();
            if (tvScreenCollider == null)
            {
                MeshCollider mc = tvScreenRenderer.gameObject.AddComponent<MeshCollider>();
                tvScreenCollider = mc;
            }
        }
    }

    /// <summary>
    /// Creates the 3D cursor sphere directly inside the TV renderCamera's hierarchy
    /// so it streams into the SCREENTRANSFER RenderTexture alongside the DVD logo when holding the Remote.
    /// </summary>
    private void EnsureScreenReticle()
    {
        Camera orthoCam = bouncer != null ? bouncer.RenderCamera : null;

        if (screenReticleTransform != null)
        {
            if (orthoCam != null && screenReticleTransform.parent != orthoCam.transform)
            {
                screenReticleTransform.SetParent(orthoCam.transform, false);
            }
            screenReticleRenderer = screenReticleTransform.GetComponentInChildren<Renderer>();
            return;
        }

        if (orthoCam != null)
        {
            Transform existing = orthoCam.transform.Find("TVScreen3DReticle_InScreen");
            if (existing != null)
            {
                screenReticleTransform = existing;
                screenReticleRenderer = existing.GetComponent<Renderer>();
                return;
            }
        }

        // Clean up any legacy outside-screen reticle if present
        Transform oldExternal = transform.Find("TVScreen3DReticle");
        if (oldExternal != null)
        {
            Destroy(oldExternal.gameObject);
        }

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "TVScreen3DReticle_InScreen";
        Collider col = sphere.GetComponent<Collider>();
        if (col != null) Destroy(col);

        if (orthoCam != null)
        {
            sphere.transform.SetParent(orthoCam.transform, false);
        }
        else
        {
            sphere.transform.SetParent(transform, false);
        }

        if (bouncer != null)
        {
            sphere.layer = bouncer.gameObject.layer;
        }

        sphere.transform.localScale = Vector3.one * remoteReticleOrthoSize;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

        Material mat = new Material(shader);
        mat.color = remoteReticleColor;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", remoteReticleColor);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", remoteReticleColor * 1.4f);
        }

        screenReticleRenderer = sphere.GetComponent<Renderer>();
        if (screenReticleRenderer != null)
        {
            screenReticleRenderer.material = mat;
            screenReticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            screenReticleRenderer.receiveShadows = false;
        }

        screenReticleTransform = sphere.transform;
    }

    /// <summary>
    /// Creates the 3D hovering towel model in 3D room world space (NOT inside the TV RenderTexture)
    /// so when the player holds the towel, the actual 3D towel hovers over the TV glass as they wipe.
    /// </summary>
    private void EnsureHoverTowelModel()
    {
        if (towelHoverTransform != null) return;

        Transform existing = transform.Find("HoveringTowelCursor_3D");
        if (existing != null)
        {
            towelHoverTransform = existing;
            return;
        }

        GameObject rootHolder = new GameObject("HoveringTowelCursor_3D");
        rootHolder.transform.SetParent(transform, false);
        rootHolder.transform.localScale = towelHoverScale;

        GameObject sourceVisual = towelHoverModelOverride;
        if (sourceVisual == null && towelInteractable != null)
        {
            Transform tvChild = towelInteractable.transform.Find("TowelVisual");
            sourceVisual = tvChild != null ? tvChild.gameObject : towelInteractable.gameObject;
        }

        GameObject visualCopy;
        if (sourceVisual != null)
        {
            visualCopy = Instantiate(sourceVisual, rootHolder.transform);
            visualCopy.name = "TowelMesh";
        }
        else
        {
            visualCopy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualCopy.name = "TowelMesh";
            visualCopy.transform.SetParent(rootHolder.transform, false);
            visualCopy.transform.localScale = new Vector3(0.24f, 0.04f, 0.18f);
        }

        visualCopy.transform.localPosition = Vector3.zero;
        visualCopy.transform.localRotation = Quaternion.identity;

        // Strip any colliders or InteractableObject scripts from the hovering towel copy so it never blocks screen raycasts
        Collider[] cols = rootHolder.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null) Destroy(cols[i]);
        }
        InteractableObject[] ios = rootHolder.GetComponentsInChildren<InteractableObject>(true);
        for (int i = 0; i < ios.Length; i++)
        {
            if (ios[i] != null) Destroy(ios[i]);
        }

        // Ensure static batching flags are cleared and center the mesh pivot on the holder origin
        Transform[] allTr = rootHolder.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allTr.Length; i++)
        {
            if (allTr[i] != null)
            {
                allTr[i].gameObject.isStatic = false;
                allTr[i].gameObject.layer = 0; // Default room layer so Main Camera renders it outside the TV screen
            }
        }

        MeshFilter[] mfs = rootHolder.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < mfs.Length; i++)
        {
            if (mfs[i] != null && mfs[i].sharedMesh != null)
            {
                if (mfs[i].sharedMesh.isReadable)
                {
                    Mesh cloneMesh = Instantiate(mfs[i].sharedMesh);
                    mfs[i].mesh = cloneMesh;
                }
                // Center local position relative to mesh bounds so towel is centered on cursor
                Bounds mb = mfs[i].sharedMesh.bounds;
                mfs[i].transform.localPosition = - Vector3.Scale(mb.center, mfs[i].transform.localScale);
            }
        }

        Renderer[] rends = rootHolder.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
            {
                rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        towelHoverTransform = rootHolder.transform;
        towelHoverTransform.gameObject.SetActive(false);
    }

    // =========================================================================
    // PUBLIC ENTRY POINTS (WIRED TO INTERACTABLE OBJECTS)
    // =========================================================================

    public void PickUpRemote()
    {
        EnterToolFocus(HeldTool.Remote);
    }

    public void PickUpTowel()
    {
        EnterToolFocus(HeldTool.Towel);
    }

    public void PutDownCurrentTool()
    {
        if (CurrentTool == HeldTool.None) return;

        ReleaseGrabbedLogoIfAny();
        PlaySound(putDownSound);

        // Restore desk visual of whichever tool was held
        if (CurrentTool == HeldTool.Remote)
        {
            SetDeskRemoteVisible(true);
        }
        else if (CurrentTool == HeldTool.Towel)
        {
            SetDeskTowelVisible(true);
        }

        CurrentTool = HeldTool.None;
        isCursorOnScreen = false;
        prevWipeUV = new Vector2(-1f, -1f);
        smoothedTowelScrubVel = Vector2.zero;

        SetScreenReticleVisible(false);
        SetHoverTowelVisible(false);

        if (tvScreenCollider != null)
        {
            tvScreenCollider.enabled = false;
        }

        if (smoothCamera != null)
        {
            smoothCamera.ExitFocusMode();
        }

        if (playerController != null)
        {
            playerController.SetTVFocusMode(false);
        }
    }

    private void EnterToolFocus(HeldTool tool)
    {
        if (PauseManager.IsGamePaused) return;
        AutoResolveReferences();

        if (WorldSpaceUpgradeMenu.ActiveMenu != null && WorldSpaceUpgradeMenu.ActiveMenu.IsMenuOpen)
        {
            WorldSpaceUpgradeMenu.ActiveMenu.CloseMenu();
        }

        if (CurrentTool != HeldTool.None && CurrentTool != tool)
        {
            PutDownCurrentTool();
        }

        CurrentTool = tool;
        prevWipeUV = new Vector2(-1f, -1f);
        smoothedTowelScrubVel = Vector2.zero;
        PlaySound(pickUpSound);

        if (tool == HeldTool.Remote)
        {
            SetDeskRemoteVisible(false);
            SetHoverTowelVisible(false);
            UpdateReticleStyle(remoteReticleOrthoSize, remoteReticleColor);
        }
        else if (tool == HeldTool.Towel)
        {
            SetDeskTowelVisible(false);
            SetScreenReticleVisible(false);
            EnsureHoverTowelModel();
        }

        if (tvScreenCollider != null)
        {
            tvScreenCollider.enabled = true;
        }

        // Lock player movement and rotation via GridCrawlerController
        if (playerController != null)
        {
            playerController.SetTVFocusMode(true);
        }

        // Smoothly zoom camera so TV screen fills ~85-90% of viewport (exact Iteration 1 centered framing)
        if (smoothCamera != null && tvScreenRenderer != null)
        {
            Bounds b = tvScreenRenderer.bounds;
            Vector3 screenCenter = b.center;
            float dist = Mathf.Max(0.8f, focusDistanceFromScreen);

            // TV screen faces -Z in room coordinates (player is at z ~ -3.45 looking towards z ~ -2.02)
            Vector3 focusCamPos = screenCenter + new Vector3(0f, 0f, -dist);
            Quaternion focusCamRot = Quaternion.LookRotation((screenCenter - focusCamPos).normalized, Vector3.up);

            // Account for the top/bottom UI crop bars (croper takes ~80% of vertical screen height)
            float activeViewportFraction = 0.80f;
            float desiredVisibleHeightAtScreen = b.size.y / (targetViewportFill * activeViewportFraction);
            float calculatedFOV = 2f * Mathf.Atan(desiredVisibleHeightAtScreen / (2f * dist)) * Mathf.Rad2Deg;
            calculatedFOV = Mathf.Clamp(calculatedFOV, 22f, 58f);

            smoothCamera.EnterFocusMode(focusCamPos, focusCamRot, calculatedFOV);
        }
    }

    private void UpdateReticleStyle(float orthoDiameter, Color c)
    {
        EnsureScreenReticle();
        if (screenReticleTransform != null)
        {
            screenReticleTransform.localScale = Vector3.one * orthoDiameter;
        }
        if (screenReticleRenderer != null && screenReticleRenderer.material != null)
        {
            Material m = screenReticleRenderer.material;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 1.4f);
        }
    }

    private void SetDeskRemoteVisible(bool visible)
    {
        if (remoteInteractable != null)
        {
            remoteInteractable.gameObject.SetActive(visible);
        }
    }

    private void SetDeskTowelVisible(bool visible)
    {
        if (towelInteractable != null)
        {
            towelInteractable.gameObject.SetActive(visible);
        }
    }

    public void HideScreenReticle()
    {
        SetScreenReticleVisible(false);
        SetHoverTowelVisible(false);
    }

    private void SetScreenReticleVisible(bool visible)
    {
        if (screenReticleTransform != null && screenReticleTransform.gameObject.activeSelf != visible)
        {
            screenReticleTransform.gameObject.SetActive(visible);
        }
    }

    private void SetHoverTowelVisible(bool visible)
    {
        if (towelHoverTransform != null && towelHoverTransform.gameObject.activeSelf != visible)
        {
            towelHoverTransform.gameObject.SetActive(visible);
        }
    }

    // =========================================================================
    // FOCUS CURSOR & SCREEN INTERACTION (CALLED BY GRIDCRAWLERCONTROLLER)
    // =========================================================================

    public void HandleFocusCursorAndInput(Camera cam)
    {
        if (!IsInFocusMode || cam == null) return;

        if (PauseManager.IsGamePaused)
        {
            SetScreenReticleVisible(false);
            SetHoverTowelVisible(false);
            return;
        }

        Vector2 cursorPos = playerController != null
            ? playerController.virtualMousePos
            : (Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

        Ray ray = cam.ScreenPointToRay(cursorPos);
        bool leftHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool leftClickedThisFrame = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        // If the player is currently holding/shaking a grabbed DVD logo, lock the cursor inside the TV screen!
        if (grabbedLogo != null)
        {
            if (!leftHeld || CurrentTool != HeldTool.Remote || DVDLogoBouncer.GlobalStaticHalt)
            {
                ReleaseGrabbedLogoIfAny();
            }
            else
            {
                UpdateLockedScreenGrab(ray);
                return;
            }
        }

        bool hitScreen = false;
        RaycastHit screenHit = default;

        if (tvScreenCollider != null)
        {
            hitScreen = tvScreenCollider.Raycast(ray, out screenHit, 10f);
        }

        isCursorOnScreen = hitScreen;

        if (isCursorOnScreen)
        {
            // INSIDE TV SCREEN:
            // Hide 2D UI crosshair
            if (playerController != null)
            {
                playerController.SetFocusCursorVisuals(false, false, "");
            }

            Vector2 uv = ComputeScreenUV(screenHit.point);

            if (CurrentTool == HeldTool.Remote)
            {
                // REMOTE: Show 3D sphere cursor INSIDE the TV RenderTexture (DVDSetup/Camera)
                SetHoverTowelVisible(false);

                Vector3 orthoPoint = ScreenUVToOrthoWorldPoint(uv);
                EnsureScreenReticle();
                SetScreenReticleVisible(true);
                if (screenReticleTransform != null)
                {
                    float reticleZ = bouncer != null ? (bouncer.transform.position.z - 0.2f) : (orthoPoint.z - 0.2f);
                    screenReticleTransform.position = new Vector3(orthoPoint.x, orthoPoint.y, reticleZ);
                }

                EnsureMinigameController();
                if (TVMinigameController.Instance != null)
                {
                    TVMinigameController.Instance.OnScreenCursorMoved(orthoPoint);
                }

                HandleRemoteScreenInput(screenHit, uv, orthoPoint, leftClickedThisFrame);
            }
            else if (CurrentTool == HeldTool.Towel)
            {
                // TOWEL: Do NOT show in-screen sphere; hover the actual 3D towel model OUTSIDE on the TV glass!
                SetScreenReticleVisible(false);
                UpdateHoverTowelOnScreen(screenHit, leftHeld);
                HandleTowelScreenInput(uv, leftHeld);
            }
        }
        else
        {
            // OUTSIDE TV SCREEN:
            // Hide 3D screen reticle and 3D hovering towel, show standard 2D reticle + "[Click] Put Down Remote/Towel"
            SetScreenReticleVisible(false);
            SetHoverTowelVisible(false);
            prevWipeUV = new Vector2(-1f, -1f);

            if (playerController != null)
            {
                string prompt = CurrentTool == HeldTool.Remote ? putDownRemoteText : putDownTowelText;
                playerController.SetFocusCursorVisuals(true, true, prompt);
            }

            if (leftClickedThisFrame)
            {
                PutDownCurrentTool();
            }
        }
    }

    /// <summary>
    /// Updates the 3D Towel model hovering directly over the TV screen glass in room world space,
    /// pressing slightly into the glass when holding left-click and tilting dynamically as the player scrubs.
    /// </summary>
    private void UpdateHoverTowelOnScreen(RaycastHit screenHit, bool isWiping)
    {
        EnsureHoverTowelModel();
        if (towelHoverTransform == null) return;

        bool wasInactive = !towelHoverTransform.gameObject.activeSelf;
        SetHoverTowelVisible(true);

        // Determine outward normal toward the player camera (-Z in room space)
        Vector3 outwardNormal = screenHit.normal;
        if (smoothCamera != null)
        {
            Vector3 toCam = (smoothCamera.transform.position - screenHit.point).normalized;
            if (Vector3.Dot(outwardNormal, toCam) < 0f)
            {
                outwardNormal = -outwardNormal;
            }
        }

        float depthOffset = towelHoverSurfaceOffset + (isWiping ? towelPressDepthOffset : 0f);
        Vector3 targetWorldPos = screenHit.point + outwardNormal * depthOffset;

        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        if (wasInactive)
        {
            towelHoverTransform.position = targetWorldPos;
            lastTowelWorldPos = targetWorldPos;
            smoothedTowelScrubVel = Vector2.zero;
        }
        else
        {
            towelHoverTransform.position = Vector3.Lerp(
                towelHoverTransform.position,
                targetWorldPos,
                Mathf.Clamp01(dt * towelHoverFollowSpeed));

            Vector3 worldDelta = (towelHoverTransform.position - lastTowelWorldPos) / dt;
            lastTowelWorldPos = towelHoverTransform.position;
            Vector2 targetVel = new Vector2(worldDelta.x, worldDelta.y);
            smoothedTowelScrubVel = Vector2.Lerp(smoothedTowelScrubVel, targetVel, Mathf.Clamp01(dt * 16f));
        }

        towelHoverTransform.localScale = towelHoverScale;

        // Base rotation aligns the towel flush with the TV screen surface + user's rotation offset
        Quaternion surfaceBasis = Quaternion.LookRotation(-outwardNormal, Vector3.up);
        float tiltZ = Mathf.Clamp(-smoothedTowelScrubVel.x * towelScrubTiltAmount, -28f, 28f);
        float tiltX = Mathf.Clamp(smoothedTowelScrubVel.y * (towelScrubTiltAmount * 0.65f), -20f, 20f);
        Quaternion scrubTilt = Quaternion.Euler(tiltX, 0f, tiltZ);

        towelHoverTransform.rotation = surfaceBasis * scrubTilt * Quaternion.Euler(towelHoverRotationOffset);
    }

    /// <summary>
    /// While holding a logo with the Grab & Shake Remote, projects the mouse ray onto the TV screen plane
    /// and clamps the UV so the cursor and logo can NEVER leave the TV screen until the click hold is released.
    /// </summary>
    private void UpdateLockedScreenGrab(Ray ray)
    {
        isCursorOnScreen = true;

        if (playerController != null)
        {
            playerController.SetFocusCursorVisuals(false, false, "");
        }

        Vector3 worldPointOnScreenPlane = ProjectRayOntoScreenPlane(ray);
        Vector2 clampedUV = ComputeScreenUV(worldPointOnScreenPlane);
        clampedUV.x = Mathf.Clamp(clampedUV.x, 0.02f, 0.98f);
        clampedUV.y = Mathf.Clamp(clampedUV.y, 0.02f, 0.98f);

        Vector3 orthoPoint = ScreenUVToOrthoWorldPoint(clampedUV);

        EnsureScreenReticle();
        SetScreenReticleVisible(true);
        if (screenReticleTransform != null)
        {
            float reticleZ = bouncer != null ? (bouncer.transform.position.z - 0.2f) : (orthoPoint.z - 0.2f);
            screenReticleTransform.position = new Vector3(orthoPoint.x, orthoPoint.y, reticleZ);
        }

        Vector3 targetLogoCenter = new Vector3(
            orthoPoint.x + grabCenterOffset.x,
            orthoPoint.y + grabCenterOffset.y,
            orthoPoint.z);

        grabbedLogo.UpdateGrabbedPosition(targetLogoCenter, Time.deltaTime);
    }

    private void ReleaseGrabbedLogoIfAny()
    {
        if (grabbedLogo != null)
        {
            grabbedLogo.EndGrab();
            grabbedLogo = null;
            if (grabReleaseSound != null)
            {
                PlaySound(grabReleaseSound);
            }
        }
    }

    private Vector3 ProjectRayOntoScreenPlane(Ray ray)
    {
        if (tvScreenRenderer == null) return ray.GetPoint(focusDistanceFromScreen);
        Bounds b = tvScreenRenderer.bounds;
        Plane screenPlane = new Plane(-Vector3.forward, b.center);
        if (screenPlane.Raycast(ray, out float enter) && enter > 0f)
        {
            return ray.GetPoint(enter);
        }
        return b.center;
    }

    private void HandleRemoteScreenInput(RaycastHit screenHit, Vector2 uv, Vector3 orthoPoint, bool leftClickedThisFrame)
    {
        if (!leftClickedThisFrame) return;
        if (bouncer == null || bouncer.RenderCamera == null) return;
        if (DVDLogoBouncer.GlobalStaticHalt) return;

        // If an active Remote Minigame (Dino Jump Remote or Pong Paddle Remote) is running, let it handle the screen click
        if (TVMinigameController.Instance != null && TVMinigameController.Instance.OnScreenClicked(orthoPoint))
        {
            return;
        }

        int activeRemoteTier = upgradeManager != null ? upgradeManager.ActiveRemoteTier : 0;
        // Remote Tiers:
        // 0 = Tier 1: Stock Clicker (Direct Hit Impulse)
        // 1 = Tier 2: Mitosis Splitter (Direct Hit + Split into 2x Children + Recombine)
        // 2 = Tier 3: Telekinetic Grabber (Click & Hold Logo to Grab & Drag into Walls/Corners)
        // 3 = Tier 4: Pinball Bumper Pro (Direct Hit on Logo + Click Empty Screen to Spawn up to 5 Bumpers)
        // 4 = Tier 5: Dino Jump Remote (No-Internet Dino & Bird Jumper while holding remote)
        // 5 = Tier 6: Pong Paddle Remote (1-Sided Pong Bar Defender while holding remote)
        // 6 = Tier 7: Suika Merger Remote (Suika Logo Drop & Exponential Merger while holding remote)
        // 7 = Tier 8: Tower Stack Remote (Deformed Logo Tower Stacker while holding remote)
        // 8 = Tier 9: Fish Frenzy Remote (Eat Smaller Swimming DVD Logos while holding remote)
        // 9 = Tier 10: Block Blast Remote (Full-screen 8x8 DVD Block Blast Grid while holding remote)
        // 10 = Tier 11: Omni-Remote 9000 (Ultimate Remote: Direct Hit + Mitosis Split + Pinball Bumpers!)

        bool isOmni = upgradeManager != null ? upgradeManager.IsCurrentRemoteOmni : (activeRemoteTier == 10);
        bool isGrabShakeRemote = (activeRemoteTier == 2) || (isOmni && ultimateIncludesGrabShake);
        bool canMitosisSplit = (activeRemoteTier == 1) || isOmni;
        bool canSpawnBumpers = (activeRemoteTier == 3) || isOmni;

        // Check if any active DVD logo (main or split child) is under the reticle
        DVDLogoBouncer hitLogo = FindLogoAtOrthoPoint(orthoPoint);

        if (hitLogo != null)
        {
            if (isGrabShakeRemote)
            {
                grabbedLogo = hitLogo;
                Vector3 logoCenter = hitLogo.GetLogoCenter();
                grabCenterOffset = new Vector2(logoCenter.x - orthoPoint.x, logoCenter.y - orthoPoint.y);
                grabbedLogo.BeginGrab();
                PlaySound(grabStartSound != null ? grabStartSound : (bouncer.directHitSound != null ? bouncer.directHitSound : pickUpSound));
            }
            else
            {
                hitLogo.ApplyDirectHit(orthoPoint, screenHit.point, canMitosisSplit);
            }
        }
        else if (canSpawnBumpers)
        {
            // Pinball Bumpers Remote / Ultimate Remote: Clicking empty screen space spawns a 3D bumper inside the TV screen
            AudioSource sharedAudio = bouncer.audioSource != null ? bouncer.audioSource : focusAudioSource;
            Vector3 bumperOrthoPos = new Vector3(orthoPoint.x, orthoPoint.y, bouncer.transform.position.z - 0.1f);
            ScreenBumper.SpawnBumper(
                screenHit.point,
                screenHit.normal,
                bumperOrthoPos,
                bouncer.RenderCamera,
                bouncer.gameObject.layer,
                sharedAudio,
                bumperSpawnSound,
                bumperPingSound,
                bumperOrthoRadius,
                maxActiveBumpers,
                customBumperPrefab,
                bumperColor);
        }
    }

    private void HandleTowelScreenInput(Vector2 uv, bool leftHeld)
    {
        if (screenCleaner == null) return;

        if (leftHeld)
        {
            if (prevWipeUV.x < 0f)
            {
                screenCleaner.WipeStrokeUV(uv, uv);
            }
            else
            {
                screenCleaner.WipeStrokeUV(prevWipeUV, uv);
            }
            prevWipeUV = uv;
        }
        else
        {
            prevWipeUV = new Vector2(-1f, -1f);
        }
    }

    /// <summary>
    /// Converts a world-space point on the TV screen mesh into normalized (0..1, 0..1) screen UV coordinates.
    /// Uses the screen renderer's world-space AABB for distortion-free mapping across the flat CRT screen face.
    /// </summary>
    public Vector2 ComputeScreenUV(Vector3 worldHitPoint)
    {
        if (tvScreenRenderer == null) return new Vector2(0.5f, 0.5f);

        Bounds b = tvScreenRenderer.bounds;
        float width = Mathf.Max(0.001f, b.size.x);
        float height = Mathf.Max(0.001f, b.size.y);

        // From the player's viewpoint (looking towards +Z at the screen), left is min.x and right is max.x
        float u = Mathf.Clamp01((worldHitPoint.x - b.min.x) / width);
        float v = Mathf.Clamp01((worldHitPoint.y - b.min.y) / height);

        return new Vector2(u, v);
    }

    /// <summary>
    /// Maps a normalized (0..1) screen UV coordinate into the orthographic camera's world space where DVDLogoBouncer lives.
    /// </summary>
    public Vector3 ScreenUVToOrthoWorldPoint(Vector2 uv)
    {
        if (bouncer == null || bouncer.RenderCamera == null) return Vector3.zero;

        Camera orthoCam = bouncer.RenderCamera;
        Vector3 camPos = orthoCam.transform.position;
        float halfH = orthoCam.orthographicSize;
        float halfW = halfH * orthoCam.aspect;

        float orthoX = Mathf.Lerp(camPos.x - halfW, camPos.x + halfW, uv.x);
        float orthoY = Mathf.Lerp(camPos.y - halfH, camPos.y + halfH, uv.y);

        return new Vector3(orthoX, orthoY, bouncer.transform.position.z);
    }

    private DVDLogoBouncer FindLogoAtOrthoPoint(Vector3 orthoPoint)
    {
        DVDLogoBouncer bestMatch = null;
        float bestDistSq = float.MaxValue;

        var logos = DVDLogoBouncer.ActiveLogos;
        for (int i = 0; i < logos.Count; i++)
        {
            DVDLogoBouncer logo = logos[i];
            if (logo == null || !logo.isActiveAndEnabled) continue;

            Vector3 center = logo.GetLogoCenter();
            float halfW = logo.HalfWidth + logoHitboxPadding;
            float halfH = logo.HalfHeight + logoHitboxPadding;

            float dx = Mathf.Abs(orthoPoint.x - center.x);
            float dy = Mathf.Abs(orthoPoint.y - center.y);

            if (dx <= halfW && dy <= halfH)
            {
                float dSq = dx * dx + dy * dy;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestMatch = logo;
                }
            }
        }

        return bestMatch;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        if (focusAudioSource != null)
        {
            focusAudioSource.PlayOneShot(clip, 0.85f);
        }
        else if (bouncer != null && bouncer.audioSource != null)
        {
            bouncer.audioSource.PlayOneShot(clip, 0.85f);
        }
    }
}
