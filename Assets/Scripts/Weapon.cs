using System.Collections.Generic;
using UnityEngine;

public enum WeaponSlotType
{
    MainWeapon,
    SpecialWeapon,
    DualWeapon
}

public interface ISpecialAttackWeapon
{
    void OnSpecialAttackPressed();
    void OnSpecialAttackHeld();
    void OnSpecialAttackReleased();
}

public enum WeaponAnimationPhase
{
    None,
    Ready,
    Charge,
    Hold,
    Release,
    Attack
}

public abstract class Weapon : MonoBehaviour
{
    [Header("Info")]
    [SerializeField] protected string weaponName;
    [SerializeField] protected WeaponSlotType slotType;

    [Header("Timing")]
    [SerializeField] protected float attackStartDelay;
    [SerializeField] protected float attackCooldown;
    [SerializeField] protected float attackEndDelay;

    [Header("Stats")]
    [SerializeField] protected int damage;

    [Header("Aim")]
    [SerializeField] protected bool changeDirectionWhileHovering;
    [SerializeField] protected float minimumDirectAimDistance = 0.25f;
    [SerializeField, Range(0f, 1f)] protected float minimumDirectAimAlignment;

    [Header("Floating Weapon Visual")]
    [SerializeField] protected GameObject floatingWeaponPrefab;
    [SerializeField] protected Transform floatingFollowTarget;
    [SerializeField] protected Vector3 floatingPositionOffset;
    [SerializeField] protected float floatingMinimumTargetDistance = 1f;
    [SerializeField] protected float floatingFollowDelay = 0.15f;
    [SerializeField] protected float floatingPositionSmoothTime = 0.08f;
    [SerializeField] protected float floatingTeleportResetDistance = 6f;
    [SerializeField] protected float floatingTiltDegreesPerSpeed = 2f;
    [SerializeField] protected float floatingMaxTiltAngle = 18f;
    [SerializeField] protected float floatingTiltSmoothTime = 0.1f;
    [SerializeField] protected float floatingDirectionThreshold = 0.1f;
    [SerializeField] protected float floatingTurnSmoothTime = 0.12f;

    protected Player owner;
    protected PlayerWeaponController playerWeaponController;
    protected float cooldownTimer;
    protected bool isAttacking;
    protected WeaponAnimationPhase animationPhase;
    protected GameObject floatingWeaponInstance;
    protected Transform floatingTiltPivot;
    protected Transform floatingDirectionObject;
    protected Quaternion floatingBasePivotRotation;
    protected Quaternion floatingBaseObjectRotation;
    protected Vector3 floatingPositionVelocity;
    protected float floatingTiltX;
    protected float floatingTiltXVelocity;
    protected float floatingTiltZ;
    protected float floatingTiltZVelocity;
    protected float floatingYaw;
    protected float floatingYawVelocity;
    protected bool floatingFacesPositiveX = true;
    protected readonly List<FloatingPositionSample> floatingPositionHistory =
        new List<FloatingPositionSample>();

    public string WeaponName => weaponName;
    public WeaponSlotType SlotType => slotType;
    public int Damage => damage;
    public bool ChangeDirectionWhileHovering => changeDirectionWhileHovering;
    public bool IsAttacking => isAttacking;
    public bool IsOnCooldown => cooldownTimer > 0f;
    public WeaponAnimationPhase AnimationPhase => animationPhase;
    public virtual bool IsCameraAiming => isAttacking || animationPhase is WeaponAnimationPhase.Ready
        or WeaponAnimationPhase.Charge or WeaponAnimationPhase.Hold
        or WeaponAnimationPhase.Release or WeaponAnimationPhase.Attack;

    public virtual bool TryGetAimLine(out Transform muzzle, out Vector2 targetPoint)
    {
        CachePlayerWeaponController();
        muzzle = GetAimMuzzle();
        targetPoint = GetDesiredAimPoint(muzzle);
        return muzzle != null && playerWeaponController != null;
    }

    public virtual void Initialize(Player player)
    {
        owner = player;
        CachePlayerWeaponController();
        InitializeFloatingWeaponVisual();
    }

    protected virtual void CachePlayerWeaponController()
    {
        if (playerWeaponController == null)
        {
            playerWeaponController = GetComponentInParent<PlayerWeaponController>();
        }

        if (playerWeaponController == null && owner != null)
        {
            playerWeaponController = owner.GetComponent<PlayerWeaponController>();
        }
    }

    protected virtual void OnValidate()
    {
        minimumDirectAimDistance = Mathf.Max(0f, minimumDirectAimDistance);
        minimumDirectAimAlignment = Mathf.Clamp01(minimumDirectAimAlignment);
        floatingFollowDelay = Mathf.Max(0f, floatingFollowDelay);
        floatingMinimumTargetDistance = Mathf.Max(0f, floatingMinimumTargetDistance);
        floatingPositionSmoothTime = Mathf.Max(0.0001f, floatingPositionSmoothTime);
        floatingTeleportResetDistance = Mathf.Max(0f, floatingTeleportResetDistance);
        floatingTiltDegreesPerSpeed = Mathf.Max(0f, floatingTiltDegreesPerSpeed);
        floatingMaxTiltAngle = Mathf.Clamp(floatingMaxTiltAngle, 0f, 90f);
        floatingTiltSmoothTime = Mathf.Max(0.0001f, floatingTiltSmoothTime);
        floatingDirectionThreshold = Mathf.Max(0f, floatingDirectionThreshold);
        floatingTurnSmoothTime = Mathf.Max(0.0001f, floatingTurnSmoothTime);
    }

    protected virtual Transform GetAimMuzzle()
    {
        return null;
    }

    protected virtual Vector2 GetDesiredAimPoint(Transform muzzle)
    {
        CachePlayerWeaponController();
        return playerWeaponController != null
            ? playerWeaponController.GetAimWorldPoint()
            : (Vector2)transform.position + (Vector2)transform.right;
    }

    protected virtual Vector2 GetSafeAimDirection(
        Vector3 origin,
        PlayerWeaponController aimController = null
    )
    {
        PlayerWeaponController controller = aimController;
        if (controller == null)
        {
            CachePlayerWeaponController();
            controller = playerWeaponController;
        }

        if (controller == null)
        {
            Vector2 fallbackDirection = transform.right;
            return fallbackDirection.sqrMagnitude > 0.0001f
                ? fallbackDirection.normalized
                : Vector2.right;
        }

        Vector2 baseDirection = controller.GetAimDirection();
        if (controller.HasAimTurnSpeedLimit)
        {
            return baseDirection;
        }

        Vector2 originToAimPoint = controller.GetAimWorldPoint() - (Vector2)origin;
        return CanUseDirectAim(originToAimPoint, baseDirection)
            ? originToAimPoint.normalized
            : baseDirection;
    }

    protected virtual bool CanUseDirectAim(Vector2 originToAimPoint, Vector2 baseDirection)
    {
        float minimumDistanceSqr = minimumDirectAimDistance * minimumDirectAimDistance;
        return originToAimPoint.sqrMagnitude >= minimumDistanceSqr
            && IsDirectionSafelyAligned(originToAimPoint, baseDirection);
    }

    protected virtual bool IsDirectionSafelyAligned(Vector2 direction, Vector2 baseDirection)
    {
        if (direction.sqrMagnitude <= 0.0001f || baseDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float alignment = Vector2.Dot(direction.normalized, baseDirection.normalized);
        return alignment >= minimumDirectAimAlignment;
    }

    protected void ApplyAimTurnSpeedLimit(float degreesPerSecond)
    {
        CachePlayerWeaponController();
        playerWeaponController?.SetAimTurnSpeedLimit(this, degreesPerSecond);
    }

    protected void RemoveAimTurnSpeedLimit()
    {
        if (playerWeaponController != null)
        {
            playerWeaponController.RemoveAimTurnSpeedLimit(this);
        }
    }

    protected virtual void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }
    }

    protected virtual void LateUpdate()
    {
        UpdateFloatingWeaponVisual();
    }

    protected virtual void OnDisable()
    {
        RemoveAimTurnSpeedLimit();
        ClearWeaponAnimation();
        ExitWeaponBusyStateImmediately();

        if (floatingWeaponInstance != null)
        {
            floatingWeaponInstance.SetActive(false);
        }
    }

    protected virtual void OnDestroy()
    {
        if (floatingWeaponInstance != null)
        {
            Destroy(floatingWeaponInstance);
        }
    }

    protected virtual void InitializeFloatingWeaponVisual()
    {
        if (floatingWeaponPrefab == null || floatingWeaponInstance != null)
        {
            return;
        }

        Transform followTarget = GetFloatingFollowTarget();
        if (followTarget == null)
        {
            return;
        }

        Vector3 initialPosition = GetFloatingTargetPosition(followTarget);
        floatingWeaponInstance = Instantiate(
            floatingWeaponPrefab,
            initialPosition,
            floatingWeaponPrefab.transform.rotation
        );

        floatingTiltPivot = floatingWeaponInstance.transform;
        floatingDirectionObject = floatingTiltPivot.childCount > 0
            ? floatingTiltPivot.GetChild(0)
            : null;

        if (floatingDirectionObject == null)
        {
            Destroy(floatingWeaponInstance);
            floatingWeaponInstance = null;
            floatingTiltPivot = null;
            return;
        }

        floatingBasePivotRotation = floatingTiltPivot.localRotation;
        floatingBaseObjectRotation = floatingDirectionObject.localRotation;
        floatingPositionHistory.Clear();
        floatingPositionHistory.Add(new FloatingPositionSample(Time.time, initialPosition));
        floatingPositionVelocity = Vector3.zero;
        floatingTiltX = 0f;
        floatingTiltZ = 0f;
        floatingYaw = 0f;
        floatingFacesPositiveX = true;
    }

    protected virtual void UpdateFloatingWeaponVisual()
    {
        if (floatingWeaponPrefab == null)
        {
            return;
        }

        if (floatingWeaponInstance == null)
        {
            InitializeFloatingWeaponVisual();
        }

        Transform followTarget = GetFloatingFollowTarget();
        if (floatingWeaponInstance == null || followTarget == null
            || floatingTiltPivot == null || floatingDirectionObject == null)
        {
            return;
        }

        if (!floatingWeaponInstance.activeSelf)
        {
            floatingWeaponInstance.SetActive(true);
            ResetFloatingPositionHistory(GetFloatingTargetPosition(followTarget));
        }

        Vector3 currentTargetPosition = GetFloatingTargetPosition(followTarget);
        if (floatingPositionHistory.Count > 0
            && floatingTeleportResetDistance > 0f
            && Vector3.Distance(
                floatingPositionHistory[floatingPositionHistory.Count - 1].Position,
                currentTargetPosition
            ) >= floatingTeleportResetDistance)
        {
            ResetFloatingPositionHistory(currentTargetPosition);
        }
        else
        {
            floatingPositionHistory.Add(
                new FloatingPositionSample(Time.time, currentTargetPosition)
            );
        }

        Vector3 delayedPosition = GetDelayedFloatingPosition(Time.time - floatingFollowDelay);
        Vector3 nextPosition = Vector3.SmoothDamp(
            floatingTiltPivot.position,
            delayedPosition,
            ref floatingPositionVelocity,
            floatingPositionSmoothTime
        );
        floatingTiltPivot.position = EnforceFloatingMinimumDistance(
            nextPosition,
            followTarget.position
        );

        UpdateFloatingWeaponRotation();
        TrimFloatingPositionHistory(Time.time - floatingFollowDelay - 0.5f);
    }

    protected virtual void UpdateFloatingWeaponRotation()
    {
        Vector2 ownerVelocity = owner != null ? owner.Velocity : Vector2.zero;
        float targetTiltX = Mathf.Clamp(
            ownerVelocity.y * floatingTiltDegreesPerSpeed,
            -floatingMaxTiltAngle,
            floatingMaxTiltAngle
        );
        float targetTiltZ = Mathf.Clamp(
            -ownerVelocity.x * floatingTiltDegreesPerSpeed,
            -floatingMaxTiltAngle,
            floatingMaxTiltAngle
        );

        floatingTiltX = Mathf.SmoothDampAngle(
            floatingTiltX,
            targetTiltX,
            ref floatingTiltXVelocity,
            floatingTiltSmoothTime
        );
        floatingTiltZ = Mathf.SmoothDampAngle(
            floatingTiltZ,
            targetTiltZ,
            ref floatingTiltZVelocity,
            floatingTiltSmoothTime
        );

        if (ownerVelocity.x > floatingDirectionThreshold)
        {
            floatingFacesPositiveX = true;
        }
        else if (ownerVelocity.x < -floatingDirectionThreshold)
        {
            floatingFacesPositiveX = false;
        }

        float targetYaw = floatingFacesPositiveX ? 0f : 180f;
        floatingYaw = Mathf.SmoothDampAngle(
            floatingYaw,
            targetYaw,
            ref floatingYawVelocity,
            floatingTurnSmoothTime
        );

        floatingTiltPivot.localRotation = floatingBasePivotRotation
            * Quaternion.Euler(floatingTiltX, 0f, floatingTiltZ);
        floatingDirectionObject.localRotation = Quaternion.Euler(0f, floatingYaw, 0f)
            * floatingBaseObjectRotation;
    }

    protected virtual Vector3 EnforceFloatingMinimumDistance(
        Vector3 position,
        Vector3 targetPosition
    )
    {
        if (floatingMinimumTargetDistance <= 0f)
        {
            return position;
        }

        Vector3 fromTarget = position - targetPosition;
        if (fromTarget.sqrMagnitude >= floatingMinimumTargetDistance * floatingMinimumTargetDistance)
        {
            return position;
        }

        if (fromTarget.sqrMagnitude <= 0.000001f)
        {
            fromTarget = floatingPositionOffset;
        }

        if (fromTarget.sqrMagnitude <= 0.000001f)
        {
            fromTarget = floatingFacesPositiveX ? Vector3.left : Vector3.right;
        }

        return targetPosition + fromTarget.normalized * floatingMinimumTargetDistance;
    }

    protected virtual Transform GetFloatingFollowTarget()
    {
        return floatingFollowTarget != null
            ? floatingFollowTarget
            : owner != null ? owner.transform : null;
    }

    protected virtual Vector3 GetFloatingTargetPosition(Transform followTarget)
    {
        return followTarget.TransformPoint(floatingPositionOffset);
    }

    protected virtual Vector3 GetDelayedFloatingPosition(float targetTime)
    {
        if (floatingPositionHistory.Count == 0)
        {
            return floatingTiltPivot.position;
        }

        if (targetTime <= floatingPositionHistory[0].Time)
        {
            return floatingPositionHistory[0].Position;
        }

        for (int i = 1; i < floatingPositionHistory.Count; i++)
        {
            FloatingPositionSample next = floatingPositionHistory[i];
            if (next.Time < targetTime)
            {
                continue;
            }

            FloatingPositionSample previous = floatingPositionHistory[i - 1];
            float duration = Mathf.Max(0.0001f, next.Time - previous.Time);
            float interpolation = Mathf.Clamp01((targetTime - previous.Time) / duration);
            return Vector3.Lerp(previous.Position, next.Position, interpolation);
        }

        return floatingPositionHistory[floatingPositionHistory.Count - 1].Position;
    }

    protected virtual void TrimFloatingPositionHistory(float minimumTime)
    {
        while (floatingPositionHistory.Count > 2
            && floatingPositionHistory[1].Time < minimumTime)
        {
            floatingPositionHistory.RemoveAt(0);
        }
    }

    protected virtual void ResetFloatingPositionHistory(Vector3 position)
    {
        floatingPositionHistory.Clear();
        floatingPositionHistory.Add(new FloatingPositionSample(Time.time, position));
        floatingTiltPivot.position = position;
        floatingPositionVelocity = Vector3.zero;
    }

    protected readonly struct FloatingPositionSample
    {
        public readonly float Time;
        public readonly Vector3 Position;

        public FloatingPositionSample(float time, Vector3 position)
        {
            Time = time;
            Position = position;
        }
    }

    protected void SetWeaponAnimationPhase(WeaponAnimationPhase phase)
    {
        if (animationPhase == phase)
        {
            return;
        }

        animationPhase = phase;
        if (owner != null)
        {
            owner.SetWeaponAnimationPhase(this, phase);
        }
    }

    protected void EnterChargeAnimation()
    {
        SetWeaponAnimationPhase(WeaponAnimationPhase.Charge);
    }

    protected void EnterReadyAnimation()
    {
        SetWeaponAnimationPhase(WeaponAnimationPhase.Ready);
    }

    protected void EnterHoldAnimation()
    {
        SetWeaponAnimationPhase(WeaponAnimationPhase.Hold);
    }

    protected void EnterReleaseAnimation()
    {
        SetWeaponAnimationPhase(WeaponAnimationPhase.Release);
    }

    protected void ReplayReleaseAnimation()
    {
        animationPhase = WeaponAnimationPhase.Release;
        if (owner != null)
        {
            owner.ReplayWeaponAnimationPhase(this, WeaponAnimationPhase.Release);
        }
    }

    protected void EnterAttackAnimation()
    {
        SetWeaponAnimationPhase(WeaponAnimationPhase.Attack);
    }

    protected void ClearWeaponAnimation()
    {
        animationPhase = WeaponAnimationPhase.None;
        if (owner != null)
        {
            owner.ClearWeaponAnimationPhase(this);
        }
    }

    protected void EnterWeaponBusyState()
    {
        if (owner != null)
        {
            owner.EnterBusyState(this);
        }
    }

    protected void ExitWeaponBusyStateAfterAnimation()
    {
        if (owner != null)
        {
            owner.ExitBusyStateAfterAnimation(this);
        }
    }

    protected void ExitWeaponBusyStateImmediately()
    {
        if (owner != null)
        {
            owner.ExitBusyStateImmediately(this);
        }
    }

    public void NotifyBusyAnimationCompleted()
    {
        animationPhase = WeaponAnimationPhase.None;
    }

    public virtual bool CanStartAttack()
    {
        return !isAttacking && cooldownTimer <= 0f;
    }

    public virtual void OnAttackPressed()
    {
        if (!CanStartAttack())
        {
            return;
        }

        isAttacking = true;
        owner?.UpdateWeaponAim(this);
        OnAttackStart();
        OnAttack();
    }

    public virtual void OnAttackHeld() { }

    public virtual void OnAttackReleased()
    {
        OnAttackEnd();
        isAttacking = false;
        ClearWeaponAnimation();
        cooldownTimer = attackCooldown;
    }

    private float scoreAttackStartedAt = float.NegativeInfinity;
    private float scoreStartMovementRatio;

    protected virtual void OnAttackStart()
    {
        if (owner == null)
            owner = GetComponentInParent<Player>();
        scoreAttackStartedAt = Time.time;
        scoreStartMovementRatio = owner != null ? owner.ScoreMovementRatio : 0f;
    }

    protected AttackScoreContext CreateScoreAttack()
    {
        if (owner == null)
            owner = GetComponentInParent<Player>();
        float ratio = owner != null ? owner.ScoreMovementRatio : 0f;
        if (owner != null && Time.time - scoreAttackStartedAt <= owner.ScoreMovementGraceSeconds)
            ratio = Mathf.Max(ratio, scoreStartMovementRatio);
        return new AttackScoreContext(owner, ratio);
    }
    protected abstract void OnAttack();
    protected virtual void OnAttackEnd() { }
}
