using System.Collections.Generic;
using UnityEngine;

public class SniperRifleWeapon : ProjectileWeapon
{
    protected enum SniperState
    {
        Idle,
        Charging,
        Holding
    }

    [Header("Sniper Charge")]
    [SerializeField] protected float holdSlowMultiplier = 0.35f;

    [Header("Sniper Explosion")]
    [SerializeField] protected int explosionDamage = 3;
    [SerializeField] protected float explosionRadius = 1.5f;
    [SerializeField] protected LayerMask explosionLayers = ~0;
    [SerializeField] protected bool includeExplosionTriggers = true;
    [SerializeField] protected int explosionBufferSize = 32;
    [SerializeField] protected GameObject explosionEffectPrefab;
    [SerializeField] protected float explosionEffectLifetime = 2f;

    [Header("Sniper Debug")]
    [SerializeField] protected bool drawExplosionDebug;
    [SerializeField] protected Color explosionDebugColor =
        new Color(1f, 0.35f, 0.05f, 0.35f);

    protected SniperState sniperState;
    protected bool isAttackButtonHeld;
    protected int chargeAnimationStartFrame;
    protected Collider2D[] explosionResults;
    protected readonly HashSet<IDamageable> explosionTargets = new HashSet<IDamageable>();

    protected override void Awake()
    {
        base.Awake();
        EnsureExplosionBuffer();
    }

    protected override void Update()
    {
        base.Update();

        if (sniperState == SniperState.Charging
            && Time.frameCount > chargeAnimationStartFrame
            && (owner == null
                || owner.IsWeaponAnimationFinished(this, WeaponAnimationPhase.Charge)))
        {
            EnterHolding();
        }
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        holdSlowMultiplier = Mathf.Clamp01(holdSlowMultiplier);
        explosionDamage = Mathf.Max(0, explosionDamage);
        explosionRadius = Mathf.Max(0f, explosionRadius);
        explosionBufferSize = Mathf.Max(1, explosionBufferSize);
        explosionEffectLifetime = Mathf.Max(0f, explosionEffectLifetime);
    }

    protected override void OnDisable()
    {
        ClearSniperState();
        base.OnDisable();
    }

    public override bool CanStartAttack()
    {
        return sniperState == SniperState.Idle && !isAttacking && cooldownTimer <= 0f;
    }

    public override void OnAttackPressed()
    {
        if (!CanStartAttack())
        {
            return;
        }

        isAttacking = true;
        isAttackButtonHeld = true;
        sniperState = SniperState.Charging;
        chargeAnimationStartFrame = Time.frameCount;
        EnterWeaponBusyState();
        EnterChargeAnimation();
        OnAttackStart();
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
        FireChargedShot();
    }

    protected virtual void EnterHolding()
    {
        if (sniperState != SniperState.Charging)
        {
            return;
        }

        sniperState = SniperState.Holding;
        EnterHoldAnimation();
        ApplyHoldSlow();

        if (!isAttackButtonHeld)
        {
            FireChargedShot();
        }
    }

    protected virtual void FireChargedShot()
    {
        if (!isAttacking
            || (sniperState != SniperState.Charging && sniperState != SniperState.Holding))
        {
            return;
        }

        CacheReferences();
        Vector2 aimDirection = playerWeaponController != null
            ? playerWeaponController.GetAimDirection()
            : (Vector2)transform.right;
        if (aimDirection.sqrMagnitude <= 0.0001f)
        {
            aimDirection = Vector2.right;
        }
        aimDirection.Normalize();

        RemoveHoldSlow();
        if (owner != null)
        {
            owner.SetMovementDirection(aimDirection, true);
            owner.UpdateWeaponAim(this);
        }

        EnterReleaseAnimation();
        FireProjectile();
        OnAttackEnd();

        sniperState = SniperState.Idle;
        isAttacking = false;
        isAttackButtonHeld = false;
        cooldownTimer = attackCooldown;
        ExitWeaponBusyStateAfterAnimation();
    }

    protected override void ConfigureProjectile(GameObject projectile, Vector2 direction)
    {
        base.ConfigureProjectile(projectile, direction);

        SniperProjectile sniperProjectile = projectile.GetComponent<SniperProjectile>();
        if (sniperProjectile != null)
        {
            AttackScoreContext context = sniperProjectile.ScoreContext;
            sniperProjectile.SetEnemyImpactCallback(point => HandleEnemyImpactWithScore(point, context));
        }
    }

    private AttackScoreContext explosionScoreContext;

    private void HandleEnemyImpactWithScore(Vector2 point, AttackScoreContext context)
    {
        AttackScoreContext previous = explosionScoreContext;
        explosionScoreContext = context;
        try { HandleEnemyImpact(point); }
        finally { explosionScoreContext = previous; }
    }

    protected virtual void HandleEnemyImpact(Vector2 impactPoint)
    {
        ApplyExplosionDamage(impactPoint);
        SpawnExplosionEffect(impactPoint);
    }

    protected virtual void ApplyExplosionDamage(Vector2 center)
    {
        if (explosionDamage <= 0 || explosionRadius <= 0f)
        {
            return;
        }

        EnsureExplosionBuffer();
        AttackScoreContext context = explosionScoreContext.Id != 0 ? explosionScoreContext : CreateScoreAttack();
        explosionTargets.Clear();
        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = explosionLayers,
            useTriggers = includeExplosionTriggers
        };

        int hitCount = Physics2D.OverlapCircle(
            center,
            explosionRadius,
            filter,
            explosionResults
        );
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = explosionResults[i];
            if (hitCollider == null
                || (owner != null
                    && (hitCollider.transform == owner.transform
                        || hitCollider.transform.IsChildOf(owner.transform))))
            {
                continue;
            }

            IDamageable damageable = hitCollider.GetComponentInParent<IDamageable>();
            if (damageable == null || !explosionTargets.Add(damageable))
            {
                continue;
            }

            Vector2 hitPoint = hitCollider.ClosestPoint(center);
            Vector2 hitDirection = (Vector2)hitCollider.bounds.center - center;
            if (hitDirection.sqrMagnitude <= 0.0001f)
            {
                hitDirection = Vector2.up;
            }

            CombatDamage.Apply(damageable,
                explosionDamage,
                hitPoint,
                hitDirection.normalized,
                GetProjectileOwner(), context
            );
        }

        explosionTargets.Clear();
    }

    protected virtual void SpawnExplosionEffect(Vector2 position)
    {
        if (explosionEffectPrefab == null)
        {
            return;
        }

        GameObject effect = Instantiate(explosionEffectPrefab, position, Quaternion.identity);
        if (explosionEffectLifetime > 0f)
        {
            Destroy(effect, explosionEffectLifetime);
        }
    }

    protected virtual void ApplyHoldSlow()
    {
        owner?.SetMovementSpeedModifier(this, holdSlowMultiplier);
    }

    protected virtual void RemoveHoldSlow()
    {
        owner?.RemoveMovementSpeedModifier(this);
    }

    protected virtual void ClearSniperState()
    {
        RemoveHoldSlow();
        sniperState = SniperState.Idle;
        isAttacking = false;
        isAttackButtonHeld = false;
        explosionTargets.Clear();
    }

    protected virtual void EnsureExplosionBuffer()
    {
        int safeSize = Mathf.Max(1, explosionBufferSize);
        if (explosionResults == null || explosionResults.Length != safeSize)
        {
            explosionResults = new Collider2D[safeSize];
        }
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (!drawExplosionDebug)
        {
            return;
        }

        Gizmos.color = explosionDebugColor;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
