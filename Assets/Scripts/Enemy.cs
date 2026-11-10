using UnityEngine;

public class Enemy : MonoBehaviour, IDamageable
{
    public static event System.Action<Enemy, GameObject> Defeated;
    public static event System.Action<Enemy, GameObject> DamageReceived;
    private AttackScoreContext pendingScoreContext;
    public AttackScoreContext LastScoreContext { get; private set; }
    public bool LastHitWasFlank { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetScoreEvents() => DamageReceived = null;

    public void TakeAttackDamage(int amount, Vector2 point, Vector2 direction, GameObject source,
        AttackScoreContext context)
    {
        AttackScoreContext previous = pendingScoreContext;
        pendingScoreContext = context;
        try { TakeDamage(amount, point, direction, source); }
        finally { pendingScoreContext = previous; }
    }

    protected virtual bool IsFlankHit(Vector2 point, Vector2 direction, GameObject source) => false;

    public enum EnemyState
    {
        Idle,
        Moving,
        Cooldown
    }

    [Header("References")]
    [SerializeField] protected Player targetPlayer;
    [SerializeField] protected Transform projectileSpawnPoint;
    [SerializeField] protected GameObject projectilePrefab;
    [SerializeField] protected FCS fireControlSystem;

    [Header("Movement")]
    [SerializeField] protected float moveSpeed = 3f;
    [SerializeField] protected float moveLerpSpeed = 8f;
    [SerializeField] protected float targetReachDistance = 0.5f;
    [SerializeField] protected float orbitMinRadius = 2f;
    [SerializeField] protected float orbitMaxRadius = 5f;

    [Header("Attack")]
    [SerializeField] protected float attackDelay = 0.15f;
    [SerializeField] protected float attackCooldown = 0.4f;
    [SerializeField] protected float projectileSpeed = 8f;

    [Header("Health")]
    [SerializeField] protected int maxHealth = 3;
    [SerializeField] protected int currentHealth;
    [SerializeField] protected bool destroyOnDeath = true;

    [Header("Hit Feedback")]
    [SerializeField] protected GameObject hitEffectPrefab;
    [SerializeField] protected Transform hitEffectSpawnPoint;
    [SerializeField] protected bool parentHitEffectToEnemy;
    [SerializeField] protected bool rotateHitEffectToHitDirection = true;
    [SerializeField] protected Vector2 hitEffectPositionOffset;
    [SerializeField] protected float hitEffectRandomOffsetRadius = 0.08f;
    [SerializeField] protected float hitEffectInterval = 0.05f;

    [Header("Debug")]
    [SerializeField] protected EnemyState currentState = EnemyState.Idle;
    [SerializeField] protected Vector2 moveDirection = Vector2.zero;
    [SerializeField] protected Vector2 lockedAttackDirection = Vector2.right;
    [SerializeField] protected Vector2 currentMoveOffset;
    [SerializeField] protected Vector2 currentMoveTarget;

    protected float attackDelayTimer;
    protected float cooldownTimer;
    protected bool isAttackQueued;
    protected bool shouldPickNewTargetAfterCooldown;
    protected bool hasMoveTarget;
    protected float lastHitEffectTime = -Mathf.Infinity;
    private bool isDead;

    public bool IsDead => isDead;

    protected virtual void Awake()
    {
        if (currentHealth <= 0)
        {
            currentHealth = maxHealth;
        }

        if (projectileSpawnPoint == null)
        {
            projectileSpawnPoint = transform;
        }

        if (targetPlayer == null)
        {
            targetPlayer = FindFirstObjectByType<Player>();
        }

        if (fireControlSystem == null)
        {
            fireControlSystem = GetComponent<FCS>();
        }
    }

    protected virtual void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        hitEffectRandomOffsetRadius = Mathf.Max(0f, hitEffectRandomOffsetRadius);
        hitEffectInterval = Mathf.Max(0f, hitEffectInterval);
    }

    protected virtual void Update()
    {
        if (SceneLoadManager.IsBattleEntryLoading) return;
        if (targetPlayer == null || !targetPlayer.isActiveAndEnabled)
        {
            targetPlayer = null;
            foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
                if (candidate.isActiveAndEnabled && candidate.gameObject.scene == gameObject.scene)
                { targetPlayer = candidate; break; }
        }
        if (targetPlayer != null && targetPlayer.IsBattleEntryLocked) return;
        TickTimers();
        UpdateState();
        ActByState();
    }

    protected virtual void TickTimers()
    {
        if (attackDelayTimer > 0f)
        {
            attackDelayTimer -= Time.deltaTime;
        }

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }
    }

    protected virtual void UpdateState()
    {
        if (targetPlayer == null)
        {
            targetPlayer = FindFirstObjectByType<Player>();
        }

        if (targetPlayer == null)
        {
            currentState = EnemyState.Idle;
            moveDirection = Vector2.zero;
            hasMoveTarget = false;
            return;
        }

        if (!hasMoveTarget)
        {
            PickNewMoveTarget();
        }

        if (isAttackQueued)
        {
            if (attackDelayTimer <= 0f)
            {
                FireLockedProjectile();
                isAttackQueued = false;
                cooldownTimer = attackCooldown;
                currentState = EnemyState.Cooldown;
            }
        }

        if (currentState == EnemyState.Cooldown)
        {
            if (cooldownTimer <= 0f)
            {
                if (shouldPickNewTargetAfterCooldown)
                {
                    PickNewMoveTarget();
                    shouldPickNewTargetAfterCooldown = false;
                }

                currentState = EnemyState.Moving;
            }
        }
        else if (!isAttackQueued)
        {
            currentState = EnemyState.Moving;
        }
    }

    protected virtual void ActByState()
    {
        if (currentState == EnemyState.Moving || currentState == EnemyState.Cooldown)
        {
            MoveTowardsTarget();
            return;
        }

        moveDirection = Vector2.zero;
    }

    protected virtual void MoveTowardsTarget()
    {
        if (targetPlayer == null)
        {
            return;
        }

        if (!hasMoveTarget)
        {
            PickNewMoveTarget();
        }

        currentMoveTarget = GetCurrentMoveTarget();
        Vector2 currentPosition = transform.position;
        Vector2 toTarget = currentMoveTarget - currentPosition;
        float distanceToTarget = toTarget.magnitude;

        if (!shouldPickNewTargetAfterCooldown && distanceToTarget <= targetReachDistance)
        {
            TryAttackWhileMoving();
        }

        Vector2 smoothedTargetPosition = Vector2.Lerp(
            currentPosition,
            currentMoveTarget,
            Mathf.Clamp01(moveLerpSpeed * Time.deltaTime)
        );
        Vector2 nextPosition = Vector2.MoveTowards(
            currentPosition,
            smoothedTargetPosition,
            moveSpeed * Time.deltaTime
        );

        Vector2 moveDelta = nextPosition - currentPosition;
        moveDirection = moveDelta.sqrMagnitude > 0.0001f ? moveDelta.normalized : toTarget.normalized;
        transform.position = nextPosition;
    }

    protected virtual void BeginAttack()
    {
        if (cooldownTimer > 0f || isAttackQueued)
        {
            return;
        }

        attackDelayTimer = attackDelay;
        isAttackQueued = true;
    }

    protected virtual void TryAttackWhileMoving()
    {
        if (cooldownTimer > 0f)
        {
            return;
        }

        BeginAttack();
    }

    protected virtual void FireLockedProjectile()
    {
        if (projectilePrefab == null)
        {
            PickNewMoveTarget();
            return;
        }

        Transform spawnTransform = projectileSpawnPoint != null ? projectileSpawnPoint : transform;
        lockedAttackDirection = GetAttackDirection(spawnTransform.position);
        Quaternion projectileRotation = Quaternion.FromToRotation(Vector3.right, lockedAttackDirection);
        GameObject projectile = Instantiate(
            projectilePrefab,
            spawnTransform.position,
            projectileRotation
        );

        ConfigureProjectile(projectile, lockedAttackDirection);
        OnProjectileFired(projectile, lockedAttackDirection);
        shouldPickNewTargetAfterCooldown = true;
    }

    protected virtual void ConfigureProjectile(GameObject projectile, Vector2 direction)
    {
        Projectile projectileComponent = projectile.GetComponent<Projectile>();
        if (projectileComponent == null)
        {
            return;
        }

        projectileComponent.SetDirection(direction);
        projectileComponent.SetSpeed(projectileSpeed);
        projectileComponent.SetOwner(gameObject);
    }

    protected virtual void OnProjectileFired(GameObject projectile, Vector2 direction)
    {
    }

    public virtual void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitDirection, GameObject source)
    {
        if (amount <= 0 || currentHealth <= 0)
        {
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - amount);
        LastScoreContext = pendingScoreContext;
        LastHitWasFlank = IsFlankHit(hitPoint, hitDirection, source);
        SpawnHitEffect(hitPoint, hitDirection);
        DamageReceived?.Invoke(this, source);

        if (currentHealth <= 0)
        {
            Die(source);
        }
    }

    public virtual void TakeDamage(int amount)
    {
        TakeDamage(amount, GetDefaultHitEffectPosition(), Vector2.zero, null);
    }

    protected virtual void SpawnHitEffect(Vector2 hitPoint, Vector2 hitDirection)
    {
        if (hitEffectPrefab == null || Time.time < lastHitEffectTime + hitEffectInterval)
        {
            return;
        }

        lastHitEffectTime = Time.time;

        Vector2 randomOffset = Random.insideUnitCircle * hitEffectRandomOffsetRadius;
        Vector3 spawnPosition = (Vector3)(hitPoint + hitEffectPositionOffset + randomOffset);
        if (hitPoint.sqrMagnitude <= 0.0001f)
        {
            spawnPosition =
                GetDefaultHitEffectPosition() + (Vector3)(hitEffectPositionOffset + randomOffset);
        }

        Quaternion spawnRotation = Quaternion.identity;
        if (rotateHitEffectToHitDirection && hitDirection.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(hitDirection.y, hitDirection.x) * Mathf.Rad2Deg;
            spawnRotation = Quaternion.Euler(0f, 0f, angle);
        }

        Transform parent = parentHitEffectToEnemy ? transform : null;
        GameObject hitEffect = Instantiate(hitEffectPrefab, spawnPosition, spawnRotation, parent);
        SpriteAnimationParticle spriteParticle = hitEffect.GetComponent<SpriteAnimationParticle>();
        if (spriteParticle == null)
        {
            spriteParticle = hitEffect.AddComponent<SpriteAnimationParticle>();
        }

        if (spriteParticle != null)
        {
            spriteParticle.Restart();
        }
    }

    protected virtual Vector3 GetDefaultHitEffectPosition()
    {
        return hitEffectSpawnPoint != null ? hitEffectSpawnPoint.position : transform.position;
    }

    protected virtual void Die(GameObject source)
    {
        if (isDead)
        {
            return;
        }

        isDead = true;
        Defeated?.Invoke(this, source);

        if (destroyOnDeath)
        {
            Destroy(gameObject);
        }
    }

    protected virtual Vector2 GetDirectionToPlayer()
    {
        return GetDirectionToPlayer(transform.position);
    }

    protected virtual Vector2 GetDirectionToPlayer(Vector3 origin)
    {
        if (targetPlayer == null)
        {
            return lockedAttackDirection;
        }

        Vector2 direction = (targetPlayer.transform.position - origin);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return lockedAttackDirection;
        }

        return direction.normalized;
    }

    protected virtual Vector2 GetAttackDirection(Vector3 origin)
    {
        if (fireControlSystem != null && targetPlayer != null)
        {
            return fireControlSystem.GetAimDirection(origin, targetPlayer.transform, projectileSpeed);
        }

        return GetDirectionToPlayer(origin);
    }

    protected virtual void PickNewMoveTarget()
    {
        if (targetPlayer == null)
        {
            hasMoveTarget = false;
            return;
        }

        float minRadius = Mathf.Max(0f, orbitMinRadius);
        float maxRadius = Mathf.Max(minRadius, orbitMaxRadius);
        Vector2 randomDirection = Random.insideUnitCircle.normalized;

        if (randomDirection.sqrMagnitude <= 0.0001f)
        {
            randomDirection = Vector2.right;
        }

        float randomRadius = Random.Range(minRadius, maxRadius);
        currentMoveOffset = randomDirection * randomRadius;
        currentMoveTarget = GetCurrentMoveTarget();
        hasMoveTarget = true;
    }

    protected virtual Vector2 GetCurrentMoveTarget()
    {
        if (targetPlayer == null)
        {
            return currentMoveTarget;
        }

        return (Vector2)targetPlayer.transform.position + currentMoveOffset;
    }

    public virtual void SetTarget(Player player)
    {
        targetPlayer = player;
    }

    public EnemyState GetCurrentState()
    {
        return currentState;
    }

    protected virtual void OnDrawGizmos()
    {
        Player gizmoTarget = targetPlayer != null ? targetPlayer : FindFirstObjectByType<Player>();
        if (gizmoTarget != null)
        {
            Vector3 targetPosition = gizmoTarget.transform.position;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(targetPosition, orbitMinRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(targetPosition, orbitMaxRadius);
        }

        if (hasMoveTarget)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(GetCurrentMoveTarget(), 0.15f);
        }
    }
}
