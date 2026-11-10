using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class BgmTrackRowView : MonoBehaviour, IPointerClickHandler
{
    private TMP_Text orderLabel;
    private TMP_Text titleLabel;
    private TMP_Text playingLabel;
    private Image background;
    private BgmPlaylistPanelController owner;
    private BgmKey key;

    public BgmKey Key => key;

    public void Build(BgmPlaylistPanelController panelOwner)
    {
        owner = panelOwner;
        RectTransform rowRect = (RectTransform)transform;
        rowRect.sizeDelta = new Vector2(0f, 72f);

        LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 72f;

        background = gameObject.AddComponent<Image>();
        background.color = new Color(0.13f, 0.14f, 0.18f, 1f);

        RectTransform handle = CreateRect("Drag Handle", rowRect);
        SetHorizontalRect(handle, 0f, 64f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(1f, 1f, 1f, 0.08f);
        RectTransform handleLabelRect = CreateRect("Handle Label", handle);
        handleLabelRect.anchorMin = Vector2.zero;
        handleLabelRect.anchorMax = Vector2.one;
        handleLabelRect.offsetMin = Vector2.zero;
        handleLabelRect.offsetMax = Vector2.zero;
        TMP_Text handleLabel = CreateText(handleLabelRect, "≡", 30f, TextAlignmentOptions.Center);
        handleLabel.raycastTarget = false;
        BgmTrackDragHandle dragHandle = handle.gameObject.AddComponent<BgmTrackDragHandle>();
        dragHandle.Initialize(this);

        RectTransform orderRect = CreateRect("Order", rowRect);
        SetHorizontalRect(orderRect, 72f, 60f);
        orderLabel = CreateText(orderRect, "1", 22f, TextAlignmentOptions.Center);

        RectTransform titleRect = CreateRect("Title", rowRect);
        titleRect.anchorMin = new Vector2(0f, 0f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.offsetMin = new Vector2(145f, 0f);
        titleRect.offsetMax = new Vector2(-150f, 0f);
        titleLabel = CreateText(titleRect, string.Empty, 25f, TextAlignmentOptions.MidlineLeft);

        RectTransform stateRect = CreateRect("State", rowRect);
        stateRect.anchorMin = new Vector2(1f, 0f);
        stateRect.anchorMax = new Vector2(1f, 1f);
        stateRect.pivot = new Vector2(1f, 0.5f);
        stateRect.anchoredPosition = new Vector2(-12f, 0f);
        stateRect.sizeDelta = new Vector2(125f, 0f);
        playingLabel = CreateText(stateRect, string.Empty, 19f, TextAlignmentOptions.Center);
        playingLabel.color = new Color(0.45f, 0.9f, 0.7f, 1f);
    }

    public void Bind(BgmKey trackKey, int index, bool isCurrent)
    {
        key = trackKey;
        orderLabel.text = (index + 1).ToString("00");
        titleLabel.text = BgmManager.Instance.TryGetTrackDisplayInfo(
                trackKey,
                out BgmTrackDisplayInfo displayInfo
            )
            && !string.IsNullOrWhiteSpace(displayInfo.DisplayTitle)
                ? displayInfo.DisplayTitle
                : trackKey.ToString();
        playingLabel.text = isCurrent ? "PLAYING" : string.Empty;
        background.color = isCurrent
            ? new Color(0.18f, 0.28f, 0.25f, 1f)
            : new Color(0.13f, 0.14f, 0.18f, 1f);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            owner?.PlayRow(this);
        }
    }

    internal void BeginDrag(PointerEventData eventData)
    {
        owner?.BeginRowDrag(this, eventData);
    }

    internal void Drag(PointerEventData eventData)
    {
        owner?.DragRow(this, eventData);
    }

    internal void EndDrag(PointerEventData eventData)
    {
        owner?.EndRowDrag(this, eventData);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = 5;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void SetHorizontalRect(RectTransform rect, float left, float width)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(left, 0f);
        rect.sizeDelta = new Vector2(width, 0f);
    }

    private static TMP_Text CreateText(RectTransform parent, string text, float size, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI label = parent.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = Color.white;
        label.alignment = alignment;
        return label;
    }
}

public sealed class BgmTrackDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private BgmTrackRowView row;

    public void Initialize(BgmTrackRowView rowView)
    {
        row = rowView;
    }

    public void OnBeginDrag(PointerEventData eventData) => row?.BeginDrag(eventData);
    public void OnDrag(PointerEventData eventData) => row?.Drag(eventData);
    public void OnEndDrag(PointerEventData eventData) => row?.EndDrag(eventData);
}
