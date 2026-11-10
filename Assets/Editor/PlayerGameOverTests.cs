using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerGameOverTests
{
    private readonly List<GameObject> objects = new();
    private float previousTimeScale;

    [SetUp]
    public void Setup() => previousTimeScale = Time.timeScale;

    [TearDown]
    public void Cleanup()
    {
        foreach (GameObject instance in objects)
            if (instance != null)
                Object.DestroyImmediate(instance);
        objects.Clear();
        Time.timeScale = previousTimeScale;
    }

    private GameObject Create(string name, params System.Type[] components)
    {
        GameObject result = new GameObject(name, components);
        objects.Add(result);
        return result;
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Call(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    private Player CreatePlayer(int health = 10)
    {
        Player player = Create("Test Player").AddComponent<Player>();
        Set(player, "maxHealth", health);
        Call(player, "Awake");
        return player;
    }

    private GameOverController CreateUI()
    {
        GameOverController controller = Create("Game Over Controller").AddComponent<GameOverController>();
        CanvasGroup panel = Create("Panel", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>();
        TMP_Text title = Create("Title", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        TMP_Text score = Create("Score", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        Button button = Create("Return", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
        title.transform.SetParent(panel.transform, false);
        score.transform.SetParent(panel.transform, false);
        button.transform.SetParent(panel.transform, false);
        Set(controller, "panel", panel);
        Set(controller, "gameOverText", title);
        Set(controller, "scoreText", score);
        Set(controller, "returnToLobbyButton", button);
        Set(controller, "fadeDuration", 0f);
        Call(controller, "Awake");
        return controller;
    }

    [Test]
    public void HealthInitializesAndRejectsNonPositiveDamage()
    {
        Player player = CreatePlayer(8);
        int changes = 0;
        player.HealthChanged += (_, _) => changes++;
        player.TakeDamage(0, Vector2.zero, Vector2.zero, null);
        player.TakeDamage(-5, Vector2.zero, Vector2.zero, null);
        Assert.That(player.CurrentHealth, Is.EqualTo(8));
        Assert.That(changes, Is.Zero);
        player.TakeDamage(3, Vector2.zero, Vector2.zero, null);
        Assert.That(player.CurrentHealth, Is.EqualTo(5));
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(player.IsDead, Is.False);
        Assert.That(player.enabled, Is.True);
    }

    [Test]
    public void LethalHitClampsToZeroAndNotifiesDeathExactlyOnceWithoutRemovingCamera()
    {
        Player player = CreatePlayer(2);
        GameObject cameraObject = Create("Camera", typeof(Camera));
        cameraObject.transform.SetParent(player.transform, false);
        PlayerWeaponController weapons = player.gameObject.AddComponent<PlayerWeaponController>();
        Set(weapons, "owner", player);
        int deaths = 0;
        player.Died += dead => { Assert.That(dead, Is.SameAs(player)); deaths++; };
        player.TakeDamage(int.MaxValue, Vector2.zero, Vector2.right, null);
        player.TakeDamage(1, Vector2.zero, Vector2.right, null);
        Assert.That(player.IsDead, Is.True);
        Assert.That(player.CurrentHealth, Is.Zero);
        Assert.That(deaths, Is.EqualTo(1));
        Assert.That(player.enabled, Is.False);
        Assert.That(weapons.enabled, Is.False);
        Assert.That(player.gameObject.activeSelf, Is.True);
        Assert.That(cameraObject.activeInHierarchy, Is.True);
    }

    [Test]
    public void ExistingProjectileDamageResolvesPlayerThroughChildCollider()
    {
        Player player = CreatePlayer();
        GameObject hit = Create("Hitbox", typeof(BoxCollider2D));
        hit.transform.SetParent(player.transform, false);
        EnemyProjectile projectile = Create("Bullet").AddComponent<EnemyProjectile>();
        projectile.SetDamage(3);
        typeof(Projectile).GetMethod("ApplyDamage", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(projectile, new object[] { hit.GetComponent<Collider2D>() });
        Assert.That(player.CurrentHealth, Is.EqualTo(7));
    }

    [Test]
    public void GameOverShowsResultsOnceAndRestoresOwnedPauseOnCleanup()
    {
        Time.timeScale = 0.75f;
        GameOverController controller = CreateUI();
        controller.SetScore(12345);
        controller.Show();
        controller.Show();
        Assert.That(controller.IsShowing, Is.True);
        Assert.That(controller.DisplayedScore, Is.EqualTo(12345));
        Assert.That(Time.timeScale, Is.Zero);
        CanvasGroup panel = (CanvasGroup)typeof(GameOverController)
            .GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        Assert.That(panel.gameObject.activeSelf, Is.True);
        Assert.That(panel.alpha, Is.EqualTo(1f));
        Assert.That(panel.blocksRaycasts, Is.True);
        Assert.That(panel.GetComponentsInChildren<TMP_Text>()[0].text, Is.EqualTo("GAME OVER"));
        Assert.That(panel.GetComponentInChildren<Button>().interactable, Is.True);
        controller.SetScore(77);
        Assert.That(controller.DisplayedScore, Is.EqualTo(77));
        Call(controller, "OnDisable");
        Assert.That(Time.timeScale, Is.EqualTo(0.75f));
        Assert.That(panel.gameObject.activeSelf, Is.False);
        Call(controller, "OnDisable");
        Assert.That(Time.timeScale, Is.EqualTo(0.75f));
    }

    private sealed class ScoreProvider : IGameOverScoreProvider
    {
        public int Calls;
        public long GetScore() { Calls++; return 9876543210L; }
    }

    [Test]
    public void ScoreProviderIsSampledOnceAndNoProviderDefaultsToZero()
    {
        GameOverController controller = CreateUI();
        ScoreProvider provider = new ScoreProvider();
        controller.SetScoreProvider(provider);
        controller.Show();
        controller.Show();
        Assert.That(controller.DisplayedScore, Is.EqualTo(9876543210L));
        Assert.That(provider.Calls, Is.EqualTo(1));
        Call(controller, "OnDisable");
        GameOverController noScore = CreateUI();
        noScore.Show();
        Assert.That(noScore.DisplayedScore, Is.Zero);
    }

    [Test]
    public void ActivePlayerDeathFailsMissionAndCannotCompleteBossProgression()
    {
        Player player = CreatePlayer();
        GameOverController ui = CreateUI();
        MissionRuntimeController mission = Create("Mission").AddComponent<MissionRuntimeController>();
        Set(mission, "player", player);
        Set(mission, "gameOverController", ui);
        Set(mission, "<State>k__BackingField", MissionRuntimeState.FightingBoss);
        Call(mission, "OnEnable");
        int failures = 0;
        int completions = 0;
        mission.MissionFailed += _ => failures++;
        mission.MissionCompleted += _ => completions++;
        player.TakeDamage(10, Vector2.zero, Vector2.zero, null);
        Call(mission, "HandleBossDefeated");
        Assert.That(mission.State, Is.EqualTo(MissionRuntimeState.Failed));
        Assert.That(failures, Is.EqualTo(1));
        Assert.That(completions, Is.Zero);
        Assert.That(ui.IsShowing, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
        Call(mission, "OnDisable");
    }

    [Test]
    public void BattleSceneHasGameOverControllerAndKoreanFontAssigned()
    {
        string scene = File.ReadAllText("Assets/Scenes/GamePlayScene.unity");
        StringAssert.Contains("gameOverController: {fileID: 2200000004}", scene);
        StringAssert.Contains("guid: 25ef334b71024c57b7c8908ec3488e79", scene);
        StringAssert.Contains("defaultUIFont: {fileID: 11400000, guid: 8ea7ad17998e0274885bf79062d2f29c", scene);
    }
}
