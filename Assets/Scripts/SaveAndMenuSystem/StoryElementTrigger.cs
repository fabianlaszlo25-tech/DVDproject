using UnityEngine;

/// <summary>
/// Attach to any story-related prop, trigger zone, or InteractableObject to record
/// "Story Elements Encountered This Run" in <see cref="RunSaveManager"/>.
/// Can be triggered automatically via InteractableObject.onClick, OnTriggerEnter, or via TriggerStoryElement().
/// </summary>
public class StoryElementTrigger : MonoBehaviour
{
    [Header("Story Element Identification")]
    [Tooltip("Unique ID for this story element so it is only counted once per run if onlyCountOncePerRun is enabled.")]
    public string storyElementId = "story_01";
    public bool onlyCountOncePerRun = true;

    [Header("Automatic Hooks")]
    [Tooltip("If true and an InteractableObject is on this GameObject, clicking it automatically records the story element.")]
    public bool hookInteractableOnClick = true;

    private InteractableObject interactable;
    private bool hasTriggeredLocally;

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(storyElementId))
        {
            storyElementId = RunSaveManager.GetHierarchyPath(transform);
        }

        if (hookInteractableOnClick)
        {
            interactable = GetComponent<InteractableObject>();
            if (interactable != null)
            {
                interactable.onClick.AddListener(TriggerStoryElement);
            }
        }
    }

    private void OnDestroy()
    {
        if (interactable != null)
        {
            interactable.onClick.RemoveListener(TriggerStoryElement);
        }
    }

    [ContextMenu("Trigger Story Element Now")]
    public void TriggerStoryElement()
    {
        if (onlyCountOncePerRun && hasTriggeredLocally) return;
        hasTriggeredLocally = true;

        RunSaveManager saveManager = RunSaveManager.EnsureExists();
        if (saveManager != null)
        {
            saveManager.RecordStoryElementEncountered(onlyCountOncePerRun ? storyElementId : string.Empty);
        }
    }
}
