using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public enum UpgradeCategory { MoneyMultiplier, SpeedMultiplier }

[System.Serializable]
public class UpgradeTier
{
    public string tierName;
    public float cost;
    public float multiplierValue;
    public GameObject previewPrefab;
    public Vector3 previewPositionOffset = Vector3.zero;
    public Vector3 previewScale = new Vector3(50f, 50f, 50f);
    public Vector3 previewRotation = Vector3.zero;

    [Header("DVD Video Replacements (WebGL)")]
    [Tooltip("Type the exact file name including extension, e.g., 'dvd1.mp4'")]
    public string dvdVideoFileName;
    public AudioClip dvdMusic;

    [Header("Player Replacements")]
    public GameObject worldGameObject;
    public Mesh playerMesh1;
    public Mesh playerMesh2;

    [Header("Visual & Booster Upgrades")]
    public Material logoMaterial;
    public BoosterObject booster;
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

    public List<UpgradeTier> tiers;
    public int equippedIndex = 0;
    public List<int> unlockedIndices = new List<int>();
}

public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    public DVDLogoBouncer bouncer;
    public List<UpgradePath> upgradePaths;

    public float worldModelShrinkSpeed = 0.5f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
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

            if (path.category == UpgradeCategory.MoneyMultiplier)
            {
                if (path.targetVideoPlayer != null && !string.IsNullOrEmpty(path.tiers[equippedIdx].dvdVideoFileName))
                {
                    path.targetVideoPlayer.source = VideoSource.Url;
                    path.targetVideoPlayer.url = Path.Combine(Application.streamingAssetsPath, path.tiers[equippedIdx].dvdVideoFileName);
                    path.targetVideoPlayer.Play();
                }

                PlayDvdMusicOnPathSources(path, path.tiers[equippedIdx].dvdMusic);
            }
        }
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
        if (bouncer.TrySpendMoney(cost))
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
            r.material = mat;
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
            bouncer.SetMoneyMultiplier(newMultiplier);
            ApplyWorldVisualsAnimated(path, previousIndex, targetIndex);
            if (path.targetVideoPlayer != null)
            {
                StartCoroutine(PlayVideoTransition(path, targetIndex));
            }
        }
        else if (path.category == UpgradeCategory.SpeedMultiplier)
        {
            bouncer.SetSpeedMultiplier(newMultiplier);
            ApplyWorldVisualsAnimated(path, previousIndex, targetIndex);
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

        string videoName = path.tiers[targetIndex].dvdVideoFileName;
        bool hasSpecificVideo = false;
        if (!string.IsNullOrEmpty(videoName))
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, videoName);
            if (File.Exists(fullPath))
            {
                path.targetVideoPlayer.source = VideoSource.Url;
                path.targetVideoPlayer.url = fullPath;
                path.targetVideoPlayer.isLooping = true;
                path.targetVideoPlayer.Play();
                hasSpecificVideo = true;
            }
        }

        // If no video clip is ready for this DVD yet, ensure the background video stays on static!
        if (!hasSpecificVideo && path.targetVideoPlayer != null && !string.IsNullOrEmpty(path.staticVideoFileName))
        {
            path.targetVideoPlayer.source = VideoSource.Url;
            path.targetVideoPlayer.url = Path.Combine(Application.streamingAssetsPath, path.staticVideoFileName);
            path.targetVideoPlayer.isLooping = true;
            path.targetVideoPlayer.Play();
        }

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
