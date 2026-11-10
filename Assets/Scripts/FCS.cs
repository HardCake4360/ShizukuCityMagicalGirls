using UnityEngine;

public class FCS : MonoBehaviour
{
    [Header("Tracking")]
    [SerializeField] private Vector2 targetVelocity;

    [Header("Accuracy")]
    [SerializeField] private float aimErrorRadius = 0f;
    [SerializeField] private bool scaleErrorByDistance = false;
    [SerializeField] private float distanceErrorMultiplier = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Color aimLineColor = Color.cyan;
    [SerializeField] private Color interceptPointColor = Color.red;
    [SerializeField] private Color targetVelocityColor = Color.green;
    [SerializeField] private float interceptPointRadius = 0.15f;
    [SerializeField] private float velocityVectorScale = 0.2f;

    private Transform trackedTarget;
    private Vector2 lastTargetPosition;
    private bool hasTargetSample;
    private Vector2 lastAimOrigin;
    private Vector2 lastPredictedHitPoint;
    private float lastProjectileSpeed;
    private bool hasAimSolution;

    private void LateUpdate()
    {
        if (trackedTarget == null)
        {
            hasTargetSample = false;
            targetVelocity = Vector2.zero;
            return;
        }

        Vector2 currentPosition = trackedTarget.position;
        if (!hasTargetSample)
        {
            lastTargetPosition = currentPosition;
            hasTargetSample = true;
            targetVelocity = Vector2.zero;
            return;
        }

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        targetVelocity = (currentPosition - lastTargetPosition) / deltaTime;
        lastTargetPosition = currentPosition;
    }

    public Vector2 GetAimDirection(Vector2 origin, Transform target, float projectileSpeed)
    {
        if (target == null)
        {
            hasAimSolution = false;
            return Vector2.right;
        }

        if (trackedTarget != target)
        {
            trackedTarget = target;
            lastTargetPosition = target.position;
            hasTargetSample = false;
            targetVelocity = Vector2.zero;
        }

        Vector2 originToTarget = (Vector2)target.position - origin;
        if (originToTarget.sqrMagnitude <= 0.0001f)
        {
            return Vector2.right;
        }

        Vector2 interceptPoint = CalculateInterceptPoint(
            origin,
            (Vector2)target.position,
            targetVelocity,
            projectileSpeed
        );
        interceptPoint += GetAimErrorOffset(origin, interceptPoint);
        lastAimOrigin = origin;
        lastPredictedHitPoint = interceptPoint;
        lastProjectileSpeed = projectileSpeed;
        hasAimSolution = true;

        Vector2 aimDirection = interceptPoint - origin;
        if (aimDirection.sqrMagnitude <= 0.0001f)
        {
            return originToTarget.normalized;
        }

        return aimDirection.normalized;
    }

    public Vector2 GetPredictedHitPoint(Vector2 origin, Transform target, float projectileSpeed)
    {
        if (target == null)
        {
            return origin;
        }

        return CalculateInterceptPoint(
            origin,
            target.position,
            targetVelocity,
            projectileSpeed
        );
    }

    public Vector2 GetTrackedTargetVelocity()
    {
        return targetVelocity;
    }

    private Vector2 GetAimErrorOffset(Vector2 origin, Vector2 interceptPoint)
    {
        float errorRadius = Mathf.Max(0f, aimErrorRadius);
        if (scaleErrorByDistance)
        {
            errorRadius += Vector2.Distance(origin, interceptPoint) * Mathf.Max(0f, distanceErrorMultiplier);
        }

        if (errorRadius <= 0.0001f)
        {
            return Vector2.zero;
        }

        return Random.insideUnitCircle * errorRadius;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos || !hasAimSolution || trackedTarget == null)
        {
            return;
        }

        Gizmos.color = aimLineColor;
        Gizmos.DrawLine(lastAimOrigin, lastPredictedHitPoint);

        Gizmos.color = interceptPointColor;
        Gizmos.DrawSphere(lastPredictedHitPoint, interceptPointRadius);

        Gizmos.color = targetVelocityColor;
        Vector3 targetPosition = trackedTarget.position;
        Vector3 velocityEnd = targetPosition + ((Vector3)targetVelocity * velocityVectorScale);
        Gizmos.DrawLine(targetPosition, velocityEnd);
        Gizmos.DrawSphere(velocityEnd, interceptPointRadius * 0.6f);
    }

    private Vector2 CalculateInterceptPoint(
        Vector2 shooterPosition,
        Vector2 targetPosition,
        Vector2 targetMoveVelocity,
        float projectileSpeed
    )
    {
        if (projectileSpeed <= 0f)
        {
            return targetPosition;
        }

        Vector2 displacement = targetPosition - shooterPosition;
        float a = Vector2.Dot(targetMoveVelocity, targetMoveVelocity) - (projectileSpeed * projectileSpeed);
        float b = 2f * Vector2.Dot(displacement, targetMoveVelocity);
        float c = Vector2.Dot(displacement, displacement);

        float interceptTime = SolveInterceptTime(a, b, c);
        if (interceptTime <= 0f)
        {
            return targetPosition;
        }

        return targetPosition + (targetMoveVelocity * interceptTime);
    }

    private float SolveInterceptTime(float a, float b, float c)
    {
        const float epsilon = 0.0001f;

        if (Mathf.Abs(a) < epsilon)
        {
            if (Mathf.Abs(b) < epsilon)
            {
                return 0f;
            }

            float linearTime = -c / b;
            return linearTime > 0f ? linearTime : 0f;
        }

        float discriminant = (b * b) - (4f * a * c);
        if (discriminant < 0f)
        {
            return 0f;
        }

        float sqrtDiscriminant = Mathf.Sqrt(discriminant);
        float t1 = (-b + sqrtDiscriminant) / (2f * a);
        float t2 = (-b - sqrtDiscriminant) / (2f * a);

        bool t1Valid = t1 > 0f;
        bool t2Valid = t2 > 0f;

        if (t1Valid && t2Valid)
        {
            return Mathf.Min(t1, t2);
        }

        if (t1Valid)
        {
            return t1;
        }

        if (t2Valid)
        {
            return t2;
        }

        return 0f;
    }
}
