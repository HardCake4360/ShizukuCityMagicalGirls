using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000)]
public sealed class SceneLoadManager : MonoBehaviour
{
    private const float DefaultMinimumLoadDuration = 1f;
    private const float DefaultFadeDuration = 0.5f;

    private static SceneLoadManager instance;
    private static bool isLoadingNow;

    private CanvasGroup overlay;
    private Image overlayImage;
    private Coroutine loadCoroutine;

    public static SceneLoadManager Instance
    {
        get
        {
            EnsureInstance();
            return instance;
        }
    }

    public static bool IsLoadingNow => isLoadingNow;
    public static bool IsBattleEntryLoading { get; private set; }
    public bool IsLoading => isLoadingNow;
    public float Progress { get; private set; }

    public static event Action<bool> LoadingStateChanged;
    public event Action<float> ProgressChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        isLoadingNow = false;
        IsBattleEntryLoading = false;
        LoadingStateChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (instance != null)
        {
            return;
        }

        instance = FindFirstObjectByType<SceneLoadManager>();
        if (instance == null)
        {
            GameObject managerObject = new GameObject("Scene Load Manager");
            instance = managerObject.AddComponent<SceneLoadManager>();
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        CreateOverlay();
    }

    public void LoadScene(string sceneName)
    {
        LoadScene(sceneName, DefaultMinimumLoadDuration, null);
    }

    public bool LoadScene(
        string sceneName,
        float minimumLoadDuration,
        SceneTransitionHook specialTransition
    )
    {
        return BeginLoad(sceneName, minimumLoadDuration, specialTransition, false, DefaultFadeDuration);
    }

    public bool LoadBattleScene(string sceneName, float minimumLoadDuration, float whiteOutDuration = 0.4f) =>
        BeginLoad(sceneName, minimumLoadDuration, null, true, whiteOutDuration);

    private bool BeginLoad(string sceneName, float minimumLoadDuration, SceneTransitionHook specialTransition,
        bool battleEntry, float coverDuration)
    {
        if (isLoadingNow)
        {
            Debug.LogWarning($"Scene load request for '{sceneName}' was ignored because another load is active.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"Scene '{sceneName}' is not available. Add it to Build Settings first.");
            return false;
        }

        IsBattleEntryLoading = battleEntry;
        overlayImage.color = battleEntry ? Color.white : Color.black;
        SetLoadingState(true);
        loadCoroutine = StartCoroutine(LoadRoutine(
            sceneName,
            Mathf.Max(0f, minimumLoadDuration),
            specialTransition,
            battleEntry,
            Mathf.Max(0f, coverDuration)
        ));
        return true;
    }

    private IEnumerator LoadRoutine(
        string sceneName,
        float minimumLoadDuration,
        SceneTransitionHook specialTransition,
        bool battleEntry,
        float coverDuration
    )
    {
        float startedAt = Time.realtimeSinceStartup;
        bool usesSpecialTransition = specialTransition != null && specialTransition.isActiveAndEnabled;

        if (usesSpecialTransition)
        {
            bool enterComplete = false;
            specialTransition.PlayEnter(() => enterComplete = true);

            if (specialTransition.WaitForEnterCompletion)
            {
                yield return WaitForSignal(
                    () => enterComplete
                          || specialTransition == null
                          || !specialTransition.isActiveAndEnabled,
                    specialTransition.CompletionTimeout,
                    "Special transition enter animation timed out."
                );
            }

            if (specialTransition != null)
            {
                specialTransition.PlayLoop();
            }
        }
        else
        {
            yield return FadeOverlay(1f, coverDuration);
        }

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (operation == null)
        {
            Debug.LogError($"Unity could not start loading scene '{sceneName}'.");
            yield return RecoverFromFailure();
            yield break;
        }

        operation.allowSceneActivation = false;
        while (operation.progress < 0.9f
               || Time.realtimeSinceStartup - startedAt < minimumLoadDuration)
        {
            SetProgress(Mathf.Clamp01(operation.progress / 0.9f));
            yield return null;
        }

        SetProgress(1f);

        if (usesSpecialTransition && specialTransition != null)
        {
            bool exitComplete = false;
            specialTransition.PlayExit(() => exitComplete = true);

            if (specialTransition.WaitForExitCompletion)
            {
                yield return WaitForSignal(
                    () => exitComplete
                          || specialTransition == null
                          || !specialTransition.isActiveAndEnabled,
                    specialTransition.CompletionTimeout,
                    "Special transition exit animation timed out."
                );
            }

            overlay.alpha = 1f;
        }

        operation.allowSceneActivation = true;
        while (!operation.isDone)
        {
            yield return null;
        }

        // Keep the opaque cover across activation and destination Awake/Start/layout.
        yield return null;
        BattleEntryDirector entry = null;
        if (battleEntry)
        {
            float readyStartedAt = Time.realtimeSinceStartup;
            while (entry == null || !entry.IsPrepared)
            {
                foreach (BattleEntryDirector candidate in FindObjectsByType<BattleEntryDirector>(FindObjectsSortMode.None))
                    if (candidate.gameObject.scene == SceneManager.GetActiveScene()) { entry = candidate; break; }
                if (entry != null && entry.IsPrepared) break;
                if (Time.realtimeSinceStartup - readyStartedAt >= 10f)
                {
                    Debug.LogError("Battle entry preparation failed; returning to Lobby under the white cover.");
                    MissionSessionManager.Instance.ClearMission();
                    IsBattleEntryLoading = false;
                    if (entry != null) entry.Abort();
                    if (Application.CanStreamedLevelBeLoaded("Lobby"))
                    {
                        AsyncOperation recovery = SceneManager.LoadSceneAsync("Lobby");
                        while (recovery != null && !recovery.isDone) yield return null;
                        yield return null;
                    }
                    yield return RecoverFromFailure();
                    yield break;
                }
                yield return null;
            }
            entry.BeginReveal();
        }
        yield return FadeOverlay(0f, entry != null ? entry.RevealDuration : DefaultFadeDuration);
        while (entry != null && entry.IsRevealing) yield return null;

        loadCoroutine = null;
        SetProgress(0f);
        IsBattleEntryLoading = false;
        SetLoadingState(false);
        if (entry != null) entry.ArmFirstInput();
    }

    private IEnumerator WaitForSignal(Func<bool> predicate, float timeout, string timeoutMessage)
    {
        float startedAt = Time.realtimeSinceStartup;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup - startedAt >= timeout)
            {
                Debug.LogWarning(timeoutMessage);
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator FadeOverlay(float targetAlpha, float duration)
    {
        float startAlpha = overlay.alpha;
        if (duration <= 0f)
        {
            overlay.alpha = targetAlpha;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            overlay.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        overlay.alpha = targetAlpha;
    }

    private IEnumerator RecoverFromFailure()
    {
        yield return FadeOverlay(0f, DefaultFadeDuration);
        loadCoroutine = null;
        SetProgress(0f);
        IsBattleEntryLoading = false;
        SetLoadingState(false);
    }

    private void CreateOverlay()
    {
        GameObject canvasObject = new GameObject(
            "Scene Transition Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup)
        );
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        overlay = canvasObject.GetComponent<CanvasGroup>();
        overlay.alpha = 0f;
        overlay.interactable = false;
        overlay.blocksRaycasts = false;

        GameObject blackImageObject = new GameObject(
            "Black Overlay",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        blackImageObject.transform.SetParent(canvasObject.transform, false);

        RectTransform rect = blackImageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = blackImageObject.GetComponent<Image>();
        overlayImage = image;
        image.color = Color.black;
        image.raycastTarget = true;
    }

    private void SetLoadingState(bool value)
    {
        isLoadingNow = value;
        if (overlay != null)
        {
            overlay.blocksRaycasts = value;
        }

        LoadingStateChanged?.Invoke(value);
    }

    private void SetProgress(float value)
    {
        Progress = value;
        ProgressChanged?.Invoke(value);
    }
}
