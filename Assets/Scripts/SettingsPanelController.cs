using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SettingsPanelController : MonoBehaviour
{
    [Header("Resolution presets (cycle in list order)")]
    [SerializeField] private List<SettingsResolution> resolutions = new()
    {
        new(1280, 720), new(1600, 900), new(1920, 1080),
        new(2560, 1080), new(2560, 1440), new(3840, 2160)
    };
    [SerializeField] private Button previousResolutionButton;
    [SerializeField] private Button nextResolutionButton;
    [SerializeField] private TMP_Text resolutionText;

    [Header("Window mode: Windowed / Fullscreen / Borderless")]
    [SerializeField] private Button previousWindowModeButton;
    [SerializeField] private Button nextWindowModeButton;
    [SerializeField] private TMP_Text windowModeText;
    [SerializeField] private string windowedLabel = "창모드";
    [SerializeField] private string fullscreenLabel = "전체화면";
    [SerializeField] private string borderlessLabel = "테두리없는 창모드";

    [Header("Texture quality: Low / Normal / High")]
    [SerializeField] private Button previousQualityButton;
    [SerializeField] private Button nextQualityButton;
    [SerializeField] private TMP_Text qualityText;
    [SerializeField] private string lowLabel = "낮음";
    [SerializeField] private string normalLabel = "보통";
    [SerializeField] private string highLabel = "높음";

    [Header("Audio (sliders automatically use 0 - 100%)")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private TMP_Text masterVolumeText;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private TMP_Text musicVolumeText;
    [SerializeField] private Slider effectVolumeSlider;
    [SerializeField] private TMP_Text effectVolumeText;

    [Header("Mouse sensitivity (1 - 100%, 50% = unchanged)")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_Text sensitivityText;

    private readonly List<SettingsResolution> usableResolutions = new();

    private void OnEnable()
    {
        BindListeners(false);
        GameSettings.Changed -= RefreshUI;
        usableResolutions.Clear();
        if (resolutions != null)
            foreach (SettingsResolution resolution in resolutions)
                if (resolution.IsValid && !usableResolutions.Exists(item =>
                        item.width == resolution.width && item.height == resolution.height))
                    usableResolutions.Add(resolution);

        ConfigureSlider(masterVolumeSlider, 0f);
        ConfigureSlider(musicVolumeSlider, 0f);
        ConfigureSlider(effectVolumeSlider, 0f);
        ConfigureSlider(sensitivitySlider, 1f);
        BindListeners(true);
        GameSettings.Changed += RefreshUI;
        RefreshUI();
    }

    private void OnDisable()
    {
        GameSettings.Changed -= RefreshUI;
        BindListeners(false);
        GameSettings.Save();
    }

    private void OnApplicationPause(bool paused) { if (paused) GameSettings.Save(); }

    private static void ConfigureSlider(Slider slider, float minimum)
    {
        if (slider == null) return;
        slider.minValue = minimum;
        slider.maxValue = 100f;
        slider.wholeNumbers = true;
    }

    private void BindListeners(bool add)
    {
        Bind(previousResolutionButton, PreviousResolution, add);
        Bind(nextResolutionButton, NextResolution, add);
        Bind(previousWindowModeButton, PreviousWindowMode, add);
        Bind(nextWindowModeButton, NextWindowMode, add);
        Bind(previousQualityButton, PreviousQuality, add);
        Bind(nextQualityButton, NextQuality, add);
        Bind(masterVolumeSlider, SetMasterVolumePercent, add);
        Bind(musicVolumeSlider, SetMusicVolumePercent, add);
        Bind(effectVolumeSlider, SetEffectVolumePercent, add);
        Bind(sensitivitySlider, SetSensitivityPercent, add);
    }

    private static void Bind(Button button, UnityAction action, bool add)
    {
        if (button == null) return;
        if (add) button.onClick.AddListener(action);
        else button.onClick.RemoveListener(action);
    }

    private static void Bind(Slider slider, UnityAction<float> action, bool add)
    {
        if (slider == null) return;
        if (add) slider.onValueChanged.AddListener(action);
        else slider.onValueChanged.RemoveListener(action);
    }

    public void PreviousResolution() => CycleResolution(-1);
    public void NextResolution() => CycleResolution(1);
    private void CycleResolution(int step)
    {
        if (usableResolutions.Count == 0) return;
        SettingsResolution current = GameSettings.Resolution;
        int index = usableResolutions.FindIndex(item => item.width == current.width && item.height == current.height);
        if (index < 0) index = step > 0 ? 0 : usableResolutions.Count - 1;
        else index = (index + step + usableResolutions.Count) % usableResolutions.Count;
        GameSettings.SetResolution(usableResolutions[index]);
    }

    public void PreviousWindowMode() => CycleWindowMode(-1);
    public void NextWindowMode() => CycleWindowMode(1);
    private static void CycleWindowMode(int step) => GameSettings.SetWindowMode(
        (SettingsWindowMode)(((int)GameSettings.WindowMode + step + 3) % 3));
    public void PreviousQuality() => CycleQuality(-1);
    public void NextQuality() => CycleQuality(1);
    private static void CycleQuality(int step) => GameSettings.SetTextureQuality(
        (SettingsTextureQuality)(((int)GameSettings.TextureQuality + step + 3) % 3));

    public void SetMasterVolumePercent(float percent) => GameSettings.SetMasterVolume(percent / 100f);
    public void SetMusicVolumePercent(float percent) => GameSettings.SetMusicVolume(percent / 100f);
    public void SetEffectVolumePercent(float percent) => GameSettings.SetEffectVolume(percent / 100f);
    public void SetSensitivityPercent(float percent) => GameSettings.SetMouseSensitivity(Mathf.RoundToInt(percent));

    public void RefreshUI()
    {
        SetText(resolutionText, GameSettings.Resolution.ToString());
        SetText(windowModeText, GameSettings.WindowMode switch
        {
            SettingsWindowMode.Windowed => windowedLabel,
            SettingsWindowMode.Fullscreen => fullscreenLabel,
            _ => borderlessLabel
        });
        SetText(qualityText, GameSettings.TextureQuality switch
        {
            SettingsTextureQuality.Low => lowLabel,
            SettingsTextureQuality.Normal => normalLabel,
            _ => highLabel
        });
        SettingsResolution current = GameSettings.Resolution;
        bool canChooseResolution = usableResolutions.Count > 0 && (usableResolutions.Count > 1
            || usableResolutions[0].width != current.width || usableResolutions[0].height != current.height);
        if (previousResolutionButton != null) previousResolutionButton.interactable = canChooseResolution;
        if (nextResolutionButton != null) nextResolutionButton.interactable = canChooseResolution;
        SetPercent(masterVolumeSlider, masterVolumeText, GameSettings.MasterVolume * 100f);
        SetPercent(musicVolumeSlider, musicVolumeText, GameSettings.MusicVolume * 100f);
        SetPercent(effectVolumeSlider, effectVolumeText, GameSettings.EffectVolume * 100f);
        SetPercent(sensitivitySlider, sensitivityText, GameSettings.MouseSensitivityPercent);
    }

    private static void SetPercent(Slider slider, TMP_Text text, float percent)
    {
        if (slider != null) slider.SetValueWithoutNotify(percent);
        SetText(text, $"{Mathf.RoundToInt(percent)}%");
    }
    private static void SetText(TMP_Text text, string value) { if (text != null) text.text = value; }
}
