using System.Collections.Generic;
using UnityEngine;

public class HitscanBeamVisual : MonoBehaviour
{
    private const int HardMaxSegments = 512;

    [Header("Sprites")]
    [SerializeField] private SpriteRenderer tipRenderer;
    [SerializeField] private SpriteRenderer midSegmentPrefab;
    [SerializeField] private Transform segmentRoot;

    [Header("Layout")]
    [SerializeField] private float tipOffset;
    [SerializeField] private float midFirstSegmentOffset = 0.25f;
    [SerializeField] private float midSegmentSpacing = 0.25f;
    [SerializeField] private int maxSegments = 64;

    [Header("Scale")]
    [SerializeField] private float spriteAngleOffset;
    [SerializeField] private bool hideWhenWidthIsZero = true;

    private readonly List<SpriteRenderer> midSegments = new List<SpriteRenderer>();
    private Vector3 tipBaseScale = Vector3.one;
    private Vector3 midBaseScale = Vector3.one;
    private bool baseScalesCached;

    private void Awake()
    {
        CacheBaseScales();
        Hide();
    }

    private void OnValidate()
    {
        midFirstSegmentOffset = Mathf.Max(0f, midFirstSegmentOffset);
        midSegmentSpacing = Mathf.Max(0.001f, midSegmentSpacing);
        maxSegments = Mathf.Clamp(maxSegments, 0, HardMaxSegments);
    }

    public void Show(Vector3 origin, Vector2 direction, float length, float width)
    {
        CacheBaseScales();

        if (direction.sqrMagnitude <= 0.0001f || length <= 0f)
        {
            Hide();
            return;
        }

        if (hideWhenWidthIsZero && width <= 0f)
        {
            Hide();
            return;
        }

        Vector2 normalizedDirection = direction.normalized;
        Quaternion rotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(normalizedDirection.y, normalizedDirection.x) * Mathf.Rad2Deg + spriteAngleOffset
        );
        float activeWidth = Mathf.Max(0.001f, width);

        if (tipRenderer != null)
        {
            ApplySpriteTransform(
                tipRenderer,
                tipBaseScale,
                origin + (Vector3)(normalizedDirection * tipOffset),
                rotation,
                activeWidth
            );
            tipRenderer.gameObject.SetActive(true);
        }

        int segmentCount = GetSegmentCount(length);
        EnsureSegmentPool(segmentCount);

        for (int i = 0; i < midSegments.Count; i++)
        {
            SpriteRenderer segment = midSegments[i];
            bool shouldShow = i < segmentCount;
            segment.gameObject.SetActive(shouldShow);

            if (!shouldShow)
            {
                continue;
            }

            float distance = midFirstSegmentOffset + midSegmentSpacing * i;
            ApplySpriteTransform(
                segment,
                midBaseScale,
                origin + (Vector3)(normalizedDirection * distance),
                rotation,
                activeWidth
            );
        }
    }

    public void Hide()
    {
        if (tipRenderer != null)
        {
            tipRenderer.gameObject.SetActive(false);
        }

        if (midSegmentPrefab != null)
        {
            midSegmentPrefab.gameObject.SetActive(false);
        }

        for (int i = 0; i < midSegments.Count; i++)
        {
            if (midSegments[i] != null)
            {
                midSegments[i].gameObject.SetActive(false);
            }
        }
    }

    private int GetSegmentCount(float length)
    {
        if (midSegmentPrefab == null || maxSegments <= 0 || length < midFirstSegmentOffset)
        {
            return 0;
        }

        return Mathf.Clamp(
            Mathf.FloorToInt((length - midFirstSegmentOffset) / midSegmentSpacing) + 1,
            0,
            Mathf.Min(maxSegments, HardMaxSegments)
        );
    }

    private void EnsureSegmentPool(int count)
    {
        if (midSegmentPrefab == null)
        {
            return;
        }

        Transform parent = segmentRoot != null ? segmentRoot : transform;
        while (midSegments.Count < count)
        {
            SpriteRenderer segment = Instantiate(midSegmentPrefab, parent);
            segment.gameObject.SetActive(false);
            midSegments.Add(segment);
        }
    }

    private void ApplySpriteTransform(
        SpriteRenderer spriteRenderer,
        Vector3 baseScale,
        Vector3 position,
        Quaternion rotation,
        float width
    )
    {
        spriteRenderer.transform.position = position;
        spriteRenderer.transform.rotation = rotation;
        spriteRenderer.transform.localScale = new Vector3(
            baseScale.x,
            GetLocalScaleForWorldWidth(spriteRenderer, baseScale, width),
            baseScale.z
        );
    }

    private float GetLocalScaleForWorldWidth(SpriteRenderer spriteRenderer, Vector3 baseScale, float width)
    {
        if (spriteRenderer.sprite == null || spriteRenderer.sprite.bounds.size.y <= 0.0001f)
        {
            return baseScale.y;
        }

        Transform parent = spriteRenderer.transform.parent;
        float parentScaleY = parent != null ? Mathf.Abs(parent.lossyScale.y) : 1f;
        if (parentScaleY <= 0.0001f)
        {
            parentScaleY = 1f;
        }

        float scaleSign = baseScale.y < 0f ? -1f : 1f;
        return scaleSign * width / spriteRenderer.sprite.bounds.size.y / parentScaleY;
    }

    private void CacheBaseScales()
    {
        if (baseScalesCached)
        {
            return;
        }

        if (tipRenderer != null)
        {
            tipBaseScale = tipRenderer.transform.localScale;
        }

        if (midSegmentPrefab != null)
        {
            midBaseScale = midSegmentPrefab.transform.localScale;
        }

        baseScalesCached = true;
    }
}
