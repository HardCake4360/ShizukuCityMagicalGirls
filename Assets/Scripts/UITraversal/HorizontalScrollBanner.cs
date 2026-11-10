using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

[Serializable]
public sealed class HorizontalBannerFocusEvent : UnityEvent<int, Sprite>
{
}

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform), typeof(RectMask2D))]
public sealed class HorizontalScrollBanner : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [Header("Banner List")]
    [Tooltip("배너가 표시되는 순서입니다.")]
    [SerializeField] private List<Sprite> bannerSprites = new List<Sprite>();
    [SerializeField, Min(0)] private int startIndex;
    [Tooltip("켜면 마지막 배너 다음에 첫 배너가 이어집니다.")]
    [SerializeField] private bool loop = true;

    [Header("Viewport")]
    [Tooltip("비어 있으면 이 컴포넌트의 RectTransform을 마스크 영역으로 사용합니다.")]
    [SerializeField] private RectTransform viewport;
    [Tooltip("비어 있으면 실행 시 자동으로 생성합니다.")]
    [SerializeField] private RectTransform bannerContainer;
    [SerializeField] private bool preserveAspect;

    [Header("Drag And Snap")]
    [SerializeField, Range(0.01f, 0.99f)] private float pageChangeThreshold = 0.25f;
    [SerializeField, Min(0f)] private float snapDuration = 0.2f;
    [SerializeField] private AnimationCurve snapCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Auto Advance")]
    [SerializeField] private bool autoAdvance = true;
    [SerializeField, Min(0.1f)] private float autoAdvanceInterval = 5f;

    [Header("Events")]
    [Tooltip("배너가 한 칸에 완전히 멈추고 포커스가 확정될 때 호출됩니다.")]
    [SerializeField] private HorizontalBannerFocusEvent onBannerFocused;

    private Image previousBanner;
    private Image currentBanner;
    private Image nextBanner;
    private Coroutine snapCoroutine;
    private int currentIndex;
    private float autoAdvanceElapsed;
    private float dragStartPointerX;
    private float dragStartContainerX;
    private bool isInitialized;
    private bool isDragging;
    private bool isPointerHeld;
    private bool isTransitioning;

    public int CurrentIndex => bannerSprites.Count > 0 ? currentIndex : -1;
    public Sprite CurrentSprite => IsValidIndex(currentIndex)
        ? bannerSprites[currentIndex]
        : null;
    public bool IsDragging => isDragging;
    public bool IsTransitioning => isTransitioning;
    public bool AutoAdvance
    {
        get => autoAdvance;
        set
        {
            autoAdvance = value;
            ResetAutoAdvanceTimer();
        }
    }

    private void Reset()
    {
        viewport = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        ResolveViewport();
        EnsureBannerHierarchy();
        Initialize(startIndex, false);
    }

    private void OnEnable()
    {
        ResolveViewport();
        EnsureBannerHierarchy();

        if (!isInitialized)
        {
            Initialize(startIndex, true);
        }
        else
        {
            Refresh(true);
        }
    }

    private void Start()
    {
        // Layout systems may not have calculated the viewport size during Awake.
        Refresh(false);
    }

    private void OnDisable()
    {
        StopSnap();
        isDragging = false;
        isPointerHeld = false;
    }

    private void Update()
    {
        if (!autoAdvance
            || bannerSprites.Count == 0
            || isDragging
            || isPointerHeld
            || isTransitioning)
        {
            return;
        }

        autoAdvanceElapsed += useUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        if (autoAdvanceElapsed < autoAdvanceInterval)
        {
            return;
        }

        autoAdvanceElapsed = 0f;
        Next();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isInitialized || isDragging || isTransitioning)
        {
            return;
        }

        LayoutSlots();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPointerHeld = true;
        ResetAutoAdvanceTimer();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerHeld = false;
        ResetAutoAdvanceTimer();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isTransitioning || bannerSprites.Count == 0)
        {
            return;
        }

        if (!TryGetLocalPointerX(eventData, out dragStartPointerX))
        {
            return;
        }

        isDragging = true;
        dragStartContainerX = bannerContainer.anchoredPosition.x;
        ResetAutoAdvanceTimer();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || !TryGetLocalPointerX(eventData, out float pointerX))
        {
            return;
        }

        float width = PageWidth;
        float minOffset = CanMove(1) ? -width : 0f;
        float maxOffset = CanMove(-1) ? width : 0f;
        float offset = dragStartContainerX + pointerX - dragStartPointerX;
        SetContainerX(Mathf.Clamp(offset, minOffset, maxOffset));
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        float offset = bannerContainer.anchoredPosition.x;
        float threshold = PageWidth * pageChangeThreshold;

        if (offset <= -threshold && CanMove(1))
        {
            BeginSnap(1);
        }
        else if (offset >= threshold && CanMove(-1))
        {
            BeginSnap(-1);
        }
        else
        {
            BeginSnap(0);
        }
    }

    public void Next()
    {
        if (!isDragging && !isTransitioning && CanMove(1))
        {
            BeginSnap(1);
        }
    }

    public void Previous()
    {
        if (!isDragging && !isTransitioning && CanMove(-1))
        {
            BeginSnap(-1);
        }
    }

    public void SetPage(int index)
    {
        SetPage(index, false);
    }

    public void SetPage(int index, bool animate)
    {
        if (bannerSprites.Count == 0)
        {
            Refresh(false);
            return;
        }

        index = NormalizeOrClampIndex(index);
        if (index == currentIndex)
        {
            BeginSnap(0);
            return;
        }

        if (animate && index == GetAdjacentIndex(1))
        {
            Next();
            return;
        }

        if (animate && index == GetAdjacentIndex(-1))
        {
            Previous();
            return;
        }

        StopSnap();
        currentIndex = index;
        RefreshSlots();
        LayoutSlots();
        NotifyFocusedBanner();
    }

    public void Refresh()
    {
        Refresh(true);
    }

    public void SetAutoAdvance(bool enabled)
    {
        AutoAdvance = enabled;
    }

    private void Initialize(int index, bool notify)
    {
        currentIndex = bannerSprites.Count > 0
            ? NormalizeOrClampIndex(index)
            : 0;
        isInitialized = true;
        RefreshSlots();
        LayoutSlots();
        ResetAutoAdvanceTimer();

        if (notify)
        {
            NotifyFocusedBanner();
        }
    }

    private void Refresh(bool notify)
    {
        StopSnap();
        currentIndex = bannerSprites.Count > 0
            ? NormalizeOrClampIndex(currentIndex)
            : 0;
        RefreshSlots();
        LayoutSlots();
        ResetAutoAdvanceTimer();

        if (notify)
        {
            NotifyFocusedBanner();
        }
    }

    private void BeginSnap(int direction)
    {
        StopSnap();
        float targetX = -direction * PageWidth;

        if (snapDuration <= 0f || !isActiveAndEnabled)
        {
            SetContainerX(targetX);
            CompleteSnap(direction);
            return;
        }

        snapCoroutine = StartCoroutine(AnimateSnap(direction, targetX));
    }

    private IEnumerator AnimateSnap(int direction, float targetX)
    {
        isTransitioning = true;
        float startX = bannerContainer.anchoredPosition.x;
        float elapsed = 0f;

        while (elapsed < snapDuration)
        {
            elapsed += useUnscaledTime
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / snapDuration);
            float curvedProgress = snapCurve != null
                ? snapCurve.Evaluate(progress)
                : progress;
            SetContainerX(Mathf.LerpUnclamped(startX, targetX, curvedProgress));
            yield return null;
        }

        SetContainerX(targetX);
        snapCoroutine = null;
        isTransitioning = false;
        CompleteSnap(direction);
    }

    private void CompleteSnap(int direction)
    {
        if (direction == 1)
        {
            RecycleAfterMovingNext();
        }
        else if (direction == -1)
        {
            RecycleAfterMovingPrevious();
        }
        else
        {
            SetContainerX(0f);
        }

        ResetAutoAdvanceTimer();
        if (direction != 0)
        {
            NotifyFocusedBanner();
        }
    }

    private void RecycleAfterMovingNext()
    {
        currentIndex = GetAdjacentIndex(1);

        Image recycled = previousBanner;
        previousBanner = currentBanner;
        currentBanner = nextBanner;
        nextBanner = recycled;

        PositionSlot(previousBanner, -PageWidth);
        PositionSlot(currentBanner, 0f);
        PositionSlot(nextBanner, PageWidth);
        SetContainerX(0f);

        // 기존 이전/현재 이미지는 그대로 사용하고 새 다음 칸만 교체합니다.
        ApplySlotSprite(nextBanner, GetAdjacentIndex(1));
        ApplyEdgeVisibility();
    }

    private void RecycleAfterMovingPrevious()
    {
        currentIndex = GetAdjacentIndex(-1);

        Image recycled = nextBanner;
        nextBanner = currentBanner;
        currentBanner = previousBanner;
        previousBanner = recycled;

        PositionSlot(previousBanner, -PageWidth);
        PositionSlot(currentBanner, 0f);
        PositionSlot(nextBanner, PageWidth);
        SetContainerX(0f);

        // 기존 현재/다음 이미지는 그대로 사용하고 새 이전 칸만 교체합니다.
        ApplySlotSprite(previousBanner, GetAdjacentIndex(-1));
        ApplyEdgeVisibility();
    }

    private void RefreshSlots()
    {
        if (bannerSprites.Count == 0)
        {
            ApplySlotSprite(previousBanner, -1);
            ApplySlotSprite(currentBanner, -1);
            ApplySlotSprite(nextBanner, -1);
            return;
        }

        ApplySlotSprite(previousBanner, GetAdjacentIndex(-1));
        ApplySlotSprite(currentBanner, currentIndex);
        ApplySlotSprite(nextBanner, GetAdjacentIndex(1));
        ApplyEdgeVisibility();
    }

    private void ApplyEdgeVisibility()
    {
        SetSlotVisible(currentBanner, bannerSprites.Count > 0);
        SetSlotVisible(previousBanner, CanMove(-1));
        SetSlotVisible(nextBanner, CanMove(1));
    }

    private void LayoutSlots()
    {
        if (bannerContainer == null)
        {
            return;
        }

        float width = PageWidth;
        float height = PageHeight;
        bannerContainer.anchorMin = new Vector2(0.5f, 0.5f);
        bannerContainer.anchorMax = new Vector2(0.5f, 0.5f);
        bannerContainer.pivot = new Vector2(0.5f, 0.5f);
        bannerContainer.sizeDelta = new Vector2(width * 3f, height);
        bannerContainer.anchoredPosition = Vector2.zero;

        ConfigureSlot(previousBanner, new Vector2(width, height), -width);
        ConfigureSlot(currentBanner, new Vector2(width, height), 0f);
        ConfigureSlot(nextBanner, new Vector2(width, height), width);
    }

    private void ConfigureSlot(Image slot, Vector2 size, float x)
    {
        if (slot == null)
        {
            return;
        }

        RectTransform rect = slot.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.localScale = Vector3.one;
        slot.preserveAspect = preserveAspect;
    }

    private void PositionSlot(Image slot, float x)
    {
        if (slot != null)
        {
            slot.rectTransform.anchoredPosition = new Vector2(x, 0f);
        }
    }

    private void ApplySlotSprite(Image slot, int index)
    {
        if (slot == null)
        {
            return;
        }

        Sprite sprite = IsValidIndex(index) ? bannerSprites[index] : null;
        if (slot.sprite != sprite)
        {
            slot.sprite = sprite;
        }
    }

    private static void SetSlotVisible(Image slot, bool visible)
    {
        if (slot != null && slot.gameObject.activeSelf != visible)
        {
            slot.gameObject.SetActive(visible);
        }
    }

    private bool CanMove(int direction)
    {
        if (bannerSprites.Count == 0)
        {
            return false;
        }

        if (loop)
        {
            return true;
        }

        if (bannerSprites.Count == 1)
        {
            return false;
        }

        int candidate = currentIndex + direction;
        return candidate >= 0 && candidate < bannerSprites.Count;
    }

    private int GetAdjacentIndex(int direction)
    {
        if (bannerSprites.Count == 0)
        {
            return -1;
        }

        int candidate = currentIndex + direction;
        return loop
            ? WrapIndex(candidate, bannerSprites.Count)
            : Mathf.Clamp(candidate, 0, bannerSprites.Count - 1);
    }

    private int NormalizeOrClampIndex(int index)
    {
        if (bannerSprites.Count == 0)
        {
            return 0;
        }

        return loop
            ? WrapIndex(index, bannerSprites.Count)
            : Mathf.Clamp(index, 0, bannerSprites.Count - 1);
    }

    private static int WrapIndex(int index, int count)
    {
        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < bannerSprites.Count;
    }

    private void EnsureBannerHierarchy()
    {
        if (bannerContainer == null)
        {
            GameObject containerObject = new GameObject(
                "Banner Container",
                typeof(RectTransform)
            );
            bannerContainer = containerObject.GetComponent<RectTransform>();
            bannerContainer.SetParent(viewport, false);
        }

        if (previousBanner == null)
        {
            previousBanner = CreateBannerImage("Previous Banner");
        }

        if (currentBanner == null)
        {
            currentBanner = CreateBannerImage("Current Banner");
        }

        if (nextBanner == null)
        {
            nextBanner = CreateBannerImage("Next Banner");
        }

        bannerContainer.SetAsFirstSibling();
    }

    private Image CreateBannerImage(string objectName)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(bannerContainer, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = true;
        image.preserveAspect = preserveAspect;
        return image;
    }

    private void ResolveViewport()
    {
        if (viewport == null)
        {
            viewport = GetComponent<RectTransform>();
        }
    }

    private bool TryGetLocalPointerX(PointerEventData eventData, out float x)
    {
        bool success = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            viewport,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint
        );
        x = localPoint.x;
        return success;
    }

    private void SetContainerX(float x)
    {
        Vector2 position = bannerContainer.anchoredPosition;
        position.x = x;
        bannerContainer.anchoredPosition = position;
    }

    private void StopSnap()
    {
        if (snapCoroutine != null)
        {
            StopCoroutine(snapCoroutine);
            snapCoroutine = null;
        }

        isTransitioning = false;
    }

    private void ResetAutoAdvanceTimer()
    {
        autoAdvanceElapsed = 0f;
    }

    private void NotifyFocusedBanner()
    {
        onBannerFocused?.Invoke(CurrentIndex, CurrentSprite);
    }

    private float PageWidth => viewport != null
        ? Mathf.Max(1f, viewport.rect.width)
        : 1f;

    private float PageHeight => viewport != null
        ? Mathf.Max(1f, viewport.rect.height)
        : 1f;

    private void OnValidate()
    {
        startIndex = Mathf.Max(0, startIndex);
        autoAdvanceInterval = Mathf.Max(0.1f, autoAdvanceInterval);
        snapDuration = Mathf.Max(0f, snapDuration);

        if (Application.isPlaying && isInitialized)
        {
            Refresh(false);
        }
    }
}
