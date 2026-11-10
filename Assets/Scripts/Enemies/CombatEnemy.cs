using UnityEngine;

/// <summary>Shared movement and firing helpers; Enemy still owns health and defeat reporting.</summary>
public abstract class CombatEnemy : Enemy
{
    [Header("Projectile Settings")]
    [SerializeField, Min(1)] protected int projectileDamage = 1;
    [SerializeField, Min(0.1f)] protected float projectileLifetime = 6f;
    [SerializeField, Min(0f)] protected float muzzleOffset = 0.4f;

    private float nextTargetSearchTime;

    protected bool CanAct()
    {
        if (SceneLoadManager.IsBattleEntryLoading) return false;
        if (IsDead || currentHealth <= 0)
            return false;

        if (targetPlayer != null && targetPlayer.isActiveAndEnabled)
            return !targetPlayer.IsBattleEntryLocked;

        if (Time.time >= nextTargetSearchTime)
        {
            nextTargetSearchTime = Time.time + 0.5f;
            targetPlayer = null;
            foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
            {
                if (candidate.isActiveAndEnabled)
                {
                    targetPlayer = candidate;
                    break;
                }
            }
        }

        currentState = EnemyState.Idle;
        moveDirection = Vector2.zero;
        return targetPlayer != null && !targetPlayer.IsBattleEntryLocked;
    }

    protected void MoveTo(Vector2 destination, float speed)
    {
        Vector2 current = transform.position;
        Vector2 next = Vector2.MoveTowards(current, destination, Mathf.Max(0f, speed) * Time.deltaTime);
        Vector2 delta = next - current;
        moveDirection = delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector2.zero;
        transform.position = new Vector3(next.x, next.y, transform.position.z);
    }

    protected void Face(Vector2 direction, float degreesPerSecond = float.PositiveInfinity)
    {
        if (direction.sqrMagnitude <= 0.000001f)
            return;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float nextAngle = float.IsPositiveInfinity(degreesPerSecond)
            ? angle
            : Mathf.MoveTowardsAngle(transform.eulerAngles.z, angle, degreesPerSecond * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, 0f, nextAngle);
    }

    protected void Fire(Vector2 direction)
    {
        if (projectilePrefab == null || direction.sqrMagnitude <= 0.000001f)
            return;

        direction.Normalize();
        Vector3 origin = projectileSpawnPoint != null && projectileSpawnPoint != transform
            ? projectileSpawnPoint.position
            : transform.position + (Vector3)(direction * muzzleOffset);
        GameObject instance = Instantiate(projectilePrefab, origin,
            Quaternion.FromToRotation(Vector3.right, direction));
        ConfigureProjectile(instance, direction);
        Projectile projectile = instance.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.SetDamage(projectileDamage);
            projectile.SetLifetime(projectileLifetime);
        }
        OnProjectileFired(instance, direction);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        moveSpeed = Mathf.Max(0f, moveSpeed);
        projectileSpeed = Mathf.Max(0f, projectileSpeed);
        projectileDamage = Mathf.Max(1, projectileDamage);
        projectileLifetime = Mathf.Max(0.1f, projectileLifetime);
        muzzleOffset = Mathf.Max(0f, muzzleOffset);
    }

    // Only speed-type enemies need the player-centered orbit gizmos.
    protected override void OnDrawGizmos() { }
}
