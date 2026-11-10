using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

public enum UIAnimPathType
{
    Linear,
    Curved
}

[System.Serializable]
public struct UIAnimPose
{
    public Vector3 anchoredPosition;
    public Vector3 localEulerAngles;
}

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class UIAnimElement : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const int MaxChainedCompletionNotifications = 32;

    [Header("Target")]
    [SerializeField] private RectTransform target;
    [SerializeField] private bool startActive;
    [SerializeField] private bool applyStartStateOnAwake = true;

    [Header("Poses")]
    [SerializeField] private bool useActivePose = true;
    [SerializeField] private UIAnimPose activePose;
    [SerializeField] private bool useInactivePose = true;
    [SerializeField] private UIAnimPose inactivePose;
    [SerializeField] private bool useHoveringPose;
    [SerializeField] private UIAnimPose hoveringPose;

    [Header("Transition")]
    [SerializeField] private UIAnimPathType pathType = UIAnimPathType.Linear;
    [SerializeField, Min(0f)] private float duration = 0.25f;
    [SerializeField] private AnimationCurve speedCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onActivated;
    [SerializeField] private UnityEvent onDeactivated;

    private Coroutine transitionCoroutine;
    private bool isActive;
    private bool desiredActiveState;
    private bool isStateTransitionPending;
    private bool isPointerHovering;
    private bool hasHoverReturnPose;
    private UIAnimPose hoverReturnPose;
    private bool hasAwakened;
    private bool isNotifyingCompletion;
    private bool hasPendingCompletion;
    private bool pendingCompletionState;

    public event Action<bool> StateTransitionStarted;
    public event Action<bool> StateTransitionCompleted;

    public bool IsActive => isActive;
    public bool DesiredActiveState => desiredActiveState;
    public bool IsStateTransitionPending => isStateTransitionPending;
    public bool IsTransitioning => transitionCoroutine != null;
    public RectTransform Target => target;
    public UIAnimPathType PathType => pathType;
    public bool IsPointerHovering => isPointerHovering;

    private void Reset()
    {
        target = GetComponent<RectTransform>();
        CaptureActivePose();
        inactivePose = activePose;
        hoveringPose = activePose;
    }

    private void Awake()
    {
        ResolveTarget();
        isActive = startActive;
        desiredActiveState = startActive;
        hasAwakened = true;
    }

    private void OnEnable()
    {
        if (hasAwakened && applyStartStateOnAwake)
        {
            if (TryGetStatePose(isActive, out UIAnimPose startPose))
            {
                ApplyPose(startPose);
            }
        }
    }

    private void OnDisable()
    {
        isPointerHovering = false;
        hasHoverReturnPose = false;
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }
        isActive = desiredActiveState;
        isStateTransitionPending = false;
    }

    public void Activate()
    {
        SetActiveState(true);
    }

    public void Deactivate()
    {
        SetActiveState(false);
    }

    public void Toggle()
    {
        SetActiveState(!desiredActiveState);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerHovering = true;
        if (!isActive && !isStateTransitionPending && useHoveringPose)
        {
            hoverReturnPose = CaptureCurrentPose();
            hasHoverReturnPose = true;
            StartPoseTransition(hoveringPose, false, false);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerHovering = false;
        if (!isActive && !isStateTransitionPending)
        {
            if (useInactivePose)
            {
                StartPoseTransition(inactivePose, false, false);
            }
            else if (hasHoverReturnPose)
            {
                StartPoseTransition(hoverReturnPose, false, false);
            }
        }
        hasHoverReturnPose = false;
    }

    public void SetActiveState(bool active)
    {
        SetActiveState(active, false);
    }

    public void SetActiveState(bool active, bool immediate)
    {
        ResolveTarget();
        desiredActiveState = active;
        isStateTransitionPending = true;
        hasHoverReturnPose = false;

        // Activation takes effect immediately, but deactivation is committed only
        // after the element has reached its inactive pose.
        if (active)
        {
            isActive = true;
        }
        StateTransitionStarted?.Invoke(active);

        if (!TryGetStatePose(active, out UIAnimPose destination))
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
                transitionCoroutine = null;
            }
            CompleteStateTransition(active);
            return;
        }
        StartPoseTransition(destination, active, true, immediate);
    }

    private bool TryGetStatePose(bool active, out UIAnimPose pose)
    {
        if (active)
        {
            pose = activePose;
            return useActivePose;
        }

        pose = inactivePose;
        return useInactivePose;
    }

    private void StartPoseTransition(
        UIAnimPose destination,
        bool activeAtCompletion,
        bool notifyStateCompletion,
        bool immediate = false
    )
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        if (!isActiveAndEnabled || immediate || duration <= 0f)
        {
            ApplyPose(destination);
            if (notifyStateCompletion)
            {
                CompleteStateTransition(activeAtCompletion);
            }
            return;
        }

        transitionCoroutine = StartCoroutine(
            TransitionTo(destination, activeAtCompletion, notifyStateCompletion)
        );
    }

    public void SnapToActive()
    {
        SetActiveState(true, true);
    }

    public void SnapToInactive()
    {
        SetActiveState(false, true);
    }

    [ContextMenu("Capture Current As Active Pose")]
    public void CaptureActivePose()
    {
        ResolveTarget();
        activePose = CaptureCurrentPose();
    }

    [ContextMenu("Capture Current As Inactive Pose")]
    public void CaptureInactivePose()
    {
        ResolveTarget();
        inactivePose = CaptureCurrentPose();
    }

    [ContextMenu("Capture Current As Hovering Pose")]
    public void CaptureHoveringPose()
    {
        ResolveTarget();
        hoveringPose = CaptureCurrentPose();
    }

    private IEnumerator TransitionTo(
        UIAnimPose destination,
        bool activeAtCompletion,
        bool notifyStateCompletion
    )
    {
        UIAnimPose origin = CaptureCurrentPose();
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float curvedProgress = speedCurve != null
                ? speedCurve.Evaluate(progress)
                : progress;
            ApplyInterpolatedPose(origin, destination, curvedProgress);
            yield return null;
        }

        ApplyPose(destination);
        transitionCoroutine = null;
        if (notifyStateCompletion)
        {
            CompleteStateTransition(activeAtCompletion);
        }
    }

    private void CompleteStateTransition(bool active)
    {
        isActive = active;
        desiredActiveState = active;
        isStateTransitionPending = false;
        NotifyCompleted(active);

        // PointerEnter may have happened while the element was moving toward the
        // inactive pose. Start hover feedback only after that move is complete.
        if (!isActive
            && !isStateTransitionPending
            && !desiredActiveState
            && isPointerHovering
            && useHoveringPose)
        {
            hoverReturnPose = CaptureCurrentPose();
            hasHoverReturnPose = true;
            StartPoseTransition(hoveringPose, false, false);
        }
    }

    private void ApplyInterpolatedPose(UIAnimPose from, UIAnimPose to, float progress)
    {
        // Curved is reserved for a future waypoint-based implementation. Until then,
        // both path modes use the same direct positional interpolation.
        target.anchoredPosition3D = Vector3.LerpUnclamped(
            from.anchoredPosition,
            to.anchoredPosition,
            progress
        );
        target.localRotation = Quaternion.SlerpUnclamped(
            Quaternion.Euler(from.localEulerAngles),
            Quaternion.Euler(to.localEulerAngles),
            progress
        );
    }

    private UIAnimPose CaptureCurrentPose()
    {
        return new UIAnimPose
        {
            anchoredPosition = target.anchoredPosition3D,
            localEulerAngles = target.localEulerAngles
        };
    }

    private void ApplyPose(UIAnimPose pose)
    {
        target.anchoredPosition3D = pose.anchoredPosition;
        target.localRotation = Quaternion.Euler(pose.localEulerAngles);
    }

    private void NotifyCompleted(bool active)
    {
        if (isNotifyingCompletion)
        {
            pendingCompletionState = active;
            hasPendingCompletion = true;
            return;
        }

        isNotifyingCompletion = true;
        int notificationCount = 0;
        bool stateToNotify = active;
        try
        {
            while (true)
            {
                hasPendingCompletion = false;
                StateTransitionCompleted?.Invoke(stateToNotify);
                if (stateToNotify)
                {
                    onActivated?.Invoke();
                }
                else
                {
                    onDeactivated?.Invoke();
                }

                notificationCount++;
                if (!hasPendingCompletion)
                {
                    break;
                }

                if (notificationCount >= MaxChainedCompletionNotifications)
                {
                    Debug.LogError(
                        $"UI animation element stopped after "
                        + $"{MaxChainedCompletionNotifications} chained completion events. "
                        + "Check Activated/Deactivated callbacks for an event loop.",
                        this
                    );
                    break;
                }

                stateToNotify = pendingCompletionState;
            }
        }
        finally
        {
            isNotifyingCompletion = false;
            hasPendingCompletion = false;
        }
    }

    private void ResolveTarget()
    {
        if (target == null)
        {
            target = GetComponent<RectTransform>();
        }
    }

    private void OnValidate()
    {
        ResolveTarget();
        duration = Mathf.Max(0f, duration);
    }
}
