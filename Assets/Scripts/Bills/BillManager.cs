using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BillManager : MonoBehaviour
{
    public static BillManager Instance { get; private set; }

    [Header("Bill Objects (5 Cubes on Wall)")]
    [Tooltip("Drag the 5 bill cubes located on the wall beside the front door here.")]
    public BillObject[] billObjects;

    [Header("References")]
    public DVDLogoBouncer economyBouncer;
    public BarManager barManager;

    [Header("Penalty Multipliers")]
    [Tooltip("Food cost multiplier when FoodMoreExpensive bill is unpaid (e.g. 2.0 = 2x price).")]
    public float foodBillMultiplier = 2.0f;
    [Tooltip("DVD logo speed multiplier when DvdBouncesSlower bill is unpaid (e.g. 0.5 = 50% speed).")]
    public float electricSpeedMultiplier = 0.5f;

    [Header("Daily Tracking State")]
    public int activeBillIndex = -1;
    public bool isPaid = false;
    private List<int> localHistory = new List<int>();

    public bool HasUnpaidBill => activeBillIndex >= 0 && !isPaid;

    public bool IsFoodMoreExpensive =>
        HasUnpaidBill && billObjects != null && activeBillIndex >= 0 && activeBillIndex < billObjects.Length
        && billObjects[activeBillIndex] != null && billObjects[activeBillIndex].billType == BillType.FoodMoreExpensive;

    public bool IsElectricSlowdown =>
        HasUnpaidBill && billObjects != null && activeBillIndex >= 0 && activeBillIndex < billObjects.Length
        && billObjects[activeBillIndex] != null && billObjects[activeBillIndex].billType == BillType.DvdBouncesSlower;

    public bool IsToiletBlocked =>
        HasUnpaidBill && billObjects != null && activeBillIndex >= 0 && activeBillIndex < billObjects.Length
        && billObjects[activeBillIndex] != null && billObjects[activeBillIndex].billType == BillType.ToiletBroken;

    public bool IsShowerBlocked =>
        HasUnpaidBill && billObjects != null && activeBillIndex >= 0 && activeBillIndex < billObjects.Length
        && billObjects[activeBillIndex] != null && billObjects[activeBillIndex].billType == BillType.ShowerBroken;

    public bool IsBarBlocked =>
        HasUnpaidBill && billObjects != null && activeBillIndex >= 0 && activeBillIndex < billObjects.Length
        && billObjects[activeBillIndex] != null && billObjects[activeBillIndex].billType == BillType.CarPayment;

    public float FoodBillMultiplier => foodBillMultiplier;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Instance = this;
    }

    private void Start()
    {
        if (economyBouncer == null) economyBouncer = FindObjectOfType<DVDLogoBouncer>();
        if (barManager == null) barManager = FindObjectOfType<BarManager>();

        InitializeDailyBill();
    }

    private void InitializeDailyBill()
    {
        if (billObjects == null || billObjects.Length == 0) return;

        bool isNewMorning = true;
        if (RunSaveManager.HasInstance)
        {
            // If we have an active bill and haven't slept yet (e.g. returning from Bar on the same day)
            if (RunSaveManager.Instance.currentActiveBillIndex >= 0 && !RunSaveManager.Instance.isCurrentBillPaid)
            {
                // Preserve current bill state
                isNewMorning = false;
                activeBillIndex = RunSaveManager.Instance.currentActiveBillIndex;
                isPaid = RunSaveManager.Instance.isCurrentBillPaid;
            }
            else if (RunSaveManager.Instance.currentActiveBillIndex >= 0 && RunSaveManager.Instance.isCurrentBillPaid)
            {
                // If paid today and returning to room on the same day without sleeping
                // Check if days survived changed or if it was marked as a new day
                isNewMorning = false;
                activeBillIndex = RunSaveManager.Instance.currentActiveBillIndex;
                isPaid = true;
            }
        }

        if (isNewMorning)
        {
            SelectNewDailyBill();
        }

        ApplyBillStateToWorld();
    }

    private void SelectNewDailyBill()
    {
        List<int> history = RunSaveManager.HasInstance ? RunSaveManager.Instance.billHistory : localHistory;

        // 2-day cooldown rule: "cant be the same consequtively +1 so 2 days have to pass before its in the random list again"
        List<int> eligible = new List<int>();
        for (int i = 0; i < billObjects.Length; i++)
        {
            if (!history.Contains(i))
            {
                eligible.Add(i);
            }
        }

        // Safeguard if pool is somehow empty
        if (eligible.Count == 0)
        {
            for (int i = 0; i < billObjects.Length; i++) eligible.Add(i);
        }

        int chosen = eligible[Random.Range(0, eligible.Count)];
        activeBillIndex = chosen;
        isPaid = false;

        history.Add(chosen);
        while (history.Count > 2)
        {
            history.RemoveAt(0); // Only keep the last 2 days on cooldown
        }

        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.currentActiveBillIndex = activeBillIndex;
            RunSaveManager.Instance.isCurrentBillPaid = false;
        }
    }

    public void ApplyBillStateToWorld()
    {
        for (int i = 0; i < billObjects.Length; i++)
        {
            if (billObjects[i] == null) continue;

            if (i == activeBillIndex && !isPaid)
            {
                billObjects[i].gameObject.SetActive(true);
            }
            else
            {
                billObjects[i].gameObject.SetActive(false);
            }
        }

        if (economyBouncer != null)
        {
            economyBouncer.billSpeedMultiplier = IsElectricSlowdown ? electricSpeedMultiplier : 1.0f;
        }
    }

    public void MarkBillPaid(BillType type)
    {
        isPaid = true;
        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.isCurrentBillPaid = true;
        }

        if (economyBouncer != null)
        {
            economyBouncer.billSpeedMultiplier = 1.0f;
        }

        for (int i = 0; i < billObjects.Length; i++)
        {
            if (billObjects[i] != null && billObjects[i].billType == type)
            {
                billObjects[i].gameObject.SetActive(false);
            }
        }
    }

    private void Update()
    {
        if (economyBouncer != null)
        {
            economyBouncer.billSpeedMultiplier = IsElectricSlowdown ? electricSpeedMultiplier : 1.0f;
        }
    }

    // --- DENIAL FEEDBACK HELPERS ---

    public void ShowBedDenyFeedback(GameObject bedObject)
    {
        if (bedObject == null) return;
        InteractableObject io = bedObject.GetComponent<InteractableObject>();
        if (io != null)
        {
            StartCoroutine(ShowTemporaryFeedbackRoutine(io, "Can't sleep with an unpaid bill on the door!", 2.0f));
        }
    }

    public void ShowBarDenyFeedback(GameObject doorObject)
    {
        if (doorObject == null) return;
        InteractableObject io = doorObject.GetComponent<InteractableObject>();
        if (io != null)
        {
            StartCoroutine(ShowTemporaryFeedbackRoutine(io, "Car impounded! Pay your car note first.", 2.0f));
        }
    }

    public void ShowToiletDenyFeedback(GameObject toiletObject)
    {
        if (toiletObject == null) return;
        InteractableObject io = toiletObject.GetComponent<InteractableObject>();
        if (io != null)
        {
            StartCoroutine(ShowTemporaryFeedbackRoutine(io, "Water is shut off! Pay your water bill first.", 2.0f));
        }
    }

    public void ShowShowerDenyFeedback(GameObject showerObject)
    {
        if (showerObject == null) return;
        InteractableObject io = showerObject.GetComponent<InteractableObject>();
        if (io != null)
        {
            StartCoroutine(ShowTemporaryFeedbackRoutine(io, "Hot water is shut off! Pay your sanitation bill first.", 2.0f));
        }
    }

    private IEnumerator ShowTemporaryFeedbackRoutine(InteractableObject io, string message, float duration)
    {
        string originalText = io.hoverText;
        io.hoverText = message;
        yield return new WaitForSeconds(duration);
        if (io.hoverText == message)
        {
            io.hoverText = originalText;
        }
    }
}
