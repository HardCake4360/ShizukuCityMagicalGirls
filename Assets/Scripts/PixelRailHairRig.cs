using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PixelRailHairRig : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform root;
    [SerializeField] Transform segmentContainer;
    [Tooltip("Optional. When empty, the root transform is used as the rail source.")]
    [SerializeField] Transform railSourceOverride;

    [Header("Sprites")]
    [SerializeField] Sprite[] middleSprites = new Sprite[0];
    [SerializeField] Sprite tipSprite;
    [SerializeField] Color tint = Color.white;
    [SerializeField] string sortingLayerName = "Default";
    [SerializeField] int sortingOrder;
    [SerializeField] int sortingOrderStep;

    [Header("Rail")]
    [Tooltip("Number of visible hair pieces. This includes the final tip piece.")]
    [SerializeField, Min(1)] int pieceCount = 5;
    [SerializeField] Vector2 restDirection = Vector2.down;
    [SerializeField, Min(0.001f)] float railSampleMinDistance = 0.01f;
    [SerializeField, Min(0f)] float railHistoryPadding = 0.5f;

    [Header("Sprite Deformation")]
    [SerializeField, Min(0.001f)] float connectionPointUnits = 4f;
    [SerializeField, Min(0.0001f)] float connectionPointUnitScale = 0.01f;
    [SerializeField] bool stretchSpritesToJointDistance = true;
    [SerializeField] AnimationCurve widthScaleByPiece = AnimationCurve.Linear(0f, 1f, 1f, 0.7f);
    [SerializeField, Min(0.01f)] float globalWidthScale = 1f;

    [Header("Sprite Alignment")]
    [SerializeField] float rotationOffsetDegrees;

    [Header("Debug Visualization")]
    [SerializeField] bool showRailGizmos = true;
    [SerializeField] Color railColor = Color.magenta;
    [SerializeField] Color jointColor = Color.cyan;

    [SerializeField, HideInInspector] List<SpriteRenderer> segmentRenderers = new List<SpriteRenderer>();
    [SerializeField, HideInInspector] List<int> middleSpriteIndices = new List<int>();

    readonly List<Vector2> railPositions = new List<Vector2>();
    readonly List<float> railDistances = new List<float>();
    float railDistance;
    int builtPieceCount = -1;

    public int PieceCount => GetPieceCount();

    void Reset()
    {
        root = transform;
        segmentContainer = transform;
    }

    void OnEnable()
    {
        InitializeDefaults();
        RebuildSegments(false);
        ResetRail();
        RenderSegments();
    }

    void LateUpdate()
    {
        SyncPieceCountIfNeeded();
        RecordRailSample(GetRailSourcePosition());
        RenderSegments();
    }

    [ContextMenu("Rebuild Segments")]
    public void RebuildSegments()
    {
        RebuildSegments(true);
    }

    [ContextMenu("Reset Rail")]
    public void ResetRail()
    {
        Vector2 sourcePosition = GetRailSourcePosition();
        railPositions.Clear();
        railDistances.Clear();
        railDistance = 0f;
        railPositions.Add(sourcePosition);
        railDistances.Add(railDistance);
    }

    [ContextMenu("Randomize Middle Sprites")]
    public void RandomizeMiddleSprites()
    {
        EnsureMiddleSpriteChoices(true);
        ApplyRendererSettings();
    }

    public void SetPieceCount(int newPieceCount)
    {
        SetPieceCount(newPieceCount, false);
    }

    public void SetPieceCount(int newPieceCount, bool randomizeMiddleSprites)
    {
        pieceCount = Mathf.Max(1, newPieceCount);
        RebuildSegments(randomizeMiddleSprites);
        TrimRailHistory();
        RenderSegments();
    }

    void InitializeDefaults()
    {
        if (root == null)
        {
            root = transform;
        }

        if (segmentContainer == null)
        {
            segmentContainer = transform;
        }
    }

    void SyncPieceCountIfNeeded()
    {
        int count = GetPieceCount();
        if (builtPieceCount == count && segmentRenderers.Count == count)
        {
            return;
        }

        RebuildSegments(false);
    }

    void RebuildSegments(bool randomizeMiddleSprites)
    {
        InitializeDefaults();
        int count = GetPieceCount();
        EnsureMiddleSpriteChoices(randomizeMiddleSprites);

        for (int i = segmentRenderers.Count - 1; i >= 0; i--)
        {
            if (segmentRenderers[i] == null)
            {
                segmentRenderers.RemoveAt(i);
            }
        }

        while (segmentRenderers.Count < count)
        {
            GameObject segmentObject = new GameObject(GetSegmentName(segmentRenderers.Count));
            segmentObject.transform.SetParent(segmentContainer, false);
            segmentRenderers.Add(segmentObject.AddComponent<SpriteRenderer>());
        }

        while (segmentRenderers.Count > count)
        {
            int lastIndex = segmentRenderers.Count - 1;
            SpriteRenderer rendererToRemove = segmentRenderers[lastIndex];
            segmentRenderers.RemoveAt(lastIndex);

            if (rendererToRemove != null)
            {
                DestroySegmentObject(rendererToRemove.gameObject);
            }
        }

        ApplyRendererSettings();
        CleanupOrphanSegmentObjects();
        builtPieceCount = count;
    }

    void ApplyRendererSettings()
    {
        for (int i = 0; i < segmentRenderers.Count; i++)
        {
            SpriteRenderer segmentRenderer = segmentRenderers[i];
            if (segmentRenderer == null)
            {
                continue;
            }

            segmentRenderer.name = GetSegmentName(i);
            segmentRenderer.sprite = GetSpriteForSegment(i);
            segmentRenderer.color = tint;
            segmentRenderer.sortingLayerName = sortingLayerName;
            segmentRenderer.sortingOrder = sortingOrder + sortingOrderStep * i;
        }
    }

    void RecordRailSample(Vector2 sourcePosition)
    {
        if (railPositions.Count == 0)
        {
            ResetRail();
            return;
        }

        Vector2 lastPosition = railPositions[railPositions.Count - 1];
        float distanceFromLast = Vector2.Distance(sourcePosition, lastPosition);
        if (distanceFromLast >= railSampleMinDistance)
        {
            railDistance += distanceFromLast;
            railPositions.Add(sourcePosition);
            railDistances.Add(railDistance);
        }
        else
        {
            railPositions[railPositions.Count - 1] = sourcePosition;
        }

        TrimRailHistory();
    }

    void RenderSegments()
    {
        for (int i = 0; i < segmentRenderers.Count; i++)
        {
            SpriteRenderer segmentRenderer = segmentRenderers[i];
            if (segmentRenderer == null)
            {
                continue;
            }

            Vector2 start = SampleRailPoint(i * GetSegmentLength());
            Vector2 end = SampleRailPoint((i + 1) * GetSegmentLength());
            ApplySegmentTransform(segmentRenderer.transform, segmentRenderer.sprite, i, start, end);
        }
    }

    void ApplySegmentTransform(Transform segmentTransform, Sprite sprite, int index, Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        float distance = delta.magnitude;
        Vector2 direction = distance > 0.0001f ? delta / distance : GetWorldRestDirection();
        Vector2 centerPosition = (start + end) * 0.5f;

        float targetAngle = GetAngleForDirection(direction);
        Quaternion rotation = Quaternion.Euler(0f, 0f, targetAngle);
        Vector3 scale = GetScaleForSegment(index, distance);
        Vector3 pivotPosition = GetPivotPositionForSpriteCenter(sprite, centerPosition, rotation, scale);

        segmentTransform.position = new Vector3(pivotPosition.x, pivotPosition.y, segmentTransform.position.z);
        segmentTransform.rotation = rotation;
        segmentTransform.localScale = scale;
    }

    Vector2 SampleRailPoint(float distanceBehindRoot)
    {
        if (railPositions.Count == 0)
        {
            return GetFallbackPoint(distanceBehindRoot);
        }

        float targetDistance = railDistance - distanceBehindRoot;
        if (targetDistance <= railDistances[0])
        {
            return railPositions[0];
        }

        for (int i = railDistances.Count - 1; i > 0; i--)
        {
            float olderDistance = railDistances[i - 1];
            float newerDistance = railDistances[i];
            if (targetDistance < olderDistance || targetDistance > newerDistance)
            {
                continue;
            }

            float segmentDistance = newerDistance - olderDistance;
            float t = segmentDistance > 0.0001f
                ? (targetDistance - olderDistance) / segmentDistance
                : 0f;
            return Vector2.Lerp(railPositions[i - 1], railPositions[i], t);
        }

        return GetFallbackPoint(distanceBehindRoot);
    }

    Vector2 GetFallbackPoint(float distanceBehindRoot)
    {
        return GetRailSourcePosition() + GetWorldRestDirection() * distanceBehindRoot;
    }

    void TrimRailHistory()
    {
        float maxHistoryDistance = GetSegmentLength() * (GetPieceCount() + 1) + railHistoryPadding;
        float minimumKeptDistance = railDistance - maxHistoryDistance;

        while (railDistances.Count > 2 && railDistances[1] < minimumKeptDistance)
        {
            railDistances.RemoveAt(0);
            railPositions.RemoveAt(0);
        }
    }

    float GetAngleForDirection(Vector2 direction)
    {
        float directionAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        return directionAngle - -90f + rotationOffsetDegrees;
    }

    Vector3 GetScaleForSegment(int index, float jointDistance)
    {
        float normalizedIndex = GetPieceCount() <= 1 ? 1f : index / (float)(GetPieceCount() - 1);
        float widthScale = globalWidthScale * EvaluateCurve(widthScaleByPiece, normalizedIndex, 1f);
        float lengthScale = stretchSpritesToJointDistance
            ? jointDistance / GetSegmentLength()
            : 1f;

        return new Vector3(widthScale, lengthScale, 1f);
    }

    Vector3 GetPivotPositionForSpriteCenter(Sprite sprite, Vector2 centerPosition, Quaternion rotation, Vector3 scale)
    {
        if (sprite == null)
        {
            return centerPosition;
        }

        Vector3 scaledCenterOffset = Vector3.Scale(sprite.bounds.center, scale);
        Vector3 worldCenterOffset = rotation * scaledCenterOffset;
        return new Vector3(centerPosition.x, centerPosition.y, transform.position.z) - worldCenterOffset;
    }

    Sprite GetSpriteForSegment(int index)
    {
        if (index >= GetPieceCount() - 1)
        {
            return tipSprite;
        }

        if (middleSprites == null || middleSprites.Length == 0)
        {
            return tipSprite;
        }

        if (middleSprites.Length == 1)
        {
            return middleSprites[0];
        }

        EnsureMiddleSpriteChoices(false);

        if (index < middleSpriteIndices.Count)
        {
            int spriteIndex = Mathf.Clamp(middleSpriteIndices[index], 0, middleSprites.Length - 1);
            return middleSprites[spriteIndex];
        }

        return middleSprites[Random.Range(0, middleSprites.Length)];
    }

    void EnsureMiddleSpriteChoices(bool randomize)
    {
        int middlePieceCount = Mathf.Max(0, GetPieceCount() - 1);
        while (middleSpriteIndices.Count < middlePieceCount)
        {
            middleSpriteIndices.Add(GetRandomMiddleSpriteIndex());
        }

        while (middleSpriteIndices.Count > middlePieceCount)
        {
            middleSpriteIndices.RemoveAt(middleSpriteIndices.Count - 1);
        }

        if (!randomize)
        {
            return;
        }

        for (int i = 0; i < middleSpriteIndices.Count; i++)
        {
            middleSpriteIndices[i] = GetRandomMiddleSpriteIndex();
        }
    }

    int GetRandomMiddleSpriteIndex()
    {
        if (middleSprites == null || middleSprites.Length == 0)
        {
            return 0;
        }

        return Random.Range(0, middleSprites.Length);
    }

    int GetPieceCount()
    {
        return Mathf.Max(1, pieceCount);
    }

    float GetSegmentLength()
    {
        return Mathf.Max(0.0001f, connectionPointUnits * connectionPointUnitScale) * 2f;
    }

    Vector2 GetRailSourcePosition()
    {
        Transform railSource = railSourceOverride != null ? railSourceOverride : root;
        if (railSource == null)
        {
            railSource = transform;
        }

        return railSource.position;
    }

    Vector2 GetWorldRestDirection()
    {
        Vector2 direction = restDirection.sqrMagnitude <= 0.0001f ? Vector2.down : restDirection.normalized;
        return direction;
    }

    float EvaluateCurve(AnimationCurve curve, float time, float fallback)
    {
        if (curve == null || curve.length == 0)
        {
            return fallback;
        }

        return curve.Evaluate(time);
    }

    string GetSegmentName(int index)
    {
        return index >= GetPieceCount() - 1
            ? "RailHairPiece_Tip"
            : $"RailHairPiece_Middle_{index:00}";
    }

    void CleanupOrphanSegmentObjects()
    {
        if (segmentContainer == null)
        {
            return;
        }

        SpriteRenderer[] childRenderers = segmentContainer.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < childRenderers.Length; i++)
        {
            SpriteRenderer childRenderer = childRenderers[i];
            if (childRenderer == null ||
                childRenderer.transform.parent != segmentContainer ||
                segmentRenderers.Contains(childRenderer) ||
                !IsManagedSegmentName(childRenderer.name))
            {
                continue;
            }

            DestroySegmentObject(childRenderer.gameObject);
        }
    }

    bool IsManagedSegmentName(string objectName)
    {
        return objectName.StartsWith("RailHairPiece_Middle_") || objectName == "RailHairPiece_Tip";
    }

    void DestroySegmentObject(GameObject segmentObject)
    {
        segmentObject.SetActive(false);

        if (Application.isPlaying)
        {
            Destroy(segmentObject);
            return;
        }

        DestroyImmediate(segmentObject);
    }

    void OnDrawGizmosSelected()
    {
        if (!showRailGizmos || railPositions.Count <= 1)
        {
            return;
        }

        Gizmos.color = railColor;
        for (int i = 0; i < railPositions.Count - 1; i++)
        {
            Gizmos.DrawLine(railPositions[i], railPositions[i + 1]);
        }

        Gizmos.color = jointColor;
        for (int i = 0; i <= GetPieceCount(); i++)
        {
            Gizmos.DrawWireSphere(SampleRailPoint(i * GetSegmentLength()), 0.015f);
        }
    }

    void OnValidate()
    {
        pieceCount = Mathf.Max(1, pieceCount);
        railSampleMinDistance = Mathf.Max(0.001f, railSampleMinDistance);
        connectionPointUnits = Mathf.Max(0.001f, connectionPointUnits);
        connectionPointUnitScale = Mathf.Max(0.0001f, connectionPointUnitScale);
        globalWidthScale = Mathf.Max(0.01f, globalWidthScale);
        InitializeDefaults();

        if (!Application.isPlaying)
        {
            RebuildSegments(false);
        }
    }
}
