using UnityEngine;

public abstract class InteractableBase : MonoBehaviour, IInteractable, IInteractableFeedbackSettingsReceiver
{
    [Header("Interaction")]
    [SerializeField] private bool interactionEnabled = true;
    [SerializeField] private int interactionPriority;

    [Header("Hover Feedback")]
    [SerializeField] private SpriteOutlineFeedback outlineFeedback;

    private bool isHovered;

    public virtual bool CanInteract => interactionEnabled && isActiveAndEnabled;

    public int InteractionPriority => interactionPriority;

    public bool IsHovered => isHovered;

    protected virtual void Awake()
    {
        if (outlineFeedback == null)
        {
            outlineFeedback = GetComponent<SpriteOutlineFeedback>();
        }
    }

    protected virtual void OnDisable()
    {
        SetHovered(false);
    }

    public void OnHoverEnter()
    {
        if (!CanInteract)
        {
            return;
        }

        SetHovered(true);
    }

    public void OnHoverExit()
    {
        SetHovered(false);
    }

    public void Interact()
    {
        if (!CanInteract)
        {
            return;
        }

        OnInteract();
    }

    public void SetInteractionEnabled(bool enabled)
    {
        interactionEnabled = enabled;
        if (!enabled)
        {
            SetHovered(false);
        }
    }

    public void ApplyFeedbackSettings(SpriteOutlineSettings settings)
    {
        if (outlineFeedback == null)
        {
            outlineFeedback = GetComponent<SpriteOutlineFeedback>();
        }

        outlineFeedback?.Configure(settings);
    }

    protected abstract void OnInteract();

    protected virtual void OnHoverChanged(bool hovered)
    {
        if (outlineFeedback != null)
        {
            outlineFeedback.SetHighlighted(hovered);
        }
    }

    private void SetHovered(bool hovered)
    {
        if (isHovered == hovered)
        {
            return;
        }

        isHovered = hovered;
        OnHoverChanged(hovered);
    }
}
