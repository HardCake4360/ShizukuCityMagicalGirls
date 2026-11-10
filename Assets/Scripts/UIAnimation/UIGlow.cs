using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Creates a soft halo outside a UI graphic without camera Bloom or texture-atlas sampling.</summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[AddComponentMenu("UI/Effects/UI Glow")]
public sealed class UIGlow : BaseMeshEffect, IMaterialModifier
{
    public const string ShaderName = "ShizukuCity/UI/Glow";
    private static readonly int RadiusId = Shader.PropertyToID("_GlowRadius");
    private static readonly int IntensityId = Shader.PropertyToID("_GlowIntensity");
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int TintId = Shader.PropertyToID("_Color");
    private static readonly int BrightnessId = Shader.PropertyToID("_SourceBrightness");
    private static readonly int GlowBrightnessId = Shader.PropertyToID("_GlowBrightness");
    private static readonly int FalloffId = Shader.PropertyToID("_GlowFalloff");
    private static readonly int HDRId = Shader.PropertyToID("_GlowHDR");

    [Header("Glow Quality")]
    [Tooltip("텍스처 이미지의 블러 샘플 밀도입니다. 폴리곤/단색 Image는 샘플 단계 없는 연속 거리 계산을 사용합니다.")]
    [SerializeField, Range(1, 6)] private int rings = 3;
    [SerializeField, Range(8, 24)] private int samplesPerRing = 16;

    private readonly UIGlowMeshBuilder meshBuilder = new();
    private readonly UIGlowPolygonShape analyticShape = new();
    private int cachedSignature;
    private int cachedRings, cachedSamples;
    private bool refreshPending = true;

    protected override void OnEnable()
    {
        base.OnEnable(); refreshPending = true;
        if (graphic != null) graphic.SetMaterialDirty();
    }
    protected override void OnDisable()
    {
        base.OnDisable();
        if (graphic != null) graphic.SetMaterialDirty();
        analyticShape.Dispose();
    }
    protected override void OnDestroy() { analyticShape.Dispose(); base.OnDestroy(); }
    protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); refreshPending = true; }
    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        if (graphic != null) graphic.SetMaterialDirty();
    }

    private void LateUpdate()
    {
        if (!IsActive() || graphic == null) return;
        bool channelsChanged = EnableGlowChannels(graphic);
        Material material = GetGlowMaterial();
        int signature = MaterialSignature(material);
        if (!refreshPending && !channelsChanged && cachedSignature == signature
            && cachedRings == rings && cachedSamples == samplesPerRing) return;
        cachedSignature = signature;
        cachedRings = rings;
        cachedSamples = samplesPerRing;
        refreshPending = false;
        graphic.SetVerticesDirty();
        graphic.SetMaterialDirty();
    }

    internal static bool EnableGlowChannels(Graphic owner)
    {
        Canvas canvas = owner != null ? owner.canvas : null;
        bool changed = EnableGlowChannel(canvas);
        if (canvas != null && canvas.rootCanvas != canvas) changed |= EnableGlowChannel(canvas.rootCanvas);
        return changed;
    }

    private static bool EnableGlowChannel(Canvas canvas)
    {
        if (canvas == null || (canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1) != 0) return false;
        // Do not remove the channel on disable: another graphic may share this Canvas and use UV1.
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        return true;
    }

    public void Refresh()
    {
        refreshPending = true;
        if (graphic != null) { graphic.SetVerticesDirty(); graphic.SetMaterialDirty(); }
    }
    private Material GetGlowMaterial()
    {
        Material material = graphic != null ? graphic.material : null;
        return IsGlowMaterial(material) ? material : null;
    }

    internal static bool IsGlowMaterial(Material material) => material != null && material.shader != null
        && material.shader.name == ShaderName;

    internal static int MaterialSignature(Material material)
    {
        if (!IsGlowMaterial(material)) return 0;
        unchecked
        {
            int hash = material.GetInstanceID();
            hash = hash * 31 + material.GetColor(GlowColorId).GetHashCode();
            hash = hash * 31 + material.GetColor(TintId).GetHashCode();
            hash = hash * 31 + material.GetFloat(RadiusId).GetHashCode();
            hash = hash * 31 + material.GetFloat(IntensityId).GetHashCode();
            hash = hash * 31 + material.GetFloat(BrightnessId).GetHashCode();
            if (material.HasProperty(GlowBrightnessId)) hash = hash * 31 + material.GetFloat(GlowBrightnessId).GetHashCode();
            if (material.HasProperty(FalloffId)) hash = hash * 31 + material.GetFloat(FalloffId).GetHashCode();
            if (material.HasProperty(HDRId)) hash = hash * 31 + material.GetFloat(HDRId).GetHashCode();
            return hash;
        }
    }

    internal static bool IsStencilWriter(Graphic owner)
    {
        Mask mask = owner != null ? owner.GetComponent<Mask>() : null;
        return mask != null && mask.IsActive();
    }

    public override void ModifyMesh(VertexHelper vertices)
    {
        analyticShape.Clear();
        if (!IsActive()) return;
        Material material = GetGlowMaterial();
        if (material == null) return;
        if (IsStencilWriter(graphic)) return;
        if (graphic is UICustomPolygonRenderer polygon && polygon.UsesAnalyticGlow) return;
        // A texture-less Image is an exact solid rectangle, so use the continuous path too.
        if (graphic is Image image && image.sprite == null && analyticShape.PrepareRectangle(vertices))
        {
            analyticShape.Populate(vertices, material, graphic.color);
            return;
        }
        meshBuilder.Apply(vertices, material, rings, samplesPerRing);
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        if (!IsActive()) return baseMaterial;
        Material result = SyncVisualProperties(GetGlowMaterial(), baseMaterial);
        return analyticShape.IsReady ? analyticShape.GetMaterial(result) : result;
    }

    internal static Material SyncVisualProperties(Material source, Material baseMaterial)
    {
        if (!IsGlowMaterial(source) || baseMaterial == null || source == baseMaterial || baseMaterial.shader != source.shader)
            return baseMaterial;
        // Preserve Mask's stencil/alpha-clip state while refreshing its cached material's visuals.
        baseMaterial.SetColor(GlowColorId, source.GetColor(GlowColorId));
        baseMaterial.SetColor(TintId, source.GetColor(TintId));
        baseMaterial.SetFloat(IntensityId, source.GetFloat(IntensityId));
        baseMaterial.SetFloat(RadiusId, source.GetFloat(RadiusId));
        baseMaterial.SetFloat(BrightnessId, source.GetFloat(BrightnessId));
        if (source.HasProperty(GlowBrightnessId)) baseMaterial.SetFloat(GlowBrightnessId, source.GetFloat(GlowBrightnessId));
        if (source.HasProperty(FalloffId)) baseMaterial.SetFloat(FalloffId, source.GetFloat(FalloffId));
        if (source.HasProperty(HDRId)) baseMaterial.SetFloat(HDRId, source.GetFloat(HDRId));
        return baseMaterial;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        rings = Mathf.Clamp(rings, 1, 6);
        samplesPerRing = Mathf.Clamp(samplesPerRing, 8, 24);
        refreshPending = true;
        base.OnValidate();
    }
#endif
}

/// <summary>A single expanded quad and a per-graphic polygon SDF, without repeated silhouettes.</summary>
internal sealed class UIGlowPolygonShape
{
    private const int MaximumPoints = 64;
    private readonly Vector4[] points = new Vector4[MaximumPoints];
    private readonly List<UIVertex> rectangle = new();
    private int count;
    private Vector2 minimum, maximum;
    private Material renderingMaterial;
    public bool IsReady => count >= 3;

    public void Clear() { count = 0; Upload(); }
    public bool Prepare(IReadOnlyList<Vector2> vertices)
    {
        if (vertices == null || vertices.Count < 3 || vertices.Count > MaximumPoints) { Clear(); return false; }
        count = vertices.Count;
        minimum = maximum = vertices[0];
        for (int i = 0; i < count; i++)
        {
            points[i] = new Vector4(vertices[i].x, vertices[i].y, 0f, 0f);
            minimum = Vector2.Min(minimum, vertices[i]);
            maximum = Vector2.Max(maximum, vertices[i]);
        }
        Upload();
        return true;
    }
    public bool PrepareRectangle(VertexHelper vertices)
    {
        rectangle.Clear();
        vertices.GetUIVertexStream(rectangle);
        if (rectangle.Count == 0) return false;
        Vector2 low = rectangle[0].position, high = low;
        foreach (UIVertex vertex in rectangle)
        {
            low = Vector2.Min(low, vertex.position);
            high = Vector2.Max(high, vertex.position);
        }
        return Prepare(new[] { low, new Vector2(low.x, high.y), high, new Vector2(high.x, low.y) });
    }
    public void Populate(VertexHelper vertices, Material material, Color color)
    {
        float padding = Mathf.Max(0f, material.GetFloat("_GlowRadius"));
        Vector2 low = minimum - Vector2.one * padding, high = maximum + Vector2.one * padding;
        vertices.Clear();
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.uv0 = Vector2.one * 0.5f;
        AddCorner(vertices, vertex, low.x, low.y);
        AddCorner(vertices, vertex, low.x, high.y);
        AddCorner(vertices, vertex, high.x, high.y);
        AddCorner(vertices, vertex, high.x, low.y);
        vertices.AddTriangle(0, 1, 2); vertices.AddTriangle(2, 3, 0);
    }
    private static void AddCorner(VertexHelper vertices, UIVertex vertex, float x, float y)
    {
        vertex.position = new Vector3(x, y);
        // Canvas batching may transform POSITION into canvas space. UV1 preserves the
        // original graphic-local coordinates for both scene and game rendering paths.
        vertex.uv1 = new Vector4(x, y, 0f, 0f);
        vertices.AddVert(vertex);
    }
    public Material GetMaterial(Material source)
    {
        if (source == null || !IsReady) return source;
        if (renderingMaterial == null)
            renderingMaterial = new Material(source) { name = source.name + " (UI Glow Shape)", hideFlags = HideFlags.HideAndDontSave };
        if (renderingMaterial.shader != source.shader) renderingMaterial.shader = source.shader;
        // The source is already MaskableGraphic's stencil material. Preserve all of its state.
        renderingMaterial.CopyPropertiesFromMaterial(source);
        Upload();
        return renderingMaterial;
    }
    private void Upload()
    {
        if (renderingMaterial == null) return;
        renderingMaterial.SetInteger("_GlowPolygonCount", count);
        if (count >= 3) renderingMaterial.SetVectorArray("_GlowPolygonPoints", points);
    }
    public void Dispose()
    {
        count = 0;
        if (renderingMaterial == null) return;
        if (Application.isPlaying) Object.Destroy(renderingMaterial);
        else Object.DestroyImmediate(renderingMaterial);
        renderingMaterial = null;
    }
}

/// <summary>Shared by Image's mesh effect and the polygon renderer's material-only fallback.</summary>
internal sealed class UIGlowMeshBuilder
{
    private const int MaximumVertices = 64000;
    private readonly List<UIVertex> sourceVertices = new();
    private readonly List<UIVertex> outputVertices = new();

    public void Apply(VertexHelper vertices, Material material, int rings = 3, int samplesPerRing = 16)
    {
        float radius = Mathf.Clamp(material.GetFloat("_GlowRadius"), 0f, 128f);
        if (radius <= 0f || material.GetFloat("_GlowIntensity") <= 0f) return;
        sourceVertices.Clear();
        vertices.GetUIVertexStream(sourceVertices);
        int count = sourceVertices.Count;
        if (count == 0) return;
        int availableCopies = MaximumVertices / count - 1;
        int ringCount = Mathf.Clamp(rings, 1, 6);
        while (ringCount > 1 && availableCopies / (ringCount + 1) < 4) ringCount--;
        bool includeCore = availableCopies >= (ringCount + 1) * 4;
        int directions = Mathf.Min(Mathf.Clamp(samplesPerRing, 8, 24), availableCopies / (ringCount + (includeCore ? 1 : 0)));
        if (directions < 4) return;

        float falloff = material.HasProperty("_GlowFalloff") ? Mathf.Clamp(material.GetFloat("_GlowFalloff"), 0.25f, 4f) : 1.2f;

        int sampleCount = ringCount * directions;
        float totalWeight = 0f;
        for (int sample = 0; sample < sampleCount; sample++)
            totalWeight += RingWeight(Mathf.Sqrt((sample + 0.5f) / sampleCount), falloff);
        outputVertices.Clear();
        int capacity = count * ((ringCount + (includeCore ? 1 : 0)) * directions + 1);
        if (outputVertices.Capacity < capacity) outputVertices.Capacity = capacity;
        // Low-discrepancy disk samples avoid the concentric bands of repeated fixed-radius rings.
        const float goldenAngle = 2.39996323f;
        for (int sample = sampleCount - 1; sample >= 0; sample--)
        {
            float distance = Mathf.Sqrt((sample + 0.5f) / sampleCount);
            float weight = RingWeight(distance, falloff) / totalWeight;
            AddCopy(sample * goldenAngle, distance * radius, 1f, weight);
        }
        // A bright, thin core is separate from the soft outer haze. Normalization alone made
        // the previous implementation nearly invisible at the silhouette's outer edge.
        if (includeCore)
        {
            float coreWeight = 0f;
            for (int sample = 0; sample < directions; sample++)
                coreWeight += RingWeight(Mathf.Sqrt((sample + 0.5f) / directions), falloff);
            for (int sample = directions - 1; sample >= 0; sample--)
            {
                float distance = Mathf.Sqrt((sample + 0.5f) / directions);
                AddCopy(sample * goldenAngle, distance * Mathf.Min(radius, Mathf.Max(1f, radius * 0.15f)),
                    2f, RingWeight(distance, falloff) / coreWeight);
            }
        }
        for (int i = 0; i < count; i++)
        {
            UIVertex vertex = sourceVertices[i];
            vertex.uv1 = Vector4.zero;
            outputVertices.Add(vertex);
        }
        vertices.Clear();
        vertices.AddUIVertexTriangleStream(outputVertices);
    }
    private void AddCopy(float angle, float radius, float kind, float weight)
    {
        Vector3 offset = new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        foreach (UIVertex source in sourceVertices)
        {
            UIVertex vertex = source;
            vertex.position += offset;
            vertex.uv1 = new Vector4(kind, weight, 0f, 0f);
            outputVertices.Add(vertex);
        }
    }
    private static float RingWeight(float distance, float falloff) => Mathf.Exp(-falloff * distance * distance);
}
