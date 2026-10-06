using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class WorldSpaceUpgradeMenu : MonoBehaviour
{
    // Enforces mutual exclusivity globally
    public static WorldSpaceUpgradeMenu ActiveMenu { get; private set; }

    [Header("Menu Animation")]
    public float transitionSpeed = 10f;

    [Header("UI Visuals")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI statsText;
    public TextMeshProUGUI priceText;
    public Image buyButtonImage;

    [Header("Hitboxes (InteractableObjects)")]
    public InteractableObject buyInteractable;
    public InteractableObject nextInteractable;
    public InteractableObject prevInteractable;
    public InteractableObject closeInteractable;

    [Header("Interaction Blocking")]
    [Tooltip("GameObjects to disable while this menu is open.")]
    public List<GameObject> objectsToDisable;
    public bool IsMenuOpen { get; private set; } = false;

    [Header("Preview Settings")]
    public Transform previewAnchor;
    public float animationDuration = 0.8f;

    [Header("Colors")]
    public Color canAffordColor = Color.white;
    public Color cannotAffordColor = Color.red;
    public Color purchasedColor = Color.gray;

    private UpgradePath currentPath;
    private int viewingIndex = 0;
    private GameObject currentPreviewModel;

    private bool isAnimatingPurchase = false;
    private Vector3 originalScale;
    private Vector3 targetScale;

    private void Awake()
    {
        originalScale = transform.localScale;
        transform.localScale = Vector3.zero;
        targetScale = Vector3.zero;
    }

    private void Update()
    {
        if (transform.localScale != targetScale)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * transitionSpeed);
        }
    }

    public void OpenMenu(UpgradePath path)
    {
        if (isAnimatingPurchase) return;

        // Force close any other open menu
        if (ActiveMenu != null && ActiveMenu != this) ActiveMenu.CloseMenu();
        ActiveMenu = this;

        currentPath = path;
        viewingIndex = Mathf.Min(path.CurrentLevel + 1, path.tiers.Count - 1);
        targetScale = originalScale;

        if (!IsMenuOpen)
        {
            IsMenuOpen = true;
            foreach (var obj in objectsToDisable)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        if (closeInteractable != null) closeInteractable.GetComponent<Collider>().enabled = true;
        UpdateUI();
    }

    public void CloseMenu()
    {
        if (!IsMenuOpen || isAnimatingPurchase) return;

        if (ActiveMenu == this) ActiveMenu = null;

        targetScale = Vector3.zero;
        IsMenuOpen = false;

        foreach (var obj in objectsToDisable)
        {
            if (obj != null) obj.SetActive(true);
        }

        // Disable hitboxes so they can't be clicked while shrinking
        ToggleAllHitboxes(false);
        StartCoroutine(DestroyPreviewAfterDelay());
    }

    private IEnumerator DestroyPreviewAfterDelay()
    {
        yield return new WaitForSeconds(0.3f);
        if (!IsMenuOpen && currentPreviewModel != null)
        {
            Destroy(currentPreviewModel);
        }
    }

    public void CycleNext()
    {
        if (viewingIndex < currentPath.tiers.Count - 1 && !isAnimatingPurchase)
        {
            viewingIndex++;
            UpdateUI();
        }
    }

    public void CyclePrev()
    {
        if (viewingIndex > 0 && !isAnimatingPurchase)
        {
            viewingIndex--;
            UpdateUI();
        }
    }

    public void OnBuyClicked()
    {
        if (isAnimatingPurchase) return;

        if (UpgradeManager.Instance.TrySpendForUpgrade(currentPath, viewingIndex))
        {
            StartCoroutine(PurchaseAnimationRoutine());
        }
    }

    private void UpdateUI()
    {
        var tier = currentPath.tiers[viewingIndex];

        titleText.text = tier.tierName;
        statsText.text = $"Multiplier: x{tier.multiplierValue}";
        priceText.text = $"${tier.cost:0.00}";

        // Toggle UI collider states based on logic (replaces button.interactable)
        if (nextInteractable != null)
            nextInteractable.GetComponent<Collider>().enabled = viewingIndex < currentPath.tiers.Count - 1;

        if (prevInteractable != null)
            prevInteractable.GetComponent<Collider>().enabled = viewingIndex > 0;

        bool isPurchased = viewingIndex <= currentPath.CurrentLevel;
        bool canAfford = UpgradeManager.Instance.bouncer.currentMoney >= (decimal)tier.cost;

        if (buyInteractable != null)
            buyInteractable.GetComponent<Collider>().enabled = !isPurchased && canAfford;

        if (isPurchased)
        {
            buyButtonImage.color = purchasedColor;
            priceText.text = "OWNED";
        }
        else if (canAfford) buyButtonImage.color = canAffordColor;
        else buyButtonImage.color = cannotAffordColor;

        SpawnPreviewModel(tier.previewPrefab);
    }

    private void SpawnPreviewModel(GameObject prefab)
    {
        if (currentPreviewModel != null) Destroy(currentPreviewModel);
        if (prefab == null) return;

        currentPreviewModel = Instantiate(prefab, previewAnchor);
        currentPreviewModel.transform.localPosition = Vector3.zero;

        if (currentPreviewModel.GetComponent<FloatingPreview>() == null)
            currentPreviewModel.AddComponent<FloatingPreview>();
    }

    private void ToggleAllHitboxes(bool state)
    {
        if (buyInteractable != null) buyInteractable.GetComponent<Collider>().enabled = state;
        if (nextInteractable != null) nextInteractable.GetComponent<Collider>().enabled = state;
        if (prevInteractable != null) prevInteractable.GetComponent<Collider>().enabled = state;
        if (closeInteractable != null) closeInteractable.GetComponent<Collider>().enabled = state;
    }

    private IEnumerator PurchaseAnimationRoutine()
    {
        isAnimatingPurchase = true;
        ToggleAllHitboxes(false);

        GameObject flyingModel = currentPreviewModel;
        currentPreviewModel = null;

        var floatScript = flyingModel.GetComponent<FloatingPreview>();
        if (floatScript != null) Destroy(floatScript);

        flyingModel.transform.SetParent(null);
        Vector3 startPos = flyingModel.transform.position;
        Quaternion startRot = flyingModel.transform.rotation;

        Transform animTarget = currentPath.animationTargetPoint;
        Vector3 endPos = animTarget != null ? animTarget.position : startPos;
        Quaternion endRot = animTarget != null ? animTarget.rotation : startRot;

        float time = 0;
        while (time < animationDuration)
        {
            float t = time / animationDuration;
            t = t * t * (3f - 2f * t);

            flyingModel.transform.position = Vector3.Lerp(startPos, endPos, t);
            flyingModel.transform.rotation = Quaternion.Slerp(startRot, endRot, t);
            flyingModel.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.8f, t);

            time += Time.deltaTime;
            yield return null;
        }

        Destroy(flyingModel);

        UpgradeManager.Instance.FinalizeUpgrade(currentPath, viewingIndex);

        isAnimatingPurchase = false;
        if (closeInteractable != null) closeInteractable.GetComponent<Collider>().enabled = true;

        if (viewingIndex < currentPath.tiers.Count - 1) CycleNext();
        else UpdateUI();
    }
}