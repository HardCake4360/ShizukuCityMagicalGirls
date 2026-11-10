using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public sealed class StyleScoreDisplayTests
{
    private readonly List<Object> created = new();
    private CombatScoreSettings settings;
    private CombatScoreManager score;
    private StyleScoreDisplay display;
    private Image image;
    private Sprite[] sprites;
    private Sprite fallbackSprite;
    private readonly Vector3 baseScale = new(2f, 3f, 1f);
    private readonly Color baseColor = new(0.7f, 0.6f, 0.5f, 0.8f);

    private T Track<T>(T item) where T : Object { created.Add(item); return item; }
    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    [SetUp]
    public void Setup()
    {
        settings = Track(ScriptableObject.CreateInstance<CombatScoreSettings>());
        score = Track(new GameObject("Score")).AddComponent<CombatScoreManager>();
        Set(score, "settings", settings);
        Set(score, "generateDefaultHUD", false);
        Texture2D texture = Track(new Texture2D(2, 2));
        fallbackSprite = Track(Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.one * 0.5f));
        image = Track(new GameObject("StyleScore", typeof(RectTransform), typeof(Image))).GetComponent<Image>();
        image.color = baseColor;
        image.sprite = fallbackSprite;
        image.transform.localScale = baseScale;
        display = image.gameObject.AddComponent<StyleScoreDisplay>();
        string[] fields = { "dSprite", "dPlusSprite", "cSprite", "cPlusSprite", "bSprite", "bPlusSprite",
            "aSprite", "aPlusSprite", "sSprite", "sPlusSprite" };
        sprites = new Sprite[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            sprites[i] = Track(Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.one * 0.5f));
            Set(display, fields[i], sprites[i]);
        }
        display.BindScoreManager(score);
    }

    [TearDown]
    public void Cleanup()
    {
        if (display != null)
            Call(display, "OnDisable");
        if (score != null)
            Call(score, "OnDisable");
        // Destroy GameObjects before the sprites/settings they reference.
        foreach (Object item in created)
            if (item is GameObject && item != null)
                Object.DestroyImmediate(item);
        for (int i = created.Count - 1; i >= 0; i--)
            if (created[i] != null)
                Object.DestroyImmediate(created[i]);
        created.Clear();
    }

    private void SetGauge(float gauge, CombatScoreManager source = null)
    {
        source = source != null ? source : score;
        Set(source, "<StyleGauge>k__BackingField", gauge);
        Call(source, "NotifyChanged");
    }

    [Test]
    public void InitialUnrankedStateIsHiddenWithoutPulsingAndKeepsAuthoredScale()
    {
        Assert.That(display.DisplayedRank, Is.EqualTo(CombatStyleRank.None));
        Assert.That(image.sprite, Is.SameAs(fallbackSprite));
        Assert.That(image.color.a, Is.Zero);
        Assert.That(image.color.r, Is.EqualTo(baseColor.r));
        Assert.That(display.IsPulsing, Is.False);
        Assert.That(display.IsFading, Is.False);
        Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
    }

    [Test]
    public void EveryRankIncludingDUsesItsSpriteAtFullAuthoredAlpha()
    {
        for (int i = 0; i < CombatScoreSettings.RankCount; i++)
        {
            SetGauge(Mathf.Max(0.01f, settings.rankThresholds[i]));
            Assert.That(display.DisplayedRank, Is.EqualTo((CombatStyleRank)i));
            Assert.That(image.sprite, Is.SameAs(sprites[i]));
            Assert.That(image.color.a, Is.EqualTo(baseColor.a).Within(0.0001f));
            Assert.That(display.IsPulsing, Is.True);
            display.AdvancePulse(1f);
            Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
        }
    }

    [Test]
    public void GaugeTicksWithinSameRankDoNotRestartThePulse()
    {
        SetGauge(100f);
        display.AdvancePulse(0.075f);
        Assert.That(image.transform.localScale.x, Is.EqualTo(baseScale.x * 1.1f).Within(0.001f));
        SetGauge(110f);
        display.AdvancePulse(0.075f);
        Assert.That(image.transform.localScale.x, Is.EqualTo(baseScale.x * 1.2f).Within(0.001f));
        display.AdvancePulse(0.15f);
        Assert.That(display.IsPulsing, Is.False);
        Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
        SetGauge(120f);
        Assert.That(display.IsPulsing, Is.False);
    }

    [Test]
    public void RankDropToDStaysVisibleAndTriggersPulse()
    {
        SetGauge(100f);
        display.AdvancePulse(1f);
        SetGauge(99f);
        Assert.That(image.sprite, Is.SameAs(sprites[0]));
        Assert.That(image.color.a, Is.EqualTo(baseColor.a).Within(0.0001f));
        Assert.That(display.IsPulsing, Is.True);
        Assert.That(display.IsFading, Is.False);
    }

    [Test]
    public void BelowDKeepsLastSpriteAndFadesCompletelyWithoutPulse()
    {
        SetGauge(100f);
        display.AdvancePulse(0.15f);
        SetGauge(0f);
        Assert.That(display.DisplayedRank, Is.EqualTo(CombatStyleRank.None));
        Assert.That(image.sprite, Is.SameAs(sprites[1]));
        Assert.That(display.IsPulsing, Is.False);
        Assert.That(display.IsFading, Is.True);
        Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
        Assert.That(image.color.a, Is.EqualTo(baseColor.a));
        display.AdvanceVisibility(0.15f);
        Assert.That(image.color.a, Is.EqualTo(baseColor.a * 0.5f).Within(0.0001f));
        Assert.That(image.color.r, Is.EqualTo(baseColor.r));
        SetGauge(0f); // Another notification must not restart an ongoing fade.
        display.AdvanceVisibility(0.15f);
        Assert.That(image.color.a, Is.Zero);
        Assert.That(display.IsFading, Is.False);
        Assert.That(image.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void RecoveringDInterruptsFadeAndRestoresAuthoredAlpha()
    {
        SetGauge(99f);
        SetGauge(0f);
        display.AdvanceVisibility(0.1f);
        Assert.That(image.color.a, Is.LessThan(baseColor.a));
        SetGauge(1f);
        Assert.That(display.DisplayedRank, Is.EqualTo(CombatStyleRank.D));
        Assert.That(image.sprite, Is.SameAs(sprites[0]));
        Assert.That(image.color.a, Is.EqualTo(baseColor.a));
        Assert.That(display.IsFading, Is.False);
        Assert.That(display.IsPulsing, Is.True);
        display.AdvanceVisibility(1f);
        Assert.That(image.color.a, Is.EqualTo(baseColor.a));
    }

    [Test]
    public void ConfiguredDThresholdHidesLowerPositiveGaugeAndFadeSupportsZeroDuration()
    {
        settings.rankThresholds[0] = 50f;
        SetGauge(50f);
        Assert.That(display.DisplayedRank, Is.EqualTo(CombatStyleRank.D));
        SetGauge(49f);
        Assert.That(display.IsFading, Is.True);
        display.AdvanceVisibility(10f);
        Assert.That(image.color.a, Is.Zero);
        SetGauge(50f);
        Set(display, "fadeOutDuration", 0f);
        SetGauge(49f);
        Assert.That(image.color.a, Is.Zero);
        Assert.That(display.IsFading, Is.False);
    }

    [Test]
    public void RapidChangesRestartWithoutCompoundingAuthoredScale()
    {
        SetGauge(100f);
        display.AdvancePulse(0.15f);
        SetGauge(220f);
        Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
        display.AdvancePulse(0.15f);
        Assert.That(image.transform.localScale.x, Is.EqualTo(baseScale.x * 1.2f).Within(0.001f));
        display.AdvancePulse(100f);
        Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
    }

    [Test]
    public void ReenableSnapsToCurrentRankWithoutInitialPulseOrFade()
    {
        SetGauge(100f);
        display.AdvancePulse(0.15f);
        image.gameObject.SetActive(false);
        Call(display, "OnDisable");
        Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
        Assert.That(image.color, Is.EqualTo(baseColor));
        SetGauge(220f);
        image.gameObject.SetActive(true);
        Call(display, "OnEnable");
        Assert.That(display.DisplayedRank, Is.EqualTo(CombatStyleRank.C));
        Assert.That(display.IsPulsing, Is.False);
        SetGauge(0f);
        Call(display, "OnDisable");
        Call(display, "OnEnable");
        Assert.That(image.color.a, Is.Zero);
        Assert.That(display.IsFading, Is.False);
    }

    [Test]
    public void RebindingUnsubscribesPreviousSource()
    {
        CombatScoreManager other = Track(new GameObject("Other score")).AddComponent<CombatScoreManager>();
        Set(other, "settings", settings);
        Set(other, "generateDefaultHUD", false);
        display.BindScoreManager(other);
        SetGauge(1250f, score);
        Assert.That(display.DisplayedRank, Is.EqualTo(CombatStyleRank.None));
        SetGauge(1250f, other);
        Assert.That(display.DisplayedRank, Is.EqualTo(CombatStyleRank.SPlus));
        display.BindScoreManager(null);
        Assert.That(display.DisplayedRank, Is.EqualTo(CombatStyleRank.None));
        Assert.That(image.color.a, Is.Zero);
        Assert.That(display.IsPulsing, Is.False);
    }

    [Test]
    public void MissingRankSpriteUsesAuthoredFallbackRatherThanPreviousRank()
    {
        Set(display, "sPlusSprite", null);
        SetGauge(1100f);
        SetGauge(1250f);
        Assert.That(image.sprite, Is.SameAs(fallbackSprite));
    }

    [Test]
    public void ZeroDurationAndOvershootingCurveCannotLeaveWrongScale()
    {
        Set(display, "pulseDuration", 0f);
        SetGauge(100f);
        Assert.That(display.IsPulsing, Is.False);
        Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
        Set(display, "pulseDuration", 0.3f);
        Set(display, "pulseCurve", AnimationCurve.Linear(0f, 0f, 1f, 5f));
        SetGauge(220f);
        display.AdvancePulse(0.15f);
        Assert.That(image.transform.localScale.x, Is.EqualTo(baseScale.x * 1.2f).Within(0.001f));
        display.AdvancePulse(1f);
        Assert.That(image.transform.localScale, Is.EqualTo(baseScale));
    }

    [Test]
    public void SavedSceneBindsAuthoredStyleScoreToMissionScore()
    {
        string scene = File.ReadAllText("Assets/Scenes/GamePlayScene.unity");
        StringAssert.Contains("component: {fileID: 1748041204}", scene);
        StringAssert.Contains("guid: 65bdd2ad79a44cfb9e54f647edac5da2", scene);
        StringAssert.Contains("rankImage: {fileID: 1748041202}", scene);
        StringAssert.Contains("pulseTarget: {fileID: 1748041201}", scene);
        StringAssert.Contains("scoreManager: {fileID: 2200000005}", scene);
        StringAssert.Contains("fadeOutDuration: 0.3", scene);
        StringAssert.DoesNotContain("dimAlphaMultiplier:", scene);
    }
}
