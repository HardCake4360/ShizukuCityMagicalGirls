using UnityEngine;

public sealed class DefensiveEnemy : CombatEnemy
{
    [Header("Slow Turning Shield Carrier")]
    [SerializeField, Min(0f)] private float turnSpeed = 30f;
    [SerializeField, Min(0f)] private float stoppingDistance = 1.5f;
    [SerializeField] private EnemyDirectionalShield shield;

    protected override void Awake()
    {
        base.Awake();
        if (shield == null)
            shield = GetComponentInChildren<EnemyDirectionalShield>();
    }

    protected override void Update()
    {
        if (!CanAct())
            return;

        Vector2 toPlayer = (Vector2)targetPlayer.transform.position - (Vector2)transform.position;
        Face(toPlayer, Mathf.Max(0f, turnSpeed));
        if (toPlayer.magnitude > stoppingDistance)
        {
            // Move along the current facing, not directly toward the player: no instant shield turn.
            float step = Mathf.Min(moveSpeed * Time.deltaTime, toPlayer.magnitude - stoppingDistance);
            moveDirection = transform.right;
            transform.position += (Vector3)(moveDirection * Mathf.Max(0f, step));
            currentState = EnemyState.Moving;
        }
        else
        {
            moveDirection = Vector2.zero;
            currentState = EnemyState.Idle;
        }
    }

    public override void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitDirection, GameObject source)
    {
        if (IsDead || amount <= 0)
            return;

        // Also gate body hits, so multi-hit beams/explosions cannot bypass the front shield.
        if (shield != null && shield.Blocks(hitPoint, hitDirection, source))
        {
            shield.ShowBlockedHit();
            return;
        }
        base.TakeDamage(amount, hitPoint, hitDirection, source);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        turnSpeed = Mathf.Max(0f, turnSpeed);
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
    }

    protected override bool IsFlankHit(Vector2 point, Vector2 direction, GameObject source) =>
        shield != null && shield.isActiveAndEnabled && !shield.Blocks(point, direction, source);
}
