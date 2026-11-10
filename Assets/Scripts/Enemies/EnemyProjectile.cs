using UnityEngine;

/// <summary>Reuses swept high-speed collision, but never damages friendly enemies.</summary>
public sealed class EnemyProjectile : SniperProjectile
{
    protected override bool IsOwnerCollider(Collider2D other)
    {
        return base.IsOwnerCollider(other)
            || (other != null && (other.GetComponentInParent<Enemy>() != null
                || other.GetComponentInParent<Projectile>() != null));
    }
}
