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

            StoryCardController[] all = Resources.FindObjectsOfTypeAll<StoryCardController>();
            foreach (var sc in all)
            {
                if (sc != null && sc.gameObject.scene.name == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                {
                    Instance = sc;
                    return sc;
                }
            }

            if (StoryManager.Instance != null && StoryManager.Instance.storyCardCanvas != null)
            {
                Instance = StoryManager.Instance.storyCardCanvas;
                return Instance;
            }

            // Check if saved template exists in Resources
            GameObject template = Resources.Load<GameObject>("StoryCardCanvasTemplate");
            if (template != null)
            {
                GameObject instantiated = Instantiate(template);
                instantiated.name = "StoryCardCanvas";
                StoryCardController ctrl = instantiated.GetComponent<StoryCardController>();
                Instance = ctrl;
                return ctrl;
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
        public TMP_Text reckoningDepressionText;
        public TMP_Text reckoningSubstancesText;
        public TMP_Text reckoningScoreText;
        public TMP_Text reckoningVerdictPromptText;
        public Button reckoningContinueButton;

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
            bool isDay3Finale = isEndgame && RunSaveManager.HasInstance && RunSaveManager.Instance.postMaxDaysSurvived >= 3 && !RunSaveManager.Instance.finalPhoneEncounterDone;

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

            transform.localScale = Vector3.one;
            RectTransform rootRT = GetComponent<RectTransform>();
            if (rootRT != null) rootRT.localScale = Vector3.one;

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
        // DAY 3 ENDGAME RECKONING / STATS SCREEN
        // =========================================================================

        public void ShowReckoningStatsScreen(Action onComplete)
        {
            onCardFinished = onComplete;
            hasChosen = false;
            isDragging = false;
            isDraggingConsequence = false;
            currentDragOffset = Vector2.zero;

            transform.localScale = Vector3.one;
            RectTransform rootRT = GetComponent<RectTransform>();
            if (rootRT != null) rootRT.localScale = Vector3.one;

            gameObject.SetActive(true);
            EnsureUIHierarchy();
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
                }
                reckoningPanel.SetActive(true);
            }

            // 1. Gather stats
            int pos = RunSaveManager.HasInstance ? RunSaveManager.Instance.positiveSecretCounter : 0;
            int neg = RunSaveManager.HasInstance ? RunSaveManager.Instance.negativeSecretCounter : 0;
            float depression = BarManager.Instance != null ? BarManager.Instance.currentDepression : 0f;
            int substances = RunSaveManager.HasInstance ? RunSaveManager.Instance.substancesConsumed : 0;

            // 2. Weigh values
            float storyScore = pos - neg;
            float selfCareScore = 0f;

            if (depression <= 25f) selfCareScore += 2.0f;
            else if (depression <= 45f) selfCareScore += 1.0f;
            else if (depression >= 80f) selfCareScore -= 2.0f;
            else if (depression >= 60f) selfCareScore -= 1.0f;

            if (substances <= 2) selfCareScore += 1.5f;
            else if (substances <= 6) selfCareScore += 0.5f;
            else if (substances >= 18) selfCareScore -= 2.0f;
            else if (substances >= 10) selfCareScore -= 1.0f;

            float totalScore = storyScore + selfCareScore;
            bool isHopeful = totalScore >= 0f;
            lastVerdictHopeful = isHopeful;

            // 3. Populate texts
            if (reckoningTitleText != null) reckoningTitleText.text = "THE FINAL RECKONING";
            if (reckoningSubtitleText != null) reckoningSubtitleText.text = "DAY 3 ENDGAME TELEPHONE EVALUATION";

            if (reckoningNarrativeText != null)
            {
                reckoningNarrativeText.text = isHopeful
                    ? "\"The static clears into a calm voice: 'You stood firm against the darkness. Through every hour of noise and exhaustion, you kept standing. The screen doesn't own your life anymore. Reach out.'\""
                    : "\"The receiver buzzes with cold, merciless feedback: 'You let the haze consume you. Running to chemicals, letting the room drown in filth and regret. The flicker has swallowed everything you were. It is time to end it.'\"";
            }

            if (reckoningPositiveChoicesText != null)
            {
                reckoningPositiveChoicesText.text = $"Positive Decisions: <b>{pos}</b>";
            }
            if (reckoningNegativeChoicesText != null)
            {
                reckoningNegativeChoicesText.text = $"Escapist Decisions: <b>{neg}</b>";
            }

            if (reckoningDepressionText != null)
            {
                string depRating = depression <= 35f ? "Managed" : (depression <= 65f ? "Moderate" : "Critical");
                string depColor = depression <= 35f ? "#55FF77" : (depression <= 65f ? "#FFAA44" : "#FF5555");
                reckoningDepressionText.text = $"Depression: <color={depColor}><b>{depression:0}% ({depRating})</b></color>";
            }

            if (reckoningSubstancesText != null)
            {
                string subRating = substances <= 4 ? "Restrained" : (substances <= 9 ? "Moderate" : "Heavy");
                string subColor = substances <= 4 ? "#55FF77" : (substances <= 9 ? "#FFAA44" : "#FF5555");
                reckoningSubstancesText.text = $"Substances: <color={subColor}><b>{substances} ({subRating})</b></color>";
            }

            if (reckoningScoreText != null)
            {
                string scoreColor = isHopeful ? "#55FF77" : "#FF5555";
                string tiltName = isHopeful ? "TILTED TOWARD HOPE" : "TILTED TOWARD DESPAIR";
                reckoningScoreText.text = $"Weighing Balance: <color={scoreColor}><b>{(totalScore >= 0 ? "+" : "")}{totalScore:0.0} ({tiltName})</b></color>";
            }

            if (reckoningVerdictPromptText != null)
            {
                reckoningVerdictPromptText.text = isHopeful
                    ? "<color=#55FF77><b>VERDICT: REACH OUT</b>\nYou kept your head above the water. A life awaits beyond these walls.\n<b>GO TO THE FRONT DOOR TO STEP OUTSIDE.</b></color>"
                    : "<color=#FF5555><b>VERDICT: SURRENDER</b>\nThe isolation was too heavy. The room calls you back into the dark.\n<b>GO INTO THE ROOM TO THE TELEVISION TO END IT ALL.</b></color>";
            }

            UpdateAllTMPMeshGeometry();
            PlayProceduralBeep(isHopeful ? 660f : 240f, 0.15f);
        }

        public void OnReckoningContinueClicked()
        {
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

            // CRITICAL: Always enforce scale = 1 on root canvas
            transform.localScale = Vector3.one;
            RectTransform rootRT = GetComponent<RectTransform>();
            if (rootRT != null)
            {
                rootRT.localScale = Vector3.one;
            }

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

                // CRITICAL: Camera near clip is 0.05 and far clip is 25.
                // A planeDistance of 100 is completely outside camera far clip and culled!
                // Enforce planeDistance safely right in front of nearClipPlane (0.22m to 0.35m).
                float safeDistance = Mathf.Clamp(cam.nearClipPlane + 0.17f, 0.20f, 0.35f);
                if (canvas.planeDistance < cam.nearClipPlane + 0.05f || canvas.planeDistance > 2f)
                {
                    canvas.planeDistance = safeDistance;
                }
                else
                {
                    canvas.planeDistance = safeDistance;
                }

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

        public void EnsureUIHierarchy()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            SetupScreenSpaceCamera(canvas);

            transform.localScale = Vector3.one;
            RectTransform rootRT = GetComponent<RectTransform>();
            if (rootRT != null) rootRT.localScale = Vector3.one;

            // Dark CRT vignette backdrop (Full Screen Stretch)
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
            Image bgImg = bgTrans.GetComponent<Image>();
            if (bgImg == null) bgImg = bgTrans.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.04f, 0.06f, 0.08f, 0.88f);
            bgImg.raycastTarget = false;
            bgTrans.SetAsFirstSibling();

            // 1. Left and Right Swipe Visual Indicators on Canvas Edges (Guiding Player to Swipe)
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
            leftSwipeIndicator.sizeDelta = new Vector2(180f, 130f);
            leftSwipeIndicator.anchoredPosition = new Vector2(40f, 0f);
            leftSwipeIndicator.localScale = Vector3.one;
            leftSwipeIndicator.localRotation = Quaternion.identity;

            Image leftImg = leftSwipeIndicator.GetComponent<Image>();
            if (leftImg == null) leftImg = leftSwipeIndicator.gameObject.AddComponent<Image>();
            leftImg.color = new Color(0.12f, 0.04f, 0.05f, 0.85f);
            leftImg.raycastTarget = false;

            leftSwipeGroup = leftSwipeIndicator.GetComponent<CanvasGroup>();
            if (leftSwipeGroup == null) leftSwipeGroup = leftSwipeIndicator.gameObject.AddComponent<CanvasGroup>();
            leftSwipeGroup.alpha = 0.35f;
            leftSwipeGroup.blocksRaycasts = false;
            leftSwipeGroup.interactable = false;

            if (leftSwipeText == null) leftSwipeText = leftSwipeIndicator.GetComponentInChildren<TMP_Text>(true);
            if (leftSwipeText == null)
            {
                leftSwipeText = CreateTMPText("Label", leftSwipeIndicator, Vector2.zero, new Vector2(170f, 120f), 18, TextAlignmentOptions.Center, new Color(1f, 0.45f, 0.45f));
            }
            else
            {
                RectTransform lblRT = leftSwipeText.rectTransform;
                lblRT.anchorMin = new Vector2(0.5f, 0.5f);
                lblRT.anchorMax = new Vector2(0.5f, 0.5f);
                lblRT.pivot = new Vector2(0.5f, 0.5f);
                lblRT.sizeDelta = new Vector2(170f, 120f);
                lblRT.anchoredPosition = Vector2.zero;
                lblRT.localScale = Vector3.one;
            }
            leftSwipeText.text = "<b>◀ REJECT</b>\n<size=12><color=#ffaaaa>[ SWIPE LEFT ]</color></size>";

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
            rightSwipeIndicator.sizeDelta = new Vector2(180f, 130f);
            rightSwipeIndicator.anchoredPosition = new Vector2(-40f, 0f);
            rightSwipeIndicator.localScale = Vector3.one;
            rightSwipeIndicator.localRotation = Quaternion.identity;

            Image rightImg = rightSwipeIndicator.GetComponent<Image>();
            if (rightImg == null) rightImg = rightSwipeIndicator.gameObject.AddComponent<Image>();
            rightImg.color = new Color(0.04f, 0.12f, 0.06f, 0.85f);
            rightImg.raycastTarget = false;

            rightSwipeGroup = rightSwipeIndicator.GetComponent<CanvasGroup>();
            if (rightSwipeGroup == null) rightSwipeGroup = rightSwipeIndicator.gameObject.AddComponent<CanvasGroup>();
            rightSwipeGroup.alpha = 0.35f;
            rightSwipeGroup.blocksRaycasts = false;
            rightSwipeGroup.interactable = false;

            if (rightSwipeText == null) rightSwipeText = rightSwipeIndicator.GetComponentInChildren<TMP_Text>(true);
            if (rightSwipeText == null)
            {
                rightSwipeText = CreateTMPText("Label", rightSwipeIndicator, Vector2.zero, new Vector2(170f, 120f), 18, TextAlignmentOptions.Center, new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                RectTransform lblRT = rightSwipeText.rectTransform;
                lblRT.anchorMin = new Vector2(0.5f, 0.5f);
                lblRT.anchorMax = new Vector2(0.5f, 0.5f);
                lblRT.pivot = new Vector2(0.5f, 0.5f);
                lblRT.sizeDelta = new Vector2(170f, 120f);
                lblRT.anchoredPosition = Vector2.zero;
                lblRT.localScale = Vector3.one;
            }
            rightSwipeText.text = "<b>ACCEPT ▶</b>\n<size=12><color=#aaffaa>[ SWIPE RIGHT ]</color></size>";

            // 2. Card Container (Size 440 x 620, Centered)
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
            cardContainer.sizeDelta = new Vector2(440f, 620f);
            cardContainer.anchoredPosition = Vector2.zero;
            cardContainer.localScale = Vector3.one;

            Image cardBg = cardContainer.GetComponent<Image>();
            if (cardBg == null) cardBg = cardContainer.gameObject.AddComponent<Image>();
            cardBg.color = new Color(0.08f, 0.12f, 0.16f, 0.98f);
            cardBg.raycastTarget = true;

            if (cardCanvasGroup == null) cardCanvasGroup = cardContainer.GetComponent<CanvasGroup>();
            if (cardCanvasGroup == null) cardCanvasGroup = cardContainer.gameObject.AddComponent<CanvasGroup>();
            cardCanvasGroup.blocksRaycasts = true;
            cardCanvasGroup.interactable = true;

            // HeaderPanel (Anchored to top of card container, 80px tall)
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
            headerRT.sizeDelta = new Vector2(0f, 80f);
            headerRT.anchoredPosition = Vector2.zero;
            headerRT.localScale = Vector3.one;

            Image hImg = headerTrans.GetComponent<Image>();
            if (hImg == null) hImg = headerTrans.gameObject.AddComponent<Image>();
            hImg.color = new Color(0.12f, 0.18f, 0.24f, 1f);
            hImg.raycastTarget = false;

            if (contactTitleText == null)
            {
                Transform t = headerTrans.Find("ContactTitle");
                if (t != null) contactTitleText = t.GetComponent<TMP_Text>();
                else contactTitleText = CreateTMPText("ContactTitle", headerTrans, new Vector2(0f, -10f), new Vector2(400f, 32f), 20f, TextAlignmentOptions.Center, new Color(0.4f, 0.95f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (contactTitleText != null)
            {
                RectTransform rt = contactTitleText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(400f, 32f);
                rt.anchoredPosition = new Vector2(0f, -10f);
                rt.localScale = Vector3.one;
                contactTitleText.fontStyle = FontStyles.Bold;
                contactTitleText.fontSize = 20f;
                contactTitleText.alignment = TextAlignmentOptions.Center;
                if (string.IsNullOrEmpty(contactTitleText.text)) contactTitleText.text = "CONTACT NAME";
            }

            if (contactRoleText == null)
            {
                Transform t = headerTrans.Find("ContactRole");
                if (t != null) contactRoleText = t.GetComponent<TMP_Text>();
                else contactRoleText = CreateTMPText("ContactRole", headerTrans, new Vector2(0f, -46f), new Vector2(400f, 22f), 12f, TextAlignmentOptions.Center, new Color(0.7f, 0.8f, 0.9f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (contactRoleText != null)
            {
                RectTransform rt = contactRoleText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(400f, 22f);
                rt.anchoredPosition = new Vector2(0f, -46f);
                rt.localScale = Vector3.one;
                contactRoleText.fontSize = 12f;
                contactRoleText.alignment = TextAlignmentOptions.Center;
                if (string.IsNullOrEmpty(contactRoleText.text)) contactRoleText.text = "INCOMING CALL";
            }

            // Portrait Frame & Image (Upper center of card)
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
            portRT.sizeDelta = new Vector2(170f, 170f);
            portRT.anchoredPosition = new Vector2(0f, 85f);
            portRT.localScale = Vector3.one;

            Image pfImg = portFrameTrans.GetComponent<Image>();
            if (pfImg == null) pfImg = portFrameTrans.gameObject.AddComponent<Image>();
            pfImg.color = new Color(0.04f, 0.06f, 0.08f, 1f);
            pfImg.raycastTarget = false;

            if (portraitImage == null)
            {
                Transform t = portFrameTrans.Find("PortraitImage");
                if (t != null) portraitImage = t.GetComponent<Image>();
                else
                {
                    GameObject portImgObj = new GameObject("PortraitImage", typeof(RectTransform), typeof(Image));
                    portImgObj.transform.SetParent(portFrameTrans, false);
                    portraitImage = portImgObj.GetComponent<Image>();
                }
            }
            if (portraitImage != null)
            {
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

            // Narrative Text Body (Lower center of card, zero overlap with portrait)
            if (narrativeBodyText == null)
            {
                Transform t = cardContainer.Find("NarrativeText");
                if (t != null) narrativeBodyText = t.GetComponent<TMP_Text>();
                else narrativeBodyText = CreateTMPText("NarrativeText", cardContainer, new Vector2(0f, -120f), new Vector2(380f, 190f), 15f, TextAlignmentOptions.Center, Color.white);
            }
            if (narrativeBodyText != null)
            {
                RectTransform rt = narrativeBodyText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(380f, 190f);
                rt.anchoredPosition = new Vector2(0f, -120f);
                rt.localScale = Vector3.one;
                narrativeBodyText.fontSize = 15f;
                narrativeBodyText.alignment = TextAlignmentOptions.Center;
                narrativeBodyText.raycastTarget = false;
                narrativeBodyText.textWrappingMode = TextWrappingModes.Normal;
                if (string.IsNullOrEmpty(narrativeBodyText.text))
                {
                    narrativeBodyText.text = "\"Story proposal text... Drag card left to reject or right to accept.\"";
                }
            }

            // Stamp Badges
            if (rejectStamp == null)
            {
                Transform t = cardContainer.Find("RejectStamp");
                if (t != null) rejectStamp = t.GetComponent<RectTransform>();
                else
                {
                    GameObject rejObj = new GameObject("RejectStamp", typeof(RectTransform), typeof(CanvasGroup));
                    rejObj.transform.SetParent(cardContainer, false);
                    rejectStamp = rejObj.GetComponent<RectTransform>();
                    CreateTMPText("RejectLabel", rejObj.transform, Vector2.zero, new Vector2(150f, 45f), 24f, TextAlignmentOptions.Center, new Color(0.95f, 0.3f, 0.3f)).text = "[ REJECT ]";
                }
            }
            rejectStamp.anchorMin = new Vector2(0.5f, 0.5f);
            rejectStamp.anchorMax = new Vector2(0.5f, 0.5f);
            rejectStamp.pivot = new Vector2(0.5f, 0.5f);
            rejectStamp.sizeDelta = new Vector2(150f, 45f);
            rejectStamp.anchoredPosition = new Vector2(-110f, 170f);
            rejectStamp.localRotation = Quaternion.Euler(0f, 0f, 15f);
            rejectStamp.localScale = Vector3.one;
            rejectStampGroup = rejectStamp.GetComponent<CanvasGroup>();
            if (rejectStampGroup == null) rejectStampGroup = rejectStamp.gameObject.AddComponent<CanvasGroup>();

            if (acceptStamp == null)
            {
                Transform t = cardContainer.Find("AcceptStamp");
                if (t != null) acceptStamp = t.GetComponent<RectTransform>();
                else
                {
                    GameObject accObj = new GameObject("AcceptStamp", typeof(RectTransform), typeof(CanvasGroup));
                    accObj.transform.SetParent(cardContainer, false);
                    acceptStamp = accObj.GetComponent<RectTransform>();
                    CreateTMPText("AcceptLabel", accObj.transform, Vector2.zero, new Vector2(150f, 45f), 24f, TextAlignmentOptions.Center, new Color(0.3f, 0.95f, 0.4f)).text = "[ ACCEPT ]";
                }
            }
            acceptStamp.anchorMin = new Vector2(0.5f, 0.5f);
            acceptStamp.anchorMax = new Vector2(0.5f, 0.5f);
            acceptStamp.pivot = new Vector2(0.5f, 0.5f);
            acceptStamp.sizeDelta = new Vector2(150f, 45f);
            acceptStamp.anchoredPosition = new Vector2(110f, 170f);
            acceptStamp.localRotation = Quaternion.Euler(0f, 0f, -15f);
            acceptStamp.localScale = Vector3.one;
            acceptStampGroup = acceptStamp.GetComponent<CanvasGroup>();
            if (acceptStampGroup == null) acceptStampGroup = acceptStamp.gameObject.AddComponent<CanvasGroup>();

            // Choice Buttons (Hidden because user requested call card to be swipe-only)
            Transform leftBtnTrans = cardContainer.Find("LeftChoiceButton");
            if (leftBtnTrans != null)
            {
                leftChoiceButton = leftBtnTrans.GetComponent<Button>();
                leftChoiceButton.gameObject.SetActive(false);
            }
            Transform rightBtnTrans = cardContainer.Find("RightChoiceButton");
            if (rightBtnTrans != null)
            {
                rightChoiceButton = rightBtnTrans.GetComponent<Button>();
                rightChoiceButton.gameObject.SetActive(false);
            }

            // 3. Consequence Panel (Size 540 x 460)
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
            consRT.sizeDelta = new Vector2(540f, 460f);
            consRT.anchoredPosition = Vector2.zero;
            consRT.localScale = Vector3.one;
            consRT.localRotation = Quaternion.identity;

            Image consImg = consequencePanel.GetComponent<Image>();
            if (consImg == null) consImg = consequencePanel.AddComponent<Image>();
            consImg.color = new Color(0.06f, 0.10f, 0.14f, 0.98f);
            consImg.raycastTarget = true;

            Transform cpTrans = consequencePanel.transform;

            if (consequenceTitleText == null)
            {
                Transform t = cpTrans.Find("ConsTitle");
                if (t != null) consequenceTitleText = t.GetComponent<TMP_Text>();
                else consequenceTitleText = CreateTMPText("ConsTitle", cpTrans, new Vector2(0f, -25f), new Vector2(500f, 36f), 20f, TextAlignmentOptions.Center, Color.green, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (consequenceTitleText != null)
            {
                RectTransform rt = consequenceTitleText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(500f, 36f);
                rt.anchoredPosition = new Vector2(0f, -25f);
                rt.localScale = Vector3.one;
                consequenceTitleText.fontSize = 20f;
                consequenceTitleText.fontStyle = FontStyles.Bold;
                consequenceTitleText.alignment = TextAlignmentOptions.Center;
                if (string.IsNullOrEmpty(consequenceTitleText.text)) consequenceTitleText.text = "CALL OUTCOME";
            }

            if (consequenceBodyText == null)
            {
                Transform t = cpTrans.Find("ConsBody");
                if (t != null) consequenceBodyText = t.GetComponent<TMP_Text>();
                else consequenceBodyText = CreateTMPText("ConsBody", cpTrans, new Vector2(0f, -75f), new Vector2(480f, 110f), 15f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (consequenceBodyText != null)
            {
                RectTransform rt = consequenceBodyText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(480f, 110f);
                rt.anchoredPosition = new Vector2(0f, -75f);
                rt.localScale = Vector3.one;
                consequenceBodyText.fontSize = 15f;
                consequenceBodyText.alignment = TextAlignmentOptions.Center;
                consequenceBodyText.textWrappingMode = TextWrappingModes.Normal;
                if (string.IsNullOrEmpty(consequenceBodyText.text)) consequenceBodyText.text = "Outcome narrative text goes here...";
            }

            if (moneyDeltaText == null)
            {
                Transform t = cpTrans.Find("MoneyDelta");
                if (t != null) moneyDeltaText = t.GetComponent<TMP_Text>();
                else moneyDeltaText = CreateTMPText("MoneyDelta", cpTrans, new Vector2(0f, -200f), new Vector2(460f, 28f), 16f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (moneyDeltaText != null)
            {
                RectTransform rt = moneyDeltaText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(460f, 28f);
                rt.anchoredPosition = new Vector2(0f, -200f);
                rt.localScale = Vector3.one;
                moneyDeltaText.fontSize = 16f;
                moneyDeltaText.alignment = TextAlignmentOptions.Center;
                if (string.IsNullOrEmpty(moneyDeltaText.text)) moneyDeltaText.text = "Cash: $0";
            }

            if (depressionDeltaText == null)
            {
                Transform t = cpTrans.Find("DepDelta");
                if (t != null) depressionDeltaText = t.GetComponent<TMP_Text>();
                else depressionDeltaText = CreateTMPText("DepDelta", cpTrans, new Vector2(0f, -232f), new Vector2(460f, 28f), 16f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (depressionDeltaText != null)
            {
                RectTransform rt = depressionDeltaText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(460f, 28f);
                rt.anchoredPosition = new Vector2(0f, -232f);
                rt.localScale = Vector3.one;
                depressionDeltaText.fontSize = 16f;
                depressionDeltaText.alignment = TextAlignmentOptions.Center;
                if (string.IsNullOrEmpty(depressionDeltaText.text)) depressionDeltaText.text = "Depression: Unchanged";
            }

            if (tirednessDeltaText == null)
            {
                Transform t = cpTrans.Find("TiredDelta");
                if (t != null) tirednessDeltaText = t.GetComponent<TMP_Text>();
                else tirednessDeltaText = CreateTMPText("TiredDelta", cpTrans, new Vector2(0f, -264f), new Vector2(460f, 28f), 16f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (tirednessDeltaText != null)
            {
                RectTransform rt = tirednessDeltaText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(460f, 28f);
                rt.anchoredPosition = new Vector2(0f, -264f);
                rt.localScale = Vector3.one;
                tirednessDeltaText.fontSize = 16f;
                tirednessDeltaText.alignment = TextAlignmentOptions.Center;
                if (string.IsNullOrEmpty(tirednessDeltaText.text)) tirednessDeltaText.text = "Tiredness: Unchanged";
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
                RectTransform rt = continueButton.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(340f, 48f);
                rt.anchoredPosition = new Vector2(0f, 25f);
                rt.localScale = Vector3.one;

                continueButton.onClick.RemoveListener(OnContinueClicked);
                continueButton.onClick.AddListener(OnContinueClicked);

                TMP_Text btnLabel = continueButton.GetComponentInChildren<TMP_Text>(true);
                if (btnLabel != null)
                {
                    EnsureFontOnTMP(btnLabel);
                    btnLabel.text = "[ ◀ SWIPE OR CLICK TO RETURN ▶ ]";
                }
            }

            // 4. Reckoning Panel (Size 580 x 560)
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

            Transform rpTrans = reckoningPanel.transform;

            if (reckoningTitleText == null)
            {
                Transform t = rpTrans.Find("ReckTitle");
                if (t != null) reckoningTitleText = t.GetComponent<TMP_Text>();
                else reckoningTitleText = CreateTMPText("ReckTitle", rpTrans, new Vector2(0f, -20f), new Vector2(540f, 36f), 22f, TextAlignmentOptions.Center, new Color(0.35f, 0.95f, 0.75f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (reckoningTitleText != null)
            {
                RectTransform rt = reckoningTitleText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(540f, 36f);
                rt.anchoredPosition = new Vector2(0f, -20f);
                rt.localScale = Vector3.one;
                reckoningTitleText.fontStyle = FontStyles.Bold;
                reckoningTitleText.fontSize = 22f;
                if (string.IsNullOrEmpty(reckoningTitleText.text)) reckoningTitleText.text = "THE FINAL RECKONING";
            }

            if (reckoningSubtitleText == null)
            {
                Transform t = rpTrans.Find("ReckSubtitle");
                if (t != null) reckoningSubtitleText = t.GetComponent<TMP_Text>();
                else reckoningSubtitleText = CreateTMPText("ReckSubtitle", rpTrans, new Vector2(0f, -58f), new Vector2(540f, 22f), 12f, TextAlignmentOptions.Center, new Color(0.6f, 0.7f, 0.8f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (reckoningSubtitleText != null)
            {
                RectTransform rt = reckoningSubtitleText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(540f, 22f);
                rt.anchoredPosition = new Vector2(0f, -58f);
                rt.localScale = Vector3.one;
                reckoningSubtitleText.fontSize = 12f;
                if (string.IsNullOrEmpty(reckoningSubtitleText.text)) reckoningSubtitleText.text = "DAY 3 ENDGAME TELEPHONE EVALUATION";
            }

            if (reckoningNarrativeText == null)
            {
                Transform t = rpTrans.Find("ReckNarrative");
                if (t != null) reckoningNarrativeText = t.GetComponent<TMP_Text>();
                else reckoningNarrativeText = CreateTMPText("ReckNarrative", rpTrans, new Vector2(0f, -85f), new Vector2(520f, 85f), 14f, TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            }
            if (reckoningNarrativeText != null)
            {
                RectTransform rt = reckoningNarrativeText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(520f, 85f);
                rt.anchoredPosition = new Vector2(0f, -85f);
                rt.localScale = Vector3.one;
                reckoningNarrativeText.fontSize = 14f;
                reckoningNarrativeText.textWrappingMode = TextWrappingModes.Normal;
                if (string.IsNullOrEmpty(reckoningNarrativeText.text)) reckoningNarrativeText.text = "The receiver buzzes with static...";
            }

            // Stats Box
            Transform statsBox = rpTrans.Find("StatsBox");
            if (statsBox == null)
            {
                GameObject sbObj = new GameObject("StatsBox", typeof(RectTransform), typeof(Image));
                sbObj.transform.SetParent(rpTrans, false);
                statsBox = sbObj.transform;
            }
            RectTransform boxRT = statsBox.GetComponent<RectTransform>();
            boxRT.anchorMin = new Vector2(0.5f, 0.5f);
            boxRT.anchorMax = new Vector2(0.5f, 0.5f);
            boxRT.pivot = new Vector2(0.5f, 0.5f);
            boxRT.sizeDelta = new Vector2(520f, 130f);
            boxRT.anchoredPosition = new Vector2(0f, 5f);
            boxRT.localScale = Vector3.one;

            Image sbImg = statsBox.GetComponent<Image>();
            if (sbImg == null) sbImg = statsBox.gameObject.AddComponent<Image>();
            sbImg.color = new Color(0.03f, 0.05f, 0.08f, 0.9f);
            sbImg.raycastTarget = false;

            if (reckoningPositiveChoicesText == null)
            {
                Transform t = statsBox.Find("PosChoices");
                if (t != null) reckoningPositiveChoicesText = t.GetComponent<TMP_Text>();
                else reckoningPositiveChoicesText = CreateTMPText("PosChoices", statsBox, new Vector2(20f, -15f), new Vector2(230f, 25f), 14f, TextAlignmentOptions.Left, new Color(0.35f, 0.95f, 0.45f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            }
            if (reckoningPositiveChoicesText != null)
            {
                RectTransform rt = reckoningPositiveChoicesText.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(230f, 25f);
                rt.anchoredPosition = new Vector2(20f, -15f);
                rt.localScale = Vector3.one;
                reckoningPositiveChoicesText.fontSize = 14f;
                reckoningPositiveChoicesText.alignment = TextAlignmentOptions.Left;
                if (string.IsNullOrEmpty(reckoningPositiveChoicesText.text)) reckoningPositiveChoicesText.text = "Positive Decisions: 0";
            }

            if (reckoningNegativeChoicesText == null)
            {
                Transform t = statsBox.Find("NegChoices");
                if (t != null) reckoningNegativeChoicesText = t.GetComponent<TMP_Text>();
                else reckoningNegativeChoicesText = CreateTMPText("NegChoices", statsBox, new Vector2(-20f, -15f), new Vector2(230f, 25f), 14f, TextAlignmentOptions.Right, new Color(0.95f, 0.45f, 0.35f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            }
            if (reckoningNegativeChoicesText != null)
            {
                RectTransform rt = reckoningNegativeChoicesText.rectTransform;
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(230f, 25f);
                rt.anchoredPosition = new Vector2(-20f, -15f);
                rt.localScale = Vector3.one;
                reckoningNegativeChoicesText.fontSize = 14f;
                reckoningNegativeChoicesText.alignment = TextAlignmentOptions.Right;
                if (string.IsNullOrEmpty(reckoningNegativeChoicesText.text)) reckoningNegativeChoicesText.text = "Escapist Decisions: 0";
            }

            if (reckoningDepressionText == null)
            {
                Transform t = statsBox.Find("DepressionStat");
                if (t != null) reckoningDepressionText = t.GetComponent<TMP_Text>();
                else reckoningDepressionText = CreateTMPText("DepressionStat", statsBox, new Vector2(20f, -48f), new Vector2(230f, 25f), 14f, TextAlignmentOptions.Left, Color.white, new Vector2(0f, 1f), new Vector2(0f, 1f));
            }
            if (reckoningDepressionText != null)
            {
                RectTransform rt = reckoningDepressionText.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(230f, 25f);
                rt.anchoredPosition = new Vector2(20f, -48f);
                rt.localScale = Vector3.one;
                reckoningDepressionText.fontSize = 14f;
                reckoningDepressionText.alignment = TextAlignmentOptions.Left;
                if (string.IsNullOrEmpty(reckoningDepressionText.text)) reckoningDepressionText.text = "Depression: 0%";
            }

            if (reckoningSubstancesText == null)
            {
                Transform t = statsBox.Find("SubstancesStat");
                if (t != null) reckoningSubstancesText = t.GetComponent<TMP_Text>();
                else reckoningSubstancesText = CreateTMPText("SubstancesStat", statsBox, new Vector2(-20f, -48f), new Vector2(230f, 25f), 14f, TextAlignmentOptions.Right, Color.white, new Vector2(1f, 1f), new Vector2(1f, 1f));
            }
            if (reckoningSubstancesText != null)
            {
                RectTransform rt = reckoningSubstancesText.rectTransform;
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(230f, 25f);
                rt.anchoredPosition = new Vector2(-20f, -48f);
                rt.localScale = Vector3.one;
                reckoningSubstancesText.fontSize = 14f;
                reckoningSubstancesText.alignment = TextAlignmentOptions.Right;
                if (string.IsNullOrEmpty(reckoningSubstancesText.text)) reckoningSubstancesText.text = "Substances: 0";
            }

            if (reckoningScoreText == null)
            {
                Transform t = statsBox.Find("ScoreStat");
                if (t != null) reckoningScoreText = t.GetComponent<TMP_Text>();
                else reckoningScoreText = CreateTMPText("ScoreStat", statsBox, new Vector2(0f, 12f), new Vector2(480f, 28f), 15f, TextAlignmentOptions.Center, new Color(0.9f, 0.85f, 0.4f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            }
            if (reckoningScoreText != null)
            {
                RectTransform rt = reckoningScoreText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(480f, 28f);
                rt.anchoredPosition = new Vector2(0f, 12f);
                rt.localScale = Vector3.one;
                reckoningScoreText.fontSize = 15f;
                reckoningScoreText.alignment = TextAlignmentOptions.Center;
                if (string.IsNullOrEmpty(reckoningScoreText.text)) reckoningScoreText.text = "Weighing Balance: 0.0";
            }

            if (reckoningVerdictPromptText == null)
            {
                Transform t = rpTrans.Find("VerdictPrompt");
                if (t != null) reckoningVerdictPromptText = t.GetComponent<TMP_Text>();
                else reckoningVerdictPromptText = CreateTMPText("VerdictPrompt", rpTrans, new Vector2(0f, 95f), new Vector2(540f, 80f), 15f, TextAlignmentOptions.Center, Color.yellow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            }
            if (reckoningVerdictPromptText != null)
            {
                RectTransform rt = reckoningVerdictPromptText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(540f, 80f);
                rt.anchoredPosition = new Vector2(0f, 95f);
                rt.localScale = Vector3.one;
                reckoningVerdictPromptText.fontSize = 15f;
                reckoningVerdictPromptText.alignment = TextAlignmentOptions.Center;
                reckoningVerdictPromptText.textWrappingMode = TextWrappingModes.Normal;
                if (string.IsNullOrEmpty(reckoningVerdictPromptText.text)) reckoningVerdictPromptText.text = "VERDICT: REACH OUT - GO TO FRONT DOOR";
            }

            if (reckoningContinueButton == null)
            {
                Transform t = rpTrans.Find("HangupButton");
                if (t != null) reckoningContinueButton = t.GetComponent<Button>();
                else
                {
                    GameObject contBtn = CreateButton("HangupButton", rpTrans, new Vector2(0f, 25f), new Vector2(300f, 50f), new Color(0.18f, 0.35f, 0.45f), OnReckoningContinueClicked, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
                    reckoningContinueButton = contBtn.GetComponent<Button>();
                    CreateTMPText("Label", contBtn.transform, Vector2.zero, new Vector2(280f, 40f), 15f, TextAlignmentOptions.Center, Color.white).text = "[ HANG UP / HEED VERDICT ]";
                }
            }
            if (reckoningContinueButton != null)
            {
                RectTransform rt = reckoningContinueButton.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(300f, 50f);
                rt.anchoredPosition = new Vector2(0f, 25f);
                rt.localScale = Vector3.one;

                reckoningContinueButton.onClick.RemoveListener(OnReckoningContinueClicked);
                reckoningContinueButton.onClick.AddListener(OnReckoningContinueClicked);
            }

            // Ensure font and z=0 on all TMP texts in hierarchy
            UpdateAllTMPMeshGeometry();
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
