using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Implement on the future score system; the value is read once on game over.</summary>
public interface IGameOverScoreProvider
{
    long GetScore();
}

/// <summary>Scene-owned result UI. Never creates persistent managers during cleanup.</summary>
[DisallowMultipleComponent]
public sealed class GameOverController : MonoBehaviour
{
    [Header("Optional custom UI (leave Panel empty to generate the default UI)")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private TMP_Text gameOverText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private Button returnToLobbyButton;
    [SerializeField] private TMP_FontAsset defaultUIFont;

    [Header("Presentation")]
    [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.75f;
    [SerializeField, Min(0f)] private float fadeDuration = 0.3f;
    [SerializeField] private string lobbySceneName = "Lobby";

    [Header("Future score system")]
    [Tooltip("Assign a component implementing IGameOverScoreProvider, or call SetScore from your score system.")]
    [SerializeField] private MonoBehaviour scoreProvider;

    private IGameOverScoreProvider runtimeScoreProvider;
    private long suppliedScore;
    private bool ownsPause;
    private float previousTimeScale;
    private Button boundReturnButton;

    public bool IsShowing { get; private set; }
    public long DisplayedScore { get; private set; }

    private void Awake()
    {
        if (panel != null)
        {
            panel.alpha = 0f;
            panel.blocksRaycasts = false;
            panel.interactable = false;
            if (panel.gameObject != gameObject)
                panel.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (IsShowing && panel != null)
            panel.alpha = fadeDuration <= 0f ? 1f
                : Mathf.MoveTowards(panel.alpha, 1f, Time.unscaledDeltaTime / fadeDuration);
    }

    public void SetScoreProvider(IGameOverScoreProvider provider)
    {
        runtimeScoreProvider = provider;
    }

    public void SetScore(long score)
    {
        suppliedScore = score;
        if (IsShowing)
        {
            DisplayedScore = score;
            RefreshScoreText();
        }
    }

    public void Show()
    {
        if (IsShowing || !isActiveAndEnabled)
            return;

        if (panel == null)
            CreateDefaultUI();
        BindReturnButton();
        DisplayedScore = ResolveScore();
        RefreshScoreText();
        if (gameOverText != null)
            gameOverText.text = "GAME OVER";

        IsShowing = true;
        previousTimeScale = Time.timeScale;
        ownsPause = true;
        Time.timeScale = 0f;
        panel.alpha = fadeDuration <= 0f ? 1f : 0f;
        panel.blocksRaycasts = true;
        panel.interactable = true;
        panel.gameObject.SetActive(true);
        if (returnToLobbyButton != null)
        {
            returnToLobbyButton.interactable = true;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(returnToLobbyButton.gameObject);
        }
    }

    private long ResolveScore()
    {
        // Unity's destroyed-object check must precede an interface call.
        IGameOverScoreProvider provider = runtimeScoreProvider ?? scoreProvider as IGameOverScoreProvider;
        if (provider is Object unityObject && unityObject == null)
            return suppliedScore;
        if (provider != null)
        {
            try { return provider.GetScore(); }
            catch (System.Exception exception)
            {
                // A future scoring error must not prevent the death screen or pause.
                Debug.LogException(exception, this);
                return suppliedScore;
            }
        }
        if (scoreProvider != null)
            Debug.LogWarning("Game Over Score Provider must implement IGameOverScoreProvider.", scoreProvider);
        return suppliedScore;
    }

    private void RefreshScoreText()
    {
        if (scoreText != null)
            scoreText.text = $"점수: {DisplayedScore:N0}";
    }

    public void ReturnToLobby()
    {
        if (!IsShowing || SceneLoadManager.IsLoadingNow)
            return;
        // Loading fades use unscaled time. Keep combat paused until this scene unloads.
        if (!SceneLoadManager.Instance.LoadScene(lobbySceneName, 1f, null))
            return;
        MissionSessionManager.Instance.ClearMission();
        if (returnToLobbyButton != null)
            returnToLobbyButton.interactable = false;
    }

    private void BindReturnButton()
    {
        if (boundReturnButton == returnToLobbyButton)
            return;
        if (boundReturnButton != null)
            boundReturnButton.onClick.RemoveListener(ReturnToLobby);
        boundReturnButton = returnToLobbyButton;
        if (boundReturnButton != null)
            boundReturnButton.onClick.AddListener(ReturnToLobby);
    }

    private void OnDisable()
    {
        if (ownsPause)
        {
            Time.timeScale = previousTimeScale;
            ownsPause = false;
        }
        IsShowing = false;
        if (panel != null)
        {
            panel.alpha = 0f;
            panel.blocksRaycasts = false;
            panel.interactable = false;
            if (panel.gameObject != gameObject)
                panel.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (boundReturnButton != null)
            boundReturnButton.onClick.RemoveListener(ReturnToLobby);
    }

    private void CreateDefaultUI()
    {
        GameObject root = new GameObject("Game Over UI", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 1; // The scene-loading overlay stays on top.
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        panel = root.GetComponent<CanvasGroup>();

        GameObject dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(root.transform, false);
        RectTransform dimRect = dim.GetComponent<RectTransform>();
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
        dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, dimAlpha);

        gameOverText = CreateText("GAME OVER", root.transform, new Vector2(0f, 100f),
            new Vector2(1000f, 120f), 88f);
        scoreText = CreateText("Score", root.transform, new Vector2(0f, -25f),
            new Vector2(1000f, 70f), 40f);
        GameObject buttonObject = new GameObject("Return To Lobby", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(root.transform, false);
        SetCenteredRect(buttonObject.GetComponent<RectTransform>(), new Vector2(0f, -150f), new Vector2(400f, 80f));
        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.22f, 0.24f, 0.3f, 1f);
        returnToLobbyButton = buttonObject.GetComponent<Button>();
        returnToLobbyButton.targetGraphic = buttonImage;
        TMP_Text label = CreateText("로비로 돌아가기", buttonObject.transform, Vector2.zero, new Vector2(380f, 70f), 32f);
        label.text = "로비로 돌아가기";

        if (EventSystem.current == null && FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject events = new GameObject("Game Over Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    private TMP_Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        SetCenteredRect(textObject.GetComponent<RectTransform>(), position, size);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        if (defaultUIFont != null)
            text.font = defaultUIFont;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
