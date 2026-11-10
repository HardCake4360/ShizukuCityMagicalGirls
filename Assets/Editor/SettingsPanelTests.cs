using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public sealed class SettingsPanelTests
{
    private const string Prefix = "Shizuku.Settings.";
    private static readonly string[] IntKeys = { "Width", "Height", "WindowMode", "TextureQuality", "MouseSensitivity" };
    private static readonly string[] FloatKeys = { "MasterVolume", "MusicVolume", "EffectVolume" };
    private readonly Dictionary<string, int> oldInts = new();
    private readonly Dictionary<string, float> oldFloats = new();
    private readonly List<GameObject> objects = new();
    private readonly List<SettingsPanelController> panels = new();
    private readonly List<SoundEffectVolumeBinding> bindings = new();
    private float oldListenerVolume;

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static void Call(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    private static void Reload() => typeof(GameSettings)
        .GetField("initialized", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);

    [SetUp]
    public void Setup()
    {
        oldListenerVolume = AudioListener.volume;
        foreach (string key in IntKeys)
        {
            if (PlayerPrefs.HasKey(Prefix + key)) oldInts[key] = PlayerPrefs.GetInt(Prefix + key);
            PlayerPrefs.DeleteKey(Prefix + key);
        }
        foreach (string key in FloatKeys)
        {
            if (PlayerPrefs.HasKey(Prefix + key)) oldFloats[key] = PlayerPrefs.GetFloat(Prefix + key);
            PlayerPrefs.DeleteKey(Prefix + key);
        }
        Reload();
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (SettingsPanelController panel in panels) if (panel != null) Call(panel, "OnDisable");
        foreach (SoundEffectVolumeBinding binding in bindings) if (binding != null) Call(binding, "OnDisable");
        foreach (GameObject item in objects) if (item != null) Object.DestroyImmediate(item);
        panels.Clear();
        bindings.Clear();
        objects.Clear();
        foreach (string key in IntKeys)
        {
            if (oldInts.TryGetValue(key, out int value)) PlayerPrefs.SetInt(Prefix + key, value);
            else PlayerPrefs.DeleteKey(Prefix + key);
        }
        foreach (string key in FloatKeys)
        {
            if (oldFloats.TryGetValue(key, out float value)) PlayerPrefs.SetFloat(Prefix + key, value);
            else PlayerPrefs.DeleteKey(Prefix + key);
        }
        oldInts.Clear();
        oldFloats.Clear();
        PlayerPrefs.Save();
        Reload();
        AudioListener.volume = oldListenerVolume;
    }

    private GameObject Create(string name, params System.Type[] components)
    {
        GameObject item = new(name, components);
        objects.Add(item);
        return item;
    }

    private SettingsPanelController CreatePanel()
    {
        GameObject item = Create("Options");
        item.SetActive(false);
        SettingsPanelController panel = item.AddComponent<SettingsPanelController>();
        panels.Add(panel);
        return panel;
    }

    [Test]
    public void DefaultsClampsAndAllSettingsSurviveReload()
    {
        Assert.That(GameSettings.MouseSensitivityPercent, Is.EqualTo(50));
        Assert.That(GameSettings.MouseSensitivityMultiplier, Is.EqualTo(1f));
        Assert.That(GameSettings.MasterVolume, Is.EqualTo(0.7f));
        Assert.That(GameSettings.MusicVolume, Is.EqualTo(0.7f));
        Assert.That(GameSettings.EffectVolume, Is.EqualTo(0.7f));
        Assert.That(AudioListener.volume, Is.EqualTo(0.7f));
        GameSettings.SetMouseSensitivity(0);
        Assert.That(GameSettings.MouseSensitivityMultiplier, Is.EqualTo(0.02f));
        GameSettings.SetMouseSensitivity(101);
        Assert.That(GameSettings.MouseSensitivityPercent, Is.EqualTo(100));
        GameSettings.SetMasterVolume(-1f);
        Assert.That(AudioListener.volume, Is.Zero);
        GameSettings.SetMusicVolume(2f);
        Assert.That(GameSettings.MusicVolume, Is.EqualTo(1f));
        GameSettings.SetMasterVolume(0.4f);
        GameSettings.SetMusicVolume(0.6f);
        GameSettings.SetEffectVolume(0.8f);
        GameSettings.SetMouseSensitivity(37);
        GameSettings.SetResolution(new SettingsResolution(2560, 1080));
        GameSettings.SetWindowMode(SettingsWindowMode.Borderless);
        GameSettings.SetTextureQuality(SettingsTextureQuality.Low);
        GameSettings.Save();
        Reload();
        Assert.That(GameSettings.Resolution.width, Is.EqualTo(2560));
        Assert.That(GameSettings.Resolution.height, Is.EqualTo(1080));
        Assert.That(GameSettings.WindowMode, Is.EqualTo(SettingsWindowMode.Borderless));
        Assert.That(GameSettings.TextureQuality, Is.EqualTo(SettingsTextureQuality.Low));
        Assert.That(GameSettings.MasterVolume, Is.EqualTo(0.4f));
        Assert.That(GameSettings.MusicVolume, Is.EqualTo(0.6f));
        Assert.That(GameSettings.EffectVolume, Is.EqualTo(0.8f));
        Assert.That(GameSettings.MouseSensitivityPercent, Is.EqualTo(37));
    }

    [Test]
    public void InvalidValuesDoNotCorruptResolutionOrAudio()
    {
        GameSettings.SetResolution(new SettingsResolution(1280, 720));
        GameSettings.SetResolution(new SettingsResolution(0, -3));
        Assert.That(GameSettings.Resolution.width, Is.EqualTo(1280));
        GameSettings.SetMusicVolume(0.6f);
        GameSettings.SetMusicVolume(float.NaN);
        GameSettings.SetMusicVolume(float.PositiveInfinity);
        Assert.That(GameSettings.MusicVolume, Is.EqualTo(0.6f));
    }

    [Test]
    public void VideoModesAndTextureQualityHaveSeparateEngineMappings()
    {
        Assert.That(GameSettings.ToFullScreenMode(SettingsWindowMode.Windowed), Is.EqualTo(FullScreenMode.Windowed));
        Assert.That(GameSettings.ToFullScreenMode(SettingsWindowMode.Fullscreen), Is.EqualTo(FullScreenMode.ExclusiveFullScreen));
        Assert.That(GameSettings.ToFullScreenMode(SettingsWindowMode.Borderless), Is.EqualTo(FullScreenMode.FullScreenWindow));
        Assert.That(GameSettings.GetTextureMipmapLimit(SettingsTextureQuality.Low), Is.EqualTo(2));
        Assert.That(GameSettings.GetTextureMipmapLimit(SettingsTextureQuality.Normal), Is.EqualTo(1));
        Assert.That(GameSettings.GetTextureMipmapLimit(SettingsTextureQuality.High), Is.Zero);
    }

    [Test]
    public void AssignedSlidersAndTextSynchronizeWithoutChangingOtherAudioChannels()
    {
        SettingsPanelController panel = CreatePanel();
        Slider slider = Create("Music", typeof(RectTransform), typeof(Slider)).GetComponent<Slider>();
        TMP_Text label = Create("Value", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        Slider sensitivity = Create("Sensitivity", typeof(RectTransform), typeof(Slider)).GetComponent<Slider>();
        Set(panel, "musicVolumeSlider", slider);
        Set(panel, "musicVolumeText", label);
        Set(panel, "sensitivitySlider", sensitivity);
        Call(panel, "OnEnable");
        Assert.That(slider.minValue, Is.Zero);
        Assert.That(slider.maxValue, Is.EqualTo(100));
        Assert.That(slider.value, Is.EqualTo(70));
        Assert.That(label.text, Is.EqualTo("70%"));
        Assert.That(sensitivity.minValue, Is.EqualTo(1));
        Assert.That(sensitivity.value, Is.EqualTo(50));
        slider.value = 35;
        Assert.That(GameSettings.MusicVolume, Is.EqualTo(0.35f).Within(0.0001f));
        Assert.That(label.text, Is.EqualTo("35%"));
        Assert.That(GameSettings.MasterVolume, Is.EqualTo(0.7f));
        Assert.That(GameSettings.EffectVolume, Is.EqualTo(0.7f));
        GameSettings.SetMusicVolume(0.7f);
        Assert.That(slider.value, Is.EqualTo(70));
        Assert.That(label.text, Is.EqualTo("70%"));
        Call(panel, "OnDisable");
        GameSettings.SetMusicVolume(0.8f);
        Assert.That(slider.value, Is.EqualTo(70));
        Call(panel, "OnEnable");
        Assert.That(slider.value, Is.EqualTo(80));
    }

    [Test]
    public void ButtonsWrapPresetsSkipInvalidValuesAndDoNotDuplicateAfterReopening()
    {
        SettingsPanelController panel = CreatePanel();
        Button next = Create("Next", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
        Set(panel, "nextResolutionButton", next);
        Set(panel, "resolutions", new List<SettingsResolution>
        {
            new(1280, 720), new(0, 0), new(1280, 720), new(1920, 1080)
        });
        GameSettings.SetResolution(new SettingsResolution(1280, 720));
        GameSettings.SetWindowMode(SettingsWindowMode.Windowed);
        Call(panel, "OnEnable");
        Call(panel, "OnDisable");
        Call(panel, "OnEnable");
        next.onClick.Invoke();
        Assert.That(GameSettings.Resolution.width, Is.EqualTo(1920));
        next.onClick.Invoke();
        Assert.That(GameSettings.Resolution.width, Is.EqualTo(1280));
        panel.PreviousResolution();
        Assert.That(GameSettings.Resolution.width, Is.EqualTo(1920));
        panel.PreviousWindowMode();
        Assert.That(GameSettings.WindowMode, Is.EqualTo(SettingsWindowMode.Borderless));
        panel.NextWindowMode();
        Assert.That(GameSettings.WindowMode, Is.EqualTo(SettingsWindowMode.Windowed));
        Set(panel, "resolutions", new List<SettingsResolution>());
        Call(panel, "OnDisable");
        Call(panel, "OnEnable");
        Assert.That(next.interactable, Is.False);
    }

    [Test]
    public void SoundEffectBindingPreservesRelativeSourceVolumeAndRespondsToSharedChanges()
    {
        GameObject item = Create("Effect", typeof(AudioSource));
        item.SetActive(false);
        AudioSource source = item.GetComponent<AudioSource>();
        source.volume = 0.4f;
        SoundEffectVolumeBinding binding = item.AddComponent<SoundEffectVolumeBinding>();
        bindings.Add(binding);
        Call(binding, "Awake");
        Call(binding, "OnEnable");
        GameSettings.SetEffectVolume(0.5f);
        Assert.That(source.volume, Is.EqualTo(0.2f).Within(0.0001f));
        GameSettings.SetMasterVolume(0.3f);
        Assert.That(AudioListener.volume, Is.EqualTo(0.3f));
        Assert.That(source.volume, Is.EqualTo(0.2f).Within(0.0001f));
        binding.SetBaseVolume(0.6f); // Disabled binding will apply this value on its next enable.
        Call(binding, "OnEnable");
        Assert.That(source.volume, Is.EqualTo(0.3f).Within(0.0001f));
        Call(binding, "OnDisable");
        Assert.That(source.volume, Is.EqualTo(0.6f).Within(0.0001f));
    }

    [Test]
    public void BgmVolumeSetterWritesTheSharedMusicSetting()
    {
        GameObject item = Create("BGM");
        item.SetActive(false);
        BgmManager manager = item.AddComponent<BgmManager>();
        manager.SetVolume(0.42f);
        Assert.That(GameSettings.MusicVolume, Is.EqualTo(0.42f));
        Call(manager, "HandleSettingsChanged");
        Assert.That(manager.Volume, Is.EqualTo(0.42f));
    }

    [Test]
    public void VirtualMouseSensitivityScalesDeltasWithoutRepeatedReadAccumulation()
    {
        MouseSensitivityPointer pointer = new();
        Rect bounds = new(0, 0, 100, 100);
        Assert.That(pointer.GetPosition(new Vector2(20, 20), 0.5f, bounds, 1), Is.EqualTo(new Vector2(20, 20)));
        Assert.That(pointer.GetPosition(new Vector2(40, 30), 0.5f, bounds, 2), Is.EqualTo(new Vector2(30, 25)));
        Assert.That(pointer.GetPosition(new Vector2(40, 30), 0.5f, bounds, 2), Is.EqualTo(new Vector2(30, 25)));
        Assert.That(pointer.GetPosition(new Vector2(60, 50), 1f, bounds, 3), Is.EqualTo(new Vector2(60, 50)));
        Assert.That(pointer.GetPosition(new Vector2(90, 50), 2f, bounds, 4), Is.EqualTo(new Vector2(100, 50)));
        pointer.Reset();
        Assert.That(pointer.GetPosition(new Vector2(20, 20), 2f, bounds, 5), Is.EqualTo(new Vector2(20, 20)));
    }

    [Test]
    public void HardwareDeltaStillMovesLowSensitivityAimAtPhysicalCursorEdgeOnlyOncePerFrame()
    {
        MouseSensitivityPointer pointer = new();
        Rect bounds = new(0, 0, 100, 100);
        pointer.GetPosition(new Vector2(80, 50), 0.5f, bounds, 1, Vector2.zero);
        Assert.That(pointer.GetPosition(new Vector2(100, 50), 0.5f, bounds, 2, new Vector2(20, 0)),
            Is.EqualTo(new Vector2(90, 50)));
        Assert.That(pointer.GetPosition(new Vector2(100, 50), 0.5f, bounds, 2, new Vector2(20, 0)),
            Is.EqualTo(new Vector2(90, 50)));
        Assert.That(pointer.GetPosition(new Vector2(100, 50), 0.5f, bounds, 3, new Vector2(20, 0)),
            Is.EqualTo(new Vector2(100, 50)));
    }

    private SettingsWindowController CreateWindow(out GameObject panel, out Button button)
    {
        GameObject host = Create("Always Active Window Controller");
        host.SetActive(false);
        SettingsWindowController window = host.AddComponent<SettingsWindowController>();
        panel = Create("Settings Root");
        button = Create("Settings Button", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
        Set(window, "settingsPanel", panel);
        Set(window, "settingsButton", button);
        return window;
    }

    [Test]
    public void SettingsButtonTogglesClosedPanelAndListenersDoNotDuplicateAfterReenable()
    {
        SettingsWindowController window = CreateWindow(out GameObject panel, out Button button);
        Call(window, "Awake");
        Assert.That(panel.activeSelf, Is.False);
        Call(window, "OnEnable");
        button.onClick.Invoke();
        Assert.That(window.IsOpen, Is.True);
        button.onClick.Invoke();
        Assert.That(window.IsOpen, Is.False);
        Call(window, "OnDisable");
        Call(window, "OnEnable");
        Call(window, "OnEnable");
        button.onClick.Invoke();
        Assert.That(window.IsOpen, Is.True);
        Call(window, "OnDisable");
        Assert.That(window.IsOpen, Is.False);
        button.onClick.Invoke();
        Assert.That(window.IsOpen, Is.False);
    }

    [Test]
    public void EscapeTogglesOnNewPressNotHoldOrReleaseEvenWhilePanelIsInactive()
    {
        SettingsWindowController window = CreateWindow(out GameObject panel, out _);
        Call(window, "Awake");
        Call(window, "OnEnable");
        InputAction action = (InputAction)typeof(SettingsWindowController)
            .GetField("toggleAction", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.SetDeviceUsage(keyboard, "SettingsWindowTest");
        action.ApplyBindingOverride("<Keyboard>{SettingsWindowTest}/escape");
        try
        {
            Assert.That(panel.activeSelf, Is.False);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            Assert.That(window.IsOpen, Is.True);
            InputSystem.Update();
            Assert.That(window.IsOpen, Is.True);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Assert.That(window.IsOpen, Is.True);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            Assert.That(window.IsOpen, Is.False);
            Set(window, "toggleWithEscape", false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            Assert.That(window.IsOpen, Is.False);
        }
        finally
        {
            Call(window, "OnDisable");
            InputSystem.RemoveDevice(keyboard);
        }
    }

    [Test]
    public void WindowControllerRejectsPanelContainingItsOwnInputListener()
    {
        SettingsWindowController window = CreateWindow(out GameObject panel, out _);
        window.transform.SetParent(panel.transform);
        LogAssert.Expect(LogType.Error,
            "SettingsWindowController must be placed outside the settings panel on an always-active object.");
        Call(window, "Awake");
        window.Close();
        Assert.That(panel.activeSelf, Is.True);
    }
}
