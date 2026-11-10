using UnityEngine;

public class ProjectileWeapon : Weapon
{
    [Header("Projectile")]
    [SerializeField] protected GameObject projectilePrefab;
    [SerializeField] protected float projectileSpeed = 18f;
    [SerializeField] protected float projectileLifetime = 3f;

    [Header("Fire")]
    [SerializeField] protected Transform[] muzzlePoints;
    [SerializeField] protected float fireInterval = 0.12f;
    [SerializeField] protected float spreadAngle = 0f;

    [Header("References")]
    [SerializeField] protected PlayerWeaponController aimController;

    [Header("Aim Compensation")]
    [SerializeField] protected bool useAimCompensation = true;

    protected ProjectileAimCompensator aimCompensator;
    protected float fireTimer;
    protected int nextMuzzleIndex;

    protected virtual void Awake()
    {
        CacheReferences();
    }

    protected virtual void Start()
    {
        CacheAimCompensator();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        projectileSpeed = Mathf.Max(0f, projectileSpeed);
        projectileLifetime = Mathf.Max(0f, projectileLifetime);
        fireInterval = Mathf.Max(0.01f, fireInterval);
        spreadAngle = Mathf.Max(0f, spreadAngle);
    }

    public override void Initialize(Player player)
    {
        base.Initialize(player);
        CacheReferences();
    }

    public override void OnAttackHeld()
    {
        if (!isAttacking)
        {
            return;
        }

        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f)
        {
            return;
        }

        OnAttack();
    }

    protected override void OnAttackStart()
    {
        base.OnAttackStart();
        fireTimer = 0f;
    }

    protected override void OnAttack()
    {
        FireProjectile();
        fireTimer = fireInterval;
    }

    protected virtual void CacheReferences()
    {
        if (owner == null)
        {
            owner = GetComponentInParent<Player>();
        }

        if (aimController == null)
        {
            aimController = GetComponentInParent<PlayerWeaponController>();
        }

        if (aimController == null && owner != null)
        {
            aimController = owner.GetComponent<PlayerWeaponController>();
        }

        CacheAimCompensator();
    }

    protected virtual void CacheAimCompensator()
    {
        if (aimCompensator != null)
        {
            return;
        }

        aimCompensator = GetComponentInParent<ProjectileAimCompensator>();

        if (aimCompensator == null && owner != null)
        {
            aimCompensator = owner.GetComponent<ProjectileAimCompensator>();
        }
    }

    protected virtual void FireProjectile()
    {
        if (projectilePrefab == null)
        {
            return;
        }

        owner?.UpdateWeaponAim(this);
        Transform muzzle = GetNextMuzzlePoint();
        Vector3 spawnPosition = muzzle != null ? muzzle.position : transform.position;
        Vector2 fireDirection = muzzle != null ? (Vector2)muzzle.right : (Vector2)transform.right;
        fireDirection = fireDirection.sqrMagnitude > 0.0001f
            ? fireDirection.normalized
            : Vector2.right;
        fireDirection = ApplySpread(fireDirection);

        GameObject projectile = Instantiate(
            projectilePrefab,
            spawnPosition,
            GetRotationForDirection(fireDirection)
        );

        ConfigureProjectile(projectile, fireDirection);
    }

    protected virtual Transform GetNextMuzzlePoint()
    {
        if (muzzlePoints == null || muzzlePoints.Length == 0)
        {
            return transform;
        }

        for (int i = 0; i < muzzlePoints.Length; i++)
        {
            int index = nextMuzzleIndex % muzzlePoints.Length;
            nextMuzzleIndex = (nextMuzzleIndex + 1) % muzzlePoints.Length;

            if (muzzlePoints[index] != null)
            {
                return muzzlePoints[index];
            }
        }

        return transform;
    }

    protected override Transform GetAimMuzzle()
    {
        if (muzzlePoints == null || muzzlePoints.Length == 0)
        {
            return transform;
        }

        for (int i = 0; i < muzzlePoints.Length; i++)
        {
            int index = (nextMuzzleIndex + i) % muzzlePoints.Length;
            if (muzzlePoints[index] != null)
            {
                return muzzlePoints[index];
            }
        }

        return transform;
    }

    protected override Vector2 GetDesiredAimPoint(Transform muzzle)
    {
        Vector2 rawAimPoint = base.GetDesiredAimPoint(muzzle);
        if (!useAimCompensation || aimCompensator == null || muzzle == null)
        {
            return rawAimPoint;
        }

        return aimCompensator.GetCompensatedAimPoint(
            muzzle.position,
            rawAimPoint,
            projectileSpeed
        );
    }

    protected virtual Vector2 GetFireDirection(Vector3 spawnPosition)
    {
        Transform muzzle = GetAimMuzzle();
        Vector2 direction = muzzle != null ? (Vector2)muzzle.right : (Vector2)transform.right;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
    }

    protected virtual Vector2 GetDirectionToAimPoint(Vector3 origin, Vector2 aimPoint)
    {
        Vector2 direction = aimPoint - (Vector2)origin;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            Vector2 fallbackDirection = transform.right;
            return fallbackDirection.sqrMagnitude > 0.0001f ? fallbackDirection.normalized : Vector2.right;
        }

        return direction.normalized;
    }

    protected virtual Vector2 ApplySpread(Vector2 direction)
    {
        if (spreadAngle <= 0.0001f)
        {
            return direction;
        }

        float randomAngle = Random.Range(-spreadAngle * 0.5f, spreadAngle * 0.5f);
        return (Vector2)(Quaternion.Euler(0f, 0f, randomAngle) * direction);
    }

    protected virtual Quaternion GetRotationForDirection(Vector2 direction)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, 0f, angle);
    }

    protected virtual void ConfigureProjectile(GameObject projectile, Vector2 direction)
    {
        Projectile projectileComponent = projectile.GetComponent<Projectile>();
        if (projectileComponent == null)
        {
            return;
        }

        projectileComponent.Configure(
            direction,
            projectileSpeed,
            projectileLifetime,
            damage,
            GetProjectileOwner()
        );
        projectileComponent.SetScoreContext(CreateScoreAttack());
    }

    protected virtual GameObject GetProjectileOwner()
    {
        if (owner != null)
        {
            return owner.gameObject;
        }

        if (aimController != null && aimController.Owner != null)
        {
            return aimController.Owner.gameObject;
        }

        return gameObject;
    }
}
