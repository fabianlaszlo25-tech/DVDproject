using UnityEngine;
using System.Collections;

public class SurvivalAction : MonoBehaviour
{
    [Header("References")]
    public BarManager barManager;

    public enum ActionType { Eat, Toilet, Sleep, Clean }
    public ActionType actionType;

    [Header("Visuals")]
    public GameObject temporaryVisuals;
    public float actionDuration = 2.0f;

    private bool isActing = false;

    public void PerformAction()
    {
        if (!isActing && barManager != null)
        {
            StartCoroutine(ActionRoutine());
        }
    }

    private IEnumerator ActionRoutine()
    {
        isActing = true;

        switch (actionType)
        {
            case ActionType.Eat:
                barManager.TryEatFood();
                break;
            case ActionType.Toilet:
                barManager.UseToilet();
                break;
            case ActionType.Sleep:
                barManager.GoToSleep();
                break;
            case ActionType.Clean:
                barManager.CleanSelf();
                break;
        }

        if (temporaryVisuals != null) temporaryVisuals.SetActive(true);

        yield return new WaitForSeconds(actionDuration);

        if (temporaryVisuals != null) temporaryVisuals.SetActive(false);

        isActing = false;
    }
}