using UnityEngine;

public sealed class SpeedEnemy : CombatEnemy
{
    public enum DashPhase { Dashing, Aiming, BetweenDashes, Resting }

    [Header("Three-Dash Cycle")]
    [SerializeField, Min(1)] private int dashesPerCycle = 3;
    [SerializeField, Min(0f)] private float minimumDashTravel = 2f;
    [SerializeField, Min(0f)] private float aimDelay = 0.12f;
    [SerializeField, Min(0f)] private float betweenDashDelay = 0.12f;
    [SerializeField, Min(0f)] private float restDuration = 1.5f;
    [SerializeField] private DashPhase phase;
    [SerializeField] private int completedDashes;

    private bool cycleStarted;
    private float phaseTimer;
    public DashPhase Phase => phase;
    public int CompletedDashes => completedDashes;

    protected override void Update()
    {
        if (!CanAct())
            return;

        if (!cycleStarted)
        {
            cycleStarted = true;
            BeginDash();
        }

        switch (phase)
        {
            case DashPhase.Dashing:
                MoveTo(currentMoveTarget, moveSpeed);
                Face(moveDirection);
                currentState = EnemyState.Moving;
                if (Vector2.Distance(transform.position, currentMoveTarget) <= Mathf.Max(0.01f, targetReachDistance))
                {
                    phase = DashPhase.Aiming;
                    phaseTimer = aimDelay;
                    moveDirection = Vector2.zero;
                }
                break;
            case DashPhase.Aiming:
                currentState = EnemyState.Cooldown;
                Face(GetDirectionToPlayer());
                phaseTimer -= Time.deltaTime;
                if (phaseTimer <= 0f)
                {
                    Fire(GetAttackDirection(transform.position));
                    completedDashes++;
                    bool rest = completedDashes >= Mathf.Max(1, dashesPerCycle);
                    phase = rest ? DashPhase.Resting : DashPhase.BetweenDashes;
                    phaseTimer = rest ? restDuration : betweenDashDelay;
                }
                break;
            default:
                currentState = EnemyState.Cooldown;
                moveDirection = Vector2.zero;
                phaseTimer -= Time.deltaTime;
                if (phaseTimer <= 0f)
                {
                    if (phase == DashPhase.Resting)
                        completedDashes = 0;
                    BeginDash();
                }
                break;
        }
    }

    private void BeginDash()
    {
        Vector2 center = targetPlayer.transform.position;
        Vector2 position = transform.position;
        float minRadius = Mathf.Max(0f, orbitMinRadius);
        float maxRadius = Mathf.Max(minRadius, orbitMaxRadius);
        // Freeze the destination for this dash; a moving player does not drag it along.
        Vector2 best = center + Vector2.right * maxRadius;
        float bestDistance = -1f;
        for (int i = 0; i < 12; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(minRadius, maxRadius);
            float distance = Vector2.Distance(position, candidate);
            if (distance > bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
            if (distance >= minimumDashTravel)
                break;
        }
        currentMoveTarget = best;
        currentMoveOffset = best - center;
        hasMoveTarget = true;
        phase = DashPhase.Dashing;
    }

    public override void SetTarget(Player player)
    {
        base.SetTarget(player);
        cycleStarted = false;
        completedDashes = 0;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        orbitMinRadius = Mathf.Max(0f, orbitMinRadius);
        orbitMaxRadius = Mathf.Max(orbitMinRadius, orbitMaxRadius);
        targetReachDistance = Mathf.Max(0.01f, targetReachDistance);
        dashesPerCycle = Mathf.Max(1, dashesPerCycle);
        minimumDashTravel = Mathf.Max(0f, minimumDashTravel);
        aimDelay = Mathf.Max(0f, aimDelay);
        betweenDashDelay = Mathf.Max(0f, betweenDashDelay);
        restDuration = Mathf.Max(0f, restDuration);
    }

    protected override void OnDrawGizmos()
    {
        if (targetPlayer == null)
            return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(targetPlayer.transform.position, orbitMinRadius);
        Gizmos.DrawWireSphere(targetPlayer.transform.position, orbitMaxRadius);
        if (hasMoveTarget)
        {
            Gizmos.DrawLine(transform.position, currentMoveTarget);
            Gizmos.DrawWireSphere(currentMoveTarget, 0.15f);
        }
    }
}
