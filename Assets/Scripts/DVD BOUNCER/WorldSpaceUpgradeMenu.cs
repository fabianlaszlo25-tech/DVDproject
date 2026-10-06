using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class WorldSpaceUpgradeMenu : MonoBehaviour
{
    public static WorldSpaceUpgradeMenu Instance { get; private set; }

    [Header("Menu Positioning")]
    public Vector3 menuOffset = new Vector3(0, 2f, 0);
    public float transitionSpeed = 5f;

    [Header("UI Elements")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI statsText;
    public TextMeshProUGUI priceText;
    public Button buyButton;
    public Image buyButtonImage;
    public Button nextButton;
    public Button prevButton;
    public Button closeButton;

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

    private bool isOpen = false;
    private bool isAnimatingPurchase = false;
    private Vector3 targetPosition;
    private Vector3 targetScale = Vector3.zero;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        transform.localScale = Vector3.zero;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (isOpen)
        {
            // Smoothly track the position and scale
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * transitionSpeed);
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * transitionSpeed);
        }
    }

    public void OpenMenu(UpgradePath path, Transform sourceObject)
    {
        if (isAnimatingPurchase) return; // Prevent opening while a purchase is animating

        currentPath = path;
        viewingIndex = Mathf.Min(path.CurrentLevel + 1, path.tiers.Count - 1);

        targetPosition = sourceObject.position + menuOffset;
        targetScale = Vector3.one;

        if (!isOpen)
        {
            transform.position = sourceObject.position; // Start from inside the object
            gameObject.SetActive(true);
            isOpen = true;
        }

        UpdateUI();
    }

    public void CloseMenu()
    {
        if (!isOpen || isAnimatingPurchase) return;
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        targetScale = Vector3.zero;
        isOpen = false;

        // Wait for scale to approximate 0
        yield return new WaitForSeconds(0.3f);

        if (currentPreviewModel != null) Destroy(currentPreviewModel);
        gameObject.SetActive(false);
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

        nextButton.interactable = viewingIndex < currentPath.tiers.Count - 1;
        prevButton.interactable = viewingIndex > 0;

        bool isPurchased = viewingIndex <= currentPath.CurrentLevel;
        bool canAfford = UpgradeManager.Instance.bouncer.currentMoney >= (decimal)tier.cost; //[cite: 2]

        buyButton.interactable = !isPurchased && canAfford;

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

    private IEnumerator PurchaseAnimationRoutine()
    {
        isAnimatingPurchase = true;

        // Disable UI interaction during animation
        buyButton.interactable = false;
        nextButton.interactable = false;
        prevButton.interactable = false;
        closeButton.interactable = false;

        GameObject flyingModel = currentPreviewModel;
        currentPreviewModel = null; // Detach from menu

        // Stop floating script
        var floatScript = flyingModel.GetComponent<FloatingPreview>();
        if (floatScript != null) Destroy(floatScript);

        // Detach from canvas and fly to target
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
            // Use a smooth curve (Ease In-Out)
            t = t * t * (3f - 2f * t);

            flyingModel.transform.position = Vector3.Lerp(startPos, endPos, t);
            flyingModel.transform.rotation = Quaternion.Slerp(startRot, endRot, t);

            // Optional: Shrink slightly as it reaches the destination
            flyingModel.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.8f, t);

            time += Time.deltaTime;
            yield return null;
        }

        Destroy(flyingModel);

        // Apply the actual upgrade (enables the stack/player visuals)
        UpgradeManager.Instance.FinalizeUpgrade(currentPath, viewingIndex);

        isAnimatingPurchase = false;
        closeButton.interactable = true;

        // Auto-cycle to the next upgrade or refresh UI
        if (viewingIndex < currentPath.tiers.Count - 1) CycleNext();
        else UpdateUI();
    }
}