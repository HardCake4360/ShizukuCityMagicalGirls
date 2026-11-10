using System.Collections;
using System;
using UnityEngine;

[DefaultExecutionOrder(-900)]
public sealed class BgmManager : MonoBehaviour
{
    private const string DefaultLibraryResourcePath = "Audio/BgmLibrary";

    private static BgmManager instance;

    [Header("Library")]
    [SerializeField] private BgmLibrary library;

    [Header("Playback")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField, Min(0f)] private float defaultFadeDuration = 0.5f;
    [SerializeField] private bool loop = true;

    private AudioSource[] sources;
    private int activeSourceIndex;
    private int transitionTargetSourceIndex = -1;
    private Coroutine transitionCoroutine;
    private BgmKey currentKey = BgmKey.None;
    private bool isPaused;

    public static BgmManager Instance
    {
        get
        {
            EnsureInstance();
            return instance;
        }
    }

    public BgmKey CurrentKey => currentKey;
    public bool IsPlaying => GetActiveSource() != null && GetActiveSource().isPlaying;
    public bool IsPaused => isPaused;
    public float Volume => volume;
    public AudioClip CurrentClip => GetCurrentPlaybackSource()?.clip;
    public float CurrentTime
    {
        get
        {
            AudioSource source = GetCurrentPlaybackSource();
            return source != null && source.clip != null ? source.time : 0f;
        }
    }
    public float CurrentDuration => CurrentClip != null ? CurrentClip.length : 0f;
    public float NormalizedTime => CurrentDuration > 0f
        ? Mathf.Clamp01(CurrentTime / CurrentDuration)
        : 0f;

    public event Action<BgmKey> TrackChanged;
    public event Action PlaybackStateChanged;
    public event Action<float> VolumeChanged;

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

        instance = FindFirstObjectByType<BgmManager>();
        if (instance == null)
        {
            GameObject managerObject = new GameObject("BGM Manager");
            instance = managerObject.AddComponent<BgmManager>();
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
        EnsureSources();
        volume = GameSettings.MusicVolume;
        GameSettings.Changed += HandleSettingsChanged;

        if (library == null)
        {
            library = Resources.Load<BgmLibrary>(DefaultLibraryResourcePath);
        }

        if (library == null)
        {
            Debug.LogWarning(
                $"BGM Library was not found at Resources/{DefaultLibraryResourcePath}.asset.",
                this
            );
        }
    }

    private void OnValidate()
    {
        volume = Mathf.Clamp01(volume);
        defaultFadeDuration = Mathf.Max(0f, defaultFadeDuration);
    }

    public bool Play(BgmKey key)
    {
        return Play(key, defaultFadeDuration, false);
    }

    public bool Play(BgmKey key, float fadeDuration, bool restartIfAlreadyPlaying = false)
    {
        if (library == null || !library.TryGetClip(key, out AudioClip clip))
        {
            Debug.LogWarning($"No BGM clip is assigned for key '{key}'.", this);
            return false;
        }

        AudioSource activeSource = GetActiveSource();
        if (!restartIfAlreadyPlaying && currentKey == key && activeSource != null && activeSource.clip == clip)
        {
            if (isPaused)
            {
                Resume();
            }

            return true;
        }

        StopTransition();
        isPaused = false;
        currentKey = key;
        float duration = Mathf.Max(0f, fadeDuration);

        if (activeSource == null || !activeSource.isPlaying || duration <= 0f)
        {
            StopAllSources();
            AudioSource source = sources[activeSourceIndex];
            ConfigureSource(source, clip, volume);
            source.Play();
            TrackChanged?.Invoke(currentKey);
            PlaybackStateChanged?.Invoke();
            return true;
        }

        int nextSourceIndex = 1 - activeSourceIndex;
        AudioSource nextSource = sources[nextSourceIndex];
        ConfigureSource(nextSource, clip, 0f);
        nextSource.Play();
        transitionTargetSourceIndex = nextSourceIndex;
        transitionCoroutine = StartCoroutine(
            Crossfade(activeSourceIndex, nextSourceIndex, duration)
        );
        TrackChanged?.Invoke(currentKey);
        PlaybackStateChanged?.Invoke();
        return true;
    }

    public void Stop()
    {
        Stop(defaultFadeDuration);
    }

    public void Stop(float fadeDuration)
    {
        StopTransition();
        currentKey = BgmKey.None;
        isPaused = false;
        TrackChanged?.Invoke(currentKey);
        PlaybackStateChanged?.Invoke();

        AudioSource activeSource = GetActiveSource();
        if (activeSource == null || !activeSource.isPlaying || fadeDuration <= 0f)
        {
            StopAllSources();
            return;
        }

        transitionCoroutine = StartCoroutine(FadeOutAndStop(activeSource, fadeDuration));
    }

    public void Pause()
    {
        if (isPaused)
        {
            return;
        }

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].isPlaying)
            {
                sources[i].Pause();
            }
        }

        isPaused = true;
        PlaybackStateChanged?.Invoke();
    }

    public void Resume()
    {
        if (!isPaused)
        {
            return;
        }

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].clip != null)
            {
                sources[i].UnPause();
            }
        }

        isPaused = false;
        PlaybackStateChanged?.Invoke();
    }

    public void SetVolume(float newVolume)
    {
        GameSettings.SetMusicVolume(newVolume);
    }

    private void HandleSettingsChanged()
    {
        float clampedVolume = GameSettings.MusicVolume;
        if (Mathf.Approximately(volume, clampedVolume))
        {
            return;
        }

        volume = clampedVolume;
        if (transitionCoroutine == null)
        {
            AudioSource activeSource = GetActiveSource();
            if (activeSource != null)
            {
                activeSource.volume = volume;
            }
        }

        VolumeChanged?.Invoke(volume);
    }

    private void OnDestroy()
    {
        GameSettings.Changed -= HandleSettingsChanged;
        if (instance == this) instance = null;
    }

    public void SetLibrary(BgmLibrary newLibrary)
    {
        library = newLibrary;
    }

    public bool TryGetTrackDisplayInfo(BgmKey key, out BgmTrackDisplayInfo info)
    {
        if (library != null)
        {
            return library.TryGetDisplayInfo(key, out info);
        }

        info = null;
        return false;
    }

    public bool Seek(float seconds)
    {
        AudioSource source = GetCurrentPlaybackSource();
        if (source == null || source.clip == null)
        {
            return false;
        }

        float maximumTime = Mathf.Max(0f, source.clip.length - 0.001f);
        source.time = Mathf.Clamp(seconds, 0f, maximumTime);
        PlaybackStateChanged?.Invoke();
        return true;
    }

    public bool SeekNormalized(float normalizedTime)
    {
        AudioSource source = GetCurrentPlaybackSource();
        if (source == null || source.clip == null)
        {
            return false;
        }

        return Seek(Mathf.Clamp01(normalizedTime) * source.clip.length);
    }

    private IEnumerator Crossfade(int fromIndex, int toIndex, float duration)
    {
        AudioSource from = sources[fromIndex];
        AudioSource to = sources[toIndex];
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            from.volume = Mathf.Lerp(volume, 0f, progress);
            to.volume = Mathf.Lerp(0f, volume, progress);
            yield return null;
        }

        from.Stop();
        from.clip = null;
        from.volume = 0f;
        to.volume = volume;
        activeSourceIndex = toIndex;
        transitionTargetSourceIndex = -1;
        transitionCoroutine = null;
    }

    private IEnumerator FadeOutAndStop(AudioSource source, float duration)
    {
        float initialVolume = source.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(initialVolume, 0f, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        StopAllSources();
        transitionCoroutine = null;
    }

    private void EnsureSources()
    {
        AudioSource[] existingSources = GetComponents<AudioSource>();
        sources = new AudioSource[2];
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = i < existingSources.Length
                ? existingSources[i]
                : gameObject.AddComponent<AudioSource>();
        }

        for (int i = 0; i < sources.Length; i++)
        {
            sources[i].playOnAwake = false;
            sources[i].loop = loop;
            sources[i].spatialBlend = 0f;
            sources[i].volume = 0f;
        }
    }

    private void ConfigureSource(AudioSource source, AudioClip clip, float sourceVolume)
    {
        source.clip = clip;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.volume = sourceVolume;
    }

    private AudioSource GetActiveSource()
    {
        return sources != null && sources.Length > activeSourceIndex
            ? sources[activeSourceIndex]
            : null;
    }

    private AudioSource GetCurrentPlaybackSource()
    {
        if (sources == null)
        {
            return null;
        }

        if (transitionTargetSourceIndex >= 0
            && transitionTargetSourceIndex < sources.Length)
        {
            return sources[transitionTargetSourceIndex];
        }

        return GetActiveSource();
    }

    private void StopTransition()
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        if (sources != null
            && transitionTargetSourceIndex >= 0
            && transitionTargetSourceIndex < sources.Length)
        {
            int previousSourceIndex = 1 - transitionTargetSourceIndex;
            if (previousSourceIndex >= 0 && previousSourceIndex < sources.Length)
            {
                sources[previousSourceIndex].Stop();
                sources[previousSourceIndex].clip = null;
                sources[previousSourceIndex].volume = 0f;
            }

            activeSourceIndex = transitionTargetSourceIndex;
            sources[activeSourceIndex].volume = volume;
        }

        transitionTargetSourceIndex = -1;
    }

    private void StopAllSources()
    {
        if (sources == null)
        {
            return;
        }

        for (int i = 0; i < sources.Length; i++)
        {
            sources[i].Stop();
            sources[i].clip = null;
            sources[i].volume = 0f;
        }

        transitionTargetSourceIndex = -1;
    }
}
