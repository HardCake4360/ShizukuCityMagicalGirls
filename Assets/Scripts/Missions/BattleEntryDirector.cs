using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Arrival presentation and a fresh-gameplay-input gate, separate from scene loading and camera follow.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class BattleEntryDirector : MonoBehaviour
{
    [SerializeField] private BattleCameraRig cameraRig;
    [SerializeField, Min(0f)] private float revealDuration = 0.6f;
    [SerializeField, Min(0f)] private float zoomInDuration = 0.8f;
    [SerializeField, Range(0.1f, 1f)] private float closeUpSizeMultiplier = 0.55f;
    [SerializeField, Min(0f)] private float returnZoomDuration = 0.45f;
    [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private float standingRotationOffset = -90f;
    [SerializeField, Range(0.05f, 0.9f)] private float inputDeadZone = 0.2f;
    private readonly Dictionary<InputAction, bool> armed = new();
    private Player player;
    private Action beginCombat;
    private float elapsed;
    private float returnStartSize;
    private bool returningZoom;
    private AnimationClip transformationAfter;
    private bool revealStarted;
    public bool IsPrepared { get; private set; }
    public bool IsRevealing { get; private set; }
    public bool IsWaitingForInput { get; private set; }
    public float RevealDuration => Mathf.Max(0f, revealDuration);

    public bool Prepare(Player activePlayer, BattleCameraRig rig, Action onFirstInput,
        AnimationClip afterClip = null)
    {
        cameraRig = rig != null ? rig : cameraRig;
        player = activePlayer;
        beginCombat = onFirstInput;
        transformationAfter = afterClip;
        revealStarted = false;
        IsPrepared = player != null && cameraRig != null;
        if (!IsPrepared) return false;
        cameraRig.RestoreNormalSize();
        player.LockBattleEntry(this, standingRotationOffset);
        return true;
    }

    public void BeginReveal()
    {
        if (!IsPrepared || revealStarted) return;
        revealStarted = true;
        elapsed = 0f;
        IsRevealing = true;
        player.PlayBattleEntryTransformation(transformationAfter);
        Advance(0f);
    }

    public void ArmFirstInput()
    {
        if (!IsPrepared || IsRevealing) return;
        UnsubscribeInputs();
        armed.Clear();
        foreach (InputAction action in player.GetGameplayInputActions())
            if (action != null && action.enabled)
            {
                bool neutral = Mathf.Abs(action.ReadValue<float>()) < inputDeadZone;
                armed[action] = neutral;
                if (!neutral) player.SuppressEntryInput(action);
                action.canceled += OnInputCanceled;
            }
        IsWaitingForInput = true;
        GetComponent<MissionRuntimeController>()?.MarkAwaitingFirstInput();
    }

    private void OnInputCanceled(InputAction.CallbackContext context)
    {
        // Keep listening after combat starts: another control may still be held from loading.
        // Also permits release + new press within the same Input System update.
        if (armed.ContainsKey(context.action)) armed[context.action] = true;
        if (player != null) player.AllowEntryInput(context.action);
    }

    private void UnsubscribeInputs()
    {
        foreach (InputAction action in armed.Keys) action.canceled -= OnInputCanceled;
    }

    public bool TryStartFromInput(InputAction action)
    {
        if (!IsWaitingForInput || action == null || !armed.TryGetValue(action, out bool ready) || !ready
            || Mathf.Abs(action.ReadValue<float>()) < inputDeadZone)
            return false;
        IsWaitingForInput = false;
        player.ReleaseBattleEntry(this);
        // Weapon widening is composed by the rig; never bake it into the presentation baseline.
        returnStartSize = cameraRig.PresentationSize;
        returningZoom = true;
        elapsed = 0f;
        beginCombat?.Invoke();
        Advance(0f);
        return true;
    }

    private void Update()
    {
        Advance(Time.unscaledDeltaTime);
        if (!IsWaitingForInput || player == null) return;
        // Updating dictionary values while enumerating is not portable across Unity runtimes.
        foreach (InputAction action in player.GetGameplayInputActions())
        {
            if (action == null || !action.enabled || !armed.ContainsKey(action)) continue;
            if (Mathf.Abs(action.ReadValue<float>()) < inputDeadZone)
            { armed[action] = true; player.AllowEntryInput(action); }
            else if (armed[action] && TryStartFromInput(action)) break;
        }
    }

    public void Advance(float deltaTime)
    {
        if (cameraRig == null || deltaTime < 0f) return;
        elapsed += deltaTime;
        if (IsRevealing)
        {
            if (player != null) player.AdvanceBattleEntryTransformation(deltaTime);
            float t = zoomInDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / zoomInDuration);
            cameraRig.SetSize(Mathf.Lerp(cameraRig.NormalSize, cameraRig.NormalSize * closeUpSizeMultiplier, Evaluate(t)));
            if (t >= 1f && (player == null || !player.IsPlayingBattleEntryTransformation)) IsRevealing = false;
        }
        else if (returningZoom)
        {
            float t = returnZoomDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / returnZoomDuration);
            cameraRig.SetSize(Mathf.Lerp(returnStartSize, cameraRig.NormalSize, Evaluate(t)));
            if (t >= 1f) { returningZoom = false; cameraRig.RestoreNormalSize(); }
        }
    }

    private float Evaluate(float t) => zoomCurve != null && zoomCurve.length > 0
        ? Mathf.Clamp01(zoomCurve.Evaluate(t)) : t;

    public void Abort()
    {
        IsWaitingForInput = IsRevealing = returningZoom = IsPrepared = false;
        if (player != null) player.ReleaseBattleEntry(this);
        if (cameraRig != null) cameraRig.RestoreNormalSize();
        beginCombat = null;
        transformationAfter = null;
        revealStarted = false;
        UnsubscribeInputs();
        if (player != null)
            foreach (InputAction action in armed.Keys) player.AllowEntryInput(action);
        armed.Clear();
    }
    private void OnDisable() => Abort();
}
