using System;
using System.Collections.Generic;
using UnityEngine;

public class ExplosiveProjectile : Projectile
{
    [Header("Explosion")]
    [SerializeField] protected float explosionRadius = 1.5f;
    [SerializeField] protected LayerMask explosionLayers = ~0;
    [SerializeField] protected bool includeExplosionTriggers = true;
    [SerializeField] protected bool explodeOnHit = true;
    [SerializeField] protected bool destroyOnExplode = true;
    [SerializeField] protected int explosionBufferSize = 32;

    [Header("Effects")]
    [SerializeField] protected GameObject explosionEffectPrefab;
    [SerializeField] protected float explosionEffectLifetime = 2f;

    [Header("Debug")]
    [SerializeField] protected bool drawDebug;
    [SerializeField] protected Color debugExplosionColor = new Color(1f, 0.35f, 0.05f, 0.35f);

    public event Action<ExplosiveProjectile> Exploded;
    public event Action<ExplosiveProjectile> ProjectileDestroyed;

    protected Collider2D[] explosionResults;
    private readonly List<Component> damagedTargets = new List<Component>();
    private bool hasExploded;
    private bool destroyNotified;

    protected override void Awake()
    {
        base.Awake();
        EnsureExplosionBuffer();
    }

    protected virtual void OnValidate()
    {
        explosionRadius = Mathf.Max(0f, explosionRadius);
        explosionBufferSize = Mathf.Max(1, explosionBufferSize);
        explosionEffectLifetime = Mathf.Max(0f, explosionEffectLifetime);
    }

    protected override void OnHit(Component hitTarget)
    {
        if (explodeOnHit)
        {
            Explode();
            return;
        }

        base.OnHit(hitTarget);
    }

    public virtual void Explode()
    {
        if (hasExploded)
        {
            return;
        }

        hasExploded = true;
        ApplyExplosionDamage();
        SpawnExplosionEffect();
        Exploded?.Invoke(this);

        if (destroyOnExplode)
        {
            DestroyProjectile();
        }
    }

    protected virtual void ApplyExplosionDamage()
    {
        if (damage <= 0 || explosionRadius <= 0f)
        {
            return;
        }

        EnsureExplosionBuffer();
        damagedTargets.Clear();

        ContactFilter2D contactFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = explosionLayers,
            useTriggers = includeExplosionTriggers
        };

        int hitCount = Physics2D.OverlapCircle(
            transform.position,
            explosionRadius,
            contactFilter,
            explosionResults
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = explosionResults[i];
            if (hitCollider == null || IsOwnerCollider(hitCollider))
            {
                continue;
            }

            IDamageable damageable = hitCollider.GetComponentInParent<IDamageable>();
            if (damageable == null)
            {
                continue;
            }

            Component damageableComponent = damageable as Component;
            if (damageableComponent != null && damagedTargets.Contains(damageableComponent))
            {
                continue;
            }

            if (damageableComponent != null)
            {
                damagedTargets.Add(damageableComponent);
            }

            Vector2 hitPoint = hitCollider.ClosestPoint(transform.position);
            Vector2 hitDirection = (hitCollider.bounds.center - transform.position);
            if (hitDirection.sqrMagnitude <= 0.0001f)
            {
                hitDirection = direction;
            }

            GameObject source = ownerObject != null ? ownerObject : gameObject;
            CombatDamage.Apply(damageable, damage, hitPoint, hitDirection.normalized, source, ScoreContext);
        }
    }

    protected virtual void SpawnExplosionEffect()
    {
        if (explosionEffectPrefab == null)
        {
            return;
        }

        GameObject effect = Instantiate(
            explosionEffectPrefab,
            transform.position,
            Quaternion.identity
        );

        if (explosionEffectLifetime > 0f)
        {
            Destroy(effect, explosionEffectLifetime);
        }
    }

    protected virtual void EnsureExplosionBuffer()
    {
        int safeSize = Mathf.Max(1, explosionBufferSize);
        if (explosionResults == null || explosionResults.Length != safeSize)
        {
            explosionResults = new Collider2D[safeSize];
        }
    }

    protected override void DestroyProjectile()
    {
        NotifyDestroyed();
        base.DestroyProjectile();
    }

    protected virtual void OnDestroy()
    {
        NotifyDestroyed();
    }

    protected virtual void NotifyDestroyed()
    {
        if (destroyNotified)
        {
            return;
        }

        destroyNotified = true;
        ProjectileDestroyed?.Invoke(this);
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (!drawDebug)
        {
            return;
        }

        Gizmos.color = debugExplosionColor;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
