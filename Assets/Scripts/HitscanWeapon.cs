using System.Collections.Generic;
using UnityEngine;

public enum HitscanCastType
{
    Ray,
    Circle,
    Box,
    BeamArea
}

public class HitscanWeapon : Weapon
{
    [Header("Hitscan")]
    [SerializeField] protected Transform fireOrigin;
    [SerializeField] protected float range = 12f;
    [SerializeField] protected LayerMask hitLayers = ~0;
    [SerializeField] protected bool includeTriggers = true;
    [SerializeField] protected bool hitMultipleTargets = true;
    [SerializeField] protected int maxHits = 16;

    [Header("Shape")]
    [SerializeField] protected HitscanCastType castType = HitscanCastType.BeamArea;
    [SerializeField] protected float radius = 0.25f;
    [SerializeField] protected Vector2 boxSize = new Vector2(1f, 0.5f);

    [Header("Beam Width")]
    [SerializeField] protected float beamWidth = 0.5f;
    [SerializeField] protected float beamWidthGrowDuration = 0.12f;
    [SerializeField] protected AnimationCurve beamWidthGrowCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Fire")]
    [SerializeField] protected bool continuousFire = true;
    [SerializeField] protected float hitInterval = 0.1f;

    [Header("Visual")]
    [SerializeField] protected bool showBeamVisual = true;
    [SerializeField] protected bool hideBeamVisualOnEnd = true;
    [SerializeField] protected bool trimVisualAtFirstHit;
    [SerializeField] protected HitscanBeamVisual beamVisual;

    [Header("References")]
    [SerializeField] protected PlayerWeaponController aimController;

    [Header("Debug")]
    [SerializeField] protected bool drawDebug;
    [SerializeField] protected Color debugHitColor = Color.red;
    [SerializeField] protected Color debugMissColor = Color.cyan;
    [SerializeField] protected float debugDuration;

    protected RaycastHit2D[] hitResults;
    protected Collider2D[] overlapResults;
    protected float hitTimer;
    protected float attackStartTimer;
    protected float attackElapsedTime;
    protected float currentBeamWidth;
    protected float lastHitDistance;
    protected Vector2 lastFireDirection = Vector2.right;
    protected bool attackStarted;
    protected bool lastAttackHadHit;
    private readonly HashSet<IDamageable> damagedTargets = new();
    private AttackScoreContext scoreContext;

    protected virtual void Awake()
    {
        CacheReferences();
        EnsureHitBuffers();
    }

    protected override void Update()
    {
        base.Update();

        if (isAttacking)
        {
            attackElapsedTime += Time.deltaTime;
            UpdateBeamWidth();
            UpdateBeamVisual();
        }

        TickAttackStartDelay();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (beamVisual != null)
        {
            beamVisual.Hide();
        }
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        range = Mathf.Max(0f, range);
        radius = Mathf.Max(0f, radius);
        boxSize.x = Mathf.Max(0f, boxSize.x);
        boxSize.y = Mathf.Max(0f, boxSize.y);
        beamWidth = Mathf.Max(0f, beamWidth);
        beamWidthGrowDuration = Mathf.Max(0f, beamWidthGrowDuration);
        hitInterval = Mathf.Max(0.01f, hitInterval);
        maxHits = Mathf.Max(1, maxHits);
        debugDuration = Mathf.Max(0f, debugDuration);

        if (beamWidthGrowCurve == null || beamWidthGrowCurve.length == 0)
        {
            beamWidthGrowCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }

    public override void Initialize(Player player)
    {
        base.Initialize(player);
        CacheReferences();
    }

    public override bool CanStartAttack()
    {
        return !isAttacking && cooldownTimer <= 0f;
    }

    public override void OnAttackPressed()
    {
        if (!CanStartAttack())
        {
            return;
        }

        isAttacking = true;
        owner?.UpdateWeaponAim(this);
        attackStarted = false;
        attackStartTimer = Mathf.Max(0f, attackStartDelay);
        attackElapsedTime = 0f;
        currentBeamWidth = 0f;
        lastHitDistance = range;
        lastAttackHadHit = false;
        hitTimer = 0f;

        UpdateBeamWidth();
        UpdateBeamVisual();
        OnAttackStart();

        if (attackStartTimer <= 0f)
        {
            BeginHitscanAttack();
        }
    }

    public override void OnAttackHeld()
    {
        if (!isAttacking || !attackStarted || !continuousFire)
        {
            return;
        }

        hitTimer -= Time.deltaTime;
        if (hitTimer > 0f)
        {
            return;
        }

        OnAttack();
    }

    public override void OnAttackReleased()
    {
        if (!isAttacking)
        {
            return;
        }

        OnAttackEnd();
        isAttacking = false;
        attackStarted = false;
        attackStartTimer = 0f;
        attackElapsedTime = 0f;
        currentBeamWidth = 0f;
        cooldownTimer = attackCooldown;

        if (hideBeamVisualOnEnd && beamVisual != null)
        {
            beamVisual.Hide();
        }
    }

    protected override void OnAttack()
    {
        FireHitscan();
        hitTimer = hitInterval;
    }

    protected virtual void TickAttackStartDelay()
    {
        if (!isAttacking || attackStarted || attackStartTimer <= 0f)
        {
            return;
        }

        attackStartTimer -= Time.deltaTime;
        if (attackStartTimer <= 0f)
        {
            BeginHitscanAttack();
        }
    }

    protected virtual void BeginHitscanAttack()
    {
        attackStarted = true;
        hitTimer = 0f;
        OnAttack();

        if (!continuousFire)
        {
            OnAttackReleased();
        }
    }

    protected virtual void FireHitscan()
    {
        scoreContext = CreateScoreAttack();
        damagedTargets.Clear();
        owner?.UpdateWeaponAim(this);
        Vector3 origin = GetFireOriginPosition();
        Vector2 direction = GetFireDirection(origin);
        lastFireDirection = direction;
        lastAttackHadHit = false;
        lastHitDistance = range;

        if (castType == HitscanCastType.BeamArea)
        {
            ProcessBeamAreaHits(origin, direction);
            DrawDebugBeamArea(origin, direction, lastAttackHadHit);
            return;
        }

        ProcessCastHits(origin, direction);
        DrawDebugHit(origin, direction, lastAttackHadHit, lastHitDistance);
    }

    protected virtual void ProcessCastHits(Vector2 origin, Vector2 direction)
    {
        int hitCount = CastHits(origin, direction);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = hitResults[i];
            if (hit.collider == null || IsOwnerCollider(hit.collider))
            {
                continue;
            }

            if (!lastAttackHadHit)
            {
                lastAttackHadHit = true;
                lastHitDistance = hit.distance;
            }

            OnHit(hit);

            if (!hitMultipleTargets)
            {
                break;
            }
        }
    }

    protected virtual void ProcessBeamAreaHits(Vector2 origin, Vector2 direction)
    {
        float activeWidth = GetCurrentBeamWidth();
        if (activeWidth <= 0f || range <= 0f)
        {
            return;
        }

        int hitCount = OverlapBeamArea(origin, direction, activeWidth);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = overlapResults[i];
            if (hitCollider == null || IsOwnerCollider(hitCollider))
            {
                continue;
            }

            if (!lastAttackHadHit)
            {
                lastAttackHadHit = true;
                lastHitDistance = GetProjectedDistance(origin, direction, hitCollider.bounds.center);
            }

            OnHit(hitCollider);

            if (!hitMultipleTargets)
            {
                break;
            }
        }
    }

    protected virtual int CastHits(Vector2 origin, Vector2 direction)
    {
        EnsureHitBuffers();
        ContactFilter2D contactFilter = CreateContactFilter();

        if (castType == HitscanCastType.Circle)
        {
            return Physics2D.CircleCast(origin, radius, direction, contactFilter, hitResults, range);
        }

        if (castType == HitscanCastType.Box)
        {
            float angle = GetDirectionAngle(direction);
            return Physics2D.BoxCast(origin, boxSize, angle, direction, contactFilter, hitResults, range);
        }

        return Physics2D.Raycast(origin, direction, contactFilter, hitResults, range);
    }

    protected virtual int OverlapBeamArea(Vector2 origin, Vector2 direction, float activeWidth)
    {
        EnsureHitBuffers();

        Vector2 normalizedDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.right;
        Vector2 center = origin + normalizedDirection * (range * 0.5f);
        Vector2 size = new Vector2(range, activeWidth);
        float angle = GetDirectionAngle(normalizedDirection);

        return Physics2D.OverlapBox(
            center,
            size,
            angle,
            CreateContactFilter(),
            overlapResults
        );
    }

    protected virtual void OnHit(RaycastHit2D hit)
    {
        ApplyDamage(hit.collider, hit.point);
    }

    protected virtual void OnHit(Collider2D hitCollider)
    {
        if (hitCollider == null)
        {
            return;
        }

        Vector2 hitPoint = hitCollider.ClosestPoint(GetFireOriginPosition());
        ApplyDamage(hitCollider, hitPoint);
    }

    protected virtual void ApplyDamage(Collider2D hitCollider, Vector2 hitPoint)
    {
        if (damage <= 0 || hitCollider == null)
        {
            return;
        }

        IDamageable damageable = hitCollider.GetComponentInParent<IDamageable>();
        if (damageable == null || !damagedTargets.Add(damageable))
        {
            return;
        }

        CombatDamage.Apply(damageable, damage, hitPoint, lastFireDirection, GetDamageSource(), scoreContext);
    }

    protected virtual GameObject GetDamageSource()
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

    protected virtual Vector3 GetFireOriginPosition()
    {
        return fireOrigin != null ? fireOrigin.position : transform.position;
    }

    protected virtual Vector2 GetFireDirection(Vector3 origin)
    {
        Transform originTransform = fireOrigin != null ? fireOrigin : transform;
        Vector2 direction = originTransform.right;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
    }

    protected override Transform GetAimMuzzle()
    {
        return fireOrigin != null ? fireOrigin : transform;
    }

    protected virtual float GetCurrentBeamWidth()
    {
        return currentBeamWidth;
    }

    protected virtual void UpdateBeamWidth()
    {
        if (beamWidthGrowDuration <= 0f)
        {
            currentBeamWidth = beamWidth;
            return;
        }

        float normalizedTime = Mathf.Clamp01(attackElapsedTime / beamWidthGrowDuration);
        float widthFactor = beamWidthGrowCurve != null
            ? Mathf.Clamp01(beamWidthGrowCurve.Evaluate(normalizedTime))
            : normalizedTime;
        currentBeamWidth = beamWidth * widthFactor;
    }

    protected virtual void UpdateBeamVisual()
    {
        if (!showBeamVisual || beamVisual == null)
        {
            return;
        }

        Vector3 origin = GetFireOriginPosition();
        Vector2 direction = GetFireDirection(origin);
        float visualLength = trimVisualAtFirstHit && lastAttackHadHit ? lastHitDistance : range;
        beamVisual.Show(origin, direction, visualLength, GetCurrentBeamWidth());
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

        if (beamVisual == null)
        {
            beamVisual = GetComponentInChildren<HitscanBeamVisual>(true);
        }
    }

    protected virtual ContactFilter2D CreateContactFilter()
    {
        return new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = hitLayers,
            useTriggers = includeTriggers
        };
    }

    protected virtual void EnsureHitBuffers()
    {
        int safeMaxHits = Mathf.Max(1, maxHits);
        if (hitResults == null || hitResults.Length != safeMaxHits)
        {
            hitResults = new RaycastHit2D[safeMaxHits];
        }

        if (overlapResults == null || overlapResults.Length != safeMaxHits)
        {
            overlapResults = new Collider2D[safeMaxHits];
        }
    }

    protected virtual bool IsOwnerCollider(Collider2D other)
    {
        if (owner == null || other == null)
        {
            return false;
        }

        return other.transform == owner.transform || other.transform.IsChildOf(owner.transform);
    }

    protected virtual float GetDirectionAngle(Vector2 direction)
    {
        Vector2 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        return Mathf.Atan2(safeDirection.y, safeDirection.x) * Mathf.Rad2Deg;
    }

    protected virtual float GetProjectedDistance(Vector2 origin, Vector2 direction, Vector2 target)
    {
        Vector2 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        return Mathf.Clamp(Vector2.Dot(target - origin, safeDirection), 0f, range);
    }

    protected virtual void DrawDebugHit(
        Vector3 origin,
        Vector2 direction,
        bool hasValidHit,
        float hitDistance
    )
    {
        if (!drawDebug)
        {
            return;
        }

        float lineDistance = hasValidHit ? hitDistance : range;
        Color lineColor = hasValidHit ? debugHitColor : debugMissColor;
        Debug.DrawRay(origin, direction * lineDistance, lineColor, debugDuration);
    }

    protected virtual void DrawDebugBeamArea(Vector3 origin, Vector2 direction, bool hasValidHit)
    {
        if (!drawDebug)
        {
            return;
        }

        Vector2 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        Vector2 side = new Vector2(-safeDirection.y, safeDirection.x) * (GetCurrentBeamWidth() * 0.5f);
        Vector2 start = origin;
        Vector2 end = start + safeDirection * range;
        Color lineColor = hasValidHit ? debugHitColor : debugMissColor;

        Debug.DrawLine(start + side, end + side, lineColor, debugDuration);
        Debug.DrawLine(start - side, end - side, lineColor, debugDuration);
        Debug.DrawLine(start + side, start - side, lineColor, debugDuration);
        Debug.DrawLine(end + side, end - side, lineColor, debugDuration);
    }
}
