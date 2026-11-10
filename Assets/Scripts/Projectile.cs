using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    protected const string ProjectileLayerName = "Projectile";

    [Header("Movement")]
    [SerializeField, HideInInspector] protected float speed = 8f;
    [SerializeField, HideInInspector] protected Vector2 direction = Vector2.right;

    [Header("Lifetime")]
    [SerializeField, HideInInspector] protected float lifetime = 5f;
    [SerializeField] protected bool destroyOnHit = true;

    [Header("Damage")]
    [SerializeField, HideInInspector] protected int damage = 1;

    [Header("Collision")]
    [SerializeField] protected LayerMask hitLayers = ~0;
    [SerializeField] protected bool ensureRigidbody = true;
    [SerializeField] protected bool useTriggerCollision = true;

    protected GameObject ownerObject;
    protected Collider2D projectileCollider;
    protected Rigidbody2D projectileRigidbody;
    protected float lifeTimer;
    public AttackScoreContext ScoreContext { get; private set; }
    public void SetScoreContext(AttackScoreContext context) => ScoreContext = context;

    protected virtual void Awake()
    {
        ConfigureProjectileLayer();
        CacheCollisionComponents();
        ConfigureCollisionComponents();
        lifeTimer = lifetime;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        IgnoreOwnerCollisions();
    }

    protected virtual void ConfigureProjectileLayer()
    {
        int projectileLayer = LayerMask.NameToLayer(ProjectileLayerName);
        if (projectileLayer < 0)
        {
            return;
        }

        gameObject.layer = projectileLayer;
        Physics2D.IgnoreLayerCollision(projectileLayer, projectileLayer, true);
    }

    protected virtual void Update()
    {
        Move();
        TickLifetime();
    }

    protected virtual void Move()
    {
        transform.position += (Vector3)(direction * speed * Time.deltaTime);
    }

    protected virtual void TickLifetime()
    {
        if (lifetime <= 0f)
        {
            return;
        }

        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            DestroyProjectile();
        }
    }

    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        if (IsOwnerCollider(other))
        {
            return;
        }

        if (!IsInHitLayer(other.gameObject.layer))
        {
            return;
        }

        OnHit(other);
    }

    protected virtual void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsOwnerCollider(collision.collider))
        {
            return;
        }

        if (!IsInHitLayer(collision.gameObject.layer))
        {
            return;
        }

        OnHit(collision.collider);
    }

    protected virtual void OnHit(Component hitTarget)
    {
        ApplyDamage(hitTarget);

        if (destroyOnHit)
        {
            DestroyProjectile();
        }
    }

    protected virtual void ApplyDamage(Component hitTarget)
    {
        if (damage <= 0 || hitTarget == null)
        {
            return;
        }

        IDamageable damageable = hitTarget.GetComponentInParent<IDamageable>();
        if (damageable == null)
        {
            return;
        }

        Vector2 hitPoint = transform.position;
        if (hitTarget is Collider2D hitCollider)
        {
            hitPoint = hitCollider.ClosestPoint(transform.position);
        }

        GameObject source = ownerObject != null ? ownerObject : gameObject;
        CombatDamage.Apply(damageable, damage, hitPoint, direction, source, ScoreContext);
    }

    protected virtual bool IsInHitLayer(int layer)
    {
        return (hitLayers.value & (1 << layer)) != 0;
    }

    protected virtual void CacheCollisionComponents()
    {
        if (projectileCollider == null)
        {
            projectileCollider = GetComponent<Collider2D>();
        }

        if (projectileRigidbody == null)
        {
            projectileRigidbody = GetComponent<Rigidbody2D>();
        }
    }

    protected virtual void ConfigureCollisionComponents()
    {
        if (projectileCollider != null && useTriggerCollision)
        {
            projectileCollider.isTrigger = true;
        }

        if (!ensureRigidbody)
        {
            return;
        }

        if (projectileRigidbody == null)
        {
            projectileRigidbody = gameObject.AddComponent<Rigidbody2D>();
        }

        projectileRigidbody.bodyType = RigidbodyType2D.Kinematic;
        projectileRigidbody.gravityScale = 0f;
        projectileRigidbody.freezeRotation = true;
        projectileRigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    protected virtual void DestroyProjectile()
    {
        Destroy(gameObject);
    }

    public virtual void SetDirection(Vector2 newDirection)
    {
        if (newDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        direction = newDirection.normalized;
    }

    public virtual void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
    }

    public virtual void SetLifetime(float newLifetime)
    {
        lifetime = newLifetime;
        lifeTimer = newLifetime;
    }

    public virtual void SetDamage(int newDamage)
    {
        damage = newDamage;
    }

    public virtual void SetOwner(GameObject newOwner)
    {
        ScoreContext = default;
        ownerObject = newOwner;
        IgnoreOwnerCollisions();
    }

    public virtual void Configure(
        Vector2 newDirection,
        float newSpeed,
        float newLifetime,
        int newDamage,
        GameObject newOwner
    )
    {
        SetDirection(newDirection);
        SetSpeed(newSpeed);
        SetLifetime(newLifetime);
        SetDamage(newDamage);
        SetOwner(newOwner);
    }

    public Vector2 GetDirection()
    {
        return direction;
    }

    protected virtual void IgnoreOwnerCollisions()
    {
        if (ownerObject == null)
        {
            return;
        }

        if (projectileCollider == null)
        {
            CacheCollisionComponents();
        }

        if (projectileCollider == null)
        {
            return;
        }

        Collider2D[] ownerColliders = ownerObject.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < ownerColliders.Length; i++)
        {
            Collider2D ownerCollider = ownerColliders[i];
            if (ownerCollider != null && ownerCollider != projectileCollider)
            {
                Physics2D.IgnoreCollision(projectileCollider, ownerCollider, true);
            }
        }
    }

    protected virtual bool IsOwnerCollider(Collider2D other)
    {
        if (ownerObject == null || other == null)
        {
            return false;
        }

        return other.transform == ownerObject.transform || other.transform.IsChildOf(ownerObject.transform);
    }
}
