using UnityEngine;
using TMPro;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class DVDLogoBouncer : MonoBehaviour
{
    [Header("References")]
    public Camera renderCamera;
    public Transform displayQuad;
    public RenderTexture tvRenderTexture;
    private Collider logoCollider;

    [Header("Resolution Settings")]
    public int baseVerticalResolution = 1080;
    [Range(0.1f, 4f)] public float resolutionScale = 1f;

    [Header("Movement Settings")]
    public float baseSpeed = 2.5f;
    public float stateMultiplier = 1f; // Changed by DVD Player
    public Vector2 manualOffset = new Vector2(0f, 0f);

    [Header("Stacking Boost Settings")]
    public float speedAddedPerClick = 10f;
    public float boostRampUpTime = 0.2f;
    public float boostRampDownTime = 1.0f;

    private class BoostInstance { public float timer; }
    private List<BoostInstance> activeBoosts = new List<BoostInstance>();

    [Header("Anti-Loop & Bounce Settings")]
    public float bounceVariation = 0.15f;
    public float minAxisVelocity = 0.2f;

    [Header("Economy & UI")]
    public decimal edgeReward = 0.01m;
    public decimal cornerReward = 0.50m;
    public decimal currentMoney = 0m;
    public float moneyMultiplier = 1f; // Changed by DVD Pile

    public TMP_Text totalMoneyText;
    public Transform popupCanvasParent;
    public GameObject moneyPopupPrefab;
    [Tooltip("Prefab for when money is spent (should be styled red or with a minus sign)")]
    public GameObject negativeMoneyPopupPrefab;

    [Header("Hit Effects")]
    public GameObject edgeEffectTemplate;
    public GameObject cornerEffectTemplate;
    public float effectLifetime = 1.5f;

    private Vector2 direction;
    private int lastWidth = -1;
    private int lastHeight = -1;

    void Start()
    {
        if (renderCamera == null) renderCamera = Camera.main;
        logoCollider = GetComponent<Collider>();

        direction = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
        SyncCameraAndTextureAspect();
        UpdateMoneyUI();
    }

    void Update()
    {
        SyncCameraAndTextureAspect();
        MoveLogo();
    }

    private void SyncCameraAndTextureAspect()
    {
        if (displayQuad == null) return;

        float quadAspect = displayQuad.lossyScale.x / displayQuad.lossyScale.y;
        renderCamera.aspect = quadAspect;

        int targetHeight = Mathf.RoundToInt(baseVerticalResolution * resolutionScale);
        int targetWidth = Mathf.RoundToInt(targetHeight * quadAspect);

        if (tvRenderTexture != null && (tvRenderTexture.width != targetWidth || tvRenderTexture.height != targetHeight))
        {
            tvRenderTexture.Release();
            tvRenderTexture.width = targetWidth;
            tvRenderTexture.height = targetHeight;
            tvRenderTexture.Create();

            lastWidth = targetWidth;
            lastHeight = targetHeight;
        }
    }

    private void MoveLogo()
    {
        float totalBoostAdder = 0f;
        for (int i = activeBoosts.Count - 1; i >= 0; i--)
        {
            activeBoosts[i].timer += Time.deltaTime;
            float t = activeBoosts[i].timer;

            if (t <= boostRampUpTime)
            {
                totalBoostAdder += Mathf.Lerp(0f, speedAddedPerClick, t / boostRampUpTime);
            }
            else if (t <= boostRampUpTime + boostRampDownTime)
            {
                totalBoostAdder += Mathf.Lerp(speedAddedPerClick, 0f, (t - boostRampUpTime) / boostRampDownTime);
            }
            else
            {
                activeBoosts.RemoveAt(i);
            }
        }

        float activeSpeed = (baseSpeed * stateMultiplier) + totalBoostAdder;
        transform.position += (Vector3)(direction * activeSpeed * Time.deltaTime);

        CheckBounds();
    }

    private void CheckBounds()
    {
        if (!renderCamera.orthographic) return;
        Physics.SyncTransforms();

        Vector3 camPos = renderCamera.transform.position;
        float halfHeight = renderCamera.orthographicSize;
        float halfWidth = halfHeight * renderCamera.aspect;

        float camMaxX = camPos.x + halfWidth - manualOffset.x;
        float camMinX = camPos.x - halfWidth + manualOffset.x;
        float camMaxY = camPos.y + halfHeight - manualOffset.y;
        float camMinY = camPos.y - halfHeight + manualOffset.y;

        Bounds bounds = logoCollider.bounds;
        bool hitX = false;
        bool hitY = false;
        Vector3 correction = Vector3.zero;

        if (bounds.max.x >= camMaxX)
        {
            correction.x = camMaxX - bounds.max.x;
            direction.x = -Mathf.Abs(direction.x);
            hitX = true;
        }
        else if (bounds.min.x <= camMinX)
        {
            correction.x = camMinX - bounds.min.x;
            direction.x = Mathf.Abs(direction.x);
            hitX = true;
        }

        if (bounds.max.y >= camMaxY)
        {
            correction.y = camMaxY - bounds.max.y;
            direction.y = -Mathf.Abs(direction.y);
            hitY = true;
        }
        else if (bounds.min.y <= camMinY)
        {
            correction.y = camMinY - bounds.min.y;
            direction.y = Mathf.Abs(direction.y);
            hitY = true;
        }

        transform.position += correction;
        if (hitX || hitY) HandleBounce(hitX, hitY);
    }

    private void HandleBounce(bool hitX, bool hitY)
    {
        if (hitX) direction.y += Random.Range(-bounceVariation, bounceVariation);
        if (hitY) direction.x += Random.Range(-bounceVariation, bounceVariation);

        if (Mathf.Abs(direction.x) < minAxisVelocity) direction.x = Mathf.Sign(direction.x) * minAxisVelocity;
        if (Mathf.Abs(direction.y) < minAxisVelocity) direction.y = Mathf.Sign(direction.y) * minAxisVelocity;

        direction.Normalize();
        bool isCorner = hitX && hitY;

        if (isCorner)
        {
            decimal reward = cornerReward * (decimal)moneyMultiplier;
            currentMoney += reward;
            SpawnHitEffect(cornerEffectTemplate);
            SpawnPopup(reward, false);
        }
        else
        {
            decimal reward = edgeReward * (decimal)moneyMultiplier;
            currentMoney += reward;
            SpawnHitEffect(edgeEffectTemplate);
            SpawnPopup(reward, false);
        }

        UpdateMoneyUI();
    }

    private void SpawnHitEffect(GameObject template)
    {
        if (template == null) return;

        GameObject clone = Instantiate(template, template.transform.parent);
        clone.transform.localPosition = template.transform.localPosition;
        clone.transform.localRotation = template.transform.localRotation;
        clone.transform.localScale = template.transform.localScale;

        clone.SetActive(true);
        Destroy(clone, effectLifetime);
    }

    private void SpawnPopup(decimal amount, bool isNegative)
    {
        GameObject prefabToUse = isNegative ? negativeMoneyPopupPrefab : moneyPopupPrefab;
        if (prefabToUse == null || popupCanvasParent == null) return;

        GameObject popup = Instantiate(prefabToUse, popupCanvasParent);
        MoneyPopup popupScript = popup.GetComponent<MoneyPopup>();

        if (popupScript != null)
        {
            // Pass negative amount if it's a cost deduction
            popupScript.AnimatePopup(isNegative ? -amount : amount);
        }
    }

    public bool TrySpendMoney(decimal amount)
    {
        if (currentMoney >= amount)
        {
            currentMoney -= amount;
            UpdateMoneyUI();
            SpawnPopup(amount, true);
            return true;
        }
        return false;
    }

    private void UpdateMoneyUI()
    {
        if (totalMoneyText != null) totalMoneyText.text = $"${currentMoney:0.00}";
    }

    public void TriggerRemoteBoost()
    {
        activeBoosts.Add(new BoostInstance { timer = 0f });
    }

    public void SetSpeedMultiplier(float newMultiplier)
    {
        stateMultiplier = newMultiplier;
    }

    public void SetMoneyMultiplier(float newMultiplier)
    {
        moneyMultiplier = newMultiplier;
    }
}