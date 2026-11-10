using UnityEngine;

/// <summary>One emitted projectile, beam damage tick, slash, or thrust; survives delayed impacts.</summary>
public readonly struct AttackScoreContext
{
    private static long nextId;
    public AttackScoreContext(Player player, float movementRatio)
    {
        Id = ++nextId;
        Player = player;
        MovementRatio = Mathf.Clamp01(movementRatio);
    }
    public long Id { get; }
    public Player Player { get; }
    public float MovementRatio { get; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetIds() => nextId = 0;
}

/// <summary>Adds score attribution without changing IDamageable or the existing killer GameObject.</summary>
public static class CombatDamage
{
    public static void Apply(IDamageable target, int damage, Vector2 point, Vector2 direction,
        GameObject source, AttackScoreContext context)
    {
        if (target is Enemy enemy)
            enemy.TakeAttackDamage(damage, point, direction, source, context);
        else
            target?.TakeDamage(damage, point, direction, source);
    }
}
