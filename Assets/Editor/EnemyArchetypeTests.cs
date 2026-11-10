using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class EnemyArchetypeTests
{
    private readonly List<GameObject> instances = new();

    [TearDown]
    public void Cleanup()
    {
        foreach (GameObject instance in instances)
            if (instance != null)
                Object.DestroyImmediate(instance);
        instances.Clear();
    }

    private T Create<T>(string name) where T : Enemy
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefab/Enemies/{name}.prefab");
        Assert.That(prefab, Is.Not.Null);
        GameObject instance = Object.Instantiate(prefab);
        instances.Add(instance);
        T enemy = instance.GetComponent<T>();
        Assert.That(enemy, Is.Not.Null);
        Assert.That(instance.GetComponentsInChildren<Enemy>(), Has.Length.EqualTo(1));
        Call(enemy, "Awake");
        Assert.That(enemy.GetComponent<Collider2D>(), Is.Not.Null);
        Assert.That(enemy.GetComponentInChildren<SpriteRenderer>().sprite, Is.Not.Null);
        GameObject projectile = new SerializedObject(enemy).FindProperty("projectilePrefab").objectReferenceValue as GameObject;
        Assert.That(projectile.GetComponent<EnemyProjectile>(), Is.Not.Null);
        return enemy;
    }

    private Player CreateTarget()
    {
        GameObject instance = new GameObject("Test Player");
        instances.Add(instance);
        return instance.AddComponent<Player>();
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static int Health(Enemy enemy) => new SerializedObject(enemy).FindProperty("currentHealth").intValue;

    [Test]
    public void PrefabsAndMissionReferencesAreValid()
    {
        Create<RegularEnemy>("RegularEnemy");
        Create<SpeedEnemy>("SpeedEnemy");
        DefensiveEnemy defensive = Create<DefensiveEnemy>("DefensiveEnemy");
        Assert.That(defensive.GetComponentInChildren<EnemyDirectionalShield>(), Is.Not.Null);
        MissionDefinition mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>("Assets/Resources/Missions/PrototypeMission.asset");
        Assert.That(mission.Enemies, Has.Length.EqualTo(3));
        foreach (EnemySpawnEntry entry in mission.Enemies)
        {
            Assert.That(entry.Weight, Is.GreaterThan(0));
            Assert.That(entry.Enemy.Prefab.GetComponent<CombatEnemy>(), Is.Not.Null);
        }
    }

    [Test]
    public void ShieldBlocksFrontButNotSideOrRearIncludingBodyHits()
    {
        DefensiveEnemy enemy = Create<DefensiveEnemy>("DefensiveEnemy");
        EnemyDirectionalShield shield = enemy.GetComponentInChildren<EnemyDirectionalShield>();
        Call(shield, "Awake");
        Assert.That(shield.GetComponent<PolygonCollider2D>().GetPath(0), Has.Length.EqualTo(50));
        Assert.That(shield.GetComponent<LineRenderer>().positionCount, Is.EqualTo(25));
        Assert.That(shield.Blocks(Vector2.right, Vector2.left, null), Is.True);
        Assert.That(shield.Blocks(Vector2.up, Vector2.down, null), Is.False);
        Assert.That(shield.Blocks(Vector2.left, Vector2.right, null), Is.False);
        int initial = Health(enemy);
        enemy.TakeDamage(int.MaxValue, Vector2.right, Vector2.left, null);
        Assert.That(Health(enemy), Is.EqualTo(initial));
        IDamageable shieldReceiver = shield.GetComponent<Collider2D>().GetComponentInParent<IDamageable>();
        Assert.That(shieldReceiver, Is.SameAs(enemy));
        shieldReceiver.TakeDamage(1, Vector2.up, Vector2.down, null);
        Assert.That(Health(enemy), Is.EqualTo(initial - 1));
        enemy.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        Assert.That(shield.Blocks(Vector2.up, Vector2.down, null), Is.True);
        Assert.That(shield.Blocks(Vector2.right, Vector2.left, null), Is.False);
        shield.gameObject.SetActive(false);
        enemy.TakeDamage(1, Vector2.up, Vector2.down, null);
        Assert.That(Health(enemy), Is.EqualTo(initial - 2));
    }

    [Test]
    public void SpeedTypeSamplesFixedDestinationsAndRestsAfterThreeShots()
    {
        SpeedEnemy enemy = Create<SpeedEnemy>("SpeedEnemy");
        Player player = CreateTarget();
        player.transform.position = new Vector3(10f, 20f, 0f);
        enemy.SetTarget(player);
        for (int i = 0; i < 30; i++)
        {
            Call(enemy, "BeginDash");
            Vector2 destination = new SerializedObject(enemy).FindProperty("currentMoveTarget").vector2Value;
            Assert.That(Vector2.Distance(player.transform.position, destination), Is.InRange(2.999f, 6.001f));
        }
        Vector2 snapshot = new SerializedObject(enemy).FindProperty("currentMoveTarget").vector2Value;
        player.transform.position += Vector3.up * 10f;
        Assert.That(new SerializedObject(enemy).FindProperty("currentMoveTarget").vector2Value, Is.EqualTo(snapshot));
        Set(enemy, "cycleStarted", true);
        // Avoid spawning runtime projectiles in an edit-mode state-machine test.
        typeof(Enemy).GetField("projectilePrefab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(enemy, null);
        for (int i = 0; i < 3; i++)
        {
            Set(enemy, "phase", SpeedEnemy.DashPhase.Aiming);
            Set(enemy, "phaseTimer", 0f);
            Call(enemy, "Update");
            Assert.That(enemy.CompletedDashes, Is.EqualTo(i + 1));
            Assert.That(enemy.Phase, Is.EqualTo(i == 2 ? SpeedEnemy.DashPhase.Resting : SpeedEnemy.DashPhase.BetweenDashes));
        }
        Vector3 position = enemy.transform.position;
        Set(enemy, "phaseTimer", 100f);
        Call(enemy, "Update");
        Assert.That(enemy.transform.position, Is.EqualTo(position));
        Set(enemy, "phaseTimer", 0f);
        Call(enemy, "Update");
        Assert.That(enemy.CompletedDashes, Is.Zero);
        Assert.That(enemy.Phase, Is.EqualTo(SpeedEnemy.DashPhase.Dashing));
    }

    [Test]
    public void RegularTypeFiresOneBulletInItsForwardDirection()
    {
        RegularEnemy enemy = Create<RegularEnemy>("RegularEnemy");
        Player player = CreateTarget();
        player.transform.position = Vector3.right * 10f;
        enemy.SetTarget(player);
        Set(enemy, "fireTimer", 0f);
        Call(enemy, "Update");
        EnemyProjectile[] bullets = Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None);
        Assert.That(bullets, Has.Length.EqualTo(1));
        foreach (EnemyProjectile bullet in bullets)
        {
            instances.Add(bullet.gameObject);
            Assert.That(Vector2.Angle(Vector2.right, bullet.GetDirection()), Is.LessThanOrEqualTo(0.001f));
            Assert.That(new SerializedObject(bullet).FindProperty("speed").floatValue, Is.EqualTo(8f));
        }
        Set(enemy, "fireTimer", 100f);
        Call(enemy, "Update");
        Assert.That(Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
    }

    [Test]
    public void EnemyProjectileIgnoresFriendlyBodyAndShield()
    {
        DefensiveEnemy enemy = Create<DefensiveEnemy>("DefensiveEnemy");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Enemies/EnemyBullet.prefab");
        GameObject instance = Object.Instantiate(prefab);
        instances.Add(instance);
        EnemyProjectile projectile = instance.GetComponent<EnemyProjectile>();
        MethodInfo filter = typeof(EnemyProjectile).GetMethod("IsOwnerCollider", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (Collider2D collider in enemy.GetComponentsInChildren<Collider2D>())
            Assert.That((bool)filter.Invoke(projectile, new object[] { collider }), Is.True);
        Player player = CreateTarget();
        Collider2D playerCollider = player.gameObject.AddComponent<CircleCollider2D>();
        Assert.That((bool)filter.Invoke(projectile, new object[] { playerCollider }), Is.False);
    }

    [Test]
    public void ShieldAndBodyUseOneDamageAndLockOnIdentity()
    {
        DefensiveEnemy enemy = Create<DefensiveEnemy>("DefensiveEnemy");
        EnemyDirectionalShield shield = enemy.GetComponentInChildren<EnemyDirectionalShield>();
        Call(shield, "Awake");
        GameObject weaponObject = new GameObject("Test Hitscan");
        instances.Add(weaponObject);
        HitscanWeapon weapon = weaponObject.AddComponent<HitscanWeapon>();
        typeof(Weapon).GetField("damage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(weapon, 1);
        MethodInfo apply = typeof(HitscanWeapon).GetMethod("ApplyDamage", BindingFlags.Instance | BindingFlags.NonPublic);
        int initial = Health(enemy);
        apply.Invoke(weapon, new object[] { enemy.GetComponent<Collider2D>(), Vector2.left });
        apply.Invoke(weapon, new object[] { shield.GetComponent<Collider2D>(), Vector2.left });
        Assert.That(Health(enemy), Is.EqualTo(initial - 1));
        HomingMissileWeapon homing = weaponObject.AddComponent<HomingMissileWeapon>();
        MethodInfo resolve = typeof(HomingMissileWeapon).GetMethod("GetDamageableTransform", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(resolve.Invoke(homing, new object[] { shield.GetComponent<Collider2D>() }), Is.SameAs(enemy.transform));
        Assert.That(resolve.Invoke(homing, new object[] { enemy.GetComponent<Collider2D>() }), Is.SameAs(enemy.transform));
    }

    [Test]
    public void BattleSceneEnablesInitialThreeTypeWave()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GamePlayScene.unity", OpenSceneMode.Additive);
        try
        {
            MissionRuntimeController runtime = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                runtime = root.GetComponentInChildren<MissionRuntimeController>();
                if (runtime != null)
                    break;
            }
            Assert.That(runtime, Is.Not.Null);
            Assert.That(new SerializedObject(runtime).FindProperty("spawnEachEnemyTypeInitially").boolValue, Is.True);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
