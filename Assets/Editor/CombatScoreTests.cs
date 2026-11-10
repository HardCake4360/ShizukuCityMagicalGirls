using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CombatScoreTests
{
    private readonly List<GameObject> objects = new();
    private CombatScoreSettings settings;
    private CombatScoreManager score;
    private Player player;
    private float previousTimeScale;

    private GameObject Create(string name, params System.Type[] components)
    {
        GameObject result = new GameObject(name, components);
        objects.Add(result);
        return result;
    }
    private static void Set(object target, string field, object value)
    {
        System.Type type = target.GetType();
        FieldInfo info = null;
        while (type != null && info == null)
        { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
        Assert.That(info, Is.Not.Null, field);
        info.SetValue(target, value);
    }
    private static void Call(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);

    [SetUp]
    public void Setup()
    {
        previousTimeScale = Time.timeScale;
        settings = ScriptableObject.CreateInstance<CombatScoreSettings>();
        player = Create("Player").AddComponent<Player>();
        Call(player, "Awake");
        score = Create("Score").AddComponent<CombatScoreManager>();
        Set(score, "settings", settings);
        Set(score, "generateDefaultHUD", false);
        Call(score, "OnEnable");
        score.BeginRun(player);
    }

    [TearDown]
    public void Cleanup()
    {
        if (score != null)
            Call(score, "OnDisable");
        foreach (GameObject instance in objects)
            if (instance != null)
                Object.DestroyImmediate(instance);
        objects.Clear();
        Object.DestroyImmediate(settings);
        Time.timeScale = previousTimeScale;
    }

    private T EnemyOf<T>() where T : Enemy
    {
        T enemy = Create(typeof(T).Name).AddComponent<T>();
        Set(enemy, "currentHealth", 3);
        Set(enemy, "destroyOnDeath", false);
        score.RegisterEnemy(enemy);
        return enemy;
    }

    private void Kill(Enemy enemy, AttackScoreContext context, bool boss = false, GameObject source = null)
    {
        CombatDamage.Apply(enemy, 3, Vector2.zero, Vector2.right, source != null ? source : player.gameObject, context);
        score.ReportDefeat(enemy, boss, source != null ? source : player.gameObject);
    }

    [TestCase(0f, CombatStyleRank.None, "—")]
    [TestCase(0.01f, CombatStyleRank.D, "D")]
    [TestCase(100f, CombatStyleRank.DPlus, "D+")]
    [TestCase(220f, CombatStyleRank.C, "C")]
    [TestCase(350f, CombatStyleRank.CPlus, "C+")]
    [TestCase(500f, CombatStyleRank.B, "B")]
    [TestCase(650f, CombatStyleRank.BPlus, "B+")]
    [TestCase(800f, CombatStyleRank.A, "A")]
    [TestCase(950f, CombatStyleRank.APlus, "A+")]
    [TestCase(1100f, CombatStyleRank.S, "S")]
    [TestCase(1250f, CombatStyleRank.SPlus, "S+")]
    [TestCase(1300f, CombatStyleRank.SPlus, "S+")]
    public void AllTenRanksUseRequestedNames(float gauge, CombatStyleRank rank, string name)
    {
        Assert.That(settings.RankFor(gauge), Is.EqualTo(rank));
        Assert.That(CombatScoreSettings.RankName(rank), Is.EqualTo(name));
        Assert.That(System.Enum.GetValues(typeof(CombatStyleRank)).Length, Is.EqualTo(CombatScoreSettings.RankCount + 1));
    }

    [Test]
    public void DThresholdCanBeConfiguredWithoutRenumberingVisibleRanks()
    {
        settings.rankThresholds[0] = 50f;
        Call(settings, "OnValidate");
        Assert.That(settings.rankThresholds[0], Is.EqualTo(50f));
        Assert.That(settings.RankFor(49f), Is.EqualTo(CombatStyleRank.None));
        Assert.That(settings.RankFor(50f), Is.EqualTo(CombatStyleRank.D));
        Assert.That((int)CombatStyleRank.D, Is.Zero);
        Assert.That((int)CombatStyleRank.SPlus, Is.EqualTo(9));
    }

    [Test]
    public void OldEightRankSettingsKeepTheirThresholdsWhenUpgraded()
    {
        settings.maxGauge = 1000f;
        settings.rankThresholds = new[] { 0f, 100f, 220f, 350f, 500f, 650f, 800f, 950f };
        Call(settings, "OnValidate");
        Assert.That(settings.rankThresholds, Is.EqualTo(new[] { 0f, 100f, 220f, 350f, 500f, 650f, 800f,
            950f, 1100f, 1250f }));
        Assert.That(settings.maxGauge, Is.EqualTo(1300f));
        Assert.That(settings.RankFor(1250f), Is.EqualTo(CombatStyleRank.SPlus));
    }

    [Test]
    public void StyleGaugeCanReachAndCapAtSPlus()
    {
        for (int i = 0; i < 15; i++)
            Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.StyleGauge, Is.EqualTo(settings.maxGauge));
        Assert.That(score.Rank, Is.EqualTo(CombatStyleRank.SPlus));
        Assert.That(score.HighestRank, Is.EqualTo(CombatStyleRank.SPlus));
        Assert.That(score.TotalScore, Is.GreaterThan(settings.maxGauge));
    }

    [Test]
    public void BaseKillsChainAndRepetitionNeverReduceBasePoints()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.TotalScore, Is.EqualTo(100));
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.TotalScore, Is.EqualTo(220)); // 100 + chain 20
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.TotalScore, Is.EqualTo(352)); // 100 + 40 * .8
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.TotalScore, Is.EqualTo(488)); // 100 + 60 * .6
        Assert.That(score.LastBonus, Does.Contain("연속 격추"));
    }

    [Test]
    public void SameAttackGetsMultiKillButDifferentAttacksDoNot()
    {
        AttackScoreContext attack = new AttackScoreContext(player, 0f);
        Kill(EnemyOf<Enemy>(), attack);
        Kill(EnemyOf<Enemy>(), attack);
        Assert.That(score.TotalScore, Is.EqualTo(270)); // 100 + 100 + 20 + 50
        Assert.That(score.LastBonus, Does.Contain("다중 격추 2"));
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.LastBonus, Does.Not.Contain("다중 격추"));
    }

    [Test]
    public void ExpiredAttackGroupCannotFarmMultiKillAcrossTime()
    {
        AttackScoreContext attack = new AttackScoreContext(player, 0f);
        Kill(EnemyOf<Enemy>(), attack);
        score.Tick(1f, true, false);
        Kill(EnemyOf<Enemy>(), attack);
        Assert.That(score.LastBonus, Does.Not.Contain("다중 격추"));
    }

    [Test]
    public void SnapshotMovementIsUsedAtImpactInsteadOfCurrentPlayerVelocity()
    {
        AttackScoreContext shot = new AttackScoreContext(player, 1f);
        Set(player, "velocity", Vector3.zero);
        Kill(EnemyOf<Enemy>(), shot);
        Assert.That(score.TotalScore, Is.EqualTo(150));
        Assert.That(score.LastBonus, Does.Contain("기동"));
    }

    [Test]
    public void EnemyTypeAndBossPointsAreIndependentOfStyleCap()
    {
        settings.chainStepPoints = 0;
        Kill(EnemyOf<SpeedEnemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.TotalScore, Is.EqualTo(150));
        float before = score.StyleGauge;
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f), true);
        Assert.That(score.TotalScore, Is.EqualTo(1150));
        Assert.That(score.StyleGauge - before, Is.EqualTo(200f));
    }

    [Test]
    public void ShieldFrontIsNotActivityOrScoreAndRearHitGetsFlankBonus()
    {
        DefensiveEnemy enemy = EnemyOf<DefensiveEnemy>();
        EnemyDirectionalShield shield = Create("Shield", typeof(PolygonCollider2D), typeof(LineRenderer))
            .AddComponent<EnemyDirectionalShield>();
        shield.transform.SetParent(enemy.transform, false);
        Set(shield, "owner", enemy);
        Set(enemy, "shield", shield);
        AttackScoreContext shot = new AttackScoreContext(player, 0f);
        CombatDamage.Apply(enemy, 3, Vector2.right, Vector2.left, player.gameObject, shot);
        Assert.That(enemy.IsDead, Is.False);
        Assert.That(score.TotalScore, Is.Zero);
        CombatDamage.Apply(enemy, 3, Vector2.left, Vector2.right, player.gameObject, shot);
        score.ReportDefeat(enemy, false, player.gameObject);
        Assert.That(score.TotalScore, Is.EqualTo(250));
        Assert.That(score.LastBonus, Does.Contain("측후방 공략"));
    }

    [Test]
    public void KillCanOnlyBeCountedOnceAndMustBeFromBoundPlayer()
    {
        Enemy enemy = EnemyOf<Enemy>();
        AttackScoreContext context = new AttackScoreContext(player, 0f);
        Kill(enemy, context);
        score.ReportDefeat(enemy, false, player.gameObject);
        Assert.That(score.TotalScore, Is.EqualTo(100));
        Player other = Create("Other player").AddComponent<Player>();
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(other, 1f), false, other.gameObject);
        Assert.That(score.TotalScore, Is.EqualTo(100));
        Enemy untracked = Create("Untracked").AddComponent<Enemy>();
        Set(untracked, "currentHealth", 3);
        Set(untracked, "destroyOnDeath", false);
        Kill(untracked, context);
        Assert.That(score.TotalScore, Is.EqualTo(100));
    }

    [Test]
    public void HitDropsGaugeWithoutSubtractingEarnedScoreAndZeroDamageDoesNothing()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        player.TakeDamage(0, Vector2.zero, Vector2.zero, null);
        Assert.That(score.StyleGauge, Is.EqualTo(100f));
        player.TakeDamage(1, Vector2.zero, Vector2.zero, null);
        Assert.That(score.StyleGauge, Is.EqualTo(75f));
        Assert.That(score.TotalScore, Is.EqualTo(100));
        Assert.That(score.HighestRank, Is.EqualTo(CombatStyleRank.DPlus));
    }

    [Test]
    public void IdleDecayWaitsThreeSecondsAndStopsDuringPauseOrNoTargets()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        score.Tick(20f, false, false);
        score.Tick(0f, true, false);
        Assert.That(score.StyleGauge, Is.EqualTo(100f));
        score.Tick(3f, true, false);
        Assert.That(score.StyleGauge, Is.EqualTo(100f));
        score.Tick(1f, true, false);
        Assert.That(score.StyleGauge, Is.EqualTo(60f));
    }

    [Test]
    public void RepeatedEmptyChargesCannotRefreshGraceIndefinitely()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        for (int i = 0; i < 6; i++)
        { score.Tick(0.5f, true, true); score.Tick(0.5f, true, false); }
        Assert.That(score.StyleGauge, Is.EqualTo(60f)); // only 2s grace + 3s delay
    }

    [Test]
    public void ActualNonLethalDamageMaintainsFlowButEmptyFireDoesNot()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Enemy target = EnemyOf<Enemy>();
        score.Tick(2f, true, false);
        CombatDamage.Apply(target, 1, Vector2.zero, Vector2.right, player.gameObject,
            new AttackScoreContext(player, 0f));
        score.Tick(2f, true, false);
        Assert.That(score.StyleGauge, Is.EqualTo(100f));
        score.Tick(2f, true, false);
        Assert.That(score.StyleGauge, Is.EqualTo(60f));
    }

    [Test]
    public void ChargeGraceAlsoProtectsAnAlreadyDecayingGaugeWithoutRestoringIt()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        score.Tick(4f, true, false);
        Assert.That(score.StyleGauge, Is.EqualTo(60f));
        score.Tick(1f, true, true);
        Assert.That(score.StyleGauge, Is.EqualTo(60f));
        score.Tick(2f, true, true);
        Assert.That(score.StyleGauge, Is.EqualTo(20f));
    }

    [Test]
    public void ChainExpiresAndTheSameBonusRecoversAfterFiveSeconds()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.TotalScore, Is.EqualTo(352));
        score.Tick(6f, true, false);
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.ChainCount, Is.EqualTo(1));
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        Assert.That(score.TotalScore, Is.EqualTo(572));
    }

    [Test]
    public void RepeatedCompoundBonusesDoNotRecoverEachOtherWithinOneKill()
    {
        var attack = new AttackScoreContext(player, 0f);
        Kill(EnemyOf<Enemy>(), attack);
        Kill(EnemyOf<Enemy>(), attack); // 100 + 20 + 50
        Kill(EnemyOf<Enemy>(), attack); // 100 + 40*.8 + 50*.8
        Assert.That(score.TotalScore, Is.EqualTo(442));
    }

    [Test]
    public void EndRunFreezesScoreAndGameOverReadsTheFinalTotal()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        score.EndRun();
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 0f));
        score.Tick(100f, true, false);
        Assert.That(score.TotalScore, Is.EqualTo(100));
        Assert.That(score.StyleGauge, Is.EqualTo(100f));
        GameOverController ui = Create("GameOver").AddComponent<GameOverController>();
        CanvasGroup panel = Create("Panel", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>();
        Set(ui, "panel", panel);
        ui.SetScoreProvider(score);
        ui.Show();
        Assert.That(ui.DisplayedScore, Is.EqualTo(100));
        Call(ui, "OnDisable");
    }

    [Test]
    public void ProjectileKeepsAttackIdAndReconfigurationClearsIt()
    {
        Projectile projectile = Create("Shot", typeof(BoxCollider2D)).AddComponent<Projectile>();
        var context = new AttackScoreContext(player, 1f);
        projectile.SetScoreContext(context);
        Assert.That(projectile.ScoreContext.Id, Is.EqualTo(context.Id));
        Assert.That(projectile.ScoreContext.MovementRatio, Is.EqualTo(1f));
        projectile.SetOwner(player.gameObject);
        Assert.That(projectile.ScoreContext.Id, Is.Zero);
    }

    [Test]
    public void StartNewRunResetsPointsRanksCombosAndScopes()
    {
        Kill(EnemyOf<Enemy>(), new AttackScoreContext(player, 1f));
        score.EndRun();
        score.BeginRun(player);
        Assert.That(score.TotalScore, Is.Zero);
        Assert.That(score.StyleGauge, Is.Zero);
        Assert.That(score.ChainCount, Is.Zero);
        Assert.That(score.HighestRank, Is.EqualTo(CombatStyleRank.None));
    }

    [Test]
    public void SavedBattleSceneConnectsScoreSettingsAndResultProvider()
    {
        string scene = File.ReadAllText("Assets/Scenes/GamePlayScene.unity");
        StringAssert.Contains("scoreManager: {fileID: 2200000005}", scene);
        StringAssert.Contains("scoreProvider: {fileID: 2200000005}", scene);
        StringAssert.Contains("guid: 535c2772d80f48f0aa8a0570f4e07a39", scene);
    }
}
