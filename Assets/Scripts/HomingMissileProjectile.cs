using UnityEngine;

public class HomingMissileProjectile : Projectile
{
    [Header("Homing")]
    [SerializeField, HideInInspector] protected Transform target;
    [SerializeField, HideInInspector] protected float turnSpeed = 360f;
    [SerializeField, HideInInspector] protected float homingDelay;
    [SerializeField] protected bool destroyWhenTargetLost;

    protected float homingTimer;

    protected override void Awake()
    {
        base.Awake();
        homingTimer = homingDelay;
    }

    protected override void Move()
    {
        TickHoming();
        base.Move();
    }

    public virtual void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public virtual void SetTurnSpeed(float newTurnSpeed)
    {
        turnSpeed = Mathf.Max(0f, newTurnSpeed);
    }

    public virtual void SetHomingDelay(float newHomingDelay)
    {
        homingDelay = Mathf.Max(0f, newHomingDelay);
        homingTimer = homingDelay;
    }

    public virtual void ConfigureHomingMissile(
        Vector2 newDirection,
        float newSpeed,
        float newLifetime,
        int newDamage,
        GameObject newOwner,
        Transform newTarget,
        float newTurnSpeed,
        float newHomingDelay
    )
    {
        Configure(newDirection, newSpeed, newLifetime, newDamage, newOwner);
        SetTarget(newTarget);
        SetTurnSpeed(newTurnSpeed);
        SetHomingDelay(newHomingDelay);
    }

    protected virtual void TickHoming()
    {
        if (homingTimer > 0f)
        {
            homingTimer -= Time.deltaTime;
            return;
        }

        if (target == null)
        {
            if (destroyWhenTargetLost)
            {
                DestroyProjectile();
            }

            return;
        }

        Vector2 toTarget = (Vector2)(target.position - transform.position);
        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        direction = Vector3.RotateTowards(
            direction,
            toTarget.normalized,
            turnSpeed * Mathf.Deg2Rad * Time.deltaTime,
            0f
        ).normalized;

        transform.rotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
        );
    }
}
