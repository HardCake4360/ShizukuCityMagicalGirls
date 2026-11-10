using UnityEngine;
using UnityEngine.Events;

public class NpcInteractable : InteractableBase
{
    [SerializeField] private UnityEvent onInteract;

    protected override void OnInteract()
    {
        onInteract?.Invoke();
    }
}
