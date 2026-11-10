using System.Collections.Generic;
using UnityEditor;

[CustomEditor(typeof(CombatEnemy), true)]
[CanEditMultipleObjects]
public sealed class CombatEnemyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        // Do not expose the legacy orbit/attack timers that these new controllers do not use.
        List<string> excluded = new()
        {
            "m_Script", "moveLerpSpeed", "attackDelay", "attackCooldown",
            "currentMoveOffset", "currentMoveTarget", "lockedAttackDirection", "moveDirection"
        };
        if (!(target is SpeedEnemy))
            excluded.AddRange(new[] { "targetReachDistance", "orbitMinRadius", "orbitMaxRadius", "fireControlSystem" });
        if (target is DefensiveEnemy)
            excluded.AddRange(new[] { "projectileSpawnPoint", "projectilePrefab", "projectileSpeed",
                "projectileDamage", "projectileLifetime", "muzzleOffset" });

        DrawPropertiesExcluding(serializedObject, excluded.ToArray());
        serializedObject.ApplyModifiedProperties();
    }
}
