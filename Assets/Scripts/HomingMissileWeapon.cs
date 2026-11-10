using System.Collections.Generic;
using UnityEngine;

public class HomingMissileWeapon : Weapon
{
    [Header("Projectile")]
    [SerializeField] protected GameObject missilePrefab;
    [SerializeField] protected float missileSpeed = 12f;
    [SerializeField] protected float missileLifetime = 5f;
    [SerializeField] protected float missileTurnSpeed = 360f;
    [SerializeField] protected float missileHomingDelay = 0.05f;

    [Header("Fire")]
    [SerializeField] protected Transform[] muzzlePoints;
    [SerializeField] protected float missileFireInterval = 0.12f;

    [Header("Charge")]
    [SerializeField] protected float maxChargeTime = 2f;
    [SerializeField] protected float chargeSlowMultiplier = 0.45f;
    [SerializeField, HideInInspector] protected float baseLockOnRange = 4f;
    [SerializeField, HideInInspector] protected float maxLockOnRange = 10f;
    [SerializeField, HideInInspector] protected AnimationCurve lockOnRangeGrowCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Lock On")]
    [SerializeField] protected LayerMask enemyLayers = ~0;
    [SerializeField] protected bool includeTriggers = true;
    [SerializeField] protected float lockOnAngle = 70f;
    [SerializeField] protected float lockOnDelay = 0.25f;
    [SerializeField] protected int maxLockOnCount = 6;
    [SerializeField] protected int searchBufferSize = 32;

    [Header("Lock On Screen")]
    [SerializeField] protected Camera lockOnCamera;
    [SerializeField] protected Vector2 lockViewportPadding;
    [SerializeField] protected float lockScreenWorldPadding;

    [Header("Lock On Marker")]
    [SerializeField] protected LockOnMarker lockOnMarkerPrefab;
    [SerializeField] protected Transform lockOnMarkerRoot;
    [SerializeField] protected LockOnMarkerVisualSettings lockOnMarkerVisual =
        new LockOnMarkerVisualSettings();

    [Header("Charge Laser Pointer")]
    [SerializeField] protected bool showChargeLaserPointer = true;
    [SerializeField] protected HitscanBeamVisual chargeLaserPointerVisual;
    [SerializeField] protected Transform chargeLaserPointerOrigin;
    [SerializeField] protected float chargeLaserPointerWidth = 0.08f;
    [SerializeField] protected float chargeLaserPointerLengthPadding;
    [SerializeField] protected float minChargeLaserPointerLength = 0.1f;

    [Header("Vision")]
    [SerializeField] protected Camera chargeCamera;
    [SerializeField] protected float chargeViewSizeMultiplier = 1.25f;
    [SerializeField] protected AnimationCurve chargeViewGrowCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] protected float postFireViewHoldDuration = 0.5f;
    [SerializeField] protected float viewLerpSpeed = 8f;

    [Header("References")]
    [SerializeField] protected PlayerWeaponController aimController;

    [Header("Debug")]
    [SerializeField] protected bool drawDebug;
    [SerializeField] protected Color lockRangeColor = Color.yellow;
    [SerializeField] protected Color aimConeColor = Color.cyan;

    protected readonly List<LockOnEntry> lockedTargets = new List<LockOnEntry>();
    protected readonly List<Transform> missileQueue = new List<Transform>();
    protected Collider2D[] searchResults;
    protected Transform pendingLockTarget;
    protected float chargeTimer;
    protected float lockOnTimer;
    protected float originalOrthographicSize;
    protected float originalFieldOfView;
    protected float viewRestoreDelayTimer;
    protected float fireTimer;
    protected bool hasOriginalCameraValues;
    protected bool isCharging;
    protected bool isFiringQueue;
    protected bool isWaitingForReadyAnimation;
    private BattleCameraRig battleCamera;
    protected int readyAnimationStartFrame;
    protected int nextMuzzleIndex;

    public int TotalLockOnCount => GetTotalLockOnCount();
    public override bool IsCameraAiming => base.IsCameraAiming || isCharging || isFiringQueue;
    public Bounds CurrentLockScreenBounds => GetLockScreenWorldBounds(GetLockOnCamera());

    public void SetBattleCamera(BattleCameraRig rig)
    {
        if (battleCamera != null) battleCamera.RemoveViewMultiplier(this, viewLerpSpeed, true);
        battleCamera = rig;
        if (rig == null) return;
        chargeCamera = lockOnCamera = rig.OutputCamera;
        // Capture the authored normal view, not an in-progress entry close-up or charge zoom.
        originalOrthographicSize = rig.NormalSize;
        originalFieldOfView = chargeCamera.fieldOfView;
        hasOriginalCameraValues = true;
    }

    protected virtual void Awake()
    {
        CacheReferences();
        EnsureSearchBuffer();
    }

    protected override void Update()
    {
        base.Update();

        if (isCharging)
        {
            TickCharge();
        }
        else if (isFiringQueue)
        {
            TickMissileQueue();
        }
        else if (viewRestoreDelayTimer > 0f)
        {
            viewRestoreDelayTimer = Mathf.Max(0f, viewRestoreDelayTimer - Time.deltaTime);
        }
        else
        {
            RestoreChargeView();
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ClearChargeEffects();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        missileSpeed = Mathf.Max(0f, missileSpeed);
        missileLifetime = Mathf.Max(0f, missileLifetime);
        missileTurnSpeed = Mathf.Max(0f, missileTurnSpeed);
        missileHomingDelay = Mathf.Max(0f, missileHomingDelay);
        missileFireInterval = Mathf.Max(0f, missileFireInterval);
        maxChargeTime = Mathf.Max(0.01f, maxChargeTime);
        chargeSlowMultiplier = Mathf.Clamp01(chargeSlowMultiplier);
        baseLockOnRange = Mathf.Max(0f, baseLockOnRange);
        maxLockOnRange = Mathf.Max(baseLockOnRange, maxLockOnRange);
        lockOnAngle = Mathf.Clamp(lockOnAngle, 0f, 360f);
        lockOnDelay = Mathf.Max(0.01f, lockOnDelay);
        maxLockOnCount = Mathf.Max(0, maxLockOnCount);
        searchBufferSize = Mathf.Max(1, searchBufferSize);
        lockViewportPadding.x = Mathf.Max(0f, lockViewportPadding.x);
        lockViewportPadding.y = Mathf.Max(0f, lockViewportPadding.y);
        lockScreenWorldPadding = Mathf.Max(0f, lockScreenWorldPadding);
        chargeLaserPointerWidth = Mathf.Max(0f, chargeLaserPointerWidth);
        chargeLaserPointerLengthPadding = Mathf.Max(0f, chargeLaserPointerLengthPadding);
        minChargeLaserPointerLength = Mathf.Max(0f, minChargeLaserPointerLength);
        chargeViewSizeMultiplier = Mathf.Max(1f, chargeViewSizeMultiplier);
        postFireViewHoldDuration = Mathf.Max(0f, postFireViewHoldDuration);
        viewLerpSpeed = Mathf.Max(0f, viewLerpSpeed);
        ValidateLockOnMarkerSettings();

        if (lockOnRangeGrowCurve == null || lockOnRangeGrowCurve.length == 0)
        {
            lockOnRangeGrowCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        if (chargeViewGrowCurve == null || chargeViewGrowCurve.length == 0)
        {
            chargeViewGrowCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
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
        EnterWeaponBusyState();
        isCharging = true;
        isFiringQueue = false;
        fireTimer = 0f;
        chargeTimer = 0f;
        viewRestoreDelayTimer = 0f;
        lockOnTimer = 0f;
        pendingLockTarget = null;
        DestroyLockMarkers();
        lockedTargets.Clear();
        missileQueue.Clear();
        CacheReferences();
        CaptureCameraValues();
        ApplyChargeSlow();
        isWaitingForReadyAnimation = true;
        readyAnimationStartFrame = Time.frameCount;
        EnterReadyAnimation();
        OnAttackStart();
    }

    public override void OnAttackHeld()
    {
    }

    public override void OnAttackReleased()
    {
        if (!isAttacking)
        {
            return;
        }

        if (isCharging)
        {
            BeginMissileQueue();
            return;
        }

        if (!isFiringQueue)
        {
            CompleteAttack();
        }
    }

    protected override void OnAttack()
    {
    }

    protected virtual void TickCharge()
    {
        chargeTimer = Mathf.Min(maxChargeTime, chargeTimer + Time.deltaTime);
        if (isWaitingForReadyAnimation
            && Time.frameCount > readyAnimationStartFrame
            && (owner == null
                || owner.IsWeaponAnimationFinished(this, WeaponAnimationPhase.Ready)))
        {
            isWaitingForReadyAnimation = false;
            EnterHoldAnimation();
        }
        ApplyChargeView();
        UpdateChargeLaserPointer();

        if (lockedTargets.Count >= maxLockOnCount)
        {
            ClearPendingLockTarget();
            return;
        }

        if (TryLockNextTarget(out Transform lockedTarget))
        {
            OnTargetLocked(lockedTarget);
        }
    }

    protected virtual void BeginMissileQueue()
    {
        isCharging = false;
        HideChargeLaserPointer();
        RemoveChargeSlow();
        BuildMissileQueue();

        if (missileQueue.Count == 0)
        {
            EnterReleaseAnimation();
            CompleteAttack();
            return;
        }

        isFiringQueue = true;
        fireTimer = 0f;
    }

    protected virtual void TickMissileQueue()
    {
        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f)
        {
            return;
        }

        Transform target = null;
        while (missileQueue.Count > 0 && target == null)
        {
            target = missileQueue[0];
            missileQueue.RemoveAt(0);
        }

        if (target != null)
        {
            ReplayReleaseAnimation();
            FireMissile(target);
            fireTimer = missileFireInterval;
        }

        if (missileQueue.Count == 0)
        {
            FinishMissileQueue();
        }
    }

    protected virtual void FinishMissileQueue()
    {
        isFiringQueue = false;
        fireTimer = 0f;
        viewRestoreDelayTimer = postFireViewHoldDuration;
        CompleteAttack();
    }

    protected virtual bool TryLockNextTarget(out Transform lockedTarget)
    {
        lockedTarget = null;
        Vector3 origin = GetLockOrigin();
        Vector2 aimDirection = GetAimDirection(origin);
        int hitCount = FindLockCandidates();

        if (hitCount <= 0)
        {
            ClearPendingLockTarget();
            return false;
        }

        Transform candidate = FindBestLockTarget(origin, aimDirection, hitCount);

        if (candidate == null)
        {
            ClearPendingLockTarget();
            return false;
        }

        if (pendingLockTarget != candidate)
        {
            pendingLockTarget = candidate;
            lockOnTimer = 0f;
            return false;
        }

        lockOnTimer += Time.deltaTime;
        if (lockOnTimer < lockOnDelay)
        {
            return false;
        }

        lockedTarget = candidate;
        AddLock(lockedTarget);
        ClearPendingLockTarget();
        return true;
    }

    protected virtual void ClearPendingLockTarget()
    {
        pendingLockTarget = null;
        lockOnTimer = 0f;
    }

    protected virtual int FindLockCandidates()
    {
        EnsureSearchBuffer();
        Camera cameraToUse = GetLockOnCamera();
        if (cameraToUse == null)
        {
            return 0;
        }

        ContactFilter2D contactFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = enemyLayers,
            useTriggers = includeTriggers
        };

        Bounds screenBounds = GetLockScreenWorldBounds(cameraToUse);
        return Physics2D.OverlapBox(
            screenBounds.center,
            screenBounds.size,
            0f,
            contactFilter,
            searchResults
        );
    }

    protected virtual Transform FindBestLockTarget(
        Vector2 origin,
        Vector2 aimDirection,
        int hitCount
    )
    {
        Transform bestTarget = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D candidateCollider = searchResults[i];
            Transform candidate = GetDamageableTransform(candidateCollider);
            if (candidate == null || IsOwnerTransform(candidate))
            {
                continue;
            }

            if (!IsColliderInsideLockScreen(candidateCollider))
            {
                continue;
            }

            if (IsTargetLocked(candidate))
            {
                continue;
            }

            Vector2 toCandidate = (Vector2)candidate.position - origin;
            if (!IsInsideLockCone(toCandidate, aimDirection))
            {
                continue;
            }

            float score = GetTargetScore(toCandidate, aimDirection);
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }

    protected virtual void AddLock(Transform target)
    {
        for (int i = 0; i < lockedTargets.Count; i++)
        {
            if (lockedTargets[i].Target == target)
            {
                if (lockedTargets[i].Marker != null)
                {
                    lockedTargets[i].Marker.PlayLockPulse();
                }

                return;
            }
        }

        LockOnEntry newEntry = new LockOnEntry(target);
        lockedTargets.Add(newEntry);
        RefreshLockMarker(newEntry);
    }

    protected virtual void BuildMissileQueue()
    {
        missileQueue.Clear();
        if (maxLockOnCount <= 0)
        {
            return;
        }

        List<Transform> validTargets = new List<Transform>();

        for (int i = 0; i < lockedTargets.Count; i++)
        {
            LockOnEntry entry = lockedTargets[i];
            if (entry.Target != null)
            {
                validTargets.Add(entry.Target);
            }
        }

        if (validTargets.Count == 0)
        {
            return;
        }

        for (int i = 0; i < maxLockOnCount; i++)
        {
            missileQueue.Add(validTargets[i % validTargets.Count]);
        }
    }

    protected virtual void FireMissile(Transform target)
    {
        Transform muzzle = GetNextMuzzlePoint();
        Vector3 spawnPosition = muzzle != null ? muzzle.position : transform.position;
        Vector2 launchDirection = GetInitialMissileDirection(spawnPosition, target);
        FireMissile(target, launchDirection, muzzle);
    }

    protected virtual void FireMissile(
        Transform target,
        Vector2 launchDirection,
        Transform muzzle
    )
    {
        if (missilePrefab == null || target == null)
        {
            return;
        }

        Vector3 spawnPosition = muzzle != null ? muzzle.position : transform.position;
        Vector2 direction = launchDirection.sqrMagnitude > 0.0001f
            ? launchDirection.normalized
            : GetInitialMissileDirection(spawnPosition, target);
        GameObject missile = Instantiate(
            missilePrefab,
            spawnPosition,
            Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg)
        );

        ConfigureMissile(missile, direction, target);
        Projectile scoreProjectile = missile.GetComponent<Projectile>();
        if (scoreProjectile != null)
            scoreProjectile.SetScoreContext(CreateScoreAttack());
    }

    protected virtual Vector2 GetInitialMissileDirection(Vector3 spawnPosition, Transform target)
    {
        Vector2 direction = GetAimDirection(spawnPosition);
        if (direction.sqrMagnitude > 0.0001f)
        {
            return direction.normalized;
        }

        if (target != null)
        {
            direction = target.position - spawnPosition;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }
        }

        Vector2 fallbackDirection = transform.right;
        return fallbackDirection.sqrMagnitude > 0.0001f ? fallbackDirection.normalized : Vector2.right;
    }

    protected virtual void ConfigureMissile(GameObject missile, Vector2 direction, Transform target)
    {
        HomingMissileProjectile homingProjectile = missile.GetComponent<HomingMissileProjectile>();
        if (homingProjectile != null)
        {
            homingProjectile.ConfigureHomingMissile(
                direction,
                missileSpeed,
                missileLifetime,
                damage,
                GetProjectileOwner(),
                target,
                missileTurnSpeed,
                missileHomingDelay
            );
            return;
        }

        Projectile projectile = missile.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.Configure(
                direction,
                missileSpeed,
                missileLifetime,
                damage,
                GetProjectileOwner()
            );
        }
    }

    protected virtual void CompleteAttack()
    {
        OnAttackEnd();
        isCharging = false;
        isFiringQueue = false;
        fireTimer = 0f;
        isWaitingForReadyAnimation = false;
        isAttacking = false;
        HideChargeLaserPointer();
        ClearPendingLockTarget();
        DestroyLockMarkers();
        lockedTargets.Clear();
        missileQueue.Clear();
        RemoveChargeSlow();
        ExitWeaponBusyStateAfterAnimation();
        cooldownTimer = attackCooldown;
    }

    protected virtual void ClearChargeEffects()
    {
        isCharging = false;
        isFiringQueue = false;
        fireTimer = 0f;
        isWaitingForReadyAnimation = false;
        viewRestoreDelayTimer = 0f;
        HideChargeLaserPointer();
        ClearPendingLockTarget();
        DestroyLockMarkers();
        lockedTargets.Clear();
        missileQueue.Clear();
        RemoveChargeSlow();
        ClearWeaponAnimation();
        RestoreCameraImmediately();
    }

    protected virtual void ApplyChargeSlow()
    {
        if (owner != null)
        {
            owner.SetMovementSpeedModifier(this, chargeSlowMultiplier);
        }
    }

    protected virtual void RemoveChargeSlow()
    {
        if (owner != null)
        {
            owner.RemoveMovementSpeedModifier(this);
        }
    }

    protected virtual void CaptureCameraValues()
    {
        if (chargeCamera == null)
        {
            chargeCamera = Camera.main;
        }

        if (chargeCamera == null || hasOriginalCameraValues)
        {
            return;
        }

        originalOrthographicSize = battleCamera != null ? battleCamera.NormalSize : chargeCamera.orthographicSize;
        originalFieldOfView = chargeCamera.fieldOfView;
        hasOriginalCameraValues = true;
    }

    protected virtual void ApplyChargeView()
    {
        if (chargeCamera == null)
        {
            return;
        }

        CaptureCameraValues();
        if (!hasOriginalCameraValues)
        {
            return;
        }

        float chargeRatio = GetChargeRatio();
        float viewGrowRatio = chargeViewGrowCurve != null
            ? Mathf.Clamp01(chargeViewGrowCurve.Evaluate(chargeRatio))
            : chargeRatio;
        float multiplier = Mathf.Lerp(1f, chargeViewSizeMultiplier, viewGrowRatio);

        if (battleCamera != null)
        {
            battleCamera.RequestViewMultiplier(this, multiplier, viewLerpSpeed);
            return;
        }

        if (chargeCamera.orthographic)
        {
            float targetSize = originalOrthographicSize * multiplier;
            chargeCamera.orthographicSize = Mathf.Lerp(
                chargeCamera.orthographicSize,
                targetSize,
                Mathf.Clamp01(viewLerpSpeed * Time.deltaTime)
            );
            return;
        }

        float targetFieldOfView = originalFieldOfView * multiplier;
        chargeCamera.fieldOfView = Mathf.Lerp(
            chargeCamera.fieldOfView,
            targetFieldOfView,
            Mathf.Clamp01(viewLerpSpeed * Time.deltaTime)
        );
    }

    protected virtual void RestoreChargeView()
    {
        if (battleCamera != null)
        {
            battleCamera.RemoveViewMultiplier(this, viewLerpSpeed);
            return;
        }
        if (chargeCamera == null || !hasOriginalCameraValues)
        {
            return;
        }

        float lerp = Mathf.Clamp01(viewLerpSpeed * Time.deltaTime);
        if (chargeCamera.orthographic)
        {
            chargeCamera.orthographicSize = Mathf.Lerp(
                chargeCamera.orthographicSize,
                originalOrthographicSize,
                lerp
            );
            return;
        }

        chargeCamera.fieldOfView = Mathf.Lerp(chargeCamera.fieldOfView, originalFieldOfView, lerp);
    }

    protected virtual void RestoreCameraImmediately()
    {
        if (battleCamera != null)
        {
            battleCamera.RemoveViewMultiplier(this, viewLerpSpeed, true);
            return;
        }
        if (chargeCamera == null || !hasOriginalCameraValues)
        {
            return;
        }

        if (chargeCamera.orthographic)
        {
            chargeCamera.orthographicSize = originalOrthographicSize;
        }
        else
        {
            chargeCamera.fieldOfView = originalFieldOfView;
        }
    }

    protected virtual void UpdateChargeLaserPointer()
    {
        if (!showChargeLaserPointer || chargeLaserPointerVisual == null)
        {
            return;
        }

        Vector3 origin = GetChargeLaserPointerOriginPosition();
        Vector2 direction = GetAimDirection(origin);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            HideChargeLaserPointer();
            return;
        }

        float length = GetChargeLaserPointerLength(origin, direction);
        chargeLaserPointerVisual.Show(origin, direction, length, chargeLaserPointerWidth);
    }

    protected virtual void HideChargeLaserPointer()
    {
        if (chargeLaserPointerVisual != null)
        {
            chargeLaserPointerVisual.Hide();
        }
    }

    protected virtual Vector3 GetChargeLaserPointerOriginPosition()
    {
        if (chargeLaserPointerOrigin != null)
        {
            return chargeLaserPointerOrigin.position;
        }

        Transform muzzle = GetFirstMuzzlePoint();
        return muzzle != null ? muzzle.position : transform.position;
    }

    protected virtual float GetChargeLaserPointerLength(Vector3 origin, Vector2 direction)
    {
        Bounds bounds = GetLockScreenWorldBounds(GetLockOnCamera());
        float length = GetDistanceToBoundsEdge(origin, direction, bounds);
        if (length <= 0f)
        {
            length = Mathf.Max(bounds.extents.x, bounds.extents.y);
        }

        return Mathf.Max(minChargeLaserPointerLength, length + chargeLaserPointerLengthPadding);
    }

    protected virtual float GetDistanceToBoundsEdge(Vector2 origin, Vector2 direction, Bounds bounds)
    {
        Vector2 safeDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.right;
        float bestDistance = float.MaxValue;

        TryUseVerticalBoundsEdge(origin, safeDirection, bounds.min.x, bounds, ref bestDistance);
        TryUseVerticalBoundsEdge(origin, safeDirection, bounds.max.x, bounds, ref bestDistance);
        TryUseHorizontalBoundsEdge(origin, safeDirection, bounds.min.y, bounds, ref bestDistance);
        TryUseHorizontalBoundsEdge(origin, safeDirection, bounds.max.y, bounds, ref bestDistance);

        return bestDistance < float.MaxValue ? bestDistance : 0f;
    }

    protected virtual void TryUseVerticalBoundsEdge(
        Vector2 origin,
        Vector2 direction,
        float edgeX,
        Bounds bounds,
        ref float bestDistance
    )
    {
        if (Mathf.Abs(direction.x) <= 0.0001f)
        {
            return;
        }

        float distance = (edgeX - origin.x) / direction.x;
        if (distance <= 0f || distance >= bestDistance)
        {
            return;
        }

        float y = origin.y + direction.y * distance;
        if (y >= bounds.min.y && y <= bounds.max.y)
        {
            bestDistance = distance;
        }
    }

    protected virtual void TryUseHorizontalBoundsEdge(
        Vector2 origin,
        Vector2 direction,
        float edgeY,
        Bounds bounds,
        ref float bestDistance
    )
    {
        if (Mathf.Abs(direction.y) <= 0.0001f)
        {
            return;
        }

        float distance = (edgeY - origin.y) / direction.y;
        if (distance <= 0f || distance >= bestDistance)
        {
            return;
        }

        float x = origin.x + direction.x * distance;
        if (x >= bounds.min.x && x <= bounds.max.x)
        {
            bestDistance = distance;
        }
    }

    protected virtual void RefreshLockMarker(LockOnEntry entry)
    {
        if (entry == null || entry.Target == null)
        {
            return;
        }

        if (entry.Marker == null)
        {
            entry.Marker = CreateLockMarker(entry.Target);
            return;
        }

        entry.Marker.PlayLockPulse();
    }

    protected virtual LockOnMarker CreateLockMarker(Transform target)
    {
        LockOnMarker marker = null;
        Transform parent = lockOnMarkerRoot != null ? lockOnMarkerRoot : null;

        if (lockOnMarkerPrefab != null)
        {
            marker = Instantiate(lockOnMarkerPrefab, parent);
        }
        else
        {
            GameObject markerObject = new GameObject("Lock On Marker");
            if (parent != null)
            {
                markerObject.transform.SetParent(parent, false);
            }

            marker = markerObject.AddComponent<LockOnMarker>();
        }

        marker.Configure(target, lockOnMarkerVisual);
        return marker;
    }

    protected virtual void DestroyLockMarkers()
    {
        for (int i = 0; i < lockedTargets.Count; i++)
        {
            LockOnMarker marker = lockedTargets[i].Marker;
            if (marker != null)
            {
                Destroy(marker.gameObject);
            }

            lockedTargets[i].Marker = null;
        }
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

    protected virtual Transform GetFirstMuzzlePoint()
    {
        if (muzzlePoints == null || muzzlePoints.Length == 0)
        {
            return transform;
        }

        for (int i = 0; i < muzzlePoints.Length; i++)
        {
            if (muzzlePoints[i] != null)
            {
                return muzzlePoints[i];
            }
        }

        return transform;
    }

    protected virtual Vector3 GetLockOrigin()
    {
        if (aimController != null)
        {
            return aimController.AimOrigin.position;
        }

        return owner != null ? owner.transform.position : transform.position;
    }

    protected virtual Camera GetLockOnCamera()
    {
        if (lockOnCamera != null)
        {
            return lockOnCamera;
        }

        if (chargeCamera != null)
        {
            return chargeCamera;
        }

        return Camera.main;
    }

    protected virtual Bounds GetLockScreenWorldBounds(Camera cameraToUse)
    {
        if (cameraToUse == null)
        {
            return new Bounds(transform.position, Vector3.zero);
        }

        if (cameraToUse.orthographic)
        {
            float height = cameraToUse.orthographicSize * 2f;
            float width = height * cameraToUse.aspect;
            Vector3 size = new Vector3(
                width + lockScreenWorldPadding * 2f,
                height + lockScreenWorldPadding * 2f,
                0f
            );
            return new Bounds(cameraToUse.transform.position, size);
        }

        float zDistance = Mathf.Abs(cameraToUse.transform.position.z - transform.position.z);
        Vector3 bottomLeft = cameraToUse.ViewportToWorldPoint(new Vector3(0f, 0f, zDistance));
        Vector3 topRight = cameraToUse.ViewportToWorldPoint(new Vector3(1f, 1f, zDistance));
        Bounds bounds = new Bounds();
        bounds.SetMinMax(
            Vector3.Min(bottomLeft, topRight),
            Vector3.Max(bottomLeft, topRight)
        );
        bounds.Expand(lockScreenWorldPadding * 2f);
        return bounds;
    }

    protected virtual bool IsColliderInsideLockScreen(Collider2D candidateCollider)
    {
        if (candidateCollider == null)
        {
            return false;
        }

        Camera cameraToUse = GetLockOnCamera();
        if (cameraToUse == null)
        {
            return false;
        }

        Vector3 viewportPoint = cameraToUse.WorldToViewportPoint(candidateCollider.bounds.center);
        if (viewportPoint.z < 0f)
        {
            return false;
        }

        return viewportPoint.x >= -lockViewportPadding.x
            && viewportPoint.x <= 1f + lockViewportPadding.x
            && viewportPoint.y >= -lockViewportPadding.y
            && viewportPoint.y <= 1f + lockViewportPadding.y;
    }

    protected virtual Vector2 GetAimDirection(Vector3 origin)
    {
        return GetSafeAimDirection(origin, aimController);
    }

    protected virtual float GetChargeRatio()
    {
        return Mathf.Clamp01(chargeTimer / maxChargeTime);
    }

    protected virtual bool IsInsideLockCone(Vector2 toCandidate, Vector2 aimDirection)
    {
        if (toCandidate.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        if (lockOnAngle >= 359.9f)
        {
            return true;
        }

        float angle = Vector2.Angle(aimDirection, toCandidate);
        return angle <= lockOnAngle * 0.5f;
    }

    protected virtual float GetTargetScore(Vector2 toCandidate, Vector2 aimDirection)
    {
        Vector2 normalizedAim = aimDirection.sqrMagnitude > 0.0001f
            ? aimDirection.normalized
            : Vector2.right;
        float forwardDistance = Mathf.Max(0f, Vector2.Dot(toCandidate, normalizedAim));
        Vector2 closestPointOnAimLine = normalizedAim * forwardDistance;
        float perpendicularDistance = (toCandidate - closestPointOnAimLine).magnitude;
        return perpendicularDistance + toCandidate.magnitude * 0.05f;
    }

    protected virtual Transform GetDamageableTransform(Collider2D candidateCollider)
    {
        if (candidateCollider == null)
        {
            return null;
        }

        IDamageable damageable = candidateCollider.GetComponentInParent<IDamageable>();
        if (damageable == null)
        {
            return null;
        }

        Component damageableComponent = damageable as Component;
        return damageableComponent != null ? damageableComponent.transform : candidateCollider.transform;
    }

    protected virtual bool IsOwnerTransform(Transform candidate)
    {
        if (owner == null || candidate == null)
        {
            return false;
        }

        return candidate == owner.transform || candidate.IsChildOf(owner.transform);
    }

    protected virtual bool IsTargetLocked(Transform target)
    {
        for (int i = 0; i < lockedTargets.Count; i++)
        {
            if (lockedTargets[i].Target == target)
            {
                return true;
            }
        }

        return false;
    }

    protected virtual int GetTotalLockOnCount()
    {
        return lockedTargets.Count;
    }

    protected virtual GameObject GetProjectileOwner()
    {
        return owner != null ? owner.gameObject : gameObject;
    }

    protected virtual void EnsureSearchBuffer()
    {
        int safeSize = Mathf.Max(1, searchBufferSize);
        if (searchResults == null || searchResults.Length != safeSize)
        {
            searchResults = new Collider2D[safeSize];
        }
    }

    protected virtual void ValidateLockOnMarkerSettings()
    {
        if (lockOnMarkerVisual == null)
        {
            lockOnMarkerVisual = new LockOnMarkerVisualSettings();
        }

        lockOnMarkerVisual.initialScale = Mathf.Max(0f, lockOnMarkerVisual.initialScale);
        lockOnMarkerVisual.pulseScale = Mathf.Max(0f, lockOnMarkerVisual.pulseScale);
        lockOnMarkerVisual.restingScale = Mathf.Max(0f, lockOnMarkerVisual.restingScale);
        lockOnMarkerVisual.pulseDuration = Mathf.Max(0.001f, lockOnMarkerVisual.pulseDuration);
        lockOnMarkerVisual.sortingOrder = Mathf.Clamp(
            lockOnMarkerVisual.sortingOrder,
            short.MinValue,
            short.MaxValue
        );
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

        if (chargeCamera == null)
        {
            chargeCamera = Camera.main;
        }

        if (lockOnCamera == null)
        {
            lockOnCamera = chargeCamera != null ? chargeCamera : Camera.main;
        }

        if (chargeLaserPointerVisual == null)
        {
            chargeLaserPointerVisual = GetComponentInChildren<HitscanBeamVisual>(true);
        }
    }

    protected virtual void OnTargetLocked(Transform lockedTarget)
    {
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (!drawDebug)
        {
            return;
        }

        Vector3 origin = Application.isPlaying ? GetLockOrigin() : transform.position;
        Vector2 aimDirection = Application.isPlaying ? GetAimDirection(origin) : (Vector2)transform.right;
        Camera cameraToUse = Application.isPlaying ? GetLockOnCamera() : lockOnCamera;
        Bounds lockBounds = GetLockScreenWorldBounds(cameraToUse);
        float range = Mathf.Max(lockBounds.extents.x, lockBounds.extents.y);

        Gizmos.color = lockRangeColor;
        Gizmos.DrawWireCube(lockBounds.center, lockBounds.size);

        if (lockOnAngle < 359.9f)
        {
            Quaternion leftRotation = Quaternion.Euler(0f, 0f, lockOnAngle * 0.5f);
            Quaternion rightRotation = Quaternion.Euler(0f, 0f, -lockOnAngle * 0.5f);
            Gizmos.color = aimConeColor;
            Gizmos.DrawLine(origin, origin + (Vector3)(leftRotation * aimDirection * range));
            Gizmos.DrawLine(origin, origin + (Vector3)(rightRotation * aimDirection * range));
        }
    }

    protected class LockOnEntry
    {
        public LockOnEntry(Transform target)
        {
            Target = target;
        }

        public Transform Target;
        public LockOnMarker Marker;
    }
}
