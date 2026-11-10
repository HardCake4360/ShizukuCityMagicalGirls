using UnityEngine;

public sealed class RegularEnemy : CombatEnemy
{
    [Header("Forward Single Shot")]
    [SerializeField, Min(0f)] private float stoppingDistance = 0.6f;
    [SerializeField, Min(0.05f)] private float barrageInterval = 0.8f;
    [SerializeField, Min(0f)] private float initialFireDelay = 0.8f;

    private float fireTimer;

    protected override void Awake()
    {
        base.Awake();
        fireTimer = initialFireDelay;
    }

    protected override void Update()
    {
        if (!CanAct())
            return;

        Vector2 toPlayer = (Vector2)targetPlayer.transform.position - (Vector2)transform.position;
        lockedAttackDirection = GetDirectionToPlayer();
        Vector2 destination = (Vector2)targetPlayer.transform.position - lockedAttackDirection * stoppingDistance;
        if (toPlayer.magnitude > stoppingDistance)
            MoveTo(destination, moveSpeed);
        else
            moveDirection = Vector2.zero;

        Vector2 forward = moveDirection.sqrMagnitude > 0f ? moveDirection : lockedAttackDirection;
        Face(forward);
        currentState = moveDirection.sqrMagnitude > 0f ? EnemyState.Moving : EnemyState.Idle;
        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f)
            return;

        fireTimer = Mathf.Max(0.05f, barrageInterval);
        Fire(forward);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
        barrageInterval = Mathf.Max(0.05f, barrageInterval);
        initialFireDelay = Mathf.Max(0f, initialFireDelay);
    }
}
