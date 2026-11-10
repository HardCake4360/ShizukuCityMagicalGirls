using UnityEngine;
using UnityEngine.Events;

public class PlayableCharacterInteractable : InteractableBase
{
    [SerializeField] private UnityEvent onInteract;

    protected override void OnInteract()
    {
        onInteract?.Invoke();
    }
}
