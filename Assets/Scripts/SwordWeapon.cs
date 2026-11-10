using System.Collections.Generic;
using UnityEngine;

public class SwordWeapon : Weapon, ISpecialAttackWeapon
{
    protected enum SwordState
    {
        Idle,
        SlashWindup,
        SlashRecovery,
        ThrustWindup,
        ThrustReady,
        Thrusting
    }

    [Header("Slash")]
    [SerializeField] protected Transform slashOrigin;
    [SerializeField] protected float slashRadius = 2f;
    [SerializeField, Range(0f, 360f)] protected float slashAngle = 100f;
    [SerializeField, Range(-180f, 180f)] protected float slashAngleOffset;
    [SerializeField] protected float slashWindupDuration = 0.1f;
    [SerializeField] protected float slashRecoveryDuration = 0.25f;

    [Header("Thrust")]
    [SerializeField] protected float thrustWindupDuration = 0.12f;
    [SerializeField] protected float thrustSpeed = 28f;
    [SerializeField] protected float thrustDuration = 0.2f;
    [SerializeField] protected float thrustWidth = 0.8f;
    [SerializeField] protected float thrustExitMomentumMultiplier = 0.6f;
    [SerializeField] protected float specialAttackCooldown = 1.5f;

    [Header("Hit Detection")]
    [SerializeField] protected LayerMask enemyLayers = ~0;
    [SerializeField] protected bool includeTriggers = true;
    [SerializeField] protected int hitBufferSize = 32;

    [Header("Debug")]
    [SerializeField] protected bool drawDebug;
    [SerializeField] protected Color slashDebugColor = Color.cyan;
    [SerializeField] protected Color thrustDebugColor = Color.red;

    protected SwordState state;
    protected Collider2D[] hitResults;
    protected readonly HashSet<IDamageable> hitTargets = new HashSet<IDamageable>();
    protected float stateTimer;
    protected float specialCooldownTimer;
    protected Vector2 slashAimDirection = Vector2.right;
    protected Vector2 thrustDirection = Vector2.right;
    protected Vector2 previousThrustPosition;
    private AttackScoreContext scoreContext;
    protected bool isSpecialAttackButtonHeld;

    protected virtual void Awake()
    {
        CacheReferences();
        EnsureHitBuffer();
    }

    protected override void Update()
    {
        base.Update();

        if (specialCooldownTimer > 0f)
        {
            specialCooldownTimer -= Time.deltaTime;
        }

        switch (state)
        {
            case SwordState.SlashWindup:
                TickSlashWindup();
                break;
            case SwordState.SlashRecovery:
                TickSlashRecovery();
                break;
            case SwordState.ThrustWindup:
                TickThrustWindup();
                break;
            case SwordState.ThrustReady:
                if (!isSpecialAttackButtonHeld)
                {
                    BeginThrust();
                }
                break;
            case SwordState.Thrusting:
                TickThrust();
                break;
        }
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        slashRadius = Mathf.Max(0f, slashRadius);
        slashAngle = Mathf.Clamp(slashAngle, 0f, 360f);
        slashAngleOffset = Mathf.Clamp(slashAngleOffset, -180f, 180f);
        slashWindupDuration = Mathf.Max(0f, slashWindupDuration);
        slashRecoveryDuration = Mathf.Max(0f, slashRecoveryDuration);
        thrustWindupDuration = Mathf.Max(0f, thrustWindupDuration);
        thrustSpeed = Mathf.Max(0f, thrustSpeed);
        thrustDuration = Mathf.Max(0.01f, thrustDuration);
        thrustWidth = Mathf.Max(0.01f, thrustWidth);
        thrustExitMomentumMultiplier = Mathf.Max(0f, thrustExitMomentumMultiplier);
        specialAttackCooldown = Mathf.Max(0f, specialAttackCooldown);
        hitBufferSize = Mathf.Max(1, hitBufferSize);
    }

    public override void Initialize(Player player)
    {
        base.Initialize(player);
        CacheReferences();
    }

    protected override void OnDisable()
    {
        if (owner != null)
        {
            owner.RemoveVisualDirectionOverride(this);
            owner.CancelVelocityOverride(this, false);
        }

        state = SwordState.Idle;
        isAttacking = false;
        isSpecialAttackButtonHeld = false;
        hitTargets.Clear();
        base.OnDisable();
    }

    public override bool CanStartAttack()
    {
        return state == SwordState.Idle && !isAttacking && cooldownTimer <= 0f;
    }

    public override void OnAttackPressed()
    {
        if (!CanStartAttack())
        {
            return;
        }

        isAttacking = true;
        slashAimDirection = GetAimDirection();
        if (owner != null)
        {
            owner.SetVisualDirectionOverride(this, slashAimDirection);
        }

        state = SwordState.SlashWindup;
        stateTimer = slashWindupDuration;
        EnterAttackAnimation();
        OnAttackStart();

        if (stateTimer <= 0f)
        {
            FinishSlash();
        }
    }

    public override void OnAttackReleased()
    {
        // Sword attacks are committed when pressed and cannot be canceled by release.
    }

    public void OnSpecialAttackPressed()
    {
        if (state != SwordState.Idle || isAttacking || specialCooldownTimer > 0f)
        {
            return;
        }

        isAttacking = true;
        EnterWeaponBusyState();
        isSpecialAttackButtonHeld = true;
        state = SwordState.ThrustWindup;
        stateTimer = thrustWindupDuration;
        EnterChargeAnimation();
        OnAttackStart();

        if (stateTimer <= 0f)
        {
            FinishThrustWindup();
        }
    }

    public void OnSpecialAttackHeld()
    {
        if (isAttacking
            && (state == SwordState.ThrustWindup || state == SwordState.ThrustReady))
        {
            isSpecialAttackButtonHeld = true;
        }
    }

    public void OnSpecialAttackReleased()
    {
        if (!isAttacking
            || (state != SwordState.ThrustWindup && state != SwordState.ThrustReady))
        {
            return;
        }

        isSpecialAttackButtonHeld = false;
        if (state == SwordState.ThrustReady)
        {
            BeginThrust();
        }
    }

    protected override void OnAttack()
    {
        PerformSlash();
    }

    protected virtual void TickSlashWindup()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            FinishSlash();
        }
    }

    protected virtual void FinishSlash()
    {
        OnAttack();
        state = SwordState.SlashRecovery;
        stateTimer = slashRecoveryDuration;

        if (stateTimer <= 0f)
        {
            CompleteSlash();
        }
    }

    protected virtual void TickSlashRecovery()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            CompleteSlash();
        }
    }

    protected virtual void CompleteSlash()
    {
        OnAttackEnd();
        ClearWeaponAnimation();
        if (owner != null)
        {
            owner.RemoveVisualDirectionOverride(this);
        }

        state = SwordState.Idle;
        isAttacking = false;
        stateTimer = 0f;
        cooldownTimer = attackCooldown;
    }

    protected virtual void TickThrustWindup()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            FinishThrustWindup();
        }
    }

    protected virtual void FinishThrustWindup()
    {
        if (isSpecialAttackButtonHeld)
        {
            state = SwordState.ThrustReady;
            stateTimer = 0f;
            EnterHoldAnimation();
        }
        else
        {
            BeginThrust();
        }
    }

    protected virtual void BeginThrust()
    {
        scoreContext = CreateScoreAttack();
        CacheReferences();
        thrustDirection = GetAimDirection();
        isSpecialAttackButtonHeld = false;
        EnterReleaseAnimation();
        if (owner != null)
        {
            owner.RemoveVisualDirectionOverride(this);
            owner.SetMovementDirection(thrustDirection, true);
        }

        previousThrustPosition = owner != null ? (Vector2)owner.transform.position : transform.position;
        stateTimer = thrustDuration;
        hitTargets.Clear();

        if (owner == null || !owner.StartVelocityOverride(
            this,
            thrustDirection,
            thrustSpeed,
            thrustDuration,
            thrustExitMomentumMultiplier
        ))
        {
            CompleteThrust();
            return;
        }

        state = SwordState.Thrusting;
        ProcessThrustPath(previousThrustPosition, previousThrustPosition);
    }

    protected virtual void TickThrust()
    {
        Vector2 currentPosition = owner != null ? (Vector2)owner.transform.position : transform.position;
        ProcessThrustPath(previousThrustPosition, currentPosition);
        previousThrustPosition = currentPosition;

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f || owner == null || !owner.IsVelocityOverrideActive(this))
        {
            CompleteThrust();
        }
    }

    protected virtual void CompleteThrust()
    {
        if (owner != null)
        {
            owner.CancelVelocityOverride(this, true);
        }

        OnAttackEnd();
        state = SwordState.Idle;
        isAttacking = false;
        isSpecialAttackButtonHeld = false;
        stateTimer = 0f;
        specialCooldownTimer = specialAttackCooldown;
        hitTargets.Clear();
        ExitWeaponBusyStateAfterAnimation();
    }

    protected virtual void PerformSlash()
    {
        scoreContext = CreateScoreAttack();
        EnsureHitBuffer();
        hitTargets.Clear();
        Vector2 origin = GetSlashOriginPosition();
        Vector2 slashDirection = GetSlashDirection();
        int hitCount = Physics2D.OverlapCircle(origin, slashRadius, CreateEnemyFilter(), hitResults);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = hitResults[i];
            if (!TryGetDamageable(hitCollider, out IDamageable damageable) || hitTargets.Contains(damageable))
            {
                continue;
            }

            Vector2 toTarget = (Vector2)hitCollider.bounds.center - origin;
            if (slashAngle < 359.9f && toTarget.sqrMagnitude > 0.0001f
                && Vector2.Angle(slashDirection, toTarget) > slashAngle * 0.5f)
            {
                continue;
            }

            hitTargets.Add(damageable);
            Vector2 hitPoint = hitCollider.ClosestPoint(origin);
            CombatDamage.Apply(damageable, damage, hitPoint, slashDirection, GetDamageSource(), scoreContext);
        }

        hitTargets.Clear();
    }

    protected virtual void ProcessThrustPath(Vector2 start, Vector2 end)
    {
        EnsureHitBuffer();
        Vector2 movement = end - start;
        float distance = movement.magnitude;
        Vector2 direction = distance > 0.0001f ? movement / distance : thrustDirection;
        Vector2 center = (start + end) * 0.5f;
        Vector2 boxSize = new Vector2(Mathf.Max(thrustWidth, distance + thrustWidth), thrustWidth);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        int hitCount = Physics2D.OverlapBox(center, boxSize, angle, CreateEnemyFilter(), hitResults);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = hitResults[i];
            if (!TryGetDamageable(hitCollider, out IDamageable damageable) || hitTargets.Contains(damageable))
            {
                continue;
            }

            hitTargets.Add(damageable);
            Vector2 hitPoint = hitCollider.ClosestPoint(center);
            CombatDamage.Apply(damageable, int.MaxValue, hitPoint, thrustDirection, GetDamageSource(), scoreContext);
        }
    }

    protected virtual bool TryGetDamageable(Collider2D hitCollider, out IDamageable damageable)
    {
        damageable = null;
        if (hitCollider == null || owner == null
            || hitCollider.transform == owner.transform
            || hitCollider.transform.IsChildOf(owner.transform))
        {
            return false;
        }

        damageable = hitCollider.GetComponentInParent<IDamageable>();
        return damageable != null;
    }

    protected virtual Vector2 GetAimDirection()
    {
        CacheReferences();
        Vector3 origin = playerWeaponController != null
            ? playerWeaponController.AimOrigin.position
            : transform.position;
        return GetSafeAimDirection(origin, playerWeaponController);
    }

    protected virtual Vector2 GetSlashDirection()
    {
        Vector2 baseDirection = state == SwordState.SlashWindup
            || state == SwordState.SlashRecovery
            ? slashAimDirection
            : GetAimDirection();
        return (Vector2)(Quaternion.Euler(0f, 0f, slashAngleOffset) * baseDirection);
    }

    protected virtual Vector2 GetSlashOriginPosition()
    {
        if (slashOrigin != null)
        {
            return slashOrigin.position;
        }

        return owner != null ? owner.transform.position : transform.position;
    }

    protected virtual GameObject GetDamageSource()
    {
        return owner != null ? owner.gameObject : gameObject;
    }

    protected virtual ContactFilter2D CreateEnemyFilter()
    {
        return new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = enemyLayers,
            useTriggers = includeTriggers
        };
    }

    protected virtual void CacheReferences()
    {
        if (owner == null)
        {
            owner = GetComponentInParent<Player>();
        }

        CachePlayerWeaponController();
    }

    protected virtual void EnsureHitBuffer()
    {
        if (hitResults == null || hitResults.Length != hitBufferSize)
        {
            hitResults = new Collider2D[Mathf.Max(1, hitBufferSize)];
        }
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (!drawDebug)
        {
            return;
        }

        Vector2 origin = slashOrigin != null ? slashOrigin.position : transform.position;
        Vector2 aimDirection = Application.isPlaying ? GetSlashDirection() :
            (Vector2)(Quaternion.Euler(0f, 0f, slashAngleOffset) * transform.right);
        Gizmos.color = slashDebugColor;
        Gizmos.DrawWireSphere(origin, slashRadius);
        Gizmos.DrawLine(origin, origin + (Vector2)(Quaternion.Euler(0f, 0f, slashAngle * 0.5f) * aimDirection) * slashRadius);
        Gizmos.DrawLine(origin, origin + (Vector2)(Quaternion.Euler(0f, 0f, -slashAngle * 0.5f) * aimDirection) * slashRadius);

        Gizmos.color = thrustDebugColor;
        Gizmos.DrawLine(origin, origin + aimDirection * thrustSpeed * thrustDuration);
    }
}
