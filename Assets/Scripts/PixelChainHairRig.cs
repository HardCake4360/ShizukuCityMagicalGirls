using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class PixelChainHairRig : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform root;
    [SerializeField] Transform segmentContainer;

    [Header("Sprites")]
    [SerializeField] Sprite[] middleSprites = new Sprite[0];
    [SerializeField] Sprite tipSprite;
    [SerializeField] Color tint = Color.white;
    [SerializeField] string sortingLayerName = "Default";
    [SerializeField] int sortingOrder;
    [SerializeField] int sortingOrderStep;

    [Header("Motion Source")]
    [FormerlySerializedAs("motionSource")]
    [Tooltip("Optional. When empty, this rig measures its parent transform velocity.")]
    [SerializeField] Transform motionSourceOverride;
    [SerializeField] Transform restDirectionSpace;
    [SerializeField] Vector2 extraForce;

    [Header("Chain")]
    [Tooltip("Number of visible hair pieces. This includes the final tip piece.")]
    [SerializeField, Min(1)] int pieceCount = 5;
    [SerializeField] Vector2 restDirection = Vector2.down;

    [Header("Physics")]
    [SerializeField, Range(0f, 3f)] float inertia = 0.75f;
    [SerializeField, Min(0f)] float gravity = 0.6f;
    [SerializeField, Min(0f)] float stiffness = 7f;
    [SerializeField, Range(0f, 1f)] float damping = 0.86f;
    [SerializeField, Range(1, 16)] int constraintIterations = 6;

    [Header("Lateral Flutter")]
    [SerializeField] bool enableLateralFlutter = true;
    [SerializeField, Min(0f)] float flutterStrength = 0.9f;
    [SerializeField, Min(0f)] float flutterFrequency = 7f;
    [SerializeField] float flutterPhaseOffset = 0.75f;
    [SerializeField, Min(0f)] float flutterMinMotionSpeed = 0.05f;
    [SerializeField, Min(0.001f)] float flutterMaxMotionSpeed = 4f;
    [SerializeField, Range(0f, 4f)] float flutterTipExponent = 1.4f;

    [Header("Sprite Deformation")]
    [Tooltip("Inspector-friendly connection distance value. Effective distance is Units multiplied by Unit Scale.")]
    [SerializeField, Min(0.001f)] float connectionPointUnits = 4f;
    [SerializeField, Min(0.0001f)] float connectionPointUnitScale = 0.01f;
    [SerializeField] bool stretchSpritesToJointDistance = true;
    [SerializeField] AnimationCurve widthScaleByPiece = AnimationCurve.Linear(0f, 1f, 1f, 0.7f);
    [SerializeField, Min(0.01f)] float globalWidthScale = 1f;

    [Header("Sprite Alignment")]
    [SerializeField] float rotationOffsetDegrees;

    [Header("Debug Visualization")]
    [SerializeField] bool showConnectionPointGizmos = true;
    [SerializeField, Min(0.001f)] float connectionPointGizmoRadius = 0.015f;
    [SerializeField] Color upperConnectionColor = Color.yellow;
    [SerializeField] Color lowerConnectionColor = Color.green;
    [SerializeField] Color connectionAxisColor = Color.white;
    [SerializeField] Color jointColor = Color.cyan;
    [SerializeField, HideInInspector] List<SpriteRenderer> segmentRenderers = new List<SpriteRenderer>();
    [SerializeField, HideInInspector] List<int> middleSpriteIndices = new List<int>();

    Vector2[] points;
    Vector2[] previousPoints;
    Vector3 previousMotionPosition;
    bool hasPreviousMotionPosition;
    int builtPieceCount = -1;
    float flutterSpeedWeight;

    public int PieceCount => GetPieceCount();
    public Vector2 CurrentSourceVelocity { get; private set; }

    void Reset()
    {
        root = transform;
        segmentContainer = transform;
        motionSourceOverride = null;
        restDirectionSpace = transform;
    }

    void OnEnable()
    {
        InitializeDefaults();
        RebuildSegments(false);

        previousMotionPosition = GetMotionSource().position;
        hasPreviousMotionPosition = true;
        ResetSimulation();
    }

    void LateUpdate()
    {
        SyncPieceCountIfNeeded();

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        CurrentSourceVelocity = GetSourceVelocity(deltaTime);
        flutterSpeedWeight = GetFlutterSpeedWeight(CurrentSourceVelocity.magnitude);
        Simulate(deltaTime, CurrentSourceVelocity);
        RenderSegments();

        previousMotionPosition = GetMotionSource().position;
        hasPreviousMotionPosition = true;
    }

    [ContextMenu("Rebuild Segments")]
    public void RebuildSegments()
    {
        RebuildSegments(true);
    }

    public void SetPieceCount(int newPieceCount)
    {
        SetPieceCount(newPieceCount, false, false);
    }

    public void SetPieceCount(int newPieceCount, bool randomizeMiddleSprites)
    {
        SetPieceCount(newPieceCount, randomizeMiddleSprites, false);
    }

    public void SetPieceCount(int newPieceCount, bool randomizeMiddleSprites, bool resetSimulation)
    {
        pieceCount = Mathf.Max(1, newPieceCount);
        ApplyPieceCountChange(randomizeMiddleSprites, resetSimulation);
    }

    void SyncPieceCountIfNeeded()
    {
        if (!IsSegmentStructureDirty())
        {
            return;
        }

        ApplyPieceCountChange(false, false);
    }

    bool IsSegmentStructureDirty()
    {
        int count = GetPieceCount();
        if (builtPieceCount != count || segmentRenderers.Count != count)
        {
            return true;
        }

        for (int i = 0; i < segmentRenderers.Count; i++)
        {
            if (segmentRenderers[i] == null)
            {
                return true;
            }
        }

        return points == null || points.Length != GetPointCount();
    }

    void ApplyPieceCountChange(bool randomizeMiddleSprites, bool resetSimulation)
    {
        RebuildSegments(randomizeMiddleSprites);

        if (resetSimulation)
        {
            ResetSimulation();
            return;
        }

        EnsureBuffers();
        RenderSegments();
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

        for (int i = 0; i < segmentRenderers.Count; i++)
        {
            SpriteRenderer segmentRenderer = segmentRenderers[i];
            if (segmentRenderer == null)
            {
                continue;
            }

            segmentRenderer.name = GetSegmentName(i);
            ApplyRendererSettings(segmentRenderer, i);
        }

        EnsureBuffers();
        RenderSegments();
        CleanupOrphanSegmentObjects();
        builtPieceCount = count;
    }

    [ContextMenu("Randomize Middle Sprites")]
    public void RandomizeMiddleSprites()
    {
        EnsureMiddleSpriteChoices(true);

        for (int i = 0; i < segmentRenderers.Count; i++)
        {
            SpriteRenderer segmentRenderer = segmentRenderers[i];
            if (segmentRenderer == null)
            {
                continue;
            }

            ApplyRendererSettings(segmentRenderer, i);
        }
    }

    public void ResetSimulation()
    {
        EnsureBuffers();

        Vector2 rootPosition = GetRootPosition();
        Vector2 worldRestDirection = GetWorldRestDirection();

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 pointPosition = rootPosition + worldRestDirection * GetSegmentLength() * i;
            points[i] = pointPosition;
            previousPoints[i] = pointPosition;
        }

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

        if (restDirectionSpace == null)
        {
            restDirectionSpace = transform;
        }
    }

    Vector2 GetSourceVelocity(float deltaTime)
    {
        Transform source = GetMotionSource();
        if (!hasPreviousMotionPosition)
        {
            return Vector2.zero;
        }

        Vector3 positionDelta = source.position - previousMotionPosition;
        return new Vector2(positionDelta.x, positionDelta.y) / deltaTime;
    }

    Transform GetMotionSource()
    {
        if (motionSourceOverride != null)
        {
            return motionSourceOverride;
        }

        if (transform.parent != null)
        {
            return transform.parent;
        }

        return transform;
    }

    float GetFlutterSpeedWeight(float speed)
    {
        if (flutterMaxMotionSpeed <= flutterMinMotionSpeed)
        {
            return speed >= flutterMinMotionSpeed ? 1f : 0f;
        }

        return Mathf.InverseLerp(flutterMinMotionSpeed, flutterMaxMotionSpeed, speed);
    }

    void Simulate(float deltaTime, Vector2 sourceVelocity)
    {
        EnsureBuffers();

        Vector2 rootPosition = GetRootPosition();
        Vector2 worldRestDirection = GetWorldRestDirection();
        Vector2 acceleration = Vector2.down * gravity - sourceVelocity * inertia + extraForce;
        float stiffnessFactor = 1f - Mathf.Exp(-stiffness * deltaTime);

        points[0] = rootPosition;
        previousPoints[0] = rootPosition;

        for (int i = 1; i < points.Length; i++)
        {
            Vector2 current = points[i];
            Vector2 velocity = (points[i] - previousPoints[i]) * damping;
            Vector2 restPoint = rootPosition + worldRestDirection * GetSegmentLength() * i;
            Vector2 lateralAcceleration = GetLateralFlutterAcceleration(i);

            previousPoints[i] = current;
            points[i] += velocity + (acceleration + lateralAcceleration) * deltaTime * deltaTime;
            points[i] = Vector2.Lerp(points[i], restPoint, stiffnessFactor);
        }

        int iterations = Mathf.Max(1, constraintIterations);
        for (int i = 0; i < iterations; i++)
        {
            ApplyDistanceConstraints(rootPosition);
        }
    }

    Vector2 GetLateralFlutterAcceleration(int pointIndex)
    {
        if (!enableLateralFlutter || flutterSpeedWeight <= 0f || points == null || points.Length < 2)
        {
            return Vector2.zero;
        }

        Vector2 chainDirection = points[points.Length - 1] - points[0];
        if (chainDirection.sqrMagnitude <= 0.0001f)
        {
            chainDirection = GetWorldRestDirection();
        }

        chainDirection.Normalize();
        Vector2 sideDirection = new Vector2(-chainDirection.y, chainDirection.x);
        float pointTime = Mathf.Clamp01(pointIndex / (float)Mathf.Max(1, points.Length - 1));
        float tipWeight = Mathf.Pow(pointTime, flutterTipExponent);
        float wave = Mathf.Sin(Time.time * flutterFrequency + pointIndex * flutterPhaseOffset);

        return sideDirection * (wave * flutterStrength * flutterSpeedWeight * tipWeight);
    }

    void ApplyDistanceConstraints(Vector2 rootPosition)
    {
        points[0] = rootPosition;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 start = points[i];
            Vector2 end = points[i + 1];
            Vector2 delta = end - start;
            float distance = delta.magnitude;
            Vector2 direction = distance > 0.0001f ? delta / distance : Vector2.down;
            float segmentLength = GetSegmentLength();
            float difference = distance - segmentLength;

            if (i == 0)
            {
                points[i + 1] = start + direction * segmentLength;
                continue;
            }

            Vector2 correction = direction * (difference * 0.5f);
            points[i] += correction;
            points[i + 1] -= correction;
        }

        points[0] = rootPosition;
    }

    void RenderSegments()
    {
        if (points == null)
        {
            return;
        }

        for (int i = 0; i < segmentRenderers.Count && i < points.Length - 1; i++)
        {
            SpriteRenderer segmentRenderer = segmentRenderers[i];
            if (segmentRenderer == null)
            {
                continue;
            }

            ApplyRendererSettings(segmentRenderer, i);
            ApplySegmentTransform(segmentRenderer.transform, segmentRenderer.sprite, i);
        }
    }

    void ApplyRendererSettings(SpriteRenderer segmentRenderer, int index)
    {
        segmentRenderer.sprite = GetSpriteForSegment(index);
        segmentRenderer.color = tint;
        segmentRenderer.sortingLayerName = sortingLayerName;
        segmentRenderer.sortingOrder = sortingOrder + sortingOrderStep * index;
    }

    void ApplySegmentTransform(Transform segmentTransform, Sprite sprite, int index)
    {
        Vector2 start = points[index];
        Vector2 end = points[index + 1];
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

    float GetAngleForDirection(Vector2 direction)
    {
        float directionAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float downwardAxisAngle = -90f;
        float targetAngle = directionAngle - downwardAxisAngle + rotationOffsetDegrees;

        return targetAngle;
    }

    Vector3 GetScaleForSegment(int index, float jointDistance)
    {
        float normalizedIndex = GetPieceCount() <= 1 ? 1f : index / (float)(GetPieceCount() - 1);
        float widthScale = globalWidthScale * EvaluateCurve(widthScaleByPiece, normalizedIndex, 1f);
        float lengthScale = 1f;

        if (stretchSpritesToJointDistance)
        {
            lengthScale = jointDistance / GetSegmentLength();
        }

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

    void EnsureBuffers()
    {
        int pointCount = GetPointCount();
        if (points != null && points.Length == pointCount)
        {
            return;
        }

        Vector2[] oldPoints = points;
        Vector2[] oldPreviousPoints = previousPoints;
        points = new Vector2[pointCount];
        previousPoints = new Vector2[pointCount];

        Vector2 rootPosition = GetRootPosition();
        Vector2 worldRestDirection = GetWorldRestDirection();
        int copiedPointCount = oldPoints != null ? Mathf.Min(oldPoints.Length, pointCount) : 0;

        for (int i = 0; i < copiedPointCount; i++)
        {
            points[i] = oldPoints[i];
            previousPoints[i] = oldPreviousPoints != null && i < oldPreviousPoints.Length
                ? oldPreviousPoints[i]
                : oldPoints[i];
        }

        if (copiedPointCount > 0)
        {
            points[0] = rootPosition;
            previousPoints[0] = rootPosition;
        }

        for (int i = copiedPointCount; i < pointCount; i++)
        {
            Vector2 previousPoint = i > 0 ? points[i - 1] : rootPosition;
            Vector2 pointPosition = previousPoint + worldRestDirection * GetSegmentLength();
            points[i] = pointPosition;
            previousPoints[i] = pointPosition;
        }
    }

    int GetPieceCount()
    {
        return Mathf.Max(1, pieceCount);
    }

    int GetPointCount()
    {
        return GetPieceCount() + 1;
    }

    float GetSegmentLength()
    {
        return GetConnectionPointDistance() * 2f;
    }

    float GetConnectionPointDistance()
    {
        return Mathf.Max(0.0001f, connectionPointUnits * connectionPointUnitScale);
    }

    Vector2 GetRootPosition()
    {
        Transform rootTransform = root != null ? root : transform;
        return rootTransform.position;
    }

    Vector2 GetWorldRestDirection()
    {
        Vector2 localDirection = GetSafeDirection(restDirection, Vector2.down);
        Transform directionSpace = restDirectionSpace != null ? restDirectionSpace : transform;
        Vector3 worldDirection = directionSpace.TransformDirection(localDirection);
        return GetSafeDirection(new Vector2(worldDirection.x, worldDirection.y), Vector2.down);
    }

    Vector2 GetSafeDirection(Vector2 direction, Vector2 fallback)
    {
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return fallback.normalized;
        }

        return direction.normalized;
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
            ? "HairPiece_Tip"
            : $"HairPiece_Middle_{index:00}";
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
        return objectName.StartsWith("HairPiece_Middle_") || objectName == "HairPiece_Tip";
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
        Vector2 rootPosition = GetRootPosition();
        Vector2 worldRestDirection = GetWorldRestDirection();
        int pointCount = GetPointCount();

        for (int i = 0; i < pointCount - 1; i++)
        {
            Vector2 start = points != null && points.Length == pointCount
                ? points[i]
                : rootPosition + worldRestDirection * GetSegmentLength() * i;
            Vector2 end = points != null && points.Length == pointCount
                ? points[i + 1]
                : rootPosition + worldRestDirection * GetSegmentLength() * (i + 1);

            Gizmos.color = jointColor;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, connectionPointGizmoRadius * 0.75f);
            if (i == pointCount - 2)
            {
                Gizmos.DrawWireSphere(end, connectionPointGizmoRadius * 0.75f);
            }
        }

        if (!showConnectionPointGizmos)
        {
            return;
        }

        for (int i = 0; i < segmentRenderers.Count; i++)
        {
            SpriteRenderer segmentRenderer = segmentRenderers[i];
            if (segmentRenderer == null || segmentRenderer.sprite == null)
            {
                continue;
            }

            Vector3 center = segmentRenderer.transform.TransformPoint(segmentRenderer.sprite.bounds.center);
            float connectionPointDistance = GetConnectionPointDistance();
            Vector3 upper = segmentRenderer.transform.TransformPoint(
                segmentRenderer.sprite.bounds.center + Vector3.up * connectionPointDistance
            );
            Vector3 lower = segmentRenderer.transform.TransformPoint(
                segmentRenderer.sprite.bounds.center + Vector3.down * connectionPointDistance
            );

            Gizmos.color = connectionAxisColor;
            Gizmos.DrawLine(upper, lower);

            Gizmos.color = upperConnectionColor;
            Gizmos.DrawSphere(upper, connectionPointGizmoRadius);

            Gizmos.color = lowerConnectionColor;
            Gizmos.DrawSphere(lower, connectionPointGizmoRadius);

            Gizmos.color = connectionAxisColor;
            Gizmos.DrawWireSphere(center, connectionPointGizmoRadius * 0.5f);
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        pieceCount = Mathf.Max(1, pieceCount);
        flutterMaxMotionSpeed = Mathf.Max(flutterMinMotionSpeed + 0.001f, flutterMaxMotionSpeed);
        connectionPointUnits = Mathf.Max(0.001f, connectionPointUnits);
        connectionPointUnitScale = Mathf.Max(0.0001f, connectionPointUnitScale);
        globalWidthScale = Mathf.Max(0.01f, globalWidthScale);
        InitializeDefaults();

        if (!Application.isPlaying)
        {
            RebuildSegments(false);
            ResetSimulation();
        }
    }
#endif
}
