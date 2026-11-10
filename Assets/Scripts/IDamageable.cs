using UnityEngine;

public interface IDamageable
{
    void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitDirection, GameObject source);
}
