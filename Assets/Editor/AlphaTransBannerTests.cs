using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class AlphaTransBannerTests
{
    private readonly List<GameObject> objects = new();
    private readonly List<Object> assets = new();
    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static T Get<T>(object target, string field) => (T)target.GetType()
        .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static void Call(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);

    [TearDown]
    public void Cleanup()
    {
        foreach (GameObject item in objects) if (item != null) Object.DestroyImmediate(item);
        foreach (Object item in assets) if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
        assets.Clear();
    }
    private Sprite Sprite(string name)
    {
        Texture2D texture = new(4, 4);
        UnityEngine.Sprite sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
        sprite.name = name;
        assets.Add(sprite);
        assets.Add(texture);
        return sprite;
    }
    private AlphaTransBanner Create(params Sprite[] sprites)
    {
        GameObject root = new("Alpha Banner Test", typeof(RectTransform));
        objects.Add(root);
        root.SetActive(false);
        AlphaTransBanner banner = root.AddComponent<AlphaTransBanner>();
        Set(banner, "bannerSprites", new List<Sprite>(sprites));
        Set(banner, "autoAdvance", false);
        Set(banner, "fadeDuration", 0.5f);
        Call(banner, "Awake");
        root.SetActive(true);
        Call(banner, "OnEnable");
        return banner;
    }

    [Test]
    public void CrossfadeChangesBothAlphasAndCommitsFocusOnlyAtCompletion()
    {
        Sprite first = Sprite("A"), second = Sprite("B");
        AlphaTransBanner banner = Create(first, second);
        Image outgoing = Get<Image>(banner, "currentImage");
        Image incoming = Get<Image>(banner, "incomingImage");
        int notifications = 0;
        AlphaBannerFocusEvent focused = new();
        focused.AddListener((index, sprite) => { notifications++; Assert.That(index, Is.EqualTo(1)); Assert.That(sprite, Is.SameAs(second)); });
        Set(banner, "onBannerFocused", focused);
        banner.Next();
        Assert.That(banner.IsTransitioning, Is.True);
        Assert.That(banner.CurrentSprite, Is.SameAs(first));
        Assert.That(incoming.sprite, Is.SameAs(second));
        Assert.That(incoming.color.a, Is.Zero);
        Assert.That(outgoing.color.a, Is.EqualTo(1f));
        banner.Advance(0.25f);
        Assert.That(incoming.color.a, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(outgoing.color.a, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(notifications, Is.Zero);
        banner.Advance(0.25f);
        Assert.That(banner.CurrentIndex, Is.EqualTo(1));
        Assert.That(banner.CurrentSprite, Is.SameAs(second));
        Assert.That(banner.IsTransitioning, Is.False);
        Assert.That(incoming.color.a, Is.EqualTo(1f));
        Assert.That(outgoing.color.a, Is.Zero);
        Assert.That(outgoing.enabled, Is.False);
        Assert.That(notifications, Is.EqualTo(1));
    }

    [Test]
    public void TwoBannersLoopWithoutCreatingMoreImagesAndNextLayerStaysAboveOutgoing()
    {
        Sprite first = Sprite("A"), second = Sprite("B");
        AlphaTransBanner banner = Create(first, second);
        for (int i = 0; i < 6; i++)
        {
            banner.Next();
            Image current = Get<Image>(banner, "currentImage");
            Image incoming = Get<Image>(banner, "incomingImage");
            Assert.That(incoming.transform.GetSiblingIndex(), Is.GreaterThan(current.transform.GetSiblingIndex()));
            banner.Advance(0.5f);
            Assert.That(banner.CurrentIndex, Is.EqualTo((i + 1) % 2));
        }
        Assert.That(banner.GetComponentsInChildren<Image>(true), Has.Length.EqualTo(2));
        Assert.That(banner.CurrentSprite, Is.SameAs(first));
    }

    [Test]
    public void AutomaticAdvanceWaitsFiveSecondsAfterCompletedFade()
    {
        AlphaTransBanner banner = Create(Sprite("A"), Sprite("B"));
        banner.SetAutoAdvance(true);
        banner.Advance(4.9f);
        Assert.That(banner.IsTransitioning, Is.False);
        banner.Advance(0.11f);
        Assert.That(banner.IsTransitioning, Is.True);
        banner.Advance(0.5f);
        Assert.That(banner.CurrentIndex, Is.EqualTo(1));
        banner.Advance(4.9f);
        Assert.That(banner.IsTransitioning, Is.False);
        banner.Advance(0.11f);
        Assert.That(banner.IsTransitioning, Is.True);
        banner.Advance(0.5f);
        banner.SetAutoAdvance(false);
        banner.Advance(100f);
        Assert.That(banner.CurrentIndex, Is.Zero);
        Assert.That(banner.IsTransitioning, Is.False);
    }

    [Test]
    public void EmptySingleAndNullEntriesDoNotProduceBlankTransitions()
    {
        AlphaTransBanner banner = Create();
        Assert.That(banner.CurrentIndex, Is.EqualTo(-1));
        foreach (Image image in banner.GetComponentsInChildren<Image>(true)) Assert.That(image.enabled, Is.False);
        Sprite first = Sprite("A"), second = Sprite("B");
        banner.SetSprites(new[] { first });
        banner.SetAutoAdvance(true);
        banner.Advance(100f);
        banner.Next();
        Assert.That(banner.CurrentIndex, Is.Zero);
        Assert.That(banner.IsTransitioning, Is.False);
        banner.SetSprites(new[] { null, first, null, second, null });
        Assert.That(banner.CurrentIndex, Is.EqualTo(1));
        banner.Next();
        banner.Advance(0.5f);
        Assert.That(banner.CurrentIndex, Is.EqualTo(3));
        banner.Previous();
        banner.Advance(0.5f);
        Assert.That(banner.CurrentIndex, Is.EqualTo(1));
    }

    [Test]
    public void NonLoopingModeStopsAtEdgesAndImmediatePageCancelsFadeCleanly()
    {
        AlphaTransBanner banner = Create(Sprite("A"), Sprite("B"), Sprite("C"));
        Set(banner, "loop", false);
        banner.Previous();
        Assert.That(banner.IsTransitioning, Is.False);
        banner.Next();
        banner.Next(); // Repeated commands during a transition are ignored.
        Assert.That(Get<int>(banner, "pendingIndex"), Is.EqualTo(1));
        banner.SetPage(2, false);
        Assert.That(banner.IsTransitioning, Is.False);
        Assert.That(banner.CurrentIndex, Is.EqualTo(2));
        Assert.That(Get<Image>(banner, "currentImage").color.a, Is.EqualTo(1f));
        Assert.That(Get<Image>(banner, "incomingImage").enabled, Is.False);
        banner.Next();
        Assert.That(banner.IsTransitioning, Is.False);
    }

    [Test]
    public void DisableMidFadeResolvesDestinationAndReenableKeepsIt()
    {
        AlphaTransBanner banner = Create(Sprite("A"), Sprite("B"));
        banner.Next();
        banner.Advance(0.2f);
        Call(banner, "OnDisable");
        Assert.That(banner.IsTransitioning, Is.False);
        Assert.That(banner.CurrentIndex, Is.EqualTo(1));
        Assert.That(Get<Image>(banner, "currentImage").color.a, Is.EqualTo(1f));
        Call(banner, "OnEnable");
        Assert.That(banner.CurrentIndex, Is.EqualTo(1));
        Assert.That(banner.GetComponentsInChildren<Image>(true), Has.Length.EqualTo(2));
    }

    [Test]
    public void ZeroDurationSwitchesImmediatelyAndValidationDefersGraphicChanges()
    {
        AlphaTransBanner banner = Create(Sprite("A"), Sprite("B"));
        Set(banner, "fadeDuration", 0f);
        banner.Next();
        Assert.That(banner.CurrentIndex, Is.EqualTo(1));
        Assert.That(banner.IsTransitioning, Is.False);
        Image image = Get<Image>(banner, "currentImage");
        Sprite original = image.sprite;
        banner.SetAutoAdvance(false);
        Set(banner, "bannerSprites", new List<Sprite>());
        Call(banner, "OnValidate");
        Assert.That(image.sprite, Is.SameAs(original));
        banner.Advance(0f);
        Assert.That(banner.CurrentIndex, Is.EqualTo(-1));
        Assert.That(image.enabled, Is.False);
    }

    [Test]
    public void GeneratedLayersSupportMasksAndDoNotHandleDragOrBlockRaycasts()
    {
        AlphaTransBanner banner = Create(Sprite("A"));
        Assert.That(typeof(IDragHandler).IsAssignableFrom(typeof(AlphaTransBanner)), Is.False);
        Assert.That(typeof(IBeginDragHandler).IsAssignableFrom(typeof(AlphaTransBanner)), Is.False);
        foreach (Image image in banner.GetComponentsInChildren<Image>(true))
        {
            Assert.That(image.maskable, Is.True);
            Assert.That(image.raycastTarget, Is.False);
            Assert.That(image.GetComponent<CanvasRenderer>(), Is.Not.Null);
            Assert.That(image.GetComponent<LayoutElement>().ignoreLayout, Is.True);
        }
    }
}
