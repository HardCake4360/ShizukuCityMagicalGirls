using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UnityEngine.Animations;
using UnityEngine.Playables;

public enum PlayerAnimationState
{
    NeutralIdle,
    NeutralIdleBlink,
    FlyIdle,
    DirectionChange,
    Ready,
    Charge,
    Hold,
    Release,
    Attack
}

public enum PlayerActivityState
{
    Idle,
    Ready,
    Busy
}

public class Player : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 10;
    private int currentHealth;
    private bool isDead;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => isDead;
    public float ScoreMovementGraceSeconds { get; set; } = 1.5f;
    public float ScoreMovementRatio => Mathf.Clamp01(velocity.magnitude / Mathf.Max(0.01f, maxSpeed));
    public bool IsChargingAttack => GetHighestWeaponAnimationPhase() is WeaponAnimationPhase.Charge
        or WeaponAnimationPhase.Ready or WeaponAnimationPhase.Hold;
    public event System.Action<int, int> HealthChanged;
    public event System.Action<Player> Died;
    private bool battleEntryLocked;
    private Vector3 entryHoldPosition;
    private BattleEntryDirector entryDirector;
    private readonly HashSet<InputAction> suppressedEntryInputs = new();
    public bool IsBattleEntryLocked => battleEntryLocked;
    private PlayableGraph entryAnimationGraph;
    private AnimationClipPlayable entryAnimationPlayable;
    private bool hasEntryAnimationGraph;
    private float entryAnimationTime;
    private float entryAnimationDuration;
    private bool entryAnimationLoops;
    public bool IsPlayingBattleEntryTransformation { get; private set; }

    public bool PlayBattleEntryTransformation(AnimationClip clip)
    {
        StopBattleEntryTransformation();
        if (!battleEntryLocked || clip == null) return false;
        CacheRollReferences();
        if (spriteAnimator == null || !spriteAnimator.isActiveAndEnabled)
        {
            Debug.LogWarning("Battle entry transformation requires the Player's body Sprite Animator.", this);
            return false;
        }
        entryAnimationGraph = PlayableGraph.Create($"Battle Entry - {clip.name}");
        entryAnimationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(entryAnimationGraph, "Body", spriteAnimator);
        entryAnimationPlayable = AnimationClipPlayable.Create(entryAnimationGraph, clip);
        entryAnimationPlayable.SetApplyFootIK(false);
        entryAnimationPlayable.SetApplyPlayableIK(false);
        // Manual time prevents replay even if the authored clip's Loop Time is checked.
        entryAnimationPlayable.SetSpeed(0d);
        output.SetSourcePlayable(entryAnimationPlayable);
        entryAnimationTime = 0f;
        entryAnimationDuration = Mathf.Max(0f, clip.length);
        entryAnimationLoops = clip.isLooping;
        hasEntryAnimationGraph = true;
        IsPlayingBattleEntryTransformation = entryAnimationDuration > 0f;
        entryAnimationGraph.Play();
        entryAnimationGraph.Evaluate(0f);
        return true;
    }

    public void AdvanceBattleEntryTransformation(float deltaTime)
    {
        if (!hasEntryAnimationGraph || !entryAnimationGraph.IsValid() || deltaTime < 0f) return;
        if (IsPlayingBattleEntryTransformation)
        {
            entryAnimationTime = Mathf.Min(entryAnimationDuration, entryAnimationTime + deltaTime);
            // Non-looping clips can have a key at exact duration. Looping clips must stay just inside it.
            float lastSampleTime = entryAnimationLoops ? Mathf.Max(0f, entryAnimationDuration - 0.00001f) : entryAnimationDuration;
            entryAnimationPlayable.SetTime(Mathf.Min(entryAnimationTime, lastSampleTime));
            if (entryAnimationTime >= entryAnimationDuration) IsPlayingBattleEntryTransformation = false;
        }
        entryAnimationGraph.Evaluate(0f);
    }

    private void StopBattleEntryTransformation()
    {
        if (hasEntryAnimationGraph && entryAnimationGraph.IsValid()) entryAnimationGraph.Destroy();
        hasEntryAnimationGraph = false;
        IsPlayingBattleEntryTransformation = false;
    }

    public void LockBattleEntry(BattleEntryDirector director, float rotationOffset)
    {
        entryDirector = director;
        battleEntryLocked = true;
        entryHoldPosition = transform.position;
        velocityOverrideSource = null;
        velocityOverrideTimer = 0f;
        velocity = Vector3.zero;
        currentRollSpeed = 0f;
        // Visual-only clockwise rotation; actual movement direction remains Vector2.up.
        SetVisualDirectionOverride(director, Quaternion.Euler(0f, 0f, rotationOffset) * Vector2.up);
    }

    public void ReleaseBattleEntry(BattleEntryDirector director)
    {
        if (entryDirector != director) return;
        StopBattleEntryTransformation();
        RemoveVisualDirectionOverride(director);
        battleEntryLocked = false;
        entryDirector = null;
        velocity = Vector3.zero;
        currentRollSpeed = 0f;
        UpdatePlayerAnimationState(true);
    }

    public IEnumerable<InputAction> GetGameplayInputActions()
    {
        InitializeInputActions();
        yield return rotateAction;
        yield return thrustAction;
        yield return bombAction;
        foreach (PlayerWeaponController weapons in GetComponentsInChildren<PlayerWeaponController>(true))
            foreach (InputAction action in weapons.GetGameplayInputActions()) yield return action;
    }
    public void SuppressEntryInput(InputAction action) => suppressedEntryInputs.Add(action);
    public void AllowEntryInput(InputAction action) => suppressedEntryInputs.Remove(action);
    public bool IsGameplayInputBlocked(InputAction action)
    {
        if (action != null && suppressedEntryInputs.Contains(action))
        {
            if (Mathf.Abs(action.ReadValue<float>()) < 0.2f) suppressedEntryInputs.Remove(action);
            else return true;
        }
        return battleEntryLocked;
    }
    public bool TryAcceptGameplayInput(InputAction action)
    {
        if (action != null && suppressedEntryInputs.Contains(action)) return false;
        return !battleEntryLocked || (entryDirector != null && entryDirector.TryStartFromInput(action));
    }

    private void Awake()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = maxHealth;
        battleEntryLocked = SceneLoadManager.IsBattleEntryLoading;
        entryHoldPosition = transform.position;
    }

    public void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitDirection, GameObject source)
    {
        if (isDead || battleEntryLocked || amount <= 0 || !isActiveAndEnabled)
            return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        isDead = currentHealth == 0;
        HealthChanged?.Invoke(currentHealth, maxHealth);
        if (!isDead)
            return;

        // Keep the character and its camera alive, but stop input and committed attacks.
        foreach (Weapon weapon in GetComponentsInChildren<Weapon>(true))
            weapon.enabled = false;
        foreach (PlayerWeaponController controller in GetComponentsInChildren<PlayerWeaponController>(true))
            controller.enabled = false;
        velocity = Vector3.zero;
        enabled = false;
        Died?.Invoke(this);
    }

    [Header("Debug")]
    [SerializeField] bool printDebugLog;
    [SerializeField] bool drawDirectionLine;
    [SerializeField] bool showDirection;
    [SerializeField] GameObject indicator;

    [Header("Physic Setting")]
    [SerializeField] float gravity = 9.8f;
    [SerializeField] float maxFallSpeed = 15f;
    [SerializeField] float maxSpeed = 10f;
    [SerializeField] float acceleration = 15f;
    [SerializeField] float drag = 3f; // 관성을 서서히 줄여줄 공기 저항
    [SerializeField] float maxRollSpeed = 180f;
    [SerializeField] float rollAccel = 360f;

    [Header("Debug view")]
    [SerializeField] Vector3 velocity;
    [SerializeField] float currentSpeed;
    [SerializeField] Vector2 direction;

    [Header("Visual")]
    [SerializeField] Transform spriteZRotationTransform;
    [SerializeField] Transform spriteXRotationTransform;
    [SerializeField] Transform armPivot;
    [SerializeField] Transform armAimPivot;
    [SerializeField] SpriteRenderer armSpriteRenderer;
    [SerializeField, HideInInspector] Transform spriteTransform;
    [SerializeField] float spriteRotationOffset;
    [SerializeField] bool rotateArmToAim = true;
    [SerializeField] bool rotateArmOnlyWhileAttacking;
    [SerializeField] float armAimAngleOffset;
    [SerializeField] bool rotateArmPivotOnRoll = true;
    [SerializeField] bool invertArmRollRotation;
    [SerializeField] bool playArmRollAnimation = true;
    [SerializeField] bool rollArmGradually;
    [SerializeField] Animator armAnimator;
    [SerializeField] string armRollIdleAnimationState = "fly_idle";
    [SerializeField] string armUpperRollAnimationState = "direction_change";
    [SerializeField] int armRollAnimationLayer;

    [Header("Animation State")]
    [SerializeField] string neutralIdleAnimationState = "neutral_idle";
    [SerializeField] string neutralIdleBlinkAnimationState = "neutral_idle_blink";
    [SerializeField] string readyAnimationState = "ready";
    [SerializeField] string chargeAnimationState = "charge";
    [SerializeField] string holdAnimationState = "hold";
    [SerializeField] string releaseAnimationState = "release";
    [SerializeField] string attackAnimationState = "attack";
    [SerializeField] float blinkInterval = 4f;
    [SerializeField] float blinkIntervalVariance = 1.5f;
    [SerializeField] float minimumBlinkInterval = 0.5f;

    [Header("Roll")]
    [SerializeField] PlayerWeaponController weaponController;
    [SerializeField] Animator spriteAnimator;
    [SerializeField] string isUpperRollParameter = "IsUpperRoll";
    [SerializeField] string rollIdleAnimationState = "fly_idle";
    [SerializeField] string upperRollAnimationState = "direction_change";
    [SerializeField] int rollAnimationLayer;
    [SerializeField] float rollSectorOffsetDegrees;
    [SerializeField] bool drawRollSectorGizmo = true;
    [SerializeField] float rollSectorGizmoRadius = 1.25f;

    GroundChecker groundChecker;
    InputAction rotateAction;
    InputAction thrustAction;
    InputAction bombAction;

    private float currentRollSpeed = 0f;
    private bool grounded;
    private bool hasRollState;
    private bool isUpperRollState;
    private bool visualUpperRollState;
    private bool isRollAnimationPlaying;
    private bool hasEnteredRollAnimationState;
    private int rollAnimationStartFrame;
    private int playingRollStateShortNameHash;
    private int playingRollStateFullPathHash;
    private bool visualArmUpperRollState;
    private bool isArmRollAnimationPlaying;
    private bool hasEnteredArmRollAnimationState;
    private bool pendingArmRollState;
    private int armRollAnimationStartFrame;
    private int playingArmRollStateShortNameHash;
    private int playingArmRollStateFullPathHash;
    private bool isArmRollGradualRotationActive;
    private float armRollGradualStartXAngle;
    private float armRollGradualTargetXAngle;
    private float armRollGradualCurrentXAngle;
    private float armRollGradualStartTime;
    private float armRollGradualDuration = 0.1f;
    private readonly Dictionary<object, float> movementSpeedModifiers = new Dictionary<object, float>();
    private readonly Dictionary<Weapon, WeaponAnimationPhase> weaponAnimationPhases =
        new Dictionary<Weapon, WeaponAnimationPhase>();
    private readonly HashSet<object> busySources = new HashSet<object>();
    private readonly HashSet<Weapon> pendingBusyAnimationExits = new HashSet<Weapon>();
    private readonly Dictionary<Weapon, int> pendingBusyAnimationExitFrames =
        new Dictionary<Weapon, int>();
    private readonly List<Weapon> completedBusyAnimationExits = new List<Weapon>();
    private PlayerAnimationState currentAnimationState = PlayerAnimationState.FlyIdle;
    private PlayerActivityState activityState = PlayerActivityState.Idle;
    private float blinkTimer;
    private bool isBlinkAnimationPlaying;
    private object velocityOverrideSource;
    private Vector2 velocityOverrideDirection;
    private float velocityOverrideSpeed;
    private float velocityOverrideTimer;
    private float velocityOverrideExitMomentum = 1f;
    private object visualDirectionOverrideSource;
    private Vector2 visualDirectionOverride = Vector2.up;

    public bool HasVelocityOverride => velocityOverrideSource != null;
    public bool HasVisualDirectionOverride => visualDirectionOverrideSource != null;
    public PlayerActivityState ActivityState => activityState;
    public Vector2 Velocity => velocity;
    public Vector2 MovementDirection => direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;

    void Start()
    {
        groundChecker = GetComponentInChildren<GroundChecker>();
        velocity = Vector3.zero;
        currentSpeed = 0f;
        direction = Vector2.up;
        grounded = false;

        CacheSpriteTransform();
        CacheRollReferences();
        UpdateSpriteRotation();
        UpdateArmAimRotation();
        UpdateArmVisibility();
        ScheduleNextBlink();
    }

    void OnEnable()
    {
        InitializeInputActions();
        rotateAction.Enable();
        thrustAction.Enable();
        bombAction.Enable();
        bombAction.performed += OnBombPerformed;
    }

    void OnDisable()
    {
        StopBattleEntryTransformation();
        velocityOverrideSource = null;
        velocityOverrideTimer = 0f;
        visualDirectionOverrideSource = null;
        busySources.Clear();
        pendingBusyAnimationExits.Clear();
        pendingBusyAnimationExitFrames.Clear();
        completedBusyAnimationExits.Clear();

        if (bombAction != null)
        {
            bombAction.performed -= OnBombPerformed;
        }

        rotateAction?.Disable();
        thrustAction?.Disable();
        bombAction?.Disable();
    }

    void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        rollAnimationLayer = Mathf.Max(0, rollAnimationLayer);
        armRollAnimationLayer = Mathf.Max(0, armRollAnimationLayer);
        rollSectorGizmoRadius = Mathf.Max(0f, rollSectorGizmoRadius);
        blinkInterval = Mathf.Max(0f, blinkInterval);
        blinkIntervalVariance = Mathf.Max(0f, blinkIntervalVariance);
        minimumBlinkInterval = Mathf.Max(0.01f, minimumBlinkInterval);
    }

    void Update()
    {
        if (battleEntryLocked)
        {
            velocity = Vector3.zero;
            currentSpeed = currentRollSpeed = 0f;
            transform.position = entryHoldPosition;
            activityState = PlayerActivityState.Idle;
            UpdateSpriteRotation();
            UpdateArmVisibility();
            UpdatePlayerAnimationState();
            return;
        }
        grounded = groundChecker.CheckIfTouchingGround();

        UpdatePendingBusyAnimationExits();
        rotate();
        RefreshActivityState();
        UpdateArmVisibility();
        roll();
        UpdateRollAnimationState();
        UpdateArmRollAnimationState();
        UpdateSpriteRotation();
        UpdateArmAimRotation();
        UpdatePlayerAnimationState();
        updateVelocity(); // 함수 이름 변경

        transform.Translate(velocity * Time.deltaTime);
        currentSpeed = velocity.magnitude;

        // debug zone
        if (drawDirectionLine)
        {
            Debug.DrawRay(transform.position, direction * 2f, Color.red);
        }
        indicateDirection();
    }

    // 관성이 적용된 새로운 물리 계산 함수
    void updateVelocity()
    {
        if (TickVelocityOverride())
        {
            return;
        }

        float speedMultiplier = GetMovementSpeedMultiplier();

        // 1. 중력 적용 (기존 속도에 아래로 당기는 힘을 더해줌)
        if (grounded && velocity.y <= 0) // 땅에 닿아있고 아래로 떨어지는 중일 때만
        {
            velocity.y = 0; // 땅을 뚫지 않도록 Y축 속도 멈춤
        }
        else
        {
            velocity.y -= gravity * Time.deltaTime; // 매 프레임 중력 누적
            velocity.y = Mathf.Max(velocity.y, -maxFallSpeed); // 최대 낙하 속도 제한
        }

        // 2. 추진력(관성) 적용
        if (thrustAction != null && !IsGameplayInputBlocked(thrustAction) && thrustAction.IsPressed())
        {
            // 핵심: 바라보는 방향(direction)으로 가속도를 곱해서 기존 속도에 '더해줍니다(+)'
            velocity += (Vector3)(direction * acceleration * speedMultiplier * Time.deltaTime);

            // 속도가 무한정 빨라지지 않게 전체 속도 제한
            float currentMaxSpeed = maxSpeed * speedMultiplier;
            if (velocity.magnitude > currentMaxSpeed)
            {
                velocity = velocity.normalized * currentMaxSpeed;
            }
        }
        else
        {
            // 3. 공기 저항 적용 (스페이스바를 뗐을 때 서서히 멈추는 관성 감속)
            // X축(좌우) 속도를 0을 향해 서서히 줄여 미끄러지듯 멈추게 함
            velocity.x = Mathf.MoveTowards(velocity.x, 0, drag * Time.deltaTime);

            // 공중으로 솟구치고 있을 때도 공기 저항을 받아 부드럽게 정점으로 가도록 함
            if (velocity.y > 0)
            {
                velocity.y = Mathf.MoveTowards(velocity.y, 0, drag * Time.deltaTime);
            }
        }
    }

    void rotate()
    {
        if (HasVelocityOverride)
        {
            currentRollSpeed = 0f;
            return;
        }

        float rotateInput = rotateAction != null && !IsGameplayInputBlocked(rotateAction) ? rotateAction.ReadValue<float>() : 0f;
        float speedMultiplier = GetMovementSpeedMultiplier();
        float targetRollSpeed = rotateInput * maxRollSpeed * speedMultiplier;

        currentRollSpeed = Mathf.MoveTowards(
            currentRollSpeed,
            targetRollSpeed,
            rollAccel * speedMultiplier * Time.deltaTime
        );
        float angleToRotate = currentRollSpeed * Time.deltaTime;

        direction = Quaternion.Euler(0, 0, angleToRotate) * direction;
        direction = direction.normalized;
    }

    public bool StartVelocityOverride(
        object source,
        Vector2 overrideDirection,
        float speed,
        float duration,
        float exitMomentumMultiplier
    )
    {
        if (battleEntryLocked || source == null || overrideDirection.sqrMagnitude <= 0.0001f || duration <= 0f)
        {
            return false;
        }

        velocityOverrideSource = source;
        velocityOverrideDirection = overrideDirection.normalized;
        velocityOverrideSpeed = Mathf.Max(0f, speed);
        velocityOverrideTimer = duration;
        velocityOverrideExitMomentum = Mathf.Max(0f, exitMomentumMultiplier);
        velocity = velocityOverrideDirection * velocityOverrideSpeed;
        currentRollSpeed = 0f;
        return true;
    }

    public void SetVisualDirectionOverride(object source, Vector2 overrideDirection)
    {
        if (source == null || overrideDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        visualDirectionOverrideSource = source;
        visualDirectionOverride = overrideDirection.normalized;
        CancelRollAnimationTracking();
        UpdateSpriteRotation();
    }

    public void RemoveVisualDirectionOverride(object source)
    {
        if (source == null || visualDirectionOverrideSource != source)
        {
            return;
        }

        visualDirectionOverrideSource = null;
        UpdateSpriteRotation();
    }

    public void SetMovementDirection(Vector2 newDirection, bool resetRollSpeed = true)
    {
        if (newDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        direction = newDirection.normalized;
        if (resetRollSpeed)
        {
            currentRollSpeed = 0f;
        }

        UpdateSpriteRotation();
    }

    public void CancelVelocityOverride(object source, bool keepExitMomentum = true)
    {
        if (source == null || velocityOverrideSource != source)
        {
            return;
        }

        if (keepExitMomentum)
        {
            velocity = velocityOverrideDirection
                * velocityOverrideSpeed
                * velocityOverrideExitMomentum;
        }
        else
        {
            velocity = Vector2.zero;
        }

        velocityOverrideSource = null;
        velocityOverrideTimer = 0f;
    }

    public bool IsVelocityOverrideActive(object source)
    {
        return source != null && velocityOverrideSource == source;
    }

    bool TickVelocityOverride()
    {
        if (!HasVelocityOverride)
        {
            return false;
        }

        velocity = velocityOverrideDirection * velocityOverrideSpeed;
        velocityOverrideTimer -= Time.deltaTime;
        if (velocityOverrideTimer > 0f)
        {
            return true;
        }

        velocity = velocityOverrideDirection
            * velocityOverrideSpeed
            * velocityOverrideExitMomentum;
        velocityOverrideSource = null;
        velocityOverrideTimer = 0f;
        return true;
    }

    public void SetMovementSpeedModifier(object source, float multiplier)
    {
        if (source == null)
        {
            return;
        }

        movementSpeedModifiers[source] = Mathf.Max(0f, multiplier);
    }

    public void RemoveMovementSpeedModifier(object source)
    {
        if (source == null)
        {
            return;
        }

        movementSpeedModifiers.Remove(source);
    }

    public float GetMovementSpeedMultiplier()
    {
        float multiplier = 1f;
        foreach (float value in movementSpeedModifiers.Values)
        {
            multiplier *= value;
        }

        return multiplier;
    }

    public void SetWeaponAnimationPhase(Weapon source, WeaponAnimationPhase phase)
    {
        if (source == null)
        {
            return;
        }

        if (phase != WeaponAnimationPhase.None)
        {
            CancelRollAnimationTracking();
        }

        if (phase == WeaponAnimationPhase.None)
        {
            weaponAnimationPhases.Remove(source);
        }
        else
        {
            weaponAnimationPhases[source] = phase;
        }

        isBlinkAnimationPlaying = false;
        UpdatePlayerAnimationState(true);
    }

    public void ReplayWeaponAnimationPhase(Weapon source, WeaponAnimationPhase phase)
    {
        if (source == null || phase == WeaponAnimationPhase.None)
        {
            return;
        }

        weaponAnimationPhases[source] = phase;
        isBlinkAnimationPlaying = false;
        UpdatePlayerAnimationState(true);
    }

    public void EnterBusyState(object source)
    {
        if (source == null)
        {
            return;
        }

        busySources.Add(source);
        SetActivityState(PlayerActivityState.Busy);
    }

    public void ExitBusyStateImmediately(object source)
    {
        if (source == null)
        {
            return;
        }

        busySources.Remove(source);
        if (source is Weapon weapon)
        {
            pendingBusyAnimationExits.Remove(weapon);
            pendingBusyAnimationExitFrames.Remove(weapon);
            weaponAnimationPhases.Remove(weapon);
        }

        // OnDisable still needs to remove bookkeeping, but must not restart animation or input.
        if (!isActiveAndEnabled || isDead) return;
        RefreshActivityState();
        UpdatePlayerAnimationState(true);
    }

    public void ExitBusyStateAfterAnimation(Weapon source)
    {
        if (source == null)
        {
            return;
        }

        if (!busySources.Contains(source) || !weaponAnimationPhases.ContainsKey(source))
        {
            ExitBusyStateImmediately(source);
            return;
        }

        pendingBusyAnimationExits.Add(source);
        pendingBusyAnimationExitFrames[source] = Time.frameCount;
    }

    public void ClearWeaponAnimationPhase(Weapon source)
    {
        if (source == null)
        {
            return;
        }

        if (weaponAnimationPhases.Remove(source))
        {
            UpdatePlayerAnimationState(true);
        }
    }

    void UpdatePlayerAnimationState(bool forceReplay = false)
    {
        // The arrival graph owns the body through its one-shot and final-frame input wait.
        if (!isActiveAndEnabled || isDead || hasEntryAnimationGraph) return;
        WeaponAnimationPhase weaponPhase = GetHighestWeaponAnimationPhase();
        if (weaponPhase != WeaponAnimationPhase.None)
        {
            isBlinkAnimationPlaying = false;
            PlayPlayerAnimation(GetPlayerStateForWeaponPhase(weaponPhase), forceReplay);
            return;
        }

        if (isRollAnimationPlaying || isArmRollAnimationPlaying)
        {
            currentAnimationState = PlayerAnimationState.DirectionChange;
            return;
        }

        if (activityState != PlayerActivityState.Idle)
        {
            isBlinkAnimationPlaying = false;
            ScheduleNextBlink();
            PlayPlayerAnimation(PlayerAnimationState.FlyIdle, forceReplay);
            return;
        }

        if (isBlinkAnimationPlaying)
        {
            if (!IsAnimationFinished(spriteAnimator, neutralIdleBlinkAnimationState, rollAnimationLayer))
            {
                return;
            }

            isBlinkAnimationPlaying = false;
            ScheduleNextBlink();
            PlayPlayerAnimation(PlayerAnimationState.NeutralIdle, true);
            return;
        }

        blinkTimer -= Time.deltaTime;
        if (blinkTimer <= 0f)
        {
            if (TryPlayPlayerAnimation(PlayerAnimationState.NeutralIdleBlink, true))
            {
                isBlinkAnimationPlaying = true;
                return;
            }

            ScheduleNextBlink();
        }

        PlayPlayerAnimation(PlayerAnimationState.NeutralIdle, forceReplay);
    }

    void RefreshActivityState()
    {
        if (busySources.Count > 0)
        {
            SetActivityState(PlayerActivityState.Busy);
            return;
        }

        float rotateInput = rotateAction != null && !IsGameplayInputBlocked(rotateAction) ? rotateAction.ReadValue<float>() : 0f;
        bool hasMovementInput = Mathf.Abs(rotateInput) > 0.0001f
            || (thrustAction != null && !IsGameplayInputBlocked(thrustAction) && thrustAction.IsPressed());
        bool hasMainWeaponAction = weaponController != null
            && (weaponController.HasMainAttackInput || weaponController.IsMainWeaponAttacking);
        SetActivityState(hasMovementInput || hasMainWeaponAction
            ? PlayerActivityState.Ready
            : PlayerActivityState.Idle);
    }

    void SetActivityState(PlayerActivityState newState)
    {
        if (activityState == newState)
        {
            return;
        }

        activityState = newState;
        if (newState == PlayerActivityState.Busy)
        {
            CancelRollAnimationTracking();
        }
    }

    void UpdateArmVisibility()
    {
        if (armSpriteRenderer == null)
        {
            CacheAimVisualReferences();
        }

        if (armSpriteRenderer != null)
        {
            armSpriteRenderer.enabled = activityState == PlayerActivityState.Ready;
        }
    }

    void UpdatePendingBusyAnimationExits()
    {
        if (pendingBusyAnimationExits.Count == 0)
        {
            return;
        }

        completedBusyAnimationExits.Clear();
        foreach (Weapon source in pendingBusyAnimationExits)
        {
            if (source != null
                && pendingBusyAnimationExitFrames.TryGetValue(source, out int requestFrame)
                && Time.frameCount <= requestFrame)
            {
                continue;
            }

            if (source == null || !weaponAnimationPhases.TryGetValue(
                source,
                out WeaponAnimationPhase phase
            ))
            {
                completedBusyAnimationExits.Add(source);
                continue;
            }

            string stateName = GetPlayerAnimationStateName(GetPlayerStateForWeaponPhase(phase));
            if (IsAnimationFinished(spriteAnimator, stateName, rollAnimationLayer))
            {
                completedBusyAnimationExits.Add(source);
            }
        }

        if (completedBusyAnimationExits.Count == 0)
        {
            return;
        }

        for (int i = 0; i < completedBusyAnimationExits.Count; i++)
        {
            Weapon source = completedBusyAnimationExits[i];
            pendingBusyAnimationExits.Remove(source);
            pendingBusyAnimationExitFrames.Remove(source);
            busySources.Remove(source);
            if (source != null)
            {
                weaponAnimationPhases.Remove(source);
                source.NotifyBusyAnimationCompleted();
            }
        }

        completedBusyAnimationExits.Clear();
        RefreshActivityState();
        UpdatePlayerAnimationState(true);
    }

    WeaponAnimationPhase GetHighestWeaponAnimationPhase()
    {
        WeaponAnimationPhase highestPhase = WeaponAnimationPhase.None;
        bool highestIsBusy = false;
        foreach (KeyValuePair<Weapon, WeaponAnimationPhase> entry in weaponAnimationPhases)
        {
            if (entry.Key == null)
            {
                continue;
            }

            bool entryIsBusy = busySources.Contains(entry.Key);
            if ((entryIsBusy && !highestIsBusy)
                || (entryIsBusy == highestIsBusy && (int)entry.Value > (int)highestPhase))
            {
                highestPhase = entry.Value;
                highestIsBusy = entryIsBusy;
            }
        }

        return highestPhase;
    }

    static PlayerAnimationState GetPlayerStateForWeaponPhase(WeaponAnimationPhase phase)
    {
        switch (phase)
        {
            case WeaponAnimationPhase.Ready:
                return PlayerAnimationState.Ready;
            case WeaponAnimationPhase.Charge:
                return PlayerAnimationState.Charge;
            case WeaponAnimationPhase.Hold:
                return PlayerAnimationState.Hold;
            case WeaponAnimationPhase.Release:
                return PlayerAnimationState.Release;
            case WeaponAnimationPhase.Attack:
                return PlayerAnimationState.Attack;
            default:
                return PlayerAnimationState.FlyIdle;
        }
    }

    void PlayPlayerAnimation(PlayerAnimationState state, bool forceReplay)
    {
        if (state == currentAnimationState && !forceReplay)
        {
            return;
        }

        TryPlayPlayerAnimation(state, forceReplay);
    }

    bool TryPlayPlayerAnimation(PlayerAnimationState state, bool forceReplay)
    {
        string stateName = GetPlayerAnimationStateName(state);
        bool bodyPlayed = TryPlayAnimatorState(spriteAnimator, stateName, rollAnimationLayer, forceReplay);
        // Arm roll is an independent one-shot animation. Do not let weapon or
        // locomotion animation updates overwrite it before it reaches the end.
        bool armPlayed = isArmRollAnimationPlaying
            ? false
            : TryPlayAnimatorState(
                armAnimator,
                stateName,
                armRollAnimationLayer,
                forceReplay
            );

        if (!bodyPlayed && state == PlayerAnimationState.NeutralIdle)
        {
            bodyPlayed = TryPlayAnimatorState(
                spriteAnimator,
                GetRollIdleAnimationStateName(),
                rollAnimationLayer,
                forceReplay
            );
        }

        bool isWeaponState = state == PlayerAnimationState.Ready
            || state == PlayerAnimationState.Charge
            || state == PlayerAnimationState.Hold
            || state == PlayerAnimationState.Release
            || state == PlayerAnimationState.Attack;
        if (!bodyPlayed && isWeaponState)
        {
            bodyPlayed = TryPlayAnimatorState(
                spriteAnimator,
                GetRollIdleAnimationStateName(),
                rollAnimationLayer,
                forceReplay
            );
        }

        if (!isArmRollAnimationPlaying
            && !armPlayed
            && (state == PlayerAnimationState.NeutralIdle || isWeaponState))
        {
            TryPlayAnimatorState(
                armAnimator,
                GetArmRollIdleAnimationStateName(),
                armRollAnimationLayer,
                forceReplay
            );
        }

        if (bodyPlayed)
        {
            currentAnimationState = state;
        }

        return bodyPlayed;
    }

    string GetPlayerAnimationStateName(PlayerAnimationState state)
    {
        switch (state)
        {
            case PlayerAnimationState.NeutralIdle:
                return neutralIdleAnimationState;
            case PlayerAnimationState.NeutralIdleBlink:
                return neutralIdleBlinkAnimationState;
            case PlayerAnimationState.Ready:
                return readyAnimationState;
            case PlayerAnimationState.Charge:
                return chargeAnimationState;
            case PlayerAnimationState.Hold:
                return holdAnimationState;
            case PlayerAnimationState.Release:
                return releaseAnimationState;
            case PlayerAnimationState.Attack:
                return attackAnimationState;
            case PlayerAnimationState.DirectionChange:
                return GetRollAnimationStateName(isUpperRollState);
            default:
                return GetRollIdleAnimationStateName();
        }
    }

    void ScheduleNextBlink()
    {
        float randomOffset = Random.Range(-blinkIntervalVariance, blinkIntervalVariance);
        blinkTimer = Mathf.Max(minimumBlinkInterval, blinkInterval + randomOffset);
    }

    static bool CanUseAnimator(Animator animator) => animator != null && animator.isActiveAndEnabled
        && animator.runtimeAnimatorController != null && animator.layerCount > 0;

    static bool TryPlayAnimatorState(
        Animator animator,
        string stateName,
        int layerIndex,
        bool forceReplay
    )
    {
        if (!CanUseAnimator(animator) || string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        int clampedLayer = Mathf.Clamp(layerIndex, 0, animator.layerCount - 1);
        int shortHash = Animator.StringToHash(stateName);
        int fullHash = Animator.StringToHash(animator.GetLayerName(clampedLayer) + "." + stateName);
        if (!animator.HasState(clampedLayer, shortHash) && !animator.HasState(clampedLayer, fullHash))
        {
            return false;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(clampedLayer);
        if (!forceReplay && (stateInfo.shortNameHash == shortHash || stateInfo.fullPathHash == fullHash))
        {
            return true;
        }

        animator.Play(stateName, clampedLayer, 0f);
        return true;
    }

    static bool IsAnimationFinished(Animator animator, string stateName, int layerIndex)
    {
        if (!CanUseAnimator(animator))
        {
            return true;
        }

        int clampedLayer = Mathf.Clamp(layerIndex, 0, animator.layerCount - 1);
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(clampedLayer);
        int shortHash = Animator.StringToHash(stateName);
        int fullHash = Animator.StringToHash(animator.GetLayerName(clampedLayer) + "." + stateName);
        bool isExpectedState = stateInfo.shortNameHash == shortHash
            || stateInfo.fullPathHash == fullHash;
        return !isExpectedState || (!animator.IsInTransition(clampedLayer) && stateInfo.normalizedTime >= 1f);
    }

    public bool IsWeaponAnimationFinished(Weapon source, WeaponAnimationPhase phase)
    {
        if (source == null
            || !weaponAnimationPhases.TryGetValue(source, out WeaponAnimationPhase currentPhase)
            || currentPhase != phase)
        {
            return true;
        }

        string stateName = GetPlayerAnimationStateName(GetPlayerStateForWeaponPhase(phase));
        return IsAnimationFinished(spriteAnimator, stateName, rollAnimationLayer);
    }

    private void roll()
    {
        if (activityState != PlayerActivityState.Ready)
        {
            return;
        }

        if (HasVisualDirectionOverride)
        {
            CancelRollAnimationTracking();
            return;
        }

        if (weaponController == null)
        {
            CacheRollReferences();
        }

        if (weaponController == null || !CanUseAimDirectionForWeaponVisuals())
        {
            return;
        }

        Vector2 aimDirection = weaponController.GetAimDirection(transform.position);
        if (aimDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        bool shouldUseUpperRollState = IsAimInUpperRollSector(aimDirection);
        bool previousUpperRollState = hasRollState && isUpperRollState;
        if (hasRollState && previousUpperRollState == shouldUseUpperRollState)
        {
            return;
        }

        hasRollState = true;
        isUpperRollState = shouldUseUpperRollState;

        if (previousUpperRollState == shouldUseUpperRollState)
        {
            SetIsUpperRollParameter(shouldUseUpperRollState);
            visualUpperRollState = shouldUseUpperRollState;
            StartArmRollOrApplyRotation(shouldUseUpperRollState);
            return;
        }

        SetIsUpperRollParameter(shouldUseUpperRollState);
        StartArmRollOrApplyRotation(shouldUseUpperRollState);

        if (TryPlayRollAnimation(shouldUseUpperRollState))
        {
            visualUpperRollState = previousUpperRollState;
            isRollAnimationPlaying = true;
            hasEnteredRollAnimationState = false;
            rollAnimationStartFrame = Time.frameCount;
            return;
        }

        visualUpperRollState = shouldUseUpperRollState;
    }

    void UpdateSpriteRotation()
    {
        if (spriteZRotationTransform == null || spriteXRotationTransform == null)
        {
            CacheSpriteTransform();
        }

        Transform zRotationTransform = GetSpriteZRotationTransform();
        Transform xRotationTransform = GetSpriteXRotationTransform();

        if (zRotationTransform == null && xRotationTransform == null)
        {
            return;
        }

        Vector2 visualDirection = HasVisualDirectionOverride
            ? visualDirectionOverride
            : direction;
        if (visualDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        float rollXAngle = GetRollFallbackXAngle();
        float directionAngle = GetSpriteDirectionAngle(visualDirection);

        if (zRotationTransform != null && zRotationTransform == xRotationTransform)
        {
            zRotationTransform.localRotation = Quaternion.Euler(rollXAngle, 0f, directionAngle);
            return;
        }

        if (zRotationTransform != null)
        {
            zRotationTransform.localRotation = Quaternion.Euler(0f, 0f, directionAngle);
        }

        if (xRotationTransform != null)
        {
            xRotationTransform.localRotation = Quaternion.Euler(rollXAngle, 0f, 0f);
        }
    }

    void UpdateArmAimRotation()
    {
        Transform armRollPivot = GetArmRollPivot();
        Transform aimPivot = GetArmAimPivot();

        if (!rotateArmToAim && !rotateArmPivotOnRoll)
        {
            return;
        }

        if (weaponController == null || armRollPivot == null || aimPivot == null || armRollPivot == aimPivot)
        {
            CacheAimVisualReferences();
            armRollPivot = GetArmRollPivot();
            aimPivot = GetArmAimPivot();
        }

        if (weaponController == null || armRollPivot == null || aimPivot == null)
        {
            return;
        }

        bool canUpdateAimRotation =
            rotateArmToAim
            && (!rotateArmOnlyWhileAttacking || CanUseAimDirectionForWeaponVisuals());
        if (!canUpdateAimRotation && !rotateArmPivotOnRoll)
        {
            return;
        }

        float armRollAngle = GetArmRollFallbackXAngle();
        armRollPivot.localRotation = Quaternion.Euler(armRollAngle, 0f, 0f);

        float armAngle = aimPivot.localEulerAngles.z;
        if (canUpdateAimRotation)
        {
            Weapon aimWeapon = weaponController.CurrentAimWeapon;
            if (aimWeapon != null && TryApplyWeaponAimLine(aimWeapon, aimPivot))
            {
                return;
            }

            Vector2 aimDirection = weaponController.GetAimDirection(armRollPivot.position);
            if (aimDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            Vector2 localAimDirection = aimPivot.parent != null
                ? aimPivot.parent.InverseTransformDirection(aimDirection)
                : aimDirection;

            armAngle = Mathf.Atan2(localAimDirection.y, localAimDirection.x) * Mathf.Rad2Deg
                + armAimAngleOffset;
        }

        aimPivot.localRotation = Quaternion.Euler(0f, 0f, armAngle);
    }

    public bool UpdateWeaponAim(Weapon weapon)
    {
        if (weapon == null)
        {
            return false;
        }

        Transform aimPivot = GetArmAimPivot();
        return aimPivot != null && TryApplyWeaponAimLine(weapon, aimPivot);
    }

    bool TryApplyWeaponAimLine(Weapon weapon, Transform aimPivot)
    {
        if (!weapon.TryGetAimLine(out Transform muzzle, out Vector2 targetPoint)
            || muzzle == null
            || aimPivot == null
            || (muzzle != aimPivot && !muzzle.IsChildOf(aimPivot)))
        {
            return false;
        }

        Vector2 pivotPosition = aimPivot.position;
        Vector2 muzzleOffset = (Vector2)muzzle.position - pivotPosition;
        Vector2 targetOffset = targetPoint - pivotPosition;
        Vector2 barrelDirection = muzzle.right;
        if (targetOffset.sqrMagnitude <= 0.000001f
            || barrelDirection.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        barrelDirection.Normalize();
        float targetDistance = targetOffset.magnitude;
        float signedLineOffset = Cross(barrelDirection, muzzleOffset);
        float relativeTargetAngle = Vector2.SignedAngle(barrelDirection, targetOffset);
        float rotationDelta;

        if (targetDistance + 0.0001f >= Mathf.Abs(signedLineOffset))
        {
            float ratio = Mathf.Clamp(signedLineOffset / targetDistance, -1f, 1f);
            float offsetAngle = Mathf.Asin(ratio) * Mathf.Rad2Deg;
            // Select the solution whose barrel forward direction faces the
            // target side. The other geometric solution points approximately
            // 180 degrees away even though its infinite line also intersects.
            rotationDelta = Mathf.DeltaAngle(0f, relativeTargetAngle - offsetAngle);
        }
        else
        {
            // No rotated parallel line can cross a point inside its fixed
            // perpendicular offset. Aim the pivot directly at the target.
            rotationDelta = relativeTargetAngle;
        }

        if (weaponController != null
            && weaponController.TryGetAimTurnSpeedLimit(out float turnSpeedLimit))
        {
            float maxDelta = Mathf.Max(0f, turnSpeedLimit) * Time.deltaTime;
            rotationDelta = Mathf.Clamp(rotationDelta, -maxDelta, maxDelta);
        }

        aimPivot.rotation = Quaternion.AngleAxis(rotationDelta, Vector3.forward)
            * aimPivot.rotation;
        return true;
    }

    static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    bool CanUseAimDirectionForWeaponVisuals()
    {
        return weaponController != null
            && (weaponController.IsAttacking || weaponController.CanChangeDirectionWhileHovering);
    }

    void ApplyArmRollRotation(bool useUpperRollState)
    {
        if (!rotateArmPivotOnRoll)
        {
            return;
        }

        Transform armRollPivot = GetArmRollPivot();
        if (armRollPivot == null)
        {
            CacheAimVisualReferences();
            armRollPivot = GetArmRollPivot();
        }

        if (armRollPivot == null)
        {
            return;
        }

        isArmRollGradualRotationActive = false;
        visualArmUpperRollState = invertArmRollRotation ? !useUpperRollState : useUpperRollState;
        UpdateArmAimRotation();
    }

    void StartArmRollOrApplyRotation(bool useUpperRollState)
    {
        if (playArmRollAnimation && TryPlayArmRollAnimation(useUpperRollState))
        {
            pendingArmRollState = useUpperRollState;
            isArmRollAnimationPlaying = true;
            hasEnteredArmRollAnimationState = false;
            armRollAnimationStartFrame = Time.frameCount;
            BeginGradualArmRollRotation(useUpperRollState);
            return;
        }

        ApplyArmRollRotation(useUpperRollState);
    }

    void BeginGradualArmRollRotation(bool useUpperRollState)
    {
        bool shouldUseGradualRotation = rollArmGradually && rotateArmPivotOnRoll;
        if (!shouldUseGradualRotation)
        {
            isArmRollGradualRotationActive = false;
            return;
        }

        bool targetUpperRollState = invertArmRollRotation ? !useUpperRollState : useUpperRollState;

        armRollGradualStartXAngle = isArmRollGradualRotationActive
            ? armRollGradualCurrentXAngle
            : GetArmRollStateXAngle(visualArmUpperRollState);
        armRollGradualTargetXAngle = targetUpperRollState ? -180f : 0f;
        armRollGradualCurrentXAngle = armRollGradualStartXAngle;
        armRollGradualStartTime = Time.time;
        armRollGradualDuration = Mathf.Max(0.01f, GetArmRollAnimationClipLength(useUpperRollState));
        isArmRollGradualRotationActive = true;
    }

    bool TryPlayArmRollAnimation(bool useUpperRollState)
    {
        if (armAnimator == null)
        {
            CacheAimVisualReferences();
        }

        if (!CanUseAnimator(armAnimator))
        {
            return false;
        }

        string stateName = GetArmRollAnimationStateName(useUpperRollState);
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        int layerIndex = Mathf.Clamp(armRollAnimationLayer, 0, armAnimator.layerCount - 1);
        int shortNameHash = Animator.StringToHash(stateName);
        int fullPathHash = Animator.StringToHash(armAnimator.GetLayerName(layerIndex) + "." + stateName);
        if (!armAnimator.HasState(layerIndex, shortNameHash) && !armAnimator.HasState(layerIndex, fullPathHash))
        {
            return false;
        }

        playingArmRollStateShortNameHash = shortNameHash;
        playingArmRollStateFullPathHash = fullPathHash;
        currentAnimationState = PlayerAnimationState.DirectionChange;
        armAnimator.Play(stateName, layerIndex, 0f);
        return true;
    }

    bool IsAimInUpperRollSector(Vector2 aimDirection)
    {
        Vector2 localAimDirection = Quaternion.Euler(0f, 0f, -GetRollSectorAngle()) * aimDirection.normalized;
        return localAimDirection.y >= 0f;
    }

    bool TryPlayRollAnimation(bool useUpperRollState)
    {
        if (spriteAnimator == null)
        {
            CacheRollReferences();
        }

        if (!CanUseAnimator(spriteAnimator))
        {
            return false;
        }

        string stateName = GetRollAnimationStateName(useUpperRollState);
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        int layerIndex = Mathf.Clamp(rollAnimationLayer, 0, spriteAnimator.layerCount - 1);
        int shortNameHash = Animator.StringToHash(stateName);
        int fullPathHash = Animator.StringToHash(spriteAnimator.GetLayerName(layerIndex) + "." + stateName);
        if (!spriteAnimator.HasState(layerIndex, shortNameHash) && !spriteAnimator.HasState(layerIndex, fullPathHash))
        {
            return false;
        }

        playingRollStateShortNameHash = shortNameHash;
        playingRollStateFullPathHash = fullPathHash;
        currentAnimationState = PlayerAnimationState.DirectionChange;
        spriteAnimator.Play(stateName, layerIndex, 0f);
        return true;
    }

    void UpdateRollAnimationState()
    {
        if (!isRollAnimationPlaying || !CanUseAnimator(spriteAnimator))
        {
            return;
        }

        if (Time.frameCount == rollAnimationStartFrame)
        {
            return;
        }

        int layerIndex = Mathf.Clamp(rollAnimationLayer, 0, spriteAnimator.layerCount - 1);
        if (spriteAnimator.IsInTransition(layerIndex))
        {
            return;
        }

        AnimatorStateInfo stateInfo = spriteAnimator.GetCurrentAnimatorStateInfo(layerIndex);
        bool isPlayingRollState =
            stateInfo.shortNameHash == playingRollStateShortNameHash
            || stateInfo.fullPathHash == playingRollStateFullPathHash;

        if (!hasEnteredRollAnimationState)
        {
            if (isPlayingRollState)
            {
                hasEnteredRollAnimationState = true;
                return;
            }

            isRollAnimationPlaying = false;
            visualUpperRollState = isUpperRollState;
            return;
        }

        if (isPlayingRollState && stateInfo.normalizedTime < 1f)
        {
            return;
        }

        isRollAnimationPlaying = false;
        hasEnteredRollAnimationState = false;
        visualUpperRollState = isUpperRollState;
        UpdatePlayerAnimationState(true);
    }

    void UpdateArmRollAnimationState()
    {
        if (!isArmRollAnimationPlaying || !CanUseAnimator(armAnimator))
        {
            return;
        }

        if (Time.frameCount == armRollAnimationStartFrame)
        {
            return;
        }

        bool gradualRotationStillPlaying = UpdateGradualArmRollRotation();
        int layerIndex = Mathf.Clamp(armRollAnimationLayer, 0, armAnimator.layerCount - 1);
        if (armAnimator.IsInTransition(layerIndex))
        {
            return;
        }

        AnimatorStateInfo stateInfo = armAnimator.GetCurrentAnimatorStateInfo(layerIndex);
        bool isPlayingRollState =
            stateInfo.shortNameHash == playingArmRollStateShortNameHash
            || stateInfo.fullPathHash == playingArmRollStateFullPathHash;

        if (!hasEnteredArmRollAnimationState)
        {
            if (isPlayingRollState)
            {
                hasEnteredArmRollAnimationState = true;
                return;
            }

            isArmRollAnimationPlaying = false;
            isArmRollGradualRotationActive = false;
            ApplyArmRollRotation(isUpperRollState);
            TryPlayArmRollIdleAnimation(armRollAnimationLayer);
            return;
        }

        if (isPlayingRollState && stateInfo.normalizedTime < 1f)
        {
            return;
        }

        if (gradualRotationStillPlaying)
        {
            return;
        }

        isArmRollAnimationPlaying = false;
        hasEnteredArmRollAnimationState = false;
        isArmRollGradualRotationActive = false;
        ApplyArmRollRotation(pendingArmRollState);
        UpdatePlayerAnimationState(true);
        TryPlayArmRollIdleAnimation(armRollAnimationLayer);
    }

    void CancelRollAnimationTracking()
    {
        bool hadBodyRoll = isRollAnimationPlaying;
        bool hadArmRoll = isArmRollAnimationPlaying || isArmRollGradualRotationActive;

        isRollAnimationPlaying = false;
        hasEnteredRollAnimationState = false;
        isArmRollAnimationPlaying = false;
        hasEnteredArmRollAnimationState = false;
        isArmRollGradualRotationActive = false;

        if (hadBodyRoll)
        {
            visualUpperRollState = isUpperRollState;
        }

        if (hadArmRoll)
        {
            ApplyArmRollRotation(isUpperRollState);
        }
    }

    bool UpdateGradualArmRollRotation()
    {
        if (!isArmRollGradualRotationActive)
        {
            return false;
        }

        float elapsed = Time.time - armRollGradualStartTime;
        float progress = Mathf.Clamp01(elapsed / armRollGradualDuration);
        armRollGradualCurrentXAngle = Mathf.Lerp(
            armRollGradualStartXAngle,
            armRollGradualTargetXAngle,
            progress
        );

        return progress < 1f;
    }

    bool TryPlayRollIdleAnimation(int layerIndex)
    {
        if (!CanUseAnimator(spriteAnimator))
        {
            return false;
        }

        string stateName = GetRollIdleAnimationStateName();
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        layerIndex = Mathf.Clamp(layerIndex, 0, spriteAnimator.layerCount - 1);
        int shortNameHash = Animator.StringToHash(stateName);
        int fullPathHash = Animator.StringToHash(spriteAnimator.GetLayerName(layerIndex) + "." + stateName);
        if (!spriteAnimator.HasState(layerIndex, shortNameHash) && !spriteAnimator.HasState(layerIndex, fullPathHash))
        {
            return false;
        }

        spriteAnimator.Play(stateName, layerIndex, 0f);
        return true;
    }

    bool TryPlayArmRollIdleAnimation(int layerIndex)
    {
        if (!CanUseAnimator(armAnimator))
        {
            return false;
        }

        string stateName = GetArmRollIdleAnimationStateName();
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        int clampedLayerIndex = Mathf.Clamp(layerIndex, 0, armAnimator.layerCount - 1);
        int shortNameHash = Animator.StringToHash(stateName);
        int fullPathHash = Animator.StringToHash(armAnimator.GetLayerName(clampedLayerIndex) + "." + stateName);
        if (!armAnimator.HasState(clampedLayerIndex, shortNameHash) && !armAnimator.HasState(clampedLayerIndex, fullPathHash))
        {
            return false;
        }

        armAnimator.Play(stateName, clampedLayerIndex, 0f);
        return true;
    }

    void SetIsUpperRollParameter(bool value)
    {
        if (spriteAnimator == null)
        {
            CacheRollReferences();
        }

        if (!CanUseAnimator(spriteAnimator) || string.IsNullOrWhiteSpace(isUpperRollParameter))
        {
            return;
        }

        for (int i = 0; i < spriteAnimator.parameterCount; i++)
        {
            AnimatorControllerParameter parameter = spriteAnimator.GetParameter(i);
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == isUpperRollParameter)
            {
                spriteAnimator.SetBool(isUpperRollParameter, value);
                return;
            }
        }
    }

    string GetRollAnimationStateName(bool useUpperRollState)
    {
        string stateName = upperRollAnimationState;
        if (!string.IsNullOrWhiteSpace(stateName))
        {
            return stateName;
        }

        return "direction_change";
    }

    string GetRollIdleAnimationStateName()
    {
        if (!string.IsNullOrWhiteSpace(rollIdleAnimationState))
        {
            return rollIdleAnimationState;
        }

        return "fly_idle";
    }

    string GetArmRollAnimationStateName(bool useUpperRollState)
    {
        string stateName = armUpperRollAnimationState;
        if (!string.IsNullOrWhiteSpace(stateName))
        {
            return stateName;
        }

        return GetRollAnimationStateName(useUpperRollState);
    }

    string GetArmRollIdleAnimationStateName()
    {
        if (!string.IsNullOrWhiteSpace(armRollIdleAnimationState))
        {
            return armRollIdleAnimationState;
        }

        return GetRollIdleAnimationStateName();
    }

    float GetRollFallbackXAngle()
    {
        if (!hasRollState)
        {
            return 0f;
        }

        return visualUpperRollState ? 180f : 0f;
    }

    float GetArmRollFallbackXAngle()
    {
        if (isArmRollGradualRotationActive)
        {
            return armRollGradualCurrentXAngle;
        }

        if (!rotateArmPivotOnRoll || !hasRollState)
        {
            return 0f;
        }

        return GetArmRollStateXAngle(visualArmUpperRollState);
    }

    float GetArmRollStateXAngle(bool useUpperRollState)
    {
        return useUpperRollState ? -180f : 0f;
    }

    float GetArmRollAnimationClipLength(bool useUpperRollState)
    {
        if (armAnimator == null || armAnimator.runtimeAnimatorController == null)
        {
            return armRollGradualDuration;
        }

        string clipName = GetArmRollAnimationStateName(useUpperRollState);
        AnimationClip[] clips = armAnimator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null && clip.name == clipName)
            {
                return clip.length;
            }
        }

        return armRollGradualDuration;
    }

    float GetRollSectorAngle()
    {
        return GetSpriteDirectionAngle() + rollSectorOffsetDegrees;
    }

    float GetSpriteDirectionAngle()
    {
        return GetSpriteDirectionAngle(direction);
    }

    float GetSpriteDirectionAngle(Vector2 sourceDirection)
    {
        Vector2 spriteDirection = sourceDirection.sqrMagnitude > Mathf.Epsilon
            ? sourceDirection.normalized
            : Vector2.up;
        return Vector2.SignedAngle(Vector2.up, spriteDirection) + spriteRotationOffset;
    }

    Transform GetSpriteZRotationTransform()
    {
        return spriteZRotationTransform != null ? spriteZRotationTransform : spriteTransform;
    }

    Transform GetSpriteXRotationTransform()
    {
        return spriteXRotationTransform != null ? spriteXRotationTransform : spriteTransform;
    }

    void CacheSpriteTransform()
    {
        if (spriteTransform == null)
        {
            Transform namedSprite = transform.Find("PlayerSprite");
            if (namedSprite != null)
            {
                spriteTransform = namedSprite;
            }
        }

        if (spriteTransform == null)
        {
            SpriteRenderer[] childRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < childRenderers.Length; i++)
            {
                if (childRenderers[i].transform == transform)
                {
                    continue;
                }

                spriteTransform = childRenderers[i].transform;
                break;
            }
        }

        if (spriteZRotationTransform == null)
        {
            spriteZRotationTransform = spriteTransform;
        }

        if (spriteXRotationTransform == null)
        {
            spriteXRotationTransform = spriteTransform;
        }
    }

    void CacheAimVisualReferences()
    {
        if (weaponController == null)
        {
            weaponController = GetComponent<PlayerWeaponController>();
        }

        if (armPivot == null)
        {
            armPivot = transform.Find("characterProperties/armPivot");
        }

        EnsureArmAimPivot();

        Transform armSearchRoot = GetArmAimPivot();
        if (armSpriteRenderer == null && armSearchRoot != null)
        {
            armSpriteRenderer = FindArmSpriteRenderer(armSearchRoot);
        }

        if (armAnimator == null && armSearchRoot != null)
        {
            armAnimator = FindArmAnimator(armSearchRoot);
        }
    }

    Transform GetArmRollPivot()
    {
        return armPivot;
    }

    Transform GetArmAimPivot()
    {
        return armAimPivot != null ? armAimPivot : armPivot;
    }

    void EnsureArmAimPivot()
    {
        if (armPivot == null)
        {
            return;
        }

        if (armAimPivot == armPivot)
        {
            if (armPivot.parent != null && armPivot.name == "armAimPivot")
            {
                armAimPivot = armPivot;
                armPivot = armPivot.parent;
                return;
            }

            armAimPivot = null;
        }

        if (armAimPivot != null)
        {
            return;
        }

        Transform existingAimPivot = armPivot.Find("armAimPivot");
        if (existingAimPivot != null)
        {
            armAimPivot = existingAimPivot;
            return;
        }

        GameObject aimPivotObject = new GameObject("armAimPivot");
        armAimPivot = aimPivotObject.transform;
        armAimPivot.SetParent(armPivot, false);
        armAimPivot.localPosition = Vector3.zero;
        armAimPivot.localRotation = Quaternion.identity;
        armAimPivot.localScale = Vector3.one;

        for (int i = armPivot.childCount - 1; i >= 0; i--)
        {
            Transform child = armPivot.GetChild(i);
            if (child == armAimPivot)
            {
                continue;
            }

            child.SetParent(armAimPivot, false);
        }
    }

    SpriteRenderer FindArmSpriteRenderer(Transform searchRoot)
    {
        if (searchRoot == null)
        {
            return null;
        }

        SpriteRenderer directRenderer = searchRoot.GetComponent<SpriteRenderer>();
        if (directRenderer != null)
        {
            return directRenderer;
        }

        SpriteRenderer[] childRenderers = searchRoot.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer fallbackRenderer = null;
        for (int i = 0; i < childRenderers.Length; i++)
        {
            SpriteRenderer candidate = childRenderers[i];
            if (candidate == null)
            {
                continue;
            }

            if (fallbackRenderer == null)
            {
                fallbackRenderer = candidate;
            }

            if (candidate.GetComponentInParent<Weapon>() == null)
            {
                return candidate;
            }
        }

        return fallbackRenderer;
    }

    Animator FindArmAnimator(Transform searchRoot)
    {
        if (searchRoot == null)
        {
            return null;
        }

        Animator[] childAnimators = searchRoot.GetComponentsInChildren<Animator>(true);
        Animator fallbackAnimator = null;
        for (int i = 0; i < childAnimators.Length; i++)
        {
            Animator candidate = childAnimators[i];
            if (candidate == null || candidate.transform == searchRoot)
            {
                continue;
            }

            if (fallbackAnimator == null)
            {
                fallbackAnimator = candidate;
            }

            if (candidate.GetComponentInParent<Weapon>() == null)
            {
                return candidate;
            }
        }

        return fallbackAnimator;
    }

    void CacheRollReferences()
    {
        if (weaponController == null)
        {
            weaponController = GetComponent<PlayerWeaponController>();
        }

        if (spriteAnimator == null)
        {
            if (spriteXRotationTransform == null)
            {
                CacheSpriteTransform();
            }

            Transform xRotationTransform = GetSpriteXRotationTransform();
            if (xRotationTransform != null)
            {
                spriteAnimator = xRotationTransform.GetComponent<Animator>();
            }
        }
    }

    void indicateDirection()
    {
        if (!showDirection) return;
        indicator.transform.localPosition = new Vector3(direction.x, direction.y, 0);
    }

    void InitializeInputActions()
    {
        if (rotateAction != null && thrustAction != null && bombAction != null)
        {
            return;
        }

        rotateAction = new InputAction(name: "Rotate", type: InputActionType.Value, expectedControlType: "Axis");
        rotateAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/d")
            .With("Positive", "<Keyboard>/a");
        rotateAction.AddBinding("<Gamepad>/leftStick/x").WithProcessor("Invert");

        thrustAction = new InputAction(name: "Thrust", type: InputActionType.Button);
        thrustAction.AddBinding("<Keyboard>/space");
        thrustAction.AddBinding("<Gamepad>/buttonSouth");

        bombAction = new InputAction(name: "Bomb", type: InputActionType.Button);
        bombAction.AddBinding("<Keyboard>/leftCtrl");
        bombAction.AddBinding("<Gamepad>/leftShoulder");
    }

    void OnBombPerformed(InputAction.CallbackContext context)
    {
        if (!TryAcceptGameplayInput(context.action)) return;
        ActivateBomb();
    }

    void ActivateBomb()
    {
        Projectile[] projectiles = FindObjectsByType<Projectile>(FindObjectsSortMode.None);
        for (int i = 0; i < projectiles.Length; i++)
        {
            Destroy(projectiles[i].gameObject);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawRollSectorGizmo || rollSectorGizmoRadius <= 0f)
        {
            return;
        }

        Vector3 center = transform.position;
        Quaternion sectorRotation = Quaternion.Euler(0f, 0f, GetRollSectorAngle());
        Vector3 dividerDirection = sectorRotation * Vector3.right;
        Vector3 upperDirection = sectorRotation * Vector3.up;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(
            center - dividerDirection * rollSectorGizmoRadius,
            center + dividerDirection * rollSectorGizmoRadius
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(center, center + upperDirection * rollSectorGizmoRadius * 0.5f);
    }

}
