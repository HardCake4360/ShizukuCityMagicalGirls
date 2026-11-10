using UnityEngine;

public class BazookaWeapon : ProjectileWeapon
{
    [Header("Bazooka")]
    [SerializeField] protected bool allowRemoteDetonation = true;

    protected ExplosiveProjectile activeProjectile;

    public override void OnAttackPressed()
    {
        if (TryDetonateActiveProjectile())
        {
            return;
        }

        if (!CanStartAttack())
        {
            return;
        }

        isAttacking = true;
        OnAttackStart();
        OnAttack();
    }

    public override void OnAttackHeld()
    {
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ClearActiveProjectile();
    }

    protected override void ConfigureProjectile(GameObject projectile, Vector2 direction)
    {
        base.ConfigureProjectile(projectile, direction);

        ExplosiveProjectile explosiveProjectile = projectile.GetComponent<ExplosiveProjectile>();
        if (explosiveProjectile != null)
        {
            RegisterActiveProjectile(explosiveProjectile);
        }
    }

    protected virtual bool TryDetonateActiveProjectile()
    {
        if (!allowRemoteDetonation || activeProjectile == null)
        {
            return false;
        }

        ExplosiveProjectile projectileToDetonate = activeProjectile;
        ClearActiveProjectile();
        projectileToDetonate.Explode();
        return true;
    }

    protected virtual void RegisterActiveProjectile(ExplosiveProjectile projectile)
    {
        ClearActiveProjectile();

        activeProjectile = projectile;
        activeProjectile.Exploded += OnActiveProjectileFinished;
        activeProjectile.ProjectileDestroyed += OnActiveProjectileFinished;
    }

    protected virtual void ClearActiveProjectile()
    {
        if (activeProjectile == null)
        {
            return;
        }

        activeProjectile.Exploded -= OnActiveProjectileFinished;
        activeProjectile.ProjectileDestroyed -= OnActiveProjectileFinished;
        activeProjectile = null;
    }

    protected virtual void OnActiveProjectileFinished(ExplosiveProjectile projectile)
    {
        if (projectile != activeProjectile)
        {
            return;
        }

        ClearActiveProjectile();
    }
}
