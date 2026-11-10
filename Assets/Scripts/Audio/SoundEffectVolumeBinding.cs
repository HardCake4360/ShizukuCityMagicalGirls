using UnityEngine;

/// <summary>Apply the shared effect volume without losing this source's authored relative loudness.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class SoundEffectVolumeBinding : MonoBehaviour
{
    [SerializeField] private AudioSource source;
    [SerializeField] private bool useInitialSourceVolume = true;
    [SerializeField, Range(0f, 1f)] private float baseVolume = 1f;

    private void Awake()
    {
        if (source == null) source = GetComponent<AudioSource>();
        if (useInitialSourceVolume && source != null) baseVolume = source.volume;
    }
    private void OnEnable()
    {
        if (source == null) source = GetComponent<AudioSource>();
        GameSettings.Changed -= ApplyVolume;
        GameSettings.Changed += ApplyVolume;
        ApplyVolume();
    }
    private void OnDisable()
    {
        GameSettings.Changed -= ApplyVolume;
        if (source != null) source.volume = baseVolume;
    }
    public void SetBaseVolume(float value) { baseVolume = Mathf.Clamp01(value); if (isActiveAndEnabled) ApplyVolume(); }
    private void ApplyVolume() { if (source != null) source.volume = baseVolume * GameSettings.EffectVolume; }
}
