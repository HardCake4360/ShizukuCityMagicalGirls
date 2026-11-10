using System;
using UnityEngine;
using UnityEngine.Events;

public sealed class SceneTransitionHook : MonoBehaviour
{
    [Header("Animation Calls")]
    [SerializeField] private UnityEvent onEnter = new UnityEvent();
    [SerializeField] private UnityEvent onLoop = new UnityEvent();
    [SerializeField] private UnityEvent onExit = new UnityEvent();

    [Header("Completion")]
    [SerializeField] private bool waitForEnterCompletion = true;
    [SerializeField] private bool waitForExitCompletion;
    [SerializeField, Min(0.1f)] private float completionTimeout = 10f;

    private Action enterCompleted;
    private Action exitCompleted;

    public bool WaitForEnterCompletion => waitForEnterCompletion;
    public bool WaitForExitCompletion => waitForExitCompletion;
    public float CompletionTimeout => Mathf.Max(0.1f, completionTimeout);

    internal void PlayEnter(Action completed)
    {
        enterCompleted = completed;
        onEnter.Invoke();

        if (!waitForEnterCompletion)
        {
            NotifyEnterComplete();
        }
    }

    internal void PlayLoop()
    {
        onLoop.Invoke();
    }

    internal void PlayExit(Action completed)
    {
        exitCompleted = completed;
        onExit.Invoke();

        if (!waitForExitCompletion)
        {
            NotifyExitComplete();
        }
    }

    // Call from an Animation Event at the end of the enter animation.
    public void NotifyEnterComplete()
    {
        Action callback = enterCompleted;
        enterCompleted = null;
        callback?.Invoke();
    }

    // Call from an Animation Event at the end of the optional exit animation.
    public void NotifyExitComplete()
    {
        Action callback = exitCompleted;
        exitCompleted = null;
        callback?.Invoke();
    }

    private void OnDisable()
    {
        enterCompleted = null;
        exitCompleted = null;
    }
}
