using UnityEngine;

public class MachineGunWeapon : ProjectileWeapon
{
    protected enum MachineGunState
    {
        Idle,
        Windup,
        Ready,
        Firing
    }

    [Header("Machine Gun")]
    [SerializeField] protected float windupDuration = 0.5f;
    [SerializeField] protected int burstShotCount = 20;
    [SerializeField] protected float attackSlowMultiplier = 0.45f;
    [SerializeField] protected AnimationCurve firingSlowReleaseCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] protected float firingAimTurnSpeed = 90f;

    protected MachineGunState state = MachineGunState.Idle;
    protected float windupTimer;
    protected float firingElapsedTime;
    protected float firingDuration;
    protected int remainingShots;
    protected bool isAttackButtonHeld;

    protected override void Update()
    {
        base.Update();

        switch (state)
        {
            case MachineGunState.Windup:
                TickWindup();
                break;
            case MachineGunState.Ready:
                if (!isAttackButtonHeld)
                {
                    BeginFiring();
                }
                break;
            case MachineGunState.Firing:
                TickFiring();
                break;
        }
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        windupDuration = Mathf.Max(0f, windupDuration);
        burstShotCount = Mathf.Max(1, burstShotCount);
        attackSlowMultiplier = Mathf.Clamp01(attackSlowMultiplier);
        firingAimTurnSpeed = Mathf.Max(0f, firingAimTurnSpeed);

        if (firingSlowReleaseCurve == null || firingSlowReleaseCurve.length == 0)
        {
            firingSlowReleaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ClearAttackState();
    }

    public override void OnAttackPressed()
    {
        if (!CanStartAttack())
        {
            return;
        }

        isAttacking = true;
        EnterWeaponBusyState();
        isAttackButtonHeld = true;
        state = MachineGunState.Windup;
        windupTimer = windupDuration;
        remainingShots = 0;
        fireTimer = 0f;
        ApplyAttackSlow();
        EnterChargeAnimation();
        OnAttackStart();

        if (windupTimer <= 0f)
        {
            FinishWindup();
        }
    }

    public override void OnAttackHeld()
    {
        if (isAttacking)
        {
            isAttackButtonHeld = true;
        }
    }

    public override void OnAttackReleased()
    {
        if (!isAttacking)
        {
            return;
        }

        isAttackButtonHeld = false;
        if (state == MachineGunState.Ready)
        {
            BeginFiring();
        }
    }

    protected virtual void TickWindup()
    {
        windupTimer -= Time.deltaTime;
        if (windupTimer <= 0f)
        {
            FinishWindup();
        }
    }

    protected virtual void FinishWindup()
    {
        if (isAttackButtonHeld)
        {
            state = MachineGunState.Ready;
            EnterHoldAnimation();
        }
        else
        {
            BeginFiring();
        }
    }

    protected virtual void BeginFiring()
    {
        if (!isAttacking || state == MachineGunState.Firing)
        {
            return;
        }

        state = MachineGunState.Firing;
        EnterReleaseAnimation();
        remainingShots = burstShotCount;
        fireTimer = 0f;
        firingElapsedTime = 0f;
        firingDuration = Mathf.Max(0f, (burstShotCount - 1) * fireInterval);
        ApplyAimTurnSpeedLimit(firingAimTurnSpeed);
        UpdateFiringSlow();
    }

    protected virtual void TickFiring()
    {
        firingElapsedTime += Time.deltaTime;
        UpdateFiringSlow();

        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f)
        {
            return;
        }

        OnAttack();
        remainingShots--;

        if (remainingShots <= 0)
        {
            CompleteAttack();
            return;
        }

        fireTimer = fireInterval;
    }

    protected virtual void CompleteAttack()
    {
        OnAttackEnd();
        RemoveAttackSlow();
        RemoveAimTurnSpeedLimit();
        ExitWeaponBusyStateAfterAnimation();
        state = MachineGunState.Idle;
        isAttacking = false;
        isAttackButtonHeld = false;
        windupTimer = 0f;
        remainingShots = 0;
        fireTimer = 0f;
        firingElapsedTime = 0f;
        firingDuration = 0f;
        cooldownTimer = attackCooldown;
    }

    protected virtual void ClearAttackState()
    {
        RemoveAttackSlow();
        RemoveAimTurnSpeedLimit();
        ClearWeaponAnimation();
        state = MachineGunState.Idle;
        isAttacking = false;
        isAttackButtonHeld = false;
        windupTimer = 0f;
        remainingShots = 0;
        fireTimer = 0f;
        firingElapsedTime = 0f;
        firingDuration = 0f;
    }

    protected virtual void ApplyAttackSlow()
    {
        if (owner != null)
        {
            owner.SetMovementSpeedModifier(this, attackSlowMultiplier);
        }
    }

    protected virtual void RemoveAttackSlow()
    {
        if (owner != null)
        {
            owner.RemoveMovementSpeedModifier(this);
        }
    }

    protected virtual void UpdateFiringSlow()
    {
        if (owner == null)
        {
            return;
        }

        float progress = firingDuration <= 0f
            ? 1f
            : Mathf.Clamp01(firingElapsedTime / firingDuration);
        float curveProgress = firingSlowReleaseCurve != null
            ? Mathf.Clamp01(firingSlowReleaseCurve.Evaluate(progress))
            : progress;
        float multiplier = Mathf.Lerp(attackSlowMultiplier, 1f, curveProgress);
        owner.SetMovementSpeedModifier(this, multiplier);
    }
}
