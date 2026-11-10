public interface IInteractable
{
    bool CanInteract { get; }

    int InteractionPriority { get; }

    void OnHoverEnter();

    void OnHoverExit();

    void Interact();
}
