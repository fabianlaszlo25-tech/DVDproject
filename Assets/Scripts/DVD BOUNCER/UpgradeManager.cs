using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

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

    [Header("DVD Video Replacements")]
    public VideoClip dvdVideoClip;
    public AudioClip dvdMusic;

    [Header("Player Replacements")]
    public GameObject worldGameObject;
    public Mesh playerMesh1;
    public Mesh playerMesh2;
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
    public VideoClip staticVideoClip;
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
        foreach (var path in upgradePaths)
        {
            if (!path.unlockedIndices.Contains(0)) path.unlockedIndices.Add(0);

            path.equippedIndex = 0;
            SnapWorldVisuals(path, 0);

            if (path.category == UpgradeCategory.MoneyMultiplier)
            {
                if (path.targetVideoPlayer != null && path.tiers[0].dvdVideoClip != null)
                {
                    path.targetVideoPlayer.clip = path.tiers[0].dvdVideoClip;
                    path.targetVideoPlayer.Play();
                }

                if (path.audioSource != null && path.tiers[0].dvdMusic != null)
                {
                    path.audioSource.clip = path.tiers[0].dvdMusic;
                    path.audioSource.loop = true;
                    path.audioSource.Play();
                }
            }
        }
    }

    public bool TryPurchaseUpgrade(UpgradePath path, int targetIndex)
    {
        if (path.unlockedIndices.Contains(targetIndex)) return true;

        decimal cost = (decimal)path.tiers[targetIndex].cost;
        if (bouncer.TrySpendMoney(cost))
        {
            path.unlockedIndices.Add(targetIndex);
            return true;
        }
        return false;
    }

    public void FinalizeEquip(UpgradePath path, int targetIndex)
    {
        int previousIndex = path.equippedIndex;
        path.equippedIndex = targetIndex;

        float newMultiplier = path.tiers[targetIndex].multiplierValue;

        if (path.category == UpgradeCategory.MoneyMultiplier)
        {
            bouncer.SetMoneyMultiplier(newMultiplier);
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
        if (path.category == UpgradeCategory.SpeedMultiplier)
        {
            for (int i = 0; i < path.tiers.Count; i++)
            {
                if (path.tiers[i].worldGameObject != null)
                {
                    path.tiers[i].worldGameObject.SetActive(i == targetIndex);
                }
            }

            if (path.targetMeshFilter1 != null && path.tiers[targetIndex].playerMesh1 != null)
                path.targetMeshFilter1.mesh = path.tiers[targetIndex].playerMesh1;

            if (path.targetMeshFilter2 != null && path.tiers[targetIndex].playerMesh2 != null)
                path.targetMeshFilter2.mesh = path.tiers[targetIndex].playerMesh2;
        }
    }

    private void ApplyWorldVisualsAnimated(UpgradePath path, int previousIndex, int targetIndex)
    {
        if (path.category == UpgradeCategory.SpeedMultiplier)
        {
            if (previousIndex != targetIndex && path.tiers[previousIndex].worldGameObject != null)
            {
                StartCoroutine(ShrinkAndDisable(path.tiers[previousIndex].worldGameObject));
            }

            if (path.tiers[targetIndex].worldGameObject != null)
            {
                path.tiers[targetIndex].worldGameObject.SetActive(true);
            }

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
        if (path.audioSource != null)
        {
            path.audioSource.Stop();
        }

        if (path.staticVideoClip != null)
        {
            path.targetVideoPlayer.clip = path.staticVideoClip;
            path.targetVideoPlayer.Play();

            if (path.audioSource != null && path.staticSoundEffect != null)
            {
                path.audioSource.PlayOneShot(path.staticSoundEffect);
            }

            yield return new WaitForSeconds(path.staticDuration);
        }

        if (path.tiers[targetIndex].dvdVideoClip != null)
        {
            path.targetVideoPlayer.clip = path.tiers[targetIndex].dvdVideoClip;
            path.targetVideoPlayer.Play();
        }

        if (path.audioSource != null && path.tiers[targetIndex].dvdMusic != null)
        {
            path.audioSource.clip = path.tiers[targetIndex].dvdMusic;
            path.audioSource.loop = true;
            path.audioSource.Play();
        }
    }
}