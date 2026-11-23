using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class WorldSpaceUpgradeMenu : MonoBehaviour
{
    public static WorldSpaceUpgradeMenu ActiveMenu { get; private set; }
    public static bool IsFullscreenShopOpen => ActiveMenu != null && ActiveMenu.IsMenuOpen;

    public float openTransitionSpeed = 10f;
    public float closeTransitionSpeed = 25f;
    public float slideAnimationDuration = 0.3f;

    public Vector3 slideAxis = new Vector3(1, 0, 0);
    public float slideDistance = 100f;

    [Header("Player Specific Fix")]
    public bool isPlayerMenu = false;
    [Tooltip("Adds this position to the model as it scales to 0, masking the bottom pivot issue.")]
    public Vector3 playerSlideOffset = Vector3.zero;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip openSound;
    public AudioClip closeSound;
    public AudioClip clickSound;
    public AudioClip buySound;
    public AudioClip equipSound;

    [Header("UI Visuals (Legacy WorldSpace References)")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI statsText;
    public TextMeshProUGUI priceText;
    public TextMeshProUGUI buyButtonText;
    public Image buyButtonImage;
    public Image nextButtonImage;
    public Image prevButtonImage;

    [Header("Hitboxes (Legacy WorldSpace References)")]
    public InteractableObject buyInteractable;
    public InteractableObject nextInteractable;
    public InteractableObject prevInteractable;
    public InteractableObject closeInteractable;

    [Header("System 3: Top Tab Navigation ([DVD], [PLAYER], [REMOTE])")]
    public Button dvdTabButton;
    public Button playerTabButton;
    public Button remoteTabButton;
    public InteractableObject dvdTabInteractable;
    public InteractableObject playerTabInteractable;
    public InteractableObject remoteTabInteractable;
    public Color activeTabColor = new Color(1f, 0.88f, 0.32f, 1f);
    public Color inactiveTabColor = new Color(0.68f, 0.66f, 0.64f, 0.92f);

    public List<GameObject> objectsToDisable = new List<GameObject>();
    public bool IsMenuOpen { get; private set; } = false;

    public Transform previewAnchor;
    public float animationDuration = 0.8f;

    [Header("Colors")]
    public Color canAffordColor = Color.white;
    public Color cannotAffordColor = Color.red;
    public Color equippedColor = Color.gray;
    public Color ownedColor = Color.white;
    public Color enabledCycleColor = Color.white;
    public Color disabledCycleColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

    [Header("Fullscreen Shop Zoom-Fade Settings")]
    public float shopZoomDuration = 0.36f;
    public float shopZoomTargetFOV = 34f;

    private UpgradePath currentPath;
    private int viewingIndex = 0;
    private GameObject currentPreviewModel;

    private bool isAnimatingAction = false;
    private Vector3 originalScale;
    private Vector3 targetScale;

    // Fullscreen CRT Shop Runtime UI References
    private Canvas fullscreenShopCanvas;
    private CanvasGroup shopCanvasGroup;
    private Image shopBackdropFadeImage;
    private RectTransform shopMainWindowRect;
    private TextMeshProUGUI shopWalletText;
    private TextMeshProUGUI shopCategorySubtitleText;

    private Button fsDvdTabBtn;
    private Button fsPlayerTabBtn;
    private Button fsRemoteTabBtn;
    private Image fsDvdTabImg;
    private Image fsPlayerTabImg;
    private Image fsRemoteTabImg;
    private TextMeshProUGUI fsDvdTabLabel;
    private TextMeshProUGUI fsPlayerTabLabel;
    private TextMeshProUGUI fsRemoteTabLabel;

    private RectTransform catalogGridContainer;
    private readonly List<ShopCatalogItemButton> catalogButtons = new List<ShopCatalogItemButton>();

    private TextMeshProUGUI inspectorTierBadgeText;
    private TextMeshProUGUI inspectorTitleText;
    private TextMeshProUGUI inspectorCategoryTagText;
    private TextMeshProUGUI inspectorStatsText;
    private TextMeshProUGUI inspectorPriceText;
    private TextMeshProUGUI inspectorStatusHintText;
    private Button inspectorBuyEquipBtn;
    private Image inspectorBuyEquipBtnImg;
    private TextMeshProUGUI inspectorBuyEquipBtnLabel;
    private Button inspectorPrevBtn;
    private Button inspectorNextBtn;
    private Button shopExitBtn;
    private RectTransform inspectorPreviewBoxRect;
    private Transform shop3DPreviewAnchor;
    private Transform shop3DPreviewSpinner;
    private GameObject shop3DPreviewInstance;
    private Vector3 currentBasePreviewRotation = new Vector3(-32f, 0f, 18f);
    private float previewSpinAngle = 0f;
    private RectTransform shopVirtualCursorRect;
    private Image shopVirtualCursorRingImg;
    private Sprite cachedDefaultCursorSprite;
    private Sprite cachedInteractCursorSprite;
    private Vector2 cachedDefaultCursorSize = new Vector2(28f, 28f);
    private Vector2 cachedInteractCursorSize = new Vector2(36f, 36f);
    private float shopBackdropMaxAlpha = 0.99f;

    private Camera boundPlayerCamera;
    private float savedCameraFOV = 60f;
    private bool hasSavedCameraFOV = false;
    private int savedCameraCullingMask = -1;
    private CameraClearFlags savedCameraClearFlags;
    private Color savedCameraBgColor;
    private bool hasSavedCameraIsolation = false;
    private Coroutine activeTransitionRoutine;

    private class ShopCatalogItemButton
    {
        public GameObject rootObj;
        public Button button;
        public Image bgImage;
        public Image accentBarImage;
        public TextMeshProUGUI indexLabel;
        public TextMeshProUGUI nameLabel;
        public TextMeshProUGUI statusPriceLabel;
        public int tierIndex;
    }

    private void Awake()
    {
        originalScale = transform.localScale;
        if (originalScale == Vector3.zero) originalScale = new Vector3(0.002f, 0.002f, 0.002f);
        transform.localScale = Vector3.zero;
        targetScale = Vector3.zero;

        ToggleAllHitboxes(false);
    }

    private void OnDestroy()
    {
        if (hasSavedCameraIsolation)
        {
            RestoreShopCameraAndHudIsolation();
        }
        if (ActiveMenu == this)
        {
            ActiveMenu = null;
            GridCrawlerController.InputFrozen = false;
            if (!PauseManager.IsGamePaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    private void EnableShopCameraAndHudIsolation()
    {
        GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
        if (crawler != null)
        {
            crawler.SetHudCropPanelVisible(false);
            crawler.SetFocusCursorVisuals(false, false, "");
        }

        if (boundPlayerCamera != null && !hasSavedCameraIsolation)
        {
            savedCameraCullingMask = boundPlayerCamera.cullingMask;
            savedCameraClearFlags = boundPlayerCamera.clearFlags;
            savedCameraBgColor = boundPlayerCamera.backgroundColor;
            hasSavedCameraIsolation = true;

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0) uiLayer = 5;
            boundPlayerCamera.cullingMask = (1 << uiLayer);
            boundPlayerCamera.clearFlags = CameraClearFlags.SolidColor;
            boundPlayerCamera.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 1f);
        }
    }

    private void RestoreShopCameraAndHudIsolation()
    {
        GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
        if (crawler != null)
        {
            crawler.SetHudCropPanelVisible(true);
        }

        if (hasSavedCameraIsolation && boundPlayerCamera != null)
        {
            boundPlayerCamera.cullingMask = savedCameraCullingMask;
            boundPlayerCamera.clearFlags = savedCameraClearFlags;
            boundPlayerCamera.backgroundColor = savedCameraBgColor;
        }
        hasSavedCameraIsolation = false;
    }

    public void EnsureUnifiedTabsExist() { }
    public void WireTabEvents() { }

    private void Update()
    {
        // Always keep old worldspace canvas hidden at scale 0 so only the fullscreen CRT shop is used
        if (transform.localScale != Vector3.zero)
        {
            transform.localScale = Vector3.zero;
        }

        if (!IsMenuOpen) return;

        // Keep OS cursor LOCKED and hidden so it never leaves the Unity Editor Game View tab!
        if (!PauseManager.IsGamePaused)
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
        }

        UpdateShopVirtualCursorAndInput();

        // Allow closing shop with Escape or Right-Click
        if (!isAnimatingAction && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseMenu();
            return;
        }

        // Rotate 3D preview model inside inspector box around vertical Y axis while preserving base tilt (like FloatingPreview)
        if (shop3DPreviewInstance != null && shop3DPreviewSpinner != null && inspectorPreviewBoxRect != null && boundPlayerCamera != null)
        {
            Position3DPreviewInViewport();
            previewSpinAngle = (previewSpinAngle + 52f * Time.unscaledDeltaTime) % 360f;
            shop3DPreviewSpinner.localRotation = Quaternion.Euler(0f, previewSpinAngle, 0f) * Quaternion.Euler(currentBasePreviewRotation);
        }

        if (!isAnimatingAction && currentPath != null)
        {
            RefreshFullscreenShopLiveStates();
        }
    }

    private void UpdateShopVirtualCursorAndInput()
    {
        if (fullscreenShopCanvas == null) return;

        Vector2 vPos = GridCrawlerController.GetActiveVirtualScreenPos();
        Camera uiCam = fullscreenShopCanvas.renderMode == RenderMode.ScreenSpaceCamera ? fullscreenShopCanvas.worldCamera : null;

        if (shopVirtualCursorRect != null)
        {
            shopVirtualCursorRect.SetAsLastSibling();
            if (!shopVirtualCursorRect.gameObject.activeSelf) shopVirtualCursorRect.gameObject.SetActive(true);

            RectTransform canvasRT = fullscreenShopCanvas.GetComponent<RectTransform>();
            if (canvasRT != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, vPos, uiCam, out Vector2 localPt))
            {
                shopVirtualCursorRect.anchoredPosition = localPt;
            }
        }

        if (isAnimatingAction) return;

        bool hoveringInteractive = false;
        if (IsRectHovered(shopExitBtn, vPos, uiCam) ||
            IsRectHovered(fsDvdTabBtn, vPos, uiCam) ||
            IsRectHovered(fsPlayerTabBtn, vPos, uiCam) ||
            IsRectHovered(fsRemoteTabBtn, vPos, uiCam) ||
            IsRectHovered(inspectorPrevBtn, vPos, uiCam) ||
            IsRectHovered(inspectorNextBtn, vPos, uiCam) ||
            (inspectorBuyEquipBtn != null && inspectorBuyEquipBtn.interactable && IsRectHovered(inspectorBuyEquipBtn, vPos, uiCam)))
        {
            hoveringInteractive = true;
        }
        else
        {
            for (int i = 0; i < catalogButtons.Count; i++)
            {
                if (catalogButtons[i] != null && IsRectHovered(catalogButtons[i].button, vPos, uiCam))
                {
                    hoveringInteractive = true;
                    break;
                }
            }
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

        if (shopVirtualCursorRingImg != null)
        {
            Sprite targetSprite = hoveringInteractive && cachedInteractCursorSprite != null
                ? cachedInteractCursorSprite
                : cachedDefaultCursorSprite;
            // Align pivot with the pointer tip when showing the arrow cursor so button hitboxes match the visual tip 1:1!
            shopVirtualCursorRect.pivot = hoveringInteractive ? new Vector2(0.5f, 0.5f) : new Vector2(0.28f, 0.82f);
            if (targetSprite != null)
            {
                shopVirtualCursorRingImg.sprite = targetSprite;
                shopVirtualCursorRingImg.color = new Color(1f, 0.953f, 0.918f, 1f);
                shopVirtualCursorRect.sizeDelta = hoveringInteractive ? cachedInteractCursorSize : cachedDefaultCursorSize;
            }
            else
            {
                shopVirtualCursorRingImg.color = Color.white;
                shopVirtualCursorRect.sizeDelta = new Vector2(21.2f, 31.8f);
            }
            shopVirtualCursorRect.localScale = Vector3.one;
        }

        bool leftClicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (!leftClicked) return;

        if (IsRectHovered(shopExitBtn, vPos, uiCam)) { CloseMenu(); return; }
        if (IsRectHovered(fsDvdTabBtn, vPos, uiCam)) { SelectDvdTab(); return; }
        if (IsRectHovered(fsPlayerTabBtn, vPos, uiCam)) { SelectPlayerTab(); return; }
        if (IsRectHovered(fsRemoteTabBtn, vPos, uiCam)) { SelectRemoteTab(); return; }
        if (IsRectHovered(inspectorPrevBtn, vPos, uiCam)) { CyclePrev(); return; }
        if (IsRectHovered(inspectorNextBtn, vPos, uiCam)) { CycleNext(); return; }
        if (inspectorBuyEquipBtn != null && inspectorBuyEquipBtn.interactable && IsRectHovered(inspectorBuyEquipBtn, vPos, uiCam))
        {
            OnBuyClicked();
            return;
        }

        for (int i = 0; i < catalogButtons.Count; i++)
        {
            if (catalogButtons[i] != null && IsRectHovered(catalogButtons[i].button, vPos, uiCam))
            {
                SelectTierIndexFromGrid(catalogButtons[i].tierIndex);
                return;
            }
        }
    }

    private static bool IsRectHovered(Button btn, Vector2 screenPos, Camera uiCam)
    {
        if (btn == null || !btn.gameObject.activeInHierarchy) return false;
        RectTransform rt = btn.GetComponent<RectTransform>();
        return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, uiCam);
    }

    public void OpenMenuForPath(int pathIndex)
    {
        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.EnsureRemoteUpgradePath();
            int safeIdx = Mathf.Clamp(pathIndex, 0, Mathf.Max(0, UpgradeManager.Instance.upgradePaths.Count - 1));
            OpenMenu(UpgradeManager.Instance.upgradePaths[safeIdx]);
        }
    }

    public void OpenMenu(UpgradePath path)
    {
        if (isAnimatingAction || path == null) return;

        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.EnsureRemoteUpgradePath();
        }

        if (ActiveMenu != null && ActiveMenu != this)
        {
            ActiveMenu.CloseMenuImmediate();
        }
        ActiveMenu = this;

        currentPath = path;
        isPlayerMenu = (currentPath.category == UpgradeCategory.SpeedMultiplier);
        viewingIndex = Mathf.Clamp(path.equippedIndex, 0, Mathf.Max(0, path.tiers.Count - 1));
        targetScale = Vector3.zero;
        transform.localScale = Vector3.zero;
        ToggleAllHitboxes(false);

        EnsureFullscreenShopCanvasBuilt();

        if (!IsMenuOpen)
        {
            IsMenuOpen = true;
            if (audioSource != null && openSound != null) audioSource.PlayOneShot(openSound);
            if (objectsToDisable != null)
            {
                foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(false); }
            }

            if (activeTransitionRoutine != null) StopCoroutine(activeTransitionRoutine);
            activeTransitionRoutine = StartCoroutine(OpenShopZoomFadeRoutine());
        }
        else
        {
            RebuildCatalogGridForCurrentPath();
            UpdateInspectorPanel(true);
        }
    }

    private IEnumerator OpenShopZoomFadeRoutine()
    {
        isAnimatingAction = true;

        // Freeze room movement & hide 3D room crosshair
        GridCrawlerController.InputFrozen = true;
        GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>();
        if (crawler != null)
        {
            crawler.SetFocusCursorVisuals(false, false, "");
        }

        // Keep OS cursor locked so it never leaves the Editor tab, and ensure gameplay CRT is used (never darker pause CRT!)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        CrtPostProcessManager.SetGameplayCrtActive();

        boundPlayerCamera = ResolveMainPlayerCamera();
        if (boundPlayerCamera != null)
        {
            if (!hasSavedCameraFOV)
            {
                savedCameraFOV = boundPlayerCamera.fieldOfView;
                hasSavedCameraFOV = true;
            }
            fullscreenShopCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            fullscreenShopCanvas.worldCamera = boundPlayerCamera;
            fullscreenShopCanvas.planeDistance = 0.25f;
            fullscreenShopCanvas.sortingOrder = 32760;
        }

        EnableShopCameraAndHudIsolation();
        fullscreenShopCanvas.gameObject.SetActive(true);
        RebuildCatalogGridForCurrentPath();
        UpdateInspectorPanel(true);
        UpdateShopVirtualCursorAndInput();

        float startFOV = boundPlayerCamera != null ? boundPlayerCamera.fieldOfView : 60f;
        float endFOV = shopZoomTargetFOV;
        float elapsed = 0f;
        float dur = Mathf.Max(0.15f, shopZoomDuration);

        Color baseBackdropCol = shopBackdropFadeImage != null ? shopBackdropFadeImage.color : new Color(0.03f, 0.05f, 0.08f, 0.99f);
        if (shopCanvasGroup != null) shopCanvasGroup.alpha = 0f;
        if (shopMainWindowRect != null) shopMainWindowRect.localScale = new Vector3(0.92f, 0.92f, 1f);
        if (shopBackdropFadeImage != null) shopBackdropFadeImage.color = new Color(baseBackdropCol.r, baseBackdropCol.g, baseBackdropCol.b, 0f);

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float smooth = t * t * (3f - 2f * t);

            if (boundPlayerCamera != null)
            {
                boundPlayerCamera.fieldOfView = Mathf.Lerp(startFOV, endFOV, smooth);
            }
            if (shopBackdropFadeImage != null)
            {
                shopBackdropFadeImage.color = new Color(baseBackdropCol.r, baseBackdropCol.g, baseBackdropCol.b, Mathf.Lerp(0f, shopBackdropMaxAlpha, smooth));
            }
            if (shopCanvasGroup != null)
            {
                shopCanvasGroup.alpha = smooth;
            }
            if (shopMainWindowRect != null)
            {
                float sc = Mathf.Lerp(0.92f, 1.0f, smooth);
                shopMainWindowRect.localScale = new Vector3(sc, sc, 1f);
            }
            UpdateShopVirtualCursorAndInput();
            yield return null;
        }

        if (boundPlayerCamera != null) boundPlayerCamera.fieldOfView = endFOV;
        if (shopBackdropFadeImage != null) shopBackdropFadeImage.color = new Color(baseBackdropCol.r, baseBackdropCol.g, baseBackdropCol.b, shopBackdropMaxAlpha);
        if (shopCanvasGroup != null) shopCanvasGroup.alpha = 1f;
        if (shopMainWindowRect != null) shopMainWindowRect.localScale = Vector3.one;

        isAnimatingAction = false;
        activeTransitionRoutine = null;
    }

    public void CloseMenu()
    {
        if (!IsMenuOpen || isAnimatingAction) return;

        // While in the Tutorial Starter Store, require both starter items ($1,000 total) to be purchased before closing
        if (TutorialManager.IsTutorialStarterShopActive && TutorialManager.Instance != null && !TutorialManager.Instance.AreBothStarterItemsPurchased)
        {
            return;
        }

        if (audioSource != null && closeSound != null) audioSource.PlayOneShot(closeSound);

        if (activeTransitionRoutine != null) StopCoroutine(activeTransitionRoutine);
        activeTransitionRoutine = StartCoroutine(CloseShopZoomFadeRoutine());
    }

    private void CloseMenuImmediate()
    {
        if (activeTransitionRoutine != null)
        {
            StopCoroutine(activeTransitionRoutine);
            activeTransitionRoutine = null;
        }
        IsMenuOpen = false;
        isAnimatingAction = false;
        if (ActiveMenu == this) ActiveMenu = null;

        RestoreShopCameraAndHudIsolation();
        if (boundPlayerCamera != null && hasSavedCameraFOV)
        {
            boundPlayerCamera.fieldOfView = savedCameraFOV;
            hasSavedCameraFOV = false;
        }
        if (shop3DPreviewInstance != null) Destroy(shop3DPreviewInstance);
        if (fullscreenShopCanvas != null) fullscreenShopCanvas.gameObject.SetActive(false);

        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(true); }
        }
        GridCrawlerController.InputFrozen = false;
        if (!PauseManager.IsGamePaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        CrtPostProcessManager.SetGameplayCrtActive();
    }

    private IEnumerator CloseShopZoomFadeRoutine()
    {
        isAnimatingAction = true;
        IsMenuOpen = false;
        if (ActiveMenu == this) ActiveMenu = null;

        if (shop3DPreviewInstance != null)
        {
            Destroy(shop3DPreviewInstance);
            shop3DPreviewInstance = null;
        }

        float startFOV = boundPlayerCamera != null ? boundPlayerCamera.fieldOfView : shopZoomTargetFOV;
        float endFOV = hasSavedCameraFOV ? savedCameraFOV : 60f;
        float elapsed = 0f;
        float dur = Mathf.Max(0.15f, shopZoomDuration * 0.85f);
        Color baseBackdropCol = shopBackdropFadeImage != null ? shopBackdropFadeImage.color : new Color(0.03f, 0.05f, 0.08f, shopBackdropMaxAlpha);

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float smooth = t * t * (3f - 2f * t);

            if (boundPlayerCamera != null)
            {
                boundPlayerCamera.fieldOfView = Mathf.Lerp(startFOV, endFOV, smooth);
            }
            if (shopBackdropFadeImage != null)
            {
                shopBackdropFadeImage.color = new Color(baseBackdropCol.r, baseBackdropCol.g, baseBackdropCol.b, Mathf.Lerp(shopBackdropMaxAlpha, 0f, smooth));
            }
            if (shopCanvasGroup != null)
            {
                shopCanvasGroup.alpha = 1f - smooth;
            }
            if (shopMainWindowRect != null)
            {
                float sc = Mathf.Lerp(1.0f, 0.93f, smooth);
                shopMainWindowRect.localScale = new Vector3(sc, sc, 1f);
            }
            yield return null;
        }

        RestoreShopCameraAndHudIsolation();
        if (boundPlayerCamera != null && hasSavedCameraFOV)
        {
            boundPlayerCamera.fieldOfView = savedCameraFOV;
            hasSavedCameraFOV = false;
        }

        if (fullscreenShopCanvas != null)
        {
            fullscreenShopCanvas.gameObject.SetActive(false);
        }

        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(true); }
        }

        GridCrawlerController.InputFrozen = false;
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
        CrtPostProcessManager.SetGameplayCrtActive();

        isAnimatingAction = false;
        activeTransitionRoutine = null;
    }

    public void SelectDvdTab() => SwitchToCategoryTab(UpgradeCategory.MoneyMultiplier);
    public void SelectPlayerTab() => SwitchToCategoryTab(UpgradeCategory.SpeedMultiplier);
    public void SelectRemoteTab() => SwitchToCategoryTab(UpgradeCategory.RemoteUpgrade);

    public void SwitchToCategoryTab(UpgradeCategory category)
    {
        if (isAnimatingAction || UpgradeManager.Instance == null) return;
        if (TutorialManager.IsTutorialStarterShopActive && category == UpgradeCategory.RemoteUpgrade) return;

        UpgradeManager.Instance.EnsureRemoteUpgradePath();
        UpgradePath targetPath = UpgradeManager.Instance.GetPathByCategory(category);
        if (targetPath == null || targetPath == currentPath) return;

        if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);

        currentPath = targetPath;
        isPlayerMenu = (currentPath.category == UpgradeCategory.SpeedMultiplier);
        viewingIndex = Mathf.Clamp(currentPath.equippedIndex, 0, Mathf.Max(0, currentPath.tiers.Count - 1));

        RebuildCatalogGridForCurrentPath();
        UpdateInspectorPanel(true);
    }

    public void SelectTierIndexFromGrid(int tierIdx)
    {
        if (isAnimatingAction || currentPath == null || currentPath.tiers == null) return;
        if (tierIdx < 0 || tierIdx >= currentPath.tiers.Count) return;

        if (viewingIndex != tierIdx)
        {
            if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);
            viewingIndex = tierIdx;
            UpdateInspectorPanel(true);
            RefreshFullscreenShopLiveStates();
        }
    }

    public void CycleNext()
    {
        int maxTiers = TutorialManager.IsTutorialStarterShopActive ? 1 : (currentPath != null && currentPath.tiers != null ? currentPath.tiers.Count : 0);
        if (currentPath == null || currentPath.tiers == null || maxTiers <= 1 || isAnimatingAction) return;
        viewingIndex = (viewingIndex + 1) % maxTiers;
        if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);
        UpdateInspectorPanel(true);
        RefreshFullscreenShopLiveStates();
    }

    public void CyclePrev()
    {
        int maxTiers = TutorialManager.IsTutorialStarterShopActive ? 1 : (currentPath != null && currentPath.tiers != null ? currentPath.tiers.Count : 0);
        if (currentPath == null || currentPath.tiers == null || maxTiers <= 1 || isAnimatingAction) return;
        viewingIndex = (viewingIndex - 1 + maxTiers) % maxTiers;
        if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);
        UpdateInspectorPanel(true);
        RefreshFullscreenShopLiveStates();
    }

    public void OnBuyClicked()
    {
        if (isAnimatingAction || currentPath == null || UpgradeManager.Instance == null) return;
        bool isUnlocked = currentPath.unlockedIndices.Contains(viewingIndex);

        if (isUnlocked)
        {
            if (currentPath.equippedIndex != viewingIndex)
            {
                if (audioSource != null && equipSound != null) audioSource.PlayOneShot(equipSound);
                UpgradeManager.Instance.FinalizeEquip(currentPath, viewingIndex);
                UpdateInspectorPanel(false);
                RefreshFullscreenShopLiveStates();
            }
        }
        else
        {
            if (UpgradeManager.Instance.TryPurchaseUpgrade(currentPath, viewingIndex))
            {
                if (audioSource != null && buySound != null) audioSource.PlayOneShot(buySound);
                // Automatically equip newly purchased upgrade right away!
                UpgradeManager.Instance.FinalizeEquip(currentPath, viewingIndex);
                UpdateInspectorPanel(false);
                RefreshFullscreenShopLiveStates();
            }
        }
    }

    // =========================================================================
    // FULLSCREEN CRT SHOP BUILDER & UI LOGIC (RESPECTS HIERARCHY EDITS)
    // =========================================================================

    private Camera ResolveMainPlayerCamera()
    {
        SmoothCameraFollow smoothCam = FindObjectOfType<SmoothCameraFollow>(true);
        if (smoothCam != null)
        {
            Camera c = smoothCam.GetComponent<Camera>();
            if (c != null && c.targetTexture == null) return c;
        }
        GridCrawlerController crawler = FindObjectOfType<GridCrawlerController>(true);
        if (crawler != null && crawler.cameraScript != null)
        {
            Camera c = crawler.cameraScript.GetComponent<Camera>();
            if (c != null && c.targetTexture == null) return c;
        }
        if (Camera.main != null && Camera.main.targetTexture == null) return Camera.main;
        Camera[] allCams = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < allCams.Length; i++)
        {
            if (allCams[i] != null && allCams[i].targetTexture == null) return allCams[i];
        }
        return null;
    }

    private Canvas FindExistingFullscreenShopCanvasInScene()
    {
        Canvas[] allCanvases = Resources.FindObjectsOfTypeAll<Canvas>();
        string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        for (int i = 0; i < allCanvases.Length; i++)
        {
            Canvas c = allCanvases[i];
            if (c != null && c.gameObject.name == "FullscreenUpgradeShopCanvas" && c.gameObject.scene.name == activeSceneName)
            {
                return c;
            }
        }
        return null;
    }

    public Canvas EnsureFullscreenShopCanvasBuilt()
    {
        if (Application.isPlaying && EventSystem.current == null)
        {
            GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        if (fullscreenShopCanvas == null)
        {
            fullscreenShopCanvas = FindExistingFullscreenShopCanvasInScene();
        }

        TMP_FontAsset sharedFont = titleText != null ? titleText.font : null;
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 5;

        GameObject canvasObj;
        bool isNewCanvas = false;
        if (fullscreenShopCanvas == null)
        {
            canvasObj = new GameObject("FullscreenUpgradeShopCanvas");
            canvasObj.layer = uiLayer;
            fullscreenShopCanvas = canvasObj.AddComponent<Canvas>();
            isNewCanvas = true;
        }
        else
        {
            canvasObj = fullscreenShopCanvas.gameObject;
            canvasObj.layer = uiLayer;
        }

        boundPlayerCamera = ResolveMainPlayerCamera();
        if (boundPlayerCamera != null)
        {
            fullscreenShopCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            fullscreenShopCanvas.worldCamera = boundPlayerCamera;
            fullscreenShopCanvas.planeDistance = 0.25f;
        }
        else
        {
            fullscreenShopCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        fullscreenShopCanvas.sortingOrder = 32760;

        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (canvasObj.GetComponent<GraphicRaycaster>() == null)
        {
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        shopCanvasGroup = canvasObj.GetComponent<CanvasGroup>();
        if (shopCanvasGroup == null) shopCanvasGroup = canvasObj.AddComponent<CanvasGroup>();

        // 1. Opaque CRT Backdrop (alpha 0.99 so 3D room never bleeds through, while gameplay CRT stays bright!)
        GameObject backdropObj = GetOrCreateUIPanel("ShopBackdropFade", canvasObj.transform, new Color(0.03f, 0.05f, 0.08f, 0.99f), out bool createdBackdrop);
        RectTransform backdropRT = backdropObj.GetComponent<RectTransform>();
        backdropRT.anchorMin = Vector2.zero;
        backdropRT.anchorMax = Vector2.one;
        backdropRT.offsetMin = Vector2.zero;
        backdropRT.offsetMax = Vector2.zero;
        shopBackdropFadeImage = backdropObj.GetComponent<Image>();
        shopBackdropFadeImage.raycastTarget = false;
        shopBackdropFadeImage.color = new Color(0.03f, 0.05f, 0.08f, 0.99f);
        shopBackdropMaxAlpha = 0.99f;

        // 2. Main Shop Window Frame (1760 x 960)
        GameObject windowBorder = GetOrCreateUIPanel("ShopWindowBorder", canvasObj.transform, new Color(0.28f, 0.88f, 1.0f, 0.92f), out bool createdBorder);
        shopMainWindowRect = windowBorder.GetComponent<RectTransform>();
        if (createdBorder)
        {
            shopMainWindowRect.anchorMin = new Vector2(0.5f, 0.5f);
            shopMainWindowRect.anchorMax = new Vector2(0.5f, 0.5f);
            shopMainWindowRect.sizeDelta = new Vector2(1760f, 960f);
            shopMainWindowRect.anchoredPosition = Vector2.zero;
        }

        GameObject windowInner = GetOrCreateUIPanel("ShopWindowInner", windowBorder.transform, new Color(0.09f, 0.14f, 0.21f, 0.95f), out bool createdInner);
        if (createdInner)
        {
            RectTransform innerRT = windowInner.GetComponent<RectTransform>();
            innerRT.anchorMin = Vector2.zero;
            innerRT.anchorMax = Vector2.one;
            innerRT.offsetMin = new Vector2(3f, 3f);
            innerRT.offsetMax = new Vector2(-3f, -3f);
        }

        // 3. Top Header Bar
        GameObject headerBar = GetOrCreateUIPanel("HeaderBar", windowInner.transform, new Color(0.13f, 0.20f, 0.30f, 1f), out bool createdHeader);
        if (createdHeader)
        {
            RectTransform headerRT = headerBar.GetComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0f, 1f);
            headerRT.anchorMax = new Vector2(1f, 1f);
            headerRT.pivot = new Vector2(0.5f, 1f);
            headerRT.sizeDelta = new Vector2(0f, 76f);
            headerRT.anchoredPosition = Vector2.zero;
        }

        TextMeshProUGUI headerTitle = GetOrCreateUIText("ShopTitle", headerBar.transform, "RETRO DVD & HARDWARE CATALOG", 30f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, new Color(0.50f, 0.95f, 1f, 1f), sharedFont, out bool createdHTitle);
        if (createdHTitle)
        {
            RectTransform htRT = headerTitle.GetComponent<RectTransform>();
            htRT.anchorMin = new Vector2(0f, 0f);
            htRT.anchorMax = new Vector2(0.5f, 1f);
            htRT.offsetMin = new Vector2(28f, 0f);
            htRT.offsetMax = Vector2.zero;
        }

        // Wallet Badge in Header
        GameObject walletBox = GetOrCreateUIPanel("WalletBox", headerBar.transform, new Color(0.06f, 0.24f, 0.15f, 0.96f), out bool createdWallet);
        if (createdWallet)
        {
            RectTransform wbRT = walletBox.GetComponent<RectTransform>();
            wbRT.anchorMin = new Vector2(1f, 0.5f);
            wbRT.anchorMax = new Vector2(1f, 0.5f);
            wbRT.pivot = new Vector2(1f, 0.5f);
            wbRT.sizeDelta = new Vector2(380f, 52f);
            wbRT.anchoredPosition = new Vector2(-235f, 0f);
        }

        shopWalletText = GetOrCreateUIText("WalletText", walletBox.transform, "BALANCE: $0.00", 24f, FontStyles.Bold, TextAlignmentOptions.Center, new Color(0.42f, 1f, 0.62f, 1f), sharedFont, out bool createdWTxt);
        if (createdWTxt) StretchFull(shopWalletText.GetComponent<RectTransform>());

        // Exit Shop Button in Header
        shopExitBtn = GetOrCreateUIButton("ExitShopBtn", headerBar.transform, "[ EXIT SHOP (ESC) ]", 20f, new Color(0.76f, 0.20f, 0.24f, 1f), Color.white, sharedFont, out _, out _, out bool createdExit);
        if (createdExit)
        {
            RectTransform exitRT = shopExitBtn.GetComponent<RectTransform>();
            exitRT.anchorMin = new Vector2(1f, 0.5f);
            exitRT.anchorMax = new Vector2(1f, 0.5f);
            exitRT.pivot = new Vector2(1f, 0.5f);
            exitRT.sizeDelta = new Vector2(195f, 52f);
            exitRT.anchoredPosition = new Vector2(-22f, 0f);
        }
        shopExitBtn.onClick.RemoveListener(CloseMenu);
        shopExitBtn.onClick.AddListener(CloseMenu);

        // 4. Category Tabs Bar
        GameObject tabStrip = GetOrCreateUIPanel("CategoryTabStrip", windowInner.transform, new Color(0.09f, 0.14f, 0.21f, 1f), out bool createdTabs);
        if (createdTabs)
        {
            RectTransform tabStripRT = tabStrip.GetComponent<RectTransform>();
            tabStripRT.anchorMin = new Vector2(0f, 1f);
            tabStripRT.anchorMax = new Vector2(1f, 1f);
            tabStripRT.pivot = new Vector2(0.5f, 1f);
            tabStripRT.sizeDelta = new Vector2(0f, 68f);
            tabStripRT.anchoredPosition = new Vector2(0f, -78f);
        }

        fsDvdTabBtn = GetOrCreateUIButton("Tab_DVD", tabStrip.transform, "[ 1. DVD DISCS (BOUNCE VALUE) ]", 21f, inactiveTabColor, Color.white, sharedFont, out fsDvdTabImg, out fsDvdTabLabel, out bool createdT1);
        if (createdT1)
        {
            RectTransform t1 = fsDvdTabBtn.GetComponent<RectTransform>();
            t1.anchorMin = new Vector2(0.015f, 0.12f);
            t1.anchorMax = new Vector2(0.335f, 0.88f);
            t1.offsetMin = Vector2.zero;
            t1.offsetMax = Vector2.zero;
        }
        fsDvdTabBtn.onClick.RemoveListener(SelectDvdTab);
        fsDvdTabBtn.onClick.AddListener(SelectDvdTab);

        fsPlayerTabBtn = GetOrCreateUIButton("Tab_Player", tabStrip.transform, "[ 2. DVD PLAYERS (SPEED) ]", 21f, inactiveTabColor, Color.white, sharedFont, out fsPlayerTabImg, out fsPlayerTabLabel, out bool createdT2);
        if (createdT2)
        {
            RectTransform t2 = fsPlayerTabBtn.GetComponent<RectTransform>();
            t2.anchorMin = new Vector2(0.345f, 0.12f);
            t2.anchorMax = new Vector2(0.655f, 0.88f);
            t2.offsetMin = Vector2.zero;
            t2.offsetMax = Vector2.zero;
        }
        fsPlayerTabBtn.onClick.RemoveListener(SelectPlayerTab);
        fsPlayerTabBtn.onClick.AddListener(SelectPlayerTab);

        fsRemoteTabBtn = GetOrCreateUIButton("Tab_Remote", tabStrip.transform, "[ 3. TV REMOTES & MINIGAMES ]", 21f, inactiveTabColor, Color.white, sharedFont, out fsRemoteTabImg, out fsRemoteTabLabel, out bool createdT3);
        if (createdT3)
        {
            RectTransform t3 = fsRemoteTabBtn.GetComponent<RectTransform>();
            t3.anchorMin = new Vector2(0.665f, 0.12f);
            t3.anchorMax = new Vector2(0.985f, 0.88f);
            t3.offsetMin = Vector2.zero;
            t3.offsetMax = Vector2.zero;
        }
        fsRemoteTabBtn.onClick.RemoveListener(SelectRemoteTab);
        fsRemoteTabBtn.onClick.AddListener(SelectRemoteTab);

        // 5. Left Column: Full Category Catalog Grid
        GameObject leftPanel = GetOrCreateUIPanel("LeftCatalogPanel", windowInner.transform, new Color(0.07f, 0.11f, 0.17f, 0.96f), out bool createdLeft);
        if (createdLeft)
        {
            RectTransform leftRT = leftPanel.GetComponent<RectTransform>();
            leftRT.anchorMin = new Vector2(0.015f, 0.025f);
            leftRT.anchorMax = new Vector2(0.615f, 0.825f);
            leftRT.offsetMin = Vector2.zero;
            leftRT.offsetMax = Vector2.zero;
        }

        shopCategorySubtitleText = GetOrCreateUIText("CatalogHeader", leftPanel.transform, "SELECT AN UPGRADE TIER:", 19f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, new Color(0.70f, 0.90f, 1.0f, 1f), sharedFont, out bool createdSub);
        if (createdSub)
        {
            RectTransform subRT = shopCategorySubtitleText.GetComponent<RectTransform>();
            subRT.anchorMin = new Vector2(0f, 1f);
            subRT.anchorMax = new Vector2(1f, 1f);
            subRT.pivot = new Vector2(0.5f, 1f);
            subRT.sizeDelta = new Vector2(0f, 42f);
            subRT.offsetMin = new Vector2(18f, -42f);
            subRT.offsetMax = new Vector2(-18f, 0f);
        }

        Transform existingGrid = leftPanel.transform.Find("CatalogGridContainer");
        if (existingGrid == null)
        {
            GameObject gridObj = new GameObject("CatalogGridContainer", typeof(RectTransform), typeof(GridLayoutGroup));
            gridObj.layer = canvasObj.layer;
            gridObj.transform.SetParent(leftPanel.transform, false);
            catalogGridContainer = gridObj.GetComponent<RectTransform>();
            catalogGridContainer.anchorMin = new Vector2(0f, 0f);
            catalogGridContainer.anchorMax = new Vector2(1f, 1f);
            catalogGridContainer.offsetMin = new Vector2(16f, 16f);
            catalogGridContainer.offsetMax = new Vector2(-16f, -46f);

            GridLayoutGroup glg = gridObj.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(500f, 92f);
            glg.spacing = new Vector2(16f, 10f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            glg.childAlignment = TextAnchor.UpperCenter;
        }
        else
        {
            catalogGridContainer = existingGrid.GetComponent<RectTransform>();
        }

        // 6. Right Column: Detailed Selected Upgrade Inspector & Purchase Panel
        GameObject rightPanel = GetOrCreateUIPanel("RightInspectorPanel", windowInner.transform, new Color(0.11f, 0.16f, 0.24f, 0.98f), out bool createdRight);
        if (createdRight)
        {
            RectTransform rightRT = rightPanel.GetComponent<RectTransform>();
            rightRT.anchorMin = new Vector2(0.63f, 0.025f);
            rightRT.anchorMax = new Vector2(0.985f, 0.825f);
            rightRT.offsetMin = Vector2.zero;
            rightRT.offsetMax = Vector2.zero;
        }

        // Tier Badge + Prev/Next Row at top of Inspector
        inspectorPrevBtn = GetOrCreateUIButton("PrevTierBtn", rightPanel.transform, "< PREV", 18f, new Color(0.18f, 0.28f, 0.38f, 1f), Color.white, sharedFont, out _, out _, out bool createdPrev);
        if (createdPrev)
        {
            RectTransform prevRT = inspectorPrevBtn.GetComponent<RectTransform>();
            prevRT.anchorMin = new Vector2(0.04f, 0.90f);
            prevRT.anchorMax = new Vector2(0.28f, 0.97f);
            prevRT.offsetMin = Vector2.zero;
            prevRT.offsetMax = Vector2.zero;
        }
        inspectorPrevBtn.onClick.RemoveListener(CyclePrev);
        inspectorPrevBtn.onClick.AddListener(CyclePrev);

        inspectorTierBadgeText = GetOrCreateUIText("TierBadge", rightPanel.transform, "TIER 1 OF 14", 20f, FontStyles.Bold, TextAlignmentOptions.Center, new Color(1f, 0.88f, 0.35f, 1f), sharedFont, out bool createdBadge);
        if (createdBadge)
        {
            RectTransform badgeRT = inspectorTierBadgeText.GetComponent<RectTransform>();
            badgeRT.anchorMin = new Vector2(0.29f, 0.90f);
            badgeRT.anchorMax = new Vector2(0.71f, 0.97f);
            badgeRT.offsetMin = Vector2.zero;
            badgeRT.offsetMax = Vector2.zero;
        }

        inspectorNextBtn = GetOrCreateUIButton("NextTierBtn", rightPanel.transform, "NEXT >", 18f, new Color(0.18f, 0.28f, 0.38f, 1f), Color.white, sharedFont, out _, out _, out bool createdNext);
        if (createdNext)
        {
            RectTransform nextRT = inspectorNextBtn.GetComponent<RectTransform>();
            nextRT.anchorMin = new Vector2(0.72f, 0.90f);
            nextRT.anchorMax = new Vector2(0.96f, 0.97f);
            nextRT.offsetMin = Vector2.zero;
            nextRT.offsetMax = Vector2.zero;
        }
        inspectorNextBtn.onClick.RemoveListener(CycleNext);
        inspectorNextBtn.onClick.AddListener(CycleNext);

        // Selected Tier Title
        inspectorTitleText = GetOrCreateUIText("InspectorTitle", rightPanel.transform, "STOCK CLICKER", 32f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, sharedFont, out bool createdITitle);
        inspectorTitleText.enableWordWrapping = true;
        if (createdITitle)
        {
            RectTransform titleRT = inspectorTitleText.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.05f, 0.78f);
            titleRT.anchorMax = new Vector2(0.95f, 0.89f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;
        }

        // 3D Preview Viewport Frame inside Inspector
        GameObject previewFrame = GetOrCreateUIPanel("PreviewViewportFrame", rightPanel.transform, new Color(0.05f, 0.08f, 0.12f, 0.96f), out bool createdPFrame);
        inspectorPreviewBoxRect = previewFrame.GetComponent<RectTransform>();
        if (createdPFrame)
        {
            inspectorPreviewBoxRect.anchorMin = new Vector2(0.08f, 0.44f);
            inspectorPreviewBoxRect.anchorMax = new Vector2(0.92f, 0.76f);
            inspectorPreviewBoxRect.offsetMin = Vector2.zero;
            inspectorPreviewBoxRect.offsetMax = Vector2.zero;
        }

        inspectorCategoryTagText = GetOrCreateUIText("PreviewTag", previewFrame.transform, "HARDWARE PREVIEW", 15f, FontStyles.Bold, TextAlignmentOptions.TopLeft, new Color(0.45f, 0.85f, 1.0f, 0.75f), sharedFont, out bool createdPTag);
        if (createdPTag)
        {
            RectTransform pTagRT = inspectorCategoryTagText.GetComponent<RectTransform>();
            pTagRT.anchorMin = Vector2.zero;
            pTagRT.anchorMax = Vector2.one;
            pTagRT.offsetMin = new Vector2(12f, 8f);
            pTagRT.offsetMax = new Vector2(-12f, -8f);
        }

        Transform existingAnchor = previewFrame.transform.Find("Shop3DPreviewAnchor");
        if (existingAnchor == null)
        {
            GameObject anchorGO = new GameObject("Shop3DPreviewAnchor");
            anchorGO.layer = canvasObj.layer;
            anchorGO.transform.SetParent(previewFrame.transform, false);
            shop3DPreviewAnchor = anchorGO.transform;
        }
        else
        {
            shop3DPreviewAnchor = existingAnchor;
        }

        Transform existingSpinner = shop3DPreviewAnchor.Find("PreviewSpinner");
        if (existingSpinner == null)
        {
            GameObject spinnerGO = new GameObject("PreviewSpinner");
            spinnerGO.layer = canvasObj.layer;
            spinnerGO.transform.SetParent(shop3DPreviewAnchor, false);
            shop3DPreviewSpinner = spinnerGO.transform;
        }
        else
        {
            shop3DPreviewSpinner = existingSpinner;
        }

        // Stats / Ability Box
        GameObject statsBox = GetOrCreateUIPanel("StatsBox", rightPanel.transform, new Color(0.07f, 0.12f, 0.18f, 1f), out bool createdSBox);
        if (createdSBox)
        {
            RectTransform statsBoxRT = statsBox.GetComponent<RectTransform>();
            statsBoxRT.anchorMin = new Vector2(0.06f, 0.27f);
            statsBoxRT.anchorMax = new Vector2(0.94f, 0.42f);
            statsBoxRT.offsetMin = Vector2.zero;
            statsBoxRT.offsetMax = Vector2.zero;
        }

        inspectorStatsText = GetOrCreateUIText("InspectorStats", statsBox.transform, "Direct Impulse Hit + Bonus Cash", 23f, FontStyles.Bold, TextAlignmentOptions.Center, new Color(0.48f, 1.0f, 0.75f, 1f), sharedFont, out bool createdIStats);
        inspectorStatsText.enableWordWrapping = true;
        if (createdIStats)
        {
            RectTransform stRT = inspectorStatsText.GetComponent<RectTransform>();
            stRT.anchorMin = Vector2.zero;
            stRT.anchorMax = Vector2.one;
            stRT.offsetMin = new Vector2(16f, 8f);
            stRT.offsetMax = new Vector2(-16f, -8f);
        }

        // Price & Ownership Readout
        inspectorPriceText = GetOrCreateUIText("InspectorPrice", rightPanel.transform, "COST: $0.00", 26f, FontStyles.Bold, TextAlignmentOptions.Center, new Color(1f, 0.88f, 0.30f, 1f), sharedFont, out bool createdIPrice);
        if (createdIPrice)
        {
            RectTransform prRT = inspectorPriceText.GetComponent<RectTransform>();
            prRT.anchorMin = new Vector2(0.06f, 0.18f);
            prRT.anchorMax = new Vector2(0.94f, 0.26f);
            prRT.offsetMin = Vector2.zero;
            prRT.offsetMax = Vector2.zero;
        }

        // Big Action Button ([ BUY & EQUIP ] / [ EQUIP ] / [ EQUIPPED ])
        inspectorBuyEquipBtn = GetOrCreateUIButton("InspectorBuyBtn", rightPanel.transform, "EQUIPPED", 28f, new Color(0.18f, 0.65f, 0.36f, 1f), Color.white, sharedFont, out inspectorBuyEquipBtnImg, out inspectorBuyEquipBtnLabel, out bool createdBuyBtn);
        if (createdBuyBtn)
        {
            RectTransform buyRT = inspectorBuyEquipBtn.GetComponent<RectTransform>();
            buyRT.anchorMin = new Vector2(0.06f, 0.05f);
            buyRT.anchorMax = new Vector2(0.94f, 0.16f);
            buyRT.offsetMin = Vector2.zero;
            buyRT.offsetMax = Vector2.zero;
        }
        inspectorBuyEquipBtn.onClick.RemoveListener(OnBuyClicked);
        inspectorBuyEquipBtn.onClick.AddListener(OnBuyClicked);

        // 7. In-Canvas Virtual CRT Cursor (uses the exact in-game cursor sprite while OS cursor stays locked!)
        EnsureShopVirtualCursorBuilt(canvasObj.transform);

        SetLayerRecursively(canvasObj, uiLayer);

        if (isNewCanvas && !IsMenuOpen)
        {
            canvasObj.SetActive(false);
        }

        return fullscreenShopCanvas;
    }

    private void EnsureShopVirtualCursorBuilt(Transform canvasRoot)
    {
        Transform existing = canvasRoot.Find("ShopVirtualCursor");
        GameObject curObj;
        if (existing != null)
        {
            curObj = existing.gameObject;
            // Remove any old custom procedural diamond cursor children ("Ring", "InnerCut", "CenterDot")
            for (int i = curObj.transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(curObj.transform.GetChild(i).gameObject);
            }
        }
        else
        {
            curObj = new GameObject("ShopVirtualCursor", typeof(RectTransform));
            curObj.transform.SetParent(canvasRoot, false);
        }

        curObj.layer = canvasRoot.gameObject.layer;
        shopVirtualCursorRect = curObj.GetComponent<RectTransform>();
        shopVirtualCursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        shopVirtualCursorRect.anchorMax = new Vector2(0.5f, 0.5f);
        shopVirtualCursorRect.pivot = new Vector2(0.5f, 0.5f);
        shopVirtualCursorRect.localRotation = Quaternion.identity;
        shopVirtualCursorRect.localScale = Vector3.one;

        shopVirtualCursorRingImg = curObj.GetComponent<Image>() ?? curObj.AddComponent<Image>();
        shopVirtualCursorRingImg.raycastTarget = false;
        shopVirtualCursorRingImg.preserveAspect = true;
        shopVirtualCursorRingImg.color = Color.white;

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

        shopVirtualCursorRect.pivot = new Vector2(0.28f, 0.82f);
        if (cachedDefaultCursorSprite != null)
        {
            shopVirtualCursorRingImg.sprite = cachedDefaultCursorSprite;
            shopVirtualCursorRingImg.color = new Color(1f, 0.953f, 0.918f, 1f);
            shopVirtualCursorRect.sizeDelta = cachedDefaultCursorSize;
        }
        else
        {
            shopVirtualCursorRect.sizeDelta = new Vector2(21.2f, 31.8f);
        }
    }

    private void RebuildCatalogGridForCurrentPath()
    {
        if (catalogGridContainer == null || currentPath == null || currentPath.tiers == null) return;

        for (int i = 0; i < catalogButtons.Count; i++)
        {
            if (catalogButtons[i] != null && catalogButtons[i].rootObj != null)
            {
                Destroy(catalogButtons[i].rootObj);
            }
        }
        catalogButtons.Clear();

        TMP_FontAsset sharedFont = titleText != null ? titleText.font : null;
        bool isStarterOnly = TutorialManager.IsTutorialStarterShopActive;
        int tierCount = isStarterOnly ? Mathf.Min(1, currentPath.tiers.Count) : currentPath.tiers.Count;

        if (isStarterOnly && viewingIndex != 0)
        {
            viewingIndex = 0;
        }

        if (fsRemoteTabBtn != null)
        {
            fsRemoteTabBtn.gameObject.SetActive(!isStarterOnly);
        }
        if (inspectorPrevBtn != null)
        {
            inspectorPrevBtn.gameObject.SetActive(!isStarterOnly);
        }
        if (inspectorNextBtn != null)
        {
            inspectorNextBtn.gameObject.SetActive(!isStarterOnly);
        }
        if (shopExitBtn != null)
        {
            bool canExit = !isStarterOnly || (TutorialManager.Instance != null && TutorialManager.Instance.AreBothStarterItemsPurchased);
            shopExitBtn.gameObject.SetActive(canExit);
        }

        // Compute exact 2-column cell size dynamically from CatalogGridContainer so both columns have the exact same item count & identical aligned dimensions!
        GridLayoutGroup glg = catalogGridContainer.GetComponent<GridLayoutGroup>();
        if (glg != null)
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(tierCount / 2f));
            float availW = catalogGridContainer.rect.width;
            float availH = catalogGridContainer.rect.height;
            if (availW < 200f) availW = 1020f;
            if (availH < 200f) availH = 700f;

            float spacingX = 16f;
            float spacingY = rows > 6 ? 8f : 11f;
            float cellW = Mathf.Floor((availW - spacingX) * 0.5f);
            float cellH = Mathf.Floor((availH - spacingY * (rows - 1)) / rows);
            cellH = Mathf.Clamp(cellH, 74f, 108f);

            glg.padding = new RectOffset(0, 0, 0, 0);
            glg.cellSize = new Vector2(cellW, cellH);
            glg.spacing = new Vector2(spacingX, spacingY);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            glg.childAlignment = TextAnchor.UpperCenter;
        }

        if (shopCategorySubtitleText != null)
        {
            string catLabel = currentPath.category == UpgradeCategory.MoneyMultiplier
                ? "DVD DISCS — MULTIPLIES BOUNCE PAYOUT & TV BACKGROUND VIDEO"
                : (currentPath.category == UpgradeCategory.SpeedMultiplier
                    ? "DVD PLAYERS — MULTIPLIES LOGO VELOCITY & CHASSIS MODEL"
                    : "TV REMOTES — CLICK TO EQUIP ANY UNLOCKED ABILITY OR MINIGAME");
            shopCategorySubtitleText.text = catLabel;
        }

        // Highlight active category tab
        HighlightCategoryTab(fsDvdTabImg, fsDvdTabLabel, currentPath.category == UpgradeCategory.MoneyMultiplier);
        HighlightCategoryTab(fsPlayerTabImg, fsPlayerTabLabel, currentPath.category == UpgradeCategory.SpeedMultiplier);
        HighlightCategoryTab(fsRemoteTabImg, fsRemoteTabLabel, currentPath.category == UpgradeCategory.RemoteUpgrade);

        for (int i = 0; i < tierCount; i++)
        {
            int capturedIdx = i;
            UpgradeTier tier = currentPath.tiers[i];

            GameObject cardObj = CreateUIPanel($"TierCard_{i}", catalogGridContainer, new Color(0.11f, 0.16f, 0.24f, 1f));
            Button btn = cardObj.AddComponent<Button>();
            Image bgImg = cardObj.GetComponent<Image>();
            btn.targetGraphic = bgImg;
            btn.onClick.AddListener(() => SelectTierIndexFromGrid(capturedIdx));

            // Left status bar accent
            GameObject accentObj = CreateUIPanel("AccentBar", cardObj.transform, new Color(0.35f, 0.78f, 0.98f, 1f));
            RectTransform accRT = accentObj.GetComponent<RectTransform>();
            accRT.anchorMin = new Vector2(0f, 0f);
            accRT.anchorMax = new Vector2(0f, 1f);
            accRT.pivot = new Vector2(0f, 0.5f);
            accRT.sizeDelta = new Vector2(8f, 0f);
            accRT.anchoredPosition = Vector2.zero;

            TextMeshProUGUI idxTxt = CreateUIText("IndexTxt", cardObj.transform, $"#{i + 1:00}", 17f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, new Color(0.62f, 0.82f, 0.96f, 0.95f), sharedFont);
            RectTransform idxRT = idxTxt.GetComponent<RectTransform>();
            idxRT.anchorMin = new Vector2(0f, 0.52f);
            idxRT.anchorMax = new Vector2(0.25f, 0.94f);
            idxRT.offsetMin = new Vector2(18f, 0f);
            idxRT.offsetMax = Vector2.zero;

            TextMeshProUGUI statusTxt = CreateUIText("StatusPriceTxt", cardObj.transform, "$0", 17f, FontStyles.Bold, TextAlignmentOptions.MidlineRight, new Color(1f, 0.88f, 0.30f, 1f), sharedFont);
            RectTransform stRT = statusTxt.GetComponent<RectTransform>();
            stRT.anchorMin = new Vector2(0.26f, 0.52f);
            stRT.anchorMax = new Vector2(1f, 0.94f);
            stRT.offsetMin = Vector2.zero;
            stRT.offsetMax = new Vector2(-14f, 0f);

            TextMeshProUGUI nameTxt = CreateUIText("NameTxt", cardObj.transform, tier != null ? tier.tierName : $"Tier {i + 1}", 20f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, Color.white, sharedFont);
            RectTransform nmRT = nameTxt.GetComponent<RectTransform>();
            nmRT.anchorMin = new Vector2(0f, 0.06f);
            nmRT.anchorMax = new Vector2(1f, 0.54f);
            nmRT.offsetMin = new Vector2(18f, 0f);
            nmRT.offsetMax = new Vector2(-12f, 0f);

            catalogButtons.Add(new ShopCatalogItemButton
            {
                rootObj = cardObj,
                button = btn,
                bgImage = bgImg,
                accentBarImage = accentObj.GetComponent<Image>(),
                indexLabel = idxTxt,
                nameLabel = nameTxt,
                statusPriceLabel = statusTxt,
                tierIndex = i
            });
        }

        RefreshFullscreenShopLiveStates();
    }

    private void HighlightCategoryTab(Image tabImg, TextMeshProUGUI tabLabel, bool isActive)
    {
        if (tabImg != null)
        {
            tabImg.color = isActive ? new Color(0.22f, 0.82f, 0.98f, 1f) : new Color(0.13f, 0.19f, 0.28f, 1f);
        }
        if (tabLabel != null)
        {
            tabLabel.color = isActive ? new Color(0.03f, 0.06f, 0.10f, 1f) : new Color(0.82f, 0.90f, 0.98f, 1f);
        }
    }

    private void UpdateInspectorPanel(bool respawn3DModel)
    {
        if (currentPath == null || currentPath.tiers == null || viewingIndex < 0 || viewingIndex >= currentPath.tiers.Count) return;

        UpgradeTier tier = currentPath.tiers[viewingIndex];
        if (inspectorTierBadgeText != null)
        {
            inspectorTierBadgeText.text = $"TIER {viewingIndex + 1} OF {currentPath.tiers.Count}";
        }
        if (inspectorTitleText != null)
        {
            inspectorTitleText.text = tier.tierName.ToUpperInvariant();
        }

        if (inspectorStatsText != null)
        {
            if (currentPath.category == UpgradeCategory.MoneyMultiplier)
            {
                decimal baseEdge = (UpgradeManager.Instance != null && UpgradeManager.Instance.bouncer != null)
                    ? UpgradeManager.Instance.bouncer.edgeReward
                    : 0.01m;
                decimal bounceMoney = baseEdge * (decimal)tier.multiplierValue;
                inspectorStatsText.text = $"PAYOUT: {CurrencyFormatter.Format(bounceMoney)} / BOUNCE\n(Multiplier: x{tier.multiplierValue:N0})";
            }
            else if (currentPath.category == UpgradeCategory.SpeedMultiplier)
            {
                inspectorStatsText.text = $"BOUNCE VELOCITY: x{tier.multiplierValue:0.00}\nUpgrades Physical Player Deck & Logo";
            }
            else if (currentPath.category == UpgradeCategory.RemoteUpgrade)
            {
                inspectorStatsText.text = !string.IsNullOrEmpty(tier.customDescription)
                    ? tier.customDescription
                    : "TV Remote Special Function";
            }
        }

        if (respawn3DModel)
        {
            SpawnShop3DPreview(tier);
        }

        RefreshFullscreenShopLiveStates();
    }

    private GameObject ResolvePreviewSourceObject(UpgradeTier tier)
    {
        if (tier == null) return null;

        if (currentPath != null && currentPath.category == UpgradeCategory.RemoteUpgrade)
        {
            // If tier points to the parent 'remote' hitbox container, prefer the actual 3D 'RemoteControlTV' child mesh!
            if (tier.previewPrefab != null && tier.previewPrefab.name != "remote") return tier.previewPrefab;
            if (tier.worldGameObject != null && tier.worldGameObject.name != "remote") return tier.worldGameObject;

            GameObject foundRemote = UpgradeManager.FindRemoteVisualObjectInScene();
            if (foundRemote != null) return foundRemote;

            if (currentPath.tiers != null && currentPath.tiers.Count > 0 && currentPath.tiers[0] != null)
            {
                if (currentPath.tiers[0].previewPrefab != null) return currentPath.tiers[0].previewPrefab;
                if (currentPath.tiers[0].worldGameObject != null) return currentPath.tiers[0].worldGameObject;
            }
        }

        if (tier.previewPrefab != null) return tier.previewPrefab;
        if (tier.worldGameObject != null) return tier.worldGameObject;
        return null;
    }

    private void SpawnShop3DPreview(UpgradeTier tier)
    {
        if (shop3DPreviewInstance != null)
        {
            Destroy(shop3DPreviewInstance);
            shop3DPreviewInstance = null;
        }

        if (tier == null || shop3DPreviewAnchor == null) return;
        if (shop3DPreviewSpinner == null)
        {
            Transform existingSpinner = shop3DPreviewAnchor.Find("PreviewSpinner");
            if (existingSpinner != null) shop3DPreviewSpinner = existingSpinner;
            else
            {
                GameObject sp = new GameObject("PreviewSpinner");
                sp.transform.SetParent(shop3DPreviewAnchor, false);
                shop3DPreviewSpinner = sp.transform;
            }
        }

        GameObject sourceObj = ResolvePreviewSourceObject(tier);
        if (sourceObj == null) return;

        Position3DPreviewInViewport();
        shop3DPreviewSpinner.localPosition = Vector3.zero;
        shop3DPreviewSpinner.localRotation = Quaternion.identity;
        shop3DPreviewSpinner.localScale = Vector3.one;

        shop3DPreviewInstance = Instantiate(sourceObj, shop3DPreviewSpinner);
        shop3DPreviewInstance.SetActive(true);
        int uiLayer = fullscreenShopCanvas != null ? fullscreenShopCanvas.gameObject.layer : 5;
        SetLayerRecursively(shop3DPreviewInstance, uiLayer);

        // Strip any runtime/interaction components & particles from the 3D preview clone
        MonoBehaviour[] scripts = shop3DPreviewInstance.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < scripts.Length; i++)
        {
            if (scripts[i] != null) Destroy(scripts[i]);
        }
        Collider[] cols = shop3DPreviewInstance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null) Destroy(cols[i]);
        }
        ParticleSystem[] particles = shop3DPreviewInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] != null) Destroy(particles[i].gameObject);
        }

        // If sourceObj was the parent 'remote' container with multiple tier clones, keep only the first visual remote mesh
        if (currentPath != null && currentPath.category == UpgradeCategory.RemoteUpgrade && shop3DPreviewInstance.transform.childCount > 1)
        {
            MeshRenderer rootMr = shop3DPreviewInstance.GetComponent<MeshRenderer>();
            if (rootMr != null) rootMr.enabled = false;

            bool keptOneChild = false;
            for (int i = 0; i < shop3DPreviewInstance.transform.childCount; i++)
            {
                Transform ch = shop3DPreviewInstance.transform.GetChild(i);
                if (ch == null) continue;
                if (!keptOneChild && ch.GetComponentInChildren<MeshRenderer>(true) != null)
                {
                    ch.gameObject.SetActive(true);
                    keptOneChild = true;
                }
                else
                {
                    Destroy(ch.gameObject);
                }
            }
        }

        shop3DPreviewInstance.transform.localPosition = Vector3.zero;
        shop3DPreviewInstance.transform.localRotation = Quaternion.identity;
        shop3DPreviewInstance.transform.localScale = Vector3.one;

        // Collect only MeshRenderers and SkinnedMeshRenderers (excluding invisible root cube on 'remote')
        Renderer[] allRends = shop3DPreviewInstance.GetComponentsInChildren<Renderer>(true);
        List<Renderer> validMeshRends = new List<Renderer>();
        for (int i = 0; i < allRends.Length; i++)
        {
            Renderer r = allRends[i];
            if (r == null) continue;
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer))
            {
                r.enabled = false;
                continue;
            }
            if (r.gameObject == shop3DPreviewInstance && shop3DPreviewInstance.transform.childCount > 0 && !r.enabled)
            {
                continue;
            }
            r.gameObject.SetActive(true);
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            validMeshRends.Add(r);
        }

        if (validMeshRends.Count > 0)
        {
            Bounds combined = validMeshRends[0].bounds;
            for (int i = 1; i < validMeshRends.Count; i++) combined.Encapsulate(validMeshRends[i].bounds);

            // Offset the model inside shop3DPreviewSpinner so its geometric bounds center sits exactly at (0,0,0)!
            Vector3 localCenter = shop3DPreviewSpinner.InverseTransformPoint(combined.center);
            float maxDim = Mathf.Max(0.001f, Mathf.Max(combined.size.x, Mathf.Max(combined.size.y, combined.size.z)));
            float desiredWorldSize = 0.058f;
            float uniformScale = desiredWorldSize / maxDim;

            shop3DPreviewInstance.transform.localScale = Vector3.one * uniformScale;
            shop3DPreviewInstance.transform.localPosition = -localCenter * uniformScale;
        }

        // Determine 3D showcase tilt so flat discs, DVD players, and remotes all rotate visibly around the vertical Y axis
        if (currentPath != null && currentPath.category == UpgradeCategory.MoneyMultiplier)
        {
            currentBasePreviewRotation = new Vector3(58f, 0f, 24f);
        }
        else if (currentPath != null && currentPath.category == UpgradeCategory.SpeedMultiplier)
        {
            currentBasePreviewRotation = new Vector3(14f, 0f, 0f);
        }
        else
        {
            currentBasePreviewRotation = new Vector3(-28f, 0f, 15f);
        }

        shop3DPreviewSpinner.localRotation = Quaternion.Euler(0f, previewSpinAngle, 0f) * Quaternion.Euler(currentBasePreviewRotation);
    }

    private void Position3DPreviewInViewport()
    {
        if (shop3DPreviewAnchor == null || inspectorPreviewBoxRect == null || boundPlayerCamera == null) return;
        Vector3 boxCenterWorld = inspectorPreviewBoxRect.TransformPoint(inspectorPreviewBoxRect.rect.center);
        Vector3 toCam = (boundPlayerCamera.transform.position - boxCenterWorld).normalized;
        shop3DPreviewAnchor.position = boxCenterWorld + toCam * 0.045f;
        // Align anchor directly along the camera's sightline to the preview box center to eliminate the ~25-degree off-center frustum angle shift!
        shop3DPreviewAnchor.rotation = Quaternion.LookRotation(-toCam, boundPlayerCamera.transform.up);
    }

    private void RefreshFullscreenShopLiveStates()
    {
        if (currentPath == null || currentPath.tiers == null) return;

        decimal wallet = (UpgradeManager.Instance != null && UpgradeManager.Instance.bouncer != null)
            ? UpgradeManager.Instance.bouncer.currentMoney
            : 0m;

        if (shopWalletText != null)
        {
            shopWalletText.text = $"BALANCE: {CurrencyFormatter.Format(wallet)}";
        }

        if (shopExitBtn != null)
        {
            bool canExit = !TutorialManager.IsTutorialStarterShopActive ||
                           (TutorialManager.Instance != null && TutorialManager.Instance.AreBothStarterItemsPurchased);
            if (shopExitBtn.gameObject.activeSelf != canExit)
            {
                shopExitBtn.gameObject.SetActive(canExit);
            }
        }

        // Update all catalog grid cards
        for (int i = 0; i < catalogButtons.Count; i++)
        {
            ShopCatalogItemButton item = catalogButtons[i];
            if (item == null || item.tierIndex >= currentPath.tiers.Count) continue;

            UpgradeTier t = currentPath.tiers[item.tierIndex];
            bool unlocked = currentPath.unlockedIndices.Contains(item.tierIndex);
            bool equipped = currentPath.equippedIndex == item.tierIndex;
            bool selected = viewingIndex == item.tierIndex;
            decimal cost = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetTierCost(t) : (decimal)t.cost;
            bool affordable = wallet >= cost;

            if (equipped)
            {
                item.statusPriceLabel.text = "★ EQUIPPED";
                item.statusPriceLabel.color = new Color(0.35f, 0.95f, 1f, 1f);
                item.accentBarImage.color = new Color(0.35f, 0.95f, 1f, 1f);
            }
            else if (unlocked)
            {
                item.statusPriceLabel.text = "OWNED";
                item.statusPriceLabel.color = new Color(0.40f, 0.98f, 0.55f, 1f);
                item.accentBarImage.color = new Color(0.30f, 0.88f, 0.48f, 1f);
            }
            else
            {
                item.statusPriceLabel.text = CurrencyFormatter.Format(cost);
                item.statusPriceLabel.color = affordable
                    ? new Color(1f, 0.88f, 0.30f, 1f)
                    : new Color(0.95f, 0.35f, 0.35f, 0.9f);
                item.accentBarImage.color = affordable
                    ? new Color(1f, 0.82f, 0.25f, 1f)
                    : new Color(0.45f, 0.20f, 0.22f, 0.8f);
            }

            if (selected)
            {
                item.bgImage.color = new Color(0.16f, 0.28f, 0.42f, 1f);
                item.nameLabel.color = new Color(1f, 0.96f, 0.65f, 1f);
            }
            else
            {
                item.bgImage.color = equipped
                    ? new Color(0.09f, 0.19f, 0.26f, 0.98f)
                    : new Color(0.07f, 0.10f, 0.15f, 0.95f);
                item.nameLabel.color = Color.white;
            }
        }

        // Update Inspector Action Button
        if (viewingIndex >= 0 && viewingIndex < currentPath.tiers.Count)
        {
            UpgradeTier selTier = currentPath.tiers[viewingIndex];
            bool isUnlocked = currentPath.unlockedIndices.Contains(viewingIndex);
            bool isEquipped = currentPath.equippedIndex == viewingIndex;
            decimal selCost = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetTierCost(selTier) : (decimal)selTier.cost;
            bool canAfford = wallet >= selCost;

            if (inspectorPriceText != null)
            {
                if (isEquipped)
                {
                    inspectorPriceText.text = "STATUS: CURRENTLY ACTIVE";
                    inspectorPriceText.color = new Color(0.38f, 0.95f, 1f, 1f);
                }
                else if (isUnlocked)
                {
                    inspectorPriceText.text = "STATUS: OWNED — READY TO EQUIP";
                    inspectorPriceText.color = new Color(0.42f, 0.98f, 0.58f, 1f);
                }
                else
                {
                    inspectorPriceText.text = $"PRICE: {CurrencyFormatter.Format(selCost)}";
                    inspectorPriceText.color = canAfford
                        ? new Color(1f, 0.88f, 0.30f, 1f)
                        : new Color(1f, 0.35f, 0.35f, 1f);
                }
            }

            if (inspectorBuyEquipBtn != null && inspectorBuyEquipBtnImg != null && inspectorBuyEquipBtnLabel != null)
            {
                if (isEquipped)
                {
                    inspectorBuyEquipBtn.interactable = false;
                    inspectorBuyEquipBtnImg.color = new Color(0.18f, 0.34f, 0.42f, 0.9f);
                    inspectorBuyEquipBtnLabel.text = "★ EQUIPPED ★";
                }
                else if (isUnlocked)
                {
                    inspectorBuyEquipBtn.interactable = true;
                    inspectorBuyEquipBtnImg.color = new Color(0.18f, 0.72f, 0.92f, 1f);
                    inspectorBuyEquipBtnLabel.text = "EQUIP NOW";
                }
                else if (canAfford)
                {
                    inspectorBuyEquipBtn.interactable = true;
                    inspectorBuyEquipBtnImg.color = new Color(0.18f, 0.78f, 0.42f, 1f);
                    inspectorBuyEquipBtnLabel.text = $"BUY & EQUIP ({CurrencyFormatter.Format(selCost)})";
                }
                else
                {
                    inspectorBuyEquipBtn.interactable = false;
                    inspectorBuyEquipBtnImg.color = new Color(0.42f, 0.16f, 0.18f, 0.85f);
                    inspectorBuyEquipBtnLabel.text = $"INSUFFICIENT FUNDS ({CurrencyFormatter.Format(selCost)})";
                }
            }
        }
    }

    // =========================================================================
    // UI CONSTRUCTION HELPERS
    // =========================================================================

    private static GameObject GetOrCreateUIPanel(string name, Transform parent, Color bgColor, out bool createdNew)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            createdNew = false;
            if (existing.GetComponent<Image>() == null)
            {
                Image img = existing.gameObject.AddComponent<Image>();
                img.color = bgColor;
            }
            return existing.gameObject;
        }
        createdNew = true;
        return CreateUIPanel(name, parent, bgColor);
    }

    private static TextMeshProUGUI GetOrCreateUIText(
        string name,
        Transform parent,
        string content,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions align,
        Color color,
        TMP_FontAsset font,
        out bool createdNew)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            createdNew = false;
            TextMeshProUGUI tmp = existing.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = existing.gameObject.AddComponent<TextMeshProUGUI>();
            if (tmp.font == null && font != null) tmp.font = font;
            tmp.raycastTarget = false;
            return tmp;
        }
        createdNew = true;
        return CreateUIText(name, parent, content, fontSize, style, align, color, font);
    }

    private static Button GetOrCreateUIButton(
        string name,
        Transform parent,
        string label,
        float fontSize,
        Color bgColor,
        Color textColor,
        TMP_FontAsset font,
        out Image bgImg,
        out TextMeshProUGUI txtComp,
        out bool createdNew)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            createdNew = false;
            bgImg = existing.GetComponent<Image>() ?? existing.gameObject.AddComponent<Image>();
            Button btn = existing.GetComponent<Button>() ?? existing.gameObject.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            Transform labelT = existing.Find("Label");
            if (labelT != null)
            {
                txtComp = labelT.GetComponent<TextMeshProUGUI>() ?? labelT.gameObject.AddComponent<TextMeshProUGUI>();
                if (txtComp.font == null && font != null) txtComp.font = font;
            }
            else
            {
                txtComp = CreateUIText("Label", existing, label, fontSize, FontStyles.Bold, TextAlignmentOptions.Center, textColor, font);
                StretchFull(txtComp.GetComponent<RectTransform>());
            }
            return btn;
        }
        createdNew = true;
        return CreateUIButton(name, parent, label, fontSize, bgColor, textColor, font, out bgImg, out txtComp);
    }

    private static GameObject CreateUIPanel(string name, Transform parent, Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = bgColor;
        return go;
    }

    private static TextMeshProUGUI CreateUIText(
        string name,
        Transform parent,
        string content,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions align,
        Color color,
        TMP_FontAsset font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateUIButton(
        string name,
        Transform parent,
        string label,
        float fontSize,
        Color bgColor,
        Color textColor,
        TMP_FontAsset font,
        out Image bgImg,
        out TextMeshProUGUI txtComp)
    {
        GameObject go = CreateUIPanel(name, parent, bgColor);
        bgImg = go.GetComponent<Image>();
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = bgImg;

        txtComp = CreateUIText("Label", go.transform, label, fontSize, FontStyles.Bold, TextAlignmentOptions.Center, textColor, font);
        StretchFull(txtComp.GetComponent<RectTransform>());
        return btn;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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

    private void ToggleAllHitboxes(bool state)
    {
        if (buyInteractable != null && buyInteractable.GetComponent<Collider>() != null)
            buyInteractable.GetComponent<Collider>().enabled = state;
        if (nextInteractable != null && nextInteractable.GetComponent<Collider>() != null)
            nextInteractable.GetComponent<Collider>().enabled = state;
        if (prevInteractable != null && prevInteractable.GetComponent<Collider>() != null)
            prevInteractable.GetComponent<Collider>().enabled = state;
        if (closeInteractable != null && closeInteractable.GetComponent<Collider>() != null)
            closeInteractable.GetComponent<Collider>().enabled = state;
    }
}
