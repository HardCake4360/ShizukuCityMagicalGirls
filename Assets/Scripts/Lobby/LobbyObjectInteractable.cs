using UnityEngine;
using UnityEngine.Events;

public class LobbyObjectInteractable : InteractableBase
{
    [SerializeField] private UnityEvent onInteract;

    protected override void OnInteract()
    {
        onInteract?.Invoke();
    }
}
