using UnityEngine;

/// <summary>Instant health [0, HP] and delayed lost-health segment [HP, delayed HP].</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class PlayerHealthBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Player player;
    [SerializeField] private RectTransform health;
    [SerializeField] private RectTransform healthDelay;

    [Header("Damage delay")]
    [SerializeField, Min(0f)] private float holdDuration = 0.6f;
    [SerializeField, Min(0f)] private float catchUpDuration = 0.4f;
    [SerializeField] private AnimationCurve catchUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Keep the UI animation running when Game Over pauses combat.")]
    [SerializeField] private bool useUnscaledTime = true;

    private Player subscribedPlayer;
    private bool layoutCaptured;
    private float fullLeftAnchor;
    private float fullRightAnchor;
    private float fullLeftOffset;
    private float fullRightOffset;
    private float holdRemaining;
    private float catchUpElapsed;
    private float catchUpStartRatio;

    public Player BoundPlayer => player;
    public float HealthRatio { get; private set; } = 1f;
    public float DelayedRightRatio { get; private set; } = 1f;
    public bool IsAnimating => DelayedRightRatio > HealthRatio;

    private void OnEnable()
    {
        if (TryCaptureLayout())
            SnapTo(player != null ? NormalizedHealth(player.CurrentHealth, player.MaxHealth) : 1f);
        Subscribe();
    }

    private void OnDisable() => Unsubscribe();

    private void Update() => Advance(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

    /// <summary>MissionRuntime supplies the actual selected/spawned character, not a scene placeholder.</summary>
    public void BindPlayer(Player activePlayer)
    {
        Unsubscribe();
        player = activePlayer;
        if (TryCaptureLayout())
            SnapTo(player != null ? NormalizedHealth(player.CurrentHealth, player.MaxHealth) : 0f);
        Subscribe();
    }

    private void Subscribe()
    {
        if (!isActiveAndEnabled || player == null || subscribedPlayer == player)
            return;
        subscribedPlayer = player;
        subscribedPlayer.HealthChanged += HandleHealthChanged;
    }

    private void Unsubscribe()
    {
        if (subscribedPlayer != null)
            subscribedPlayer.HealthChanged -= HandleHealthChanged;
        subscribedPlayer = null;
    }

    private static float NormalizedHealth(int current, int maximum) =>
        maximum > 0 ? Mathf.Clamp01((float)current / maximum) : 0f;

    private void HandleHealthChanged(int current, int maximum)
    {
        if (!TryCaptureLayout())
            return;
        float next = NormalizedHealth(current, maximum);
        if (next >= HealthRatio)
        {
            // Healing hides the old lost-health segment; identical notifications do not restart a hold.
            if (next > HealthRatio)
                SnapTo(next);
            return;
        }

        HealthRatio = next;
        // On another hit only the LEFT edge moves. Preserve the right edge even mid-catch-up.
        holdRemaining = holdDuration;
        catchUpElapsed = 0f;
        catchUpStartRatio = DelayedRightRatio;
        ApplyGeometry();
    }

    public void Advance(float deltaTime)
    {
        if (!layoutCaptured || !IsAnimating || deltaTime <= 0f)
            return;
        float heldTime = Mathf.Min(deltaTime, holdRemaining);
        holdRemaining -= heldTime;
        deltaTime -= heldTime;
        if (deltaTime <= 0f)
            return;

        catchUpElapsed += deltaTime;
        if (catchUpDuration <= 0f || catchUpElapsed >= catchUpDuration)
            DelayedRightRatio = HealthRatio;
        else
        {
            float t = catchUpElapsed / catchUpDuration;
            float curved = catchUpCurve != null && catchUpCurve.length > 0 ? catchUpCurve.Evaluate(t) : t;
            float desired = Mathf.Lerp(catchUpStartRatio, HealthRatio, Mathf.Clamp01(curved));
            // Prevent curve overshoot or reversal from crossing the current HP or growing rightwards.
            DelayedRightRatio = Mathf.Clamp(desired, HealthRatio, DelayedRightRatio);
        }
        ApplyGeometry();
    }

    private void SnapTo(float ratio)
    {
        HealthRatio = DelayedRightRatio = ratio;
        holdRemaining = catchUpElapsed = 0f;
        catchUpStartRatio = ratio;
        ApplyGeometry();
    }

    private bool TryCaptureLayout()
    {
        if (health == null)
            health = transform.Find("health") as RectTransform;
        if (healthDelay == null)
            healthDelay = transform.Find("healthDelay") as RectTransform;
        if (health == null || healthDelay == null || health == healthDelay || health.parent != healthDelay.parent)
            return false;
        if (!layoutCaptured)
        {
            // Capture the authored full-health span once, including padding and fixed/stretch anchors.
            fullLeftAnchor = health.anchorMin.x;
            fullRightAnchor = health.anchorMax.x;
            fullLeftOffset = health.offsetMin.x;
            fullRightOffset = health.offsetMax.x;
            layoutCaptured = true;
        }
        return true;
    }

    private void ApplyGeometry()
    {
        SetSpan(health, 0f, HealthRatio);
        SetSpan(healthDelay, HealthRatio, DelayedRightRatio);
    }

    private void SetSpan(RectTransform rect, float left, float right)
    {
        if (rect == null)
            return;
        Vector2 anchorMin = rect.anchorMin;
        Vector2 anchorMax = rect.anchorMax;
        Vector2 offsetMin = rect.offsetMin;
        Vector2 offsetMax = rect.offsetMax;
        anchorMin.x = Mathf.Lerp(fullLeftAnchor, fullRightAnchor, left);
        anchorMax.x = Mathf.Lerp(fullLeftAnchor, fullRightAnchor, right);
        offsetMin.x = Mathf.Lerp(fullLeftOffset, fullRightOffset, left);
        offsetMax.x = Mathf.Lerp(fullLeftOffset, fullRightOffset, right);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private void OnValidate()
    {
        holdDuration = Mathf.Max(0f, holdDuration);
        catchUpDuration = Mathf.Max(0f, catchUpDuration);
    }
}
