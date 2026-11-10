using UnityEngine;

public class SpriteAnimationParticle : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private float fallbackLifetime = 0.5f;
    [SerializeField] private bool destroyOnComplete = true;

    [Header("Render Order")]
    [SerializeField] private bool forceRenderOnTop = true;
    [SerializeField] private bool useTopSortingLayer = true;
    [SerializeField] private string sortingLayerName;
    [SerializeField] private int sortingOrder = short.MaxValue;
    [SerializeField] private bool includeChildRenderers = true;

    private SpriteRenderer[] spriteRenderers;
    private float lifeTimer;

    private void Awake()
    {
        CacheReferences();
        ApplyRenderOrder();
    }

    private void OnEnable()
    {
        Restart();
    }

    private void OnValidate()
    {
        fallbackLifetime = Mathf.Max(0.01f, fallbackLifetime);
        sortingOrder = Mathf.Clamp(sortingOrder, short.MinValue, short.MaxValue);
    }

    private void Update()
    {
        lifeTimer -= Time.deltaTime;
        if (lifeTimer > 0f)
        {
            return;
        }

        if (destroyOnComplete)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    public void Restart()
    {
        CacheReferences();
        ApplyRenderOrder();
        lifeTimer = GetAnimationLifetime();

        if (animator != null)
        {
            animator.Play(0, 0, 0f);
        }
    }

    private void CacheReferences()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        spriteRenderers = includeChildRenderers
            ? GetComponentsInChildren<SpriteRenderer>(true)
            : GetComponents<SpriteRenderer>();
    }

    private void ApplyRenderOrder()
    {
        if (!forceRenderOnTop || spriteRenderers == null)
        {
            return;
        }

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = spriteRenderers[i];
            if (spriteRenderer == null)
            {
                continue;
            }

            ApplySortingLayer(spriteRenderer);
            spriteRenderer.sortingOrder = sortingOrder;
        }
    }

    private void ApplySortingLayer(SpriteRenderer spriteRenderer)
    {
        if (!string.IsNullOrWhiteSpace(sortingLayerName))
        {
            spriteRenderer.sortingLayerName = sortingLayerName;
            return;
        }

        if (!useTopSortingLayer)
        {
            return;
        }

        SortingLayer[] sortingLayers = SortingLayer.layers;
        if (sortingLayers == null || sortingLayers.Length == 0)
        {
            return;
        }

        spriteRenderer.sortingLayerID = sortingLayers[sortingLayers.Length - 1].id;
    }

    private float GetAnimationLifetime()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return fallbackLifetime;
        }

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null || clips.Length == 0 || clips[0] == null)
        {
            return fallbackLifetime;
        }

        return Mathf.Max(0.01f, clips[0].length);
    }
}
