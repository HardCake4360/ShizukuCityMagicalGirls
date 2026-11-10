using UnityEngine;

public class ProjectileAimCompensator : MonoBehaviour
{
    [Header("Motion")]
    [SerializeField] private Transform motionSource;
    [SerializeField] private float velocitySmoothing = 12f;

    [Header("Compensation")]
    [SerializeField] private float compensationStrength = 1f;
    [SerializeField] private float maxPredictionTime = 0.35f;
    [SerializeField] private float minProjectileSpeed = 0.01f;
    [SerializeField] private int predictionIterations = 1;

    [Header("Debug")]
    [SerializeField] private bool drawDebug;
    [SerializeField] private Color rawAimColor = Color.yellow;
    [SerializeField] private Color compensatedAimColor = Color.cyan;

    private Vector2 previousPosition;
    private Vector2 estimatedVelocity;
    private bool hasPreviousPosition;

    private Vector3 lastMuzzlePosition;
    private Vector2 lastRawAimPoint;
    private Vector2 lastCompensatedAimPoint;
    private bool hasDebugSample;

    private void Awake()
    {
        CacheReferences();
        ResetVelocitySample();
    }

    private void OnValidate()
    {
        velocitySmoothing = Mathf.Max(0f, velocitySmoothing);
        compensationStrength = Mathf.Max(0f, compensationStrength);
        maxPredictionTime = Mathf.Max(0f, maxPredictionTime);
        minProjectileSpeed = Mathf.Max(0.0001f, minProjectileSpeed);
        predictionIterations = Mathf.Max(1, predictionIterations);
    }

    private void Update()
    {
        UpdateEstimatedVelocity();
        DrawDebugSample();
    }

    public Vector2 GetCompensatedDirection(
        Vector3 muzzlePosition,
        Vector2 rawAimPoint,
        float projectileSpeed
    )
    {
        Vector2 compensatedAimPoint = GetCompensatedAimPoint(
            muzzlePosition,
            rawAimPoint,
            projectileSpeed
        );

        Vector2 direction = compensatedAimPoint - (Vector2)muzzlePosition;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector2.right;
        }

        return direction.normalized;
    }

    public Vector2 GetCompensatedAimPoint(
        Vector3 muzzlePosition,
        Vector2 rawAimPoint,
        float projectileSpeed
    )
    {
        float safeProjectileSpeed = Mathf.Max(minProjectileSpeed, projectileSpeed);
        Vector2 compensatedAimPoint = rawAimPoint;

        for (int i = 0; i < predictionIterations; i++)
        {
            float distance = Vector2.Distance(muzzlePosition, compensatedAimPoint);
            float travelTime = Mathf.Min(distance / safeProjectileSpeed, maxPredictionTime);
            Vector2 predictedOffset = estimatedVelocity * travelTime * compensationStrength;
            compensatedAimPoint = rawAimPoint + predictedOffset;
        }

        StoreDebugSample(muzzlePosition, rawAimPoint, compensatedAimPoint);
        return compensatedAimPoint;
    }

    public Vector2 GetEstimatedVelocity()
    {
        return estimatedVelocity;
    }

    private void CacheReferences()
    {
        if (motionSource == null)
        {
            motionSource = transform;
        }
    }

    private void ResetVelocitySample()
    {
        if (motionSource == null)
        {
            hasPreviousPosition = false;
            estimatedVelocity = Vector2.zero;
            return;
        }

        previousPosition = motionSource.position;
        estimatedVelocity = Vector2.zero;
        hasPreviousPosition = true;
    }

    private void UpdateEstimatedVelocity()
    {
        if (motionSource == null)
        {
            CacheReferences();
        }

        if (motionSource == null)
        {
            estimatedVelocity = Vector2.zero;
            hasPreviousPosition = false;
            return;
        }

        Vector2 currentPosition = motionSource.position;
        if (!hasPreviousPosition)
        {
            previousPosition = currentPosition;
            estimatedVelocity = Vector2.zero;
            hasPreviousPosition = true;
            return;
        }

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector2 rawVelocity = (currentPosition - previousPosition) / deltaTime;
        float smoothingFactor = velocitySmoothing <= 0f
            ? 1f
            : 1f - Mathf.Exp(-velocitySmoothing * deltaTime);

        estimatedVelocity = Vector2.Lerp(estimatedVelocity, rawVelocity, smoothingFactor);
        previousPosition = currentPosition;
    }

    private void StoreDebugSample(
        Vector3 muzzlePosition,
        Vector2 rawAimPoint,
        Vector2 compensatedAimPoint
    )
    {
        if (!drawDebug)
        {
            return;
        }

        lastMuzzlePosition = muzzlePosition;
        lastRawAimPoint = rawAimPoint;
        lastCompensatedAimPoint = compensatedAimPoint;
        hasDebugSample = true;
    }

    private void DrawDebugSample()
    {
        if (!drawDebug || !hasDebugSample)
        {
            return;
        }

        Debug.DrawLine(lastMuzzlePosition, lastRawAimPoint, rawAimColor);
        Debug.DrawLine(lastMuzzlePosition, lastCompensatedAimPoint, compensatedAimColor);
    }
}
