using System;
using UnityEngine;

public class SniperProjectile : Projectile
{
    [Header("High Speed Collision")]
    [SerializeField] protected int sweepBufferSize = 16;

    protected RaycastHit2D[] sweepResults;
    protected Action<Vector2> enemyImpactCallback;
    protected bool impactHandled;

    protected override void Awake()
    {
        base.Awake();
        EnsureSweepBuffer();
    }

    protected virtual void OnValidate()
    {
        sweepBufferSize = Mathf.Max(1, sweepBufferSize);
    }

    protected override void Move()
    {
        float distance = Mathf.Max(0f, speed * Time.deltaTime);
        if (distance <= 0f || impactHandled)
        {
            return;
        }

        EnsureSweepBuffer();
        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = hitLayers,
            useTriggers = true
        };
        int hitCount = Physics2D.Raycast(
            transform.position,
            direction,
            filter,
            sweepResults,
            distance
        );

        RaycastHit2D nearestHit = default;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = sweepResults[i];
            if (hit.collider == null
                || IsOwnerCollider(hit.collider)
                || hit.collider.GetComponentInParent<Projectile>() != null
                || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestHit = hit;
            nearestDistance = hit.distance;
        }

        if (nearestHit.collider != null)
        {
            transform.position = nearestHit.point;
            HandleImpact(nearestHit.collider, nearestHit.point);
            return;
        }

        transform.position += (Vector3)(direction * distance);
    }

    protected override void OnHit(Component hitTarget)
    {
        Vector2 hitPoint = transform.position;
        if (hitTarget is Collider2D hitCollider)
        {
            hitPoint = hitCollider.ClosestPoint(transform.position);
        }

        HandleImpact(hitTarget, hitPoint);
    }

    protected virtual void HandleImpact(Component hitTarget, Vector2 hitPoint)
    {
        if (impactHandled || hitTarget == null)
        {
            return;
        }

        impactHandled = true;
        bool hitEnemy = hitTarget.GetComponentInParent<IDamageable>() != null;
        ApplyDamage(hitTarget);
        if (hitEnemy)
        {
            enemyImpactCallback?.Invoke(hitPoint);
        }

        if (destroyOnHit)
        {
            DestroyProjectile();
        }
    }

    public virtual void SetEnemyImpactCallback(Action<Vector2> callback)
    {
        enemyImpactCallback = callback;
    }

    protected virtual void EnsureSweepBuffer()
    {
        int safeSize = Mathf.Max(1, sweepBufferSize);
        if (sweepResults == null || sweepResults.Length != safeSize)
        {
            sweepResults = new RaycastHit2D[safeSize];
        }
    }
}
