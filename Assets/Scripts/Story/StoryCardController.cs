using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using TMPro;

namespace Story
{
    [System.Serializable]
    public class StoryCardData
    {
        public string id = "card_mom";
        public string contactName = "MOM";
        public string contactRole = "Incoming Call";
        [TextArea(3, 6)] public string narrativeText = "Sweetheart, are you eating well?";

        [Header("Portrait Sprite (Leave null for placeholder)")]
        public Sprite portraitSprite;

        [Header("Choice Left (Reject / Ignore)")]
        public string leftLabel = "REJECT";
        [TextArea(1, 3)] public string leftChoicePrompt = "Decline offer";
        [TextArea(2, 4)] public string leftOutcomeText = "You turn away from the call.";
        public float leftDepressionDelta = 0f;
        public float leftTirednessDelta = 0f;
        public float leftMoneyDelta = 0f;
        public bool leftIsPositive = true;

        [Header("Choice Right (Accept / Partake)")]
        public string rightLabel = "ACCEPT";
        [TextArea(1, 3)] public string rightChoicePrompt = "Accept offer";
        [TextArea(2, 4)] public string rightOutcomeText = "You take what was offered.";
        public float rightDepressionDelta = 0f;
        public float rightTirednessDelta = 0f;
        public float rightMoneyDelta = 0f;
        public bool rightIsPositive = false;
    }

    /// <summary>
    /// CRT-themed swipe card story encounter overlay and Day 3 Finale Reckoning Screen.
    /// Operates in ScreenSpaceCamera render mode so CRT post-processing and fullscreen shaders apply directly.
    /// Uses the actual 3D TV DVD Logo mesh (dvd + Torus) with Suika-style bouncy physics and vertex squash-and-stretch on the reckoning scale.
    /// </summary>
    public class StoryCardController : MonoBehaviour
    {
        public static StoryCardController Instance { get; private set; }

        public static StoryCardController EnsureInstance()
        {
            if (Instance != null) return Instance;

            StoryCardController inScene = FindAnyObjectByType<StoryCardController>(FindObjectsInactive.Include);
            if (inScene != null)
            {
                Instance = inScene;
                return inScene;
            }

            if (StoryManager.Instance != null && StoryManager.Instance.storyCardCanvas != null)
            {
                Instance = StoryManager.Instance.storyCardCanvas;
                return Instance;
            }

            StoryCardController[] all = Resources.FindObjectsOfTypeAll<StoryCardController>();
            foreach (var sc in all)
            {
                if (sc != null && sc.gameObject.scene.name == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                {
                    Instance = sc;
                    return sc;
                }
            }

            GameObject canvasObj = new GameObject("StoryCardCanvas");
            StoryCardController created = canvasObj.AddComponent<StoryCardController>();
            Instance = created;
            return created;
        }

        [Header("Camera Configuration")]
        [Tooltip("Target camera used for Screen Space - Camera rendering. If null, automatically binds to the main player camera.")]
        public Camera targetCamera;

        [Header("Card Setup")]
        public RectTransform cardContainer;
        public CanvasGroup cardCanvasGroup;
        public Image portraitImage;
        public TMP_Text contactTitleText;
        public TMP_Text contactRoleText;
        public TMP_Text narrativeBodyText;

        [Header("Stamp Indicators")]
        public RectTransform rejectStamp;
        public CanvasGroup rejectStampGroup;
        public RectTransform acceptStamp;
        public CanvasGroup acceptStampGroup;

        [Header("Side Swipe Edge Indicators")]
        public RectTransform leftSwipeIndicator;
        public CanvasGroup leftSwipeGroup;
        public TMP_Text leftSwipeText;
        public RectTransform rightSwipeIndicator;
        public CanvasGroup rightSwipeGroup;
        public TMP_Text rightSwipeText;

        [Header("Buttons (Hidden - Swipe Only)")]
        public Button leftChoiceButton;
        public TMP_Text leftButtonLabel;
        public Button rightChoiceButton;
        public TMP_Text rightButtonLabel;

        [Header("Consequence Panel")]
        public GameObject consequencePanel;
        public TMP_Text consequenceTitleText;
        public TMP_Text consequenceBodyText;
        public TMP_Text moneyDeltaText;
        public TMP_Text depressionDeltaText;
        public TMP_Text tirednessDeltaText;
        public Button continueButton;

        [Header("Reckoning / Stats Screen (Day 3 Endgame Finale)")]
        public GameObject reckoningPanel;
        public TMP_Text reckoningTitleText;
        public TMP_Text reckoningSubtitleText;
        public TMP_Text reckoningNarrativeText;
        public TMP_Text reckoningPositiveChoicesText;
        public TMP_Text reckoningNegativeChoicesText;
        public TMP_Text reckoningDaysText;
        public TMP_Text reckoningCleanlinessText;
        public TMP_Text reckoningDepressionText;
        public TMP_Text reckoningSubstancesText;
        public TMP_Text reckoningMissedCallsText;
        public TMP_Text reckoningScoreText;
        public TMP_Text reckoningVerdictPromptText;
        public Button reckoningContinueButton;

        [Header("Reckoning Physical Scale & Falling DVDs")]
        public RectTransform scaleContainer;
        public RectTransform scaleBeam;
        public RectTransform leftPan;
        public RectTransform rightPan;
        public RectTransform leftDvdContainer;
        public RectTransform rightDvdContainer;
        public TMP_Text leftWeightText;
        public TMP_Text rightWeightText;
        public RectTransform leftChainA;
        public RectTransform leftChainB;
        public RectTransform rightChainA;
        public RectTransform rightChainB;

        public static bool ForceDay3FinaleForTesting = false;

        private Coroutine reckoningAnimationRoutine;
        private float reckoningSpeedMultiplier = 1f;
        private float scaleCurrentAngle = 0f;
        private float scaleAngularVelocity = 0f;
        private float scaleTargetAngle = 0f;
        private int currentGreenDvds = 0;
        private int currentRedDvds = 0;
        private Sprite greenDvdSprite;
        private Sprite redDvdSprite;

        private AudioClip typeTickClip;
        private AudioClip rowBounceClip;
        private AudioClip impactHopeClip;
        private AudioClip impactDespairClip;
        private AudioClip verdictHopeClip;
        private AudioClip verdictDespairClip;

        [Header("Swipe Parameters")]
        public float swipeThreshold = 120f;
        public float maxCardRotation = 14f;
        public float cardReturnSpeed = 16f;

        private StoryCardData currentCard;
        private Action onCardFinished;
        private bool isCardActive = false;
        private bool isDragging = false;
        private Vector2 dragStartPos;
        private Vector2 currentDragOffset;
        private bool hasChosen = false;
        private bool lastVerdictHopeful = false;
        private AudioSource uiAudio;

        // Consequence panel dragging / swiping
        private bool isDraggingConsequence = false;
        private Vector2 consequenceDragStart;
        private Vector2 consequenceDragOffset;

        // Satisfying swipe card visual accents & virtual cursor
        private Image cardOuterGlowBorder;
        private RectTransform leftBottomPillRect;
        private Image leftBottomPillImg;
        private TMP_Text leftBottomPillText;
        private RectTransform rightBottomPillRect;
        private Image rightBottomPillImg;
        private TMP_Text rightBottomPillText;
        private TMP_Text swipeHintFooterText;
        private RectTransform phoneVirtualCursorRect;
        private Image phoneVirtualCursorRingImg;
        private Sprite cachedDefaultCursorSprite;
        private Sprite cachedInteractCursorSprite;
        private Vector2 cachedDefaultCursorSize = new Vector2(20.7f, 31.8f);
        private Vector2 cachedInteractCursorSize = new Vector2(51f, 51f);
        private Vector2 internalPhoneCursorScreenPos = Vector2.zero;
        private Vector2 currentFrameMouseDelta = Vector2.zero;
        private float cardInputCooldown = 0f;
        private bool isExitingConsequence = false;

        private int savedCameraCullingMask = -1;
        private CameraClearFlags savedCameraClearFlags;
        private Color savedCameraBgColor;
        private bool hasSavedCameraIsolation = false;

        private static TMP_FontAsset cachedFontAsset = null;

        // =========================================================================
        // BOUNCY TV DVD LOGO PHYSICS PIECES ON THE RECKONING SCALE
        // =========================================================================

        private class ScaleBouncyDvdPiece
        {
            public GameObject rootObj;
            public RectTransform rectTransform;
            public bool isHopeSide;
            public Vector2 localPos;
            public Vector2 localVel;
            public float angleZ;
            public float angularVelZ;
            public float halfWidth = 34f;
            public float halfHeight = 19f;
            public Vector3 baseScale = Vector3.one;
            public Vector3 baseCenterOffset = Vector3.zero;
            public Vector3 rootDeformCenter = Vector3.zero;
            public bool hasLandedOnce = false;

            // Vertex Squash & Stretch state (matching TV / Suika DeformableLogoPiece)
            public float squashTimer = -1f;
            public float squashDuration = 0.44f;
            public float squashIntensity = 0.24f;
            public Vector2 squashNormal = Vector2.up;
            public float popTimer = -1f;
            public float popDuration = 0.28f;
            public float popPeakAdd = 0.28f;

            public class MeshPartBuffer
            {
                public MeshFilter filter;
                public Mesh mesh;
                public Vector3[] origLocalVerts;
                public Vector3[] rootSpaceVerts;
                public Vector3[] deformedVerts;
                public Matrix4x4 rootToPart;
            }

            public readonly List<MeshPartBuffer> meshParts = new List<MeshPartBuffer>();

            public void TriggerSquash(Vector2 normal, float intensity = 0.24f, float duration = 0.42f)
            {
                squashNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.up;
                squashIntensity = Mathf.Clamp(intensity, 0.04f, 0.48f);
                squashDuration = Mathf.Max(0.14f, duration);
                squashTimer = 0f;
            }

            public void TriggerPop(float peakAdd = 0.28f, float duration = 0.26f)
            {
                popPeakAdd = peakAdd;
                popDuration = Mathf.Max(0.12f, duration);
                popTimer = 0f;
            }

            public void UpdateDeformation(float dt)
            {
                if (rootObj == null) return;

                float popMult = 1f;
                if (popTimer >= 0f)
                {
                    popTimer += dt;
                    float pNorm = popTimer / popDuration;
                    if (pNorm >= 1f) popTimer = -1f;
                    else popMult = 1f + popPeakAdd * Mathf.Sin(pNorm * Mathf.PI * 2f) * (1f - pNorm);
                }

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
                        float envelope = Mathf.Exp(-2.5f * normT) * (1f - normT);
                        float wave = Mathf.Cos(normT * Mathf.PI * 2f * 2.1f);
                        deform = squashIntensity * envelope * wave;
                    }
                }

                if (meshParts.Count > 0)
                {
                    rootObj.transform.localScale = new Vector3(
                        baseScale.x * popMult,
                        baseScale.y * popMult,
                        baseScale.z);

                    Vector2 n = squashNormal;
                    Vector2 t = new Vector2(-n.y, n.x);
                    float scaleN = Mathf.Max(0.52f, 1f - deform);
                    float scaleT = Mathf.Max(0.52f, 1f + deform * 0.85f);

                    for (int p = 0; p < meshParts.Count; p++)
                    {
                        MeshPartBuffer part = meshParts[p];
                        if (part.mesh == null) continue;

                        Vector3[] rVerts = part.rootSpaceVerts;
                        Vector3[] dVerts = part.deformedVerts;
                        Matrix4x4 r2p = part.rootToPart;
                        int vCount = rVerts.Length;

                        for (int i = 0; i < vCount; i++)
                        {
                            Vector3 rv = rVerts[i];
                            Vector2 rel = new Vector2(rv.x - rootDeformCenter.x, rv.y - rootDeformCenter.y);
                            float dN = Vector2.Dot(rel, n);
                            float dT = Vector2.Dot(rel, t);
                            Vector2 defRel = n * (dN * scaleN) + t * (dT * scaleT);
                            Vector3 defRoot = new Vector3(rootDeformCenter.x + defRel.x, rootDeformCenter.y + defRel.y, rv.z);
                            dVerts[i] = r2p.MultiplyPoint3x4(defRoot);
                        }

                        part.mesh.vertices = dVerts;
                    }
                }
                else
                {
                    // Fallback 2D sprite squash & stretch
                    float sx = 1f + deform * Mathf.Abs(squashNormal.y) - deform * Mathf.Abs(squashNormal.x);
                    float sy = 1f - deform * Mathf.Abs(squashNormal.y) + deform * Mathf.Abs(squashNormal.x);
                    rootObj.transform.localScale = new Vector3(
                        baseScale.x * sx * popMult,
                        baseScale.y * sy * popMult,
                        1f);
                }

                Vector3 pivotOffset = Quaternion.Euler(0f, 0f, angleZ) * baseCenterOffset * popMult;
                rootObj.transform.localPosition = new Vector3(
                    localPos.x - pivotOffset.x,
                    localPos.y - pivotOffset.y,
                    -3f);
                rootObj.transform.localRotation = Quaternion.Euler(0f, 0f, angleZ);
            }
        }

        private readonly List<ScaleBouncyDvdPiece> leftPanDvdPieces = new List<ScaleBouncyDvdPiece>();
        private readonly List<ScaleBouncyDvdPiece> rightPanDvdPieces = new List<ScaleBouncyDvdPiece>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureUIAudio();
            EnsureUIHierarchy();

            if (consequencePanel != null) consequencePanel.SetActive(false);
            if (reckoningPanel != null) reckoningPanel.SetActive(false);
            if (cardContainer != null) cardContainer.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!isCardActive) return;

            // Keep OS cursor LOCKED and hidden so it never leaves the Unity Editor Game View tab!
            if (!PauseManager.IsGamePaused)
            {
                if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
                if (Cursor.visible) Cursor.visible = false;
            }
            GridCrawlerController.InputFrozen = true;

            // Read raw mouse delta directly every frame so swiping and the virtual cursor NEVER stall
            Vector2 rawDelta = Vector2.zero;
            try
            {
                if (Mouse.current != null) rawDelta = Mouse.current.delta.ReadValue();
            }
            catch { }
            if (rawDelta.sqrMagnitude < 0.0001f)
            {
                try
                {
                    rawDelta = new Vector2(Input.GetAxisRaw("Mouse X") * 12f, Input.GetAxisRaw("Mouse Y") * 12f);
                }
                catch { }
            }
            float sens = GlobalAudioSettings.MouseSensitivity > 0.05f ? GlobalAudioSettings.MouseSensitivity : 1.0f;
            currentFrameMouseDelta = rawDelta * sens;

            if (internalPhoneCursorScreenPos.sqrMagnitude < 1f)
            {
                internalPhoneCursorScreenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
            internalPhoneCursorScreenPos += currentFrameMouseDelta;
            internalPhoneCursorScreenPos.x = Mathf.Clamp(internalPhoneCursorScreenPos.x, 0f, Screen.width);
            internalPhoneCursorScreenPos.y = Mathf.Clamp(internalPhoneCursorScreenPos.y, 0f, Screen.height);

            GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
            if (crawler != null)
            {
                crawler.virtualMousePos = internalPhoneCursorScreenPos;
            }

            if (cardInputCooldown > 0f)
            {
                cardInputCooldown -= Time.unscaledDeltaTime;
            }

            UpdatePhoneVirtualCursor();

            // 1. While CardContainer is active (and choice not yet made)
            if (cardContainer != null && cardContainer.gameObject.activeSelf && !hasChosen)
            {
                UpdateSideIndicators();
                HandleDirectMouseSwipe();

                if (!isDragging)
                {
                    float cursorBias = 0f;
                    if (Screen.width > 20)
                    {
                        cursorBias = Mathf.Clamp((internalPhoneCursorScreenPos.x - Screen.width * 0.5f) / (Screen.width * 0.35f), -1f, 1f);
                    }
                    Vector2 targetOffset = new Vector2(cursorBias * 95f, -Mathf.Abs(cursorBias) * 10f);
                    currentDragOffset = Vector2.Lerp(currentDragOffset, targetOffset, Time.unscaledDeltaTime * 14f);
                    ApplyCardTransform(currentDragOffset);
                }

                bool leftPressed = false;
                bool rightPressed = false;
                try
                {
                    if (Keyboard.current != null)
                    {
                        leftPressed = Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame;
                        rightPressed = Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame;
                    }
                }
                catch { }

                if (!leftPressed && !rightPressed)
                {
                    try
                    {
                        leftPressed = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
                        rightPressed = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
                    }
                    catch { }
                }

                if (leftPressed) ChooseLeft();
                else if (rightPressed) ChooseRight();
            }

            // 2. While ConsequencePanel is active
            if (consequencePanel != null && consequencePanel.activeSelf)
            {
                HandleConsequenceSwipeAndInput();
            }

            // 3. While ReckoningPanel is active
            if (reckoningPanel != null && reckoningPanel.activeSelf)
            {
                float dt = Mathf.Min(0.033f, Time.unscaledDeltaTime) * reckoningSpeedMultiplier;
                UpdateScaleVisuals(dt);
                UpdatePanDvdPhysics(leftPanDvdPieces, true, dt);
                UpdatePanDvdPhysics(rightPanDvdPieces, false, dt);
                HandleReckoningInput();
            }
        }

        private void UpdatePhoneVirtualCursor()
        {
            if (phoneVirtualCursorRect == null) return;
            phoneVirtualCursorRect.SetAsLastSibling();
            if (!phoneVirtualCursorRect.gameObject.activeSelf) phoneVirtualCursorRect.gameObject.SetActive(true);

            Vector2 vPos = GetMouseScreenPosition();
            Canvas canvas = GetComponent<Canvas>();
            Camera eventCam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;
            RectTransform canvasRT = transform as RectTransform;
            if (canvasRT != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, vPos, eventCam, out Vector2 localPt))
            {
                phoneVirtualCursorRect.anchoredPosition = localPt;
            }

            if (cachedDefaultCursorSprite == null || cachedInteractCursorSprite == null)
            {
                GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
                if (crawler != null)
                {
                    if (crawler.defaultCrosshairImg != null && crawler.defaultCrosshairImg.sprite != null)
                    {
                        cachedDefaultCursorSprite = crawler.defaultCrosshairImg.sprite;
                        RectTransform rt = crawler.defaultCrosshairImg.rectTransform;
                        cachedDefaultCursorSize = new Vector2(
                            rt.sizeDelta.x * Mathf.Abs(rt.localScale.x) * 1.061f,
                            rt.sizeDelta.y * Mathf.Abs(rt.localScale.y) * 1.061f);
                        if (cachedDefaultCursorSize.sqrMagnitude < 16f) cachedDefaultCursorSize = new Vector2(20.7f, 31.8f);
                    }
                    if (crawler.interactCrosshairImg != null && crawler.interactCrosshairImg.sprite != null)
                    {
                        cachedInteractCursorSprite = crawler.interactCrosshairImg.sprite;
                        RectTransform rt = crawler.interactCrosshairImg.rectTransform;
                        cachedInteractCursorSize = new Vector2(
                            rt.sizeDelta.x * Mathf.Abs(rt.localScale.x) * 1.061f,
                            rt.sizeDelta.y * Mathf.Abs(rt.localScale.y) * 1.061f);
                        if (cachedInteractCursorSize.sqrMagnitude < 16f) cachedInteractCursorSize = new Vector2(51f, 51f);
                    }
                }
            }

            bool isHoveringButton = false;
            if (cardContainer != null && cardContainer.gameObject.activeSelf && !hasChosen)
            {
                if (leftSwipeIndicator != null && RectTransformUtility.RectangleContainsScreenPoint(leftSwipeIndicator, vPos, eventCam))
                    isHoveringButton = true;
                else if (rightSwipeIndicator != null && RectTransformUtility.RectangleContainsScreenPoint(rightSwipeIndicator, vPos, eventCam))
                    isHoveringButton = true;
            }
            if (consequencePanel != null && consequencePanel.activeSelf)
            {
                isHoveringButton = true;
            }
            if (reckoningPanel != null && reckoningPanel.activeSelf && reckoningContinueButton != null && reckoningContinueButton.gameObject.activeInHierarchy && reckoningContinueButton.interactable)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(reckoningContinueButton.GetComponent<RectTransform>(), vPos, eventCam))
                    isHoveringButton = true;
            }

            bool useInteractSprite = isDragging || isDraggingConsequence || isHoveringButton;
            phoneVirtualCursorRect.pivot = useInteractSprite ? new Vector2(0.5f, 0.5f) : new Vector2(0.28f, 0.82f);
            if (phoneVirtualCursorRingImg != null)
            {
                Sprite targetSprite = useInteractSprite && cachedInteractCursorSprite != null
                    ? cachedInteractCursorSprite
                    : cachedDefaultCursorSprite;
                if (targetSprite != null)
                {
                    phoneVirtualCursorRingImg.sprite = targetSprite;
                    phoneVirtualCursorRingImg.color = new Color(1f, 0.953f, 0.918f, 1f);
                    phoneVirtualCursorRect.sizeDelta = useInteractSprite ? cachedInteractCursorSize : cachedDefaultCursorSize;
                }
                else
                {
                    phoneVirtualCursorRingImg.color = Color.white;
                    phoneVirtualCursorRect.sizeDelta = new Vector2(21.2f, 31.8f);
                }
            }
            phoneVirtualCursorRect.localScale = Vector3.one;
        }

        private void UpdateSideIndicators()
        {
            if (leftSwipeIndicator == null || rightSwipeIndicator == null) return;

            if (!leftSwipeIndicator.gameObject.activeSelf) leftSwipeIndicator.gameObject.SetActive(true);
            if (!rightSwipeIndicator.gameObject.activeSelf) rightSwipeIndicator.gameObject.SetActive(true);

            // Normalized cursor horizontal bias (-1 on left, +1 on right)
            float cursorBias = 0f;
            if (Screen.width > 20)
            {
                cursorBias = Mathf.Clamp((internalPhoneCursorScreenPos.x - Screen.width * 0.5f) / (Screen.width * 0.35f), -1f, 1f);
            }

            float dragBias = Mathf.Clamp(currentDragOffset.x / 40f, -1f, 1f);
            float activeBias = isDragging ? dragBias : (cursorBias * 0.85f);

            float leftStrength = Mathf.Clamp01(-activeBias);
            float rightStrength = Mathf.Clamp01(activeBias);

            // Continuous rich pulsing animations that run ALWAYS, independent of holding click!
            float time = Time.unscaledTime;
            float leftPulse = Mathf.Sin(time * 3.6f) * 0.06f;
            float rightPulse = Mathf.Sin(time * 3.6f + 1.5f) * 0.06f;
            float leftChevronWiggle = Mathf.PingPong(time * 2.8f, 12f) - 6f;
            float rightChevronWiggle = -Mathf.PingPong(time * 2.8f, 12f) + 6f;

            if (leftSwipeGroup != null)
            {
                float baseLeftAlpha = 0.65f + leftPulse * 1.5f;
                leftSwipeGroup.alpha = Mathf.Lerp(baseLeftAlpha, 1.0f, leftStrength);
            }
            leftSwipeIndicator.localScale = Vector3.one * (1.0f + leftPulse + leftStrength * 0.22f);
            leftSwipeIndicator.anchoredPosition = new Vector2(54f + (leftStrength > 0.05f ? -8f * leftStrength : leftChevronWiggle), 0f);

            if (rightSwipeGroup != null)
            {
                float baseRightAlpha = 0.65f + rightPulse * 1.5f;
                rightSwipeGroup.alpha = Mathf.Lerp(baseRightAlpha, 1.0f, rightStrength);
            }
            rightSwipeIndicator.localScale = Vector3.one * (1.0f + rightPulse + rightStrength * 0.22f);
            rightSwipeIndicator.anchoredPosition = new Vector2(-54f + (rightStrength > 0.05f ? 8f * rightStrength : rightChevronWiggle), 0f);
        }

        private void HideSideIndicators()
        {
            if (leftSwipeIndicator != null && leftSwipeIndicator.gameObject.activeSelf) leftSwipeIndicator.gameObject.SetActive(false);
            if (rightSwipeIndicator != null && rightSwipeIndicator.gameObject.activeSelf) rightSwipeIndicator.gameObject.SetActive(false);
        }

        private void HandleDirectMouseSwipe()
        {
            if (cardInputCooldown > 0f) return;

            bool leftJustPressed = GetMouseButtonDown();
            bool leftReleased = GetMouseButtonUp();

            // Direct Click: clicking anywhere on left side chooses Left, right side chooses Right!
            if (leftJustPressed || leftReleased)
            {
                if (internalPhoneCursorScreenPos.x < Screen.width * 0.5f)
                {
                    ChooseLeft();
                }
                else
                {
                    ChooseRight();
                }
                return;
            }
        }

        private void HandleConsequenceSwipeAndInput()
        {
            if (isExitingConsequence) return;
            if (cardInputCooldown > 0f) return;

            bool leftJustPressed = GetMouseButtonDown();
            bool leftReleased = GetMouseButtonUp();

            // Click anywhere or release to dismiss and hang up!
            if (leftJustPressed || leftReleased)
            {
                StartCoroutine(AnimateConsequenceExit(true));
                return;
            }

            bool dismissKey = false;
            try
            {
                if (Keyboard.current != null)
                {
                    dismissKey = Keyboard.current.spaceKey.wasPressedThisFrame ||
                                 Keyboard.current.enterKey.wasPressedThisFrame ||
                                 Keyboard.current.eKey.wasPressedThisFrame ||
                                 Keyboard.current.escapeKey.wasPressedThisFrame ||
                                 Keyboard.current.leftArrowKey.wasPressedThisFrame ||
                                 Keyboard.current.rightArrowKey.wasPressedThisFrame ||
                                 Keyboard.current.aKey.wasPressedThisFrame ||
                                 Keyboard.current.dKey.wasPressedThisFrame;
                }
            }
            catch { }

            if (!dismissKey)
            {
                try
                {
                    dismissKey = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                                 Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape) ||
                                 Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) ||
                                 Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D);
                }
                catch { }
            }

            if (dismissKey)
            {
                StartCoroutine(AnimateConsequenceExit(true));
            }
        }

        private IEnumerator AnimateConsequenceExit(bool toRight)
        {
            if (isExitingConsequence) yield break;
            isExitingConsequence = true;

            RectTransform consRT = consequencePanel != null ? consequencePanel.GetComponent<RectTransform>() : null;
            if (consRT == null)
            {
                isExitingConsequence = false;
                isExitingConsequence = false;
            OnContinueClicked();
                yield break;
            }

            float targetX = toRight ? 820f : -820f;
            Vector2 startPos = consRT.anchoredPosition;
            float startRot = consRT.localRotation.eulerAngles.z;
            if (startRot > 180f) startRot -= 360f;
            float targetRot = toRight ? -22f : 22f;

            float elapsed = 0f;
            float dur = 0.20f;

            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / dur);
                consRT.anchoredPosition = Vector2.Lerp(startPos, new Vector2(targetX, -35f), t);
                consRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRot, targetRot, t));
                yield return null;
            }

            isExitingConsequence = false;
            OnContinueClicked();
        }

        private IEnumerator ReturnConsequenceToCenter(RectTransform consRT)
        {
            if (consRT == null) yield break;
            Vector2 startPos = consRT.anchoredPosition;
            float startRot = consRT.localRotation.eulerAngles.z;
            if (startRot > 180f) startRot -= 360f;

            float elapsed = 0f;
            float dur = 0.16f;

            while (elapsed < dur && !isDraggingConsequence)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / dur);
                consRT.anchoredPosition = Vector2.Lerp(startPos, Vector2.zero, t);
                consRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRot, 0f, t));
                consRT.localScale = Vector3.Lerp(consRT.localScale, Vector3.one, t);
                yield return null;
            }

            if (!isDraggingConsequence)
            {
                consRT.anchoredPosition = Vector2.zero;
                consRT.localRotation = Quaternion.identity;
                consRT.localScale = Vector3.one;
            }
        }

        private void HandleReckoningInput()
        {
            if (reckoningContinueButton == null || !reckoningContinueButton.gameObject.activeSelf || !reckoningContinueButton.interactable)
            {
                bool fastForward = false;
                try
                {
                    if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) fastForward = true;
                    if (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)) fastForward = true;
                }
                catch { }

                if (!fastForward)
                {
                    try
                    {
                        fastForward = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
                    }
                    catch { }
                }

                if (fastForward)
                {
                    reckoningSpeedMultiplier = 3.5f;
                }
                return;
            }

            Vector2 mouseScreenPos = GetMouseScreenPosition();
            Canvas canvas = GetComponent<Canvas>();
            Camera eventCam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;

            bool leftDown = GetMouseButtonDown();
            bool leftUp = GetMouseButtonUp();
            if ((leftDown || leftUp) && reckoningContinueButton != null)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(reckoningContinueButton.GetComponent<RectTransform>(), mouseScreenPos, eventCam)
                    || mouseScreenPos.y < Screen.height * 0.25f)
                {
                    OnReckoningContinueClicked();
                    return;
                }
            }

            bool dismissKey = false;
            try
            {
                if (Keyboard.current != null)
                {
                    dismissKey = Keyboard.current.spaceKey.wasPressedThisFrame ||
                                 Keyboard.current.enterKey.wasPressedThisFrame ||
                                 Keyboard.current.eKey.wasPressedThisFrame;
                }
            }
            catch { }

            if (!dismissKey)
            {
                try
                {
                    dismissKey = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.E);
                }
                catch { }
            }

            if (dismissKey)
            {
                OnReckoningContinueClicked();
            }
        }

        private Vector2 GetMouseScreenPosition()
        {
            if (internalPhoneCursorScreenPos.sqrMagnitude > 1f)
            {
                return internalPhoneCursorScreenPos;
            }
            return GridCrawlerController.GetActiveVirtualScreenPos();
        }

        private bool GetMouseButtonDown()
        {
            try
            {
                if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) return true;
            }
            catch { }
            try
            {
                return Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            }
            catch { }
            return false;
        }

        private bool GetMouseButtonHeld()
        {
            try
            {
                if (Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed)) return true;
            }
            catch { }
            try
            {
                return Input.GetMouseButton(0) || Input.GetMouseButton(1);
            }
            catch { }
            return false;
        }

        private bool GetMouseButtonUp()
        {
            try
            {
                if (Mouse.current != null && (Mouse.current.leftButton.wasReleasedThisFrame || Mouse.current.rightButton.wasReleasedThisFrame)) return true;
            }
            catch { }
            try
            {
                return Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1);
            }
            catch { }
            return false;
        }

        private void ApplyCardTransform(Vector2 offset)
        {
            if (cardContainer == null) return;

            cardContainer.anchoredPosition = new Vector2(offset.x, offset.y * 0.25f - Mathf.Abs(offset.x) * 0.04f);
            float rotationZ = Mathf.Clamp(-offset.x * 0.045f, -maxCardRotation, maxCardRotation);
            cardContainer.localRotation = Quaternion.Euler(0f, 0f, rotationZ);

            float leftNorm = Mathf.Clamp01(-offset.x / (swipeThreshold * 0.85f));
            float rightNorm = Mathf.Clamp01(offset.x / (swipeThreshold * 0.85f));
            float activeNorm = Mathf.Max(leftNorm, rightNorm);
            cardContainer.localScale = Vector3.one * (1.0f + activeNorm * 0.035f);

            if (rejectStampGroup != null)
            {
                rejectStampGroup.alpha = leftNorm;
                if (rejectStamp != null) rejectStamp.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.18f, leftNorm);
            }
            if (acceptStampGroup != null)
            {
                acceptStampGroup.alpha = rightNorm;
                if (acceptStamp != null) acceptStamp.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.18f, rightNorm);
            }

            if (cardOuterGlowBorder != null)
            {
                if (leftNorm > 0.05f)
                    cardOuterGlowBorder.color = Color.Lerp(new Color(0.28f, 0.88f, 1.0f, 0.95f), new Color(1.0f, 0.28f, 0.36f, 1.0f), leftNorm);
                else if (rightNorm > 0.05f)
                    cardOuterGlowBorder.color = Color.Lerp(new Color(0.28f, 0.88f, 1.0f, 0.95f), new Color(0.28f, 1.0f, 0.52f, 1.0f), rightNorm);
                else
                    cardOuterGlowBorder.color = new Color(0.28f, 0.88f, 1.0f, 0.95f);
            }

            if (leftBottomPillImg != null && leftBottomPillRect != null)
            {
                leftBottomPillImg.color = Color.Lerp(new Color(0.22f, 0.08f, 0.11f, 0.92f), new Color(0.88f, 0.18f, 0.26f, 1.0f), leftNorm);
                leftBottomPillRect.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.08f, leftNorm);
            }
            if (rightBottomPillImg != null && rightBottomPillRect != null)
            {
                rightBottomPillImg.color = Color.Lerp(new Color(0.08f, 0.22f, 0.13f, 0.92f), new Color(0.18f, 0.85f, 0.42f, 1.0f), rightNorm);
                rightBottomPillRect.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.08f, rightNorm);
            }
        }

        public void ShowNextEncounter(Action onComplete)
        {
            bool isEndgame = UpgradeManager.Instance != null && UpgradeManager.Instance.IsEndgameMaxed;
            bool isDay3Finale = ForceDay3FinaleForTesting ||
                                (KitchenPhone.Instance != null && KitchenPhone.Instance.pendingForceFinaleCall) ||
                                (isEndgame && RunSaveManager.HasInstance && RunSaveManager.Instance.postMaxDaysSurvived >= 3 && !RunSaveManager.Instance.finalPhoneEncounterDone);

            if (isDay3Finale)
            {
                ShowReckoningStatsScreen(onComplete);
                return;
            }

            StoryCardData nextCard = GetNextCard();
            ShowCard(nextCard, onComplete);
        }

        public void ShowCard(StoryCardData card, Action onComplete)
        {
            currentCard = card;
            onCardFinished = onComplete;
            hasChosen = false;
            isDragging = false;
            isDraggingConsequence = false;
            currentDragOffset = Vector2.zero;
            consequenceDragOffset = Vector2.zero;
            isExitingConsequence = false;
            internalPhoneCursorScreenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            cardInputCooldown = 0.15f;
            GridCrawlerController.InputFrozen = true;

            // Always use normal gameplay CRT (never darker pause CRT)
            CrtPostProcessManager.SetGameplayCrtActive();

            gameObject.SetActive(true);
            EnsureUIHierarchy();
            SetupScreenSpaceCamera(GetComponent<Canvas>());
            EnableCameraAndHudIsolation();
            EnsureEventSystemActive();
            isCardActive = true;

            EnsureCursorVisible();
            PopulateCardUI(card);

            if (cardContainer != null)
            {
                cardContainer.localScale = Vector3.one;
                cardContainer.anchoredPosition = Vector2.zero;
                cardContainer.localRotation = Quaternion.identity;
                cardContainer.gameObject.SetActive(true);
            }

            if (consequencePanel != null) consequencePanel.SetActive(false);
            if (reckoningPanel != null) reckoningPanel.SetActive(false);

            ApplyCardTransform(Vector2.zero);
            PlayProceduralBeep(520f, 0.08f);
        }

        // =========================================================================
        // DAY 3 ENDGAME RECKONING / STATS SCREEN WITH WEIGHING SCALE & TV DVD LOGOS
        // =========================================================================

        public void ShowReckoningStatsScreen(Action onComplete)
        {
            onCardFinished = onComplete;
            hasChosen = false;
            isDragging = false;
            isDraggingConsequence = false;
            currentDragOffset = Vector2.zero;

            ForceDay3FinaleForTesting = false;
            if (KitchenPhone.Instance != null)
            {
                KitchenPhone.Instance.pendingForceFinaleCall = false;
            }

            CrtPostProcessManager.SetGameplayCrtActive();

            gameObject.SetActive(true);
            EnsureUIHierarchy();
            EnsureReckoningScaleUI();
            NormalizeReckoningLayout();
            EnsureDvdSprites();
            EnsureProceduralAudioClips();
            SetupScreenSpaceCamera(GetComponent<Canvas>());
            EnableCameraAndHudIsolation();
            EnsureEventSystemActive();
            isCardActive = true;

            EnsureCursorVisible();
            HideSideIndicators();

            if (cardContainer != null) cardContainer.gameObject.SetActive(false);
            if (consequencePanel != null) consequencePanel.SetActive(false);
            if (reckoningPanel != null)
            {
                reckoningPanel.transform.localScale = Vector3.one;
                RectTransform reckRT = reckoningPanel.GetComponent<RectTransform>();
                if (reckRT != null)
                {
                    reckRT.localScale = Vector3.one;
                    reckRT.anchoredPosition = Vector2.zero;
                    reckRT.localRotation = Quaternion.identity;
                }
                reckoningPanel.SetActive(true);
            }

            if (reckoningAnimationRoutine != null)
            {
                StopCoroutine(reckoningAnimationRoutine);
            }
            reckoningAnimationRoutine = StartCoroutine(PlayReckoningSequenceRoutine());
        }

        private void ClearScaleDvdPieces()
        {
            for (int i = 0; i < leftPanDvdPieces.Count; i++)
            {
                if (leftPanDvdPieces[i] != null && leftPanDvdPieces[i].rootObj != null)
                    Destroy(leftPanDvdPieces[i].rootObj);
            }
            leftPanDvdPieces.Clear();

            for (int i = 0; i < rightPanDvdPieces.Count; i++)
            {
                if (rightPanDvdPieces[i] != null && rightPanDvdPieces[i].rootObj != null)
                    Destroy(rightPanDvdPieces[i].rootObj);
            }
            rightPanDvdPieces.Clear();

            if (leftDvdContainer != null)
            {
                for (int i = leftDvdContainer.childCount - 1; i >= 0; i--)
                    Destroy(leftDvdContainer.GetChild(i).gameObject);
            }
            if (rightDvdContainer != null)
            {
                for (int i = rightDvdContainer.childCount - 1; i >= 0; i--)
                    Destroy(rightDvdContainer.GetChild(i).gameObject);
            }
        }

        private IEnumerator PlayReckoningSequenceRoutine()
        {
            reckoningSpeedMultiplier = 1f;
            currentGreenDvds = 0;
            currentRedDvds = 0;

            ClearScaleDvdPieces();

            scaleCurrentAngle = 0f;
            scaleAngularVelocity = 0f;
            scaleTargetAngle = 0f;
            UpdateScaleVisuals(0.01f);

            if (reckoningContinueButton != null)
            {
                reckoningContinueButton.gameObject.SetActive(false);
                reckoningContinueButton.interactable = false;
                CanvasGroup cg = reckoningContinueButton.GetComponent<CanvasGroup>();
                if (cg == null) cg = reckoningContinueButton.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
            }

            if (reckoningTitleText != null) reckoningTitleText.text = "THE FINAL RECKONING";
            if (reckoningSubtitleText != null) reckoningSubtitleText.text = "DAY 3 ENDGAME TELEPHONE EVALUATION — WEIGHING YOUR RUN";
            if (leftWeightText != null) leftWeightText.text = "<color=#44FF77><b>HOPE: 0 DVDs</b></color>";
            if (rightWeightText != null) rightWeightText.text = "<color=#FF4455><b>DESPAIR: 0 DVDs</b></color>";
            if (reckoningScoreText != null) reckoningScoreText.text = "SCALE BALANCE: <b>AWAITING EVALUATION...</b>";
            if (reckoningVerdictPromptText != null) reckoningVerdictPromptText.text = "WEIGHING YOUR CHOICES AND SELF-CARE ON THE SCALE... (CLICK TO FAST-FORWARD)";

            ResetStatRowText(reckoningPositiveChoicesText, "HOPEFUL PHONE CHOICES");
            ResetStatRowText(reckoningNegativeChoicesText, "ESCAPIST PHONE CHOICES");
            ResetStatRowText(reckoningDaysText, "ENDGAME DAYS ENDURED");
            ResetStatRowText(reckoningCleanlinessText, "APARTMENT HYGIENE & CARE");
            ResetStatRowText(reckoningSubstancesText, "SUBSTANCES & TOXICITY");
            ResetStatRowText(reckoningDepressionText, "MENTAL STATE (DEPRESSION)");
            ResetStatRowText(reckoningMissedCallsText, "ISOLATION (MISSED CALLS)");

            // 1. Gather stats from RunSaveManager and BarManager
            int pos = RunSaveManager.HasInstance ? RunSaveManager.Instance.positiveSecretCounter : 0;
            int neg = RunSaveManager.HasInstance ? RunSaveManager.Instance.negativeSecretCounter : 0;
            int days = RunSaveManager.HasInstance ? Mathf.Max(1, RunSaveManager.Instance.postMaxDaysSurvived) : 3;
            float depression = BarManager.Instance != null ? BarManager.Instance.currentDepression : 0f;
            int substances = RunSaveManager.HasInstance ? RunSaveManager.Instance.substancesConsumed : 0;
            int trash = RunSaveManager.HasInstance ? RunSaveManager.Instance.trashCleaned : 0;
            float dirtiness = BarManager.Instance != null ? BarManager.Instance.currentDirtiness : 0f;
            float missed = RunSaveManager.HasInstance ? RunSaveManager.Instance.missedStoryCounter : 0f;

            bool overallHopeLeaning = (pos + (substances <= 3 ? 2 : 0) + (depression <= 40 ? 2 : 0)) >=
                                      (neg + (substances >= 10 ? 2 : 0) + (depression >= 60 ? 2 : 0));

            // 2. Typewriter Narrative
            string fullNarrative = overallHopeLeaning
                ? "\"The static clears into a steady, familiar voice: 'You stood firm against the noise. Through every sleepless hour and temptation, you held onto yourself. The screen doesn't own your life.'\""
                : "\"The receiver hums with cold, merciless feedback: 'You let the static swallow you whole. Chasing numbness, letting the room rot around you. The glow of the screen is all you have left.'\"";

            if (reckoningNarrativeText != null)
            {
                reckoningNarrativeText.text = "";
                for (int c = 1; c <= fullNarrative.Length; c++)
                {
                    reckoningNarrativeText.text = fullNarrative.Substring(0, c);
                    if (c % 2 == 1) PlayProceduralTick(1f + (c * 0.002f));
                    yield return WaitReckoningTime(0.011f);
                }
                reckoningNarrativeText.text = fullNarrative;
            }
            yield return WaitReckoningTime(0.25f);

            // 3. Stats list evaluation with bounce, color flash, and bouncy TV DVD logos dropping onto the scale
            int posDvds = Mathf.Max(1, pos);
            yield return AnimateStatRowEntry(reckoningPositiveChoicesText, "HOPEFUL PHONE CHOICES", posDvds, true, $"{pos} Answered");
            yield return WaitReckoningTime(0.14f);

            int negDvds = Mathf.Max(1, neg);
            yield return AnimateStatRowEntry(reckoningNegativeChoicesText, "ESCAPIST PHONE CHOICES", negDvds, false, $"{neg} Answered");
            yield return WaitReckoningTime(0.14f);

            yield return AnimateStatRowEntry(reckoningDaysText, "ENDGAME DAYS ENDURED", Mathf.Clamp(days, 1, 5), true, $"{days} Days Survived");
            yield return WaitReckoningTime(0.14f);

            bool isClean = dirtiness < 40f || trash >= 6;
            yield return AnimateStatRowEntry(reckoningCleanlinessText, "APARTMENT HYGIENE & CARE", 2, isClean, isClean ? "Kept Clean" : "Filthy Room");
            yield return WaitReckoningTime(0.14f);

            bool isSober = substances <= 3;
            int subDvd = isSober ? 2 : (substances <= 9 ? 2 : 4);
            yield return AnimateStatRowEntry(reckoningSubstancesText, "SUBSTANCES & TOXICITY", subDvd, isSober, isSober ? "Restrained" : $"{substances} Used");
            yield return WaitReckoningTime(0.14f);

            bool isDepManaged = depression <= 40f;
            int depDvd = isDepManaged ? 2 : (depression >= 65f ? 3 : 2);
            yield return AnimateStatRowEntry(reckoningDepressionText, "MENTAL STATE (DEPRESSION)", depDvd, isDepManaged, $"{depression:0}% ({(isDepManaged ? "Stable" : "Severe")})");
            yield return WaitReckoningTime(0.14f);

            int missedDvd = missed >= 1.0f ? Mathf.Clamp(Mathf.RoundToInt(missed), 1, 3) : 1;
            bool noMissedCalls = missed < 1.0f;
            yield return AnimateStatRowEntry(reckoningMissedCallsText, "ISOLATION (MISSED CALLS)", missedDvd, noMissedCalls, noMissedCalls ? "0 Ignored" : $"{missedDvd} Ignored");
            yield return WaitReckoningTime(0.65f);

            // 4. Final Verdict
            bool finalVerdict = currentGreenDvds >= currentRedDvds;
            lastVerdictHopeful = finalVerdict;

            if (reckoningScoreText != null)
            {
                reckoningScoreText.text = $"FINAL WEIGHED BALANCE:   <color=#44FF77><b>{currentGreenDvds} HOPE DVDs</b></color>   vs   <color=#FF4455><b>{currentRedDvds} DESPAIR DVDs</b></color>";
            }

            if (reckoningVerdictPromptText != null)
            {
                reckoningVerdictPromptText.text = finalVerdict
                    ? "<color=#44FF77><b>VERDICT: REACH OUT — HOPE PREVAILS</b></color>\nYou took care of yourself and held onto hope. <b>WALK TO THE FRONT DOOR TO STEP OUTSIDE.</b>"
                    : "<color=#FF4455><b>VERDICT: SURRENDER — CONSUMED BY STATIC</b></color>\nThe isolation and despair tipped the scale. <b>RETURN TO THE TELEVISION IN THE ROOM TO END IT ALL.</b>";
            }

            PlayVerdictSound(finalVerdict);
            yield return WaitReckoningTime(0.35f);

            // 5. Reveal Continue Button
            if (reckoningContinueButton != null)
            {
                reckoningContinueButton.gameObject.SetActive(true);
                CanvasGroup cg = reckoningContinueButton.GetComponent<CanvasGroup>();
                RectTransform btnRT = reckoningContinueButton.GetComponent<RectTransform>();

                float btnElapsed = 0f;
                float btnDur = 0.32f;
                while (btnElapsed < btnDur)
                {
                    btnElapsed += Time.unscaledDeltaTime;
                    float bt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(btnElapsed / btnDur));
                    if (cg != null) cg.alpha = bt;
                    if (btnRT != null)
                    {
                        float s = Mathf.Lerp(0.88f, 1f, bt);
                        btnRT.localScale = new Vector3(s, s, 1f);
                    }
                    yield return null;
                }

                if (cg != null) cg.alpha = 1f;
                if (btnRT != null) btnRT.localScale = Vector3.one;
                reckoningContinueButton.interactable = true;
            }

            reckoningAnimationRoutine = null;
        }

        private void ResetStatRowText(TMP_Text textComp, string label)
        {
            if (textComp == null) return;
            textComp.text = $"{label}:   <color=#778899><b>—</b></color>";
            Transform rowRoot = textComp.transform.parent != null && textComp.transform.parent.name.StartsWith("Row_")
                ? textComp.transform.parent
                : textComp.transform;
            rowRoot.localScale = Vector3.one;
        }

        private IEnumerator AnimateStatRowEntry(TMP_Text textComp, string label, int dvdCount, bool isHope, string detail)
        {
            if (textComp == null) yield break;

            Transform rowRoot = textComp.transform.parent != null && textComp.transform.parent.name.StartsWith("Row_")
                ? textComp.transform.parent
                : textComp.transform;
            Image rowBar = rowRoot.GetComponent<Image>();
            Color baseBarColor = rowBar != null ? new Color(0.08f, 0.12f, 0.18f, 0.92f) : Color.clear;
            Color flashColor = isHope
                ? new Color(0.16f, 0.88f, 0.42f, 0.55f)
                : new Color(0.92f, 0.18f, 0.24f, 0.55f);
            string hexColor = isHope ? "#44FF77" : "#FF4455";
            string sideTag = isHope ? "HOPE" : "DESPAIR";

            for (int pt = 1; pt <= dvdCount; pt++)
            {
                textComp.text = $"{label} <color=#99AABB>({detail})</color>   <color={hexColor}><b>+{pt} {sideTag} DVD{(pt > 1 ? "s" : "")}</b></color>";

                if (rowBar != null) rowBar.color = flashColor;
                rowRoot.localScale = new Vector3(1.14f, 1.18f, 1f);

                PlayProceduralRowBounce(1f + (pt * 0.06f));
                SpawnBouncyTvDvdOnScale(isHope);

                float pElapsed = 0f;
                float pDur = 0.16f;
                while (pElapsed < pDur)
                {
                    pElapsed += Time.unscaledDeltaTime * reckoningSpeedMultiplier;
                    float t = Mathf.Clamp01(pElapsed / pDur);
                    float sX = Mathf.Lerp(1.14f, 1f, t);
                    float sY = Mathf.Lerp(1.18f, 1f, t);
                    rowRoot.localScale = new Vector3(sX, sY, 1f);
                    if (rowBar != null) rowBar.color = Color.Lerp(flashColor, baseBarColor, t);
                    yield return null;
                }

                rowRoot.localScale = Vector3.one;
                if (rowBar != null) rowBar.color = baseBarColor;

                yield return WaitReckoningTime(0.24f);
            }
        }

        // =========================================================================
        // SPAWNING ACTUAL 3D TV DVD LOGOS & SUIKA PHYSICS ON THE RECKONING SCALE
        // =========================================================================

        private void SpawnBouncyTvDvdOnScale(bool isHope)
        {
            RectTransform container = isHope ? leftDvdContainer : rightDvdContainer;
            if (container == null) return;

            List<ScaleBouncyDvdPiece> list = isHope ? leftPanDvdPieces : rightPanDvdPieces;

            // Green combo for Hope, Red combo for Despair
            Color primaryColor = isHope
                ? new Color(0.18f, 1.00f, 0.44f, 1f)
                : new Color(1.00f, 0.18f, 0.24f, 1f);
            Color secondaryColor = isHope
                ? new Color(0.75f, 1.00f, 0.22f, 1f)
                : new Color(1.00f, 0.58f, 0.14f, 1f);

            ScaleBouncyDvdPiece piece = CreateScaleTvDvdPiece(container, isHope, primaryColor, secondaryColor);
            if (piece == null) return;

            // Spawn above the pan with slight horizontal spread so they stack & bounce naturally like Suika
            float spawnX = UnityEngine.Random.Range(-42f, 42f);
            float spawnY = 235f;
            piece.localPos = new Vector2(spawnX, spawnY);
            piece.localVel = new Vector2(UnityEngine.Random.Range(-28f, 28f), -180f);
            piece.angleZ = UnityEngine.Random.Range(-10f, 10f);
            piece.angularVelZ = UnityEngine.Random.Range(-35f, 35f);
            piece.TriggerPop(0.30f, 0.26f);
            piece.UpdateDeformation(0f);

            list.Add(piece);
        }

        private ScaleBouncyDvdPiece CreateScaleTvDvdPiece(RectTransform parentContainer, bool isHope, Color primaryCol, Color secondaryCol)
        {
            DVDLogoBouncer bouncer = DVDLogoBouncer.Instance != null ? DVDLogoBouncer.Instance : FindObjectOfType<DVDLogoBouncer>(true);
            Transform srcDvd = bouncer != null ? bouncer.transform.Find("dvd") : null;

            GameObject rootObj = new GameObject(isHope ? "TV_DVD_Hope" : "TV_DVD_Despair", typeof(RectTransform));
            int uiLayer = gameObject.layer;
            rootObj.layer = uiLayer;
            rootObj.transform.SetParent(parentContainer, false);

            ScaleBouncyDvdPiece piece = new ScaleBouncyDvdPiece
            {
                rootObj = rootObj,
                rectTransform = rootObj.GetComponent<RectTransform>(),
                isHopeSide = isHope,
                halfWidth = 34f,
                halfHeight = 19f
            };

            if (bouncer != null && srcDvd != null)
            {
                // Clone the ACTUAL TV DVD Logo mesh hierarchy ('dvd' + 'dvd/Torus') from DVDLogoBouncer!
                List<DVDLogoBouncer.DeformableMeshPart> sourceParts = bouncer.GetDeformableParts();
                Vector3 initialBouncerScale = bouncer.GetInitialLocalScale();
                Vector2 bouncerHalfSize = bouncer.GetBaseHalfSize();
                Vector3 bouncerCenterOffset = bouncer.GetBaseCenterOffset();

                // Scale so the 3D DVD logo is ~68 UI pixels wide inside the scale pan, with thin Z depth for ScreenSpaceCamera
                float targetUiWidth = 68f;
                float rawWidth = Mathf.Max(0.01f, bouncerHalfSize.x * 2f);
                float scaleRatio = targetUiWidth / rawWidth;

                piece.baseScale = new Vector3(
                    initialBouncerScale.x * scaleRatio,
                    initialBouncerScale.y * scaleRatio,
                    initialBouncerScale.z * scaleRatio * 0.04f);
                piece.baseCenterOffset = new Vector3(
                    bouncerCenterOffset.x * scaleRatio,
                    bouncerCenterOffset.y * scaleRatio,
                    0f);
                piece.rootDeformCenter = bouncer.GetRootDeformCenter();
                piece.halfWidth = targetUiWidth * 0.48f;
                piece.halfHeight = Mathf.Max(16f, bouncerHalfSize.y * scaleRatio * 0.92f);

                rootObj.transform.localScale = piece.baseScale;

                GameObject dvdClone = Instantiate(srcDvd.gameObject, rootObj.transform);
                dvdClone.name = "dvd";
                dvdClone.transform.localPosition = srcDvd.localPosition;
                dvdClone.transform.localRotation = srcDvd.localRotation;
                dvdClone.transform.localScale = srcDvd.localScale;
                SetLayerRecursively(dvdClone, uiLayer);

                Collider[] cols = dvdClone.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null) Destroy(cols[i]);
                }

                Renderer primaryRend = dvdClone.GetComponent<Renderer>();
                if (primaryRend != null)
                {
                    primaryRend.enabled = true;
                    primaryRend.sharedMaterial = CreateScaleDvdUnlitMaterial(primaryCol);
                    primaryRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    primaryRend.receiveShadows = false;
                }

                Transform torusClone = dvdClone.transform.Find("Torus");
                if (torusClone != null)
                {
                    Renderer secondaryRend = torusClone.GetComponent<Renderer>();
                    if (secondaryRend != null)
                    {
                        secondaryRend.enabled = true;
                        secondaryRend.sharedMaterial = CreateScaleDvdUnlitMaterial(secondaryCol);
                        secondaryRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        secondaryRend.receiveShadows = false;
                    }
                }

                MeshFilter[] cloneFilters = dvdClone.GetComponentsInChildren<MeshFilter>(false);
                for (int i = 0; i < cloneFilters.Length && i < sourceParts.Count; i++)
                {
                    MeshFilter mf = cloneFilters[i];
                    DVDLogoBouncer.DeformableMeshPart srcPart = sourceParts[i];
                    if (mf == null || srcPart == null || srcPart.meshInstance == null) continue;

                    Mesh meshInst = Instantiate(srcPart.meshInstance);
                    meshInst.name = srcPart.meshInstance.name + "_ScaleDVD";
                    meshInst.MarkDynamic();

                    int vCount = srcPart.originalLocalVerts.Length;
                    Vector3[] origCopy = new Vector3[vCount];
                    Vector3[] rootCopy = new Vector3[vCount];
                    Vector3[] defCopy = new Vector3[vCount];

                    Array.Copy(srcPart.originalLocalVerts, origCopy, vCount);
                    Array.Copy(srcPart.rootSpaceVerts, rootCopy, vCount);
                    Array.Copy(srcPart.originalLocalVerts, defCopy, vCount);

                    meshInst.vertices = defCopy;
                    meshInst.RecalculateBounds();
                    mf.mesh = meshInst;

                    piece.meshParts.Add(new ScaleBouncyDvdPiece.MeshPartBuffer
                    {
                        filter = mf,
                        mesh = meshInst,
                        origLocalVerts = origCopy,
                        rootSpaceVerts = rootCopy,
                        deformedVerts = defCopy,
                        rootToPart = srcPart.rootToPartMatrix
                    });
                }
            }
            else
            {
                // Fallback 2D Sprite if DVDLogoBouncer is not in scene
                EnsureDvdSprites();
                Image img = rootObj.AddComponent<Image>();
                img.sprite = isHope ? greenDvdSprite : redDvdSprite;
                img.raycastTarget = false;
                piece.rectTransform.sizeDelta = new Vector2(68f, 36f);
                piece.baseScale = Vector3.one;
            }

            return piece;
        }

        private Material CreateScaleDvdUnlitMaterial(Color color)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            if (s == null) s = Shader.Find("Sprites/Default");
            Material m = new Material(s);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private void UpdatePanDvdPhysics(List<ScaleBouncyDvdPiece> pieces, bool isHopeSide, float dt)
        {
            if (pieces == null || pieces.Count == 0) return;

            const float gravity = -1550f;
            const float panFloorY = 12f;
            const float panHalfWidth = 88f;

            for (int i = 0; i < pieces.Count; i++)
            {
                ScaleBouncyDvdPiece p = pieces[i];
                if (p == null || p.rootObj == null) continue;

                p.localVel.y += gravity * dt;
                p.localPos += p.localVel * dt;
                p.angleZ = Mathf.Clamp(p.angleZ + p.angularVelZ * dt, -18f, 18f);
                p.angularVelZ *= Mathf.Pow(0.15f, dt);

                // Pan Floor Collision with bouncy Suika squash!
                if (p.localPos.y - p.halfHeight < panFloorY)
                {
                    p.localPos.y = panFloorY + p.halfHeight;
                    float impactSpeed = Mathf.Abs(p.localVel.y);

                    if (!p.hasLandedOnce)
                    {
                        p.hasLandedOnce = true;
                        OnDvdFirstLandedOnPan(isHopeSide);
                    }

                    if (impactSpeed > 65f)
                    {
                        p.localVel.y = impactSpeed * 0.46f;
                        p.localVel.x *= 0.78f;
                        float sq = Mathf.Clamp(impactSpeed / 520f, 0.10f, 0.36f);
                        p.TriggerSquash(Vector2.up, sq, 0.42f);
                    }
                    else
                    {
                        p.localVel.y = 0f;
                        p.localVel.x *= Mathf.Pow(0.05f, dt);
                    }
                    p.angleZ = Mathf.Lerp(p.angleZ, 0f, Mathf.Clamp01(dt * 12f));
                }

                // Pan Left/Right Rim Wall Collisions
                if (p.localPos.x - p.halfWidth < -panHalfWidth)
                {
                    p.localPos.x = -panHalfWidth + p.halfWidth;
                    p.localVel.x = Mathf.Abs(p.localVel.x) * 0.55f;
                    if (Mathf.Abs(p.localVel.x) > 35f) p.TriggerSquash(Vector2.right, 0.16f, 0.32f);
                }
                else if (p.localPos.x + p.halfWidth > panHalfWidth)
                {
                    p.localPos.x = panHalfWidth - p.halfWidth;
                    p.localVel.x = -Mathf.Abs(p.localVel.x) * 0.55f;
                    if (Mathf.Abs(p.localVel.x) > 35f) p.TriggerSquash(Vector2.right, 0.16f, 0.32f);
                }
            }

            // Pairwise Elliptical Suika Collision & Bouncy Jiggle Between DVDs in the Same Pan!
            for (int iter = 0; iter < 3; iter++)
            {
                for (int i = 0; i < pieces.Count; i++)
                {
                    ScaleBouncyDvdPiece a = pieces[i];
                    if (a == null || a.rootObj == null) continue;

                    for (int j = i + 1; j < pieces.Count; j++)
                    {
                        ScaleBouncyDvdPiece b = pieces[j];
                        if (b == null || b.rootObj == null) continue;

                        float sumRx = (a.halfWidth + b.halfWidth) * 0.88f;
                        float sumRy = (a.halfHeight + b.halfHeight) * 0.88f;
                        Vector2 delta = b.localPos - a.localPos;
                        float nx = delta.x / Mathf.Max(1f, sumRx);
                        float ny = delta.y / Mathf.Max(1f, sumRy);
                        float normDistSq = nx * nx + ny * ny;

                        if (normDistSq < 1f)
                        {
                            if (!a.hasLandedOnce && b.hasLandedOnce)
                            {
                                a.hasLandedOnce = true;
                                OnDvdFirstLandedOnPan(isHopeSide);
                            }
                            else if (!b.hasLandedOnce && a.hasLandedOnce)
                            {
                                b.hasLandedOnce = true;
                                OnDvdFirstLandedOnPan(isHopeSide);
                            }

                            float normDist = Mathf.Sqrt(Mathf.Max(0.0001f, normDistSq));
                            Vector2 normal = delta.sqrMagnitude > 0.001f ? delta.normalized : Vector2.up;
                            float avgRadius = (sumRx + sumRy) * 0.5f;
                            float penetration = (1f - normDist) * avgRadius;

                            // Separate with slight bias upward so upper DVDs stack stably on lower DVDs
                            a.localPos -= normal * (penetration * 0.45f);
                            b.localPos += normal * (penetration * 0.55f);

                            Vector2 relVel = b.localVel - a.localVel;
                            float velAlongNormal = Vector2.Dot(relVel, normal);
                            if (velAlongNormal < 0f)
                            {
                                float restitution = 0.48f;
                                float impulse = -(1f + restitution) * velAlongNormal * 0.5f;
                                a.localVel -= normal * impulse;
                                b.localVel += normal * impulse;

                                if (Mathf.Abs(velAlongNormal) > 55f)
                                {
                                    float jiggle = Mathf.Clamp(Mathf.Abs(velAlongNormal) / 560f, 0.08f, 0.30f);
                                    a.TriggerSquash(normal, jiggle, 0.38f);
                                    b.TriggerSquash(-normal, jiggle, 0.38f);
                                }
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] != null) pieces[i].UpdateDeformation(dt);
            }
        }

        private void OnDvdFirstLandedOnPan(bool isHopeSide)
        {
            if (isHopeSide) currentGreenDvds++;
            else currentRedDvds++;

            if (leftWeightText != null) leftWeightText.text = $"<color=#44FF77><b>HOPE: {currentGreenDvds} DVDs</b></color>";
            if (rightWeightText != null) rightWeightText.text = $"<color=#FF4455><b>DESPAIR: {currentRedDvds} DVDs</b></color>";

            scaleAngularVelocity += isHopeSide ? 62f : -62f;
            PlayProceduralImpactSound(isHopeSide);
        }

        private void UpdateScaleVisuals(float dt)
        {
            if (scaleBeam == null) return;

            float weightDiff = (float)(currentGreenDvds - currentRedDvds);
            scaleTargetAngle = Mathf.Clamp(weightDiff * 2.3f, -16f, 16f);

            float stiffness = 24f;
            float damping = 5.0f;
            float torque = (scaleTargetAngle - scaleCurrentAngle) * stiffness;
            scaleAngularVelocity += torque * dt;
            scaleAngularVelocity -= scaleAngularVelocity * damping * dt;
            scaleCurrentAngle += scaleAngularVelocity * dt;

            scaleBeam.localEulerAngles = new Vector3(0f, 0f, scaleCurrentAngle);

            float rad = scaleCurrentAngle * Mathf.Deg2Rad;
            float halfBeam = 205f;
            Vector2 beamPivot = scaleBeam.anchoredPosition;
            Vector2 leftTip = beamPivot + new Vector2(-halfBeam * Mathf.Cos(rad), -halfBeam * Mathf.Sin(rad));
            Vector2 rightTip = beamPivot + new Vector2(halfBeam * Mathf.Cos(rad), halfBeam * Mathf.Sin(rad));

            float chainDrop = 115f;
            if (leftPan != null)
            {
                leftPan.anchoredPosition = new Vector2(leftTip.x, leftTip.y - chainDrop);
                leftPan.localRotation = Quaternion.identity;
            }
            if (rightPan != null)
            {
                rightPan.anchoredPosition = new Vector2(rightTip.x, rightTip.y - chainDrop);
                rightPan.localRotation = Quaternion.identity;
            }

            UpdateChainLine(leftChainA, leftTip, leftPan, -78f);
            UpdateChainLine(leftChainB, leftTip, leftPan, 78f);
            UpdateChainLine(rightChainA, rightTip, rightPan, -78f);
            UpdateChainLine(rightChainB, rightTip, rightPan, 78f);
        }

        private void UpdateChainLine(RectTransform chain, Vector2 tip, RectTransform pan, float panOffsetX)
        {
            if (chain == null || pan == null) return;
            Vector2 panAttach = pan.anchoredPosition + new Vector2(panOffsetX, 12f);
            Vector2 dir = panAttach - tip;
            float dist = dir.magnitude;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            chain.anchoredPosition = tip;
            chain.sizeDelta = new Vector2(dist, 2.2f);
            chain.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        private IEnumerator WaitReckoningTime(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime * reckoningSpeedMultiplier;
                yield return null;
            }
        }

        private void EnsureDvdSprites()
        {
            if (greenDvdSprite == null)
            {
                Color greenLetters = new Color(0.22f, 1f, 0.35f, 1f);
                Color greenRing = new Color(0.0f, 0.80f, 0.45f, 1f);
                greenDvdSprite = GenerateDvdLogoSprite(greenLetters, greenRing, "GreenDvdLogo");
            }
            if (redDvdSprite == null)
            {
                Color redLetters = new Color(1.0f, 0.16f, 0.22f, 1f);
                Color redRing = new Color(0.70f, 0.05f, 0.15f, 1f);
                redDvdSprite = GenerateDvdLogoSprite(redLetters, redRing, "RedDvdLogo");
            }
        }

        private Sprite GenerateDvdLogoSprite(Color lettersColor, Color ringColor, string spriteName)
        {
            int width = 160;
            int height = 90;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[width * height];
            Color transparent = new Color(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;

            float ringCenterX = width * 0.5f;
            float ringCenterY = height * 0.52f;
            float outerRadiusX = width * 0.44f;
            float outerRadiusY = height * 0.30f;
            float innerRadiusX = width * 0.28f;
            float innerRadiusY = height * 0.17f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - ringCenterX;
                    float dy = y - ringCenterY;
                    float outerDist = (dx * dx) / (outerRadiusX * outerRadiusX) + (dy * dy) / (outerRadiusY * outerRadiusY);
                    float innerDist = (dx * dx) / (innerRadiusX * innerRadiusX) + (dy * dy) / (innerRadiusY * innerRadiusY);

                    if (outerDist <= 1.0f && innerDist >= 0.75f)
                    {
                        float normY = (dy / outerRadiusY);
                        float shade = Mathf.Clamp01(0.75f + normY * 0.35f);
                        Color c = ringColor * shade;
                        c.a = ringColor.a * Mathf.Clamp01((1.0f - outerDist) * 8f) * Mathf.Clamp01((innerDist - 0.75f) * 8f);
                        pixels[y * width + x] = c;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        public void OnReckoningContinueClicked()
        {
            if (reckoningAnimationRoutine != null)
            {
                StopCoroutine(reckoningAnimationRoutine);
                reckoningAnimationRoutine = null;
            }

            ClearScaleDvdPieces();
            PlayProceduralBeep(440f, 0.08f);

            if (RunSaveManager.HasInstance)
            {
                RunSaveManager.Instance.finalPhoneEncounterDone = true;
                RunSaveManager.Instance.isFinalVerdictHopeful = lastVerdictHopeful;
                RunSaveManager.Instance.finalVerdictObjective = lastVerdictHopeful
                    ? "Go to the front door to reach out"
                    : "Go into the room to the TV to end it all";
            }

            ApplyEndgameVerdictToScene(lastVerdictHopeful);

            if (reckoningPanel != null) reckoningPanel.SetActive(false);
            CloseCard();
        }

        private void ApplyEndgameVerdictToScene(bool isHopeful)
        {
            string destText = isHopeful
                ? "GO TO THE FRONT DOOR TO STEP OUTSIDE AND REACH OUT"
                : "GO INTO THE ROOM TO THE TELEVISION TO END IT ALL";

            Debug.Log($"[StoryCardController] Final Reckoning Complete! Verdict: {(isHopeful ? "HOPE" : "DESPAIR")}. Player told to: {destText}");

            SceneTransitionOut[] transitions = FindObjectsOfType<SceneTransitionOut>(true);
            foreach (var trans in transitions)
            {
                if (trans != null && trans.sceneToLoad == "BarGameBarni")
                {
                    InteractableObject io = trans.GetComponent<InteractableObject>();
                    if (io != null)
                    {
                        io.hoverText = isHopeful
                            ? "Front Door: Reach Out (Step Outside)"
                            : "Front Door (Locked: Return to TV)";
                    }
                }
            }

            UpgradeManager upg = FindObjectOfType<UpgradeManager>(true);
            if (upg != null)
            {
                InteractableObject tvIo = upg.GetComponentInChildren<InteractableObject>(true);
                if (tvIo != null && !isHopeful)
                {
                    tvIo.hoverText = "Television: End It All (Surrender)";
                }
            }

            GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
            if (crawler != null && crawler.interactTextUI != null)
            {
                crawler.StartCoroutine(ShowTemporaryVerdictRoutine(crawler, isHopeful ? "VERDICT: REACH OUT - GO TO FRONT DOOR" : "VERDICT: SURRENDER - RETURN TO TV IN ROOM"));
            }
        }

        private IEnumerator ShowTemporaryVerdictRoutine(GridCrawlerController crawler, string msg)
        {
            if (crawler.interactTextUI == null) yield break;
            float elapsed = 0f;
            crawler.interactTextUI.text = msg;
            crawler.interactTextUI.color = Color.yellow;
            crawler.interactTextUI.transform.localScale = Vector3.one;

            while (elapsed < 6.0f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            crawler.interactTextUI.color = Color.white;
        }

        // =========================================================================
        // CARD INTERACTION & CHOICES
        // =========================================================================

        private void PopulateCardUI(StoryCardData card)
        {
            if (card == null) return;

            if (contactTitleText != null) contactTitleText.text = $"☎ {card.contactName.ToUpperInvariant()}";
            if (contactRoleText != null) contactRoleText.text = $"● {card.contactRole.ToUpperInvariant()} — HOLD & SWIPE CARD";
            if (narrativeBodyText != null) narrativeBodyText.text = $"\"{card.narrativeText}\"";

            string lLabel = !string.IsNullOrEmpty(card.leftLabel) ? card.leftLabel.ToUpperInvariant() : "REJECT";
            string rLabel = !string.IsNullOrEmpty(card.rightLabel) ? card.rightLabel.ToUpperInvariant() : "ACCEPT";
            string lPrompt = !string.IsNullOrEmpty(card.leftChoicePrompt) ? card.leftChoicePrompt : "Decline";
            string rPrompt = !string.IsNullOrEmpty(card.rightChoicePrompt) ? card.rightChoicePrompt : "Accept";

            if (leftSwipeText != null)
            {
                leftSwipeText.text = $"<size=30><b>◀ ◀</b></size>\n<size=22><b>{lLabel}</b></size>\n<size=15><color=#ffb8b8>{lPrompt}</color></size>";
            }
            if (rightSwipeText != null)
            {
                rightSwipeText.text = $"<size=30><b>▶ ▶</b></size>\n<size=22><b>{rLabel}</b></size>\n<size=15><color=#b8ffcc>{rPrompt}</color></size>";
            }

            if (swipeHintFooterText != null)
            {
                swipeHintFooterText.text = $"◀ SWIPE LEFT: {lLabel}    •    SWIPE RIGHT: {rLabel} ▶";
            }

            if (portraitImage != null)
            {
                portraitImage.sprite = card.portraitSprite;
                portraitImage.color = card.portraitSprite != null ? Color.white : new Color(0.14f, 0.22f, 0.34f, 0.95f);
            }

            UpdateAllTMPMeshGeometry();
        }

        private void UpdateAllTMPMeshGeometry()
        {
            TMP_Text[] allTexts = GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in allTexts)
            {
                if (t != null)
                {
                    EnsureFontOnTMP(t);
                    t.ForceMeshUpdate(true);
                }
            }
        }

        public void ChooseLeft()
        {
            if (hasChosen || currentCard == null) return;
            hasChosen = true;
            PlayProceduralBeep(320f, 0.12f);
            StartCoroutine(AnimateCardExitAndRevealOutcome(isLeft: true));
        }

        public void ChooseRight()
        {
            if (hasChosen || currentCard == null) return;
            hasChosen = true;
            PlayProceduralBeep(640f, 0.12f);
            StartCoroutine(AnimateCardExitAndRevealOutcome(isLeft: false));
        }

        private IEnumerator AnimateCardExitAndRevealOutcome(bool isLeft)
        {
            float targetX = isLeft ? -820f : 820f;
            float targetRot = isLeft ? 25f : -25f;
            Vector2 startPos = cardContainer != null ? cardContainer.anchoredPosition : Vector2.zero;
            float startRot = cardContainer != null ? cardContainer.localRotation.eulerAngles.z : 0f;
            if (startRot > 180f) startRot -= 360f;

            float elapsed = 0f;
            float dur = 0.22f;

            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / dur);
                if (cardContainer != null)
                {
                    cardContainer.anchoredPosition = Vector2.Lerp(startPos, new Vector2(targetX, startPos.y - 40f), t);
                    cardContainer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRot, targetRot, t));
                }
                yield return null;
            }

            if (cardContainer != null) cardContainer.gameObject.SetActive(false);

            DisplayConsequence(isLeft);
        }

        private void DisplayConsequence(bool isLeft)
        {
            if (currentCard == null) return;
            isExitingConsequence = false;
            cardInputCooldown = 0.12f;

            string outcomeText = isLeft ? currentCard.leftOutcomeText : currentCard.rightOutcomeText;
            decimal moneyDelta = (decimal)(isLeft ? currentCard.leftMoneyDelta : currentCard.rightMoneyDelta);
            float depDelta = isLeft ? currentCard.leftDepressionDelta : currentCard.rightDepressionDelta;
            float tiredDelta = isLeft ? currentCard.leftTirednessDelta : currentCard.rightTirednessDelta;
            bool isPositive = isLeft ? currentCard.leftIsPositive : currentCard.rightIsPositive;

            if (RunSaveManager.HasInstance)
            {
                if (isPositive) RunSaveManager.Instance.positiveSecretCounter++;
                else RunSaveManager.Instance.negativeSecretCounter++;
                RunSaveManager.Instance.RecordStoryElementEncountered(currentCard.id);
            }

            if (moneyDelta != 0m)
            {
                DVDLogoBouncer bouncer = DVDLogoBouncer.Instance != null ? DVDLogoBouncer.Instance : FindObjectOfType<DVDLogoBouncer>();
                if (bouncer != null)
                {
                    if (moneyDelta > 0m) bouncer.AddMoney(moneyDelta);
                    else bouncer.SpendMoney(Math.Abs(moneyDelta));
                }
            }

            if (BarManager.Instance != null)
            {
                if (depDelta != 0f)
                {
                    BarManager.Instance.currentDepression = Mathf.Clamp(BarManager.Instance.currentDepression + depDelta, 0f, BarManager.Instance.maxDepression);
                }

                if (tiredDelta > 0f) BarManager.Instance.AddTiredness(tiredDelta);
                else if (tiredDelta < 0f) BarManager.Instance.ReduceTiredness(Mathf.Abs(tiredDelta));
            }

            // Hide answer/reject side indicators on the consequence screen so only the consequence results are shown!
            HideSideIndicators();

            if (consequencePanel != null)
            {
                consequencePanel.transform.localScale = Vector3.one;
                RectTransform consRT = consequencePanel.GetComponent<RectTransform>();
                if (consRT != null)
                {
                    consRT.localScale = Vector3.one;
                    consRT.anchoredPosition = Vector2.zero;
                    consRT.localRotation = Quaternion.identity;
                }
                consequencePanel.SetActive(true);

                if (consequenceTitleText != null)
                {
                    consequenceTitleText.text = isLeft ? $"◀ CALL REJECTED: {currentCard.contactName}" : $"CALL ACCEPTED: {currentCard.contactName} ▶";
                    consequenceTitleText.color = isPositive ? new Color(0.38f, 1.0f, 0.52f) : new Color(1.0f, 0.45f, 0.38f);
                }

                if (consequenceBodyText != null) consequenceBodyText.text = $"\"{outcomeText}\"";

                if (moneyDeltaText != null)
                {
                    if (moneyDelta > 0m)
                    {
                        moneyDeltaText.text = $"CASH IMPACT:   +{CurrencyFormatter.Format(moneyDelta)}";
                        moneyDeltaText.color = new Color(0.38f, 1.0f, 0.52f);
                    }
                    else if (moneyDelta < 0m)
                    {
                        moneyDeltaText.text = $"CASH IMPACT:   -{CurrencyFormatter.Format(Math.Abs(moneyDelta))}";
                        moneyDeltaText.color = new Color(1.0f, 0.38f, 0.38f);
                    }
                    else
                    {
                        moneyDeltaText.text = "CASH IMPACT:   UNCHANGED ($0)";
                        moneyDeltaText.color = new Color(0.78f, 0.85f, 0.92f);
                    }
                }

                if (depressionDeltaText != null)
                {
                    if (depDelta < 0)
                    {
                        depressionDeltaText.text = $"MENTAL STATE:   {depDelta:0}% DEPRESSION";
                        depressionDeltaText.color = new Color(0.38f, 1.0f, 0.52f);
                    }
                    else if (depDelta > 0)
                    {
                        depressionDeltaText.text = $"MENTAL STATE:   +{depDelta:0}% DEPRESSION";
                        depressionDeltaText.color = new Color(1.0f, 0.38f, 0.38f);
                    }
                    else
                    {
                        depressionDeltaText.text = "MENTAL STATE:   UNCHANGED";
                        depressionDeltaText.color = new Color(0.78f, 0.85f, 0.92f);
                    }
                }

                if (tirednessDeltaText != null)
                {
                    if (tiredDelta < 0)
                    {
                        tirednessDeltaText.text = $"EXHAUSTION:   {tiredDelta:0} TIREDNESS";
                        tirednessDeltaText.color = new Color(0.38f, 1.0f, 0.52f);
                    }
                    else if (tiredDelta > 0)
                    {
                        tirednessDeltaText.text = $"EXHAUSTION:   +{tiredDelta:0} TIREDNESS";
                        tirednessDeltaText.color = new Color(1.0f, 0.82f, 0.35f);
                    }
                    else
                    {
                        tirednessDeltaText.text = "EXHAUSTION:   UNCHANGED";
                        tirednessDeltaText.color = new Color(0.78f, 0.85f, 0.92f);
                    }
                }

                UpdateAllTMPMeshGeometry();
            }
        }

        public void OnContinueClicked()
        {
            isDraggingConsequence = false;
            consequenceDragOffset = Vector2.zero;
            PlayProceduralBeep(440f, 0.08f);

            if (consequencePanel != null)
            {
                RectTransform consRT = consequencePanel.GetComponent<RectTransform>();
                if (consRT != null)
                {
                    consRT.anchoredPosition = Vector2.zero;
                    consRT.localRotation = Quaternion.identity;
                    consRT.localScale = Vector3.one;
                }
                consequencePanel.SetActive(false);
            }

            CloseCard();
        }

        private void EnableCameraAndHudIsolation()
        {
            GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
            if (crawler != null)
            {
                crawler.SetHudCropPanelVisible(false);
                crawler.SetFocusCursorVisuals(false, false, "");
            }

            if (targetCamera != null && !hasSavedCameraIsolation)
            {
                savedCameraCullingMask = targetCamera.cullingMask;
                savedCameraClearFlags = targetCamera.clearFlags;
                savedCameraBgColor = targetCamera.backgroundColor;
                hasSavedCameraIsolation = true;

                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer < 0) uiLayer = 5;
                targetCamera.cullingMask = (1 << uiLayer) | (1 << gameObject.layer);
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 1f);
            }
        }

        private void RestoreCameraAndHudIsolation()
        {
            GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
            if (crawler != null)
            {
                crawler.SetHudCropPanelVisible(true);
            }

            if (hasSavedCameraIsolation && targetCamera != null)
            {
                targetCamera.cullingMask = savedCameraCullingMask;
                targetCamera.clearFlags = savedCameraClearFlags;
                targetCamera.backgroundColor = savedCameraBgColor;
            }
            hasSavedCameraIsolation = false;
        }

        private void OnDisable()
        {
            if (hasSavedCameraIsolation)
            {
                RestoreCameraAndHudIsolation();
            }
        }

        private void OnDestroy()
        {
            if (hasSavedCameraIsolation)
            {
                RestoreCameraAndHudIsolation();
            }
        }

        private void CloseCard()
        {
            isCardActive = false;
            hasChosen = false;
            isDragging = false;
            isDraggingConsequence = false;
            currentDragOffset = Vector2.zero;
            consequenceDragOffset = Vector2.zero;
            isExitingConsequence = false;

            ClearScaleDvdPieces();
            HideSideIndicators();
            RestoreCameraAndHudIsolation();

            if (consequencePanel != null) consequencePanel.SetActive(false);
            if (reckoningPanel != null) reckoningPanel.SetActive(false);
            gameObject.SetActive(false);

            if (!PauseManager.IsGamePaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
            if (crawler != null)
            {
                crawler.SetFocusCursorVisuals(true, false, "");
            }

            Action callback = onCardFinished;
            onCardFinished = null;
            callback?.Invoke();
        }

        private void EnsureCursorVisible()
        {
            // Keep OS cursor LOCKED so it never leaves the Unity Editor Game View tab!
            if (!PauseManager.IsGamePaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
            if (crawler != null)
            {
                crawler.SetFocusCursorVisuals(false, false, "");
            }
            UpdatePhoneVirtualCursor();
        }

        private void EnsureEventSystemActive()
        {
            EventSystem es = EventSystem.current;
            if (es == null) es = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (es != null)
            {
                if (!es.gameObject.activeSelf) es.gameObject.SetActive(true);
                es.enabled = true;
                BaseInputModule module = es.GetComponent<BaseInputModule>();
                if (module != null && !module.enabled) module.enabled = true;
            }
        }

        public StoryCardData GetNextCard()
        {
            StoryManager sm = StoryManager.Instance != null ? StoryManager.Instance : FindObjectOfType<StoryManager>(true);
            if (sm != null)
            {
                return sm.GetNextCall();
            }

            return new StoryCardData
            {
                id = "card_mom",
                contactName = "MOM",
                contactRole = "Incoming Call",
                narrativeText = "Sweetheart, are you eating well?",
                leftLabel = "DISMISS",
                leftChoicePrompt = "I'm busy",
                leftOutcomeText = "You hung up cold.",
                leftDepressionDelta = 8f,
                rightLabel = "THANKS MOM",
                rightChoicePrompt = "I'm trying",
                rightOutcomeText = "A rare warmth washes over you.",
                rightDepressionDelta = -15f,
                rightMoneyDelta = 50f,
                rightIsPositive = true
            };
        }

        // =========================================================================
        // PROCEDURAL CRT AUDIO
        // =========================================================================

        private void EnsureUIAudio()
        {
            if (uiAudio == null)
            {
                uiAudio = GetComponent<AudioSource>();
                if (uiAudio == null) uiAudio = gameObject.AddComponent<AudioSource>();
                uiAudio.playOnAwake = false;
                uiAudio.spatialBlend = 0f;
                uiAudio.ignoreListenerPause = true;
                uiAudio.ignoreListenerVolume = true;
            }
        }

        private void PlayProceduralBeep(float freq, float duration)
        {
            EnsureUIAudio();
            if (uiAudio == null) return;

            int sampleRate = 44100;
            int sampleCount = (int)(sampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float env = 1f - (float)i / sampleCount;
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.28f;
            }

            AudioClip clip = AudioClip.Create("PhoneBeep", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            uiAudio.PlayOneShot(clip, 0.8f);
        }

        // =========================================================================
        // SCREEN SPACE CAMERA & CLEAN NON-OVERLAPPING UI HIERARCHY
        // =========================================================================

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (targetCamera != null && targetCamera.targetTexture == null)
            {
                Canvas c = GetComponent<Canvas>();
                if (c != null && c.worldCamera != targetCamera)
                {
                    c.worldCamera = targetCamera;
                }
            }
            else
            {
                Canvas c = GetComponent<Canvas>();
                if (c != null && c.worldCamera != null && c.worldCamera.targetTexture == null)
                {
                    targetCamera = c.worldCamera;
                }
            }
        }
#endif

        public void SetupScreenSpaceCamera(Canvas canvas)
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            if (canvas == null) return;

            Camera cam = null;
            if (targetCamera != null && targetCamera.targetTexture == null)
            {
                cam = targetCamera;
            }
            else if (canvas.worldCamera != null && canvas.worldCamera.targetTexture == null)
            {
                cam = canvas.worldCamera;
            }

            if (cam == null)
            {
                SmoothCameraFollow scf = FindAnyObjectByType<SmoothCameraFollow>(FindObjectsInactive.Include);
                if (scf != null)
                {
                    Camera scfCam = scf.GetComponent<Camera>();
                    if (scfCam != null && scfCam.targetTexture == null)
                    {
                        cam = scfCam;
                    }
                }
            }

            if (cam == null)
            {
                Camera main = Camera.main;
                if (main != null && main.targetTexture == null)
                {
                    cam = main;
                }
            }

            if (cam == null)
            {
                Camera[] allCams = Resources.FindObjectsOfTypeAll<Camera>();
                foreach (Camera c in allCams)
                {
                    if (c == null || c.targetTexture != null || c.cameraType != CameraType.Game) continue;
                    if (c.gameObject.scene.name != gameObject.scene.name) continue;
                    if (c.CompareTag("MainCamera"))
                    {
                        cam = c;
                        break;
                    }
                    if (cam == null) cam = c;
                }
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            if (cam != null)
            {
                targetCamera = cam;
                canvas.worldCamera = cam;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

                float safeDistance = Mathf.Clamp(cam.nearClipPlane + 0.03f, 0.08f, 0.12f);
                canvas.planeDistance = safeDistance;

                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer < 0) uiLayer = 5;
                cam.cullingMask |= (1 << uiLayer) | (1 << gameObject.layer);

                UniversalAdditionalCameraData camData = cam.GetComponent<UniversalAdditionalCameraData>();
                if (camData == null) camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                if (camData != null) camData.renderPostProcessing = true;
            }
            else
            {
                canvas.planeDistance = 0.08f;
            }

            canvas.sortingOrder = 32767;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = gameObject.AddComponent<GraphicRaycaster>();
            raycaster.enabled = true;
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            raycaster.ignoreReversedGraphics = true;

            int targetLayer = LayerMask.NameToLayer("UI");
            if (targetLayer >= 0)
            {
                SetLayerRecursively(gameObject, targetLayer);
            }
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                Transform child = obj.transform.GetChild(i);
                if (child != null) SetLayerRecursively(child.gameObject, layer);
            }
        }

        public void EnsureUIHierarchy()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            SetupScreenSpaceCamera(canvas);

            // Opaque dark CRT terminal backdrop so the 3D background/HUD is never visible behind the phone UI, while keeping gameplay CRT brightness!
            Transform bgTrans = transform.Find("DarkVignetteBackdrop");
            if (bgTrans == null)
            {
                GameObject bgObj = new GameObject("DarkVignetteBackdrop", typeof(RectTransform), typeof(Image));
                bgObj.transform.SetParent(transform, false);
                bgTrans = bgObj.transform;
            }
            RectTransform bgRT = bgTrans.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.pivot = new Vector2(0.5f, 0.5f);
            bgRT.sizeDelta = Vector2.zero;
            bgRT.anchoredPosition = Vector2.zero;
            bgRT.localScale = Vector3.one;
            Image bgImg = bgTrans.GetComponent<Image>() ?? bgTrans.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.03f, 0.05f, 0.08f, 0.99f);
            bgImg.raycastTarget = false;
            bgTrans.SetAsFirstSibling();

            // 1. Left and Right Swipe Visual Indicators (Brighter & More Satisfying)
            if (leftSwipeIndicator == null)
            {
                Transform t = transform.Find("LeftSwipeIndicator");
                if (t != null) leftSwipeIndicator = t.GetComponent<RectTransform>();
            }
            if (leftSwipeIndicator == null)
            {
                GameObject leftObj = new GameObject("LeftSwipeIndicator", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
                leftObj.transform.SetParent(transform, false);
                leftSwipeIndicator = leftObj.GetComponent<RectTransform>();
            }
            leftSwipeIndicator.anchorMin = new Vector2(0f, 0.5f);
            leftSwipeIndicator.anchorMax = new Vector2(0f, 0.5f);
            leftSwipeIndicator.pivot = new Vector2(0f, 0.5f);
            leftSwipeIndicator.sizeDelta = new Vector2(280f, 180f);
            leftSwipeIndicator.anchoredPosition = new Vector2(54f, 0f);
            leftSwipeIndicator.localScale = Vector3.one;
            Image leftImg = leftSwipeIndicator.GetComponent<Image>() ?? leftSwipeIndicator.gameObject.AddComponent<Image>();
            leftImg.color = new Color(0.28f, 0.07f, 0.11f, 0.94f);
            leftImg.raycastTarget = true;
            leftSwipeGroup = leftSwipeIndicator.GetComponent<CanvasGroup>() ?? leftSwipeIndicator.gameObject.AddComponent<CanvasGroup>();
            leftSwipeGroup.blocksRaycasts = true;
            leftSwipeGroup.interactable = true;
            Button leftBtn = leftSwipeIndicator.GetComponent<Button>() ?? leftSwipeIndicator.gameObject.AddComponent<Button>();
            leftBtn.onClick.RemoveAllListeners();
            leftBtn.onClick.AddListener(() => ChooseLeft());
            leftSwipeText = CreateTMPText("Label", leftSwipeIndicator, Vector2.zero, new Vector2(256f, 160f), 22f, TextAlignmentOptions.Center, new Color(1f, 0.48f, 0.48f));
            leftSwipeText.rectTransform.sizeDelta = new Vector2(256f, 160f);
            leftSwipeText.rectTransform.anchoredPosition = Vector2.zero;

            if (rightSwipeIndicator == null)
            {
                Transform t = transform.Find("RightSwipeIndicator");
                if (t != null) rightSwipeIndicator = t.GetComponent<RectTransform>();
            }
            if (rightSwipeIndicator == null)
            {
                GameObject rightObj = new GameObject("RightSwipeIndicator", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
                rightObj.transform.SetParent(transform, false);
                rightSwipeIndicator = rightObj.GetComponent<RectTransform>();
            }
            rightSwipeIndicator.anchorMin = new Vector2(1f, 0.5f);
            rightSwipeIndicator.anchorMax = new Vector2(1f, 0.5f);
            rightSwipeIndicator.pivot = new Vector2(1f, 0.5f);
            rightSwipeIndicator.sizeDelta = new Vector2(280f, 180f);
            rightSwipeIndicator.anchoredPosition = new Vector2(-54f, 0f);
            rightSwipeIndicator.localScale = Vector3.one;
            Image rightImg = rightSwipeIndicator.GetComponent<Image>() ?? rightSwipeIndicator.gameObject.AddComponent<Image>();
            rightImg.color = new Color(0.07f, 0.28f, 0.14f, 0.94f);
            rightImg.raycastTarget = true;
            rightSwipeGroup = rightSwipeIndicator.GetComponent<CanvasGroup>() ?? rightSwipeIndicator.gameObject.AddComponent<CanvasGroup>();
            rightSwipeGroup.blocksRaycasts = true;
            rightSwipeGroup.interactable = true;
            Button rightBtn = rightSwipeIndicator.GetComponent<Button>() ?? rightSwipeIndicator.gameObject.AddComponent<Button>();
            rightBtn.onClick.RemoveAllListeners();
            rightBtn.onClick.AddListener(() => ChooseRight());
            rightSwipeText = CreateTMPText("Label", rightSwipeIndicator, Vector2.zero, new Vector2(256f, 160f), 22f, TextAlignmentOptions.Center, new Color(0.48f, 1f, 0.58f));
            rightSwipeText.rectTransform.sizeDelta = new Vector2(256f, 160f);
            rightSwipeText.rectTransform.anchoredPosition = Vector2.zero;

            // 2. Card Container Template (Sleek 600 x 790 CRT Telephone Card with Outer Glow Border & Bottom Dual-Choice Pills)
            if (cardContainer == null)
            {
                Transform existing = transform.Find("CardContainer");
                if (existing != null) cardContainer = existing.GetComponent<RectTransform>();
            }
            if (cardContainer == null)
            {
                GameObject cardObj = new GameObject("CardContainer", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
                cardObj.transform.SetParent(transform, false);
                cardContainer = cardObj.GetComponent<RectTransform>();
            }
            cardContainer.anchorMin = new Vector2(0.5f, 0.5f);
            cardContainer.anchorMax = new Vector2(0.5f, 0.5f);
            cardContainer.pivot = new Vector2(0.5f, 0.5f);
            cardContainer.sizeDelta = new Vector2(600f, 790f);
            if (!isDragging) cardContainer.anchoredPosition = Vector2.zero;
            cardContainer.localScale = Vector3.one;
            Image cardBg = cardContainer.GetComponent<Image>() ?? cardContainer.gameObject.AddComponent<Image>();
            cardBg.color = new Color(0.10f, 0.15f, 0.23f, 0.98f);
            cardBg.raycastTarget = false;

            cardCanvasGroup = cardContainer.GetComponent<CanvasGroup>() ?? cardContainer.gameObject.AddComponent<CanvasGroup>();
            cardCanvasGroup.blocksRaycasts = false;
            cardCanvasGroup.interactable = false;

            // Outer Glowing Frame on CardContainer
            Transform glowBorderT = cardContainer.Find("CardOuterGlowBorder");
            if (glowBorderT == null)
            {
                GameObject gbObj = new GameObject("CardOuterGlowBorder", typeof(RectTransform), typeof(Image));
                gbObj.transform.SetParent(cardContainer, false);
                glowBorderT = gbObj.transform;
            }
            glowBorderT.SetAsFirstSibling();
            RectTransform gbRT = glowBorderT.GetComponent<RectTransform>();
            gbRT.anchorMin = Vector2.zero;
            gbRT.anchorMax = Vector2.one;
            gbRT.offsetMin = new Vector2(-4f, -4f);
            gbRT.offsetMax = new Vector2(4f, 4f);
            cardOuterGlowBorder = glowBorderT.GetComponent<Image>() ?? glowBorderT.gameObject.AddComponent<Image>();
            cardOuterGlowBorder.color = new Color(0.28f, 0.88f, 1.0f, 0.95f);
            cardOuterGlowBorder.raycastTarget = false;

            Transform innerPlateT = glowBorderT.Find("CardInnerPlate");
            if (innerPlateT == null)
            {
                GameObject innerPlate = new GameObject("CardInnerPlate", typeof(RectTransform), typeof(Image));
                innerPlate.transform.SetParent(glowBorderT, false);
                innerPlateT = innerPlate.transform;
            }
            RectTransform ipRT = innerPlateT.GetComponent<RectTransform>();
            ipRT.anchorMin = Vector2.zero;
            ipRT.anchorMax = Vector2.one;
            ipRT.offsetMin = new Vector2(4f, 4f);
            ipRT.offsetMax = new Vector2(-4f, -4f);
            Image ipImg = innerPlateT.GetComponent<Image>() ?? innerPlateT.gameObject.AddComponent<Image>();
            ipImg.color = new Color(0.09f, 0.14f, 0.21f, 0.99f);
            ipImg.raycastTarget = false;

            Transform headerTrans = cardContainer.Find("HeaderPanel");
            if (headerTrans == null)
            {
                GameObject headerObj = new GameObject("HeaderPanel", typeof(RectTransform), typeof(Image));
                headerObj.transform.SetParent(cardContainer, false);
                headerTrans = headerObj.transform;
            }
            RectTransform headerRT = headerTrans.GetComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0f, 1f);
            headerRT.anchorMax = new Vector2(1f, 1f);
            headerRT.pivot = new Vector2(0.5f, 1f);
            headerRT.sizeDelta = new Vector2(0f, 102f);
            headerRT.anchoredPosition = Vector2.zero;
            Image hImg = headerTrans.GetComponent<Image>() ?? headerTrans.gameObject.AddComponent<Image>();
            hImg.color = new Color(0.14f, 0.23f, 0.34f, 1f);
            hImg.raycastTarget = false;

            contactTitleText = CreateTMPText("ContactTitle", headerTrans, new Vector2(0f, -14f), new Vector2(540f, 42f), 30f, TextAlignmentOptions.Center, new Color(0.45f, 1.0f, 0.62f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            contactTitleText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            contactTitleText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            contactTitleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            contactTitleText.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            contactTitleText.rectTransform.sizeDelta = new Vector2(540f, 42f);

            contactRoleText = CreateTMPText("ContactRole", headerTrans, new Vector2(0f, -58f), new Vector2(540f, 28f), 16f, TextAlignmentOptions.Center, new Color(0.74f, 0.88f, 1.0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            contactRoleText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            contactRoleText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            contactRoleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            contactRoleText.rectTransform.anchoredPosition = new Vector2(0f, -58f);
            contactRoleText.rectTransform.sizeDelta = new Vector2(540f, 28f);

            Transform portFrameTrans = cardContainer.Find("PortraitFrame");
            if (portFrameTrans == null)
            {
                GameObject portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image));
                portraitFrame.transform.SetParent(cardContainer, false);
                portFrameTrans = portraitFrame.transform;
            }
            RectTransform portRT = portFrameTrans.GetComponent<RectTransform>();
            portRT.anchorMin = new Vector2(0.5f, 0.5f);
            portRT.anchorMax = new Vector2(0.5f, 0.5f);
            portRT.pivot = new Vector2(0.5f, 0.5f);
            portRT.sizeDelta = new Vector2(216f, 216f);
            portRT.anchoredPosition = new Vector2(0f, 130f);
            Image pfImg = portFrameTrans.GetComponent<Image>() ?? portFrameTrans.gameObject.AddComponent<Image>();
            pfImg.color = new Color(0.22f, 0.78f, 0.96f, 0.88f);
            pfImg.raycastTarget = false;

            Transform portImgT = portFrameTrans.Find("PortraitImage");
            if (portImgT == null)
            {
                GameObject portImgObj = new GameObject("PortraitImage", typeof(RectTransform), typeof(Image));
                portImgObj.transform.SetParent(portFrameTrans, false);
                portImgT = portImgObj.transform;
            }
            portraitImage = portImgT.GetComponent<Image>();
            RectTransform portImgRT = portraitImage.rectTransform;
            portImgRT.anchorMin = new Vector2(0.5f, 0.5f);
            portImgRT.anchorMax = new Vector2(0.5f, 0.5f);
            portImgRT.pivot = new Vector2(0.5f, 0.5f);
            portImgRT.sizeDelta = new Vector2(204f, 204f);
            portImgRT.anchoredPosition = Vector2.zero;
            portraitImage.raycastTarget = false;

            narrativeBodyText = CreateTMPText("NarrativeText", cardContainer, new Vector2(0f, -88f), new Vector2(510f, 185f), 22f, TextAlignmentOptions.Center, Color.white);
            narrativeBodyText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            narrativeBodyText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            narrativeBodyText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            narrativeBodyText.rectTransform.anchoredPosition = new Vector2(0f, -88f);
            narrativeBodyText.rectTransform.sizeDelta = new Vector2(510f, 185f);

            // Bottom Dual-Choice Swipe Guidance on CardContainer (SWIPE ONLY - No Buttons!)
            Transform bottomBarT = cardContainer.Find("BottomSwipePreviewBar");
            if (bottomBarT == null)
            {
                GameObject bBarObj = new GameObject("BottomSwipePreviewBar", typeof(RectTransform));
                bBarObj.transform.SetParent(cardContainer, false);
                bottomBarT = bBarObj.transform;
            }
            RectTransform bBarRT = bottomBarT.GetComponent<RectTransform>();
            bBarRT.anchorMin = new Vector2(0f, 0f);
            bBarRT.anchorMax = new Vector2(1f, 0f);
            bBarRT.pivot = new Vector2(0.5f, 0f);
            bBarRT.sizeDelta = new Vector2(0f, 75f);
            bBarRT.anchoredPosition = new Vector2(0f, 16f);

            Transform lPillT = bottomBarT.Find("LeftPreviewPill");
            if (lPillT != null) Destroy(lPillT.gameObject);
            Transform rPillT = bottomBarT.Find("RightPreviewPill");
            if (rPillT != null) Destroy(rPillT.gameObject);
            leftBottomPillRect = null;
            leftBottomPillImg = null;
            leftBottomPillText = null;
            rightBottomPillRect = null;
            rightBottomPillImg = null;
            rightBottomPillText = null;

            swipeHintFooterText = CreateTMPText("SwipeHintFooter", bottomBarT, Vector2.zero, new Vector2(560f, 54f), 18f, TextAlignmentOptions.Center, new Color(0.72f, 0.90f, 1.0f, 0.95f));
            swipeHintFooterText.rectTransform.anchoredPosition = Vector2.zero;
            swipeHintFooterText.rectTransform.sizeDelta = new Vector2(560f, 54f);
            swipeHintFooterText.text = "◀ SWIPE LEFT TO DECLINE    •    SWIPE RIGHT TO ACCEPT ▶";

            // Stamps
            Transform rejT = cardContainer.Find("RejectStamp");
            if (rejT == null)
            {
                GameObject rejObj = new GameObject("RejectStamp", typeof(RectTransform), typeof(CanvasGroup));
                rejObj.transform.SetParent(cardContainer, false);
                rejT = rejObj.transform;
            }
            rejectStamp = rejT.GetComponent<RectTransform>();
            rejectStamp.anchorMin = new Vector2(0.5f, 0.5f);
            rejectStamp.anchorMax = new Vector2(0.5f, 0.5f);
            rejectStamp.pivot = new Vector2(0.5f, 0.5f);
            rejectStamp.sizeDelta = new Vector2(190f, 56f);
            rejectStamp.anchoredPosition = new Vector2(-150f, 215f);
            rejectStamp.localRotation = Quaternion.Euler(0f, 0f, 15f);
            CreateTMPText("RejectLabel", rejectStamp, Vector2.zero, new Vector2(190f, 56f), 26f, TextAlignmentOptions.Center, new Color(1f, 0.35f, 0.35f)).text = "[ ◀ REJECT ]";
            rejectStampGroup = rejectStamp.GetComponent<CanvasGroup>() ?? rejectStamp.gameObject.AddComponent<CanvasGroup>();

            Transform accT = cardContainer.Find("AcceptStamp");
            if (accT == null)
            {
                GameObject accObj = new GameObject("AcceptStamp", typeof(RectTransform), typeof(CanvasGroup));
                accObj.transform.SetParent(cardContainer, false);
                accT = accObj.transform;
            }
            acceptStamp = accT.GetComponent<RectTransform>();
            acceptStamp.anchorMin = new Vector2(0.5f, 0.5f);
            acceptStamp.anchorMax = new Vector2(0.5f, 0.5f);
            acceptStamp.pivot = new Vector2(0.5f, 0.5f);
            acceptStamp.sizeDelta = new Vector2(190f, 56f);
            acceptStamp.anchoredPosition = new Vector2(150f, 215f);
            acceptStamp.localRotation = Quaternion.Euler(0f, 0f, -15f);
            CreateTMPText("AcceptLabel", acceptStamp, Vector2.zero, new Vector2(190f, 56f), 26f, TextAlignmentOptions.Center, new Color(0.35f, 1f, 0.48f)).text = "[ ACCEPT ▶ ]";
            acceptStampGroup = acceptStamp.GetComponent<CanvasGroup>() ?? acceptStamp.gameObject.AddComponent<CanvasGroup>();

            Transform leftBtnTrans = cardContainer.Find("LeftChoiceButton");
            if (leftBtnTrans != null) leftBtnTrans.gameObject.SetActive(false);
            Transform rightBtnTrans = cardContainer.Find("RightChoiceButton");
            if (rightBtnTrans != null) rightBtnTrans.gameObject.SetActive(false);

            // 3. Consequence Panel Template (Sleek 700 x 560 Swipeable Results Card)
            if (consequencePanel == null)
            {
                Transform t = transform.Find("ConsequencePanel");
                if (t != null) consequencePanel = t.gameObject;
                else
                {
                    consequencePanel = new GameObject("ConsequencePanel", typeof(RectTransform), typeof(Image));
                    consequencePanel.transform.SetParent(transform, false);
                }
            }
            RectTransform consRT = consequencePanel.GetComponent<RectTransform>();
            consRT.anchorMin = new Vector2(0.5f, 0.5f);
            consRT.anchorMax = new Vector2(0.5f, 0.5f);
            consRT.pivot = new Vector2(0.5f, 0.5f);
            consRT.sizeDelta = new Vector2(700f, 560f);
            if (!isDraggingConsequence) consRT.anchoredPosition = Vector2.zero;
            Image consImg = consequencePanel.GetComponent<Image>() ?? consequencePanel.AddComponent<Image>();
            consImg.color = new Color(0.10f, 0.15f, 0.23f, 0.98f);
            consImg.raycastTarget = false;

            Transform cpTrans = consequencePanel.transform;
            consequenceTitleText = CreateTMPText("ConsTitle", cpTrans, new Vector2(0f, -26f), new Vector2(640f, 44f), 27f, TextAlignmentOptions.Center, new Color(0.42f, 1f, 0.58f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            consequenceTitleText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            consequenceTitleText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            consequenceTitleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            consequenceTitleText.rectTransform.anchoredPosition = new Vector2(0f, -26f);
            consequenceTitleText.rectTransform.sizeDelta = new Vector2(640f, 44f);

            consequenceBodyText = CreateTMPText("ConsBody", cpTrans, new Vector2(0f, -86f), new Vector2(620f, 145f), 21f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            consequenceBodyText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            consequenceBodyText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            consequenceBodyText.rectTransform.pivot = new Vector2(0.5f, 1f);
            consequenceBodyText.rectTransform.anchoredPosition = new Vector2(0f, -86f);
            consequenceBodyText.rectTransform.sizeDelta = new Vector2(620f, 145f);

            Transform consStatsBox = cpTrans.Find("StatsBox");
            if (consStatsBox != null)
            {
                RectTransform csRT = consStatsBox.GetComponent<RectTransform>();
                if (csRT != null)
                {
                    csRT.anchorMin = new Vector2(0.5f, 1f);
                    csRT.anchorMax = new Vector2(0.5f, 1f);
                    csRT.pivot = new Vector2(0.5f, 1f);
                    csRT.anchoredPosition = new Vector2(0f, -245f);
                    csRT.sizeDelta = new Vector2(580f, 155f);
                }
                moneyDeltaText = CreateTMPText("MoneyDelta", consStatsBox, new Vector2(0f, -18f), new Vector2(540f, 34f), 20f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                depressionDeltaText = CreateTMPText("DepDelta", consStatsBox, new Vector2(0f, -62f), new Vector2(540f, 34f), 20f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                tirednessDeltaText = CreateTMPText("TiredDelta", consStatsBox, new Vector2(0f, -106f), new Vector2(540f, 34f), 20f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            else
            {
                moneyDeltaText = CreateTMPText("MoneyDelta", cpTrans, new Vector2(0f, -255f), new Vector2(580f, 34f), 20f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                depressionDeltaText = CreateTMPText("DepDelta", cpTrans, new Vector2(0f, -298f), new Vector2(580f, 34f), 20f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                tirednessDeltaText = CreateTMPText("TiredDelta", cpTrans, new Vector2(0f, -341f), new Vector2(580f, 34f), 20f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }

            GameObject contBtnObj = CreateButton("ContinueButton", cpTrans, new Vector2(0f, 28f), new Vector2(560f, 54f), Color.clear, OnContinueClicked, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            RectTransform contBtnRT = contBtnObj.GetComponent<RectTransform>();
            contBtnRT.anchorMin = new Vector2(0.5f, 0f);
            contBtnRT.anchorMax = new Vector2(0.5f, 0f);
            contBtnRT.pivot = new Vector2(0.5f, 0f);
            contBtnRT.anchoredPosition = new Vector2(0f, 28f);
            contBtnRT.sizeDelta = new Vector2(560f, 54f);
            continueButton = contBtnObj.GetComponent<Button>();
            if (continueButton != null) Destroy(continueButton);
            Image contBtnImg = contBtnObj.GetComponent<Image>();
            if (contBtnImg != null) { contBtnImg.color = Color.clear; contBtnImg.raycastTarget = false; }
            TMP_Text contBtnLabel = CreateTMPText("Label", contBtnObj.transform, Vector2.zero, new Vector2(540f, 48f), 18f, TextAlignmentOptions.Center, new Color(0.72f, 0.90f, 1.0f));
            contBtnLabel.rectTransform.sizeDelta = new Vector2(540f, 48f);
            contBtnLabel.rectTransform.anchoredPosition = Vector2.zero;
            contBtnLabel.text = "◀ CLICK ANYWHERE OR PRESS SPACE TO HANG UP ▶";

            // 4. Reckoning Panel Template (Full 1680 x 940 Two-Column Layout — Zero Overlap!)
            if (reckoningPanel == null)
            {
                Transform t = transform.Find("ReckoningPanel");
                if (t != null) reckoningPanel = t.gameObject;
                else
                {
                    reckoningPanel = new GameObject("ReckoningPanel", typeof(RectTransform), typeof(Image));
                    reckoningPanel.transform.SetParent(transform, false);
                }
            }

            EnsureReckoningScaleUI();
            NormalizeReckoningLayout();
            EnsurePhoneVirtualCursorBuilt();

            // Re-apply UI layer recursively on all newly created children so camera isolation renders every child!
            int finalUiLayer = LayerMask.NameToLayer("UI");
            if (finalUiLayer < 0) finalUiLayer = 5;
            SetLayerRecursively(gameObject, finalUiLayer);

            UpdateAllTMPMeshGeometry();
        }

        private void EnsurePhoneVirtualCursorBuilt()
        {
            Transform existing = transform.Find("PhoneVirtualCursor");
            GameObject curObj;
            if (existing != null)
            {
                curObj = existing.gameObject;
                // Remove any old procedural diamond children ("Ring", "InnerCut", "CenterDot")
                for (int i = curObj.transform.childCount - 1; i >= 0; i--)
                {
                    DestroyImmediate(curObj.transform.GetChild(i).gameObject);
                }
            }
            else
            {
                curObj = new GameObject("PhoneVirtualCursor", typeof(RectTransform));
                curObj.transform.SetParent(transform, false);
            }

            curObj.layer = gameObject.layer;
            phoneVirtualCursorRect = curObj.GetComponent<RectTransform>();
            phoneVirtualCursorRect.anchorMin = new Vector2(0.5f, 0.5f);
            phoneVirtualCursorRect.anchorMax = new Vector2(0.5f, 0.5f);
            phoneVirtualCursorRect.pivot = new Vector2(0.5f, 0.5f);
            phoneVirtualCursorRect.localRotation = Quaternion.identity;
            phoneVirtualCursorRect.localScale = Vector3.one;

            phoneVirtualCursorRingImg = curObj.GetComponent<Image>() ?? curObj.AddComponent<Image>();
            phoneVirtualCursorRingImg.raycastTarget = false;
            phoneVirtualCursorRingImg.preserveAspect = true;
            phoneVirtualCursorRingImg.color = Color.white;

            GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
            if (crawler != null)
            {
                if (crawler.defaultCrosshairImg != null && crawler.defaultCrosshairImg.sprite != null)
                {
                    cachedDefaultCursorSprite = crawler.defaultCrosshairImg.sprite;
                    RectTransform rt = crawler.defaultCrosshairImg.rectTransform;
                    cachedDefaultCursorSize = new Vector2(
                        rt.sizeDelta.x * Mathf.Abs(rt.localScale.x) * 1.061f,
                        rt.sizeDelta.y * Mathf.Abs(rt.localScale.y) * 1.061f);
                    if (cachedDefaultCursorSize.sqrMagnitude < 16f) cachedDefaultCursorSize = new Vector2(20.7f, 31.8f);
                }
                if (crawler.interactCrosshairImg != null && crawler.interactCrosshairImg.sprite != null)
                {
                    cachedInteractCursorSprite = crawler.interactCrosshairImg.sprite;
                    RectTransform rt = crawler.interactCrosshairImg.rectTransform;
                    cachedInteractCursorSize = new Vector2(
                        rt.sizeDelta.x * Mathf.Abs(rt.localScale.x) * 1.061f,
                        rt.sizeDelta.y * Mathf.Abs(rt.localScale.y) * 1.061f);
                    if (cachedInteractCursorSize.sqrMagnitude < 16f) cachedInteractCursorSize = new Vector2(51f, 51f);
                }
            }

            phoneVirtualCursorRect.pivot = new Vector2(0.28f, 0.82f);
            if (cachedDefaultCursorSprite != null)
            {
                phoneVirtualCursorRingImg.sprite = cachedDefaultCursorSprite;
                phoneVirtualCursorRingImg.color = new Color(1f, 0.953f, 0.918f, 1f);
                phoneVirtualCursorRect.sizeDelta = cachedDefaultCursorSize;
            }
            else
            {
                phoneVirtualCursorRect.sizeDelta = new Vector2(21.2f, 31.8f);
            }
        }

        /// <summary>
        /// Organizes ReckoningPanel into a spacious 1680x940 two-column terminal layout:
        /// - Top Header Banner: Title + Subtitle + Typewriter Narrative
        /// - Left Column (StatsBox): 8 distinct non-overlapping stat rows with flash/bounce bars
        /// - Right Column (ScaleContainer): Mechanical balance scale with deep pans for falling TV DVD logos
        /// - Bottom Footer: Final Verdict Prompt + Hang Up Button
        /// </summary>
        private void NormalizeReckoningLayout()
        {
            if (reckoningPanel == null) return;

            RectTransform reckRT = reckoningPanel.GetComponent<RectTransform>();
            reckRT.anchorMin = new Vector2(0.5f, 0.5f);
            reckRT.anchorMax = new Vector2(0.5f, 0.5f);
            reckRT.pivot = new Vector2(0.5f, 0.5f);
            reckRT.sizeDelta = new Vector2(1680f, 940f);
            reckRT.anchoredPosition = Vector2.zero;

            Image reckImg = reckoningPanel.GetComponent<Image>() ?? reckoningPanel.AddComponent<Image>();
            reckImg.color = new Color(0.04f, 0.06f, 0.10f, 0.98f);
            reckImg.raycastTarget = true;

            Transform rpTrans = reckoningPanel.transform;

            reckoningTitleText = CreateTMPText("ReckTitle", rpTrans, new Vector2(0f, -18f), new Vector2(1580f, 44f), 32f, TextAlignmentOptions.Center, new Color(0.38f, 0.96f, 0.78f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            reckoningSubtitleText = CreateTMPText("ReckSubtitle", rpTrans, new Vector2(0f, -62f), new Vector2(1580f, 26f), 16f, TextAlignmentOptions.Center, new Color(0.62f, 0.76f, 0.88f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            reckoningNarrativeText = CreateTMPText("ReckNarrative", rpTrans, new Vector2(0f, -94f), new Vector2(1540f, 74f), 19f, TextAlignmentOptions.Center, new Color(0.92f, 0.96f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

            // Left Column: StatsBox (x = -410, y = -182 from top, size 760 x 540)
            Transform statsBox = rpTrans.Find("StatsBox");
            if (statsBox == null)
            {
                GameObject sbObj = new GameObject("StatsBox", typeof(RectTransform), typeof(Image));
                sbObj.transform.SetParent(rpTrans, false);
                statsBox = sbObj.transform;
            }
            RectTransform sbRT = statsBox.GetComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(0.5f, 1f);
            sbRT.anchorMax = new Vector2(0.5f, 1f);
            sbRT.pivot = new Vector2(0.5f, 1f);
            sbRT.anchoredPosition = new Vector2(-415f, -182f);
            sbRT.sizeDelta = new Vector2(770f, 540f);
            Image sbImg = statsBox.GetComponent<Image>() ?? statsBox.gameObject.AddComponent<Image>();
            sbImg.color = new Color(0.03f, 0.05f, 0.08f, 0.95f);

            reckoningPositiveChoicesText = SetupStatLedgerRow(statsBox, "PosChoices", 0, -16f);
            reckoningNegativeChoicesText = SetupStatLedgerRow(statsBox, "NegChoices", 1, -78f);
            reckoningDaysText = SetupStatLedgerRow(statsBox, "DaysStat", 2, -140f);
            reckoningCleanlinessText = SetupStatLedgerRow(statsBox, "CleanlinessStat", 3, -202f);
            reckoningSubstancesText = SetupStatLedgerRow(statsBox, "SubstancesStat", 4, -264f);
            reckoningDepressionText = SetupStatLedgerRow(statsBox, "DepressionStat", 5, -326f);
            reckoningMissedCallsText = SetupStatLedgerRow(statsBox, "MissedCallsStat", 6, -388f);
            reckoningScoreText = SetupStatLedgerRow(statsBox, "ScoreStat", 7, -462f, true);

            // Right Column: ScaleContainer (x = +415, y = -182 from top, size 770 x 540)
            if (scaleContainer != null)
            {
                scaleContainer.anchorMin = new Vector2(0.5f, 1f);
                scaleContainer.anchorMax = new Vector2(0.5f, 1f);
                scaleContainer.pivot = new Vector2(0.5f, 1f);
                scaleContainer.anchoredPosition = new Vector2(415f, -182f);
                scaleContainer.sizeDelta = new Vector2(770f, 540f);
                scaleContainer.localScale = Vector3.one;

                Image scBg = scaleContainer.GetComponent<Image>() ?? scaleContainer.gameObject.AddComponent<Image>();
                scBg.color = new Color(0.03f, 0.05f, 0.08f, 0.95f);
                scBg.raycastTarget = false;
            }

            // Bottom Footer: VerdictPrompt & HangupButton
            reckoningVerdictPromptText = CreateTMPText("VerdictPrompt", rpTrans, new Vector2(0f, 94f), new Vector2(1540f, 82f), 20f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

            GameObject hangBtnObj = CreateButton("HangupButton", rpTrans, new Vector2(0f, 22f), new Vector2(520f, 58f), new Color(0.18f, 0.42f, 0.52f, 1f), OnReckoningContinueClicked, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            reckoningContinueButton = hangBtnObj.GetComponent<Button>();
            CreateTMPText("Label", hangBtnObj.transform, Vector2.zero, new Vector2(500f, 50f), 20f, TextAlignmentOptions.Center, Color.white).text = "[ HANG UP & HEED VERDICT ]";
        }

        private TMP_Text SetupStatLedgerRow(Transform statsBox, string textObjName, int rowIdx, float topOffsetY, bool isSummaryRow = false)
        {
            string rowHolderName = $"Row_{textObjName}";
            Transform rowHolder = statsBox.Find(rowHolderName);
            if (rowHolder == null)
            {
                GameObject rhObj = new GameObject(rowHolderName, typeof(RectTransform), typeof(Image));
                rhObj.transform.SetParent(statsBox, false);
                rowHolder = rhObj.transform;
            }

            RectTransform rhRT = rowHolder.GetComponent<RectTransform>();
            rhRT.anchorMin = new Vector2(0.5f, 1f);
            rhRT.anchorMax = new Vector2(0.5f, 1f);
            rhRT.pivot = new Vector2(0.5f, 1f);
            rhRT.anchoredPosition = new Vector2(0f, topOffsetY);
            rhRT.sizeDelta = new Vector2(730f, isSummaryRow ? 56f : 52f);
            rhRT.localScale = Vector3.one;

            Image rhImg = rowHolder.GetComponent<Image>() ?? rowHolder.gameObject.AddComponent<Image>();
            rhImg.color = isSummaryRow
                ? new Color(0.10f, 0.16f, 0.24f, 0.96f)
                : new Color(0.07f, 0.11f, 0.16f, 0.90f);
            rhImg.raycastTarget = false;

            // Move any existing direct child text under statsBox into this rowHolder so scene references stay intact
            Transform existingTextTrans = rowHolder.Find(textObjName) ?? statsBox.Find(textObjName) ?? reckoningPanel.transform.Find(textObjName);
            TMP_Text tmp;
            if (existingTextTrans != null)
            {
                if (existingTextTrans.parent != rowHolder)
                {
                    existingTextTrans.SetParent(rowHolder, false);
                }
                tmp = existingTextTrans.GetComponent<TMP_Text>();
            }
            else
            {
                tmp = CreateTMPText(textObjName, rowHolder, Vector2.zero, new Vector2(695f, 46f), isSummaryRow ? 20f : 18f, isSummaryRow ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft, Color.white);
            }

            RectTransform tRT = tmp.rectTransform;
            tRT.anchorMin = new Vector2(0.5f, 0.5f);
            tRT.anchorMax = new Vector2(0.5f, 0.5f);
            tRT.pivot = new Vector2(0.5f, 0.5f);
            tRT.anchoredPosition = Vector2.zero;
            tRT.sizeDelta = new Vector2(695f, 46f);
            tmp.fontSize = isSummaryRow ? 20f : 18f;
            tmp.alignment = isSummaryRow ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            EnsureFontOnTMP(tmp);
            return tmp;
        }

        private void EnsureReckoningScaleUI()
        {
            if (reckoningPanel == null) return;
            Transform rpTrans = reckoningPanel.transform;

            if (scaleContainer == null)
            {
                Transform t = rpTrans.Find("ScaleContainer");
                if (t != null) scaleContainer = t.GetComponent<RectTransform>();
                else
                {
                    GameObject scObj = new GameObject("ScaleContainer", typeof(RectTransform), typeof(Image));
                    scObj.transform.SetParent(rpTrans, false);
                    scaleContainer = scObj.GetComponent<RectTransform>();
                }
            }

            scaleContainer.anchorMin = new Vector2(0.5f, 1f);
            scaleContainer.anchorMax = new Vector2(0.5f, 1f);
            scaleContainer.pivot = new Vector2(0.5f, 1f);
            scaleContainer.anchoredPosition = new Vector2(415f, -182f);
            scaleContainer.sizeDelta = new Vector2(770f, 540f);
            scaleContainer.localScale = Vector3.one;

            // Stand Pillar
            RectTransform standRT = GetOrCreateChildImage("FulcrumStand", scaleContainer, new Color(0.22f, 0.28f, 0.35f, 0.98f));
            standRT.anchoredPosition = new Vector2(0f, -65f);
            standRT.sizeDelta = new Vector2(22f, 290f);

            // Stand Base
            RectTransform baseRT = GetOrCreateChildImage("StandBase", scaleContainer, new Color(0.30f, 0.36f, 0.44f, 1f));
            baseRT.anchoredPosition = new Vector2(0f, -210f);
            baseRT.sizeDelta = new Vector2(180f, 18f);

            // Pivot Cap
            RectTransform capRT = GetOrCreateChildImage("PivotCap", scaleContainer, new Color(0.48f, 0.56f, 0.65f, 1f));
            capRT.anchoredPosition = new Vector2(0f, 80f);
            capRT.sizeDelta = new Vector2(34f, 34f);

            // Scale Beam
            scaleBeam = GetOrCreateChildImage("ScaleBeam", scaleContainer, new Color(0.42f, 0.50f, 0.58f, 1f));
            scaleBeam.anchoredPosition = new Vector2(0f, 80f);
            scaleBeam.pivot = new Vector2(0.5f, 0.5f);
            scaleBeam.sizeDelta = new Vector2(430f, 12f);

            // Left Pan (Hope)
            leftPan = GetOrCreateChildImage("LeftPan", scaleContainer, new Color(0.10f, 0.36f, 0.20f, 0.98f));
            leftPan.anchoredPosition = new Vector2(-205f, -35f);
            leftPan.sizeDelta = new Vector2(186f, 18f);

            // Left Pan Rim Walls
            RectTransform lRimL = GetOrCreateChildImage("LeftRim", leftPan, new Color(0.18f, 0.62f, 0.34f, 0.9f));
            lRimL.anchoredPosition = new Vector2(-90f, 22f);
            lRimL.sizeDelta = new Vector2(6f, 36f);
            RectTransform lRimR = GetOrCreateChildImage("RightRim", leftPan, new Color(0.18f, 0.62f, 0.34f, 0.9f));
            lRimR.anchoredPosition = new Vector2(90f, 22f);
            lRimR.sizeDelta = new Vector2(6f, 36f);

            Transform lContT = leftPan.Find("LeftDvdContainer");
            if (lContT == null)
            {
                GameObject lDvdCont = new GameObject("LeftDvdContainer", typeof(RectTransform));
                lDvdCont.transform.SetParent(leftPan, false);
                lContT = lDvdCont.transform;
            }
            leftDvdContainer = lContT.GetComponent<RectTransform>();
            leftDvdContainer.anchoredPosition = Vector2.zero;
            leftDvdContainer.sizeDelta = new Vector2(180f, 220f);

            leftWeightText = CreateTMPText("LeftWeightLabel", leftPan, new Vector2(0f, -26f), new Vector2(200f, 28f), 16f, TextAlignmentOptions.Center, new Color(0.35f, 1f, 0.55f));

            // Right Pan (Despair)
            rightPan = GetOrCreateChildImage("RightPan", scaleContainer, new Color(0.40f, 0.10f, 0.14f, 0.98f));
            rightPan.anchoredPosition = new Vector2(205f, -35f);
            rightPan.sizeDelta = new Vector2(186f, 18f);

            RectTransform rRimL = GetOrCreateChildImage("LeftRim", rightPan, new Color(0.72f, 0.18f, 0.24f, 0.9f));
            rRimL.anchoredPosition = new Vector2(-90f, 22f);
            rRimL.sizeDelta = new Vector2(6f, 36f);
            RectTransform rRimR = GetOrCreateChildImage("RightRim", rightPan, new Color(0.72f, 0.18f, 0.24f, 0.9f));
            rRimR.anchoredPosition = new Vector2(90f, 22f);
            rRimR.sizeDelta = new Vector2(6f, 36f);

            Transform rContT = rightPan.Find("RightDvdContainer");
            if (rContT == null)
            {
                GameObject rDvdCont = new GameObject("RightDvdContainer", typeof(RectTransform));
                rDvdCont.transform.SetParent(rightPan, false);
                rContT = rDvdCont.transform;
            }
            rightDvdContainer = rContT.GetComponent<RectTransform>();
            rightDvdContainer.anchoredPosition = Vector2.zero;
            rightDvdContainer.sizeDelta = new Vector2(180f, 220f);

            rightWeightText = CreateTMPText("RightWeightLabel", rightPan, new Vector2(0f, -26f), new Vector2(200f, 28f), 16f, TextAlignmentOptions.Center, new Color(1f, 0.35f, 0.45f));

            // Chain lines
            leftChainA = CreateChainLine("LeftChainA", scaleContainer);
            leftChainB = CreateChainLine("LeftChainB", scaleContainer);
            rightChainA = CreateChainLine("RightChainA", scaleContainer);
            rightChainB = CreateChainLine("RightChainB", scaleContainer);
        }

        private RectTransform GetOrCreateChildImage(string name, Transform parent, Color color)
        {
            Transform t = parent.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            if (t == null) go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            Image img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        private RectTransform CreateChainLine(string name, Transform parent)
        {
            Transform t = parent.Find(name);
            GameObject chainObj = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            if (t == null) chainObj.transform.SetParent(parent, false);
            RectTransform rt = chainObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.localScale = Vector3.one;
            Image img = chainObj.GetComponent<Image>() ?? chainObj.AddComponent<Image>();
            img.color = new Color(0.58f, 0.65f, 0.72f, 0.78f);
            img.raycastTarget = false;
            return rt;
        }

        private void EnsureProceduralAudioClips()
        {
            if (typeTickClip == null) typeTickClip = GenerateProceduralTone(1200f, 0.015f, 0.25f, true);
            if (rowBounceClip == null) rowBounceClip = GenerateProceduralTone(520f, 0.045f, 0.35f, false);
            if (impactHopeClip == null) impactHopeClip = GenerateProceduralChime(560f, 280f, 0.10f, 0.45f);
            if (impactDespairClip == null) impactDespairClip = GenerateProceduralChime(190f, 75f, 0.14f, 0.55f);
            if (verdictHopeClip == null) verdictHopeClip = GenerateProceduralChord(new float[] { 440f, 554.37f, 659.25f }, 0.5f, 0.45f);
            if (verdictDespairClip == null) verdictDespairClip = GenerateProceduralChord(new float[] { 330f, 277.18f, 220f }, 0.6f, 0.5f);
        }

        private void PlayProceduralTick(float pitch = 1f)
        {
            EnsureUIAudio();
            EnsureProceduralAudioClips();
            if (uiAudio != null && typeTickClip != null)
            {
                uiAudio.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
                uiAudio.PlayOneShot(typeTickClip, 0.35f);
                uiAudio.pitch = 1f;
            }
        }

        private void PlayProceduralRowBounce(float pitch = 1f)
        {
            EnsureUIAudio();
            EnsureProceduralAudioClips();
            if (uiAudio != null && rowBounceClip != null)
            {
                uiAudio.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
                uiAudio.PlayOneShot(rowBounceClip, 0.45f);
                uiAudio.pitch = 1f;
            }
        }

        private void PlayProceduralImpactSound(bool isHope)
        {
            EnsureUIAudio();
            EnsureProceduralAudioClips();
            if (uiAudio != null)
            {
                AudioClip clip = isHope ? impactHopeClip : impactDespairClip;
                if (clip != null) uiAudio.PlayOneShot(clip, 0.55f);
            }
        }

        private void PlayVerdictSound(bool isHope)
        {
            EnsureUIAudio();
            EnsureProceduralAudioClips();
            if (uiAudio != null)
            {
                AudioClip clip = isHope ? verdictHopeClip : verdictDespairClip;
                if (clip != null) uiAudio.PlayOneShot(clip, 0.7f);
            }
        }

        private AudioClip GenerateProceduralTone(float freq, float duration, float volume, bool addNoise)
        {
            const int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * duration);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float env = 1f - (float)i / totalSamples;
                float s = Mathf.Sin(2f * Mathf.PI * freq * t);
                if (addNoise) s = (s * 0.7f) + (UnityEngine.Random.Range(-0.3f, 0.3f));
                data[i] = s * env * volume;
            }

            AudioClip clip = AudioClip.Create("ProcTone", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateProceduralChime(float startFreq, float endFreq, float duration, float volume)
        {
            const int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * duration);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float env = 1f - Mathf.Pow((float)i / totalSamples, 0.7f);
                float curFreq = Mathf.Lerp(startFreq, endFreq, (float)i / totalSamples);
                float s = Mathf.Sin(2f * Mathf.PI * curFreq * t);
                data[i] = s * env * volume;
            }

            AudioClip clip = AudioClip.Create("ProcChime", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateProceduralChord(float[] freqs, float duration, float volume)
        {
            const int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * duration);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float env = 1f - (float)i / totalSamples;
                float s = 0f;
                foreach (var f in freqs) s += Mathf.Sin(2f * Mathf.PI * f * t);
                s /= freqs.Length;
                data[i] = s * env * volume;
            }

            AudioClip clip = AudioClip.Create("ProcChord", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void EnsureFontOnTMP(TMP_Text tmp)
        {
            if (tmp == null) return;

            if (tmp.font == null)
            {
                if (cachedFontAsset == null)
                {
                    cachedFontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                    if (cachedFontAsset == null) cachedFontAsset = TMP_Settings.defaultFontAsset;
                    if (cachedFontAsset == null)
                    {
                        TMP_FontAsset[] allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                        foreach (var f in allFonts)
                        {
                            if (f != null)
                            {
                                if (f.name.IndexOf("Liberation", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    cachedFontAsset = f;
                                    break;
                                }
                                if (cachedFontAsset == null) cachedFontAsset = f;
                            }
                        }
                    }
                }

                if (cachedFontAsset != null) tmp.font = cachedFontAsset;
            }

            tmp.maskable = false;
            tmp.richText = true;
            tmp.raycastTarget = false;

            RectTransform rt = tmp.rectTransform;
            if (rt != null)
            {
                rt.anchoredPosition3D = new Vector3(rt.anchoredPosition.x, rt.anchoredPosition.y, 0f);
                rt.localScale = Vector3.one;
            }
        }

        private TMP_Text CreateTMPText(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align, Color color, Vector2? anchor = null, Vector2? pivot = null)
        {
            Transform existing = parent.Find(name);
            bool isNew = existing == null;
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            if (isNew) go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            if (isNew)
            {
                Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
                Vector2 p = pivot ?? new Vector2(0.5f, 0.5f);
                rt.anchorMin = a;
                rt.anchorMax = a;
                rt.pivot = p;
                rt.sizeDelta = size;
                rt.anchoredPosition = pos;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            }

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            EnsureFontOnTMP(tmp);
            if (isNew)
            {
                tmp.fontSize = fontSize;
                tmp.alignment = align;
                tmp.color = color;
            }
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.maskable = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        private GameObject CreateButton(string name, Transform parent, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick, Vector2? anchor = null, Vector2? pivot = null)
        {
            Transform existing = parent.Find(name);
            bool isNew = existing == null;
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            if (isNew) go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            if (isNew)
            {
                Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
                Vector2 p = pivot ?? new Vector2(0.5f, 0.5f);
                rt.anchorMin = a;
                rt.anchorMax = a;
                rt.pivot = p;
                rt.sizeDelta = size;
                rt.anchoredPosition = pos;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            }

            Image img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            if (isNew) img.color = color;
            img.raycastTarget = true;
            Button btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            if (onClick != null)
            {
                btn.onClick.RemoveListener(onClick);
                btn.onClick.AddListener(onClick);
            }
            return go;
        }
    }
}
