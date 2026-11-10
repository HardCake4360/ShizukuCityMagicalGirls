using System;
using UnityEngine;

public enum SettingsWindowMode { Windowed, Fullscreen, Borderless }
public enum SettingsTextureQuality { Low, Normal, High }

[Serializable]
public struct SettingsResolution
{
    [Min(1)] public int width;
    [Min(1)] public int height;

    public SettingsResolution(int width, int height) { this.width = width; this.height = height; }
    public bool IsValid => width > 0 && height > 0;
    public override string ToString() => $"{width} x {height}";
}

/// <summary>Shared settings authority. Does not create UI or scene objects.</summary>
public static class GameSettings
{
    private const string Prefix = "Shizuku.Settings.";
    private const float DefaultAudioVolume = 0.7f;
    private static bool initialized;
    private static SettingsResolution resolution;
    private static SettingsWindowMode windowMode;
    private static SettingsTextureQuality textureQuality;
    private static float masterVolume, musicVolume, effectVolume;
    private static int sensitivityPercent;

    public static event Action Changed;
    public static SettingsResolution Resolution { get { Initialize(); return resolution; } }
    public static SettingsWindowMode WindowMode { get { Initialize(); return windowMode; } }
    public static SettingsTextureQuality TextureQuality { get { Initialize(); return textureQuality; } }
    public static float MasterVolume { get { Initialize(); return masterVolume; } }
    public static float MusicVolume { get { Initialize(); return musicVolume; } }
    public static float EffectVolume { get { Initialize(); return effectVolume; } }
    public static int MouseSensitivityPercent { get { Initialize(); return sensitivityPercent; } }
    public static float MouseSensitivityMultiplier => MouseSensitivityPercent / 50f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        initialized = false;
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        resolution = new SettingsResolution(
            Mathf.Max(1, PlayerPrefs.GetInt(Prefix + "Width", Mathf.Max(1, Screen.width))),
            Mathf.Max(1, PlayerPrefs.GetInt(Prefix + "Height", Mathf.Max(1, Screen.height))));
        SettingsWindowMode currentMode = Screen.fullScreenMode == FullScreenMode.Windowed
            ? SettingsWindowMode.Windowed : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen
                ? SettingsWindowMode.Fullscreen : SettingsWindowMode.Borderless;
        windowMode = (SettingsWindowMode)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "WindowMode", (int)currentMode), 0, 2);
        textureQuality = (SettingsTextureQuality)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "TextureQuality", 2), 0, 2);
        masterVolume = ReadVolume("MasterVolume");
        musicVolume = ReadVolume("MusicVolume");
        effectVolume = ReadVolume("EffectVolume");
        sensitivityPercent = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "MouseSensitivity", 50), 1, 100);
        AudioListener.volume = masterVolume;
        ApplyTextureQuality();
        // Keep the build's initial window configuration unless video settings were explicitly saved.
        if (PlayerPrefs.HasKey(Prefix + "Width") || PlayerPrefs.HasKey(Prefix + "WindowMode")) ApplyVideo();
        Application.quitting -= Save;
        Application.quitting += Save;
    }

    private static float ReadVolume(string key)
    {
        float value = PlayerPrefs.GetFloat(Prefix + key, DefaultAudioVolume);
        return float.IsNaN(value) || float.IsInfinity(value) ? DefaultAudioVolume : Mathf.Clamp01(value);
    }

    public static void SetResolution(SettingsResolution value)
    {
        Initialize();
        if (!value.IsValid || (resolution.width == value.width && resolution.height == value.height)) return;
        resolution = value;
        SaveVideoValues();
        ApplyVideo();
        Changed?.Invoke();
    }

    public static void SetWindowMode(SettingsWindowMode value)
    {
        Initialize();
        value = (SettingsWindowMode)Mathf.Clamp((int)value, 0, 2);
        if (windowMode == value) return;
        windowMode = value;
        SaveVideoValues();
        ApplyVideo();
        Changed?.Invoke();
    }

    private static void SaveVideoValues()
    {
        PlayerPrefs.SetInt(Prefix + "Width", resolution.width);
        PlayerPrefs.SetInt(Prefix + "Height", resolution.height);
        PlayerPrefs.SetInt(Prefix + "WindowMode", (int)windowMode);
    }

    private static void ApplyVideo()
    {
        // Screen.SetResolution changes the standalone window, not the editor's Game view.
        if (Application.isPlaying && !Application.isEditor)
            Screen.SetResolution(resolution.width, resolution.height, ToFullScreenMode(windowMode));
    }

    public static FullScreenMode ToFullScreenMode(SettingsWindowMode value) => value switch
    {
        SettingsWindowMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
        SettingsWindowMode.Borderless => FullScreenMode.FullScreenWindow,
        _ => FullScreenMode.Windowed
    };

    public static int GetTextureMipmapLimit(SettingsTextureQuality value) => value switch
    {
        SettingsTextureQuality.Low => 2,
        SettingsTextureQuality.Normal => 1,
        _ => 0
    };

    public static void SetTextureQuality(SettingsTextureQuality value)
    {
        Initialize();
        value = (SettingsTextureQuality)Mathf.Clamp((int)value, 0, 2);
        if (textureQuality == value) return;
        textureQuality = value;
        PlayerPrefs.SetInt(Prefix + "TextureQuality", (int)value);
        ApplyTextureQuality();
        Changed?.Invoke();
    }

    private static void ApplyTextureQuality()
    {
        if (Application.isPlaying)
            QualitySettings.globalTextureMipmapLimit = GetTextureMipmapLimit(textureQuality);
    }

    public static void SetMasterVolume(float value)
    {
        Initialize();
        if (!SetVolume(ref masterVolume, value, "MasterVolume")) return;
        AudioListener.volume = masterVolume;
        Changed?.Invoke();
    }

    public static void SetMusicVolume(float value)
    {
        Initialize();
        if (SetVolume(ref musicVolume, value, "MusicVolume")) Changed?.Invoke();
    }

    public static void SetEffectVolume(float value)
    {
        Initialize();
        if (SetVolume(ref effectVolume, value, "EffectVolume")) Changed?.Invoke();
    }

    private static bool SetVolume(ref float current, float value, string key)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return false;
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(current, value)) return false;
        current = value;
        PlayerPrefs.SetFloat(Prefix + key, value);
        return true;
    }

    public static void SetMouseSensitivity(int percent)
    {
        Initialize();
        percent = Mathf.Clamp(percent, 1, 100);
        if (sensitivityPercent == percent) return;
        sensitivityPercent = percent;
        PlayerPrefs.SetInt(Prefix + "MouseSensitivity", percent);
        Changed?.Invoke();
    }

    // Sliders update PlayerPrefs in memory; flush on panel close or application quit, not each drag tick.
    public static void Save() => PlayerPrefs.Save();
}
