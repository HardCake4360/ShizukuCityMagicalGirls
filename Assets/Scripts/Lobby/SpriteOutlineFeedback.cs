using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpriteOutlineFeedback : MonoBehaviour
{
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int GeometryScaleId = Shader.PropertyToID("_OutlineGeometryScale");
    private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");
    private static readonly int SpritePivotId = Shader.PropertyToID("_SpritePivot");

    [SerializeField] private SpriteRenderer targetRenderer;

    private SpriteRenderer outlineRenderer;
    private Material outlineMaterial;
    private SpriteOutlineSettings settings;
    private bool highlighted;
    private float currentOutlineWidth;
    private float transitionStartWidth;
    private float transitionTargetWidth;
    private float transitionElapsed;
    private bool isTransitioning;

    private void Awake()
    {
        CacheTargetRenderer();
    }

    private void LateUpdate()
    {
        if (outlineRenderer == null)
        {
            return;
        }

        float desiredWidth = highlighted ? GetOutlineWidth() : 0f;
        if (!Mathf.Approximately(transitionTargetWidth, desiredWidth))
        {
            BeginTransition(desiredWidth);
        }

        if (isTransitioning)
        {
            transitionElapsed += Time.unscaledDeltaTime;
            float duration = GetTransitionDuration();
            float normalizedTime = duration > 0f
                ? Mathf.Clamp01(transitionElapsed / duration)
                : 1f;
            float curveValue = EvaluateTransitionCurve(normalizedTime);
            currentOutlineWidth = Mathf.LerpUnclamped(
                transitionStartWidth,
                transitionTargetWidth,
                curveValue
            );

            if (normalizedTime >= 1f)
            {
                currentOutlineWidth = transitionTargetWidth;
                isTransitioning = false;
            }
        }

        SynchronizeOutline();

        if (!highlighted && currentOutlineWidth <= Mathf.Epsilon)
        {
            outlineRenderer.enabled = false;
        }
    }

    private void OnDisable()
    {
        highlighted = false;
        currentOutlineWidth = 0f;
        transitionStartWidth = 0f;
        transitionTargetWidth = 0f;
        transitionElapsed = 0f;
        isTransitioning = false;
        if (outlineRenderer != null)
        {
            outlineRenderer.enabled = false;
        }
    }

    private void OnDestroy()
    {
        DestroyOutlineRenderer();
    }

    public void SetHighlighted(bool value)
    {
        if (highlighted == value)
        {
            return;
        }

        highlighted = value;
        BeginTransition(highlighted ? GetOutlineWidth() : 0f);
        if (highlighted)
        {
            EnsureOutlineRenderer();
            if (outlineRenderer != null)
            {
                outlineRenderer.enabled = true;
                SynchronizeOutline();
            }
        }
    }

    private void CacheTargetRenderer()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<SpriteRenderer>();
        }
    }

    public void Configure(SpriteOutlineSettings sharedSettings)
    {
        settings = sharedSettings;
    }

    private void BeginTransition(float targetWidth)
    {
        transitionStartWidth = currentOutlineWidth;
        transitionTargetWidth = Mathf.Max(0f, targetWidth);
        transitionElapsed = 0f;
        isTransitioning = !Mathf.Approximately(transitionStartWidth, transitionTargetWidth);

        if (GetTransitionDuration() <= 0f)
        {
            currentOutlineWidth = transitionTargetWidth;
            isTransitioning = false;
        }
    }

    private float EvaluateTransitionCurve(float normalizedTime)
    {
        AnimationCurve curve = settings != null ? settings.TransitionCurve : null;
        if (curve == null || curve.length == 0)
        {
            return Mathf.SmoothStep(0f, 1f, normalizedTime);
        }

        return curve.Evaluate(normalizedTime);
    }

    private float GetOutlineWidth()
    {
        return settings != null ? settings.WidthPixels : 1f;
    }

    private float GetTransitionDuration()
    {
        return settings != null ? settings.TransitionDuration : 0.15f;
    }

    private void EnsureOutlineRenderer()
    {
        CacheTargetRenderer();
        if (targetRenderer == null || outlineRenderer != null)
        {
            return;
        }

        Shader outlineShader = Shader.Find("ShizukuCity/Sprite Alpha Outline");
        if (outlineShader == null)
        {
            Debug.LogError("Sprite outline shader could not be found.", this);
            return;
        }

        outlineMaterial = new Material(outlineShader)
        {
            name = targetRenderer.name + " Outline Material",
            hideFlags = HideFlags.HideAndDontSave
        };

        GameObject outlineObject = new GameObject("Outline")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        outlineObject.transform.SetParent(targetRenderer.transform, false);
        outlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
        outlineRenderer.sharedMaterial = outlineMaterial;
    }

    private void SynchronizeOutline()
    {
        if (targetRenderer == null || outlineRenderer == null || outlineMaterial == null)
        {
            return;
        }

        outlineMaterial.SetColor(OutlineColorId, settings != null ? settings.Color : Color.white);
        outlineMaterial.SetFloat(OutlineWidthId, currentOutlineWidth);

        Sprite sprite = targetRenderer.sprite;
        outlineRenderer.sprite = sprite;
        outlineRenderer.flipX = targetRenderer.flipX;
        outlineRenderer.flipY = targetRenderer.flipY;
        outlineRenderer.sortingLayerID = targetRenderer.sortingLayerID;
        outlineRenderer.sortingOrder = targetRenderer.sortingOrder + 1;
        outlineRenderer.maskInteraction = targetRenderer.maskInteraction;
        outlineRenderer.transform.localPosition = Vector3.zero;

        if (sprite == null)
        {
            outlineRenderer.transform.localScale = Vector3.one;
            return;
        }

        Rect spriteRect = sprite.rect;
        float paddingPixels = Mathf.Max(GetOutlineWidth(), currentOutlineWidth) + 1f;
        Vector2 geometryScale = new Vector2(
            1f + paddingPixels * 2f / Mathf.Max(1f, spriteRect.width),
            1f + paddingPixels * 2f / Mathf.Max(1f, spriteRect.height)
        );
        outlineRenderer.transform.localScale = new Vector3(geometryScale.x, geometryScale.y, 1f);

        Rect textureRect = sprite.textureRect;
        Texture texture = sprite.texture;
        Vector4 uvRect = new Vector4(
            textureRect.xMin / texture.width,
            textureRect.yMin / texture.height,
            textureRect.xMax / texture.width,
            textureRect.yMax / texture.height
        );
        Vector2 normalizedPivot = new Vector2(
            sprite.pivot.x / Mathf.Max(1f, spriteRect.width),
            sprite.pivot.y / Mathf.Max(1f, spriteRect.height)
        );

        outlineMaterial.SetVector(GeometryScaleId, geometryScale);
        outlineMaterial.SetVector(SpriteUvRectId, uvRect);
        outlineMaterial.SetVector(SpritePivotId, normalizedPivot);
    }

    private void DestroyOutlineRenderer()
    {
        if (outlineRenderer != null)
        {
            if (Application.isPlaying)
            {
                Destroy(outlineRenderer.gameObject);
            }
            else
            {
                DestroyImmediate(outlineRenderer.gameObject);
            }
        }

        if (outlineMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(outlineMaterial);
            }
            else
            {
                DestroyImmediate(outlineMaterial);
            }
        }

        outlineRenderer = null;
        outlineMaterial = null;
    }
}
