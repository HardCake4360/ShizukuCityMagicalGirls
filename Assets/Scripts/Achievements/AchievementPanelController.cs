using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class AchievementPanelController : MonoBehaviour
{
    [Header("New Achievement List")]
    [SerializeField] private AchievementListManager listManager;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private GameObject activePage;
    [SerializeField] private GameObject completedPage;
    [SerializeField] private Transform activeListRoot;
    [SerializeField] private Transform completedListRoot;
    [SerializeField] private AchievementRowView rowPrefab;
    [SerializeField] private GameObject activeEmptyMessage;
    [SerializeField] private GameObject completedEmptyMessage;
    [SerializeField] private bool buildRuntimeSkeletonWhenMissing = true;

    private readonly List<AchievementRowView> spawnedRows = new();
    private bool subscribed;
    private AchievementManager subscribedAchievementManager;

    private void Awake()
    {
        if (listManager == null && buildRuntimeSkeletonWhenMissing && panelRoot == null)
        {
            BuildRuntimeSkeleton();
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void Open()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }

        if (listManager != null)
        {
            listManager.EnsureBuilt();
            listManager.RefreshExistingElements();
            return;
        }

        Subscribe();
        Rebuild();
        ShowActiveAchievements();
    }

    public void Close()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        Unsubscribe();
    }

    public void Rebuild()
    {
        if (listManager != null)
        {
            listManager.EnsureBuilt();
            listManager.RefreshExistingElements();
            return;
        }

        ClearRows();

        int activeCount = 0;
        int completedCount = 0;
        foreach (AchievementProgress progress in AchievementManager.Instance.AllAchievements)
        {
            if (progress.State == AchievementState.Inactive)
            {
                continue;
            }

            Transform listRoot = progress.State == AchievementState.Completed
                ? completedListRoot
                : activeListRoot;
            if (listRoot == null || rowPrefab == null)
            {
                continue;
            }

            AchievementRowView row = Instantiate(rowPrefab, listRoot);
            row.Bind(progress, CompleteAchievement);
            spawnedRows.Add(row);

            if (progress.State == AchievementState.Completed)
            {
                completedCount++;
            }
            else
            {
                activeCount++;
            }
        }

        activeEmptyMessage?.SetActive(activeCount == 0);
        completedEmptyMessage?.SetActive(completedCount == 0);
    }

    public void ShowActiveAchievements()
    {
        activePage?.SetActive(true);
        completedPage?.SetActive(false);
    }

    public void ShowCompletedAchievements()
    {
        activePage?.SetActive(false);
        completedPage?.SetActive(true);
    }

    private void CompleteAchievement(string achievementId)
    {
        AchievementManager.Instance.TryComplete(achievementId);
    }

    private void Subscribe()
    {
        if (listManager != null || subscribed)
        {
            return;
        }

        subscribedAchievementManager = AchievementManager.Instance;
        if (subscribedAchievementManager == null)
            return;
        subscribedAchievementManager.ProgressChanged += OnProgressChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        if (subscribedAchievementManager != null)
            subscribedAchievementManager.ProgressChanged -= OnProgressChanged;
        subscribedAchievementManager = null;
        subscribed = false;
    }

    private void OnProgressChanged(AchievementProgress progress)
    {
        Rebuild();
    }

    private void ClearRows()
    {
        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null)
            {
                Destroy(spawnedRows[i].gameObject);
            }
        }

        spawnedRows.Clear();
    }

    private void BuildRuntimeSkeleton()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("AchievementPanelController requires a parent Canvas.", this);
            return;
        }

        panelRoot = CreateUiObject("Achievement Panel", canvas.transform).gameObject;
        RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1000f, 650f);

        Image background = panelRoot.AddComponent<Image>();
        background.color = new Color(0.06f, 0.07f, 0.1f, 0.96f);

        CreateLabel("Title", panelRect, "ACHIEVEMENTS", 40f, new Vector2(0f, 285f), new Vector2(600f, 60f));

        Button activeTabButton = CreateButton("Active Tab", panelRect, "ACTIVE", new Vector2(-180f, 225f), new Vector2(260f, 55f));
        activeTabButton.onClick.AddListener(ShowActiveAchievements);

        Button completedTabButton = CreateButton("Completed Tab", panelRect, "COMPLETED", new Vector2(110f, 225f), new Vector2(260f, 55f));
        completedTabButton.onClick.AddListener(ShowCompletedAchievements);

        Button closeButton = CreateButton("Close", panelRect, "X", new Vector2(450f, 285f), new Vector2(55f, 55f));
        closeButton.onClick.AddListener(Close);

        activePage = CreateListPage("Active Page", panelRect, out activeListRoot, out activeEmptyMessage);
        completedPage = CreateListPage("Completed Page", panelRect, out completedListRoot, out completedEmptyMessage);
    }

    private static GameObject CreateListPage(string name, RectTransform parent, out Transform listRoot, out GameObject emptyMessage)
    {
        RectTransform page = CreateUiObject(name, parent);
        page.anchorMin = new Vector2(0.5f, 0.5f);
        page.anchorMax = new Vector2(0.5f, 0.5f);
        page.anchoredPosition = new Vector2(0f, -55f);
        page.sizeDelta = new Vector2(920f, 470f);

        ScrollRect scrollRect = page.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        RectTransform viewport = CreateUiObject("Viewport", page);
        StretchToParent(viewport);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.035f);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = CreateUiObject("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport;
        scrollRect.content = content;
        listRoot = content;

        TMP_Text emptyLabel = CreateLabel("Empty Message", page, "NO ACHIEVEMENTS", 28f, Vector2.zero, new Vector2(500f, 80f));
        emptyMessage = emptyLabel.gameObject;
        return page.gameObject;
    }

    private static Button CreateButton(string name, RectTransform parent, string label, Vector2 position, Vector2 size)
    {
        RectTransform buttonRect = CreateUiObject(name, parent);
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = position;
        buttonRect.sizeDelta = size;

        Image image = buttonRect.gameObject.AddComponent<Image>();
        image.color = new Color(0.23f, 0.25f, 0.32f, 1f);
        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        TMP_Text text = CreateLabel("Label", buttonRect, label, 24f, Vector2.zero, size);
        text.raycastTarget = false;
        return button;
    }

    private static TMP_Text CreateLabel(string name, RectTransform parent, string text, float fontSize, Vector2 position, Vector2 size)
    {
        RectTransform labelRect = CreateUiObject(name, parent);
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = position;
        labelRect.sizeDelta = size;

        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    private static RectTransform CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = 5;
        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        return rectTransform;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
