using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public sealed class UIAnimTransitionDefinition
{
    [SerializeField] private string id;
    [SerializeField] private bool fromAnyState;
    [SerializeField] private string fromStateId;
    [SerializeField] private string toStateId;
    [SerializeField] private bool immediate;
    [SerializeField] private bool allowReentry;
    [SerializeField] private UnityEvent onDispatched;

    public string Id => id;
    public bool FromAnyState => fromAnyState;
    public string FromStateId => fromStateId;
    public string ToStateId => toStateId;
    public bool Immediate => immediate;
    public bool AllowReentry => allowReentry;

    internal void NotifyDispatched()
    {
        onDispatched?.Invoke();
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class UIAnimator : MonoBehaviour
{
    private const int MaxChainedRequests = 32;

    private enum RequestKind
    {
        Transition,
        State
    }

    private readonly struct AnimationRequest
    {
        private AnimationRequest(
            RequestKind kind,
            int transitionIndex,
            string stateId,
            bool immediate
        )
        {
            Kind = kind;
            TransitionIndex = transitionIndex;
            StateId = stateId;
            Immediate = immediate;
        }

        public RequestKind Kind { get; }
        public int TransitionIndex { get; }
        public string StateId { get; }
        public bool Immediate { get; }

        public static AnimationRequest ForTransition(int transitionIndex)
        {
            return new AnimationRequest(
                RequestKind.Transition,
                transitionIndex,
                string.Empty,
                false
            );
        }

        public static AnimationRequest ForState(string stateId, bool immediate)
        {
            return new AnimationRequest(RequestKind.State, -1, stateId, immediate);
        }
    }

    [Header("State Source")]
    [SerializeField] private UIAnimManager manager;

    [Header("Initial State")]
    [SerializeField] private bool applyInitialStateOnStart;
    [SerializeField] private string initialStateId;
    [SerializeField] private bool initialStateIsImmediate = true;

    [Header("Transitions")]
    [SerializeField] private List<UIAnimTransitionDefinition> transitions = new();

    [Header("Events")]
    [SerializeField] private UIAnimStateChangedEvent onStateEntered;

    private string currentStateId = string.Empty;
    private bool isDispatching;
    private bool hasPendingRequest;
    private AnimationRequest pendingRequest;

    public UIAnimManager Manager => manager;
    public string CurrentStateId => currentStateId;
    public int CurrentStateIndex => manager != null
        ? manager.FindStateIndex(currentStateId)
        : -1;
    public bool IsDispatching => isDispatching;
    public IReadOnlyList<UIAnimTransitionDefinition> Transitions => transitions;

    private void Reset()
    {
        manager = GetComponent<UIAnimManager>();
        if (manager != null && manager.StateCount > 0)
        {
            initialStateId = manager.GetStateId(0);
            applyInitialStateOnStart = true;
        }
    }

    private void Awake()
    {
        ResolveManager();
    }

    private void Start()
    {
        if (applyInitialStateOnStart)
        {
            SetState(initialStateId, initialStateIsImmediate);
        }
    }

    public void Transition(int listIndex)
    {
        TryTransition(listIndex);
    }

    public bool TryTransition(int listIndex)
    {
        if (!IsValidTransitionIndex(listIndex))
        {
            Debug.LogError($"UI transition index {listIndex} is invalid.", this);
            return false;
        }

        ResolveManager();
        return ExecuteRequest(AnimationRequest.ForTransition(listIndex));
    }

    public void Transition(string transitionId)
    {
        TryTransition(transitionId);
    }

    public bool TryTransition(string transitionId)
    {
        int index = FindTransitionIndex(transitionId);
        if (index < 0)
        {
            Debug.LogError($"UI transition '{transitionId}' was not found.", this);
            return false;
        }
        return TryTransition(index);
    }

    public bool SetState(string stateId, bool immediate = false)
    {
        ResolveManager();
        if (manager == null || !manager.HasState(stateId))
        {
            Debug.LogError($"UI animation state '{stateId}' was not found.", this);
            return false;
        }

        return ExecuteRequest(AnimationRequest.ForState(stateId, immediate));
    }

    private bool ExecuteRequest(AnimationRequest request)
    {
        if (isDispatching)
        {
            pendingRequest = request;
            hasPendingRequest = true;
            return true;
        }

        bool firstResult = false;
        int handledRequestCount = 0;
        isDispatching = true;
        try
        {
            while (true)
            {
                hasPendingRequest = false;
                bool result = request.Kind == RequestKind.Transition
                    ? DispatchTransition(request.TransitionIndex)
                    : DispatchState(request.StateId, request.Immediate);
                if (handledRequestCount == 0)
                {
                    firstResult = result;
                }

                handledRequestCount++;
                if (!hasPendingRequest)
                {
                    break;
                }

                if (handledRequestCount >= MaxChainedRequests)
                {
                    Debug.LogError(
                        $"UI animation stopped after {MaxChainedRequests} chained requests. "
                        + "Check transition event callbacks for a request loop.",
                        this
                    );
                    break;
                }

                request = pendingRequest;
            }
        }
        finally
        {
            isDispatching = false;
            hasPendingRequest = false;
            pendingRequest = default;
        }

        return firstResult;
    }

    private bool DispatchState(string stateId, bool immediate)
    {
        string previousStateId = currentStateId;
        currentStateId = stateId;
        if (!manager.TryApplyState(stateId, immediate))
        {
            currentStateId = previousStateId;
            return false;
        }

        onStateEntered?.Invoke(currentStateId);
        return true;
    }

    public int FindTransitionIndex(string transitionId)
    {
        if (string.IsNullOrWhiteSpace(transitionId))
        {
            return -1;
        }

        for (int i = 0; i < transitions.Count; i++)
        {
            UIAnimTransitionDefinition transition = transitions[i];
            if (transition != null
                && string.Equals(transition.Id, transitionId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private bool DispatchTransition(int index)
    {
        UIAnimTransitionDefinition transition = transitions[index];
        if (manager == null)
        {
            Debug.LogError("UIAnimManager is not assigned.", this);
            return false;
        }

        if (!manager.HasState(transition.ToStateId))
        {
            Debug.LogError(
                $"Transition [{index}] targets missing state '{transition.ToStateId}'.",
                this
            );
            return false;
        }

        if (!transition.FromAnyState)
        {
            if (!manager.HasState(transition.FromStateId))
            {
                Debug.LogError(
                    $"Transition [{index}] starts from missing state "
                    + $"'{transition.FromStateId}'.",
                    this
                );
                return false;
            }

            if (!string.Equals(
                currentStateId,
                transition.FromStateId,
                StringComparison.Ordinal
            ))
            {
                return false;
            }
        }

        if (!transition.AllowReentry
            && string.Equals(currentStateId, transition.ToStateId, StringComparison.Ordinal))
        {
            return false;
        }

        string previousStateId = currentStateId;
        currentStateId = transition.ToStateId;
        if (!manager.TryApplyState(transition.ToStateId, transition.Immediate))
        {
            currentStateId = previousStateId;
            return false;
        }

        transition.NotifyDispatched();
        onStateEntered?.Invoke(currentStateId);
        return true;
    }

    private bool IsValidTransitionIndex(int index)
    {
        return index >= 0 && index < transitions.Count && transitions[index] != null;
    }

    private void ResolveManager()
    {
        if (manager == null)
        {
            manager = GetComponent<UIAnimManager>();
        }
    }

    private void OnValidate()
    {
        ResolveManager();

        if (applyInitialStateOnStart
            && manager != null
            && !string.IsNullOrWhiteSpace(initialStateId)
            && !manager.HasState(initialStateId))
        {
            Debug.LogWarning(
                $"Initial UI animation state '{initialStateId}' was not found.",
                this
            );
        }

        HashSet<string> transitionIds = new(StringComparer.Ordinal);
        for (int i = 0; i < transitions.Count; i++)
        {
            UIAnimTransitionDefinition transition = transitions[i];
            if (transition == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(transition.Id)
                && !transitionIds.Add(transition.Id))
            {
                Debug.LogWarning($"Duplicate UI transition id: '{transition.Id}'.", this);
            }

            if (manager != null
                && !string.IsNullOrWhiteSpace(transition.ToStateId)
                && !manager.HasState(transition.ToStateId))
            {
                Debug.LogWarning(
                    $"Transition [{i}] targets missing state '{transition.ToStateId}'.",
                    this
                );
            }

            if (manager != null
                && !transition.FromAnyState
                && !string.IsNullOrWhiteSpace(transition.FromStateId)
                && !manager.HasState(transition.FromStateId))
            {
                Debug.LogWarning(
                    $"Transition [{i}] starts from missing state "
                    + $"'{transition.FromStateId}'.",
                    this
                );
            }
        }
    }
}
