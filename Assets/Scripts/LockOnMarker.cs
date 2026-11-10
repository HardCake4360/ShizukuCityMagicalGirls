using UnityEngine;

[System.Serializable]
public class LockOnMarkerVisualSettings
{
    public Sprite markerSprite;
    public Vector3 worldOffset;
    public float initialScale = 0.5f;
    public float pulseScale = 1.2f;
    public float restingScale = 1f;
    public float pulseDuration = 0.18f;
    public Color markerColor = Color.green;
    public string sortingLayerName;
    public int sortingOrder = 32760;
}

public class LockOnMarker : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private SpriteRenderer markerRenderer;
    [SerializeField] private LockOnMarkerVisualSettings settings = new LockOnMarkerVisualSettings();

    private float pulseTimer;
    private float pulseStartScale;
    private bool pulseActive;

    private void Awake()
    {
        ValidateSettings();
        EnsureVisualComponents();
        ApplyVisualSettings();
    }

    private void OnValidate()
    {
        ValidateSettings();
    }

    private void Update()
    {
        FollowTarget();
        UpdatePulse();
    }

    public void Configure(Transform newTarget, LockOnMarkerVisualSettings visualSettings)
    {
        target = newTarget;
        if (visualSettings != null)
        {
            settings = visualSettings;
        }

        ValidateSettings();
        EnsureVisualComponents();
        ApplyVisualSettings();
        PlayInitialPulse();
        FollowTarget();
    }

    public void PlayLockPulse()
    {
        EnsureVisualComponents();
        pulseStartScale = transform.localScale.x;
        pulseTimer = 0f;
        pulseActive = true;
    }

    public void PlayInitialPulse()
    {
        EnsureVisualComponents();
        pulseStartScale = settings.initialScale;
        transform.localScale = Vector3.one * settings.initialScale;
        pulseTimer = 0f;
        pulseActive = true;
    }

    private void FollowTarget()
    {
        if (target == null)
        {
            return;
        }

        transform.position = target.position + settings.worldOffset;
    }

    private void UpdatePulse()
    {
        if (!pulseActive)
        {
            return;
        }

        float duration = Mathf.Max(0.001f, settings.pulseDuration);
        pulseTimer += Time.deltaTime;
        float t = Mathf.Clamp01(pulseTimer / duration);
        float scale = t < 0.5f
            ? Mathf.Lerp(pulseStartScale, settings.pulseScale, t * 2f)
            : Mathf.Lerp(settings.pulseScale, settings.restingScale, (t - 0.5f) * 2f);

        transform.localScale = Vector3.one * scale;

        if (t >= 1f)
        {
            pulseActive = false;
            transform.localScale = Vector3.one * settings.restingScale;
        }
    }

    private void EnsureVisualComponents()
    {
        if (markerRenderer == null)
        {
            markerRenderer = GetComponent<SpriteRenderer>();
        }

        if (markerRenderer == null)
        {
            markerRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
    }

    private void ApplyVisualSettings()
    {
        if (markerRenderer != null)
        {
            if (settings.markerSprite != null)
            {
                markerRenderer.sprite = settings.markerSprite;
            }

            markerRenderer.color = settings.markerColor;
            ApplySorting(markerRenderer);
        }
    }

    private void ApplySorting(Renderer targetRenderer, int orderOffset = 0)
    {
        if (targetRenderer == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.sortingLayerName))
        {
            targetRenderer.sortingLayerName = settings.sortingLayerName;
        }

        targetRenderer.sortingOrder = settings.sortingOrder + orderOffset;
    }

    private void ValidateSettings()
    {
        if (settings == null)
        {
            settings = new LockOnMarkerVisualSettings();
        }

        settings.initialScale = Mathf.Max(0f, settings.initialScale);
        settings.pulseScale = Mathf.Max(0f, settings.pulseScale);
        settings.restingScale = Mathf.Max(0f, settings.restingScale);
        settings.pulseDuration = Mathf.Max(0.001f, settings.pulseDuration);
        settings.sortingOrder = Mathf.Clamp(settings.sortingOrder, short.MinValue, short.MaxValue);
    }
}
