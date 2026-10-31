using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class WorldSpaceUpgradeMenu : MonoBehaviour
{
    public static WorldSpaceUpgradeMenu ActiveMenu { get; private set; }

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

    [Header("UI Visuals")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI statsText;
    public TextMeshProUGUI priceText;
    public TextMeshProUGUI buyButtonText;
    public Image buyButtonImage;
    public Image nextButtonImage;
    public Image prevButtonImage;

    [Header("Hitboxes")]
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

    private UpgradePath currentPath;
    private int viewingIndex = 0;
    private GameObject currentPreviewModel;

    private bool isAnimatingAction = false;
    private Vector3 originalScale;
    private Vector3 targetScale;

    private void Awake()
    {
        originalScale = transform.localScale;
        if (originalScale == Vector3.zero) originalScale = new Vector3(0.002f, 0.002f, 0.002f);
        transform.localScale = Vector3.zero;
        targetScale = Vector3.zero;

        EnsureUnifiedTabsExist();
        WireTabEvents();
        ApplyTitleOutline();
        ToggleAllHitboxes(false);
    }

    private void ApplyTitleOutline()
    {
        if (titleText != null)
        {
            Material mat = titleText.fontMaterial;
            if (mat != null)
            {
                mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.05f);

                mat.EnableKeyword("OUTLINE_ON");
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.28f);
                mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
                mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.02f);

                mat.EnableKeyword("UNDERLAY_ON");
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, Color.black);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.28f);
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.02f);

                titleText.UpdateMeshPadding();
            }
        }
    }

    /// <summary>
    /// Ensures the 3 top navigation tabs ([DVD], [PLAYER], [REMOTE]) and their 3D InteractableObject hitboxes
    /// exist on this unified upgrade canvas.
    /// </summary>
    public void EnsureUnifiedTabsExist()
    {
        // Only build tabs on the primary unified upgrade menu (DVDPLAYERMANAGER/Canvas)
        if (transform.parent != null && transform.parent.name == "DVDPILEMANAGER")
        {
            return;
        }

        GameObject templateBtnObj = null;
        if (closeInteractable != null && closeInteractable.transform.parent != null)
        {
            templateBtnObj = closeInteractable.transform.parent.gameObject;
        }
        else if (buyInteractable != null && buyInteractable.transform.parent != null)
        {
            templateBtnObj = buyInteractable.transform.parent.gameObject;
        }
        if (templateBtnObj == null) return;

        if (dvdTabButton == null)
        {
            Transform existing = transform.Find("TAB_DVD");
            if (existing != null)
            {
                dvdTabButton = existing.GetComponent<Button>();
                dvdTabInteractable = existing.GetComponentInChildren<InteractableObject>(true);
            }
            else
            {
                CreateTabFromTemplate(templateBtnObj, "TAB_DVD", "[DVD]", "DVD Upgrades", new Vector2(-235f, 275f), out dvdTabButton, out dvdTabInteractable);
            }
        }

        if (playerTabButton == null)
        {
            Transform existing = transform.Find("TAB_PLAYER");
            if (existing != null)
            {
                playerTabButton = existing.GetComponent<Button>();
                playerTabInteractable = existing.GetComponentInChildren<InteractableObject>(true);
            }
            else
            {
                CreateTabFromTemplate(templateBtnObj, "TAB_PLAYER", "[PLAYER]", "Player Upgrades", new Vector2(0f, 275f), out playerTabButton, out playerTabInteractable);
            }
        }

        if (remoteTabButton == null)
        {
            Transform existing = transform.Find("TAB_REMOTE");
            if (existing != null)
            {
                remoteTabButton = existing.GetComponent<Button>();
                remoteTabInteractable = existing.GetComponentInChildren<InteractableObject>(true);
            }
            else
            {
                CreateTabFromTemplate(templateBtnObj, "TAB_REMOTE", "[REMOTE]", "Remote Upgrades", new Vector2(235f, 275f), out remoteTabButton, out remoteTabInteractable);
            }
        }
    }

    private void CreateTabFromTemplate(
        GameObject templateObj,
        string objName,
        string labelText,
        string hoverLabel,
        Vector2 anchoredPos,
        out Button createdBtn,
        out InteractableObject createdInteractable)
    {
        GameObject tabObj = Instantiate(templateObj, transform);
        tabObj.name = objName;
        tabObj.SetActive(true);

        RectTransform rt = tabObj.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchoredPosition3D = new Vector3(anchoredPos.x, anchoredPos.y, 0f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = new Vector3(1.32f, 1.55f, 1.55f);
        }

        createdBtn = tabObj.GetComponent<Button>();
        if (createdBtn != null)
        {
            createdBtn.onClick.RemoveAllListeners();
        }

        TextMeshProUGUI tmp = tabObj.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            tmp.text = labelText;
            tmp.fontSize = 21f;
        }

        createdInteractable = tabObj.GetComponentInChildren<InteractableObject>(true);
        if (createdInteractable != null)
        {
            createdInteractable.hoverText = hoverLabel;
            createdInteractable.hideHoverText = false;
            createdInteractable.onClick = new UnityEngine.Events.UnityEvent();
        }
    }

    public void WireTabEvents()
    {
        if (dvdTabInteractable != null)
        {
            dvdTabInteractable.onClick.RemoveListener(SelectDvdTab);
            dvdTabInteractable.onClick.AddListener(SelectDvdTab);
        }
        if (playerTabInteractable != null)
        {
            playerTabInteractable.onClick.RemoveListener(SelectPlayerTab);
            playerTabInteractable.onClick.AddListener(SelectPlayerTab);
        }
        if (remoteTabInteractable != null)
        {
            remoteTabInteractable.onClick.RemoveListener(SelectRemoteTab);
            remoteTabInteractable.onClick.AddListener(SelectRemoteTab);
        }

        if (dvdTabButton != null)
        {
            dvdTabButton.onClick.RemoveListener(SelectDvdTab);
            dvdTabButton.onClick.AddListener(SelectDvdTab);
        }
        if (playerTabButton != null)
        {
            playerTabButton.onClick.RemoveListener(SelectPlayerTab);
            playerTabButton.onClick.AddListener(SelectPlayerTab);
        }
        if (remoteTabButton != null)
        {
            remoteTabButton.onClick.RemoveListener(SelectRemoteTab);
            remoteTabButton.onClick.AddListener(SelectRemoteTab);
        }
    }

    private void Update()
    {
        if (transform.localScale != targetScale)
        {
            float speed = IsMenuOpen ? openTransitionSpeed : closeTransitionSpeed;
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * speed);
        }

        if (IsMenuOpen && !isAnimatingAction && currentPath != null)
        {
            RefreshButtonStates();
        }
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

        EnsureUnifiedTabsExist();
        WireTabEvents();

        if (ActiveMenu != null && ActiveMenu != this) ActiveMenu.CloseMenu();
        ActiveMenu = this;

        currentPath = path;
        isPlayerMenu = (currentPath.category == UpgradeCategory.SpeedMultiplier);
        viewingIndex = Mathf.Clamp(path.equippedIndex, 0, Mathf.Max(0, path.tiers.Count - 1));
        targetScale = originalScale;

        if (!IsMenuOpen)
        {
            IsMenuOpen = true;
            if (audioSource != null && openSound != null) audioSource.PlayOneShot(openSound);
            if (objectsToDisable != null)
            {
                foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(false); }
            }
        }

        if (closeInteractable != null && closeInteractable.GetComponent<Collider>() != null)
            closeInteractable.GetComponent<Collider>().enabled = true;
        SetTabHitboxesEnabled(true);

        UpdateTabVisuals();
        UpdateUI(true);
    }

    public void SelectDvdTab()
    {
        SwitchToCategoryTab(UpgradeCategory.MoneyMultiplier);
    }

    public void SelectPlayerTab()
    {
        SwitchToCategoryTab(UpgradeCategory.SpeedMultiplier);
    }

    public void SelectRemoteTab()
    {
        SwitchToCategoryTab(UpgradeCategory.RemoteUpgrade);
    }

    public void SwitchToCategoryTab(UpgradeCategory category)
    {
        if (isAnimatingAction || UpgradeManager.Instance == null) return;

        UpgradeManager.Instance.EnsureRemoteUpgradePath();
        UpgradePath targetPath = UpgradeManager.Instance.GetPathByCategory(category);
        if (targetPath == null || targetPath == currentPath) return;

        if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);

        currentPath = targetPath;
        isPlayerMenu = (currentPath.category == UpgradeCategory.SpeedMultiplier);
        viewingIndex = Mathf.Clamp(currentPath.equippedIndex, 0, Mathf.Max(0, currentPath.tiers.Count - 1));

        UpdateTabVisuals();
        UpdateUI(true);
    }

    private void UpdateTabVisuals()
    {
        UpgradeCategory activeCat = currentPath != null ? currentPath.category : UpgradeCategory.SpeedMultiplier;
        SetTabButtonHighlight(dvdTabButton, activeCat == UpgradeCategory.MoneyMultiplier);
        SetTabButtonHighlight(playerTabButton, activeCat == UpgradeCategory.SpeedMultiplier);
        SetTabButtonHighlight(remoteTabButton, activeCat == UpgradeCategory.RemoteUpgrade);
    }

    private void SetTabButtonHighlight(Button btn, bool isActiveTab)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.color = isActiveTab ? activeTabColor : inactiveTabColor;
        }
        TextMeshProUGUI txt = btn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (txt != null)
        {
            txt.color = isActiveTab ? new Color(0.1f, 0.1f, 0.1f, 1f) : new Color(0.22f, 0.22f, 0.22f, 0.9f);
        }
    }

    public void CloseMenu()
    {
        if (!IsMenuOpen || isAnimatingAction) return;
        if (ActiveMenu == this) ActiveMenu = null;

        targetScale = Vector3.zero;
        IsMenuOpen = false;

        if (audioSource != null && closeSound != null) audioSource.PlayOneShot(closeSound);
        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(true); }
        }

        ToggleAllHitboxes(false);
        if (currentPreviewModel != null) Destroy(currentPreviewModel, 0.2f);
    }

    public void CycleNext()
    {
        if (currentPath == null) return;
        if (viewingIndex < currentPath.tiers.Count - 1 && !isAnimatingAction)
        {
            if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);
            StartCoroutine(SlidePreviewRoutine(viewingIndex, viewingIndex + 1, true));
            viewingIndex++;
            UpdateUI(false);
        }
    }

    public void CyclePrev()
    {
        if (currentPath == null) return;
        if (viewingIndex > 0 && !isAnimatingAction)
        {
            if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);
            StartCoroutine(SlidePreviewRoutine(viewingIndex, viewingIndex - 1, false));
            viewingIndex--;
            UpdateUI(false);
        }
    }

    public void OnBuyClicked()
    {
        if (isAnimatingAction || currentPath == null) return;
        bool isUnlocked = currentPath.unlockedIndices.Contains(viewingIndex);

        if (isUnlocked)
        {
            if (currentPath.equippedIndex != viewingIndex)
            {
                if (audioSource != null && equipSound != null) audioSource.PlayOneShot(equipSound);
                StartCoroutine(EquipAnimationRoutine());
            }
        }
        else
        {
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.TryPurchaseUpgrade(currentPath, viewingIndex))
            {
                if (audioSource != null && buySound != null) audioSource.PlayOneShot(buySound);
                RefreshButtonStates();
            }
        }
    }

    private void UpdateUI(bool spawnModel)
    {
        if (currentPath == null || currentPath.tiers == null || viewingIndex < 0 || viewingIndex >= currentPath.tiers.Count) return;

        var tier = currentPath.tiers[viewingIndex];
        if (titleText != null) titleText.text = tier.tierName;

        if (statsText != null)
        {
            if (currentPath.category == UpgradeCategory.MoneyMultiplier)
            {
                decimal baseEdge = (UpgradeManager.Instance != null && UpgradeManager.Instance.bouncer != null)
                    ? UpgradeManager.Instance.bouncer.edgeReward
                    : 0.01m;
                decimal bounceMoney = baseEdge * (decimal)tier.multiplierValue;
                statsText.text = $"${bounceMoney:0.00} / Bounce";
            }
            else if (currentPath.category == UpgradeCategory.SpeedMultiplier)
            {
                statsText.text = $"Speed: x{tier.multiplierValue:0.00}";
            }
            else if (currentPath.category == UpgradeCategory.RemoteUpgrade)
            {
                if (!string.IsNullOrEmpty(tier.customDescription))
                {
                    statsText.text = tier.customDescription;
                }
                else
                {
                    statsText.text = viewingIndex == 0
                        ? "Direct Impulse Hit + Bonus Cash"
                        : (viewingIndex == 1
                            ? "Mitosis Split (2x Child Logos + Recombine)"
                            : "Click Screen: Spawn up to 5 Bumpers");
                }
            }
        }

        RefreshButtonStates();

        bool hasNext = viewingIndex < currentPath.tiers.Count - 1;
        bool hasPrev = viewingIndex > 0;

        if (nextInteractable != null && nextInteractable.GetComponent<Collider>() != null)
            nextInteractable.GetComponent<Collider>().enabled = hasNext;
        if (prevInteractable != null && prevInteractable.GetComponent<Collider>() != null)
            prevInteractable.GetComponent<Collider>().enabled = hasPrev;

        if (nextButtonImage != null) nextButtonImage.color = hasNext ? enabledCycleColor : disabledCycleColor;
        if (prevButtonImage != null) prevButtonImage.color = hasPrev ? enabledCycleColor : disabledCycleColor;

        if (spawnModel) SpawnPreviewModel(tier.previewPrefab, tier.previewScale, tier.previewRotation, tier.previewPositionOffset);
    }

    private void RefreshButtonStates()
    {
        if (currentPath == null || currentPath.tiers == null || viewingIndex < 0 || viewingIndex >= currentPath.tiers.Count) return;

        var tier = currentPath.tiers[viewingIndex];
        bool isUnlocked = currentPath.unlockedIndices.Contains(viewingIndex);
        bool isEquipped = currentPath.equippedIndex == viewingIndex;
        decimal tierCost = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetTierCost(tier) : (decimal)tier.cost;
        bool canAfford = UpgradeManager.Instance != null && UpgradeManager.Instance.bouncer != null && UpgradeManager.Instance.bouncer.currentMoney >= tierCost;

        if (buyInteractable != null && buyInteractable.GetComponent<Collider>() != null)
            buyInteractable.GetComponent<Collider>().enabled = !isEquipped && (isUnlocked || canAfford);
        if (priceText != null) priceText.text = $"${tierCost:0.00}";

        if (isEquipped)
        {
            if (buyButtonImage != null) buyButtonImage.color = equippedColor;
            if (buyButtonText != null) buyButtonText.text = "EQUIPPED";
        }
        else if (isUnlocked)
        {
            if (buyButtonImage != null) buyButtonImage.color = ownedColor;
            if (buyButtonText != null) buyButtonText.text = "EQUIP";
        }
        else if (canAfford)
        {
            if (buyButtonImage != null) buyButtonImage.color = canAffordColor;
            if (buyButtonText != null) buyButtonText.text = "BUY";
        }
        else
        {
            if (buyButtonImage != null) buyButtonImage.color = cannotAffordColor;
            if (buyButtonText != null) buyButtonText.text = "BUY";
        }
    }

    private GameObject SpawnPreviewModel(GameObject prefab, Vector3 scale, Vector3 rotation, Vector3 offset)
    {
        if (currentPreviewModel != null) Destroy(currentPreviewModel);
        if (prefab == null || previewAnchor == null) return null;

        currentPreviewModel = Instantiate(prefab, previewAnchor);
        currentPreviewModel.transform.localPosition = offset;
        currentPreviewModel.transform.localScale = scale;
        currentPreviewModel.transform.localRotation = Quaternion.Euler(rotation);

        if (currentPreviewModel.GetComponent<FloatingPreview>() == null) currentPreviewModel.AddComponent<FloatingPreview>();
        return currentPreviewModel;
    }

    private IEnumerator SlidePreviewRoutine(int oldIndex, int newIndex, bool movingRight)
    {
        isAnimatingAction = true;
        ToggleAllHitboxes(false);

        GameObject oldModel = currentPreviewModel;
        if (oldModel != null && oldModel.GetComponent<FloatingPreview>() != null) Destroy(oldModel.GetComponent<FloatingPreview>());

        var oldTier = currentPath.tiers[oldIndex];
        var newTier = currentPath.tiers[newIndex];

        GameObject newModel = newTier.previewPrefab != null && previewAnchor != null
            ? Instantiate(newTier.previewPrefab, previewAnchor)
            : null;
        currentPreviewModel = newModel;

        if (newModel != null)
        {
            var newFloat = newModel.GetComponent<FloatingPreview>();
            if (newFloat != null) Destroy(newFloat);
        }

        Vector3 oldStartPos = oldTier.previewPositionOffset;
        Vector3 newEndPos = newTier.previewPositionOffset;

        Vector3 slideVector = slideAxis.normalized * slideDistance;
        if (!movingRight) slideVector = -slideVector;

        Vector3 oldEndPos = oldStartPos - slideVector;
        Vector3 newStartPos = newEndPos + slideVector;

        if (isPlayerMenu)
        {
            oldEndPos += playerSlideOffset;
            newStartPos += playerSlideOffset;
        }

        Vector3 oldStartScale = oldTier.previewScale;
        Vector3 newEndScale = newTier.previewScale;

        if (oldModel != null)
        {
            oldModel.transform.localPosition = oldStartPos;
            oldModel.transform.localScale = oldStartScale;
            oldModel.transform.localRotation = Quaternion.Euler(oldTier.previewRotation);
        }

        if (newModel != null)
        {
            newModel.transform.localPosition = newStartPos;
            newModel.transform.localScale = Vector3.zero;
            newModel.transform.localRotation = Quaternion.Euler(newTier.previewRotation);
        }

        float time = 0;
        while (time < slideAnimationDuration)
        {
            float t = time / slideAnimationDuration;
            float smoothT = t * t * (3f - 2f * t);

            if (oldModel != null)
            {
                oldModel.transform.localPosition = Vector3.Lerp(oldStartPos, oldEndPos, smoothT);
                oldModel.transform.localScale = Vector3.Lerp(oldStartScale, Vector3.zero, smoothT);
            }

            if (newModel != null)
            {
                newModel.transform.localPosition = Vector3.Lerp(newStartPos, newEndPos, smoothT);
                newModel.transform.localScale = Vector3.Lerp(Vector3.zero, newEndScale, smoothT);
            }

            time += Time.deltaTime;
            yield return null;
        }

        if (newModel != null)
        {
            newModel.transform.localPosition = newEndPos;
            newModel.transform.localScale = newEndScale;
            if (newModel.GetComponent<FloatingPreview>() == null) newModel.AddComponent<FloatingPreview>();
        }

        if (oldModel != null) Destroy(oldModel);
        isAnimatingAction = false;
        ToggleAllHitboxes(true);
        RefreshButtonStates();
    }

    private void SetTabHitboxesEnabled(bool state)
    {
        if (dvdTabInteractable != null && dvdTabInteractable.GetComponent<Collider>() != null)
            dvdTabInteractable.GetComponent<Collider>().enabled = state;
        if (playerTabInteractable != null && playerTabInteractable.GetComponent<Collider>() != null)
            playerTabInteractable.GetComponent<Collider>().enabled = state;
        if (remoteTabInteractable != null && remoteTabInteractable.GetComponent<Collider>() != null)
            remoteTabInteractable.GetComponent<Collider>().enabled = state;
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
        SetTabHitboxesEnabled(state);
    }

    private IEnumerator EquipAnimationRoutine()
    {
        isAnimatingAction = true;
        ToggleAllHitboxes(false);

        GameObject flyingModel = currentPreviewModel;
        currentPreviewModel = null;
        if (flyingModel != null && flyingModel.GetComponent<FloatingPreview>() != null)
            Destroy(flyingModel.GetComponent<FloatingPreview>());

        if (flyingModel != null)
            flyingModel.transform.SetParent(null);
        targetScale = Vector3.zero;
        IsMenuOpen = false;

        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(true); }
        }
        if (ActiveMenu == this) ActiveMenu = null;

        Transform[] targets = currentPath.animationTargetPoints;
        if (flyingModel != null && targets != null && targets.Length > 0)
        {
            float timePerStage = animationDuration / targets.Length;
            Vector3 currentStartPos = flyingModel.transform.position;
            Quaternion currentStartRot = flyingModel.transform.rotation;
            Vector3 currentStartScale = flyingModel.transform.localScale;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;

                Vector3 targetPos = targets[i].position;
                Quaternion targetRot = targets[i].rotation;
                Vector3 targetScale = targets[i].lossyScale;

                float time = 0;
                while (time < timePerStage)
                {
                    float t = time / timePerStage;
                    t = t * t * (3f - 2f * t);
                    flyingModel.transform.position = Vector3.Lerp(currentStartPos, targetPos, t);
                    flyingModel.transform.rotation = Quaternion.Slerp(currentStartRot, targetRot, t);
                    flyingModel.transform.localScale = Vector3.Lerp(currentStartScale, targetScale, t);
                    time += Time.deltaTime;
                    yield return null;
                }
                currentStartPos = targetPos;
                currentStartRot = targetRot;
                currentStartScale = targetScale;
            }
        }

        if (flyingModel != null) Destroy(flyingModel);
        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.FinalizeEquip(currentPath, viewingIndex);
        isAnimatingAction = false;
    }
}
