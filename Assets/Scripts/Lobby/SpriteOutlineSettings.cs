using System;
using UnityEngine;

[Serializable]
public sealed class SpriteOutlineSettings
{
    public const float MaximumWidthPixels = 12f;

    [SerializeField] private Color color = Color.white;
    [SerializeField, Range(0f, MaximumWidthPixels)] private float widthPixels = 1f;
    [SerializeField, Min(0f)] private float transitionDuration = 0.15f;
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public Color Color => color;

    public float WidthPixels => Mathf.Clamp(widthPixels, 0f, MaximumWidthPixels);

    public float TransitionDuration => Mathf.Max(0f, transitionDuration);

    public AnimationCurve TransitionCurve => transitionCurve;
}

public interface IInteractableFeedbackSettingsReceiver
{
    void ApplyFeedbackSettings(SpriteOutlineSettings settings);
}
