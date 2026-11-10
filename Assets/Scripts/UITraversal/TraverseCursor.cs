using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public enum TraverseCursorHideMode
{
    CanvasGroup,
    MoveOffscreen,
    CanvasGroupAndMoveOffscreen
}

public enum TraverseCursorVisualState
{
    Idle,
    Selected
}

[Serializable]
public sealed class TraverseOrder
{
    [Tooltip("이 순서를 방향키/패드 순회에 사용합니다. 꺼져 있어도 목록의 타겟은 마우스 호버에 반응합니다.")]
    [SerializeField] private bool use = true;
    [SerializeField] private List<TraverseTarget> targets = new List<TraverseTarget>();

    public bool Use => use;
    public IReadOnlyList<TraverseTarget> Targets => targets;
}

[Serializable]
public sealed class TraverseTargetEvent : UnityEvent<TraverseTarget>
{
}

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public sealed class TraverseCursor : MonoBehaviour
{
    private enum NavigateDirection
    {
        None,
        Left,
        Right,
        Up,
        Down
    }

    [Header("Cursor")]
    [SerializeField] private RectTransform cursorTransform;
    [SerializeField] private CanvasGroup cursorCanvasGroup;
    [SerializeField] private bool startActive = true;
    [SerializeField] private bool selectFirstTargetOnActivate = true;
    [SerializeField] private bool syncEventSystemSelection = true;

    [Header("Follow UIAnimElement")]
    [Tooltip("할당하면 이 UIAnimElement의 활성/비활성 상태를 커서가 따라갑니다.")]
    [SerializeField] private UIAnimElement followActiveStateOf;
    [Tooltip("활성화할 때 UIAnimElement의 이동 완료를 기다립니다. 끄면 둘이 동시에 활성화됩니다. 비활성화는 이 설정과 관계없이 항상 동시에 시작됩니다.")]
    [SerializeField] private bool activateAfterFollowedAnimation = true;

    [Header("Traversal Order")]
    [SerializeField] private TraverseOrder horizontalOrder = new TraverseOrder();
    [SerializeField] private TraverseOrder verticalOrder = new TraverseOrder();
    [SerializeField] private bool wrapAround = true;

    [Header("Input")]
    [Tooltip("보통 InputSystem_Actions/UI/Navigate를 연결합니다. 비어 있으면 방향키, WASD, 게임패드용 입력을 자동 생성합니다.")]
    [SerializeField] private InputActionReference navigateAction;
    [Tooltip("CommonUI/Select를 연결합니다. 비어 있으면 Navigate와 같은 에셋에서 자동으로 찾습니다.")]
    [SerializeField] private InputActionReference selectAction;
    [Tooltip("CommonUI/Cancel을 연결합니다. 비어 있으면 Navigate와 같은 에셋에서 자동으로 찾습니다.")]
    [SerializeField] private InputActionReference cancelAction;
    [SerializeField, Range(0.01f, 1f)] private float inputDeadZone = 0.5f;
    [SerializeField, Min(0f)] private float repeatDelay = 0.4f;
    [SerializeField, Min(0.01f)] private float repeatInterval = 0.1f;
    [SerializeField] private bool useUnscaledInputTime = true;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveDuration = 0.15f;
    [SerializeField] private AnimationCurve moveCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledMovementTime = true;

    [Header("Visual State")]
    [SerializeField] private TraverseCursorVisualState startVisualState =
        TraverseCursorVisualState.Idle;
    [SerializeField, Min(1f)] private float idlePulseScaleMultiplier = 1.1f;
    [Tooltip("Idle 상태에서 커졌다가 원래 크기로 돌아오는 한 주기의 시간입니다.")]
    [SerializeField, Min(0f)] private float idlePulseDuration = 1f;
    [SerializeField] private AnimationCurve idlePulseCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Min(1f)] private float selectedScaleMultiplier = 1.5f;
    [SerializeField, Min(0f)] private float selectedReturnDuration = 0.25f;
    [SerializeField] private AnimationCurve selectedReturnCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledVisualTime = true;

    [Header("Selection Focus")]
    [SerializeField] private bool dimUnselectedTargets = true;
    [SerializeField, Range(0f, 1f)] private float unselectedTargetAlpha = 0.4f;
    [SerializeField, Min(0f)] private float dimTransitionDuration = 0.2f;
    [SerializeField] private AnimationCurve dimTransitionCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledDimmingTime = true;

    [Header("Visibility")]
    [SerializeField] private TraverseCursorHideMode hideMode =
        TraverseCursorHideMode.CanvasGroup;
    [SerializeField] private Vector3 hiddenAnchoredPosition = new Vector3(-10000f, -10000f, 0f);

    [Header("Debug")]
    [Tooltip("이 커서 컴포넌트가 활성화된 동안 모든 캐릭터의 해금 제한을 우회합니다. 선택과 출격에 적용되며 실제 도전과제/해금 저장 데이터는 변경하지 않습니다.")]
    [SerializeField] private bool debugUnlockAllCharacters;

    [Header("Events")]
    [SerializeField] private TraverseTargetEvent onTargetChanged;
    [SerializeField] private TraverseTargetEvent onTargetSelected;
    [SerializeField] private TraverseTargetEvent onSelectionCanceled;
    [SerializeField] private UnityEvent onActivated;
    [SerializeField] private UnityEvent onDeactivated;

    private readonly HashSet<TraverseTarget> subscribedTargets =
        new HashSet<TraverseTarget>();

    private TraverseTarget currentTarget;
    private TraverseTarget selectedTarget;
    private Coroutine movementCoroutine;
    private Coroutine visualStateCoroutine;
    private InputAction fallbackNavigateAction;
    private bool enabledReferencedAction;
    private InputAction activeSelectAction;
    private InputAction activeCancelAction;
    private bool enabledSelectAction;
    private bool enabledCancelAction;
    private bool isTraversalActive;
    private bool isInitialized;
    private NavigateDirection heldDirection;
    private float nextRepeatTime;
    private bool eventSystemSelectionPending;
    private UIAnimElement subscribedFollowedElement;
    private TraverseCursorVisualState currentVisualState;
    private Vector3 baseLocalScale = Vector3.one;
    private bool isVisualStateInitialized;
    private bool hasDebugUnlockOverride;
    private Transform trackedCursorAnchor;
    private Vector3 trackedCursorOffset;

    public event Action<TraverseTarget> CurrentTargetChanged;

    public bool IsActive => isTraversalActive;
    public bool IsMoving => movementCoroutine != null;
    public TraverseTarget CurrentTarget => currentTarget;
    public TraverseTarget SelectedTarget => selectedTarget;
    public bool IsSelected => currentVisualState == TraverseCursorVisualState.Selected;
    public TraverseCursorVisualState CurrentVisualState => currentVisualState;

    private void Reset()
    {
        cursorTransform = GetComponent<RectTransform>();
        cursorCanvasGroup = GetComponent<CanvasGroup>();
        baseLocalScale = cursorTransform.localScale;
        ConfigureCanvasGroup();
    }

    private void Awake()
    {
        ResolveReferences();
        baseLocalScale = cursorTransform.localScale;
        currentVisualState = startVisualState;
        isTraversalActive = startActive;
        isInitialized = true;
    }

    private void OnEnable()
    {
        SynchronizeDebugUnlockOverride();
        ResolveReferences();
        RefreshTargets();
        EnableInput();
        SubscribeToFollowedElement();

        if (!isInitialized)
        {
            isTraversalActive = startActive;
            isInitialized = true;
        }

        if (followActiveStateOf != null)
        {
            SynchronizeWithFollowedElement(true);
        }
        else if (isTraversalActive)
        {
            RestoreOrSelectTarget(true);
        }
        else
        {
            ApplyVisibility(false);
        }

        if (isTraversalActive)
        {
            ApplyCurrentVisualState(!isVisualStateInitialized);
            ApplySelectionDimming();
        }
    }

    private void OnDisable()
    {
        hasDebugUnlockOverride = false;
        CharacterUnlockProgress.SetDebugUnlockOverride(this, false);
        StopMovement();
        StopVisualStateAnimation(true);
        RestoreAllTargetOpacity(true);
        DisableInput();
        UnsubscribeAllTargets();
        UnsubscribeFromFollowedElement();
        ResetHeldInput();
        eventSystemSelectionPending = false;
        ApplyVisibility(false);
    }

    private void OnDestroy()
    {
        CharacterUnlockProgress.SetDebugUnlockOverride(this, false);
        fallbackNavigateAction?.Dispose();
        fallbackNavigateAction = null;
    }

    private void Update()
    {
        SynchronizeDebugUnlockOverride();
        if (!isTraversalActive)
        {
            ResetHeldInput();
            return;
        }

        if (IsSelected && !IsAvailable(selectedTarget))
        {
            CancelSelection();
        }

        if (currentTarget != null && !currentTarget.IsTraversable)
        {
            SetCurrentTarget(FindFirstAvailableTarget(), false);
        }

        ProcessNavigationInput();
    }

    private void LateUpdate()
    {
        if (eventSystemSelectionPending)
        {
            eventSystemSelectionPending = false;
            SyncEventSystemSelection();
        }

        if (isTraversalActive && IsAvailable(currentTarget))
        {
            // Inspector/script edits are applied outside OnValidate to avoid native UI writes
            // during serialization. An edited pose snaps immediately, even mid-transition/paused.
            bool poseEdited = trackedCursorAnchor != currentTarget.CursorAnchor
                || !trackedCursorOffset.Equals(currentTarget.CursorOffset);
            if (poseEdited)
            {
                StopMovement();
                TrackCurrentCursorPose();
            }
            if (movementCoroutine == null)
                cursorTransform.position = currentTarget.CursorWorldPosition;
        }
    }

    private void TrackCurrentCursorPose()
    {
        trackedCursorAnchor = currentTarget.CursorAnchor;
        trackedCursorOffset = currentTarget.CursorOffset;
    }

    public void SetDebugUnlockAllCharacters(bool enabled)
    {
        debugUnlockAllCharacters = enabled;
        SynchronizeDebugUnlockOverride();
    }

    private void SynchronizeDebugUnlockOverride()
    {
        bool requested = debugUnlockAllCharacters && isActiveAndEnabled;
        if (hasDebugUnlockOverride == requested) return;
        hasDebugUnlockOverride = requested;
        CharacterUnlockProgress.SetDebugUnlockOverride(this, requested);
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
        SetActiveState(!isTraversalActive);
    }

    public void SetActiveState(bool active)
    {
        if (isTraversalActive == active)
        {
            if (active)
            {
                RestoreOrSelectTarget(false);
                EnsureVisualStateAnimation();
            }
            else
            {
                StopMovement();
                StopVisualStateAnimation(true);
                RestoreAllTargetOpacity();
                ApplyVisibility(false);
            }
            return;
        }

        isTraversalActive = active;
        ResetHeldInput();

        if (active)
        {
            RefreshTargets();
            RestoreOrSelectTarget(false);
            ApplyCurrentVisualState(!isVisualStateInitialized);
            ApplySelectionDimming();
            onActivated?.Invoke();
        }
        else
        {
            StopMovement();
            StopVisualStateAnimation(true);
            RestoreAllTargetOpacity();
            eventSystemSelectionPending = false;
            ApplyVisibility(false);
            onDeactivated?.Invoke();
        }
    }

    public void SetTarget(TraverseTarget target)
    {
        if (!isTraversalActive || IsSelected || !IsAvailable(target))
        {
            return;
        }

        SetCurrentTarget(target, false);
    }

    public void SetTargetImmediate(TraverseTarget target)
    {
        if (!isTraversalActive || IsSelected || !IsAvailable(target))
        {
            return;
        }

        SetCurrentTarget(target, true);
    }

    public void ClearTarget()
    {
        if (IsSelected)
        {
            CancelSelection();
        }

        StopMovement();
        currentTarget = null;
        eventSystemSelectionPending = false;
        ApplyVisibility(false);
        CurrentTargetChanged?.Invoke(null);
        onTargetChanged?.Invoke(null);
    }

    public void SetIdle()
    {
        CancelSelection();
    }

    public void SetSelected()
    {
        SelectCurrentTarget();
    }

    public void SetVisualState(TraverseCursorVisualState state)
    {
        if (state == TraverseCursorVisualState.Selected)
        {
            SelectCurrentTarget();
        }
        else
        {
            CancelSelection();
        }
    }

    public void SelectCurrentTarget()
    {
        TrySelectTarget(currentTarget);
    }

    public void SelectTarget(TraverseTarget target)
    {
        TrySelectTarget(target);
    }

    public void ToggleTargetSelection(TraverseTarget target)
    {
        if (IsSelected)
        {
            if (target == selectedTarget)
            {
                CancelSelection();
            }
            return;
        }

        TrySelectTarget(target);
    }

    public void CancelSelection()
    {
        if (!IsSelected)
        {
            return;
        }

        TraverseTarget canceledTarget = selectedTarget;
        EnterIdleState();
        onSelectionCanceled?.Invoke(canceledTarget);
    }

    private bool TrySelectTarget(TraverseTarget target)
    {
        if (!isTraversalActive || IsSelected || !IsAvailable(target))
        {
            return false;
        }

        SetCurrentTarget(target, false);
        EnterSelectedState(target);
        onTargetSelected?.Invoke(target);
        return true;
    }

    private void EnterSelectedState(TraverseTarget target)
    {
        selectedTarget = target;
        currentVisualState = TraverseCursorVisualState.Selected;
        isVisualStateInitialized = false;
        ResetHeldInput();
        ApplySelectionDimming();

        if (isTraversalActive && isActiveAndEnabled)
        {
            ApplyCurrentVisualState(true);
        }
        else
        {
            StopVisualStateAnimation(true);
        }
    }

    private void EnterIdleState()
    {
        RestoreAllTargetOpacity();
        selectedTarget = null;
        currentVisualState = TraverseCursorVisualState.Idle;
        isVisualStateInitialized = false;
        ResetHeldInput();

        if (isTraversalActive && isActiveAndEnabled)
        {
            ApplyCurrentVisualState(true);
        }
        else
        {
            StopVisualStateAnimation(true);
        }
    }

    public void RefreshTargets()
    {
        RestoreAllTargetOpacity(true);
        UnsubscribeAllTargets();
        SubscribeOrder(horizontalOrder);
        SubscribeOrder(verticalOrder);
        ApplySelectionDimming();
    }

    private void ApplySelectionDimming(bool immediate = false)
    {
        if (!isTraversalActive || !IsSelected || !dimUnselectedTargets)
        {
            RestoreAllTargetOpacity(immediate);
            return;
        }

        foreach (TraverseTarget target in subscribedTargets)
        {
            if (target != null)
            {
                target.SetDimmed(
                    target != selectedTarget,
                    unselectedTargetAlpha,
                    immediate ? 0f : dimTransitionDuration,
                    dimTransitionCurve,
                    useUnscaledDimmingTime
                );
            }
        }
    }

    private void RestoreAllTargetOpacity(bool immediate = false)
    {
        foreach (TraverseTarget target in subscribedTargets)
        {
            if (target != null)
            {
                target.RestoreOpacity(
                    immediate ? 0f : dimTransitionDuration,
                    dimTransitionCurve,
                    useUnscaledDimmingTime
                );
            }
        }
    }

    public void RefreshFollowedElement()
    {
        UnsubscribeFromFollowedElement();
        SubscribeToFollowedElement();
        SynchronizeWithFollowedElement(false);
    }

    private void SubscribeToFollowedElement()
    {
        if (followActiveStateOf == null
            || subscribedFollowedElement == followActiveStateOf)
        {
            return;
        }

        subscribedFollowedElement = followActiveStateOf;
        subscribedFollowedElement.StateTransitionStarted +=
            HandleFollowedTransitionStarted;
        subscribedFollowedElement.StateTransitionCompleted +=
            HandleFollowedTransitionCompleted;
    }

    private void UnsubscribeFromFollowedElement()
    {
        if (subscribedFollowedElement == null)
        {
            return;
        }

        subscribedFollowedElement.StateTransitionStarted -=
            HandleFollowedTransitionStarted;
        subscribedFollowedElement.StateTransitionCompleted -=
            HandleFollowedTransitionCompleted;
        subscribedFollowedElement = null;
    }

    private void SynchronizeWithFollowedElement(bool immediate)
    {
        if (followActiveStateOf == null)
        {
            return;
        }

        bool shouldActivate = followActiveStateOf.DesiredActiveState
            && (!activateAfterFollowedAnimation
                || !followActiveStateOf.IsStateTransitionPending);

        if (immediate)
        {
            isTraversalActive = shouldActivate;
            ResetHeldInput();
            if (shouldActivate)
            {
                RestoreOrSelectTarget(true);
            }
            else
            {
                StopMovement();
                ApplyVisibility(false);
            }
            return;
        }

        SetActiveState(shouldActivate);
    }

    private void HandleFollowedTransitionStarted(bool active)
    {
        if (!active || activateAfterFollowedAnimation)
        {
            Deactivate();
            return;
        }

        Activate();
    }

    private void HandleFollowedTransitionCompleted(bool active)
    {
        if (active)
        {
            Activate();
        }
        else
        {
            Deactivate();
        }
    }

    private void RestoreOrSelectTarget(bool immediate)
    {
        TraverseTarget target = IsAvailable(currentTarget)
            ? currentTarget
            : selectFirstTargetOnActivate
                ? FindFirstAvailableTarget()
                : null;

        if (target == null)
        {
            currentTarget = null;
            ApplyVisibility(false);
            return;
        }

        SetCurrentTarget(target, immediate);
    }

    private void ProcessNavigationInput()
    {
        if (IsSelected)
        {
            ResetHeldInput();
            return;
        }

        if (!horizontalOrder.Use && !verticalOrder.Use)
        {
            ResetHeldInput();
            return;
        }

        InputAction action = GetNavigateAction();
        Vector2 input = action != null ? action.ReadValue<Vector2>() : Vector2.zero;
        NavigateDirection direction = GetDirection(input);

        if (direction == NavigateDirection.None)
        {
            ResetHeldInput();
            return;
        }

        float currentTime = useUnscaledInputTime ? Time.unscaledTime : Time.time;
        if (direction != heldDirection)
        {
            heldDirection = direction;
            nextRepeatTime = currentTime + repeatDelay;
            Navigate(direction);
            return;
        }

        if (currentTime < nextRepeatTime)
        {
            return;
        }

        Navigate(direction);
        nextRepeatTime = currentTime + repeatInterval;
    }

    private NavigateDirection GetDirection(Vector2 input)
    {
        if (input.sqrMagnitude < inputDeadZone * inputDeadZone)
        {
            return NavigateDirection.None;
        }

        bool useHorizontalAxis = Mathf.Abs(input.x) >= Mathf.Abs(input.y);
        if (useHorizontalAxis)
        {
            return input.x < 0f ? NavigateDirection.Left : NavigateDirection.Right;
        }

        return input.y < 0f ? NavigateDirection.Down : NavigateDirection.Up;
    }

    private void Navigate(NavigateDirection direction)
    {
        TraverseOrder order;
        int step;

        if (horizontalOrder.Use && verticalOrder.Use)
        {
            bool horizontal = direction == NavigateDirection.Left
                || direction == NavigateDirection.Right;
            order = horizontal ? horizontalOrder : verticalOrder;
            step = direction == NavigateDirection.Left
                || direction == NavigateDirection.Up
                    ? -1
                    : 1;
        }
        else
        {
            order = horizontalOrder.Use ? horizontalOrder : verticalOrder;
            step = direction == NavigateDirection.Left
                || direction == NavigateDirection.Up
                    ? -1
                    : 1;
        }

        TraverseTarget nextTarget = FindAdjacentTarget(order.Targets, currentTarget, step);
        if (nextTarget != null && nextTarget != currentTarget)
        {
            SetCurrentTarget(nextTarget, false);
        }
    }

    private TraverseTarget FindAdjacentTarget(
        IReadOnlyList<TraverseTarget> targets,
        TraverseTarget origin,
        int step
    )
    {
        if (targets == null || targets.Count == 0)
        {
            return null;
        }

        int originIndex = IndexOf(targets, origin);
        if (originIndex < 0)
        {
            return FindBoundaryTarget(targets, step > 0);
        }

        int index = originIndex;
        for (int attempt = 0; attempt < targets.Count; attempt++)
        {
            int candidateIndex = index + step;
            if (wrapAround)
            {
                candidateIndex = WrapIndex(candidateIndex, targets.Count);
            }
            else if (candidateIndex < 0 || candidateIndex >= targets.Count)
            {
                return origin.IsTraversable ? origin : null;
            }

            index = candidateIndex;
            TraverseTarget candidate = targets[index];
            if (IsAvailable(candidate))
            {
                return candidate;
            }
        }

        return origin.IsTraversable ? origin : null;
    }

    private static TraverseTarget FindBoundaryTarget(
        IReadOnlyList<TraverseTarget> targets,
        bool fromStart
    )
    {
        int index = fromStart ? 0 : targets.Count - 1;
        int end = fromStart ? targets.Count : -1;
        int step = fromStart ? 1 : -1;

        for (; index != end; index += step)
        {
            TraverseTarget candidate = targets[index];
            if (IsAvailable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private TraverseTarget FindFirstAvailableTarget()
    {
        TraverseTarget target = FindBoundaryTarget(horizontalOrder.Targets, true);
        return target != null
            ? target
            : FindBoundaryTarget(verticalOrder.Targets, true);
    }

    private void SetCurrentTarget(TraverseTarget target, bool immediate)
    {
        if (!IsAvailable(target)
            || (IsSelected && target != selectedTarget))
        {
            return;
        }

        bool targetChanged = currentTarget != target;
        currentTarget = target;
        TrackCurrentCursorPose();
        ApplyVisibility(true);

        if (immediate || moveDuration <= 0f || !isActiveAndEnabled)
        {
            StopMovement();
            cursorTransform.position = target.CursorWorldPosition;
        }
        else
        {
            StartMovement();
        }

        if (syncEventSystemSelection)
        {
            eventSystemSelectionPending = true;
        }

        if (targetChanged)
        {
            CurrentTargetChanged?.Invoke(target);
            onTargetChanged?.Invoke(target);
        }
    }

    private void StartMovement()
    {
        StopMovement();
        movementCoroutine = StartCoroutine(MoveToCurrentTarget());
    }

    private IEnumerator MoveToCurrentTarget()
    {
        Vector3 origin = cursorTransform.position;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            if (!isTraversalActive || !IsAvailable(currentTarget))
            {
                movementCoroutine = null;
                yield break;
            }

            elapsed += useUnscaledMovementTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / moveDuration);
            float curvedProgress = moveCurve != null
                ? moveCurve.Evaluate(progress)
                : progress;
            cursorTransform.position = Vector3.LerpUnclamped(
                origin,
                currentTarget.CursorWorldPosition,
                curvedProgress
            );
            yield return null;
        }

        if (IsAvailable(currentTarget))
        {
            cursorTransform.position = currentTarget.CursorWorldPosition;
        }
        movementCoroutine = null;
    }

    private void StopMovement()
    {
        if (movementCoroutine == null)
        {
            return;
        }

        StopCoroutine(movementCoroutine);
        movementCoroutine = null;
    }

    private void EnsureVisualStateAnimation()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (!isVisualStateInitialized)
        {
            ApplyCurrentVisualState(true);
            return;
        }

        if (currentVisualState == TraverseCursorVisualState.Idle
            && visualStateCoroutine == null)
        {
            ApplyCurrentVisualState(false);
        }
    }

    private void ApplyCurrentVisualState(bool enterState)
    {
        StopVisualStateAnimation(true);
        isVisualStateInitialized = true;

        if (!isTraversalActive || cursorTransform == null)
        {
            return;
        }

        if (currentVisualState == TraverseCursorVisualState.Idle)
        {
            if (idlePulseDuration > 0f
                && !Mathf.Approximately(idlePulseScaleMultiplier, 1f))
            {
                visualStateCoroutine = StartCoroutine(PlayIdlePulse());
            }
            return;
        }

        if (enterState)
        {
            if (selectedReturnDuration <= 0f)
            {
                cursorTransform.localScale = baseLocalScale;
            }
            else
            {
                visualStateCoroutine = StartCoroutine(PlaySelectedEnter());
            }
        }
    }

    private IEnumerator PlayIdlePulse()
    {
        float elapsed = 0f;
        float halfCycleDuration = Mathf.Max(0.0001f, idlePulseDuration * 0.5f);

        while (isTraversalActive
            && currentVisualState == TraverseCursorVisualState.Idle)
        {
            elapsed += useUnscaledVisualTime
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            float pulseProgress = Mathf.PingPong(
                elapsed / halfCycleDuration,
                1f
            );
            float curvedProgress = idlePulseCurve != null
                ? idlePulseCurve.Evaluate(pulseProgress)
                : pulseProgress;
            float scaleMultiplier = Mathf.LerpUnclamped(
                1f,
                idlePulseScaleMultiplier,
                curvedProgress
            );
            cursorTransform.localScale = baseLocalScale * scaleMultiplier;
            yield return null;
        }

        cursorTransform.localScale = baseLocalScale;
        visualStateCoroutine = null;
    }

    private IEnumerator PlaySelectedEnter()
    {
        Vector3 enlargedScale = baseLocalScale * selectedScaleMultiplier;
        cursorTransform.localScale = enlargedScale;
        float elapsed = 0f;

        while (elapsed < selectedReturnDuration)
        {
            if (!isTraversalActive
                || currentVisualState != TraverseCursorVisualState.Selected)
            {
                cursorTransform.localScale = baseLocalScale;
                visualStateCoroutine = null;
                yield break;
            }

            elapsed += useUnscaledVisualTime
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / selectedReturnDuration);
            float curvedProgress = selectedReturnCurve != null
                ? selectedReturnCurve.Evaluate(progress)
                : progress;
            cursorTransform.localScale = Vector3.LerpUnclamped(
                enlargedScale,
                baseLocalScale,
                curvedProgress
            );
            yield return null;
        }

        cursorTransform.localScale = baseLocalScale;
        visualStateCoroutine = null;
    }

    private void StopVisualStateAnimation(bool restoreBaseScale)
    {
        if (visualStateCoroutine != null)
        {
            StopCoroutine(visualStateCoroutine);
            visualStateCoroutine = null;
        }

        if (restoreBaseScale && cursorTransform != null)
        {
            cursorTransform.localScale = baseLocalScale;
        }
    }

    private void HandlePointerEntered(TraverseTarget target)
    {
        // Mouse hover always jumps directly to the pointed target. The list order is
        // used only for the next keyboard/gamepad navigation input.
        if (isTraversalActive && !IsSelected && IsAvailable(target))
        {
            SetCurrentTarget(target, false);
        }
    }

    private void HandlePointerExited(TraverseTarget target)
    {
        // Leaving a target does not clear the selection. This keeps directional
        // navigation anchored to the last hovered target.
    }

    private void HandlePointerClicked(
        TraverseTarget target,
        PointerEventData.InputButton button
    )
    {
        if (!isTraversalActive || button != PointerEventData.InputButton.Left)
        {
            return;
        }

        ToggleTargetSelection(target);
    }

    private void SubscribeOrder(TraverseOrder order)
    {
        if (order == null || order.Targets == null)
        {
            return;
        }

        for (int i = 0; i < order.Targets.Count; i++)
        {
            TraverseTarget target = order.Targets[i];
            if (target == null || !subscribedTargets.Add(target))
            {
                continue;
            }

            target.PointerEntered += HandlePointerEntered;
            target.PointerExited += HandlePointerExited;
            target.PointerClicked += HandlePointerClicked;
        }
    }

    private void UnsubscribeAllTargets()
    {
        foreach (TraverseTarget target in subscribedTargets)
        {
            if (target == null)
            {
                continue;
            }

            target.PointerEntered -= HandlePointerEntered;
            target.PointerExited -= HandlePointerExited;
            target.PointerClicked -= HandlePointerClicked;
        }
        subscribedTargets.Clear();
    }

    private void EnableInput()
    {
        InputAction referencedAction = navigateAction != null
            ? navigateAction.action
            : null;
        if (referencedAction != null)
        {
            if (!referencedAction.enabled)
            {
                referencedAction.Enable();
                enabledReferencedAction = true;
            }
        }
        else
        {
            EnsureFallbackInput();
            fallbackNavigateAction.Enable();
        }

        activeSelectAction = ResolveCommonUIAction(
            selectAction,
            "CommonUI/Select"
        );
        if (activeSelectAction != null)
        {
            activeSelectAction.performed += HandleSelectInput;
            if (!activeSelectAction.enabled)
            {
                activeSelectAction.Enable();
                enabledSelectAction = true;
            }
        }

        activeCancelAction = ResolveCommonUIAction(
            cancelAction,
            "CommonUI/Cancel"
        );
        if (activeCancelAction != null)
        {
            activeCancelAction.performed += HandleCancelInput;
            if (!activeCancelAction.enabled)
            {
                activeCancelAction.Enable();
                enabledCancelAction = true;
            }
        }
    }

    private void DisableInput()
    {
        if (activeSelectAction != null)
        {
            activeSelectAction.performed -= HandleSelectInput;
            if (enabledSelectAction)
            {
                activeSelectAction.Disable();
            }
        }
        activeSelectAction = null;
        enabledSelectAction = false;

        if (activeCancelAction != null)
        {
            activeCancelAction.performed -= HandleCancelInput;
            if (enabledCancelAction)
            {
                activeCancelAction.Disable();
            }
        }
        activeCancelAction = null;
        enabledCancelAction = false;

        InputAction referencedAction = navigateAction != null
            ? navigateAction.action
            : null;
        if (enabledReferencedAction && referencedAction != null)
        {
            referencedAction.Disable();
        }
        enabledReferencedAction = false;
        fallbackNavigateAction?.Disable();
    }

    private InputAction ResolveCommonUIAction(
        InputActionReference actionReference,
        string actionPath
    )
    {
        if (actionReference != null && actionReference.action != null)
        {
            return actionReference.action;
        }

        InputAction navigate = navigateAction != null
            ? navigateAction.action
            : null;
        InputActionAsset asset = navigate?.actionMap?.asset ?? InputSystem.actions;
        return asset != null ? asset.FindAction(actionPath, false) : null;
    }

    private void HandleSelectInput(InputAction.CallbackContext context)
    {
        // Mouse selection is handled by TraverseTarget so the clicked target is
        // known. Processing it again here would toggle the state twice.
        if (!isTraversalActive || context.control?.device is Mouse)
        {
            return;
        }

        ToggleTargetSelection(currentTarget);
    }

    private void HandleCancelInput(InputAction.CallbackContext context)
    {
        if (isTraversalActive)
        {
            CancelSelection();
        }
    }

    private InputAction GetNavigateAction()
    {
        InputAction referencedAction = navigateAction != null
            ? navigateAction.action
            : null;
        return referencedAction ?? fallbackNavigateAction;
    }

    private void EnsureFallbackInput()
    {
        if (fallbackNavigateAction != null)
        {
            return;
        }

        fallbackNavigateAction = new InputAction(
            name: "Traverse Navigate",
            type: InputActionType.Value,
            expectedControlType: "Vector2"
        );
        fallbackNavigateAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        fallbackNavigateAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        fallbackNavigateAction.AddBinding("<Gamepad>/dpad");
        fallbackNavigateAction.AddBinding("<Gamepad>/leftStick");
    }

    private void SyncEventSystemSelection()
    {
        if (!syncEventSystemSelection
            || EventSystem.current == null
            || currentTarget == null
            || !currentTarget.IsTraversable)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(currentTarget.gameObject);
    }

    private void ApplyVisibility(bool visible)
    {
        ResolveReferences();

        bool useCanvasGroup = hideMode == TraverseCursorHideMode.CanvasGroup
            || hideMode == TraverseCursorHideMode.CanvasGroupAndMoveOffscreen;
        bool moveOffscreen = hideMode == TraverseCursorHideMode.MoveOffscreen
            || hideMode == TraverseCursorHideMode.CanvasGroupAndMoveOffscreen;

        if (cursorCanvasGroup != null)
        {
            cursorCanvasGroup.alpha = visible || !useCanvasGroup ? 1f : 0f;
            ConfigureCanvasGroup();
        }

        if (!visible && moveOffscreen && cursorTransform != null)
        {
            cursorTransform.anchoredPosition3D = hiddenAnchoredPosition;
        }
    }

    private void ResolveReferences()
    {
        if (cursorTransform == null)
        {
            cursorTransform = GetComponent<RectTransform>();
        }

        if (cursorCanvasGroup == null)
        {
            cursorCanvasGroup = GetComponent<CanvasGroup>();
        }

        ConfigureCanvasGroup();
    }

    private void ConfigureCanvasGroup()
    {
        if (cursorCanvasGroup == null)
        {
            return;
        }

        cursorCanvasGroup.interactable = false;
        cursorCanvasGroup.blocksRaycasts = false;
    }

    private void ResetHeldInput()
    {
        heldDirection = NavigateDirection.None;
        nextRepeatTime = 0f;
    }

    private static bool IsAvailable(TraverseTarget target)
    {
        return target != null && target.IsTraversable;
    }

    private static int IndexOf(
        IReadOnlyList<TraverseTarget> targets,
        TraverseTarget target
    )
    {
        if (target == null)
        {
            return -1;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == target)
            {
                return i;
            }
        }
        return -1;
    }

    private static int WrapIndex(int index, int count)
    {
        return (index % count + count) % count;
    }

    private void OnValidate()
    {
        ResolveReferences();
        inputDeadZone = Mathf.Clamp(inputDeadZone, 0.01f, 1f);
        repeatDelay = Mathf.Max(0f, repeatDelay);
        repeatInterval = Mathf.Max(0.01f, repeatInterval);
        moveDuration = Mathf.Max(0f, moveDuration);
        idlePulseScaleMultiplier = Mathf.Max(1f, idlePulseScaleMultiplier);
        idlePulseDuration = Mathf.Max(0f, idlePulseDuration);
        selectedScaleMultiplier = Mathf.Max(1f, selectedScaleMultiplier);
        selectedReturnDuration = Mathf.Max(0f, selectedReturnDuration);
        unselectedTargetAlpha = Mathf.Clamp01(unselectedTargetAlpha);
        dimTransitionDuration = Mathf.Max(0f, dimTransitionDuration);

        if (Application.isPlaying)
        {
            ApplySelectionDimming();
        }
    }
}
