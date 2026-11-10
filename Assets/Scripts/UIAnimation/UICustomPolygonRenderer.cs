using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/Custom Polygon Renderer")]
public sealed class UICustomPolygonRenderer : MaskableGraphic
{
    private const float GeometryEpsilon = 0.0001f;
    private const float GeometryEpsilonSquared = GeometryEpsilon * GeometryEpsilon;

    [Header("Polygon")]
    [Tooltip("Connect the child RectTransforms in polygon order. The last vertex is connected back to the first.")]
    [SerializeField] private List<RectTransform> vertices = new();

    private readonly List<Vector2> localVertices = new();
    private readonly List<int> triangleIndices = new();
    private readonly List<int> remainingIndices = new();
    private readonly List<Vector2> cachedVertexPositions = new();
    private readonly List<bool> cachedAssignedStates = new();
    private readonly UIGlowMeshBuilder automaticGlow = new();
    private readonly UIGlowPolygonShape analyticGlow = new();
    private int glowMaterialSignature;
    internal bool UsesAnalyticGlow => analyticGlow.IsReady;

    public IReadOnlyList<RectTransform> Vertices => vertices;

    protected override void OnEnable()
    {
        useLegacyMeshGeneration = false;
        base.OnEnable();
        InvalidateVertexCache();
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        InvalidateVertexCache();
        base.OnDisable();
        analyticGlow.Dispose();
    }

    protected override void OnDestroy() { analyticGlow.Dispose(); base.OnDestroy(); }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        CollectLocalVertices(localVertices);
        CacheCurrentVertexState();
        analyticGlow.Clear();

        if (localVertices.Count < 3
            || !TryTriangulate(localVertices, triangleIndices))
        {
            return;
        }

        UIGlow explicitEffect = GetComponent<UIGlow>();
        bool wantsGlow = UIGlow.IsGlowMaterial(material) && !UIGlow.IsStencilWriter(this)
            && (explicitEffect == null || explicitEffect.isActiveAndEnabled);
        if (wantsGlow && analyticGlow.Prepare(localVertices))
        {
            analyticGlow.Populate(vertexHelper, material, color);
            return;
        }

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.uv0 = Vector2.zero;
        for (int i = 0; i < localVertices.Count; i++)
        {
            vertex.position = localVertices[i];
            vertexHelper.AddVert(vertex);
        }

        for (int i = 0; i < triangleIndices.Count; i += 3)
        {
            vertexHelper.AddTriangle(
                triangleIndices[i],
                triangleIndices[i + 1],
                triangleIndices[i + 2]
            );
        }

        // Polygons support the Glow material directly. An explicitly attached effect owns
        // quality/enable state instead, so the halo must never be generated twice.
        if (UsesAutomaticGlow() && !UIGlow.IsStencilWriter(this))
            automaticGlow.Apply(vertexHelper, material);
    }

    private bool UsesAutomaticGlow() => UIGlow.IsGlowMaterial(material) && GetComponent<UIGlow>() == null;

    public override Material GetModifiedMaterial(Material baseMaterial)
    {
        Material result = UIGlow.SyncVisualProperties(material, base.GetModifiedMaterial(baseMaterial));
        return UsesAnalyticGlow ? analyticGlow.GetMaterial(result) : result;
    }

    public override bool Raycast(Vector2 screenPoint, Camera eventCamera)
    {
        if (!base.Raycast(screenPoint, eventCamera)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                screenPoint,
                eventCamera,
                out Vector2 localPoint
            ))
        {
            return false;
        }

        CollectLocalVertices(localVertices);
        return localVertices.Count >= 3 && IsPointInsidePolygon(localPoint, localVertices);
    }

    public void RefreshGeometry()
    {
        InvalidateVertexCache();
        SetVerticesDirty();
        SetMaterialDirty();
    }

    private void LateUpdate()
    {
        int signature = UIGlow.MaterialSignature(material);
        bool channelsChanged = UsesAutomaticGlow() && UIGlow.EnableGlowChannels(this);
        if (signature != glowMaterialSignature || channelsChanged)
        {
            glowMaterialSignature = signature;
            SetVerticesDirty();
            SetMaterialDirty();
        }
        if (HaveVertexPositionsChanged())
        {
            SetVerticesDirty();
            // A cut-in can begin fully collapsed, before a shape material exists.
            // Its first valid polygon must bind that material as well as update its vertices.
            SetMaterialDirty();
        }
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        SetMaterialDirty();
    }

    private void OnTransformChildrenChanged()
    {
        RefreshGeometry();
    }

    protected override void OnDidApplyAnimationProperties()
    {
        base.OnDidApplyAnimationProperties();
        RefreshGeometry();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        RefreshGeometry();
    }

    private void CollectLocalVertices(List<Vector2> output)
    {
        output.Clear();
        for (int i = 0; i < vertices.Count; i++)
        {
            RectTransform vertexTransform = vertices[i];
            if (vertexTransform == null)
            {
                continue;
            }

            Vector3 localPosition3D = rectTransform.InverseTransformPoint(vertexTransform.position);
            Vector2 localPosition = new Vector2(localPosition3D.x, localPosition3D.y);
            if (!ContainsApproximately(output, localPosition))
            {
                output.Add(localPosition);
            }
        }

        RemoveCollinearVertices(output);
    }

    private static void RemoveCollinearVertices(List<Vector2> points)
    {
        if (points.Count <= 3)
        {
            return;
        }

        bool removed;
        do
        {
            removed = false;
            for (int i = points.Count - 1; i >= 0 && points.Count > 3; i--)
            {
                Vector2 previous = points[(i - 1 + points.Count) % points.Count];
                Vector2 current = points[i];
                Vector2 next = points[(i + 1) % points.Count];
                if (Mathf.Abs(Cross(current - previous, next - current))
                    <= GeometryEpsilon)
                {
                    points.RemoveAt(i);
                    removed = true;
                }
            }
        }
        while (removed && points.Count > 3);
    }

    private bool TryTriangulate(List<Vector2> polygon, List<int> output)
    {
        output.Clear();
        remainingIndices.Clear();

        float signedArea = CalculateSignedArea(polygon);
        if (Mathf.Abs(signedArea) <= GeometryEpsilon)
        {
            return false;
        }

        bool counterClockwise = signedArea > 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            remainingIndices.Add(i);
        }

        int safety = polygon.Count * polygon.Count;
        while (remainingIndices.Count > 3 && safety-- > 0)
        {
            bool foundEar = false;
            for (int i = 0; i < remainingIndices.Count; i++)
            {
                int previous = remainingIndices[(i - 1 + remainingIndices.Count) % remainingIndices.Count];
                int current = remainingIndices[i];
                int next = remainingIndices[(i + 1) % remainingIndices.Count];

                if (!IsConvex(
                        polygon[previous],
                        polygon[current],
                        polygon[next],
                        counterClockwise
                    )
                    || ContainsAnotherVertex(
                        polygon,
                        remainingIndices,
                        previous,
                        current,
                        next,
                        counterClockwise
                    ))
                {
                    continue;
                }

                AddTriangle(output, previous, current, next, counterClockwise);
                remainingIndices.RemoveAt(i);
                foundEar = true;
                break;
            }

            if (!foundEar)
            {
                output.Clear();
                return false;
            }
        }

        if (remainingIndices.Count != 3)
        {
            output.Clear();
            return false;
        }

        AddTriangle(
            output,
            remainingIndices[0],
            remainingIndices[1],
            remainingIndices[2],
            counterClockwise
        );
        return true;
    }

    private static void AddTriangle(
        List<int> output,
        int first,
        int second,
        int third,
        bool counterClockwise
    )
    {
        output.Add(first);
        if (counterClockwise)
        {
            output.Add(second);
            output.Add(third);
        }
        else
        {
            output.Add(third);
            output.Add(second);
        }
    }

    private static bool ContainsAnotherVertex(
        List<Vector2> polygon,
        List<int> polygonIndices,
        int first,
        int second,
        int third,
        bool counterClockwise
    )
    {
        Vector2 a = polygon[first];
        Vector2 b = polygon[second];
        Vector2 c = polygon[third];
        for (int i = 0; i < polygonIndices.Count; i++)
        {
            int candidateIndex = polygonIndices[i];
            if (candidateIndex == first
                || candidateIndex == second
                || candidateIndex == third)
            {
                continue;
            }

            if (IsPointInsideTriangle(
                    polygon[candidateIndex],
                    a,
                    b,
                    c,
                    counterClockwise
                ))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConvex(
        Vector2 previous,
        Vector2 current,
        Vector2 next,
        bool counterClockwise
    )
    {
        float cross = Cross(current - previous, next - current);
        return counterClockwise
            ? cross > GeometryEpsilon
            : cross < -GeometryEpsilon;
    }

    private static bool IsPointInsideTriangle(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        bool counterClockwise
    )
    {
        float ab = Cross(b - a, point - a);
        float bc = Cross(c - b, point - b);
        float ca = Cross(a - c, point - c);
        return counterClockwise
            ? ab >= -GeometryEpsilon
              && bc >= -GeometryEpsilon
              && ca >= -GeometryEpsilon
            : ab <= GeometryEpsilon
              && bc <= GeometryEpsilon
              && ca <= GeometryEpsilon;
    }

    private static bool IsPointInsidePolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int previous = polygon.Count - 1;
        for (int current = 0; current < polygon.Count; current++)
        {
            Vector2 a = polygon[previous];
            Vector2 b = polygon[current];
            if (IsPointOnSegment(point, a, b))
            {
                return true;
            }

            bool crosses = (a.y > point.y) != (b.y > point.y)
                && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (crosses)
            {
                inside = !inside;
            }

            previous = current;
        }

        return inside;
    }

    private static bool IsPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 segment = b - a;
        Vector2 fromA = point - a;
        if (Mathf.Abs(Cross(segment, fromA)) > GeometryEpsilon)
        {
            return false;
        }

        float dot = Vector2.Dot(fromA, segment);
        return dot >= -GeometryEpsilon
            && dot <= segment.sqrMagnitude + GeometryEpsilon;
    }

    private static float CalculateSignedArea(List<Vector2> polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Count];
            area += current.x * next.y - next.x * current.y;
        }

        return area * 0.5f;
    }

    private static float Cross(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    private static bool ContainsApproximately(List<Vector2> points, Vector2 candidate)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if ((points[i] - candidate).sqrMagnitude <= GeometryEpsilonSquared)
            {
                return true;
            }
        }

        return false;
    }

    private bool HaveVertexPositionsChanged()
    {
        if (cachedVertexPositions.Count != vertices.Count
            || cachedAssignedStates.Count != vertices.Count)
        {
            return true;
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            RectTransform vertexTransform = vertices[i];
            bool assigned = vertexTransform != null;
            if (cachedAssignedStates[i] != assigned)
            {
                return true;
            }

            if (!assigned)
            {
                continue;
            }

            Vector3 localPosition3D = rectTransform.InverseTransformPoint(vertexTransform.position);
            Vector2 localPosition = new Vector2(localPosition3D.x, localPosition3D.y);
            if ((cachedVertexPositions[i] - localPosition).sqrMagnitude > GeometryEpsilonSquared)
            {
                return true;
            }
        }

        return false;
    }

    private void CacheCurrentVertexState()
    {
        cachedVertexPositions.Clear();
        cachedAssignedStates.Clear();
        for (int i = 0; i < vertices.Count; i++)
        {
            RectTransform vertexTransform = vertices[i];
            bool assigned = vertexTransform != null;
            cachedAssignedStates.Add(assigned);

            if (!assigned)
            {
                cachedVertexPositions.Add(Vector2.zero);
                continue;
            }

            Vector3 localPosition3D = rectTransform.InverseTransformPoint(vertexTransform.position);
            cachedVertexPositions.Add(new Vector2(localPosition3D.x, localPosition3D.y));
        }
    }

    private void InvalidateVertexCache()
    {
        cachedVertexPositions.Clear();
        cachedAssignedStates.Clear();
    }

}
