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

    public List<GameObject> objectsToDisable;
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
        transform.localScale = Vector3.zero;
        targetScale = Vector3.zero;
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

    public void OpenMenu(UpgradePath path)
    {
        if (isAnimatingAction) return;

        if (ActiveMenu != null && ActiveMenu != this) ActiveMenu.CloseMenu();
        ActiveMenu = this;

        currentPath = path;
        viewingIndex = path.equippedIndex;
        targetScale = originalScale;

        if (!IsMenuOpen)
        {
            IsMenuOpen = true;
            if (audioSource != null && openSound != null) audioSource.PlayOneShot(openSound);
            foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(false); }
        }

        if (closeInteractable != null) closeInteractable.GetComponent<Collider>().enabled = true;
        UpdateUI(true);
    }

    public void CloseMenu()
    {
        if (!IsMenuOpen || isAnimatingAction) return;
        if (ActiveMenu == this) ActiveMenu = null;

        targetScale = Vector3.zero;
        IsMenuOpen = false;

        if (audioSource != null && closeSound != null) audioSource.PlayOneShot(closeSound);
        foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(true); }

        ToggleAllHitboxes(false);
        if (currentPreviewModel != null) Destroy(currentPreviewModel, 0.2f);
    }

    public void CycleNext()
    {
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
        if (isAnimatingAction) return;
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
            if (UpgradeManager.Instance.TryPurchaseUpgrade(currentPath, viewingIndex))
            {
                if (audioSource != null && buySound != null) audioSource.PlayOneShot(buySound);
                RefreshButtonStates();
            }
        }
    }

    private void UpdateUI(bool spawnModel)
    {
        var tier = currentPath.tiers[viewingIndex];
        titleText.text = tier.tierName;
        statsText.text = $"Multiplier: x{tier.multiplierValue}";

        RefreshButtonStates();

        bool hasNext = viewingIndex < currentPath.tiers.Count - 1;
        bool hasPrev = viewingIndex > 0;

        if (nextInteractable != null) nextInteractable.GetComponent<Collider>().enabled = hasNext;
        if (prevInteractable != null) prevInteractable.GetComponent<Collider>().enabled = hasPrev;

        if (nextButtonImage != null) nextButtonImage.color = hasNext ? enabledCycleColor : disabledCycleColor;
        if (prevButtonImage != null) prevButtonImage.color = hasPrev ? enabledCycleColor : disabledCycleColor;

        if (spawnModel) SpawnPreviewModel(tier.previewPrefab, tier.previewScale, tier.previewRotation, tier.previewPositionOffset);
    }

    private void RefreshButtonStates()
    {
        var tier = currentPath.tiers[viewingIndex];
        bool isUnlocked = currentPath.unlockedIndices.Contains(viewingIndex);
        bool isEquipped = currentPath.equippedIndex == viewingIndex;
        bool canAfford = UpgradeManager.Instance.bouncer.currentMoney >= (decimal)tier.cost;

        if (buyInteractable != null) buyInteractable.GetComponent<Collider>().enabled = !isEquipped && (isUnlocked || canAfford);
        priceText.text = $"${tier.cost:0.00}";

        if (isEquipped)
        {
            buyButtonImage.color = equippedColor;
            if (buyButtonText != null) buyButtonText.text = "EQUIPPED";
        }
        else if (isUnlocked)
        {
            buyButtonImage.color = ownedColor;
            if (buyButtonText != null) buyButtonText.text = "EQUIP";
        }
        else if (canAfford)
        {
            buyButtonImage.color = canAffordColor;
            if (buyButtonText != null) buyButtonText.text = "BUY";
        }
        else
        {
            buyButtonImage.color = cannotAffordColor;
            if (buyButtonText != null) buyButtonText.text = "BUY";
        }
    }

    private GameObject SpawnPreviewModel(GameObject prefab, Vector3 scale, Vector3 rotation, Vector3 offset)
    {
        if (currentPreviewModel != null) Destroy(currentPreviewModel);
        if (prefab == null) return null;

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
        if (oldModel.GetComponent<FloatingPreview>() != null) Destroy(oldModel.GetComponent<FloatingPreview>());

        var oldTier = currentPath.tiers[oldIndex];
        var newTier = currentPath.tiers[newIndex];

        GameObject newModel = Instantiate(newTier.previewPrefab, previewAnchor);
        currentPreviewModel = newModel;

        var newFloat = newModel.GetComponent<FloatingPreview>();
        if (newFloat != null) Destroy(newFloat);

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

        oldModel.transform.localPosition = oldStartPos;
        oldModel.transform.localScale = oldStartScale;
        oldModel.transform.localRotation = Quaternion.Euler(oldTier.previewRotation);

        newModel.transform.localPosition = newStartPos;
        newModel.transform.localScale = Vector3.zero;
        newModel.transform.localRotation = Quaternion.Euler(newTier.previewRotation);

        float time = 0;
        while (time < slideAnimationDuration)
        {
            float t = time / slideAnimationDuration;
            float smoothT = t * t * (3f - 2f * t);

            oldModel.transform.localPosition = Vector3.Lerp(oldStartPos, oldEndPos, smoothT);
            oldModel.transform.localScale = Vector3.Lerp(oldStartScale, Vector3.zero, smoothT);

            newModel.transform.localPosition = Vector3.Lerp(newStartPos, newEndPos, smoothT);
            newModel.transform.localScale = Vector3.Lerp(Vector3.zero, newEndScale, smoothT);

            time += Time.deltaTime;
            yield return null;
        }

        newModel.transform.localPosition = newEndPos;
        newModel.transform.localScale = newEndScale;
        if (newModel.GetComponent<FloatingPreview>() == null) newModel.AddComponent<FloatingPreview>();

        Destroy(oldModel);
        isAnimatingAction = false;
        ToggleAllHitboxes(true);
        RefreshButtonStates();
    }

    private void ToggleAllHitboxes(bool state)
    {
        if (buyInteractable != null) buyInteractable.GetComponent<Collider>().enabled = state;
        if (nextInteractable != null) nextInteractable.GetComponent<Collider>().enabled = state;
        if (prevInteractable != null) prevInteractable.GetComponent<Collider>().enabled = state;
        if (closeInteractable != null) closeInteractable.GetComponent<Collider>().enabled = state;
    }

    private IEnumerator EquipAnimationRoutine()
    {
        isAnimatingAction = true;
        ToggleAllHitboxes(false);

        GameObject flyingModel = currentPreviewModel;
        currentPreviewModel = null;
        if (flyingModel.GetComponent<FloatingPreview>() != null) Destroy(flyingModel.GetComponent<FloatingPreview>());

        flyingModel.transform.SetParent(null);
        targetScale = Vector3.zero;
        IsMenuOpen = false;

        foreach (var obj in objectsToDisable) { if (obj != null) obj.SetActive(true); }
        if (ActiveMenu == this) ActiveMenu = null;

        Transform[] targets = currentPath.animationTargetPoints;
        if (targets != null && targets.Length > 0)
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

        Destroy(flyingModel);
        UpgradeManager.Instance.FinalizeEquip(currentPath, viewingIndex);
        isAnimatingAction = false;
    }
}