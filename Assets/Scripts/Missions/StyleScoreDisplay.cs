using UnityEngine;
using UnityEngine.UI;

/// <summary>Sprite-based style rank HUD. Gauge changes within one rank do not restart its pulse.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class StyleScoreDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CombatScoreManager scoreManager;
    [SerializeField] private Image rankImage;
    [Tooltip("Defaults to the rank Image's RectTransform.")]
    [SerializeField] private RectTransform pulseTarget;

    [Header("Rank sprites")]
    [SerializeField] private Sprite dSprite;
    [SerializeField] private Sprite dPlusSprite;
    [SerializeField] private Sprite cSprite;
    [SerializeField] private Sprite cPlusSprite;
    [SerializeField] private Sprite bSprite;
    [SerializeField] private Sprite bPlusSprite;
    [SerializeField] private Sprite aSprite;
    [SerializeField] private Sprite aPlusSprite;
    [SerializeField] private Sprite sSprite;
    [SerializeField] private Sprite sPlusSprite;

    [Header("Rank-change pulse")]
    [SerializeField, Min(1f)] private float pulseScale = 1.2f;
    [SerializeField, Min(0f)] private float pulseDuration = 0.3f;
    [SerializeField] private AnimationCurve pulseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Hide below D")]
    [Tooltip("Fade the last visible rank to zero alpha when style falls below D. Initial unranked state starts hidden.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.3f;

    private CombatScoreManager subscribedManager;
    private bool appearanceCaptured;
    private bool hasDisplayedRank;
    private Sprite originalSprite;
    private Color originalColor;
    private Vector3 originalScale;
    private float pulseElapsed;
    private float fadeElapsed;
    private float fadeStartVisibility;

    public CombatStyleRank DisplayedRank { get; private set; } = CombatStyleRank.None;
    public bool IsPulsing { get; private set; }
    public bool IsFading { get; private set; }
    public float Visibility { get; private set; } = 1f;
    public CombatScoreManager BoundScoreManager => scoreManager;

    private void OnEnable()
    {
        CaptureAppearance();
        Subscribe();
        RefreshDisplay();
    }

    private void OnDisable()
    {
        Unsubscribe();
        StopPulse();
        IsFading = false;
        if (appearanceCaptured && rankImage != null)
            rankImage.color = originalColor;
        hasDisplayedRank = false;
    }

    private void Update()
    {
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        AdvancePulse(deltaTime);
        AdvanceVisibility(deltaTime);
    }

    public void BindScoreManager(CombatScoreManager source)
    {
        Unsubscribe();
        StopPulse();
        IsFading = false;
        scoreManager = source;
        hasDisplayedRank = false;
        CaptureAppearance();
        Subscribe();
        if (isActiveAndEnabled)
            RefreshDisplay();
    }

    private void Subscribe()
    {
        if (!isActiveAndEnabled || scoreManager == null || subscribedManager == scoreManager)
            return;
        Unsubscribe();
        subscribedManager = scoreManager;
        subscribedManager.Changed += RefreshDisplay;
    }

    private void Unsubscribe()
    {
        if (subscribedManager != null)
            subscribedManager.Changed -= RefreshDisplay;
        subscribedManager = null;
    }

    private void CaptureAppearance()
    {
        if (rankImage == null)
            rankImage = GetComponent<Image>();
        if (pulseTarget == null && rankImage != null)
            pulseTarget = rankImage.rectTransform;
        if (appearanceCaptured || rankImage == null || pulseTarget == null)
            return;
        originalSprite = rankImage.sprite;
        originalColor = rankImage.color;
        originalScale = pulseTarget.localScale;
        appearanceCaptured = true;
    }

    public void RefreshDisplay()
    {
        if (!isActiveAndEnabled || !appearanceCaptured || rankImage == null)
            return;
        CombatStyleRank rank = scoreManager != null ? scoreManager.Rank : CombatStyleRank.None;
        bool firstDisplay = !hasDisplayedRank;
        bool changed = hasDisplayedRank && DisplayedRank != rank;
        if (firstDisplay || changed)
        {
            DisplayedRank = rank;
            hasDisplayedRank = true;
            if (rank >= CombatStyleRank.D)
            {
                Sprite sprite = SpriteFor(rank);
                // An unassigned rank uses the authored sprite, not a stale sprite from another rank.
                rankImage.sprite = sprite != null ? sprite : originalSprite;
                IsFading = false;
                SetVisibility(1f);
                if (changed)
                    StartPulse();
            }
            else
            {
                // Keep the last visible sprite while it fades; None has no sprite or pulse.
                StopPulse();
                if (firstDisplay || fadeOutDuration <= 0f || Visibility <= 0f)
                {
                    IsFading = false;
                    SetVisibility(0f);
                }
                else
                {
                    fadeElapsed = 0f;
                    fadeStartVisibility = Visibility;
                    IsFading = true;
                }
            }
        }
    }

    public void AdvanceVisibility(float deltaTime)
    {
        if (!IsFading || deltaTime <= 0f)
            return;
        fadeElapsed += deltaTime;
        if (fadeOutDuration <= 0f || fadeElapsed >= fadeOutDuration)
        {
            IsFading = false;
            SetVisibility(0f);
            return;
        }
        float weight = Mathf.SmoothStep(0f, 1f, fadeElapsed / fadeOutDuration);
        SetVisibility(Mathf.Lerp(fadeStartVisibility, 0f, weight));
    }

    private void SetVisibility(float value)
    {
        Visibility = Mathf.Clamp01(value);
        if (rankImage == null)
            return;
        Color color = originalColor;
        color.a *= Visibility;
        rankImage.color = color;
    }

    private Sprite SpriteFor(CombatStyleRank rank) => rank switch
    {
        CombatStyleRank.D => dSprite,
        CombatStyleRank.DPlus => dPlusSprite,
        CombatStyleRank.C => cSprite,
        CombatStyleRank.CPlus => cPlusSprite,
        CombatStyleRank.B => bSprite,
        CombatStyleRank.BPlus => bPlusSprite,
        CombatStyleRank.A => aSprite,
        CombatStyleRank.APlus => aPlusSprite,
        CombatStyleRank.S => sSprite,
        CombatStyleRank.SPlus => sPlusSprite,
        _ => null
    };

    private void StartPulse()
    {
        // Always use the original scale; rapid rank changes cannot compound the scale multiplier.
        StopPulse();
        pulseElapsed = 0f;
        IsPulsing = pulseTarget != null && pulseDuration > 0f && pulseScale > 1f;
    }

    public void AdvancePulse(float deltaTime)
    {
        if (!IsPulsing || deltaTime <= 0f)
            return;
        if (pulseTarget == null)
        {
            IsPulsing = false;
            return;
        }
        pulseElapsed += deltaTime;
        if (pulseDuration <= 0f || pulseElapsed >= pulseDuration)
        {
            StopPulse();
            return;
        }
        float weight = 1f - Mathf.Abs(2f * pulseElapsed / pulseDuration - 1f);
        float curved = pulseCurve != null && pulseCurve.length > 0 ? pulseCurve.Evaluate(weight) : weight;
        pulseTarget.localScale = originalScale * Mathf.Lerp(1f, pulseScale, Mathf.Clamp01(curved));
    }

    private void StopPulse()
    {
        IsPulsing = false;
        if (appearanceCaptured && pulseTarget != null)
            pulseTarget.localScale = originalScale;
    }

    private void OnValidate()
    {
        pulseScale = Mathf.Max(1f, pulseScale);
        pulseDuration = Mathf.Max(0f, pulseDuration);
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
    }
}
