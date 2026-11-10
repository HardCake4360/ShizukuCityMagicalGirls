using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[Serializable]
public sealed class AlphaBannerFocusEvent : UnityEvent<int, Sprite> { }

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class AlphaTransBanner : MonoBehaviour
{
    [Header("Banner List")]
    [SerializeField] private List<Sprite> bannerSprites = new();
    [SerializeField, Min(0)] private int startIndex;
    [SerializeField] private bool loop = true;

    [Header("Images (optional; unassigned images are created at runtime)")]
    [SerializeField] private RectTransform displayRoot;
    [SerializeField] private Image imageA;
    [SerializeField] private Image imageB;
    [SerializeField] private bool preserveAspect;

    [Header("Alpha Transition")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.5f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Auto Advance")]
    [SerializeField] private bool autoAdvance = true;
    [SerializeField, Min(0.1f)] private float autoAdvanceInterval = 5f;

    [Header("Events")]
    [Tooltip("전환이 끝나고 현재 배너가 확정되었을 때 호출됩니다. 인덱스는 원본 스프라이트 목록 기준입니다.")]
    [SerializeField] private AlphaBannerFocusEvent onBannerFocused;

    private Image currentImage;
    private Image incomingImage;
    private int currentIndex = -1;
    private int pendingIndex = -1;
    private float fadeElapsed;
    private float autoAdvanceElapsed;
    private bool initialized;
    private bool refreshPending;

    public int CurrentIndex => currentIndex;
    public Sprite CurrentSprite => GetSprite(currentIndex);
    public bool IsTransitioning { get; private set; }
    public bool AutoAdvance
    {
        get => autoAdvance;
        set { autoAdvance = value; autoAdvanceElapsed = 0f; }
    }

    private void Reset() => displayRoot = GetComponent<RectTransform>();
    private void Awake() => Initialize(startIndex, false);
    private void OnEnable()
    {
        if (!initialized) Initialize(startIndex, true);
        else Refresh();
    }
    private void OnDisable()
    {
        // Resolve a partial fade to its destination, never leave two half-transparent layers.
        if (IsTransitioning) CompleteTransition(false);
        autoAdvanceElapsed = 0f;
    }
    private void Update() => Advance(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

    /// <summary>Advance timers explicitly; also used by Update. No drag or pointer input is handled.</summary>
    public void Advance(float deltaTime)
    {
        if (!isActiveAndEnabled) return;
        if (refreshPending) Refresh();
        if (!initialized) Initialize(startIndex, false);
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        if (IsTransitioning)
        {
            fadeElapsed += deltaTime;
            float progress = fadeDuration > 0f ? Mathf.Clamp01(fadeElapsed / fadeDuration) : 1f;
            float alpha = Mathf.Clamp01(fadeCurve != null ? fadeCurve.Evaluate(progress) : progress);
            SetAlpha(currentImage, 1f - alpha);
            SetAlpha(incomingImage, alpha);
            if (progress >= 1f) CompleteTransition(true);
            return;
        }
        if (!autoAdvance || FindAdjacentIndex(1) < 0) return;
        autoAdvanceElapsed += deltaTime;
        if (autoAdvanceElapsed >= autoAdvanceInterval) Next();
    }

    public void Next() => Move(1);
    public void Previous() => Move(-1);
    private void Move(int direction)
    {
        if (IsTransitioning) return;
        EnsureInitialized();
        int index = FindAdjacentIndex(direction);
        if (index >= 0) SetPage(index, true);
    }

    public void SetPage(int index) => SetPage(index, true);
    public void SetPage(int index, bool animate)
    {
        EnsureInitialized();
        if (IsTransitioning && animate) return;
        index = FindDisplayIndex(index);
        if (index == currentIndex && !IsTransitioning) { autoAdvanceElapsed = 0f; return; }
        if (index < 0 || !animate || fadeDuration <= 0f || !isActiveAndEnabled
            || GetSprite(index) == CurrentSprite)
        {
            DisplayImmediately(index, true);
            return;
        }
        pendingIndex = index;
        fadeElapsed = 0f;
        autoAdvanceElapsed = 0f;
        IsTransitioning = true;
        incomingImage.sprite = GetSprite(index);
        incomingImage.enabled = true;
        if (incomingImage.transform.parent == currentImage.transform.parent)
        {
            int currentOrder = currentImage.transform.GetSiblingIndex();
            int incomingOrder = incomingImage.transform.GetSiblingIndex();
            incomingImage.transform.SetSiblingIndex(currentOrder + (incomingOrder > currentOrder ? 1 : 0));
        }
        SetAlpha(currentImage, 1f);
        SetAlpha(incomingImage, 0f);
    }

    public void SetAutoAdvance(bool enabled) => AutoAdvance = enabled;
    public void SetSprites(IEnumerable<Sprite> sprites, int initialIndex = 0)
    {
        bannerSprites = sprites != null ? new List<Sprite>(sprites) : new List<Sprite>();
        Initialize(initialIndex, true);
    }
    public void Refresh()
    {
        EnsureImages();
        initialized = true;
        DisplayImmediately(FindDisplayIndex(currentIndex < 0 ? startIndex : currentIndex), true);
    }
    private void Initialize(int index, bool notify)
    {
        EnsureImages();
        initialized = true;
        DisplayImmediately(FindDisplayIndex(index), notify);
    }
    private void EnsureInitialized() { if (!initialized) Initialize(startIndex, false); }

    private void DisplayImmediately(int index, bool notify)
    {
        IsTransitioning = false;
        refreshPending = false;
        pendingIndex = -1;
        currentIndex = index;
        fadeElapsed = 0f;
        autoAdvanceElapsed = 0f;
        currentImage.sprite = GetSprite(index);
        currentImage.enabled = currentImage.sprite != null;
        SetAlpha(currentImage, currentImage.enabled ? 1f : 0f);
        incomingImage.sprite = null;
        incomingImage.enabled = false;
        SetAlpha(incomingImage, 0f);
        if (notify) NotifyFocused();
    }

    private void CompleteTransition(bool notify)
    {
        int index = FindDisplayIndex(pendingIndex);
        (currentImage, incomingImage) = (incomingImage, currentImage);
        DisplayImmediately(index, notify);
    }
    private void NotifyFocused() => onBannerFocused?.Invoke(CurrentIndex, CurrentSprite);
    private Sprite GetSprite(int index) => bannerSprites != null && index >= 0 && index < bannerSprites.Count
        ? bannerSprites[index] : null;

    private int FindAdjacentIndex(int direction)
    {
        int count = bannerSprites?.Count ?? 0;
        if (currentIndex < 0 || count <= 1) return -1;
        for (int step = 1; step < count; step++)
        {
            int candidate = currentIndex + direction * step;
            if (!loop && (candidate < 0 || candidate >= count)) return -1;
            candidate = WrapIndex(candidate, count);
            if (GetSprite(candidate) != null) return candidate;
        }
        return -1;
    }

    private int FindDisplayIndex(int requested)
    {
        int count = bannerSprites?.Count ?? 0;
        if (count == 0) return -1;
        int index = loop ? WrapIndex(requested, count) : Mathf.Clamp(requested, 0, count - 1);
        if (GetSprite(index) != null) return index;
        // Null entries are skipped without changing original inspector/event indices.
        for (int step = 1; step < count; step++)
        {
            int candidate = index + step;
            if (candidate >= count)
            {
                if (!loop) break;
                candidate = WrapIndex(candidate, count);
            }
            if (GetSprite(candidate) != null) return candidate;
        }
        for (int candidate = index - 1; candidate >= 0; candidate--)
            if (GetSprite(candidate) != null) return candidate;
        return -1;
    }
    private static int WrapIndex(int index, int count) => (index % count + count) % count;

    private void EnsureImages()
    {
        if (displayRoot == null) displayRoot = GetComponent<RectTransform>();
        if (imageA == null) imageA = CreateImage("Alpha Banner A");
        if (imageB == null || imageB == imageA) imageB = CreateImage("Alpha Banner B");
        imageA.preserveAspect = imageB.preserveAspect = preserveAspect;
        imageA.raycastTarget = imageB.raycastTarget = false;
        if (currentImage == null || incomingImage == null
            || (currentImage != imageA && currentImage != imageB)
            || (incomingImage != imageA && incomingImage != imageB))
        {
            currentImage = imageA;
            incomingImage = imageB;
        }
    }
    private Image CreateImage(string objectName)
    {
        GameObject imageObject = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(displayRoot, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = imageObject.GetComponent<Image>();
        imageObject.GetComponent<LayoutElement>().ignoreLayout = true;
        image.raycastTarget = false;
        image.maskable = true;
        return image;
    }
    private static void SetAlpha(Image image, float alpha)
    {
        if (image == null) return;
        Color color = image.color;
        color.a = Mathf.Clamp01(alpha);
        image.color = color;
    }
    private void OnValidate()
    {
        startIndex = Mathf.Max(0, startIndex);
        fadeDuration = Mathf.Max(0f, fadeDuration);
        autoAdvanceInterval = Mathf.Max(0.1f, autoAdvanceInterval);
        // Never create objects/change graphics during OnValidate/CheckConsistency.
        refreshPending = true;
    }
}
