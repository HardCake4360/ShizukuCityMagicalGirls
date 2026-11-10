using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerHealthBarTests
{
    private readonly List<GameObject> created = new();
    private Player player;
    private PlayerHealthBar bar;
    private RectTransform mask;
    private RectTransform health;
    private RectTransform delay;
    private float previousTimeScale;

    private GameObject Create(string name, params System.Type[] components)
    {
        GameObject result = new GameObject(name, components);
        created.Add(result);
        return result;
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Call(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    private RectTransform Child(string name)
    {
        RectTransform child = Create(name, typeof(RectTransform)).GetComponent<RectTransform>();
        child.SetParent(mask, false);
        child.anchorMin = Vector2.zero;
        child.anchorMax = Vector2.one;
        child.offsetMin = child.offsetMax = Vector2.zero;
        return child;
    }

    [SetUp]
    public void Setup()
    {
        previousTimeScale = Time.timeScale;
        player = Create("Player").AddComponent<Player>();
        Call(player, "Awake");
        mask = Create("healthBarMask", typeof(RectTransform)).GetComponent<RectTransform>();
        mask.pivot = Vector2.zero;
        mask.sizeDelta = new Vector2(200f, 40f);
        health = Child("health");
        delay = Child("healthDelay");
        bar = mask.gameObject.AddComponent<PlayerHealthBar>();
        Set(bar, "holdDuration", 1f);
        Set(bar, "catchUpDuration", 2f);
        Set(bar, "catchUpCurve", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        bar.BindPlayer(player);
    }

    [TearDown]
    public void Cleanup()
    {
        if (bar != null)
            Call(bar, "OnDisable");
        foreach (GameObject instance in created)
            if (instance != null)
                Object.DestroyImmediate(instance);
        created.Clear();
        Time.timeScale = previousTimeScale;
    }

    private static Vector2 Edges(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return new Vector2(rect.parent.InverseTransformPoint(corners[0]).x,
            rect.parent.InverseTransformPoint(corners[3]).x);
    }

    private static void AssertEdges(RectTransform rect, float left, float right)
    {
        Vector2 edges = Edges(rect);
        Assert.That(edges.x, Is.EqualTo(left).Within(0.001f));
        Assert.That(edges.y, Is.EqualTo(right).Within(0.001f));
    }

    private void Damage(int amount) => player.TakeDamage(amount, Vector2.zero, Vector2.zero, null);

    [Test]
    public void FullHealthShowsOnlyHealthAndDamageInstantlyExtendsDelayToTheLeft()
    {
        AssertEdges(health, 0f, 200f);
        AssertEdges(delay, 200f, 200f);
        Damage(3);
        AssertEdges(health, 0f, 140f);
        AssertEdges(delay, 140f, 200f);
        Assert.That(health.rect.height, Is.EqualTo(40f));
        Assert.That(delay.rect.height, Is.EqualTo(40f));
    }

    [Test]
    public void RightEdgeHoldsThenShrinksUntilDelayHasZeroWidth()
    {
        Damage(3);
        bar.Advance(1f);
        AssertEdges(delay, 140f, 200f);
        bar.Advance(1f);
        AssertEdges(delay, 140f, 170f);
        bar.Advance(1f);
        AssertEdges(delay, 140f, 140f);
        Assert.That(bar.IsAnimating, Is.False);
        AssertEdges(health, 0f, 140f);
    }

    [Test]
    public void HitDuringHoldExtendsLeftEdgeAgainAndRestartsEntireHold()
    {
        Damage(2);
        bar.Advance(0.75f);
        Damage(3);
        AssertEdges(health, 0f, 100f);
        AssertEdges(delay, 100f, 200f);
        bar.Advance(0.75f);
        AssertEdges(delay, 100f, 200f);
        bar.Advance(0.25f);
        AssertEdges(delay, 100f, 200f);
        bar.Advance(1f);
        AssertEdges(delay, 100f, 150f);
    }

    [Test]
    public void HitDuringCatchUpPreservesCurrentRightEdgeAndRestartsHold()
    {
        Damage(2);
        bar.Advance(2f);
        AssertEdges(delay, 160f, 180f);
        Damage(3);
        AssertEdges(delay, 100f, 180f);
        bar.Advance(1f);
        AssertEdges(delay, 100f, 180f);
        bar.Advance(1f);
        AssertEdges(delay, 100f, 140f);
    }

    [Test]
    public void LargeDeltaConsumesHoldAndCatchUpWithoutExtraFrameOfDelay()
    {
        Damage(5);
        bar.Advance(3f);
        AssertEdges(delay, 100f, 100f);
    }

    [Test]
    public void LethalDamageShowsZeroHealthAndDelayCanFinishWhileCombatIsPaused()
    {
        Damage(10);
        Assert.That(player.IsDead, Is.True);
        AssertEdges(health, 0f, 0f);
        AssertEdges(delay, 0f, 200f);
        Time.timeScale = 0f;
        bar.Advance(3f);
        AssertEdges(delay, 0f, 0f);
    }

    [Test]
    public void RebindingAnotherCharacterRestoresOriginalFullSpanAndUnsubscribesOldPlayer()
    {
        Damage(5);
        Player other = Create("Selected Player").AddComponent<Player>();
        Set(other, "maxHealth", 20);
        Call(other, "Awake");
        bar.BindPlayer(other);
        Assert.That(bar.BoundPlayer, Is.SameAs(other));
        AssertEdges(health, 0f, 200f);
        AssertEdges(delay, 200f, 200f);
        Damage(1);
        AssertEdges(health, 0f, 200f);
        other.TakeDamage(5, Vector2.zero, Vector2.zero, null);
        AssertEdges(health, 0f, 150f);
    }

    [Test]
    public void DisableUnsubscribesAndReenableSnapsToLatestHp()
    {
        Call(bar, "OnDisable");
        Damage(4);
        Assert.That(bar.HealthRatio, Is.EqualTo(1f));
        Call(bar, "OnEnable");
        AssertEdges(health, 0f, 120f);
        AssertEdges(delay, 120f, 120f);
    }

    [Test]
    public void StretchAnchorsFollowResizedParentWithoutRecapturingReducedWidth()
    {
        Damage(5);
        mask.sizeDelta = new Vector2(400f, 40f);
        AssertEdges(health, 0f, 200f);
        AssertEdges(delay, 200f, 400f);
    }

    [Test]
    public void AuthoredFixedCenteredSpanPreservesPositionAndVerticalOffsets()
    {
        // Recapture a fresh fixed-size bar, matching the authored battle scene layout.
        Call(bar, "OnDisable");
        Set(bar, "layoutCaptured", false);
        health.anchorMin = health.anchorMax = new Vector2(0.5f, 0.5f);
        health.pivot = new Vector2(0.5f, 0.5f);
        health.sizeDelta = new Vector2(200f, 30f);
        health.anchoredPosition = new Vector2(-700f, 400f);
        delay.anchorMin = delay.anchorMax = new Vector2(0.5f, 0.5f);
        delay.sizeDelta = new Vector2(200f, 30f);
        delay.anchoredPosition = new Vector2(-700f, 400f);
        float originalY = health.anchoredPosition.y;
        Vector2 full = Edges(health);
        bar.BindPlayer(player);
        Damage(3);
        float expectedRight = Mathf.Lerp(full.x, full.y, 0.7f);
        AssertEdges(health, full.x, expectedRight);
        AssertEdges(delay, expectedRight, full.y);
        Assert.That(health.anchoredPosition.y, Is.EqualTo(originalY));
        Assert.That(health.rect.height, Is.EqualTo(30f));
        Assert.That(delay.anchoredPosition.y, Is.EqualTo(originalY));
        Assert.That(delay.rect.height, Is.EqualTo(30f));
    }

    [Test]
    public void ZeroDurationsAndOvershootingCurveNeverInvertTheDelaySegment()
    {
        Set(bar, "holdDuration", 0f);
        Set(bar, "catchUpDuration", 0f);
        Damage(3);
        bar.Advance(0.1f);
        AssertEdges(delay, 140f, 140f);
        Set(bar, "catchUpDuration", 2f);
        Set(bar, "catchUpCurve", AnimationCurve.Constant(0f, 1f, 3f));
        Damage(2);
        bar.Advance(0.1f);
        Assert.That(bar.DelayedRightRatio, Is.EqualTo(bar.HealthRatio));
    }

    [Test]
    public void SavedBattleMaskUsesExistingChildrenAndMissionBindsTheSelectedPlayer()
    {
        string scene = File.ReadAllText("Assets/Scenes/GamePlayScene.unity");
        StringAssert.Contains("healthBar: {fileID: 1264004385}", scene);
        StringAssert.Contains("guid: c4f5abe09b214029a4decb9c5fb4d97b", scene);
        StringAssert.Contains("health: {fileID: 84432412}", scene);
        StringAssert.Contains("healthDelay: {fileID: 790856745}", scene);
        StringAssert.Contains("useUnscaledTime: 1", scene);
    }
}
