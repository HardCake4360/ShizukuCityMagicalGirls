using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class BgmPlaybackProgressUI : MonoBehaviour
{
    [Header("Progress")]
    [Tooltip("전용 UI에서 진행선의 실제 시작점과 끝점을 포함하는 RectTransform입니다.")]
    [SerializeField] private RectTransform progressArea;
    [Tooltip("진행선 위에서 수평 이동할 인디케이터입니다.")]
    [SerializeField] private RectTransform indicator;
    [SerializeField, Min(0f)] private float horizontalPadding;

    [Header("Time Text")]
    [SerializeField] private TMP_Text currentTimeText;
    [SerializeField] private TMP_Text totalTimeText;

    private BgmManager manager;
    private bool isDragging;
    private float previewNormalizedTime;

    public bool IsDragging => isDragging;

    private void Awake()
    {
        if (progressArea == null)
        {
            progressArea = transform as RectTransform;
        }

        EnsureDragHandle();
    }

    private void OnEnable()
    {
        manager = BgmManager.Instance;
        RefreshFromPlayback();
    }

    private void OnDisable()
    {
        isDragging = false;
    }

    private void Update()
    {
        if (!isDragging)
        {
            RefreshFromPlayback();
        }
    }

    internal void BeginDrag(PointerEventData eventData)
    {
        if (manager == null || manager.CurrentClip == null)
        {
            return;
        }

        isDragging = true;
        UpdatePreview(eventData);
    }

    internal void Drag(PointerEventData eventData)
    {
        if (isDragging)
        {
            UpdatePreview(eventData);
        }
    }

    internal void EndDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        UpdatePreview(eventData);
        manager.SeekNormalized(previewNormalizedTime);
        isDragging = false;
        RefreshFromPlayback();
    }

    public void RefreshFromPlayback()
    {
        if (manager == null)
        {
            manager = BgmManager.Instance;
        }

        float duration = manager.CurrentDuration;
        float currentTime = manager.CurrentTime;
        ApplyNormalizedPosition(duration > 0f ? currentTime / duration : 0f);
        SetTimeText(currentTimeText, currentTime);
        SetTimeText(totalTimeText, duration);
    }

    private void UpdatePreview(PointerEventData eventData)
    {
        if (progressArea == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                progressArea,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint
            ))
        {
            return;
        }

        Rect rect = progressArea.rect;
        float minimumX = rect.xMin + horizontalPadding;
        float maximumX = rect.xMax - horizontalPadding;
        if (maximumX <= minimumX)
        {
            previewNormalizedTime = 0f;
        }
        else
        {
            previewNormalizedTime = Mathf.InverseLerp(
                minimumX,
                maximumX,
                Mathf.Clamp(localPoint.x, minimumX, maximumX)
            );
        }

        ApplyNormalizedPosition(previewNormalizedTime);
        SetTimeText(
            currentTimeText,
            previewNormalizedTime * manager.CurrentDuration
        );
        SetTimeText(totalTimeText, manager.CurrentDuration);
    }

    private void ApplyNormalizedPosition(float normalizedTime)
    {
        if (progressArea == null || indicator == null)
        {
            return;
        }

        Rect rect = progressArea.rect;
        float minimumX = rect.xMin + horizontalPadding;
        float maximumX = rect.xMax - horizontalPadding;
        float x = Mathf.Lerp(minimumX, maximumX, Mathf.Clamp01(normalizedTime));
        Vector3 worldPosition = progressArea.TransformPoint(
            new Vector3(x, rect.center.y, 0f)
        );
        Vector3 indicatorPosition = indicator.position;
        indicatorPosition.x = worldPosition.x;
        indicator.position = indicatorPosition;
    }

    private void EnsureDragHandle()
    {
        if (indicator == null)
        {
            return;
        }

        BgmProgressDragHandle handle =
            indicator.GetComponent<BgmProgressDragHandle>();
        if (handle == null)
        {
            handle = indicator.gameObject.AddComponent<BgmProgressDragHandle>();
        }

        handle.Initialize(this);
    }

    private static void SetTimeText(TMP_Text label, float seconds)
    {
        if (label == null)
        {
            return;
        }

        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;
        label.text = $"{minutes}:{remainingSeconds:00}";
    }

    private void OnValidate()
    {
        horizontalPadding = Mathf.Max(0f, horizontalPadding);
        if (progressArea == null)
        {
            progressArea = transform as RectTransform;
        }
    }
}

[DisallowMultipleComponent]
public sealed class BgmProgressDragHandle : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    private BgmPlaybackProgressUI owner;

    public void Initialize(BgmPlaybackProgressUI progressUi)
    {
        owner = progressUi;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        owner?.BeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        owner?.Drag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        owner?.EndDrag(eventData);
    }
}
