using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class LobbyInteractionController : MonoBehaviour
{
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private LayerMask interactionLayers = ~0;
    [SerializeField, Min(1)] private int overlapBufferSize = 16;
    [SerializeField] private bool blockInteractionWhenPointerIsOverUi = true;

    [Header("Shared Hover Feedback")]
    [SerializeField] private SpriteOutlineSettings outlineSettings = new SpriteOutlineSettings();

    private Collider2D[] overlapResults;
    private ContactFilter2D contactFilter;
    private IInteractable hoveredInteractable;

    private void Awake()
    {
        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }

        overlapResults = new Collider2D[Mathf.Max(1, overlapBufferSize)];
        RebuildContactFilter();
    }

    private void OnValidate()
    {
        overlapBufferSize = Mathf.Max(1, overlapBufferSize);
        RebuildContactFilter();
    }

    private void Update()
    {
        IInteractable nextInteractable = FindInteractableUnderPointer();
        ChangeHoveredInteractable(nextInteractable);

        if (hoveredInteractable != null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            hoveredInteractable.Interact();
        }
    }

    private void OnDisable()
    {
        ChangeHoveredInteractable(null);
    }

    private IInteractable FindInteractableUnderPointer()
    {
        if (interactionCamera == null || Mouse.current == null || IsPointerOverUi())
        {
            return null;
        }

        Vector2 screenPosition = Mouse.current.position.ReadValue();
        Vector3 worldPosition = interactionCamera.ScreenToWorldPoint(screenPosition);
        int hitCount = Physics2D.OverlapPoint(worldPosition, contactFilter, overlapResults);

        IInteractable bestCandidate = null;
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = overlapResults[i];
            IInteractable candidate = hitCollider != null
                ? hitCollider.GetComponentInParent<IInteractable>()
                : null;

            if (candidate == null || !candidate.CanInteract)
            {
                continue;
            }

            if (bestCandidate == null || candidate.InteractionPriority > bestCandidate.InteractionPriority)
            {
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private bool IsPointerOverUi()
    {
        return blockInteractionWhenPointerIsOverUi
            && EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject();
    }

    private void ChangeHoveredInteractable(IInteractable nextInteractable)
    {
        if (ReferenceEquals(hoveredInteractable, nextInteractable))
        {
            return;
        }

        hoveredInteractable?.OnHoverExit();
        hoveredInteractable = nextInteractable;
        ApplyFeedbackSettings(hoveredInteractable);
        hoveredInteractable?.OnHoverEnter();
        CursorStateController.SetWorldInteractableHover(hoveredInteractable != null);
    }

    private void ApplyFeedbackSettings(IInteractable interactable)
    {
        if (interactable is IInteractableFeedbackSettingsReceiver receiver)
        {
            receiver.ApplyFeedbackSettings(outlineSettings);
        }
    }

    private void RebuildContactFilter()
    {
        contactFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = interactionLayers,
            useTriggers = true
        };
    }
}
