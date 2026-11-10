using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class PixelRopeHairRig : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform root;
    [SerializeField] LineRenderer line;

    [Header("Motion Source")]
    [FormerlySerializedAs("motionSource")]
    [Tooltip("Optional. When empty, this rig measures its parent transform velocity.")]
    [SerializeField] Transform motionSourceOverride;
    [SerializeField] Transform restDirectionSpace;
    [SerializeField] Vector2 extraForce;

    [Header("Shape")]
    [SerializeField] Vector2 restDirection = Vector2.down;
    [SerializeField, Min(1)] int segmentCount = 5;
    [SerializeField, Min(0.01f)] float segmentLength = 0.08f;
    [SerializeField, Range(0f, 180f)] float maxSwingAngle = 75f;

    [Header("Line")]
    [SerializeField, Min(0.001f)] float width = 0.06f;
    [SerializeField] AnimationCurve widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.35f);
    [SerializeField] Color color = Color.white;
    [SerializeField] string sortingLayerName = "Default";
    [SerializeField] int sortingOrder;

    [Header("Motion")]
    [SerializeField, Range(0f, 3f)] float inertia = 0.75f;
    [SerializeField, Min(0f)] float gravity = 0.6f;
    [SerializeField, Min(0f)] float stiffness = 7f;
    [SerializeField, Range(0f, 1f)] float damping = 0.86f;
    [SerializeField, Range(1, 16)] int constraintIterations = 6;

    [Header("Pixel Rendering")]
    [SerializeField] bool snapRenderPointsToPixelGrid = true;
    [SerializeField, Min(1f)] float pixelsPerUnit = 16f;

    Vector2[] points;
    Vector2[] previousPoints;
    Vector3[] renderPoints;
    Vector3 previousMotionPosition;
    bool hasPreviousMotionPosition;
    Material fallbackLineMaterial;

    public Vector2 CurrentSourceVelocity { get; private set; }

    void Reset()
    {
        root = transform;
        line = GetComponent<LineRenderer>();
        motionSourceOverride = null;
        restDirectionSpace = transform;
    }

    void OnEnable()
    {
        if (line == null)
        {
            line = GetComponent<LineRenderer>();
        }

        if (root == null)
        {
            root = transform;
        }

        if (restDirectionSpace == null)
        {
            restDirectionSpace = transform;
        }

        previousMotionPosition = GetMotionSource().position;
        hasPreviousMotionPosition = true;
        ResetSimulation();
    }

    void OnDisable()
    {
        if (fallbackLineMaterial != null)
        {
            Destroy(fallbackLineMaterial);
            fallbackLineMaterial = null;
        }
    }

    void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        CurrentSourceVelocity = GetSourceVelocity(deltaTime);
        Simulate(deltaTime, CurrentSourceVelocity);
        Render();

        previousMotionPosition = GetMotionSource().position;
        hasPreviousMotionPosition = true;
    }

    public void ResetSimulation()
    {
        EnsureBuffers();

        Vector2 rootPosition = GetRootPosition();
        Vector2 worldRestDirection = GetWorldRestDirection();

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 pointPosition = rootPosition + worldRestDirection * segmentLength * i;
            points[i] = pointPosition;
            previousPoints[i] = pointPosition;
        }

        ConfigureLine();
        Render();
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
            Vector2 restPoint = rootPosition + worldRestDirection * segmentLength * i;

            previousPoints[i] = current;
            points[i] += velocity + acceleration * deltaTime * deltaTime;
            points[i] = Vector2.Lerp(points[i], restPoint, stiffnessFactor);
        }

        int iterations = Mathf.Max(1, constraintIterations);
        for (int i = 0; i < iterations; i++)
        {
            ApplyDistanceConstraints(rootPosition);
            ApplySwingLimit(rootPosition, worldRestDirection);
        }
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

    void ApplySwingLimit(Vector2 rootPosition, Vector2 worldRestDirection)
    {
        if (maxSwingAngle >= 180f)
        {
            return;
        }

        points[0] = rootPosition;

        for (int i = 1; i < points.Length; i++)
        {
            Vector2 fromRoot = points[i] - rootPosition;
            float distanceFromRoot = fromRoot.magnitude;
            if (distanceFromRoot <= 0.0001f)
            {
                continue;
            }

            Vector2 direction = fromRoot / distanceFromRoot;
            float signedAngle = Vector2.SignedAngle(worldRestDirection, direction);
            float clampedAngle = Mathf.Clamp(signedAngle, -maxSwingAngle, maxSwingAngle);
            Vector2 clampedDirection = Quaternion.Euler(0f, 0f, clampedAngle) * worldRestDirection;
            points[i] = rootPosition + clampedDirection * distanceFromRoot;
        }
    }

    void Render()
    {
        if (line == null || points == null)
        {
            return;
        }

        EnsureBuffers();
        ConfigureLine();

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 point = snapRenderPointsToPixelGrid ? SnapToGrid(points[i]) : points[i];
            renderPoints[i] = new Vector3(point.x, point.y, transform.position.z);
        }

        line.SetPositions(renderPoints);
    }

    void ConfigureLine()
    {
        if (line == null)
        {
            return;
        }

        line.useWorldSpace = true;
        line.positionCount = GetPointCount();
        line.widthMultiplier = width;
        line.widthCurve = widthCurve != null ? widthCurve : AnimationCurve.Linear(0f, 1f, 1f, 0.35f);
        line.startColor = color;
        line.endColor = color;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.sortingLayerName = sortingLayerName;
        line.sortingOrder = sortingOrder;

        if (line.sharedMaterial == null)
        {
            line.sharedMaterial = GetFallbackLineMaterial();
        }
    }

    void EnsureBuffers()
    {
        int pointCount = GetPointCount();
        if (points != null && points.Length == pointCount)
        {
            return;
        }

        points = new Vector2[pointCount];
        previousPoints = new Vector2[pointCount];
        renderPoints = new Vector3[pointCount];

        Vector2 rootPosition = GetRootPosition();
        Vector2 worldRestDirection = GetWorldRestDirection();

        for (int i = 0; i < pointCount; i++)
        {
            Vector2 pointPosition = rootPosition + worldRestDirection * segmentLength * i;
            points[i] = pointPosition;
            previousPoints[i] = pointPosition;
            renderPoints[i] = pointPosition;
        }
    }

    int GetPointCount()
    {
        return Mathf.Max(2, segmentCount + 1);
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

    Vector2 SnapToGrid(Vector2 position)
    {
        return new Vector2(
            Mathf.Round(position.x * pixelsPerUnit) / pixelsPerUnit,
            Mathf.Round(position.y * pixelsPerUnit) / pixelsPerUnit
        );
    }

    Vector2 GetSafeDirection(Vector2 direction, Vector2 fallback)
    {
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return fallback.normalized;
        }

        return direction.normalized;
    }

    Material GetFallbackLineMaterial()
    {
        if (fallbackLineMaterial != null)
        {
            return fallbackLineMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        }

        fallbackLineMaterial = new Material(shader)
        {
            name = "Pixel Rope Hair Line Material"
        };

        return fallbackLineMaterial;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;

        Vector2 rootPosition = GetRootPosition();
        Vector2 worldRestDirection = GetWorldRestDirection();
        int pointCount = GetPointCount();

        for (int i = 0; i < pointCount - 1; i++)
        {
            Vector2 start = points != null && points.Length == pointCount
                ? points[i]
                : rootPosition + worldRestDirection * segmentLength * i;
            Vector2 end = points != null && points.Length == pointCount
                ? points[i + 1]
                : rootPosition + worldRestDirection * segmentLength * (i + 1);

            Gizmos.DrawLine(start, end);
        }
    }
}
