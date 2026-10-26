using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

public class RouletteTableVisuals : MonoBehaviour
{
    [Header("Wheel Anchors")]
    public Transform wheelRotorPivot;
    public Transform ballOrbitPivot;

    [Header("3D Text Displays")]
    public TMP_Text statusBannerText;
    public TMP_Text actionPromptText;
    public TMP_Text balanceText;
    public TMP_Text totalBetText;
    public TMP_Text wheelResultText;
    public TMP_Text selectedChipText;
    public TMP_FontAsset optionalFontAsset;

    [Header("Base Material Template")]
    public Material baseCubeMaterial;

    [Header("Wheel & Spin Settings")]
    public float spinDuration = 3.2f;
    public float wheelOuterPocketRadius = 0.145f;
    public float ballRimRadius = 0.182f;
    public float ballPocketRadius = 0.132f;

    [Header("Pocket Colors")]
    public Color greenPocketColor = new Color(0.10f, 0.62f, 0.26f, 1f);
    public Color redPocketColor = new Color(0.84f, 0.15f, 0.18f, 1f);
    public Color blackPocketColor = new Color(0.14f, 0.15f, 0.19f, 1f);
    public Color goldAccentColor = new Color(0.88f, 0.70f, 0.20f, 1f);
    public Color ballColor = new Color(0.96f, 0.96f, 0.98f, 1f);

    private Transform ballCubeTransform;
    private GameObject winningDollyMarker;
    private readonly List<GameObject> spawnedBetChipObjects = new List<GameObject>();
    private Material runtimeSharedMat;
    private MaterialPropertyBlock propBlock;
    private bool wheelPocketsBuilt;
    private float currentRotorAngle;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        propBlock = new MaterialPropertyBlock();
        if (optionalFontAsset == null && statusBannerText != null && statusBannerText.font != null)
        {
            optionalFontAsset = statusBannerText.font;
        }
        EnsureRuntimeMaterial();
        BuildWheelPocketCubesIfNeeded();
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

    private static void DestroySafe(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    public void BuildWheelPocketCubesIfNeeded()
    {
        if (wheelPocketsBuilt || wheelRotorPivot == null) return;
        wheelPocketsBuilt = true;
        EnsureRuntimeMaterial();

        // Clear any pre-existing children on wheelRotorPivot / ballOrbitPivot before building fresh
        for (int i = wheelRotorPivot.childCount - 1; i >= 0; i--)
        {
            DestroySafe(wheelRotorPivot.GetChild(i).gameObject);
        }
        if (ballOrbitPivot != null && ballOrbitPivot != wheelRotorPivot)
        {
            for (int i = ballOrbitPivot.childCount - 1; i >= 0; i--)
            {
                DestroySafe(ballOrbitPivot.GetChild(i).gameObject);
            }
        }

        int count = RouletteLogicEngine.WheelPocketSequence.Length; // 37 pockets
        float stepDeg = 360f / count;

        for (int i = 0; i < count; i++)
        {
            int number = RouletteLogicEngine.WheelPocketSequence[i];
            float angleDeg = i * stepDeg;
            float angleRad = angleDeg * Mathf.Deg2Rad;

            GameObject pocketCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pocketCube.name = $"PocketCube_{number}";
            Collider col = pocketCube.GetComponent<Collider>();
            if (col != null) DestroySafe(col);

            pocketCube.transform.SetParent(wheelRotorPivot, false);
            Vector3 radialDir = new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));
            pocketCube.transform.localPosition = radialDir * wheelOuterPocketRadius + new Vector3(0f, 0.012f, 0f);
            pocketCube.transform.localRotation = Quaternion.Euler(0f, angleDeg, 0f);
            pocketCube.transform.localScale = new Vector3(0.021f, 0.014f, 0.046f);

            RoulettePocketColor pCol = RouletteLogicEngine.GetPocketColor(number);
            Color c = pCol == RoulettePocketColor.Green
                ? greenPocketColor
                : (pCol == RoulettePocketColor.Red ? redPocketColor : blackPocketColor);
            ApplyColorToRenderer(pocketCube.GetComponent<MeshRenderer>(), c, c * 0.12f);
        }

        // Central turret spindle cubes on the rotor
        GameObject turretBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
        turretBase.name = "WheelTurretBaseCube";
        if (turretBase.GetComponent<Collider>() != null) DestroySafe(turretBase.GetComponent<Collider>());
        turretBase.transform.SetParent(wheelRotorPivot, false);
        turretBase.transform.localPosition = new Vector3(0f, 0.025f, 0f);
        turretBase.transform.localScale = new Vector3(0.075f, 0.036f, 0.075f);
        ApplyColorToRenderer(turretBase.GetComponent<MeshRenderer>(), goldAccentColor, goldAccentColor * 0.15f);

        GameObject turretCrossA = GameObject.CreatePrimitive(PrimitiveType.Cube);
        turretCrossA.name = "WheelTurretArmA";
        if (turretCrossA.GetComponent<Collider>() != null) DestroySafe(turretCrossA.GetComponent<Collider>());
        turretCrossA.transform.SetParent(wheelRotorPivot, false);
        turretCrossA.transform.localPosition = new Vector3(0f, 0.048f, 0f);
        turretCrossA.transform.localScale = new Vector3(0.12f, 0.012f, 0.018f);
        ApplyColorToRenderer(turretCrossA.GetComponent<MeshRenderer>(), goldAccentColor, goldAccentColor * 0.15f);

        GameObject turretCrossB = GameObject.CreatePrimitive(PrimitiveType.Cube);
        turretCrossB.name = "WheelTurretArmB";
        if (turretCrossB.GetComponent<Collider>() != null) DestroySafe(turretCrossB.GetComponent<Collider>());
        turretCrossB.transform.SetParent(wheelRotorPivot, false);
        turretCrossB.transform.localPosition = new Vector3(0f, 0.048f, 0f);
        turretCrossB.transform.localScale = new Vector3(0.018f, 0.012f, 0.12f);
        ApplyColorToRenderer(turretCrossB.GetComponent<MeshRenderer>(), goldAccentColor, goldAccentColor * 0.15f);

        // Create the 3D Roulette Ball Cube
        Transform ballParent = ballOrbitPivot != null ? ballOrbitPivot : wheelRotorPivot;
        GameObject ballObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ballObj.name = "RouletteBallCube";
        if (ballObj.GetComponent<Collider>() != null) DestroySafe(ballObj.GetComponent<Collider>());
        ballObj.transform.SetParent(ballParent, false);
        ballObj.transform.localScale = new Vector3(0.020f, 0.020f, 0.020f);
        ballObj.transform.localPosition = new Vector3(0f, 0.026f, ballPocketRadius);
        ApplyColorToRenderer(ballObj.GetComponent<MeshRenderer>(), ballColor, ballColor * 0.35f);
        ballCubeTransform = ballObj.transform;
    }

    public IEnumerator SpinWheelAnimatedRoutine(int winningNumber)
    {
        BuildWheelPocketCubesIfNeeded();
        HideWinningDollyMarker();

        int pocketIndex = RouletteLogicEngine.GetWheelIndexForNumber(winningNumber);
        float stepDeg = 360f / RouletteLogicEngine.WheelPocketSequence.Length;
        float pocketLocalAngle = pocketIndex * stepDeg;

        float startRotorAngle = currentRotorAngle;
        // Spin wheel clockwise 4 full turns + offset
        float targetRotorAngle = startRotorAngle + 360f * 4f + Random.Range(25f, 335f);

        // The winning pocket's final world-relative angle on the wheel is:
        float finalPocketAngle = Mathf.Repeat(targetRotorAngle + pocketLocalAngle, 360f);
        // Spin the ball counter-clockwise 6 full turns to land exactly at finalPocketAngle
        float startBallAngle = finalPocketAngle + 360f * 6f;

        float duration = Mathf.Max(0.5f, spinDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Cubic ease-out for natural wheel & ball deceleration
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);

            currentRotorAngle = Mathf.Lerp(startRotorAngle, targetRotorAngle, easeOut);
            if (wheelRotorPivot != null)
            {
                wheelRotorPivot.localRotation = Quaternion.Euler(0f, currentRotorAngle, 0f);
            }

            if (ballCubeTransform != null)
            {
                float currentBallAngle = Mathf.Lerp(startBallAngle, finalPocketAngle, easeOut);
                // Ball stays on outer rim for first 55% of spin, then drops into the pocket ring with a small bounce
                float dropT = Mathf.Clamp01((t - 0.55f) / 0.45f);
                float radius = Mathf.Lerp(ballRimRadius, ballPocketRadius, dropT * dropT);
                float bounceY = 0f;
                if (t > 0.55f && t < 0.92f)
                {
                    bounceY = Mathf.Abs(Mathf.Sin((t - 0.55f) * 18f)) * 0.016f * (1f - dropT);
                }

                float rad = currentBallAngle * Mathf.Deg2Rad;
                Vector3 localPos = new Vector3(Mathf.Sin(rad) * radius, 0.025f + bounceY, Mathf.Cos(rad) * radius);

                if (ballOrbitPivot != null)
                {
                    ballCubeTransform.localPosition = localPos;
                    ballCubeTransform.localRotation = Quaternion.Euler(elapsed * 540f, currentBallAngle, 0f);
                }
            }

            yield return null;
        }

        currentRotorAngle = Mathf.Repeat(targetRotorAngle, 360f);
        if (wheelRotorPivot != null)
        {
            wheelRotorPivot.localRotation = Quaternion.Euler(0f, currentRotorAngle, 0f);
        }

        if (ballCubeTransform != null)
        {
            float rad = finalPocketAngle * Mathf.Deg2Rad;
            ballCubeTransform.localPosition = new Vector3(Mathf.Sin(rad) * ballPocketRadius, 0.024f, Mathf.Cos(rad) * ballPocketRadius);
        }
    }

    public void ClearBoardChipStacks()
    {
        for (int i = 0; i < spawnedBetChipObjects.Count; i++)
        {
            if (spawnedBetChipObjects[i] != null)
            {
                Destroy(spawnedBetChipObjects[i]);
            }
        }
        spawnedBetChipObjects.Clear();
    }

    public void HideWinningDollyMarker()
    {
        if (winningDollyMarker != null)
        {
            winningDollyMarker.SetActive(false);
        }
    }

    public void ShowWinningDollyOnSpot(Transform winningSpotTransform)
    {
        if (winningSpotTransform == null) return;
        EnsureRuntimeMaterial();

        if (winningDollyMarker == null)
        {
            winningDollyMarker = new GameObject("WinningDollyMarker");
            winningDollyMarker.transform.SetParent(transform, false);

            GameObject baseCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (baseCube.GetComponent<Collider>() != null) Destroy(baseCube.GetComponent<Collider>());
            baseCube.transform.SetParent(winningDollyMarker.transform, false);
            baseCube.transform.localPosition = new Vector3(0f, 0.018f, 0f);
            baseCube.transform.localScale = new Vector3(0.028f, 0.032f, 0.028f);
            ApplyColorToRenderer(baseCube.GetComponent<MeshRenderer>(), goldAccentColor, goldAccentColor * 0.55f);

            GameObject topCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (topCube.GetComponent<Collider>() != null) Destroy(topCube.GetComponent<Collider>());
            topCube.transform.SetParent(winningDollyMarker.transform, false);
            topCube.transform.localPosition = new Vector3(0f, 0.042f, 0f);
            topCube.transform.localScale = new Vector3(0.018f, 0.018f, 0.018f);
            ApplyColorToRenderer(topCube.GetComponent<MeshRenderer>(), Color.white, goldAccentColor * 0.65f);
        }

        winningDollyMarker.SetActive(true);
        Vector3 localSpotPos = transform.InverseTransformPoint(winningSpotTransform.position);
        winningDollyMarker.transform.localPosition = localSpotPos + new Vector3(0f, 0.010f, 0f);
    }

    public void RefreshBoardChipStacks(
        IReadOnlyDictionary<RouletteBetKey, decimal> activeBets,
        Dictionary<RouletteBetKey, RouletteInteractiveElement> boardSpotLookup)
    {
        ClearBoardChipStacks();
        if (activeBets == null || activeBets.Count == 0 || boardSpotLookup == null) return;

        EnsureRuntimeMaterial();

        foreach (KeyValuePair<RouletteBetKey, decimal> kvp in activeBets)
        {
            if (kvp.Value <= 0m) continue;
            if (!boardSpotLookup.TryGetValue(kvp.Key, out RouletteInteractiveElement spotElem) || spotElem == null)
            {
                continue;
            }

            Vector3 spotLocalPos = transform.InverseTransformPoint(spotElem.transform.position);
            // Offset chip stack slightly toward lower-right of the bet spot so number text remains visible
            Vector3 stackBasePos = spotLocalPos + new Vector3(0.014f, 0.010f, -0.012f);

            decimal remaining = kvp.Value;
            int[] denoms = { 50, 25, 10, 5, 1 };
            int spawned = 0;
            const int maxChipsPerSpot = 6;

            for (int d = 0; d < denoms.Length && spawned < maxChipsPerSpot; d++)
            {
                int denom = denoms[d];
                while (remaining >= denom && spawned < maxChipsPerSpot)
                {
                    remaining -= denom;
                    SpawnChipCubeOnSpot(stackBasePos, denom, spawned);
                    spawned++;
                }
            }

            if (spawned == 0)
            {
                SpawnChipCubeOnSpot(stackBasePos, 1, 0);
                spawned = 1;
            }

            // Floating 3D text badge on top of the chip stack showing exact dollar amount on this spot
            GameObject badgeObj = new GameObject($"BetBadge_{kvp.Key.GetDisplayLabel()}");
            badgeObj.transform.SetParent(transform, false);
            badgeObj.transform.localPosition = stackBasePos + new Vector3(0f, 0.008f + spawned * 0.0065f, 0f);
            badgeObj.transform.localRotation = Quaternion.Euler(75f, 0f, 0f);
            TextMeshPro tmp = badgeObj.AddComponent<TextMeshPro>();
            if (optionalFontAsset != null) tmp.font = optionalFontAsset;
            tmp.text = $"${kvp.Value:0}";
            tmp.fontSize = 0.16f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = new Color(1f, 0.96f, 0.55f, 1f);
            tmp.rectTransform.sizeDelta = new Vector2(0.08f, 0.04f);
            spawnedBetChipObjects.Add(badgeObj);
        }
    }

    private void SpawnChipCubeOnSpot(Vector3 baseLocalPos, int denom, int stackIdx)
    {
        GameObject chip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chip.name = $"SpotChip_${denom}_{stackIdx}";
        if (chip.GetComponent<Collider>() != null) Destroy(chip.GetComponent<Collider>());
        chip.transform.SetParent(transform, false);
        chip.transform.localPosition = baseLocalPos + new Vector3(0f, stackIdx * 0.006f, 0f);
        chip.transform.localRotation = Quaternion.Euler(0f, stackIdx * 25f, 0f);
        chip.transform.localScale = new Vector3(0.028f, 0.0055f, 0.028f);

        Color col = BlackjackTableVisuals.GetChipDenominationColor(denom);
        ApplyColorToRenderer(chip.GetComponent<MeshRenderer>(), col, col * 0.20f);
        spawnedBetChipObjects.Add(chip);
    }

    private void ApplyColorToRenderer(MeshRenderer renderer, Color color, Color emission)
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
        propBlock.SetColor(EmissionColorId, emission);
        renderer.SetPropertyBlock(propBlock);
    }

    public void RefreshTextDisplays(
        RouletteLogicEngine engine,
        decimal balance,
        decimal selectedChip,
        string statusBanner,
        string actionPrompt)
    {
        if (balanceText != null)
        {
            balanceText.text = $"BALANCE\n${balance:0.00}";
        }

        if (totalBetText != null)
        {
            decimal shownBet = engine.State == RouletteGameState.Spinning ? engine.LastCommittedBet : engine.TotalStagedBet;
            totalBetText.text = $"TOTAL BET\n${shownBet:0.00}";
        }

        if (selectedChipText != null)
        {
            selectedChipText.text = $"CHIP: ${selectedChip:0}";
        }

        if (statusBannerText != null && !string.IsNullOrEmpty(statusBanner))
        {
            statusBannerText.text = statusBanner;
        }

        if (actionPromptText != null && !string.IsNullOrEmpty(actionPrompt))
        {
            actionPromptText.text = actionPrompt;
        }

        if (wheelResultText != null)
        {
            if (engine.LastWinningNumber < 0)
            {
                wheelResultText.text = "LAST SPIN: --";
            }
            else
            {
                RoulettePocketColor col = RouletteLogicEngine.GetPocketColor(engine.LastWinningNumber);
                string colName = col == RoulettePocketColor.Green ? "GREEN" : (col == RoulettePocketColor.Red ? "RED" : "BLACK");
                StringBuilder hist = new StringBuilder();
                int count = Mathf.Min(5, engine.RecentWinningNumbers.Count);
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) hist.Append(", ");
                    hist.Append(engine.RecentWinningNumbers[i]);
                }
                wheelResultText.text = $"RESULT: {engine.LastWinningNumber} ({colName})\nHISTORY: {hist}";
            }
        }
    }
}
