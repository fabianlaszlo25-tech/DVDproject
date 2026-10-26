using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Automatically ensures both the 3D Blackjack table (BlackjackTableSetup) and the 3D Roulette table
/// (RouletteTableSetup) are properly constructed, positioned, and configured with readable uniform-scale
/// 3D TextMeshPro labels and gentle TableCameraZoomController framing in BarGameBarni.
/// Runs automatically in the Unity Editor on script reload and at runtime after scene load.
/// </summary>
#if UNITY_EDITOR
[InitializeOnLoad]
#endif
public static class CasinoSceneAutoBuilder
{
    private const int CurrentSetupVersion = 4;

#if UNITY_EDITOR
    static CasinoSceneAutoBuilder()
    {
        EditorApplication.delayCall += OnEditorDelayCall;
    }

    private static void OnEditorDelayCall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnsureCasinoTablesInActiveScene(saveInEditor: true);
    }

    [MenuItem("Tools/Casino/Setup Blackjack & Roulette Tables in Scene")]
    public static void MenuSetupCasinoTables()
    {
        EnsureCasinoTablesInActiveScene(saveInEditor: true);
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnRuntimeSceneLoaded()
    {
        EnsureCasinoTablesInActiveScene(saveInEditor: false);
    }

    public static void EnsureCasinoTablesInActiveScene(bool saveInEditor)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded) return;

        // Only run in BarGameBarni (or any scene containing BlackjackTableSetup)
        GameObject bjRoot = GameObject.Find("BlackjackTableSetup");
        if (bjRoot == null && activeScene.name != "BarGameBarni") return;

        bool sceneModified = false;

        if (bjRoot != null)
        {
            if (UpgradeBlackjackTableSetup(bjRoot))
            {
                sceneModified = true;
            }
        }

        GameObject rlRoot = GameObject.Find("RouletteTableSetup");
        if (rlRoot == null)
        {
            rlRoot = BuildRouletteTableSetup(bjRoot);
            if (rlRoot != null)
            {
                sceneModified = true;
            }
        }
        else
        {
            if (UpgradeExistingRouletteTableSetup(rlRoot, bjRoot))
            {
                sceneModified = true;
            }
        }

#if UNITY_EDITOR
        if (saveInEditor && sceneModified && !Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            Debug.Log("[CasinoSceneAutoBuilder] Updated BlackjackTableSetup and RouletteTableSetup in BarGameBarni.unity.");
        }
#endif
    }

    private static bool UpgradeBlackjackTableSetup(GameObject bjRoot)
    {
        bool changed = false;

        BlackjackGameController controller = bjRoot.GetComponent<BlackjackGameController>();
        BlackjackTableVisuals visuals = bjRoot.GetComponent<BlackjackTableVisuals>();
        BlackjackBalanceManager balanceMgr = bjRoot.GetComponent<BlackjackBalanceManager>();
        TableCameraZoomController zoom = bjRoot.GetComponent<TableCameraZoomController>();

        if (zoom == null)
        {
            zoom = bjRoot.AddComponent<TableCameraZoomController>();
            changed = true;
        }

        // Configure gentle, full-table camera zoom (not over-zoomed)
        Vector3 desiredZoomPos = new Vector3(0f, 1.24f, -1.08f);
        if (Vector3.Distance(zoom.localZoomPosition, desiredZoomPos) > 0.01f ||
            Mathf.Abs(zoom.zoomedFieldOfView - 59f) > 0.5f ||
            Mathf.Abs(zoom.zoomPitchAngle - 18f) > 0.5f)
        {
            zoom.localZoomPosition = desiredZoomPos;
            zoom.zoomPitchAngle = 18f;
            zoom.zoomedFieldOfView = 59f;
            zoom.zoomedMaxHorizontalLook = 18f;
            zoom.zoomedMaxVerticalLook = 12f;
            changed = true;
        }

        Transform zoomAnchor = bjRoot.transform.Find("CameraZoomAnchor");
        if (zoomAnchor == null)
        {
            GameObject anchorGo = new GameObject("CameraZoomAnchor");
            anchorGo.transform.SetParent(bjRoot.transform, false);
            zoomAnchor = anchorGo.transform;
            changed = true;
        }
        zoomAnchor.localPosition = zoom.localZoomPosition;
        zoomAnchor.localRotation = Quaternion.Euler(zoom.zoomPitchAngle, 0f, 0f);
        zoomAnchor.localScale = Vector3.one;
        zoom.zoomAnchor = zoomAnchor;

        if (controller != null && controller.cameraZoomController != zoom)
        {
            controller.cameraZoomController = zoom;
            changed = true;
        }

        // Ensure all BlackjackInteractiveElement labels are unparented to uniform (1,1,1) root scale
        BlackjackInteractiveElement[] elements = bjRoot.GetComponentsInChildren<BlackjackInteractiveElement>(true);
        for (int i = 0; i < elements.Length; i++)
        {
            BlackjackInteractiveElement elem = elements[i];
            if (elem == null) continue;
            if (elem.labelText != null && elem.labelText.transform.parent == elem.transform)
            {
                changed = true;
            }
            elem.EnsureUniformLabelHierarchyAndCaptureState();
        }

        // Ensure Button_LeaveTable exists on BlackjackTableSetup
        if (controller != null && controller.leaveTableButton == null)
        {
            Transform existingLeave = bjRoot.transform.Find("Button_LeaveTable");
            if (existingLeave != null)
            {
                controller.leaveTableButton = existingLeave.GetComponent<BlackjackInteractiveElement>();
            }
            else
            {
                TMP_FontAsset font = GetFontFromBlackjack(bjRoot);
                Material matClear = LoadOrFindMaterial("BJ_BtnClear", bjRoot, "Button_ClearBet");
                controller.leaveTableButton = CreateBlackjackInteractiveCube(
                    bjRoot.transform,
                    "Button_LeaveTable",
                    new Vector3(0.56f, 0.855f, -0.35f),
                    new Vector3(0.14f, 0.036f, 0.09f),
                    matClear,
                    new Color(0.55f, 0.22f, 0.22f, 1f),
                    BlackjackInteractiveElement.ElementActionType.LeaveTable,
                    0f,
                    "Leave Blackjack Table (Zoom Out)",
                    "LEAVE\nTABLE",
                    0.20f,
                    font,
                    controller);
            }
            changed = true;
        }

        if (visuals != null)
        {
            visuals.EnsureReadableTextDisplayTransforms();
        }

        return changed;
    }

    private static bool UpgradeExistingRouletteTableSetup(GameObject rlRoot, GameObject bjRoot)
    {
        bool changed = false;
        TableCameraZoomController zoom = rlRoot.GetComponent<TableCameraZoomController>();
        if (zoom == null)
        {
            zoom = rlRoot.AddComponent<TableCameraZoomController>();
            changed = true;
        }

        Vector3 desiredZoomPos = new Vector3(0f, 1.24f, -1.08f);
        if (Vector3.Distance(zoom.localZoomPosition, desiredZoomPos) > 0.01f ||
            Mathf.Abs(zoom.zoomedFieldOfView - 59f) > 0.5f ||
            Mathf.Abs(zoom.zoomPitchAngle - 18f) > 0.5f)
        {
            zoom.localZoomPosition = desiredZoomPos;
            zoom.zoomPitchAngle = 18f;
            zoom.zoomedFieldOfView = 59f;
            zoom.zoomedMaxHorizontalLook = 18f;
            zoom.zoomedMaxVerticalLook = 12f;
            changed = true;
        }

        Transform zoomAnchor = rlRoot.transform.Find("CameraZoomAnchor");
        if (zoomAnchor != null)
        {
            zoomAnchor.localPosition = zoom.localZoomPosition;
            zoomAnchor.localRotation = Quaternion.Euler(zoom.zoomPitchAngle, 0f, 0f);
            zoom.zoomAnchor = zoomAnchor;
        }

        RouletteGameController controller = rlRoot.GetComponent<RouletteGameController>();
        if (controller != null && controller.boardBetElements.Count < 40)
        {
            Object.DestroyImmediate(rlRoot);
            BuildRouletteTableSetup(bjRoot);
            return true;
        }

        return changed;
    }

    private static GameObject BuildRouletteTableSetup(GameObject bjRoot)
    {
        TMP_FontAsset font = GetFontFromBlackjack(bjRoot);
        Material matWood = LoadOrFindMaterial("BJ_WoodDark", bjRoot, "TablePedestal");
        Material matFelt = LoadOrFindMaterial("BJ_FeltGreen", bjRoot, "TableFeltSurface");
        Material matGold = LoadOrFindMaterial("BJ_GoldTrim", bjRoot, "DealerCardZoneTrim");
        Material matCardWhite = LoadOrFindMaterial("BJ_CardWhite", bjRoot, null);
        Material matChip1 = LoadOrFindMaterial("BJ_Chip1", bjRoot, "Chip_1");
        Material matChip5 = LoadOrFindMaterial("BJ_Chip5", bjRoot, "Chip_5");
        Material matChip10 = LoadOrFindMaterial("BJ_Chip10", bjRoot, "Chip_10");
        Material matChip25 = LoadOrFindMaterial("BJ_Chip25", bjRoot, "Chip_25");
        Material matChip50 = LoadOrFindMaterial("BJ_Chip50", bjRoot, "Chip_50");
        Material matBtnDeal = LoadOrFindMaterial("BJ_BtnDeal", bjRoot, "Button_Deal");
        Material matBtnClear = LoadOrFindMaterial("BJ_BtnClear", bjRoot, "Button_ClearBet");
        Material matRed = LoadOrCreateColoredMaterial("BJ_RouletteRed", matCardWhite, new Color(0.78f, 0.14f, 0.16f, 1f));
        Material matBlack = LoadOrCreateColoredMaterial("BJ_RouletteBlack", matCardWhite, new Color(0.13f, 0.14f, 0.18f, 1f));
        Material matGreen = LoadOrCreateColoredMaterial("BJ_RouletteGreen", matCardWhite, new Color(0.08f, 0.55f, 0.24f, 1f));

        // Placed directly in front of Cube (10) at (-2.533, 4.536, 9.377) facing North (+Z)
        GameObject root = new GameObject("RouletteTableSetup");
        root.transform.position = new Vector3(-2.533f, 4.536f, 10.48f);
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        BlackjackBalanceManager sharedBalance = null;
        if (bjRoot != null)
        {
            sharedBalance = bjRoot.GetComponent<BlackjackBalanceManager>();
        }
        if (sharedBalance == null)
        {
            sharedBalance = root.AddComponent<BlackjackBalanceManager>();
        }

        RouletteTableVisuals visuals = root.AddComponent<RouletteTableVisuals>();
        RouletteGameController controller = root.AddComponent<RouletteGameController>();
        TableCameraZoomController zoom = root.AddComponent<TableCameraZoomController>();

        zoom.localZoomPosition = new Vector3(0f, 1.24f, -1.08f);
        zoom.zoomPitchAngle = 18f;
        zoom.zoomedFieldOfView = 59f;
        zoom.zoomedMaxHorizontalLook = 18f;
        zoom.zoomedMaxVerticalLook = 12f;

        GameObject zoomAnchorGo = new GameObject("CameraZoomAnchor");
        zoomAnchorGo.transform.SetParent(root.transform, false);
        zoomAnchorGo.transform.localPosition = zoom.localZoomPosition;
        zoomAnchorGo.transform.localRotation = Quaternion.Euler(zoom.zoomPitchAngle, 0f, 0f);
        zoom.zoomAnchor = zoomAnchorGo.transform;

        // 1. Static Table Structure Cubes
        CreateStaticCube(root.transform, "TablePedestal", new Vector3(0f, 0.39f, 0f), new Vector3(1.18f, 0.78f, 0.74f), matWood, true);
        CreateStaticCube(root.transform, "TableTopBase", new Vector3(0f, 0.80f, 0f), new Vector3(1.72f, 0.06f, 1.02f), matWood, true);
        CreateStaticCube(root.transform, "TableFeltSurface", new Vector3(0f, 0.832f, 0f), new Vector3(1.60f, 0.01f, 0.90f), matFelt, false);

        CreateStaticCube(root.transform, "Rail_North_Backboard", new Vector3(0f, 1.12f, 0.485f), new Vector3(1.72f, 0.56f, 0.05f), matWood, false);
        CreateStaticCube(root.transform, "Rail_North_GoldTrim", new Vector3(0f, 1.38f, 0.458f), new Vector3(1.66f, 0.025f, 0.015f), matGold, false);
        CreateStaticCube(root.transform, "Rail_South_Front", new Vector3(0f, 0.855f, -0.485f), new Vector3(1.72f, 0.055f, 0.05f), matWood, false);
        CreateStaticCube(root.transform, "Rail_West_Left", new Vector3(-0.835f, 0.855f, 0f), new Vector3(0.05f, 0.055f, 0.92f), matWood, false);
        CreateStaticCube(root.transform, "Rail_East_Right", new Vector3(0.835f, 0.855f, 0f), new Vector3(0.05f, 0.055f, 0.92f), matWood, false);

        // 2. Left-Side 3D Cube Roulette Wheel Basin (centered at X = -0.495, Z = 0.08)
        float wx = -0.495f;
        float wz = 0.08f;
        CreateStaticCube(root.transform, "WheelHousingBase", new Vector3(wx, 0.844f, wz), new Vector3(0.48f, 0.020f, 0.48f), matWood, false);
        CreateStaticCube(root.transform, "WheelOuterRim_North", new Vector3(wx, 0.868f, wz + 0.22f), new Vector3(0.48f, 0.036f, 0.04f), matWood, false);
        CreateStaticCube(root.transform, "WheelOuterRim_South", new Vector3(wx, 0.868f, wz - 0.22f), new Vector3(0.48f, 0.036f, 0.04f), matWood, false);
        CreateStaticCube(root.transform, "WheelOuterRim_West", new Vector3(wx - 0.22f, 0.868f, wz), new Vector3(0.04f, 0.036f, 0.40f), matWood, false);
        CreateStaticCube(root.transform, "WheelOuterRim_East", new Vector3(wx + 0.22f, 0.868f, wz), new Vector3(0.04f, 0.036f, 0.40f), matWood, false);
        CreateStaticCube(root.transform, "WheelBowlFloor", new Vector3(wx, 0.852f, wz), new Vector3(0.39f, 0.008f, 0.39f), matBlack, false);
        CreateStaticCube(root.transform, "WheelGoldBezel_N", new Vector3(wx, 0.888f, wz + 0.22f), new Vector3(0.46f, 0.006f, 0.025f), matGold, false);
        CreateStaticCube(root.transform, "WheelGoldBezel_S", new Vector3(wx, 0.888f, wz - 0.22f), new Vector3(0.46f, 0.006f, 0.025f), matGold, false);

        GameObject rotorPivotGo = new GameObject("WheelRotorPivot");
        rotorPivotGo.transform.SetParent(root.transform, false);
        rotorPivotGo.transform.localPosition = new Vector3(wx, 0.858f, wz);

        GameObject ballPivotGo = new GameObject("BallOrbitPivot");
        ballPivotGo.transform.SetParent(root.transform, false);
        ballPivotGo.transform.localPosition = new Vector3(wx, 0.866f, wz);

        visuals.wheelRotorPivot = rotorPivotGo.transform;
        visuals.ballOrbitPivot = ballPivotGo.transform;
        visuals.optionalFontAsset = font;
        visuals.baseCubeMaterial = matCardWhite;

        // 3. 3D Text Displays (parented at uniform (1,1,1) scale and angled toward camera)
        visuals.statusBannerText = CreateTableTmpText(
            root.transform, "Text_StatusBanner", new Vector3(0f, 1.30f, 0.44f), new Vector3(18f, 0f, 0f),
            "EUROPEAN ROULETTE - STRAIGHT PAYS 35 TO 1", 0.38f, new Color(1f, 0.90f, 0.32f, 1f), new Vector2(1.62f, 0.24f), font);

        visuals.actionPromptText = CreateTableTmpText(
            root.transform, "Text_ActionPrompt", new Vector3(0f, 1.13f, 0.43f), new Vector3(20f, 0f, 0f),
            "SELECT CHIP ($1-$50), CLICK NUMBERS/OUTSIDE BETS, THEN SPIN", 0.24f, new Color(0.92f, 0.96f, 1f, 1f), new Vector2(1.62f, 0.20f), font);

        visuals.balanceText = CreateTableTmpText(
            root.transform, "Text_Balance", new Vector3(-0.495f, 0.87f, 0.37f), new Vector3(58f, 0f, 0f),
            "BANKROLL: $100", 0.23f, new Color(0.45f, 1f, 0.58f, 1f), new Vector2(0.55f, 0.16f), font);

        visuals.totalBetText = CreateTableTmpText(
            root.transform, "Text_TotalBet", new Vector3(0.05f, 0.87f, 0.37f), new Vector3(58f, 0f, 0f),
            "TOTAL BET: $0", 0.23f, new Color(1f, 0.86f, 0.32f, 1f), new Vector2(0.55f, 0.16f), font);

        visuals.selectedChipText = CreateTableTmpText(
            root.transform, "Text_SelectedChip", new Vector3(0.54f, 0.87f, 0.37f), new Vector3(58f, 0f, 0f),
            "CHIP: $5", 0.23f, new Color(0.65f, 0.92f, 1f, 1f), new Vector2(0.45f, 0.16f), font);

        visuals.wheelResultText = CreateTableTmpText(
            root.transform, "Text_WheelResult", new Vector3(-0.495f, 0.865f, -0.21f), new Vector3(62f, 0f, 0f),
            "LAST: --", 0.22f, Color.white, new Vector2(0.52f, 0.16f), font);

        // 4. Right-Side Inside & Outside Betting Grid
        List<RouletteInteractiveElement> boardSpots = new List<RouletteInteractiveElement>();

        // 0 (Green) vertical bar on the left of the 12x3 grid
        boardSpots.Add(CreateRouletteInteractiveCube(
            root.transform, "Spot_Zero", new Vector3(-0.205f, 0.844f, 0.125f), new Vector3(0.058f, 0.018f, 0.256f),
            matGreen, new Color(0.10f, 0.62f, 0.26f, 1f),
            RouletteInteractiveElement.RouletteActionType.PlaceBoardBet, 0f,
            RouletteBetType.StraightNumber, 0,
            "Bet Straight on 0 (Pays 35:1)", "0", 0.24f, font, controller));

        // 36 Straight Numbers (12 columns x 3 rows)
        float gridStartX = -0.132f;
        float colStepX = 0.062f;
        float[] rowZ = { 0.036f, 0.125f, 0.214f }; // Row 1 (1,4..34), Row 2 (2,5..35), Row 3 (3,6..36)

        for (int col = 0; col < 12; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int num = col * 3 + (row + 1);
                float sx = gridStartX + col * colStepX;
                float sz = rowZ[row];
                bool isRed = RouletteLogicEngine.GetPocketColor(num) == RoulettePocketColor.Red;
                Material spotMat = isRed ? matRed : matBlack;
                Color spotCol = isRed ? new Color(0.82f, 0.15f, 0.18f, 1f) : new Color(0.15f, 0.16f, 0.20f, 1f);

                boardSpots.Add(CreateRouletteInteractiveCube(
                    root.transform, $"Spot_Num_{num}", new Vector3(sx, 0.844f, sz), new Vector3(0.056f, 0.018f, 0.082f),
                    spotMat, spotCol,
                    RouletteInteractiveElement.RouletteActionType.PlaceBoardBet, 0f,
                    RouletteBetType.StraightNumber, num,
                    $"Bet Straight on {num} ({(isRed ? "Red" : "Black")}, Pays 35:1)",
                    num.ToString(), 0.20f, font, controller));
            }
        }

        // 3 Column Bets (2:1) on the right end of each row
        RouletteBetType[] colTypes = { RouletteBetType.Column1, RouletteBetType.Column2, RouletteBetType.Column3 };
        for (int row = 0; row < 3; row++)
        {
            boardSpots.Add(CreateRouletteInteractiveCube(
                root.transform, $"Spot_Column_{row + 1}", new Vector3(0.625f, 0.844f, rowZ[row]), new Vector3(0.072f, 0.018f, 0.082f),
                matGold, new Color(0.65f, 0.50f, 0.15f, 1f),
                RouletteInteractiveElement.RouletteActionType.PlaceBoardBet, 0f,
                colTypes[row], 0,
                $"Bet Column {row + 1} (Pays 2:1)", "2:1", 0.18f, font, controller));
        }

        // 3 Dozen Bets (1ST 12, 2ND 12, 3RD 12)
        RouletteBetType[] dozenTypes = { RouletteBetType.Dozen1, RouletteBetType.Dozen2, RouletteBetType.Dozen3 };
        string[] dozenLabels = { "1ST 12", "2ND 12", "3RD 12" };
        float[] dozenX = { -0.039f, 0.209f, 0.457f };
        for (int d = 0; d < 3; d++)
        {
            boardSpots.Add(CreateRouletteInteractiveCube(
                root.transform, $"Spot_Dozen_{d + 1}", new Vector3(dozenX[d], 0.844f, -0.056f), new Vector3(0.238f, 0.018f, 0.076f),
                matFelt, new Color(0.12f, 0.46f, 0.26f, 1f),
                RouletteInteractiveElement.RouletteActionType.PlaceBoardBet, 0f,
                dozenTypes[d], 0,
                $"Bet {dozenLabels[d]} (Pays 2:1)", dozenLabels[d], 0.19f, font, controller));
        }

        // 6 Even-Money Outside Bets (1-18, EVEN, RED, BLACK, ODD, 19-36)
        RouletteBetType[] outsideTypes =
        {
            RouletteBetType.Low1To18,
            RouletteBetType.Even,
            RouletteBetType.Red,
            RouletteBetType.Black,
            RouletteBetType.Odd,
            RouletteBetType.High19To36
        };
        string[] outsideLabels = { "1-18", "EVEN", "RED", "BLACK", "ODD", "19-36" };
        Material[] outsideMats = { matFelt, matFelt, matRed, matBlack, matFelt, matFelt };
        Color[] outsideCols =
        {
            new Color(0.14f, 0.44f, 0.26f, 1f),
            new Color(0.14f, 0.44f, 0.26f, 1f),
            new Color(0.82f, 0.15f, 0.18f, 1f),
            new Color(0.15f, 0.16f, 0.20f, 1f),
            new Color(0.14f, 0.44f, 0.26f, 1f),
            new Color(0.14f, 0.44f, 0.26f, 1f)
        };

        for (int o = 0; o < 6; o++)
        {
            float ox = -0.101f + o * 0.124f;
            boardSpots.Add(CreateRouletteInteractiveCube(
                root.transform, $"Spot_Outside_{outsideLabels[o]}", new Vector3(ox, 0.844f, -0.146f), new Vector3(0.116f, 0.018f, 0.078f),
                outsideMats[o], outsideCols[o],
                RouletteInteractiveElement.RouletteActionType.PlaceBoardBet, 0f,
                outsideTypes[o], 0,
                $"Bet {outsideLabels[o]} (Pays 1:1)", outsideLabels[o], 0.18f, font, controller));
        }

        // 5. Front-Row Chip Denomination Selector Cubes & Action Buttons
        List<RouletteInteractiveElement> chipSelectors = new List<RouletteInteractiveElement>();
        int[] chipDenoms = { 1, 5, 10, 25, 50 };
        Material[] chipMats = { matChip1, matChip5, matChip10, matChip25, matChip50 };
        Color[] chipColors =
        {
            new Color(0.90f, 0.90f, 0.94f, 1f),
            new Color(0.86f, 0.18f, 0.18f, 1f),
            new Color(0.16f, 0.46f, 0.88f, 1f),
            new Color(0.16f, 0.72f, 0.30f, 1f),
            new Color(0.92f, 0.72f, 0.16f, 1f)
        };

        for (int c = 0; c < 5; c++)
        {
            float cx = -0.54f + c * 0.125f;
            chipSelectors.Add(CreateRouletteInteractiveCube(
                root.transform, $"ChipSelector_{chipDenoms[c]}", new Vector3(cx, 0.855f, -0.35f), new Vector3(0.095f, 0.032f, 0.095f),
                chipMats[c], chipColors[c],
                RouletteInteractiveElement.RouletteActionType.SelectChipDenomination, chipDenoms[c],
                RouletteBetType.StraightNumber, 0,
                $"Select ${chipDenoms[c]} Chip", $"${chipDenoms[c]}", 0.22f, font, controller));
        }

        RouletteInteractiveElement clearBtn = CreateRouletteInteractiveCube(
            root.transform, "Button_ClearBets", new Vector3(0.14f, 0.855f, -0.35f), new Vector3(0.15f, 0.038f, 0.095f),
            matBtnClear, new Color(0.75f, 0.42f, 0.14f, 1f),
            RouletteInteractiveElement.RouletteActionType.ClearBets, 0f,
            RouletteBetType.StraightNumber, 0,
            "Clear All Staged Bets", "CLEAR\nBETS", 0.19f, font, controller);

        RouletteInteractiveElement spinBtn = CreateRouletteInteractiveCube(
            root.transform, "Button_SpinWheel", new Vector3(0.35f, 0.855f, -0.35f), new Vector3(0.18f, 0.042f, 0.10f),
            matBtnDeal, new Color(0.16f, 0.68f, 0.30f, 1f),
            RouletteInteractiveElement.RouletteActionType.SpinOrNewRound, 0f,
            RouletteBetType.StraightNumber, 0,
            "Sit / Join Roulette Table", "JOIN\nTABLE", 0.20f, font, controller);

        RouletteInteractiveElement leaveBtn = CreateRouletteInteractiveCube(
            root.transform, "Button_LeaveTable", new Vector3(0.56f, 0.855f, -0.35f), new Vector3(0.15f, 0.038f, 0.095f),
            matBtnClear, new Color(0.58f, 0.20f, 0.20f, 1f),
            RouletteInteractiveElement.RouletteActionType.LeaveTable, 0f,
            RouletteBetType.StraightNumber, 0,
            "Leave Roulette Table (Zoom Out)", "LEAVE\nTABLE", 0.19f, font, controller);

        controller.balanceManager = sharedBalance;
        controller.tableVisuals = visuals;
        controller.cameraZoomController = zoom;
        controller.chipSelectorElements = chipSelectors;
        controller.boardBetElements = boardSpots;
        controller.clearBetsButton = clearBtn;
        controller.spinButton = spinBtn;
        controller.leaveTableButton = leaveBtn;
        controller.playerController = Object.FindFirstObjectByType<GridCrawlerController>();
        controller.smoothCamera = Object.FindFirstObjectByType<SmoothCameraFollow>();

        visuals.BuildWheelPocketCubesIfNeeded();

        return root;
    }

    private static GameObject CreateStaticCube(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;

        if (!keepCollider)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mat != null)
        {
            mr.sharedMaterial = mat;
        }
        return go;
    }

    private static TMP_Text CreateTableTmpText(
        Transform parent, string name, Vector3 localPos, Vector3 localEuler,
        string text, float fontSize, Color color, Vector2 rectSize, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.localPosition = localPos;
        rt.localRotation = Quaternion.Euler(localEuler);
        rt.localScale = Vector3.one;
        rt.sizeDelta = rectSize;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        return tmp;
    }

    private static BlackjackInteractiveElement CreateBlackjackInteractiveCube(
        Transform tableRoot, string name, Vector3 localPos, Vector3 localScale,
        Material mat, Color activeColor, BlackjackInteractiveElement.ElementActionType actionType,
        float chipDenom, string hoverPrompt, string labelStr, float fontSize,
        TMP_FontAsset font, BlackjackGameController controller)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(tableRoot, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mat != null) mr.sharedMaterial = mat;

        InteractableObject io = go.AddComponent<InteractableObject>();
        io.hoverText = hoverPrompt;

        Vector3 labelPos = localPos + new Vector3(0f, localScale.y * 0.5f + 0.008f, 0f);
        TMP_Text labelTmp = CreateTableTmpText(
            tableRoot, $"{name}_Label", labelPos, new Vector3(75f, 0f, 0f),
            labelStr, fontSize, Color.white, new Vector2(0.45f, 0.25f), font);

        BlackjackInteractiveElement bie = go.AddComponent<BlackjackInteractiveElement>();
        bie.gameController = controller;
        bie.actionType = actionType;
        bie.chipDenomination = chipDenom;
        bie.activeHoverPrompt = hoverPrompt;
        bie.labelText = labelTmp;
        bie.primaryRenderer = mr;
        bie.activeColor = activeColor;
        bie.EnsureUniformLabelHierarchyAndCaptureState();
        return bie;
    }

    private static RouletteInteractiveElement CreateRouletteInteractiveCube(
        Transform tableRoot, string name, Vector3 localPos, Vector3 localScale,
        Material mat, Color activeColor, RouletteInteractiveElement.RouletteActionType actionType,
        float chipDenom, RouletteBetType betType, int betNumber,
        string hoverPrompt, string labelStr, float fontSize,
        TMP_FontAsset font, RouletteGameController controller)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(tableRoot, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mat != null) mr.sharedMaterial = mat;

        InteractableObject io = go.AddComponent<InteractableObject>();
        io.hoverText = hoverPrompt;

        float labelPitch = actionType == RouletteInteractiveElement.RouletteActionType.PlaceBoardBet ? 82f : 74f;
        Vector3 labelPos = localPos + new Vector3(0f, localScale.y * 0.5f + 0.006f, 0f);
        TMP_Text labelTmp = CreateTableTmpText(
            tableRoot, $"{name}_Label", labelPos, new Vector3(labelPitch, 0f, 0f),
            labelStr, fontSize, Color.white, new Vector2(0.35f, 0.20f), font);

        RouletteInteractiveElement rie = go.AddComponent<RouletteInteractiveElement>();
        rie.gameController = controller;
        rie.actionType = actionType;
        rie.chipDenomination = chipDenom;
        rie.betType = betType;
        rie.betNumber = betNumber;
        rie.activeHoverPrompt = hoverPrompt;
        rie.labelText = labelTmp;
        rie.primaryRenderer = mr;
        rie.activeColor = activeColor;
        return rie;
    }

    private static TMP_FontAsset GetFontFromBlackjack(GameObject bjRoot)
    {
#if UNITY_EDITOR
        TMP_FontAsset edFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Barni/ Font/ByteBounce SDF.asset");
        if (edFont != null) return edFont;
#endif
        if (bjRoot != null)
        {
            BlackjackTableVisuals vis = bjRoot.GetComponent<BlackjackTableVisuals>();
            if (vis != null && vis.optionalFontAsset != null) return vis.optionalFontAsset;
            TMP_Text anyTmp = bjRoot.GetComponentInChildren<TMP_Text>(true);
            if (anyTmp != null && anyTmp.font != null) return anyTmp.font;
        }
        return null;
    }

    private static Material LoadOrFindMaterial(string matName, GameObject bjRoot, string fallbackChildName)
    {
#if UNITY_EDITOR
        Material edMat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Barni/Materials/{matName}.mat");
        if (edMat != null) return edMat;
#endif
        if (bjRoot != null && !string.IsNullOrEmpty(fallbackChildName))
        {
            Transform child = bjRoot.transform.Find(fallbackChildName);
            if (child != null)
            {
                MeshRenderer mr = child.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null) return mr.sharedMaterial;
            }
        }
        if (bjRoot != null)
        {
            BlackjackTableVisuals vis = bjRoot.GetComponent<BlackjackTableVisuals>();
            if (vis != null && vis.baseCubeMaterial != null) return vis.baseCubeMaterial;
        }
        return null;
    }

    private static Material LoadOrCreateColoredMaterial(string matName, Material templateMat, Color col)
    {
#if UNITY_EDITOR
        Material edMat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Barni/Materials/{matName}.mat");
        if (edMat != null) return edMat;
#endif
        Material runtimeMat = null;
        if (templateMat != null)
        {
            runtimeMat = new Material(templateMat);
        }
        else
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh != null) runtimeMat = new Material(sh);
        }

        if (runtimeMat != null)
        {
            runtimeMat.color = col;
            if (runtimeMat.HasProperty("_BaseColor")) runtimeMat.SetColor("_BaseColor", col);
        }
        return runtimeMat;
    }
}
