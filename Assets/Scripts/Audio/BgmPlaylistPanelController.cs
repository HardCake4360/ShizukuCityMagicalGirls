using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(BgmPlaylistController))]
public sealed class BgmPlaylistPanelController : MonoBehaviour
{
    [Header("Roots")]
    [Tooltip("씬에 배치한 전용 BGM 플레이어 UI의 루트입니다.")]
    [SerializeField] private GameObject playerRoot;
    [Tooltip("폴더 버튼으로 여닫는 플레이리스트 UI의 루트입니다.")]
    [SerializeField] private GameObject playlistRoot;
    [SerializeField] private RectTransform playlistContentRoot;
    [SerializeField] private bool startOpen;

    [Header("Track Text")]
    [SerializeField] private TMP_Text trackTitleText;
    [SerializeField] private TMP_Text trackIdText;

    [Header("Buttons")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button folderButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button playPauseButton;
    [SerializeField] private Button nextButton;

    [Header("Sub Controls")]
    [SerializeField] private BgmPlaybackProgressUI progressUi;
    [SerializeField] private BgmVolumeControlUI volumeControlUi;

    private readonly List<BgmTrackRowView> rows = new();
    private BgmPlaylistController playlistController;
    private BgmManager manager;
    private BgmTrackRowView draggedRow;
    private Image folderButtonImage;
    private Image playPauseButtonImage;
    private Sprite folderNormalSprite;
    private Sprite folderPressedSprite;
    private Sprite playPauseNormalSprite;
    private Sprite playPausePressedSprite;

    private void Awake()
    {
        playlistController = GetComponent<BgmPlaylistController>();
        CaptureToggleButtonVisual(
            folderButton,
            out folderButtonImage,
            out folderNormalSprite,
            out folderPressedSprite
        );
        CaptureToggleButtonVisual(
            playPauseButton,
            out playPauseButtonImage,
            out playPauseNormalSprite,
            out playPausePressedSprite
        );
        BindButtons();

        playlistRoot?.SetActive(false);
        ApplyFolderButtonVisual(false);
        volumeControlUi?.CloseImmediate();
        if (playerRoot != null)
        {
            playerRoot.SetActive(startOpen);
        }
    }

    private void OnEnable()
    {
        manager = BgmManager.Instance;
        playlistController.PlaylistChanged += HandlePlaylistChanged;
        playlistController.CurrentTrackChanged += HandleCurrentTrackChanged;
        manager.TrackChanged += HandleManagerTrackChanged;
        manager.PlaybackStateChanged += RefreshPlaybackState;
        RefreshAllDisplay();
    }

    private void Start()
    {
        if (startOpen)
        {
            Open();
        }
        else
        {
            Close();
        }
    }

    private void OnDisable()
    {
        playlistController.PlaylistChanged -= HandlePlaylistChanged;
        playlistController.CurrentTrackChanged -= HandleCurrentTrackChanged;
        if (manager != null)
        {
            manager.TrackChanged -= HandleManagerTrackChanged;
            manager.PlaybackStateChanged -= RefreshPlaybackState;
        }
    }

    private void OnDestroy()
    {
        UnbindButtons();
    }

    public void Open()
    {
        if (playerRoot == null)
        {
            Debug.LogError(
                "BgmPlaylistPanelController requires a dedicated Player Root.",
                this
            );
            return;
        }

        playerRoot.SetActive(true);
        playlistRoot?.SetActive(false);
        ApplyFolderButtonVisual(false);
        volumeControlUi?.CloseImmediate();
        RebuildRows();
        RefreshAllDisplay();
    }

    public void Close()
    {
        draggedRow = null;
        playlistRoot?.SetActive(false);
        ApplyFolderButtonVisual(false);
        volumeControlUi?.CloseImmediate();
        playerRoot?.SetActive(false);
    }

    public void TogglePlaylist()
    {
        if (playlistRoot == null)
        {
            return;
        }

        bool shouldOpen = !playlistRoot.activeSelf;
        playlistRoot.SetActive(shouldOpen);
        ApplyFolderButtonVisual(shouldOpen);
        if (shouldOpen)
        {
            RebuildRows();
        }
    }

    public void PlayRow(BgmTrackRowView row)
    {
        int index = rows.IndexOf(row);
        if (index >= 0)
        {
            playlistController.PlayAt(index);
        }
    }

    internal void BeginRowDrag(BgmTrackRowView row, PointerEventData eventData)
    {
        draggedRow = row;
        CanvasGroup group = row.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = row.gameObject.AddComponent<CanvasGroup>();
        }

        group.alpha = 0.7f;
        group.blocksRaycasts = false;
    }

    internal void DragRow(BgmTrackRowView row, PointerEventData eventData)
    {
        if (draggedRow != row)
        {
            return;
        }

        int fromIndex = rows.IndexOf(row);
        int targetIndex = GetTargetIndex(
            eventData.position.y,
            eventData.pressEventCamera
        );
        if (targetIndex < 0 || targetIndex == fromIndex)
        {
            return;
        }

        rows.RemoveAt(fromIndex);
        rows.Insert(targetIndex, row);
        row.transform.SetSiblingIndex(targetIndex);
        playlistController.MoveTrack(fromIndex, targetIndex);
        RefreshRowBindings();
    }

    internal void EndRowDrag(BgmTrackRowView row, PointerEventData eventData)
    {
        if (draggedRow != row)
        {
            return;
        }

        CanvasGroup group = row.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }

        draggedRow = null;
        if (playlistContentRoot != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(playlistContentRoot);
        }
    }

    private void BindButtons()
    {
        closeButton?.onClick.AddListener(Close);
        folderButton?.onClick.AddListener(TogglePlaylist);
        previousButton?.onClick.AddListener(PlayPrevious);
        playPauseButton?.onClick.AddListener(TogglePlayPause);
        nextButton?.onClick.AddListener(PlayNext);
    }

    private void UnbindButtons()
    {
        closeButton?.onClick.RemoveListener(Close);
        folderButton?.onClick.RemoveListener(TogglePlaylist);
        previousButton?.onClick.RemoveListener(PlayPrevious);
        playPauseButton?.onClick.RemoveListener(TogglePlayPause);
        nextButton?.onClick.RemoveListener(PlayNext);
    }

    private void PlayPrevious()
    {
        playlistController.PlayPrevious();
    }

    private void TogglePlayPause()
    {
        playlistController.TogglePlayPause();
        RefreshPlaybackState();
    }

    private void PlayNext()
    {
        playlistController.PlayNext();
    }

    private int GetTargetIndex(float pointerScreenY, Camera eventCamera)
    {
        int targetIndex = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] == draggedRow)
            {
                continue;
            }

            RectTransform rowRect = (RectTransform)rows[i].transform;
            float rowScreenY = RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                rowRect.position
            ).y;
            if (pointerScreenY < rowScreenY)
            {
                targetIndex++;
            }
        }

        return targetIndex;
    }

    private void RebuildRows()
    {
        if (draggedRow != null || playlistContentRoot == null)
        {
            return;
        }

        ConfigurePlaylistLayout();

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] != null)
            {
                Destroy(rows[i].gameObject);
            }
        }

        rows.Clear();
        IReadOnlyList<BgmKey> tracks = playlistController.OrderedTracks;
        for (int i = 0; i < tracks.Count; i++)
        {
            GameObject rowObject = new GameObject(
                $"Track {i + 1}",
                typeof(RectTransform)
            );
            rowObject.layer = 5;
            rowObject.transform.SetParent(playlistContentRoot, false);
            BgmTrackRowView row = rowObject.AddComponent<BgmTrackRowView>();
            row.Build(this);
            rows.Add(row);
        }

        RefreshRowBindings();
    }

    private void ConfigurePlaylistLayout()
    {
        Vector2 anchorMin = playlistContentRoot.anchorMin;
        Vector2 anchorMax = playlistContentRoot.anchorMax;
        Vector2 pivot = playlistContentRoot.pivot;
        Vector2 sizeDelta = playlistContentRoot.sizeDelta;
        Vector2 anchoredPosition = playlistContentRoot.anchoredPosition;

        anchorMin.x = 0f;
        anchorMin.y = 1f;
        anchorMax.x = 1f;
        anchorMax.y = 1f;
        pivot.x = 0.5f;
        pivot.y = 1f;
        sizeDelta.x = 0f;
        anchoredPosition.x = 0f;

        playlistContentRoot.anchorMin = anchorMin;
        playlistContentRoot.anchorMax = anchorMax;
        playlistContentRoot.pivot = pivot;
        playlistContentRoot.sizeDelta = sizeDelta;
        playlistContentRoot.anchoredPosition = anchoredPosition;

        VerticalLayoutGroup layout =
            playlistContentRoot.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        ContentSizeFitter fitter =
            playlistContentRoot.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    private void RefreshRowBindings()
    {
        int count = Mathf.Min(rows.Count, playlistController.OrderedTracks.Count);
        for (int i = 0; i < count; i++)
        {
            rows[i].Bind(
                playlistController.OrderedTracks[i],
                i,
                i == playlistController.CurrentIndex
            );
        }

        if (playlistContentRoot != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(playlistContentRoot);
        }
    }

    private void HandlePlaylistChanged()
    {
        if (playlistRoot != null && playlistRoot.activeInHierarchy)
        {
            RebuildRows();
        }
    }

    private void HandleCurrentTrackChanged(int index, BgmKey key)
    {
        RefreshRowBindings();
        RefreshTrackText(key);
        RefreshPlaybackState();
    }

    private void HandleManagerTrackChanged(BgmKey key)
    {
        RefreshTrackText(key);
        RefreshPlaybackState();
    }

    private void RefreshAllDisplay()
    {
        if (manager == null)
        {
            manager = BgmManager.Instance;
        }

        RefreshTrackText(manager.CurrentKey);
        RefreshPlaybackState();
        progressUi?.RefreshFromPlayback();
    }

    private void RefreshTrackText(BgmKey key)
    {
        string title = string.Empty;
        string id = string.Empty;

        if (key != BgmKey.None)
        {
            if (manager.TryGetTrackDisplayInfo(
                    key,
                    out BgmTrackDisplayInfo displayInfo
                ))
            {
                title = displayInfo.DisplayTitle;
                id = displayInfo.DisplayId;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = manager.CurrentClip != null
                    ? manager.CurrentClip.name
                    : key.ToString();
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                id = key.ToString();
            }
        }

        if (trackTitleText != null)
        {
            trackTitleText.text = title;
        }

        if (trackIdText != null)
        {
            trackIdText.text = id;
        }
    }

    private void RefreshPlaybackState()
    {
        if (manager == null)
        {
            return;
        }

        bool activelyPlaying = manager.IsPlaying && !manager.IsPaused;
        ApplyToggleButtonVisual(
            playPauseButtonImage,
            playPauseNormalSprite,
            playPausePressedSprite,
            activelyPlaying
        );

        progressUi?.RefreshFromPlayback();
    }

    private void ApplyFolderButtonVisual(bool playlistOpen)
    {
        ApplyToggleButtonVisual(
            folderButtonImage,
            folderNormalSprite,
            folderPressedSprite,
            playlistOpen
        );
    }

    private static void CaptureToggleButtonVisual(
        Button button,
        out Image targetImage,
        out Sprite normalSprite,
        out Sprite pressedSprite
    )
    {
        targetImage = null;
        normalSprite = null;
        pressedSprite = null;
        if (button == null)
        {
            return;
        }

        targetImage = button.targetGraphic as Image;
        if (targetImage == null)
        {
            targetImage = button.GetComponent<Image>();
        }

        if (targetImage == null)
        {
            return;
        }

        normalSprite = targetImage.sprite;
        pressedSprite = button.spriteState.pressedSprite;

        // Sprite Swap이 토글 스프라이트를 다시 덮어쓰지 않도록 하고,
        // 기본 Button의 호버/클릭 피드백은 Color Tint로 유지합니다.
        if (button.transition == Selectable.Transition.SpriteSwap)
        {
            button.transition = Selectable.Transition.ColorTint;
        }
    }

    private static void ApplyToggleButtonVisual(
        Image targetImage,
        Sprite normalSprite,
        Sprite pressedSprite,
        bool toggled
    )
    {
        if (targetImage == null)
        {
            return;
        }

        Sprite targetSprite = toggled && pressedSprite != null
            ? pressedSprite
            : normalSprite;
        if (targetSprite != null && targetImage.sprite != targetSprite)
        {
            targetImage.sprite = targetSprite;
        }
    }
}
