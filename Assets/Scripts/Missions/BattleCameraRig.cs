using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-owned player follow, directional lead, and composed presentation/weapon zoom.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
[RequireComponent(typeof(Camera))]
public sealed class BattleCameraRig : MonoBehaviour
{
    [Header("Scene Camera")]
    [SerializeField] private Camera outputCamera;
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -1.482f);
    [SerializeField, Min(0f)] private float positionSmoothTime = 0.08f;

    [Header("Look Ahead (World Units)")]
    [SerializeField, Min(0f)] private float movementLookAheadDistance = 0.6f;
    [SerializeField, Min(0f)] private float aimLookAheadDistance = 1f;
    [SerializeField, Min(0f)] private float lookAheadSmoothTime = 0.18f;
    [Tooltip("Below this speed, use the player's heading instead of near-zero velocity.")]
    [SerializeField, Min(0f)] private float movementDirectionMinSpeed = 0.05f;

    private Player player;
    private PlayerWeaponController[] aimControllers = System.Array.Empty<PlayerWeaponController>();
    private bool capturedSize;
    private Vector2 lookAhead;
    private Vector2 lookAheadVelocity;
    private Vector3 positionVelocity;
    private float viewMultiplier = 1f;
    private float viewReturnLerpSpeed = 8f;
    private readonly Dictionary<object, ViewRequest> viewRequests = new();
    public Camera OutputCamera => outputCamera != null ? outputCamera : GetComponent<Camera>();
    public float NormalSize { get; private set; }
    public float PresentationSize { get; private set; }
    public Vector2 CurrentLookAhead => lookAhead;

    private readonly struct ViewRequest
    {
        public readonly float Multiplier;
        public readonly float LerpSpeed;
        public ViewRequest(float multiplier, float lerpSpeed)
        { Multiplier = multiplier; LerpSpeed = lerpSpeed; }
    }

    private void Awake() => CaptureSize();
    private void CaptureSize()
    {
        if (outputCamera == null) outputCamera = GetComponent<Camera>();
        if (capturedSize || outputCamera == null) return;
        NormalSize = outputCamera.orthographicSize;
        PresentationSize = NormalSize;
        capturedSize = true;
    }

    public void BindPlayer(Player activePlayer)
    {
        CaptureSize();
        // Remove old-player modifiers before binding a replacement character.
        viewRequests.Clear();
        viewMultiplier = 1f;
        lookAhead = lookAheadVelocity = Vector2.zero;
        positionVelocity = Vector3.zero;
        player = activePlayer;
        aimControllers = player != null ? player.GetComponentsInChildren<PlayerWeaponController>(true)
            : System.Array.Empty<PlayerWeaponController>();
        RestoreNormalSize();
        if (player == null) return;
        // Preserve the character's RenderTexture cameras and their display meshes.
        foreach (Camera candidate in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate != OutputCamera && candidate.gameObject.scene == gameObject.scene && candidate.targetTexture == null)
                candidate.enabled = false;
        AudioListener ownListener = GetComponent<AudioListener>();
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (listener.gameObject.scene == gameObject.scene)
                listener.enabled = listener == ownListener;
        OutputCamera.enabled = true;
        foreach (PlayerWeaponController weapons in aimControllers)
            weapons.SetAimCamera(OutputCamera);
        foreach (HomingMissileWeapon weapon in player.GetComponentsInChildren<HomingMissileWeapon>(true))
            weapon.SetBattleCamera(this);
        FollowNow();
    }

    private Vector2 GetDesiredLookAhead()
    {
        if (player == null || !player.isActiveAndEnabled || player.IsDead || player.IsBattleEntryLocked)
            return Vector2.zero;
        foreach (PlayerWeaponController controller in aimControllers)
            if (controller != null && controller.IsAimingForCamera)
            {
                Vector2 aim = controller.CurrentAimDirection;
                if (aim.sqrMagnitude > 0.0001f) return aim.normalized * aimLookAheadDistance;
            }
        Vector2 movement = player.Velocity;
        if (movement.sqrMagnitude < Mathf.Max(0.0001f, movementDirectionMinSpeed * movementDirectionMinSpeed))
            movement = player.MovementDirection;
        return movement.sqrMagnitude > 0.0001f ? movement.normalized * movementLookAheadDistance : Vector2.zero;
    }

    public void FollowNow()
    {
        if (player == null) return;
        lookAhead = GetDesiredLookAhead();
        lookAheadVelocity = Vector2.zero;
        positionVelocity = Vector3.zero;
        transform.position = player.transform.position + followOffset + (Vector3)lookAhead;
    }
    private void LateUpdate() => Advance(Time.deltaTime);

    /// <summary>Scaled gameplay time; entry presentation uses SetSize independently with unscaled time.</summary>
    public void Advance(float deltaTime)
    {
        if (deltaTime < 0f) return;
        if (player != null)
        {
            Vector2 targetAhead = GetDesiredLookAhead();
            if (lookAheadSmoothTime <= 0f) lookAhead = targetAhead;
            else if (deltaTime > 0f) lookAhead = Vector2.SmoothDamp(lookAhead, targetAhead,
                ref lookAheadVelocity, lookAheadSmoothTime, Mathf.Infinity, deltaTime);
            Vector3 targetPosition = player.transform.position + followOffset + (Vector3)lookAhead;
            if (player.IsBattleEntryLocked || positionSmoothTime <= 0f) transform.position = targetPosition;
            else if (deltaTime > 0f) transform.position = Vector3.SmoothDamp(transform.position, targetPosition,
                ref positionVelocity, positionSmoothTime, Mathf.Infinity, deltaTime);
        }
        float targetMultiplier = GetRequestedView(out float lerpSpeed);
        if (deltaTime > 0f)
            viewMultiplier = Mathf.Lerp(viewMultiplier, targetMultiplier, 1f - Mathf.Exp(-lerpSpeed * deltaTime));
        if (Mathf.Abs(viewMultiplier - targetMultiplier) < 0.0001f) viewMultiplier = targetMultiplier;
        ApplyViewSize();
    }

    public void RequestViewMultiplier(object source, float multiplier, float lerpSpeed)
    {
        if (source == null) return;
        viewRequests[source] = new ViewRequest(Mathf.Max(1f, multiplier), Mathf.Max(0f, lerpSpeed));
    }

    public void RemoveViewMultiplier(object source, float returnLerpSpeed = 8f, bool immediate = false)
    {
        if (source == null) return;
        bool removed = viewRequests.Remove(source);
        // Disable/death must also cancel an already-started smooth restoration.
        if (!removed && !immediate) return;
        viewReturnLerpSpeed = Mathf.Max(0f, returnLerpSpeed);
        if (immediate)
        {
            viewMultiplier = GetRequestedView(out _);
            ApplyViewSize();
        }
    }

    private float GetRequestedView(out float lerpSpeed)
    {
        float multiplier = 1f;
        lerpSpeed = viewReturnLerpSpeed;
        foreach (ViewRequest request in viewRequests.Values)
            if (request.Multiplier >= multiplier)
            { multiplier = request.Multiplier; lerpSpeed = request.LerpSpeed; }
        return multiplier;
    }

    public void SetSize(float size)
    {
        CaptureSize();
        PresentationSize = Mathf.Max(0.01f, size);
        ApplyViewSize();
    }

    private void ApplyViewSize()
    {
        Camera camera = OutputCamera;
        if (camera != null) camera.orthographicSize = Mathf.Max(0.01f, PresentationSize * viewMultiplier);
    }
    public void RestoreNormalSize() { CaptureSize(); SetSize(NormalSize); }

    private void OnDisable()
    {
        viewRequests.Clear();
        viewMultiplier = 1f;
        RestoreNormalSize();
    }

    private void OnValidate()
    {
        positionSmoothTime = Mathf.Max(0f, positionSmoothTime);
        movementLookAheadDistance = Mathf.Max(0f, movementLookAheadDistance);
        aimLookAheadDistance = Mathf.Max(0f, aimLookAheadDistance);
        lookAheadSmoothTime = Mathf.Max(0f, lookAheadSmoothTime);
        movementDirectionMinSpeed = Mathf.Max(0f, movementDirectionMinSpeed);
    }
}
