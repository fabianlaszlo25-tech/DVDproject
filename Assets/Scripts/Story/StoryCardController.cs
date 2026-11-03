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
    /// CRT-themed swipe card story encounter overlay.
    /// Operates in ScreenSpaceCamera render mode so CRT post-processing and fullscreen shaders apply directly.
    /// Supports direct swipe gesture on both the story card and consequence panel,
    /// with faded left/right edge visual indicators guiding the player to swipe.
    /// </summary>
    public class StoryCardController : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public static StoryCardController Instance { get; private set; }

        public static StoryCardController EnsureInstance()
        {
            if (Instance != null) return Instance;

            // 1. Search active scene for existing permanent StoryCardController
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

        private static TMP_FontAsset cachedFontAsset = null;

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

            // 1. While CardContainer is active (and choice not yet made)
            if (cardContainer != null && cardContainer.gameObject.activeSelf && !hasChosen)
            {
                UpdateSideIndicators();
                HandleDirectMouseSwipe();

                // Smoothly return card toward center when not dragging
                if (!isDragging)
                {
                    currentDragOffset = Vector2.Lerp(currentDragOffset, Vector2.zero, Time.unscaledDeltaTime * cardReturnSpeed);
                    ApplyCardTransform(currentDragOffset);
                }

                // Keyboard shortcuts: A / Left Arrow = Reject, D / Right Arrow = Accept
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
                catch {}

                if (!leftPressed && !rightPressed)
                {
                    leftPressed = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
                    rightPressed = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
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
                UpdateScaleVisuals(Time.unscaledDeltaTime);
                HandleReckoningInput();
            }
        }

        private void UpdateSideIndicators()
        {
            if (leftSwipeIndicator == null || rightSwipeIndicator == null) return;

            if (!leftSwipeIndicator.gameObject.activeSelf) leftSwipeIndicator.gameObject.SetActive(true);
            if (!rightSwipeIndicator.gameObject.activeSelf) rightSwipeIndicator.gameObject.SetActive(true);

            if (isDragging)
            {
                float leftRatio = Mathf.Clamp01(-currentDragOffset.x / swipeThreshold);
                float rightRatio = Mathf.Clamp01(currentDragOffset.x / swipeThreshold);

                if (leftRatio > 0.01f)
                {
                    if (leftSwipeGroup != null) leftSwipeGroup.alpha = Mathf.Lerp(0.35f, 1.0f, leftRatio);
                    leftSwipeIndicator.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.15f, leftRatio);

                    if (rightSwipeGroup != null) rightSwipeGroup.alpha = Mathf.Lerp(0.35f, 0.10f, leftRatio);
                    rightSwipeIndicator.localScale = Vector3.one;
                }
                else if (rightRatio > 0.01f)
                {
                    if (rightSwipeGroup != null) rightSwipeGroup.alpha = Mathf.Lerp(0.35f, 1.0f, rightRatio);
                    rightSwipeIndicator.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.15f, rightRatio);

                    if (leftSwipeGroup != null) leftSwipeGroup.alpha = Mathf.Lerp(0.35f, 0.10f, rightRatio);
                    leftSwipeIndicator.localScale = Vector3.one;
                }
                else
                {
                    float idleAlpha = Mathf.PingPong(Time.unscaledTime * 1.5f, 0.18f) + 0.28f;
                    if (leftSwipeGroup != null) leftSwipeGroup.alpha = idleAlpha;
                    if (rightSwipeGroup != null) rightSwipeGroup.alpha = idleAlpha;
                    leftSwipeIndicator.localScale = Vector3.one;
                    rightSwipeIndicator.localScale = Vector3.one;
                }
            }
            else
            {
                // Gentle breathing pulse while card is at rest
                float idleAlpha = Mathf.PingPong(Time.unscaledTime * 1.5f, 0.18f) + 0.28f;
                if (leftSwipeGroup != null) leftSwipeGroup.alpha = idleAlpha;
                if (rightSwipeGroup != null) rightSwipeGroup.alpha = idleAlpha;
                leftSwipeIndicator.localScale = Vector3.one;
                rightSwipeIndicator.localScale = Vector3.one;
            }
        }

        private void HideSideIndicators()
        {
            if (leftSwipeIndicator != null && leftSwipeIndicator.gameObject.activeSelf) leftSwipeIndicator.gameObject.SetActive(false);
            if (rightSwipeIndicator != null && rightSwipeIndicator.gameObject.activeSelf) rightSwipeIndicator.gameObject.SetActive(false);
        }

        private void HandleDirectMouseSwipe()
        {
            Vector2 mouseScreenPos = GetMouseScreenPosition();
            Canvas canvas = GetComponent<Canvas>();
            Camera eventCam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;

            bool leftDown = GetMouseButtonDown();
            bool leftHeld = GetMouseButtonHeld();
            bool leftUp = GetMouseButtonUp();

            if (leftDown && !isDragging)
            {
                if (cardContainer != null && RectTransformUtility.RectangleContainsScreenPoint(cardContainer, mouseScreenPos, eventCam))
                {
                    isDragging = true;
                    dragStartPos = mouseScreenPos;
                }
            }
            else if (isDragging && leftHeld)
            {
                currentDragOffset = mouseScreenPos - dragStartPos;
                ApplyCardTransform(currentDragOffset);
            }
            else if (isDragging && leftUp)
            {
                isDragging = false;
                if (currentDragOffset.x < -swipeThreshold)
                {
                    ChooseLeft();
                }
                else if (currentDragOffset.x > swipeThreshold)
                {
                    ChooseRight();
                }
            }
        }

        private void HandleConsequenceSwipeAndInput()
        {
            Vector2 mouseScreenPos = GetMouseScreenPosition();
            Canvas canvas = GetComponent<Canvas>();
            Camera eventCam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;

            bool leftDown = GetMouseButtonDown();
            bool leftHeld = GetMouseButtonHeld();
            bool leftUp = GetMouseButtonUp();

            RectTransform consRT = consequencePanel != null ? consequencePanel.GetComponent<RectTransform>() : null;

            if (leftDown && !isDraggingConsequence)
            {
                // Direct click fallback on continueButton
                if (continueButton != null && RectTransformUtility.RectangleContainsScreenPoint(continueButton.GetComponent<RectTransform>(), mouseScreenPos, eventCam))
                {
                    OnContinueClicked();
                    return;
                }

                // Start dragging consequence panel to swipe away
                if (consRT != null && RectTransformUtility.RectangleContainsScreenPoint(consRT, mouseScreenPos, eventCam))
                {
                    isDraggingConsequence = true;
                    consequenceDragStart = mouseScreenPos;
                }
            }
            else if (isDraggingConsequence && leftHeld)
            {
                consequenceDragOffset = mouseScreenPos - consequenceDragStart;
                if (consRT != null)
                {
                    consRT.anchoredPosition = new Vector2(consequenceDragOffset.x, 0f);
                    consRT.localRotation = Quaternion.Euler(0f, 0f, -consequenceDragOffset.x * 0.025f);
                }
            }
            else if (isDraggingConsequence && leftUp)
            {
                isDraggingConsequence = false;
                if (Mathf.Abs(consequenceDragOffset.x) > swipeThreshold * 0.7f)
                {
                    StartCoroutine(AnimateConsequenceExit(consequenceDragOffset.x > 0));
                }
                else if (consRT != null)
                {
                    StartCoroutine(ReturnConsequenceToCenter(consRT));
                }
            }

            // Keyboard shortcut to dismiss
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
            catch {}

            if (!dismissKey)
            {
                dismissKey = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                             Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape);
            }

            if (dismissKey)
            {
                OnContinueClicked();
            }
        }

        private IEnumerator AnimateConsequenceExit(bool toRight)
        {
            RectTransform consRT = consequencePanel != null ? consequencePanel.GetComponent<RectTransform>() : null;
            if (consRT == null)
            {
                OnContinueClicked();
                yield break;
            }

            float targetX = toRight ? 750f : -750f;
            Vector2 startPos = consRT.anchoredPosition;
            float startRot = consRT.localRotation.eulerAngles.z;
            if (startRot > 180f) startRot -= 360f;
            float targetRot = toRight ? -20f : 20f;

            float elapsed = 0f;
            float dur = 0.18f;

            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / dur);
                consRT.anchoredPosition = Vector2.Lerp(startPos, new Vector2(targetX, 0f), t);
                consRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRot, targetRot, t));
                yield return null;
            }

            OnContinueClicked();
        }

        private IEnumerator ReturnConsequenceToCenter(RectTransform consRT)
        {
            if (consRT == null) yield break;
            Vector2 startPos = consRT.anchoredPosition;
            float startRot = consRT.localRotation.eulerAngles.z;
            if (startRot > 180f) startRot -= 360f;

            float elapsed = 0f;
            float dur = 0.15f;

            while (elapsed < dur && !isDraggingConsequence)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / dur);
                consRT.anchoredPosition = Vector2.Lerp(startPos, Vector2.zero, t);
                consRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRot, 0f, t));
                yield return null;
            }

            if (!isDraggingConsequence)
            {
                consRT.anchoredPosition = Vector2.zero;
                consRT.localRotation = Quaternion.identity;
            }
        }

        private void HandleReckoningInput()
        {
            // If sequence is still animating, allow holding or clicking left mouse / Space / Enter to fast-forward
            if (reckoningContinueButton == null || !reckoningContinueButton.gameObject.activeSelf || !reckoningContinueButton.interactable)
            {
                bool fastForward = false;
                try
                {
                    if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) fastForward = true;
                    if (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)) fastForward = true;
                }
                catch {}

                if (!fastForward)
                {
                    fastForward = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
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
            if (leftDown && reckoningContinueButton != null)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(reckoningContinueButton.GetComponent<RectTransform>(), mouseScreenPos, eventCam))
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
            catch {}

            if (!dismissKey)
            {
                dismissKey = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.E);
            }

            if (dismissKey)
            {
                OnReckoningContinueClicked();
            }
        }

        private Vector2 GetMouseScreenPosition()
        {
            try
            {
                if (Mouse.current != null) return Mouse.current.position.ReadValue();
            }
            catch {}
            return Input.mousePosition;
        }

        private bool GetMouseButtonDown()
        {
            try
            {
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            }
            catch {}
            return Input.GetMouseButtonDown(0);
        }

        private bool GetMouseButtonHeld()
        {
            try
            {
                if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            }
            catch {}
            return Input.GetMouseButton(0);
        }

        private bool GetMouseButtonUp()
        {
            try
            {
                if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame) return true;
            }
            catch {}
            return Input.GetMouseButtonUp(0);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isCardActive || hasChosen || cardContainer == null || !cardContainer.gameObject.activeSelf) return;
            isDragging = true;
            dragStartPos = eventData.position;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!isCardActive || hasChosen || cardContainer == null || !cardContainer.gameObject.activeSelf) return;
            if (isDragging)
            {
                isDragging = false;
                if (currentDragOffset.x < -swipeThreshold)
                {
                    ChooseLeft();
                }
                else if (currentDragOffset.x > swipeThreshold)
                {
                    ChooseRight();
                }
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!isCardActive || hasChosen || cardContainer == null || !cardContainer.gameObject.activeSelf) return;
            isDragging = true;
            dragStartPos = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isCardActive || hasChosen || cardContainer == null || !cardContainer.gameObject.activeSelf) return;
            currentDragOffset = eventData.position - dragStartPos;
            ApplyCardTransform(currentDragOffset);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!isCardActive || hasChosen || cardContainer == null || !cardContainer.gameObject.activeSelf) return;
            isDragging = false;

            if (currentDragOffset.x < -swipeThreshold)
            {
                ChooseLeft();
            }
            else if (currentDragOffset.x > swipeThreshold)
            {
                ChooseRight();
            }
        }

        private void ApplyCardTransform(Vector2 offset)
        {
            if (cardContainer == null) return;

            cardContainer.anchoredPosition = new Vector2(offset.x, offset.y * 0.25f);
            float rotationZ = Mathf.Clamp(-offset.x * 0.045f, -maxCardRotation, maxCardRotation);
            cardContainer.localRotation = Quaternion.Euler(0f, 0f, rotationZ);

            // Dynamic stamp alphas
            float leftNorm = Mathf.Clamp01(-offset.x / (swipeThreshold * 0.85f));
            float rightNorm = Mathf.Clamp01(offset.x / (swipeThreshold * 0.85f));

            if (rejectStampGroup != null) rejectStampGroup.alpha = leftNorm;
            if (acceptStampGroup != null) acceptStampGroup.alpha = rightNorm;
        }

        public void ShowNextEncounter(Action onComplete)
        {
            bool isEndgame = UpgradeManager.Instance != null && UpgradeManager.Instance.IsEndgameMaxed;
            bool isDay3Finale = ForceDay3FinaleForTesting || (isEndgame && RunSaveManager.HasInstance && RunSaveManager.Instance.postMaxDaysSurvived >= 3 && !RunSaveManager.Instance.finalPhoneEncounterDone);

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

            gameObject.SetActive(true);
            EnsureUIHierarchy();
            SetupScreenSpaceCamera(GetComponent<Canvas>());
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
        // DAY 3 ENDGAME RECKONING / STATS SCREEN WITH WEIGHING SCALE & FALLING DVDS
        // =========================================================================

        public void ShowReckoningStatsScreen(Action onComplete)
        {
            onCardFinished = onComplete;
            hasChosen = false;
            isDragging = false;
            isDraggingConsequence = false;
            currentDragOffset = Vector2.zero;

            ForceDay3FinaleForTesting = false;

            gameObject.SetActive(true);
            EnsureUIHierarchy();
            EnsureReckoningScaleUI();
            EnsureDvdSprites();
            EnsureProceduralAudioClips();
            SetupScreenSpaceCamera(GetComponent<Canvas>());
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
                    if (reckRT.sizeDelta.y < 610f) reckRT.sizeDelta = new Vector2(620f, 620f);
                }
                reckoningPanel.SetActive(true);
            }

            if (reckoningAnimationRoutine != null)
            {
                StopCoroutine(reckoningAnimationRoutine);
            }
            reckoningAnimationRoutine = StartCoroutine(PlayReckoningSequenceRoutine());
        }

        private IEnumerator PlayReckoningSequenceRoutine()
        {
            reckoningSpeedMultiplier = 1f;
            currentGreenDvds = 0;
            currentRedDvds = 0;

            if (leftDvdContainer != null)
            {
                foreach (Transform child in leftDvdContainer) Destroy(child.gameObject);
            }
            if (rightDvdContainer != null)
            {
                foreach (Transform child in rightDvdContainer) Destroy(child.gameObject);
            }

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
            if (reckoningSubtitleText != null) reckoningSubtitleText.text = "DAY 3 ENDGAME TELEPHONE EVALUATION";
            if (leftWeightText != null) leftWeightText.text = "<color=#55FF77><b>HOPE: 0</b></color>";
            if (rightWeightText != null) rightWeightText.text = "<color=#FF5555><b>DESPAIR: 0</b></color>";
            if (reckoningVerdictPromptText != null) reckoningVerdictPromptText.text = "WEIGHING THE BALANCE OF YOUR CHOICES...";

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
                ? "\"The static clears into a calm voice: 'You stood firm against the darkness. Through every hour of noise and exhaustion, you kept standing. The screen doesn't own your life. Will you step into tomorrow?'\""
                : "\"The receiver buzzes with cold, merciless feedback: 'You let the haze consume you. Running to chemicals, letting the room drown in filth and regret. The flicker has swallowed everything you were. It is time to end it.'\"";

            if (reckoningNarrativeText != null)
            {
                reckoningNarrativeText.text = "";
                for (int c = 1; c <= fullNarrative.Length; c++)
                {
                    reckoningNarrativeText.text = fullNarrative.Substring(0, c);
                    if (c % 2 == 1) PlayProceduralTick(1f + (c * 0.002f));
                    yield return WaitReckoningTime(0.012f);
                }
                reckoningNarrativeText.text = fullNarrative;
            }
            yield return WaitReckoningTime(0.25f);

            // 3. Stats list evaluation with bounce & falling DVDs
            int posDvds = Mathf.Max(1, pos);
            yield return AnimateStatRowEntry(reckoningPositiveChoicesText, "PHONE: HOPEFUL DECISIONS", posDvds, true, $"+{pos} Calls");
            yield return WaitReckoningTime(0.12f);

            int negDvds = Mathf.Max(1, neg);
            yield return AnimateStatRowEntry(reckoningNegativeChoicesText, "PHONE: ESCAPIST CHOICES", negDvds, false, $"+{neg} Calls");
            yield return WaitReckoningTime(0.12f);

            yield return AnimateStatRowEntry(reckoningDaysText, "ENDGAME: DAYS SURVIVED", days, true, $"+{days} Days Endured");
            yield return WaitReckoningTime(0.12f);

            bool isClean = dirtiness < 40f || trash >= 6;
            yield return AnimateStatRowEntry(reckoningCleanlinessText, "APARTMENT: HYGIENE & CARE", 2, isClean, isClean ? "Sanctuary Preserved" : "Drowning in Filth");
            yield return WaitReckoningTime(0.12f);

            bool isSober = substances <= 3;
            int subDvd = isSober ? 2 : (substances <= 9 ? 2 : 4);
            yield return AnimateStatRowEntry(reckoningSubstancesText, "SUBSTANCES: TOXICITY", subDvd, isSober, isSober ? "Restrained / Sober" : $"{substances} Consumed");
            yield return WaitReckoningTime(0.12f);

            bool isDepManaged = depression <= 40f;
            int depDvd = isDepManaged ? 2 : (depression >= 65f ? 3 : 1);
            yield return AnimateStatRowEntry(reckoningDepressionText, "MENTAL STATE: DEPRESSION", depDvd, isDepManaged, $"{depression:0}% ({(isDepManaged ? "Managed" : "Severe")})");
            yield return WaitReckoningTime(0.12f);

            if (missed >= 1.5f)
            {
                int missedDvd = Mathf.Clamp(Mathf.RoundToInt(missed), 1, 3);
                yield return AnimateStatRowEntry(reckoningMissedCallsText, "ISOLATION: MISSED CALLS", missedDvd, false, $"{missedDvd} Disconnected");
                yield return WaitReckoningTime(0.12f);
            }

            // 4. Settle scale & reveal final verdict
            yield return WaitReckoningTime(0.75f);

            bool finalVerdict = currentGreenDvds > currentRedDvds;
            lastVerdictHopeful = finalVerdict;

            if (reckoningScoreText != null)
            {
                reckoningScoreText.text = $"FINAL WEIGHING: <color={(finalVerdict ? "#55FF77" : "#FF5555")}><b>{currentGreenDvds} HOPE vs {currentRedDvds} DESPAIR</b></color>";
            }

            if (reckoningVerdictPromptText != null)
            {
                reckoningVerdictPromptText.text = finalVerdict
                    ? "<color=#55FF77><b>VERDICT: REACH OUT</b>\nHope and perseverance outweigh the dark. The cycle is broken.\n<b>GO TO THE FRONT DOOR TO STEP OUTSIDE.</b></color>"
                    : "<color=#FF5555><b>VERDICT: SURRENDER</b>\nThe isolation was too heavy. The room calls you back into the dark.\n<b>GO INTO THE ROOM TO THE TELEVISION TO END IT ALL.</b></color>";
            }

            PlayVerdictSound(finalVerdict);
            yield return WaitReckoningTime(0.4f);

            // 5. Reveal Continue Button
            if (reckoningContinueButton != null)
            {
                reckoningContinueButton.gameObject.SetActive(true);
                CanvasGroup cg = reckoningContinueButton.GetComponent<CanvasGroup>();
                RectTransform btnRT = reckoningContinueButton.GetComponent<RectTransform>();

                float btnElapsed = 0f;
                float btnDur = 0.35f;
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

        private IEnumerator AnimateStatRowEntry(TMP_Text textComp, string label, int dvdCount, bool isHope, string detail)
        {
            Transform rowTrans = textComp != null ? textComp.transform : null;
            Image bar = rowTrans != null ? rowTrans.GetComponentInParent<Image>() : null;
            Color baseBarColor = bar != null ? bar.color : Color.clear;
            Color flashColor = isHope ? new Color(0.22f, 1f, 0.35f, 0.35f) : new Color(1f, 0.18f, 0.22f, 0.35f);
            string hexColor = isHope ? "#55FF77" : "#FF5555";

            if (textComp != null)
            {
                textComp.text = $"{label}: <b><color={hexColor}>0 DVDs</color></b>";
            }

            for (int pt = 1; pt <= dvdCount; pt++)
            {
                if (textComp != null)
                {
                    textComp.text = $"{label}: <b><color={hexColor}>+{pt} DVDs ({detail})</color></b>";
                }

                if (bar != null) bar.color = flashColor;
                if (rowTrans != null) rowTrans.localScale = new Vector3(1.16f, 1.16f, 1f);

                PlayProceduralRowBounce(1f + (pt * 0.05f));
                StartCoroutine(DropDvdRoutine(isHope));

                float pElapsed = 0f;
                float pDur = 0.14f;
                while (pElapsed < pDur)
                {
                    pElapsed += Time.unscaledDeltaTime * reckoningSpeedMultiplier;
                    float t = Mathf.Clamp01(pElapsed / pDur);
                    if (rowTrans != null)
                    {
                        float s = Mathf.Lerp(1.16f, 1f, t);
                        rowTrans.localScale = new Vector3(s, s, 1f);
                    }
                    if (bar != null)
                    {
                        bar.color = Color.Lerp(flashColor, baseBarColor, t);
                    }
                    yield return null;
                }

                if (rowTrans != null) rowTrans.localScale = Vector3.one;
                if (bar != null) bar.color = baseBarColor;

                yield return WaitReckoningTime(0.22f);
            }
        }

        private void UpdateScaleVisuals(float dt)
        {
            if (scaleBeam == null) return;

            // Target angle based on DVD weight difference
            float weightDiff = (float)(currentGreenDvds - currentRedDvds);
            // Positive diff tilts counter-clockwise (left dips down, right goes up)
            scaleTargetAngle = Mathf.Clamp(weightDiff * 2.2f, -16f, 16f);

            // Spring-damper physics
            float stiffness = 24f;
            float damping = 5.2f;
            float torque = (scaleTargetAngle - scaleCurrentAngle) * stiffness;
            scaleAngularVelocity += torque * dt;
            scaleAngularVelocity -= scaleAngularVelocity * damping * dt;
            scaleCurrentAngle += scaleAngularVelocity * dt;

            // Apply rotation to beam
            scaleBeam.localEulerAngles = new Vector3(0f, 0f, scaleCurrentAngle);

            // Calculate beam tip positions in ScaleContainer space
            float rad = scaleCurrentAngle * Mathf.Deg2Rad;
            float halfBeam = 175f;
            Vector2 beamPivot = scaleBeam.anchoredPosition;
            Vector2 leftTip = beamPivot + new Vector2(-halfBeam * Mathf.Cos(rad), -halfBeam * Mathf.Sin(rad));
            Vector2 rightTip = beamPivot + new Vector2(halfBeam * Mathf.Cos(rad), halfBeam * Mathf.Sin(rad));

            // Hang pans vertically
            float chainDrop = 55f;
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

            // Update chain lines connecting tips to trays
            UpdateChainLine(leftChainA, leftTip, leftPan, -50f);
            UpdateChainLine(leftChainB, leftTip, leftPan, 50f);
            UpdateChainLine(rightChainA, rightTip, rightPan, -50f);
            UpdateChainLine(rightChainB, rightTip, rightPan, 50f);
        }

        private void UpdateChainLine(RectTransform chain, Vector2 tip, RectTransform pan, float panOffsetX)
        {
            if (chain == null || pan == null) return;
            Vector2 panAttach = pan.anchoredPosition + new Vector2(panOffsetX, 10f);
            Vector2 dir = panAttach - tip;
            float dist = dir.magnitude;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            chain.anchoredPosition = tip;
            chain.sizeDelta = new Vector2(dist, 1.5f);
            chain.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        private IEnumerator DropDvdRoutine(bool isGreen, float dropDelay = 0f)
        {
            if (dropDelay > 0f) yield return WaitReckoningTime(dropDelay);

            EnsureDvdSprites();
            Sprite sprite = isGreen ? greenDvdSprite : redDvdSprite;
            RectTransform targetPan = isGreen ? leftPan : rightPan;
            RectTransform container = isGreen ? leftDvdContainer : rightDvdContainer;
            if (targetPan == null || container == null) yield break;

            GameObject dvdObj = new GameObject(isGreen ? "GreenDVD" : "RedDVD", typeof(RectTransform), typeof(Image));
            dvdObj.transform.SetParent(scaleContainer, false);
            RectTransform dvdRT = dvdObj.GetComponent<RectTransform>();
            Image img = dvdObj.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            dvdRT.sizeDelta = new Vector2(48f, 26f);

            float spawnX = targetPan.anchoredPosition.x + UnityEngine.Random.Range(-16f, 16f);
            float startY = 160f;
            dvdRT.anchoredPosition = new Vector2(spawnX, startY);
            float randomRot = UnityEngine.Random.Range(-12f, 12f);
            dvdRT.localEulerAngles = new Vector3(0f, 0f, randomRot);

            float velY = -80f;
            float gravity = -1200f;
            float currentY = startY;

            int countOnSide = isGreen ? currentGreenDvds : currentRedDvds;
            float stackOffsetY = 10f + (countOnSide * 5f);
            float stackOffsetX = UnityEngine.Random.Range(-26f, 26f);

            while (true)
            {
                float dt = Time.unscaledDeltaTime * reckoningSpeedMultiplier;
                velY += gravity * dt;
                currentY += velY * dt;

                float landingY = targetPan.anchoredPosition.y + stackOffsetY;
                if (currentY <= landingY)
                {
                    currentY = landingY;
                    break;
                }

                float followX = targetPan.anchoredPosition.x + stackOffsetX;
                dvdRT.anchoredPosition = new Vector2(Mathf.Lerp(dvdRT.anchoredPosition.x, followX, dt * 10f), currentY);
                yield return null;
            }

            dvdObj.transform.SetParent(container, true);
            dvdRT.anchoredPosition = new Vector2(stackOffsetX, stackOffsetY);
            dvdRT.localEulerAngles = new Vector3(0f, 0f, randomRot * 0.4f);

            if (isGreen) currentGreenDvds++;
            else currentRedDvds++;

            if (leftWeightText != null) leftWeightText.text = $"<color=#55FF77><b>HOPE: {currentGreenDvds}</b></color>";
            if (rightWeightText != null) rightWeightText.text = $"<color=#FF5555><b>DESPAIR: {currentRedDvds}</b></color>";

            scaleAngularVelocity += isGreen ? 55f : -55f;
            PlayProceduralImpactSound(isGreen);

            // Squash & stretch on landing
            float sqDur = 0.12f;
            float sqElapsed = 0f;
            while (sqElapsed < sqDur)
            {
                sqElapsed += Time.unscaledDeltaTime * reckoningSpeedMultiplier;
                float t = Mathf.Clamp01(sqElapsed / sqDur);
                float sx = Mathf.Lerp(1.24f, 1f, t);
                float sy = Mathf.Lerp(0.72f, 1f, t);
                dvdRT.localScale = new Vector3(sx, sy, 1f);
                yield return null;
            }
            dvdRT.localScale = Vector3.one;
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

            // 1. Draw Disc Ring (Torus) - Tilted Ellipse
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
                        c.a = ringColor.a;

                        float edgeA = Mathf.Clamp01((1.0f - outerDist) * 8f) * Mathf.Clamp01((innerDist - 0.75f) * 8f);
                        c.a *= edgeA;

                        pixels[y * width + x] = c;
                    }
                }
            }

            void FillRect(int rx, int ry, int rw, int rh, Color col)
            {
                for (int py = ry; py < ry + rh && py < height; py++)
                {
                    if (py < 0) continue;
                    for (int px = rx; px < rx + rw && px < width; px++)
                    {
                        if (px < 0) continue;
                        pixels[py * width + px] = col;
                    }
                }
            }

            // 2. Draw Bold Letters "D V D"
            int letterY = 38;
            int letterH = 28;
            int thickness = 6;

            void DrawLetterD(int startX)
            {
                int w = 24;
                FillRect(startX, letterY, thickness, letterH, lettersColor);
                FillRect(startX, letterY + letterH - thickness, w - 4, thickness, lettersColor);
                FillRect(startX, letterY, w - 4, thickness, lettersColor);
                FillRect(startX + w - thickness, letterY + 4, thickness, letterH - 8, lettersColor);
                FillRect(startX + w - thickness - 2, letterY + letterH - thickness, 2, thickness, lettersColor);
                FillRect(startX + w - thickness - 2, letterY, 2, thickness, lettersColor);
            }

            DrawLetterD(32);

            int vx = 64;
            int vw = 32;
            for (int step = 0; step < letterH; step++)
            {
                int py = letterY + step;
                float progress = (float)step / letterH;
                int leftX = Mathf.RoundToInt(Mathf.Lerp(vx + (vw / 2) - 2, vx, progress));
                int rightX = Mathf.RoundToInt(Mathf.Lerp(vx + (vw / 2), vx + vw - thickness, progress));

                FillRect(leftX, py, thickness, 1, lettersColor);
                FillRect(rightX, py, thickness, 1, lettersColor);
            }

            DrawLetterD(104);

            // 3. Draw Sub-Bar "VIDEO" pill/underline
            int barY = 22;
            int barH = 5;
            FillRect(36, barY, 88, barH, lettersColor);

            tex.SetPixels(pixels);
            tex.Apply();

            Sprite created = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            created.name = spriteName;
            return created;
        }

        public void OnReckoningContinueClicked()
        {
            if (reckoningAnimationRoutine != null)
            {
                StopCoroutine(reckoningAnimationRoutine);
                reckoningAnimationRoutine = null;
            }

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

            // 1. Update front door interactable
            SceneTransitionOut[] transitions = FindObjectsOfType<SceneTransitionOut>(true);
            foreach (var trans in transitions)
            {
                if (trans != null && trans.sceneToLoad == "BarGameBarni")
                {
                    InteractableObject io = trans.GetComponent<InteractableObject>();
                    if (io != null)
                    {
                        if (isHopeful)
                        {
                            io.hoverText = "Front Door: Reach Out (Step Outside)";
                        }
                        else
                        {
                            io.hoverText = "Front Door (Locked: Return to TV)";
                        }
                    }
                }
            }

            // 2. Update TV screen / remote interactable
            UpgradeManager upg = FindObjectOfType<UpgradeManager>(true);
            if (upg != null)
            {
                InteractableObject tvIo = upg.GetComponentInChildren<InteractableObject>(true);
                if (tvIo != null)
                {
                    if (!isHopeful)
                    {
                        tvIo.hoverText = "Television: End It All (Surrender)";
                    }
                }
            }

            // 3. Show crisp feedback in GridCrawlerController interact text
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

            if (contactTitleText != null) contactTitleText.text = card.contactName;
            if (contactRoleText != null) contactRoleText.text = card.contactRole;
            if (narrativeBodyText != null) narrativeBodyText.text = $"\"{card.narrativeText}\"";

            string lLabel = !string.IsNullOrEmpty(card.leftLabel) ? card.leftLabel : "REJECT";
            string rLabel = !string.IsNullOrEmpty(card.rightLabel) ? card.rightLabel : "ACCEPT";

            if (leftSwipeText != null)
            {
                leftSwipeText.text = $"<b>◀ {lLabel}</b>\n<size=12><color=#ffaaaa>[ SWIPE LEFT ]</color></size>";
            }
            if (rightSwipeText != null)
            {
                rightSwipeText.text = $"<b>{rLabel} ▶</b>\n<size=12><color=#aaffaa>[ SWIPE RIGHT ]</color></size>";
            }

            if (portraitImage != null)
            {
                portraitImage.sprite = card.portraitSprite;
                portraitImage.color = card.portraitSprite != null ? Color.white : new Color(0.12f, 0.16f, 0.22f, 0.8f);
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
            float targetX = isLeft ? -750f : 750f;
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
            HideSideIndicators();

            // Display outcome in consequence panel
            DisplayConsequence(isLeft);
        }

        private void DisplayConsequence(bool isLeft)
        {
            if (currentCard == null) return;

            string outcomeText = isLeft ? currentCard.leftOutcomeText : currentCard.rightOutcomeText;
            decimal moneyDelta = (decimal)(isLeft ? currentCard.leftMoneyDelta : currentCard.rightMoneyDelta);
            float depDelta = isLeft ? currentCard.leftDepressionDelta : currentCard.rightDepressionDelta;
            float tiredDelta = isLeft ? currentCard.leftTirednessDelta : currentCard.rightTirednessDelta;
            bool isPositive = isLeft ? currentCard.leftIsPositive : currentCard.rightIsPositive;

            // Apply Secret Counters
            if (RunSaveManager.HasInstance)
            {
                if (isPositive) RunSaveManager.Instance.positiveSecretCounter++;
                else RunSaveManager.Instance.negativeSecretCounter++;
                RunSaveManager.Instance.RecordStoryElementEncountered(currentCard.id);
            }

            // Apply normal stat deltas
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
                    consequenceTitleText.text = isLeft ? $"CALL REJECTED: {currentCard.contactName}" : $"CALL ACCEPTED: {currentCard.contactName}";
                    consequenceTitleText.color = isPositive ? new Color(0.35f, 0.95f, 0.45f) : new Color(0.95f, 0.45f, 0.35f);
                }

                if (consequenceBodyText != null) consequenceBodyText.text = outcomeText;

                if (moneyDeltaText != null)
                {
                    if (moneyDelta > 0m)
                    {
                        moneyDeltaText.text = $"Cash: +{CurrencyFormatter.Format(moneyDelta)}";
                        moneyDeltaText.color = new Color(0.35f, 0.95f, 0.45f);
                    }
                    else if (moneyDelta < 0m)
                    {
                        moneyDeltaText.text = $"Cash: -{CurrencyFormatter.Format(Math.Abs(moneyDelta))}";
                        moneyDeltaText.color = new Color(0.95f, 0.35f, 0.35f);
                    }
                    else
                    {
                        moneyDeltaText.text = "Cash: $0";
                        moneyDeltaText.color = new Color(0.7f, 0.7f, 0.7f);
                    }
                }

                if (depressionDeltaText != null)
                {
                    if (depDelta < 0)
                    {
                        depressionDeltaText.text = $"Depression: {depDelta:0}%";
                        depressionDeltaText.color = new Color(0.35f, 0.95f, 0.45f);
                    }
                    else if (depDelta > 0)
                    {
                        depressionDeltaText.text = $"Depression: +{depDelta:0}%";
                        depressionDeltaText.color = new Color(0.95f, 0.35f, 0.35f);
                    }
                    else
                    {
                        depressionDeltaText.text = "Depression: Unchanged";
                        depressionDeltaText.color = new Color(0.7f, 0.7f, 0.7f);
                    }
                }

                if (tirednessDeltaText != null)
                {
                    if (tiredDelta < 0)
                    {
                        tirednessDeltaText.text = $"Tiredness: {tiredDelta:0}";
                        tirednessDeltaText.color = new Color(0.35f, 0.95f, 0.45f);
                    }
                    else if (tiredDelta > 0)
                    {
                        tirednessDeltaText.text = $"Tiredness: +{tiredDelta:0}";
                        tirednessDeltaText.color = new Color(0.95f, 0.75f, 0.35f);
                    }
                    else
                    {
                        tirednessDeltaText.text = "Tiredness: Unchanged";
                        tirednessDeltaText.color = new Color(0.7f, 0.7f, 0.7f);
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
                }
                consequencePanel.SetActive(false);
            }

            CloseCard();
        }

        private void CloseCard()
        {
            isCardActive = false;
            hasChosen = false;
            isDragging = false;
            isDraggingConsequence = false;
            currentDragOffset = Vector2.zero;
            consequenceDragOffset = Vector2.zero;

            HideSideIndicators();

            if (consequencePanel != null) consequencePanel.SetActive(false);
            if (reckoningPanel != null) reckoningPanel.SetActive(false);
            gameObject.SetActive(false);

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            Action callback = onCardFinished;
            onCardFinished = null;
            callback?.Invoke();
        }

        private void EnsureCursorVisible()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
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

            // Fallback default
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
                float env = 1f - (float)i / sampleCount; // linear decay
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.28f;
            }

            AudioClip clip = AudioClip.Create("PhoneBeep", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            uiAudio.PlayOneShot(clip, 0.8f);
        }

        // =========================================================================
        // PROCEDURAL CRT UI HIERARCHY & SCREEN SPACE CAMERA CONFIGURATION
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

            // Note: Do NOT force transform.localScale = Vector3.one here; CanvasScaler dynamically adjusts scale in ScreenSpaceCamera!

            // 1. Check user-assigned targetCamera or canvas.worldCamera (must not be an offscreen render-texture camera like TV bouncer)
            Camera cam = null;
            if (targetCamera != null && targetCamera.targetTexture == null)
            {
                cam = targetCamera;
            }
            else if (canvas.worldCamera != null && canvas.worldCamera.targetTexture == null)
            {
                cam = canvas.worldCamera;
            }

            // 2. If not assigned or assigned to TV camera, bind to player camera with SmoothCameraFollow
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

            // 3. Fallback to Camera.main if it is a valid screen-rendering camera
            if (cam == null)
            {
                Camera main = Camera.main;
                if (main != null && main.targetTexture == null)
                {
                    cam = main;
                }
            }

            // 4. Fallback: Search all active or inactive cameras for one tagged MainCamera or without targetTexture
            if (cam == null)
            {
                Camera[] allCams = Resources.FindObjectsOfTypeAll<Camera>();
                foreach (Camera c in allCams)
                {
                    if (c == null) continue;
                    if (c.targetTexture != null) continue; // Skip TV render camera
                    if (c.cameraType != CameraType.Game) continue; // Skip preview/scene cameras
                    if (c.gameObject.scene.name != gameObject.scene.name) continue; // Must be in this scene

                    if (c.CompareTag("MainCamera"))
                    {
                        cam = c;
                        break;
                    }
                    if (cam == null)
                    {
                        cam = c;
                    }
                }
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            if (cam != null)
            {
                targetCamera = cam;
                canvas.worldCamera = cam;

                // CRITICAL FOR TEXTMESHPRO: TexCoord1 (UV2), Normal, Tangent channels
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

                // CRITICAL: Safe plane distance matching death sequence
                float safeDistance = Mathf.Clamp(cam.nearClipPlane + 0.15f, 0.20f, 0.35f);
                canvas.planeDistance = safeDistance;

                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer < 0) uiLayer = 5;
                cam.cullingMask |= (1 << uiLayer) | (1 << gameObject.layer);

                UniversalAdditionalCameraData camData = cam.GetComponent<UniversalAdditionalCameraData>();
                if (camData == null)
                {
                    camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }
                if (camData != null)
                {
                    camData.renderPostProcessing = true;
                }
            }
            else
            {
                canvas.planeDistance = 0.25f;
            }

            canvas.sortingOrder = 32767;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
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

        /// <summary>
        /// Non-destructively locates or binds UI template elements.
        /// CRITICAL: NEVER overwrites user-customized sizes, anchors, colors, or font settings at runtime!
        /// </summary>
        public void EnsureUIHierarchy()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            SetupScreenSpaceCamera(canvas);

            // Dark CRT vignette backdrop
            Transform bgTrans = transform.Find("DarkVignetteBackdrop");
            if (bgTrans == null)
            {
                GameObject bgObj = new GameObject("DarkVignetteBackdrop", typeof(RectTransform), typeof(Image));
                bgObj.transform.SetParent(transform, false);
                bgTrans = bgObj.transform;
                RectTransform bgRT = bgTrans.GetComponent<RectTransform>();
                bgRT.anchorMin = Vector2.zero;
                bgRT.anchorMax = Vector2.one;
                bgRT.pivot = new Vector2(0.5f, 0.5f);
                bgRT.sizeDelta = Vector2.zero;
                bgRT.anchoredPosition = Vector2.zero;
                bgRT.localScale = Vector3.one;
                Image bgImg = bgTrans.GetComponent<Image>();
                if (bgImg == null) bgImg = bgTrans.gameObject.AddComponent<Image>();
                bgImg.color = new Color(0.04f, 0.06f, 0.08f, 0.88f);
                bgImg.raycastTarget = false;
                bgTrans.SetAsFirstSibling();
            }

            // 1. Left and Right Swipe Visual Indicators
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
                leftSwipeIndicator.anchorMin = new Vector2(0f, 0.5f);
                leftSwipeIndicator.anchorMax = new Vector2(0f, 0.5f);
                leftSwipeIndicator.pivot = new Vector2(0f, 0.5f);
                leftSwipeIndicator.sizeDelta = new Vector2(180f, 130f);
                leftSwipeIndicator.anchoredPosition = new Vector2(40f, 0f);
                leftSwipeIndicator.localScale = Vector3.one;

                Image leftImg = leftSwipeIndicator.GetComponent<Image>();
                if (leftImg == null) leftImg = leftSwipeIndicator.gameObject.AddComponent<Image>();
                leftImg.color = new Color(0.12f, 0.04f, 0.05f, 0.85f);
                leftImg.raycastTarget = false;

                leftSwipeGroup = leftSwipeIndicator.GetComponent<CanvasGroup>();
                if (leftSwipeGroup == null) leftSwipeGroup = leftSwipeIndicator.gameObject.AddComponent<CanvasGroup>();
                leftSwipeGroup.alpha = 0.35f;
                leftSwipeGroup.blocksRaycasts = false;
                leftSwipeGroup.interactable = false;

                leftSwipeText = CreateTMPText("Label", leftSwipeIndicator, Vector2.zero, new Vector2(170f, 120f), 18, TextAlignmentOptions.Center, new Color(1f, 0.45f, 0.45f));
                leftSwipeText.text = "<b>◀ REJECT</b>\n<size=12><color=#ffaaaa>[ SWIPE LEFT ]</color></size>";
            }
            else
            {
                if (leftSwipeGroup == null) leftSwipeGroup = leftSwipeIndicator.GetComponent<CanvasGroup>();
                if (leftSwipeText == null) leftSwipeText = leftSwipeIndicator.GetComponentInChildren<TMP_Text>(true);
            }

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
                rightSwipeIndicator.anchorMin = new Vector2(1f, 0.5f);
                rightSwipeIndicator.anchorMax = new Vector2(1f, 0.5f);
                rightSwipeIndicator.pivot = new Vector2(1f, 0.5f);
                rightSwipeIndicator.sizeDelta = new Vector2(180f, 130f);
                rightSwipeIndicator.anchoredPosition = new Vector2(-40f, 0f);
                rightSwipeIndicator.localScale = Vector3.one;

                Image rightImg = rightSwipeIndicator.GetComponent<Image>();
                if (rightImg == null) rightImg = rightSwipeIndicator.gameObject.AddComponent<Image>();
                rightImg.color = new Color(0.04f, 0.12f, 0.06f, 0.85f);
                rightImg.raycastTarget = false;

                rightSwipeGroup = rightSwipeIndicator.GetComponent<CanvasGroup>();
                if (rightSwipeGroup == null) rightSwipeGroup = rightSwipeIndicator.gameObject.AddComponent<CanvasGroup>();
                rightSwipeGroup.alpha = 0.35f;
                rightSwipeGroup.blocksRaycasts = false;
                rightSwipeGroup.interactable = false;

                rightSwipeText = CreateTMPText("Label", rightSwipeIndicator, Vector2.zero, new Vector2(170f, 120f), 18, TextAlignmentOptions.Center, new Color(0.45f, 1f, 0.55f));
                rightSwipeText.text = "<b>ACCEPT ▶</b>\n<size=12><color=#aaffaa>[ SWIPE RIGHT ]</color></size>";
            }
            else
            {
                if (rightSwipeGroup == null) rightSwipeGroup = rightSwipeIndicator.GetComponent<CanvasGroup>();
                if (rightSwipeText == null) rightSwipeText = rightSwipeIndicator.GetComponentInChildren<TMP_Text>(true);
            }

            // 2. Card Container Template
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
                cardContainer.anchorMin = new Vector2(0.5f, 0.5f);
                cardContainer.anchorMax = new Vector2(0.5f, 0.5f);
                cardContainer.pivot = new Vector2(0.5f, 0.5f);
                cardContainer.sizeDelta = new Vector2(440f, 620f);
                cardContainer.anchoredPosition = Vector2.zero;
                cardContainer.localScale = Vector3.one;

                Image cardBg = cardContainer.GetComponent<Image>();
                if (cardBg == null) cardBg = cardContainer.gameObject.AddComponent<Image>();
                cardBg.color = new Color(0.08f, 0.12f, 0.16f, 0.98f);
                cardBg.raycastTarget = true;
            }

            if (cardCanvasGroup == null && cardContainer != null)
            {
                cardCanvasGroup = cardContainer.GetComponent<CanvasGroup>();
                if (cardCanvasGroup == null) cardCanvasGroup = cardContainer.gameObject.AddComponent<CanvasGroup>();
            }
            if (cardCanvasGroup != null)
            {
                cardCanvasGroup.blocksRaycasts = true;
                cardCanvasGroup.interactable = true;
            }

            // HeaderPanel
            Transform headerTrans = cardContainer != null ? cardContainer.Find("HeaderPanel") : null;
            if (headerTrans == null && cardContainer != null)
            {
                GameObject headerObj = new GameObject("HeaderPanel", typeof(RectTransform), typeof(Image));
                headerObj.transform.SetParent(cardContainer, false);
                headerTrans = headerObj.transform;
                RectTransform headerRT = headerTrans.GetComponent<RectTransform>();
                headerRT.anchorMin = new Vector2(0f, 1f);
                headerRT.anchorMax = new Vector2(1f, 1f);
                headerRT.pivot = new Vector2(0.5f, 1f);
                headerRT.sizeDelta = new Vector2(0f, 80f);
                headerRT.anchoredPosition = Vector2.zero;
                headerRT.localScale = Vector3.one;

                Image hImg = headerTrans.GetComponent<Image>();
                if (hImg == null) hImg = headerTrans.gameObject.AddComponent<Image>();
                hImg.color = new Color(0.12f, 0.18f, 0.24f, 1f);
                hImg.raycastTarget = false;
            }

            if (contactTitleText == null && headerTrans != null)
            {
                Transform t = headerTrans.Find("ContactTitle");
                if (t != null) contactTitleText = t.GetComponent<TMP_Text>();
                else contactTitleText = CreateTMPText("ContactTitle", headerTrans, new Vector2(0f, -10f), new Vector2(400f, 32f), 20f, TextAlignmentOptions.Center, new Color(0.4f, 0.95f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }

            if (contactRoleText == null && headerTrans != null)
            {
                Transform t = headerTrans.Find("ContactRole");
                if (t != null) contactRoleText = t.GetComponent<TMP_Text>();
                else contactRoleText = CreateTMPText("ContactRole", headerTrans, new Vector2(0f, -46f), new Vector2(400f, 22f), 12f, TextAlignmentOptions.Center, new Color(0.7f, 0.8f, 0.9f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }

            // Portrait Frame & Image
            Transform portFrameTrans = cardContainer != null ? cardContainer.Find("PortraitFrame") : null;
            if (portFrameTrans == null && cardContainer != null)
            {
                GameObject portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image));
                portraitFrame.transform.SetParent(cardContainer, false);
                portFrameTrans = portraitFrame.transform;
                RectTransform portRT = portFrameTrans.GetComponent<RectTransform>();
                portRT.anchorMin = new Vector2(0.5f, 0.5f);
                portRT.anchorMax = new Vector2(0.5f, 0.5f);
                portRT.pivot = new Vector2(0.5f, 0.5f);
                portRT.sizeDelta = new Vector2(170f, 170f);
                portRT.anchoredPosition = new Vector2(0f, 85f);
                portRT.localScale = Vector3.one;

                Image pfImg = portFrameTrans.GetComponent<Image>();
                if (pfImg == null) pfImg = portFrameTrans.gameObject.AddComponent<Image>();
                pfImg.color = new Color(0.04f, 0.06f, 0.08f, 1f);
                pfImg.raycastTarget = false;
            }

            if (portraitImage == null && portFrameTrans != null)
            {
                Transform t = portFrameTrans.Find("PortraitImage");
                if (t != null) portraitImage = t.GetComponent<Image>();
                else
                {
                    GameObject portImgObj = new GameObject("PortraitImage", typeof(RectTransform), typeof(Image));
                    portImgObj.transform.SetParent(portFrameTrans, false);
                    portraitImage = portImgObj.GetComponent<Image>();
                    RectTransform portImgRT = portraitImage.rectTransform;
                    portImgRT.anchorMin = new Vector2(0.5f, 0.5f);
                    portImgRT.anchorMax = new Vector2(0.5f, 0.5f);
                    portImgRT.pivot = new Vector2(0.5f, 0.5f);
                    portImgRT.sizeDelta = new Vector2(154f, 154f);
                    portImgRT.anchoredPosition = Vector2.zero;
                    portImgRT.localScale = Vector3.one;
                    portraitImage.color = new Color(0.12f, 0.16f, 0.22f, 0.8f);
                    portraitImage.raycastTarget = false;
                }
            }

            // Narrative Text Body
            if (narrativeBodyText == null && cardContainer != null)
            {
                Transform t = cardContainer.Find("NarrativeText");
                if (t != null) narrativeBodyText = t.GetComponent<TMP_Text>();
                else narrativeBodyText = CreateTMPText("NarrativeText", cardContainer, new Vector2(0f, -120f), new Vector2(380f, 190f), 15f, TextAlignmentOptions.Center, Color.white);
            }

            // Stamps
            if (rejectStamp == null && cardContainer != null)
            {
                Transform t = cardContainer.Find("RejectStamp");
                if (t != null) rejectStamp = t.GetComponent<RectTransform>();
                else
                {
                    GameObject rejObj = new GameObject("RejectStamp", typeof(RectTransform), typeof(CanvasGroup));
                    rejObj.transform.SetParent(cardContainer, false);
                    rejectStamp = rejObj.GetComponent<RectTransform>();
                    rejectStamp.anchorMin = new Vector2(0.5f, 0.5f);
                    rejectStamp.anchorMax = new Vector2(0.5f, 0.5f);
                    rejectStamp.pivot = new Vector2(0.5f, 0.5f);
                    rejectStamp.sizeDelta = new Vector2(150f, 45f);
                    rejectStamp.anchoredPosition = new Vector2(-110f, 170f);
                    rejectStamp.localRotation = Quaternion.Euler(0f, 0f, 15f);
                    rejectStamp.localScale = Vector3.one;
                    CreateTMPText("RejectLabel", rejObj.transform, Vector2.zero, new Vector2(150f, 45f), 24f, TextAlignmentOptions.Center, new Color(0.95f, 0.3f, 0.3f)).text = "[ REJECT ]";
                }
            }
            if (rejectStampGroup == null && rejectStamp != null)
            {
                rejectStampGroup = rejectStamp.GetComponent<CanvasGroup>();
                if (rejectStampGroup == null) rejectStampGroup = rejectStamp.gameObject.AddComponent<CanvasGroup>();
            }

            if (acceptStamp == null && cardContainer != null)
            {
                Transform t = cardContainer.Find("AcceptStamp");
                if (t != null) acceptStamp = t.GetComponent<RectTransform>();
                else
                {
                    GameObject accObj = new GameObject("AcceptStamp", typeof(RectTransform), typeof(CanvasGroup));
                    accObj.transform.SetParent(cardContainer, false);
                    acceptStamp = accObj.GetComponent<RectTransform>();
                    acceptStamp.anchorMin = new Vector2(0.5f, 0.5f);
                    acceptStamp.anchorMax = new Vector2(0.5f, 0.5f);
                    acceptStamp.pivot = new Vector2(0.5f, 0.5f);
                    acceptStamp.sizeDelta = new Vector2(150f, 45f);
                    acceptStamp.anchoredPosition = new Vector2(110f, 170f);
                    acceptStamp.localRotation = Quaternion.Euler(0f, 0f, -15f);
                    acceptStamp.localScale = Vector3.one;
                    CreateTMPText("AcceptLabel", accObj.transform, Vector2.zero, new Vector2(150f, 45f), 24f, TextAlignmentOptions.Center, new Color(0.3f, 0.95f, 0.4f)).text = "[ ACCEPT ]";
                }
            }
            if (acceptStampGroup == null && acceptStamp != null)
            {
                acceptStampGroup = acceptStamp.GetComponent<CanvasGroup>();
                if (acceptStampGroup == null) acceptStampGroup = acceptStamp.gameObject.AddComponent<CanvasGroup>();
            }

            // Choice Buttons (hidden if present)
            if (cardContainer != null)
            {
                Transform leftBtnTrans = cardContainer.Find("LeftChoiceButton");
                if (leftBtnTrans != null) leftBtnTrans.gameObject.SetActive(false);
                Transform rightBtnTrans = cardContainer.Find("RightChoiceButton");
                if (rightBtnTrans != null) rightBtnTrans.gameObject.SetActive(false);
            }

            // 3. Consequence Panel Template
            if (consequencePanel == null)
            {
                Transform t = transform.Find("ConsequencePanel");
                if (t != null) consequencePanel = t.gameObject;
                else
                {
                    consequencePanel = new GameObject("ConsequencePanel", typeof(RectTransform), typeof(Image));
                    consequencePanel.transform.SetParent(transform, false);
                    RectTransform consRT = consequencePanel.GetComponent<RectTransform>();
                    consRT.anchorMin = new Vector2(0.5f, 0.5f);
                    consRT.anchorMax = new Vector2(0.5f, 0.5f);
                    consRT.pivot = new Vector2(0.5f, 0.5f);
                    consRT.sizeDelta = new Vector2(540f, 460f);
                    consRT.anchoredPosition = Vector2.zero;
                    consRT.localScale = Vector3.one;
                    consRT.localRotation = Quaternion.identity;

                    Image consImg = consequencePanel.GetComponent<Image>();
                    if (consImg == null) consImg = consequencePanel.AddComponent<Image>();
                    consImg.color = new Color(0.06f, 0.10f, 0.14f, 0.98f);
                    consImg.raycastTarget = true;
                }
            }

            if (consequencePanel != null)
            {
                Transform cpTrans = consequencePanel.transform;
                if (consequenceTitleText == null)
                {
                    Transform t = cpTrans.Find("ConsTitle");
                    if (t != null) consequenceTitleText = t.GetComponent<TMP_Text>();
                    else consequenceTitleText = CreateTMPText("ConsTitle", cpTrans, new Vector2(0f, -25f), new Vector2(500f, 36f), 20f, TextAlignmentOptions.Center, Color.green, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }
                if (consequenceBodyText == null)
                {
                    Transform t = cpTrans.Find("ConsBody");
                    if (t != null) consequenceBodyText = t.GetComponent<TMP_Text>();
                    else consequenceBodyText = CreateTMPText("ConsBody", cpTrans, new Vector2(0f, -75f), new Vector2(480f, 110f), 15f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }
                if (moneyDeltaText == null)
                {
                    Transform t = cpTrans.Find("MoneyDelta") ?? cpTrans.Find("StatsBox/MoneyDelta");
                    if (t != null) moneyDeltaText = t.GetComponent<TMP_Text>();
                    else moneyDeltaText = CreateTMPText("MoneyDelta", cpTrans, new Vector2(0f, -200f), new Vector2(460f, 28f), 16f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }
                if (depressionDeltaText == null)
                {
                    Transform t = cpTrans.Find("DepDelta") ?? cpTrans.Find("StatsBox/DepDelta");
                    if (t != null) depressionDeltaText = t.GetComponent<TMP_Text>();
                    else depressionDeltaText = CreateTMPText("DepDelta", cpTrans, new Vector2(0f, -232f), new Vector2(460f, 28f), 16f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }
                if (tirednessDeltaText == null)
                {
                    Transform t = cpTrans.Find("TiredDelta") ?? cpTrans.Find("StatsBox/TiredDelta");
                    if (t != null) tirednessDeltaText = t.GetComponent<TMP_Text>();
                    else tirednessDeltaText = CreateTMPText("TiredDelta", cpTrans, new Vector2(0f, -264f), new Vector2(460f, 28f), 16f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }
                if (continueButton == null)
                {
                    Transform t = cpTrans.Find("ContinueButton");
                    if (t != null) continueButton = t.GetComponent<Button>();
                    else
                    {
                        GameObject contBtn = CreateButton("ContinueButton", cpTrans, new Vector2(0f, 25f), new Vector2(340f, 48f), new Color(0.18f, 0.35f, 0.45f), OnContinueClicked, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
                        continueButton = contBtn.GetComponent<Button>();
                        CreateTMPText("Label", contBtn.transform, Vector2.zero, new Vector2(320f, 38f), 14f, TextAlignmentOptions.Center, Color.white).text = "[ ◀ SWIPE OR CLICK TO RETURN ▶ ]";
                    }
                }
                if (continueButton != null)
                {
                    continueButton.onClick.RemoveListener(OnContinueClicked);
                    continueButton.onClick.AddListener(OnContinueClicked);
                }
            }

            // 4. Reckoning Panel Template
            if (reckoningPanel == null)
            {
                Transform t = transform.Find("ReckoningPanel");
                if (t != null) reckoningPanel = t.gameObject;
                else
                {
                    reckoningPanel = new GameObject("ReckoningPanel", typeof(RectTransform), typeof(Image));
                    reckoningPanel.transform.SetParent(transform, false);
                    RectTransform reckRT = reckoningPanel.GetComponent<RectTransform>();
                    reckRT.anchorMin = new Vector2(0.5f, 0.5f);
                    reckRT.anchorMax = new Vector2(0.5f, 0.5f);
                    reckRT.pivot = new Vector2(0.5f, 0.5f);
                    reckRT.sizeDelta = new Vector2(580f, 560f);
                    reckRT.anchoredPosition = Vector2.zero;
                    reckRT.localScale = Vector3.one;
                    reckRT.localRotation = Quaternion.identity;

                    Image reckImg = reckoningPanel.GetComponent<Image>();
                    if (reckImg == null) reckImg = reckoningPanel.AddComponent<Image>();
                    reckImg.color = new Color(0.06f, 0.09f, 0.13f, 0.98f);
                    reckImg.raycastTarget = true;
                }
            }

            if (reckoningPanel != null)
            {
                Transform rpTrans = reckoningPanel.transform;
                if (reckoningTitleText == null)
                {
                    Transform t = rpTrans.Find("ReckTitle");
                    if (t != null) reckoningTitleText = t.GetComponent<TMP_Text>();
                    else reckoningTitleText = CreateTMPText("ReckTitle", rpTrans, new Vector2(0f, -20f), new Vector2(540f, 36f), 22f, TextAlignmentOptions.Center, new Color(0.35f, 0.95f, 0.75f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }
                if (reckoningSubtitleText == null)
                {
                    Transform t = rpTrans.Find("ReckSubtitle");
                    if (t != null) reckoningSubtitleText = t.GetComponent<TMP_Text>();
                    else reckoningSubtitleText = CreateTMPText("ReckSubtitle", rpTrans, new Vector2(0f, -58f), new Vector2(540f, 22f), 12f, TextAlignmentOptions.Center, new Color(0.6f, 0.7f, 0.8f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }
                if (reckoningNarrativeText == null)
                {
                    Transform t = rpTrans.Find("ReckNarrative");
                    if (t != null) reckoningNarrativeText = t.GetComponent<TMP_Text>();
                    else reckoningNarrativeText = CreateTMPText("ReckNarrative", rpTrans, new Vector2(0f, -85f), new Vector2(520f, 85f), 14f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }

                Transform statsBox = rpTrans.Find("StatsBox");
                if (reckoningPositiveChoicesText == null)
                {
                    Transform t = (statsBox != null ? statsBox.Find("PosChoices") : null) ?? rpTrans.Find("PosChoices");
                    if (t != null) reckoningPositiveChoicesText = t.GetComponent<TMP_Text>();
                }
                if (reckoningNegativeChoicesText == null)
                {
                    Transform t = (statsBox != null ? statsBox.Find("NegChoices") : null) ?? rpTrans.Find("NegChoices");
                    if (t != null) reckoningNegativeChoicesText = t.GetComponent<TMP_Text>();
                }
                if (reckoningDepressionText == null)
                {
                    Transform t = (statsBox != null ? statsBox.Find("DepressionStat") : null) ?? rpTrans.Find("DepressionStat");
                    if (t != null) reckoningDepressionText = t.GetComponent<TMP_Text>();
                }
                if (reckoningSubstancesText == null)
                {
                    Transform t = (statsBox != null ? statsBox.Find("SubstancesStat") : null) ?? rpTrans.Find("SubstancesStat");
                    if (t != null) reckoningSubstancesText = t.GetComponent<TMP_Text>();
                }
                if (reckoningScoreText == null)
                {
                    Transform t = (statsBox != null ? statsBox.Find("ScoreStat") : null) ?? rpTrans.Find("ScoreStat");
                    if (t != null) reckoningScoreText = t.GetComponent<TMP_Text>();
                }
                if (statsBox != null)
                {
                    if (reckoningDaysText == null)
                    {
                        Transform t = statsBox.Find("DaysStat");
                        if (t != null) reckoningDaysText = t.GetComponent<TMP_Text>();
                        else reckoningDaysText = CreateTMPText("DaysStat", statsBox, new Vector2(0f, -80f), new Vector2(500f, 22f), 12f, TextAlignmentOptions.Left, Color.white);
                    }
                    if (reckoningCleanlinessText == null)
                    {
                        Transform t = statsBox.Find("CleanlinessStat");
                        if (t != null) reckoningCleanlinessText = t.GetComponent<TMP_Text>();
                        else reckoningCleanlinessText = CreateTMPText("CleanlinessStat", statsBox, new Vector2(0f, -104f), new Vector2(500f, 22f), 12f, TextAlignmentOptions.Left, Color.white);
                    }
                    if (reckoningMissedCallsText == null)
                    {
                        Transform t = statsBox.Find("MissedCallsStat");
                        if (t != null) reckoningMissedCallsText = t.GetComponent<TMP_Text>();
                        else reckoningMissedCallsText = CreateTMPText("MissedCallsStat", statsBox, new Vector2(0f, -128f), new Vector2(500f, 22f), 12f, TextAlignmentOptions.Left, Color.white);
                    }
                }
                if (reckoningVerdictPromptText == null)
                {
                    Transform t = rpTrans.Find("VerdictPrompt");
                    if (t != null) reckoningVerdictPromptText = t.GetComponent<TMP_Text>();
                }
                if (reckoningContinueButton == null)
                {
                    Transform t = rpTrans.Find("HangupButton");
                    if (t != null) reckoningContinueButton = t.GetComponent<Button>();
                }
                if (reckoningContinueButton != null)
                {
                    reckoningContinueButton.onClick.RemoveListener(OnReckoningContinueClicked);
                    reckoningContinueButton.onClick.AddListener(OnReckoningContinueClicked);
                }

                EnsureReckoningScaleUI();
            }

            // Ensure font and z=0 on all TMP texts in hierarchy
            UpdateAllTMPMeshGeometry();
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
                    GameObject scObj = new GameObject("ScaleContainer", typeof(RectTransform));
                    scObj.transform.SetParent(rpTrans, false);
                    scaleContainer = scObj.GetComponent<RectTransform>();
                    scaleContainer.anchorMin = new Vector2(0.5f, 1f);
                    scaleContainer.anchorMax = new Vector2(0.5f, 1f);
                    scaleContainer.pivot = new Vector2(0.5f, 1f);
                    scaleContainer.anchoredPosition = new Vector2(0f, -135f);
                    scaleContainer.sizeDelta = new Vector2(560f, 185f);
                    scaleContainer.localScale = Vector3.one;

                    // Stand Pillar
                    GameObject standObj = new GameObject("FulcrumStand", typeof(RectTransform), typeof(Image));
                    standObj.transform.SetParent(scaleContainer, false);
                    RectTransform sRT = standObj.GetComponent<RectTransform>();
                    sRT.anchoredPosition = new Vector2(0f, -25f);
                    sRT.sizeDelta = new Vector2(16f, 95f);
                    standObj.GetComponent<Image>().color = new Color(0.20f, 0.25f, 0.30f, 0.95f);

                    // Stand Base
                    GameObject baseObj = new GameObject("StandBase", typeof(RectTransform), typeof(Image));
                    baseObj.transform.SetParent(scaleContainer, false);
                    RectTransform bRT = baseObj.GetComponent<RectTransform>();
                    bRT.anchoredPosition = new Vector2(0f, -70f);
                    bRT.sizeDelta = new Vector2(75f, 12f);
                    baseObj.GetComponent<Image>().color = new Color(0.28f, 0.33f, 0.38f, 0.95f);

                    // Stand Pivot Cap
                    GameObject capObj = new GameObject("PivotCap", typeof(RectTransform), typeof(Image));
                    capObj.transform.SetParent(scaleContainer, false);
                    RectTransform cRT = capObj.GetComponent<RectTransform>();
                    cRT.anchoredPosition = new Vector2(0f, 24f);
                    cRT.sizeDelta = new Vector2(24f, 24f);
                    capObj.GetComponent<Image>().color = new Color(0.42f, 0.48f, 0.54f, 1f);

                    // Scale Beam
                    GameObject beamObj = new GameObject("ScaleBeam", typeof(RectTransform), typeof(Image));
                    beamObj.transform.SetParent(scaleContainer, false);
                    scaleBeam = beamObj.GetComponent<RectTransform>();
                    scaleBeam.anchoredPosition = new Vector2(0f, 24f);
                    scaleBeam.pivot = new Vector2(0.5f, 0.5f);
                    scaleBeam.sizeDelta = new Vector2(380f, 10f);
                    beamObj.GetComponent<Image>().color = new Color(0.35f, 0.40f, 0.45f, 1f);

                    // Left Pan (Hope)
                    GameObject lpObj = new GameObject("LeftPan", typeof(RectTransform), typeof(Image));
                    lpObj.transform.SetParent(scaleContainer, false);
                    leftPan = lpObj.GetComponent<RectTransform>();
                    leftPan.anchoredPosition = new Vector2(-180f, -36f);
                    leftPan.sizeDelta = new Vector2(130f, 24f);
                    lpObj.GetComponent<Image>().color = new Color(0.08f, 0.24f, 0.14f, 0.96f);

                    // Left Pan Container for DVDs
                    GameObject lDvdCont = new GameObject("LeftDvdContainer", typeof(RectTransform));
                    lDvdCont.transform.SetParent(leftPan, false);
                    leftDvdContainer = lDvdCont.GetComponent<RectTransform>();
                    leftDvdContainer.anchoredPosition = Vector2.zero;
                    leftDvdContainer.sizeDelta = new Vector2(130f, 100f);

                    // Left Weight Label
                    leftWeightText = CreateTMPText("LeftWeightLabel", leftPan, new Vector2(0f, -22f), new Vector2(140f, 20f), 11f, TextAlignmentOptions.Center, new Color(0.35f, 1f, 0.55f));
                    leftWeightText.text = "<color=#55FF77><b>HOPE: 0</b></color>";

                    // Right Pan (Despair)
                    GameObject rpObj = new GameObject("RightPan", typeof(RectTransform), typeof(Image));
                    rpObj.transform.SetParent(scaleContainer, false);
                    rightPan = rpObj.GetComponent<RectTransform>();
                    rightPan.anchoredPosition = new Vector2(180f, -36f);
                    rightPan.sizeDelta = new Vector2(130f, 24f);
                    rpObj.GetComponent<Image>().color = new Color(0.26f, 0.08f, 0.10f, 0.96f);

                    // Right Pan Container for DVDs
                    GameObject rDvdCont = new GameObject("RightDvdContainer", typeof(RectTransform));
                    rDvdCont.transform.SetParent(rightPan, false);
                    rightDvdContainer = rDvdCont.GetComponent<RectTransform>();
                    rightDvdContainer.anchoredPosition = Vector2.zero;
                    rightDvdContainer.sizeDelta = new Vector2(130f, 100f);

                    // Right Weight Label
                    rightWeightText = CreateTMPText("RightWeightLabel", rightPan, new Vector2(0f, -22f), new Vector2(140f, 20f), 11f, TextAlignmentOptions.Center, new Color(1f, 0.35f, 0.45f));
                    rightWeightText.text = "<color=#FF5555><b>DESPAIR: 0</b></color>";

                    // Chain lines
                    leftChainA = CreateChainLine("LeftChainA", scaleContainer);
                    leftChainB = CreateChainLine("LeftChainB", scaleContainer);
                    rightChainA = CreateChainLine("RightChainA", scaleContainer);
                    rightChainB = CreateChainLine("RightChainB", scaleContainer);
                }
            }
            else
            {
                if (scaleBeam == null) { Transform t = scaleContainer.Find("ScaleBeam"); if (t != null) scaleBeam = t.GetComponent<RectTransform>(); }
                if (leftPan == null) { Transform t = scaleContainer.Find("LeftPan"); if (t != null) leftPan = t.GetComponent<RectTransform>(); }
                if (rightPan == null) { Transform t = scaleContainer.Find("RightPan"); if (t != null) rightPan = t.GetComponent<RectTransform>(); }
                if (leftPan != null && leftDvdContainer == null) { Transform t = leftPan.Find("LeftDvdContainer"); if (t != null) leftDvdContainer = t.GetComponent<RectTransform>(); }
                if (rightPan != null && rightDvdContainer == null) { Transform t = rightPan.Find("RightDvdContainer"); if (t != null) rightDvdContainer = t.GetComponent<RectTransform>(); }
                if (leftPan != null && leftWeightText == null) { Transform t = leftPan.Find("LeftWeightLabel"); if (t != null) leftWeightText = t.GetComponent<TMP_Text>(); }
                if (rightPan != null && rightWeightText == null) { Transform t = rightPan.Find("RightWeightLabel"); if (t != null) rightWeightText = t.GetComponent<TMP_Text>(); }
                if (leftChainA == null) { Transform t = scaleContainer.Find("LeftChainA"); if (t != null) leftChainA = t.GetComponent<RectTransform>(); }
                if (leftChainB == null) { Transform t = scaleContainer.Find("LeftChainB"); if (t != null) leftChainB = t.GetComponent<RectTransform>(); }
                if (rightChainA == null) { Transform t = scaleContainer.Find("RightChainA"); if (t != null) rightChainA = t.GetComponent<RectTransform>(); }
                if (rightChainB == null) { Transform t = scaleContainer.Find("RightChainB"); if (t != null) rightChainB = t.GetComponent<RectTransform>(); }
            }
        }

        private RectTransform CreateChainLine(string name, Transform parent)
        {
            GameObject chainObj = new GameObject(name, typeof(RectTransform), typeof(Image));
            chainObj.transform.SetParent(parent, false);
            RectTransform rt = chainObj.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0f, 0.5f);
            chainObj.GetComponent<Image>().color = new Color(0.55f, 0.60f, 0.65f, 0.75f);
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
                    if (cachedFontAsset == null)
                    {
                        cachedFontAsset = TMP_Settings.defaultFontAsset;
                    }
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

                if (cachedFontAsset != null)
                {
                    tmp.font = cachedFontAsset;
                }
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
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            if (existing == null) go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
            Vector2 p = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchorMin = a;
            rt.anchorMax = a;
            rt.pivot = p;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            EnsureFontOnTMP(tmp);
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.maskable = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        private GameObject CreateButton(string name, Transform parent, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick, Vector2? anchor = null, Vector2? pivot = null)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            if (existing == null) go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
            Vector2 p = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchorMin = a;
            rt.anchorMax = a;
            rt.pivot = p;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            Button btn = go.GetComponent<Button>();
            if (onClick != null)
            {
                btn.onClick.RemoveListener(onClick);
                btn.onClick.AddListener(onClick);
            }
            return go;
        }
    }
}
