using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public enum UpgradeCategory { MoneyMultiplier, SpeedMultiplier, RemoteUpgrade }

[System.Serializable]
public class UpgradeTier
{
    public string tierName;
    public float cost;
    public float multiplierValue;
    [Tooltip("Optional custom ability description shown in the upgrade menu (used for Remote tiers).")]
    public string customDescription = "";

    public GameObject previewPrefab;
    public Vector3 previewPositionOffset = Vector3.zero;
    public Vector3 previewScale = new Vector3(50f, 50f, 50f);
    public Vector3 previewRotation = Vector3.zero;

    [Header("DVD Video Replacements (WebGL)")]
    [Tooltip("Type the exact file name including extension, e.g., 'dvd1.mp4'")]
    public string dvdVideoFileName;
    public AudioClip dvdMusic;

    [Header("Player & Remote Replacements")]
    public GameObject worldGameObject;
    public Mesh playerMesh1;
    public Mesh playerMesh2;

    [Header("Visual & Booster Upgrades")]
    public Material logoMaterial;
    public BoosterObject booster;
    [HideInInspector] public Vector3 initialWorldScale = Vector3.one;
}

[System.Serializable]
public class UpgradePath
{
    public string pathName;
    public UpgradeCategory category;
    public Transform[] animationTargetPoints;

    [Header("Targets to Update")]
    public VideoPlayer targetVideoPlayer;
    public AudioSource audioSource;
    public Renderer targetRenderer;

    [Header("Static Transition (WebGL)")]
    [Tooltip("Type the exact file name including extension, e.g., 'static.mp4'")]
    public string staticVideoFileName;
    public AudioClip staticSoundEffect;
    public float staticDuration = 0.5f;

    public MeshFilter targetMeshFilter1;
    public MeshFilter targetMeshFilter2;

    public List<UpgradeTier> tiers = new List<UpgradeTier>();
    public int equippedIndex = 0;
    public List<int> unlockedIndices = new List<int>();

    public UpgradeTier GetTier(int index)
    {
        if (tiers != null && index >= 0 && index < tiers.Count) return tiers[index];
        return null;
    }
}

public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    public DVDLogoBouncer bouncer;
    public List<UpgradePath> upgradePaths = new List<UpgradePath>();

    public float worldModelShrinkSpeed = 0.5f;

    /// <summary>
    /// Currently equipped Remote tier index (0 to 10 — 1 starter + 10 upgrades):
    /// 0 = Stock Clicker (Base Remote: direct hit impulse + bonus cash + squash/stretch + sparks)
    /// 1 = Mitosis Splitter (Base + splits logo into 2 smaller children that recombine on same-size collision)
    /// 2 = Telekinetic Grabber (After Mitosis, NOT part of Ultimate: hold click to grab & drag logo; wall/corner hits award bounces)
    /// 3 = Pinball Bumper Pro (Base + click empty screen to spawn up to 5 glowing 3D bumpers inside the TV screen)
    /// 4 = Dino Jump Remote (Hold Remote in Focus Mode: Google No-Internet style runner with cacti & birds!)
    /// 5 = Pong Paddle Remote (Hold Remote in Focus Mode: 1-Sided Pong paddle bar defender!)
    /// 6 = Suika Merger Remote (Hold Remote in Focus Mode: Suika Logo Drop & Exponential Merger; remembers board state!)
    /// 7 = Tower Stack Remote (Hold Remote in Focus Mode: Deformed Logo Tower Stacker; scrolls down & shrinks on topple!)
    /// 8 = Fish Frenzy Remote (Hold Remote in Focus Mode: Eat smaller swimming DVD logos & grow endlessly; bigger logos eat you!)
    /// 9 = Block Blast Remote (Hold Remote in Focus Mode: Full-screen 8x8 Block Blast with square DVD logos!)
    /// 10 = Omni-Remote 9000 (ULTIMATE Remote: combines Base + Mitosis Splitting + Pinball Bumpers all at once!)
    /// </summary>
    public int ActiveRemoteTier { get; private set; } = 0;

    public bool IsCurrentRemoteOmni => ActiveRemoteTier == 10;
    public bool IsCurrentRemoteBumperCapable => ActiveRemoteTier == 3 || ActiveRemoteTier == 10;
    public bool IsCurrentRemoteMitosisCapable => ActiveRemoteTier == 1 || ActiveRemoteTier == 10;
    public bool IsCurrentRemoteGrabber => ActiveRemoteTier == 2;
    public bool IsCurrentRemoteMinigame => ActiveRemoteTier >= 4 && ActiveRemoteTier <= 9;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        EnsureRemoteUpgradePath();
    }

    void Start()
    {
        EnsureRemoteUpgradePath();

        bool hasSavedUpgrades = RunSaveManager.HasInstance && RunSaveManager.Instance.HasSavedUpgrades;
        if (hasSavedUpgrades)
        {
            RunSaveManager.Instance.ApplySavedUpgradesToManager(this);
        }

        foreach (var path in upgradePaths)
        {
            if (path == null || path.tiers == null || path.tiers.Count == 0) continue;

            if (!path.unlockedIndices.Contains(0)) path.unlockedIndices.Add(0);

            if (!hasSavedUpgrades)
            {
                path.equippedIndex = 0;
            }
            else
            {
                path.equippedIndex = Mathf.Clamp(path.equippedIndex, 0, path.tiers.Count - 1);
            }

            int equippedIdx = path.equippedIndex;
            SnapWorldVisuals(path, equippedIdx);

            if (bouncer != null)
            {
                if (path.category == UpgradeCategory.MoneyMultiplier)
                {
                    bouncer.SetMoneyMultiplier(path.tiers[equippedIdx].multiplierValue);
                }
                else if (path.category == UpgradeCategory.SpeedMultiplier)
                {
                    bouncer.SetSpeedMultiplier(path.tiers[equippedIdx].multiplierValue);
                }
            }

            if (path.category == UpgradeCategory.RemoteUpgrade)
            {
                ActiveRemoteTier = Mathf.Clamp(equippedIdx, 0, path.tiers.Count - 1);
            }

            if (path.category == UpgradeCategory.MoneyMultiplier)
            {
                PlayEquippedDvdVideoImmediate(path, equippedIdx);
                PlayDvdMusicOnPathSources(path, path.tiers[equippedIdx].dvdMusic);
            }
        }
    }

    /// <summary>
    /// Ensures the 3rd UpgradePath (Remote Upgrades) exists and is populated with all 5 Remote Ability Tiers.
    /// </summary>
    public void EnsureRemoteUpgradePath()
    {
        if (upgradePaths == null) upgradePaths = new List<UpgradePath>();

        UpgradePath remotePath = null;
        for (int i = 0; i < upgradePaths.Count; i++)
        {
            if (upgradePaths[i] == null) continue;
            if (upgradePaths[i].category == UpgradeCategory.RemoteUpgrade ||
                (!string.IsNullOrEmpty(upgradePaths[i].pathName) && upgradePaths[i].pathName.ToLowerInvariant().Contains("remote")))
            {
                remotePath = upgradePaths[i];
                remotePath.category = UpgradeCategory.RemoteUpgrade;
                break;
            }
        }

        if (remotePath == null)
        {
            remotePath = new UpgradePath
            {
                pathName = "remote",
                category = UpgradeCategory.RemoteUpgrade,
                equippedIndex = 0,
                unlockedIndices = new List<int> { 0 },
                tiers = new List<UpgradeTier>()
            };
            upgradePaths.Add(remotePath);
        }

        string[] defaultNames = new string[]
        {
            "Stock Clicker",
            "Mitosis Splitter",
            "Telekinetic Grabber",
            "Pinball Bumper Pro",
            "Dino Jump Remote",
            "Pong Paddle Remote",
            "Suika Merger Remote",
            "Tower Stack Remote",
            "Fish Frenzy Remote",
            "Block Blast Remote",
            "Omni-Remote 9000"
        };
        float[] defaultCosts = new float[] { 0f, 85f, 190f, 320f, 420f, 520f, 640f, 760f, 880f, 1020f, 1250f };
        float[] defaultMults = new float[] { 1f, 2f, 2.5f, 3f, 3.5f, 4f, 4.5f, 5f, 5.5f, 6f, 7f };
        string[] defaultDescs = new string[]
        {
            "Direct Impulse Hit + Bonus Cash",
            "Splits Logo into 2x Children + Recombines",
            "Hold Logo: Drag Into Walls & Corners to Bounce!",
            "Click Empty Screen: Spawn up to 5 Bumpers",
            "Hold Remote: No-Internet Dino & Bird Jumper!",
            "Hold Remote: 1-Sided Pong Bar Defender!",
            "Hold Remote: Suika Logo Drop & Exponential Merger!",
            "Hold Remote: Deformed Logo Tower Stacker!",
            "Hold Remote: Eat Smaller Logos & Grow Endlessly!",
            "Hold Remote: Full-Screen DVD Block Blast Grid!",
            "ULTIMATE: Direct Hit + Mitosis + Bumpers!"
        };

        if (remotePath.tiers == null) remotePath.tiers = new List<UpgradeTier>();

        // Migrate from 3-tier layout (Stock, Mitosis, Pinball) to 5-tier layout first if needed
        if (remotePath.tiers.Count == 3 && remotePath.tiers[2] != null && remotePath.tiers[2].tierName == "Pinball Bumper Pro")
        {
            UpgradeTier oldPinballTier = remotePath.tiers[2];
            UpgradeTier grabberTier = new UpgradeTier
            {
                tierName = defaultNames[2],
                cost = defaultCosts[2],
                multiplierValue = defaultMults[2],
                customDescription = defaultDescs[2],
                previewScale = oldPinballTier.previewScale,
                previewRotation = oldPinballTier.previewRotation
            };
            UpgradeTier omniTier = new UpgradeTier
            {
                tierName = defaultNames[10],
                cost = defaultCosts[10],
                multiplierValue = defaultMults[10],
                customDescription = defaultDescs[10],
                previewScale = oldPinballTier.previewScale,
                previewRotation = oldPinballTier.previewRotation
            };
            remotePath.tiers[2] = grabberTier;
            remotePath.tiers.Add(oldPinballTier);
            remotePath.tiers.Add(omniTier);
        }

        // Migrate from 5-tier layout (where index 4 was Omni-Remote 9000) to 7-tier layout
        if (remotePath.tiers.Count == 5 && remotePath.tiers[4] != null && remotePath.tiers[4].tierName == "Omni-Remote 9000")
        {
            UpgradeTier oldOmniTier = remotePath.tiers[4];
            UpgradeTier dinoTier = new UpgradeTier
            {
                tierName = defaultNames[4],
                cost = defaultCosts[4],
                multiplierValue = defaultMults[4],
                customDescription = defaultDescs[4],
                previewScale = oldOmniTier.previewScale,
                previewRotation = oldOmniTier.previewRotation
            };
            UpgradeTier pongTier = new UpgradeTier
            {
                tierName = defaultNames[5],
                cost = defaultCosts[5],
                multiplierValue = defaultMults[5],
                customDescription = defaultDescs[5],
                previewScale = oldOmniTier.previewScale,
                previewRotation = oldOmniTier.previewRotation
            };
            remotePath.tiers[4] = dinoTier;
            remotePath.tiers.Add(pongTier);
            remotePath.tiers.Add(oldOmniTier);
        }

        // Migrate from 7-tier layout (where index 6 was Omni-Remote 9000) to 9-tier layout (inserting Suika & Tower before Omni)
        if (remotePath.tiers.Count == 7 && remotePath.tiers[6] != null && remotePath.tiers[6].tierName == "Omni-Remote 9000")
        {
            UpgradeTier oldOmniTier = remotePath.tiers[6];
            UpgradeTier suikaTier = new UpgradeTier
            {
                tierName = defaultNames[6],
                cost = defaultCosts[6],
                multiplierValue = defaultMults[6],
                customDescription = defaultDescs[6],
                previewScale = oldOmniTier.previewScale,
                previewRotation = oldOmniTier.previewRotation
            };
            UpgradeTier towerTier = new UpgradeTier
            {
                tierName = defaultNames[7],
                cost = defaultCosts[7],
                multiplierValue = defaultMults[7],
                customDescription = defaultDescs[7],
                previewScale = oldOmniTier.previewScale,
                previewRotation = oldOmniTier.previewRotation
            };
            remotePath.tiers[6] = suikaTier;
            remotePath.tiers.Add(towerTier);
            remotePath.tiers.Add(oldOmniTier);
        }

        // Migrate from 9-tier layout (where index 8 was Omni-Remote 9000) to 11-tier layout (inserting Fish Frenzy & Block Blast before Omni)
        if (remotePath.tiers.Count == 9 && remotePath.tiers[8] != null && remotePath.tiers[8].tierName == "Omni-Remote 9000")
        {
            UpgradeTier oldOmniTier = remotePath.tiers[8];
            UpgradeTier fishTier = new UpgradeTier
            {
                tierName = defaultNames[8],
                cost = defaultCosts[8],
                multiplierValue = defaultMults[8],
                customDescription = defaultDescs[8],
                previewScale = oldOmniTier.previewScale,
                previewRotation = oldOmniTier.previewRotation
            };
            UpgradeTier blockBlastTier = new UpgradeTier
            {
                tierName = defaultNames[9],
                cost = defaultCosts[9],
                multiplierValue = defaultMults[9],
                customDescription = defaultDescs[9],
                previewScale = oldOmniTier.previewScale,
                previewRotation = oldOmniTier.previewRotation
            };
            oldOmniTier.cost = defaultCosts[10];
            oldOmniTier.multiplierValue = defaultMults[10];
            remotePath.tiers[8] = fishTier;
            remotePath.tiers.Add(blockBlastTier);
            remotePath.tiers.Add(oldOmniTier);
        }

        while (remotePath.tiers.Count < 11)
        {
            remotePath.tiers.Add(new UpgradeTier());
        }

        UpgradeTier baseRefTier = remotePath.tiers[0];
        Transform remoteRoot = (baseRefTier != null && baseRefTier.worldGameObject != null)
            ? baseRefTier.worldGameObject.transform.parent
            : null;

        for (int i = 0; i < 11; i++)
        {
            UpgradeTier t = remotePath.tiers[i];
            if (string.IsNullOrEmpty(t.tierName) || t.tierName == "Level 1")
                t.tierName = defaultNames[i];
            if (i > 0 && t.cost <= 0f)
                t.cost = defaultCosts[i];
            else if (i == 0)
                t.cost = 0f;
            if (t.multiplierValue <= 0f)
                t.multiplierValue = defaultMults[i];
            if (string.IsNullOrEmpty(t.customDescription) || i == 2)
                t.customDescription = defaultDescs[i];

            if (baseRefTier != null && i > 0)
            {
                if (t.previewPrefab == null && baseRefTier.previewPrefab != null)
                    t.previewPrefab = baseRefTier.previewPrefab;

                // Ensure each tier has its own distinct desk model object so SnapWorldVisuals never disables Tier 0
                if ((t.worldGameObject == null || t.worldGameObject == baseRefTier.worldGameObject) && remoteRoot != null)
                {
                    string expectedName = $"RemoteControlTV_Tier{i}";
                    Transform existingChild = remoteRoot.Find(expectedName);
                    if (existingChild != null)
                    {
                        t.worldGameObject = existingChild.gameObject;
                    }
                    else if (Application.isPlaying && baseRefTier.worldGameObject != null)
                    {
                        GameObject clone = Instantiate(baseRefTier.worldGameObject, remoteRoot);
                        clone.name = expectedName;
                        clone.SetActive(false);
                        t.worldGameObject = clone;
                    }
                }

                if (t.previewScale == new Vector3(50f, 50f, 50f) && baseRefTier.previewScale != Vector3.zero)
                {
                    t.previewScale = baseRefTier.previewScale;
                    t.previewRotation = baseRefTier.previewRotation;
                }
            }
        }

        if (remotePath.unlockedIndices == null) remotePath.unlockedIndices = new List<int>();
        if (!remotePath.unlockedIndices.Contains(0))
        {
            remotePath.unlockedIndices.Add(0);
        }

        // Also ensure DVD Player (SpeedMultiplier) and DVD Pile (MoneyMultiplier) have 11 total tiers (1 starter + 10 upgrades)!
        EnsurePlayerAndDvdPathsHave11Tiers();
    }

    private void EnsurePlayerAndDvdPathsHave11Tiers()
    {
        for (int p = 0; p < upgradePaths.Count; p++)
        {
            UpgradePath path = upgradePaths[p];
            if (path == null || path.tiers == null || path.tiers.Count == 0) continue;

            if (path.category == UpgradeCategory.SpeedMultiplier && path.tiers.Count == 10)
            {
                UpgradeTier prev = path.tiers[9];
                UpgradeTier tier10 = new UpgradeTier
                {
                    tierName = "Quantum Singularity Deck",
                    cost = 15000f,
                    multiplierValue = 18.50f,
                    previewPrefab = prev.previewPrefab,
                    previewPositionOffset = prev.previewPositionOffset,
                    previewScale = prev.previewScale,
                    previewRotation = prev.previewRotation,
                    worldGameObject = prev.worldGameObject,
                    logoMaterial = prev.logoMaterial,
                    booster = prev.booster,
                    initialWorldScale = prev.initialWorldScale
                };
                path.tiers.Add(tier10);
            }
            else if (path.category == UpgradeCategory.MoneyMultiplier && path.tiers.Count == 10)
            {
                UpgradeTier prev = path.tiers[9];
                UpgradeTier tier10 = new UpgradeTier
                {
                    tierName = "Lost Studio Master",
                    cost = 20000f,
                    multiplierValue = 25000f,
                    previewPrefab = prev.previewPrefab,
                    previewPositionOffset = prev.previewPositionOffset,
                    previewScale = prev.previewScale,
                    previewRotation = prev.previewRotation,
                    dvdVideoFileName = prev.dvdVideoFileName,
                    dvdMusic = prev.dvdMusic,
                    logoMaterial = prev.logoMaterial,
                    booster = prev.booster,
                    initialWorldScale = prev.initialWorldScale
                };
                path.tiers.Add(tier10);
            }
        }
    }

    public UpgradePath GetPathByCategory(UpgradeCategory category)
    {
        EnsureRemoteUpgradePath();
        for (int i = 0; i < upgradePaths.Count; i++)
        {
            if (upgradePaths[i] != null && upgradePaths[i].category == category)
                return upgradePaths[i];
        }
        return null;
    }

    public int GetPathIndexByCategory(UpgradeCategory category)
    {
        EnsureRemoteUpgradePath();
        for (int i = 0; i < upgradePaths.Count; i++)
        {
            if (upgradePaths[i] != null && upgradePaths[i].category == category)
                return i;
        }
        return 0;
    }

    public decimal GetTierCost(UpgradeTier tier)
    {
        if (tier == null) return 0m;
        float mult = BarManager.Instance != null ? BarManager.Instance.CurrentInflationMultiplier : 1f;
        return (decimal)(tier.cost * mult);
    }

    public bool TryPurchaseUpgrade(UpgradePath path, int targetIndex)
    {
        if (path.unlockedIndices.Contains(targetIndex)) return true;

        decimal cost = GetTierCost(path.tiers[targetIndex]);
        if (bouncer != null && bouncer.TrySpendMoney(cost))
        {
            path.unlockedIndices.Add(targetIndex);
            return true;
        }
        return false;
    }

    public BoosterObject GetTierBooster(UpgradeTier tier)
    {
        if (tier == null) return null;
        if (tier.booster != null) return tier.booster;
        if (tier.worldGameObject != null) return tier.worldGameObject.GetComponentInChildren<BoosterObject>(true);
        return null;
    }

    private void UpdateLogoMaterial(UpgradePath path, int targetIndex)
    {
        // Only the DVD player upgrades change the DVD bouncing logo!
        // DVDs (MoneyMultiplier) change the TV screen background video, not the bouncing logo.
        if (path == null || path.category != UpgradeCategory.SpeedMultiplier) return;
        if (path.tiers == null || targetIndex < 0 || targetIndex >= path.tiers.Count) return;

        Material mat = path.tiers[targetIndex].logoMaterial;
        if (mat == null) return;

        Renderer r = path.targetRenderer;
        if (r == null && path.targetMeshFilter1 != null)
        {
            r = path.targetMeshFilter1.GetComponent<Renderer>();
        }

        if (r != null)
        {
            // Only replace the material on the primary 'dvd' renderer (NOT 'dvd/Torus'),
            // so the logo preserves its distinct two-color design while both parts deform together!
            r.material = mat;
        }

        // Also update the primary 'dvd' renderer on any active split child logos
        for (int i = 0; i < DVDLogoBouncer.ActiveLogos.Count; i++)
        {
            DVDLogoBouncer logo = DVDLogoBouncer.ActiveLogos[i];
            if (logo != null && logo.isChildLogo)
            {
                Transform childDvd = logo.transform.Find("dvd");
                Renderer childRend = childDvd != null ? childDvd.GetComponent<Renderer>() : logo.GetComponent<Renderer>();
                if (childRend != null)
                {
                    childRend.material = mat;
                }
            }
        }
    }

    public void FinalizeEquip(UpgradePath path, int targetIndex)
    {
        int previousIndex = path.equippedIndex;
        path.equippedIndex = targetIndex;

        float newMultiplier = path.tiers[targetIndex].multiplierValue;

        // Revert previous booster if changed
        if (previousIndex >= 0 && previousIndex < path.tiers.Count && previousIndex != targetIndex)
        {
            BoosterObject prevB = GetTierBooster(path.tiers[previousIndex]);
            if (prevB != null) prevB.RevertBooster();
        }

        // Apply new booster
        BoosterObject newB = GetTierBooster(path.tiers[targetIndex]);
        if (newB != null) newB.ApplyBooster();

        // Update logo material if provided
        UpdateLogoMaterial(path, targetIndex);

        if (path.category == UpgradeCategory.MoneyMultiplier)
        {
            if (bouncer != null) bouncer.SetMoneyMultiplier(newMultiplier);
            ApplyWorldVisualsAnimated(path, previousIndex, targetIndex);
            if (path.targetVideoPlayer != null)
            {
                StartCoroutine(PlayVideoTransition(path, targetIndex));
            }
        }
        else if (path.category == UpgradeCategory.SpeedMultiplier)
        {
            if (bouncer != null) bouncer.SetSpeedMultiplier(newMultiplier);
            ApplyWorldVisualsAnimated(path, previousIndex, targetIndex);
        }
        else if (path.category == UpgradeCategory.RemoteUpgrade)
        {
            int newRemoteTier = Mathf.Clamp(targetIndex, 0, path.tiers.Count - 1);
            bool oldSupportsBumpers = (ActiveRemoteTier == 3 || ActiveRemoteTier == 8);
            bool newSupportsBumpers = (newRemoteTier == 3 || newRemoteTier == 8);

            if (oldSupportsBumpers && !newSupportsBumpers)
            {
                ScreenBumper.ClearAllBumpers();
            }
            ActiveRemoteTier = newRemoteTier;
            ApplyWorldVisualsAnimated(path, previousIndex, targetIndex);
        }
    }

    /// <summary>
    /// Called by TVScreenHazard when CRT static malfunction begins so DVD music pauses while static blasts.
    /// </summary>
    public void PauseDvdMusicForStatic()
    {
        UpgradePath dvdPath = GetPathByCategory(UpgradeCategory.MoneyMultiplier);
        if (dvdPath != null)
        {
            StopDvdMusicOnPathSources(dvdPath);
        }
    }

    /// <summary>
    /// Called by TVScreenHazard when the player slaps the TV to fix static malfunction,
    /// restoring the currently equipped DVD's video on bgvideo and resuming its music.
    /// </summary>
    public void ResumeCurrentDvdVideoAndMusic()
    {
        UpgradePath dvdPath = GetPathByCategory(UpgradeCategory.MoneyMultiplier);
        if (dvdPath == null || dvdPath.tiers == null || dvdPath.tiers.Count == 0) return;

        int equippedIdx = Mathf.Clamp(dvdPath.equippedIndex, 0, dvdPath.tiers.Count - 1);
        PlayEquippedDvdVideoImmediate(dvdPath, equippedIdx);
        PlayDvdMusicOnPathSources(dvdPath, dvdPath.tiers[equippedIdx].dvdMusic);
    }

    private void PlayEquippedDvdVideoImmediate(UpgradePath path, int targetIndex)
    {
        if (path == null || path.targetVideoPlayer == null || targetIndex < 0 || targetIndex >= path.tiers.Count)
            return;

        string videoName = path.tiers[targetIndex].dvdVideoFileName;
        bool hasSpecificVideo = false;
        if (!string.IsNullOrEmpty(videoName))
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, videoName);
#if UNITY_WEBGL && !UNITY_EDITOR
            bool exists = true;
#else
            bool exists = File.Exists(fullPath);
#endif
            if (exists)
            {
                path.targetVideoPlayer.source = VideoSource.Url;
                path.targetVideoPlayer.url = fullPath;
                path.targetVideoPlayer.isLooping = true;
                path.targetVideoPlayer.Play();
                hasSpecificVideo = true;
            }
        }

        if (!hasSpecificVideo && !string.IsNullOrEmpty(path.staticVideoFileName))
        {
            path.targetVideoPlayer.source = VideoSource.Url;
            path.targetVideoPlayer.url = Path.Combine(Application.streamingAssetsPath, path.staticVideoFileName);
            path.targetVideoPlayer.isLooping = true;
            path.targetVideoPlayer.Play();
        }
    }

    private void SnapWorldVisuals(UpgradePath path, int targetIndex)
    {
        for (int i = 0; i < path.tiers.Count; i++)
        {
            if (path.tiers[i].worldGameObject != null)
            {
                path.tiers[i].worldGameObject.SetActive(i == targetIndex);
            }

            BoosterObject b = GetTierBooster(path.tiers[i]);
            if (b != null)
            {
                if (i == targetIndex) b.ApplyBooster();
                else b.RevertBooster();
            }
        }

        UpdateLogoMaterial(path, targetIndex);

        if (path.category == UpgradeCategory.SpeedMultiplier)
        {
            if (path.targetMeshFilter1 != null && path.tiers[targetIndex].playerMesh1 != null)
                path.targetMeshFilter1.mesh = path.tiers[targetIndex].playerMesh1;

            if (path.targetMeshFilter2 != null && path.tiers[targetIndex].playerMesh2 != null)
                path.targetMeshFilter2.mesh = path.tiers[targetIndex].playerMesh2;
        }
    }

    private void ApplyWorldVisualsAnimated(UpgradePath path, int previousIndex, int targetIndex)
    {
        if (previousIndex >= 0 && previousIndex < path.tiers.Count && previousIndex != targetIndex && path.tiers[previousIndex].worldGameObject != null)
        {
            StartCoroutine(ShrinkAndDisable(path.tiers[previousIndex].worldGameObject));
        }

        if (path.tiers[targetIndex].worldGameObject != null)
        {
            path.tiers[targetIndex].worldGameObject.SetActive(true);
        }

        if (path.category == UpgradeCategory.SpeedMultiplier)
        {
            if (path.targetMeshFilter1 != null && path.tiers[targetIndex].playerMesh1 != null)
                path.targetMeshFilter1.mesh = path.tiers[targetIndex].playerMesh1;

            if (path.targetMeshFilter2 != null && path.tiers[targetIndex].playerMesh2 != null)
                path.targetMeshFilter2.mesh = path.tiers[targetIndex].playerMesh2;
        }
    }

    private IEnumerator ShrinkAndDisable(GameObject obj)
    {
        Vector3 startScale = obj.transform.localScale;
        float time = 0;
        while (time < worldModelShrinkSpeed)
        {
            float t = time / worldModelShrinkSpeed;
            obj.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            time += Time.deltaTime;
            yield return null;
        }
        obj.transform.localScale = Vector3.zero;
        obj.SetActive(false);
        obj.transform.localScale = startScale;
    }

    private IEnumerator PlayVideoTransition(UpgradePath path, int targetIndex)
    {
        StopDvdMusicOnPathSources(path);

        if (path.targetVideoPlayer != null && !string.IsNullOrEmpty(path.staticVideoFileName))
        {
            path.targetVideoPlayer.source = VideoSource.Url;
            path.targetVideoPlayer.url = Path.Combine(Application.streamingAssetsPath, path.staticVideoFileName);
            path.targetVideoPlayer.isLooping = true;
            path.targetVideoPlayer.Play();

            if (path.audioSource != null && path.staticSoundEffect != null)
            {
                path.audioSource.PlayOneShot(path.staticSoundEffect);
            }

            yield return new WaitForSeconds(path.staticDuration);
        }

        PlayEquippedDvdVideoImmediate(path, targetIndex);
        PlayDvdMusicOnPathSources(path, path.tiers[targetIndex].dvdMusic);
    }

    private void StopDvdMusicOnPathSources(UpgradePath path)
    {
        if (path == null || path.audioSource == null) return;
        path.audioSource.Stop();

        if (path.audioSource.transform.parent != null)
        {
            AudioSource[] siblingScreenSources = path.audioSource.transform.parent.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < siblingScreenSources.Length; i++)
            {
                if (siblingScreenSources[i] != null)
                {
                    siblingScreenSources[i].Stop();
                }
            }
        }
    }

    private void PlayDvdMusicOnPathSources(UpgradePath path, AudioClip clip)
    {
        if (path == null || path.audioSource == null || clip == null) return;

        path.audioSource.clip = clip;
        path.audioSource.loop = true;
        path.audioSource.Play();

        if (path.audioSource.transform.parent != null)
        {
            AudioSource[] siblingScreenSources = path.audioSource.transform.parent.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < siblingScreenSources.Length; i++)
            {
                AudioSource src = siblingScreenSources[i];
                if (src != null && src != path.audioSource)
                {
                    src.clip = clip;
                    src.loop = true;
                    src.Play();
                }
            }
        }
    }
}
