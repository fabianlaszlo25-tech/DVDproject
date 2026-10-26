using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class BlackjackTableVisuals : MonoBehaviour
{
    [Header("Card Anchors")]
    public Transform deckShoeAnchor;
    public Transform dealerCardsAnchor;
    public Transform playerCardsAnchor;
    public Transform betStackAnchor;

    [Header("3D Text Displays")]
    public TMP_Text statusBannerText;
    public TMP_Text actionPromptText;
    public TMP_Text balanceText;
    public TMP_Text currentBetText;
    public TMP_Text dealerScoreText;
    public TMP_Text playerScoreText;
    public TMP_FontAsset optionalFontAsset;

    [Header("Base Material Template")]
    [Tooltip("Optional URP Lit material template cloned for dynamic cards and chips.")]
    public Material baseCubeMaterial;

    [Header("Card Layout & Animation")]
    public Vector3 cardCubeScale = new Vector3(0.115f, 0.008f, 0.165f);
    public float cardHorizontalSpacing = 0.132f;
    public float cardDealDuration = 0.24f;
    public float cardDealArcHeight = 0.08f;
    public float cardFlipDuration = 0.26f;

    [Header("Card & Suit Colors")]
    public Color cardFrontColor = new Color(0.96f, 0.95f, 0.90f, 1f);
    public Color cardBackColor = new Color(0.14f, 0.22f, 0.48f, 1f);
    public Color heartsColor = new Color(0.84f, 0.14f, 0.20f, 1f);
    public Color diamondsColor = new Color(0.90f, 0.34f, 0.12f, 1f);
    public Color spadesColor = new Color(0.12f, 0.15f, 0.22f, 1f);
    public Color clubsColor = new Color(0.10f, 0.34f, 0.20f, 1f);

    private class SpawnedCardVisual
    {
        public GameObject rootObject;
        public MeshRenderer cardBodyRenderer;
        public MeshRenderer suitStripeRenderer;
        public MeshRenderer suitPipRenderer;
        public TMP_Text topCornerText;
        public TMP_Text centerSuitText;
        public BlackjackCard cardData;
        public bool isFaceDown;
    }

    private readonly List<SpawnedCardVisual> playerCardVisuals = new List<SpawnedCardVisual>();
    private readonly List<SpawnedCardVisual> dealerCardVisuals = new List<SpawnedCardVisual>();
    private readonly List<GameObject> spawnedPotChips = new List<GameObject>();
    private Material runtimeSharedMat;
    private MaterialPropertyBlock propBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        propBlock = new MaterialPropertyBlock();
        if (optionalFontAsset == null && statusBannerText != null && statusBannerText.font != null)
        {
            optionalFontAsset = statusBannerText.font;
        }
        EnsureReadableTextDisplayTransforms();
        EnsureRuntimeMaterial();
    }

    public void EnsureReadableTextDisplayTransforms()
    {
        ConfigureTableText(statusBannerText, new Vector3(0f, 1.112f, 0.412f), new Vector3(12f, 0f, 0f), 0.45f, new Vector2(1.24f, 0.14f));
        ConfigureTableText(actionPromptText, new Vector3(0f, 1.028f, 0.416f), new Vector3(12f, 0f, 0f), 0.31f, new Vector2(1.24f, 0.12f));
        ConfigureTableText(balanceText, new Vector3(-0.52f, 0.905f, 0.20f), new Vector3(50f, 8f, 0f), 0.34f, new Vector2(0.44f, 0.18f));
        ConfigureTableText(currentBetText, new Vector3(-0.52f, 0.855f, -0.215f), new Vector3(62f, 0f, 0f), 0.33f, new Vector2(0.40f, 0.16f));
        ConfigureTableText(dealerScoreText, new Vector3(0f, 0.826f, 0.33f), new Vector3(72f, 0f, 0f), 0.34f, new Vector2(0.82f, 0.12f));
        ConfigureTableText(playerScoreText, new Vector3(0f, 0.826f, -0.235f), new Vector3(72f, 0f, 0f), 0.34f, new Vector2(0.82f, 0.12f));
    }

    private void ConfigureTableText(TMP_Text tmp, Vector3 localPos, Vector3 eulerDeg, float fontSize, Vector2 rectSize)
    {
        if (tmp == null) return;
        tmp.transform.localScale = Vector3.one;
        tmp.transform.localPosition = localPos;
        tmp.transform.localRotation = Quaternion.Euler(eulerDeg);
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        RectTransform rt = tmp.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.sizeDelta = rectSize;
        }
    }

    private void EnsureRuntimeMaterial()
    {
        if (runtimeSharedMat != null) return;

        if (baseCubeMaterial != null)
        {
            runtimeSharedMat = new Material(baseCubeMaterial);
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Standard");

        if (shader != null)
        {
            runtimeSharedMat = new Material(shader);
        }
    }

    public void ClearTableCards()
    {
        for (int i = 0; i < playerCardVisuals.Count; i++)
        {
            if (playerCardVisuals[i] != null && playerCardVisuals[i].rootObject != null)
            {
                Destroy(playerCardVisuals[i].rootObject);
            }
        }
        playerCardVisuals.Clear();

        for (int i = 0; i < dealerCardVisuals.Count; i++)
        {
            if (dealerCardVisuals[i] != null && dealerCardVisuals[i].rootObject != null)
            {
                Destroy(dealerCardVisuals[i].rootObject);
            }
        }
        dealerCardVisuals.Clear();
    }

    public void UpdateBetPotChips(decimal totalBet)
    {
        for (int i = 0; i < spawnedPotChips.Count; i++)
        {
            if (spawnedPotChips[i] != null)
            {
                Destroy(spawnedPotChips[i]);
            }
        }
        spawnedPotChips.Clear();

        if (betStackAnchor == null || totalBet <= 0m) return;

        EnsureRuntimeMaterial();

        decimal remaining = totalBet;
        int[] denominations = { 50, 25, 10, 5, 1 };
        int maxVisualChips = 18;
        int spawnedCount = 0;

        for (int d = 0; d < denominations.Length && spawnedCount < maxVisualChips; d++)
        {
            int denom = denominations[d];
            while (remaining >= denom && spawnedCount < maxVisualChips)
            {
                remaining -= denom;
                SpawnSinglePotChipCube(denom, spawnedCount);
                spawnedCount++;
            }
        }

        if (spawnedCount == 0 && totalBet > 0m)
        {
            SpawnSinglePotChipCube(1, 0);
        }
    }

    private void SpawnSinglePotChipCube(int denomination, int stackIndex)
    {
        GameObject chipCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chipCube.name = $"PotChip_${denomination}_{stackIndex}";
        Collider col = chipCube.GetComponent<Collider>();
        if (col != null) Destroy(col);

        chipCube.transform.SetParent(betStackAnchor, false);
        float jitterX = ((stackIndex % 3) - 1) * 0.004f;
        float jitterZ = (((stackIndex / 3) % 3) - 1) * 0.004f;
        chipCube.transform.localPosition = new Vector3(jitterX, 0.008f + stackIndex * 0.014f, jitterZ);
        chipCube.transform.localRotation = Quaternion.Euler(0f, stackIndex * 22f, 0f);
        chipCube.transform.localScale = new Vector3(0.082f, 0.013f, 0.082f);

        MeshRenderer mr = chipCube.GetComponent<MeshRenderer>();
        ApplyColorToRenderer(mr, GetChipDenominationColor(denomination));
        spawnedPotChips.Add(chipCube);
    }

    public static Color GetChipDenominationColor(int denomination)
    {
        switch (denomination)
        {
            case 1: return new Color(0.88f, 0.90f, 0.95f, 1f);
            case 5: return new Color(0.85f, 0.16f, 0.18f, 1f);
            case 10: return new Color(0.16f, 0.42f, 0.88f, 1f);
            case 25: return new Color(0.14f, 0.68f, 0.32f, 1f);
            case 50: return new Color(0.92f, 0.72f, 0.15f, 1f);
            default: return new Color(0.80f, 0.65f, 0.20f, 1f);
        }
    }

    public IEnumerator DealCardAnimatedRoutine(BlackjackCard card, bool toDealer, bool faceDown)
    {
        Transform targetAnchor = toDealer ? dealerCardsAnchor : playerCardsAnchor;
        if (targetAnchor == null) yield break;

        List<SpawnedCardVisual> handList = toDealer ? dealerCardVisuals : playerCardVisuals;
        SpawnedCardVisual cardVis = CreateCardCubeHierarchy(card, faceDown, targetAnchor, handList.Count);
        handList.Add(cardVis);

        int count = handList.Count;
        Vector3[] targetLocalPositions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float offsetX = (i - (count - 1) * 0.5f) * cardHorizontalSpacing;
            float offsetY = i * 0.0015f; // tiny step to prevent z-fighting
            targetLocalPositions[i] = new Vector3(offsetX, offsetY, 0f);
        }

        Vector3 startWorldPos = deckShoeAnchor != null
            ? deckShoeAnchor.position
            : targetAnchor.TransformPoint(new Vector3(0.45f, 0.12f, 0.25f));
        Quaternion startWorldRot = deckShoeAnchor != null
            ? deckShoeAnchor.rotation
            : targetAnchor.rotation;

        Vector3 startLocalPos = targetAnchor.InverseTransformPoint(startWorldPos);
        Quaternion startLocalRot = Quaternion.Inverse(targetAnchor.rotation) * startWorldRot;
        Quaternion endLocalRot = Quaternion.Euler(0f, Random.Range(-2.5f, 2.5f), 0f);

        Vector3[] existingStartPositions = new Vector3[count - 1];
        for (int i = 0; i < count - 1; i++)
        {
            if (handList[i].rootObject != null)
            {
                existingStartPositions[i] = handList[i].rootObject.transform.localPosition;
            }
        }

        cardVis.rootObject.transform.localPosition = startLocalPos;
        cardVis.rootObject.transform.localRotation = startLocalRot;

        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, cardDealDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = t * t * (3f - 2f * t);

            // Shift existing cards smoothly to re-center hand
            for (int i = 0; i < count - 1; i++)
            {
                if (handList[i].rootObject != null)
                {
                    handList[i].rootObject.transform.localPosition = Vector3.Lerp(existingStartPositions[i], targetLocalPositions[i], smoothT);
                }
            }

            if (cardVis.rootObject != null)
            {
                Vector3 pos = Vector3.Lerp(startLocalPos, targetLocalPositions[count - 1], smoothT);
                pos.y += Mathf.Sin(t * Mathf.PI) * cardDealArcHeight;
                cardVis.rootObject.transform.localPosition = pos;
                cardVis.rootObject.transform.localRotation = Quaternion.Slerp(startLocalRot, endLocalRot, smoothT);
            }

            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            if (handList[i].rootObject != null)
            {
                handList[i].rootObject.transform.localPosition = targetLocalPositions[i];
            }
        }
        if (cardVis.rootObject != null)
        {
            cardVis.rootObject.transform.localRotation = endLocalRot;
        }
    }

    public IEnumerator RevealDealerHoleCardRoutine()
    {
        SpawnedCardVisual hiddenCard = null;
        for (int i = 0; i < dealerCardVisuals.Count; i++)
        {
            if (dealerCardVisuals[i] != null && dealerCardVisuals[i].isFaceDown)
            {
                hiddenCard = dealerCardVisuals[i];
                break;
            }
        }

        if (hiddenCard == null || hiddenCard.rootObject == null)
        {
            yield break;
        }

        Transform cardTransform = hiddenCard.rootObject.transform;
        Vector3 basePos = cardTransform.localPosition;
        Quaternion baseRot = cardTransform.localRotation;
        Quaternion midRot = baseRot * Quaternion.Euler(0f, 0f, 90f);

        float halfDuration = Mathf.Max(0.05f, cardFlipDuration * 0.5f);
        float elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            cardTransform.localPosition = basePos + new Vector3(0f, Mathf.Lerp(0f, 0.05f, t), 0f);
            cardTransform.localRotation = Quaternion.Slerp(baseRot, midRot, t);
            yield return null;
        }

        hiddenCard.isFaceDown = false;
        ConfigureCardVisualFace(hiddenCard, hiddenCard.cardData, false);

        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            cardTransform.localPosition = basePos + new Vector3(0f, Mathf.Lerp(0.05f, 0f, t), 0f);
            cardTransform.localRotation = Quaternion.Slerp(midRot, baseRot, t);
            yield return null;
        }

        cardTransform.localPosition = basePos;
        cardTransform.localRotation = baseRot;
    }

    private SpawnedCardVisual CreateCardCubeHierarchy(BlackjackCard card, bool faceDown, Transform parentAnchor, int cardIndex)
    {
        EnsureRuntimeMaterial();

        GameObject cardRoot = new GameObject($"Card_{cardIndex}_{card.GetRankSymbol()}_{card.GetSuitShortCode()}");
        cardRoot.transform.SetParent(parentAnchor, false);

        // 1. Main thin rectangular card cube
        GameObject bodyCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bodyCube.name = "CardBodyCube";
        Collider bodyCol = bodyCube.GetComponent<Collider>();
        if (bodyCol != null) Destroy(bodyCol);
        bodyCube.transform.SetParent(cardRoot.transform, false);
        bodyCube.transform.localPosition = Vector3.zero;
        bodyCube.transform.localRotation = Quaternion.identity;
        bodyCube.transform.localScale = cardCubeScale;
        MeshRenderer bodyRenderer = bodyCube.GetComponent<MeshRenderer>();

        // 2. Suit header stripe cube on top of card
        GameObject stripeCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stripeCube.name = "CardSuitStripeCube";
        Collider stripeCol = stripeCube.GetComponent<Collider>();
        if (stripeCol != null) Destroy(stripeCol);
        stripeCube.transform.SetParent(cardRoot.transform, false);
        stripeCube.transform.localPosition = new Vector3(0f, cardCubeScale.y * 0.55f, -cardCubeScale.z * 0.30f);
        stripeCube.transform.localRotation = Quaternion.identity;
        stripeCube.transform.localScale = new Vector3(cardCubeScale.x * 0.86f, 0.003f, cardCubeScale.z * 0.16f);
        MeshRenderer stripeRenderer = stripeCube.GetComponent<MeshRenderer>();

        // 3. Center suit emblem cube (rotated 45 deg for Diamonds/Hearts)
        GameObject pipCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pipCube.name = "CardSuitPipCube";
        Collider pipCol = pipCube.GetComponent<Collider>();
        if (pipCol != null) Destroy(pipCol);
        pipCube.transform.SetParent(cardRoot.transform, false);
        pipCube.transform.localPosition = new Vector3(0.024f, cardCubeScale.y * 0.60f, 0.015f);
        pipCube.transform.localRotation = Quaternion.Euler(0f, card.IsRedSuit ? 45f : 0f, 0f);
        pipCube.transform.localScale = new Vector3(0.024f, 0.004f, 0.024f);
        MeshRenderer pipRenderer = pipCube.GetComponent<MeshRenderer>();

        // 4. 3D TextMeshPro for Rank value
        GameObject rankTextObj = new GameObject("CardRankText");
        rankTextObj.transform.SetParent(cardRoot.transform, false);
        rankTextObj.transform.localPosition = new Vector3(-0.010f, cardCubeScale.y * 0.65f, 0.022f);
        rankTextObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        TextMeshPro rankTmp = rankTextObj.AddComponent<TextMeshPro>();
        if (optionalFontAsset != null) rankTmp.font = optionalFontAsset;
        rankTmp.alignment = TextAlignmentOptions.Center;
        rankTmp.fontSize = 0.66f;
        rankTmp.fontStyle = FontStyles.Bold;
        rankTmp.textWrappingMode = TextWrappingModes.NoWrap;
        rankTmp.rectTransform.sizeDelta = new Vector2(0.115f, 0.095f);

        // 5. 3D TextMeshPro for Suit label on stripe
        GameObject suitTextObj = new GameObject("CardSuitText");
        suitTextObj.transform.SetParent(cardRoot.transform, false);
        suitTextObj.transform.localPosition = new Vector3(0f, cardCubeScale.y * 0.80f, -cardCubeScale.z * 0.30f);
        suitTextObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        TextMeshPro suitTmp = suitTextObj.AddComponent<TextMeshPro>();
        if (optionalFontAsset != null) suitTmp.font = optionalFontAsset;
        suitTmp.alignment = TextAlignmentOptions.Center;
        suitTmp.fontSize = 0.21f;
        suitTmp.fontStyle = FontStyles.Bold;
        suitTmp.textWrappingMode = TextWrappingModes.NoWrap;
        suitTmp.rectTransform.sizeDelta = new Vector2(0.115f, 0.045f);

        SpawnedCardVisual visual = new SpawnedCardVisual
        {
            rootObject = cardRoot,
            cardBodyRenderer = bodyRenderer,
            suitStripeRenderer = stripeRenderer,
            suitPipRenderer = pipRenderer,
            topCornerText = rankTmp,
            centerSuitText = suitTmp,
            cardData = card,
            isFaceDown = faceDown
        };

        ConfigureCardVisualFace(visual, card, faceDown);
        return visual;
    }

    private void ConfigureCardVisualFace(SpawnedCardVisual visual, BlackjackCard card, bool faceDown)
    {
        if (visual == null) return;

        if (faceDown)
        {
            ApplyColorToRenderer(visual.cardBodyRenderer, cardBackColor);
            ApplyColorToRenderer(visual.suitStripeRenderer, new Color(0.85f, 0.68f, 0.22f, 1f));
            if (visual.suitPipRenderer != null) visual.suitPipRenderer.gameObject.SetActive(false);

            if (visual.topCornerText != null)
            {
                visual.topCornerText.text = "?";
                visual.topCornerText.color = new Color(0.95f, 0.82f, 0.35f, 1f);
            }
            if (visual.centerSuitText != null)
            {
                visual.centerSuitText.text = "HIDDEN";
                visual.centerSuitText.color = Color.white;
            }
        }
        else
        {
            Color suitCol = GetSuitColor(card.Suit);
            ApplyColorToRenderer(visual.cardBodyRenderer, cardFrontColor);
            ApplyColorToRenderer(visual.suitStripeRenderer, suitCol);
            if (visual.suitPipRenderer != null)
            {
                visual.suitPipRenderer.gameObject.SetActive(true);
                ApplyColorToRenderer(visual.suitPipRenderer, suitCol);
            }

            if (visual.topCornerText != null)
            {
                visual.topCornerText.text = card.GetRankSymbol();
                visual.topCornerText.color = suitCol;
            }
            if (visual.centerSuitText != null)
            {
                visual.centerSuitText.text = card.GetSuitName();
                visual.centerSuitText.color = Color.white;
            }
        }
    }

    public Color GetSuitColor(CardSuit suit)
    {
        switch (suit)
        {
            case CardSuit.Hearts: return heartsColor;
            case CardSuit.Diamonds: return diamondsColor;
            case CardSuit.Clubs: return clubsColor;
            case CardSuit.Spades: return spadesColor;
            default: return spadesColor;
        }
    }

    private void ApplyColorToRenderer(MeshRenderer renderer, Color color)
    {
        if (renderer == null) return;
        EnsureRuntimeMaterial();

        if (runtimeSharedMat != null)
        {
            renderer.sharedMaterial = runtimeSharedMat;
        }

        if (propBlock == null) propBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(BaseColorId, color);
        propBlock.SetColor(LegacyColorId, color);
        renderer.SetPropertyBlock(propBlock);
    }

    public void RefreshTextDisplays(
        BlackjackLogicEngine engine,
        decimal balance,
        string statusMessage,
        string actionPrompt)
    {
        if (balanceText != null)
        {
            balanceText.text = $"BALANCE\n${balance:0.00}";
        }

        if (currentBetText != null)
        {
            currentBetText.text = $"BET\n${engine.CurrentBet:0.00}";
        }

        if (statusBannerText != null && !string.IsNullOrEmpty(statusMessage))
        {
            statusBannerText.text = statusMessage;
        }

        if (actionPromptText != null && !string.IsNullOrEmpty(actionPrompt))
        {
            actionPromptText.text = actionPrompt;
        }

        if (dealerScoreText != null)
        {
            if (engine.DealerHand.Count == 0)
            {
                dealerScoreText.text = "DEALER: --";
            }
            else if (!engine.IsDealerHoleCardRevealed)
            {
                int firstVal = engine.GetVisibleDealerScore();
                dealerScoreText.text = $"DEALER: {firstVal} + ?";
            }
            else
            {
                int dealerVal = engine.DealerHand.CalculateValue();
                string suffix = dealerVal > 21 ? " (BUST)" : (engine.DealerHand.IsNaturalBlackjack ? " (BJ)" : "");
                dealerScoreText.text = $"DEALER: {dealerVal}{suffix}";
            }
        }

        if (playerScoreText != null)
        {
            if (engine.PlayerHand.Count == 0)
            {
                playerScoreText.text = "PLAYER: --";
            }
            else
            {
                int playerVal = engine.PlayerHand.CalculateValue(out bool isSoft);
                string suffix = playerVal > 21
                    ? " (BUST)"
                    : (engine.PlayerHand.IsNaturalBlackjack ? " (BLACKJACK!)" : (isSoft && playerVal < 21 ? " (SOFT)" : ""));
                playerScoreText.text = $"PLAYER: {playerVal}{suffix}";
            }
        }
    }
}
