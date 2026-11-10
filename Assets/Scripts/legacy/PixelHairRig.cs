using System;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class PixelHairRig : MonoBehaviour
{
    [Serializable]
    public class Strand
    {
        [Header("References")]
        public string strandName = "Hair Strand";
        public Transform root;
        public Transform visual;

        [Header("Shape")]
        public Vector2 restDirection = Vector2.down;
        public Vector2 visualTipAxis = Vector2.down;
        [Min(0.01f)] public float length = 0.35f;
        [Range(0f, 180f)] public float maxSwingAngle = 35f;
        public float rotationOffsetDegrees;

        [Header("Motion")]
        [Range(0f, 3f)] public float inertia = 0.65f;
        [Min(0f)] public float gravity = 0.45f;
        [Min(0f)] public float stiffness = 28f;
        [Min(0f)] public float damping = 9f;

        [NonSerialized] public bool initialized;
        [NonSerialized] public Vector2 tipPosition;
        [NonSerialized] public Vector2 previousTipPosition;
    }

    [Header("Motion Source")]
    [FormerlySerializedAs("motionSource")]
    [Tooltip("Optional. When empty, this rig measures its parent transform velocity.")]
    [SerializeField] Transform motionSourceOverride;
    [SerializeField] Transform restDirectionSpace;
    [SerializeField] Vector2 extraForce;

    [Header("Pixel Art")]
    [SerializeField] bool snapToPixelGrid = true;
    [SerializeField, Min(1f)] float pixelsPerUnit = 16f;
    [SerializeField, Min(0f)] float rotationStepDegrees = 5f;

    [Header("Strands")]
    [SerializeField] Strand[] strands = new Strand[0];

    Vector3 previousMotionPosition;
    bool hasPreviousMotionPosition;

    public Vector2 CurrentSourceVelocity { get; private set; }

    void Reset()
    {
        motionSourceOverride = null;
        restDirectionSpace = transform;
    }

    void OnEnable()
    {
        if (restDirectionSpace == null)
        {
            restDirectionSpace = transform;
        }

        previousMotionPosition = GetMotionSource().position;
        hasPreviousMotionPosition = true;
        ResetSimulation();
    }

    void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        CurrentSourceVelocity = GetSourceVelocity(deltaTime);

        for (int i = 0; i < strands.Length; i++)
        {
            SimulateStrand(strands[i], deltaTime, CurrentSourceVelocity);
        }

        previousMotionPosition = GetMotionSource().position;
        hasPreviousMotionPosition = true;
    }

    public void ResetSimulation()
    {
        for (int i = 0; i < strands.Length; i++)
        {
            Strand strand = strands[i];
            Transform root = GetRoot(strand);
            if (root == null)
            {
                continue;
            }

            Vector2 rootPosition = root.position;
            Vector2 restDirection = GetWorldRestDirection(strand, root);
            Vector2 tipPosition = rootPosition + restDirection * Mathf.Max(0.01f, strand.length);

            strand.tipPosition = tipPosition;
            strand.previousTipPosition = tipPosition;
            strand.initialized = true;
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

    void SimulateStrand(Strand strand, float deltaTime, Vector2 sourceVelocity)
    {
        if (strand == null)
        {
            return;
        }

        Transform root = GetRoot(strand);
        Transform visual = GetVisual(strand, root);
        if (root == null || visual == null)
        {
            return;
        }

        Vector2 rootPosition = root.position;
        Vector2 restDirection = GetWorldRestDirection(strand, root);
        float length = Mathf.Max(0.01f, strand.length);

        if (!strand.initialized)
        {
            Vector2 tipPosition = rootPosition + restDirection * length;
            strand.tipPosition = tipPosition;
            strand.previousTipPosition = tipPosition;
            strand.initialized = true;
        }

        Vector2 currentTip = strand.tipPosition;
        Vector2 tipVelocity = (strand.tipPosition - strand.previousTipPosition) * Mathf.Exp(-strand.damping * deltaTime);
        Vector2 acceleration = Vector2.down * strand.gravity - sourceVelocity * strand.inertia + extraForce;
        Vector2 restTip = rootPosition + restDirection * length;

        strand.previousTipPosition = currentTip;
        strand.tipPosition += tipVelocity + acceleration * deltaTime * deltaTime;
        strand.tipPosition = Vector2.Lerp(
            strand.tipPosition,
            restTip,
            1f - Mathf.Exp(-strand.stiffness * deltaTime)
        );

        Vector2 rootToTip = strand.tipPosition - rootPosition;
        Vector2 direction = rootToTip.sqrMagnitude > 0.0001f ? rootToTip.normalized : restDirection;
        direction = ClampSwing(direction, restDirection, strand.maxSwingAngle);
        strand.tipPosition = rootPosition + direction * length;

        ApplyVisual(strand, visual, rootPosition, direction);
    }

    void ApplyVisual(Strand strand, Transform visual, Vector2 rootPosition, Vector2 direction)
    {
        Vector2 visualPosition = snapToPixelGrid ? SnapToGrid(rootPosition) : rootPosition;
        visual.position = new Vector3(visualPosition.x, visualPosition.y, visual.position.z);

        Vector2 visualAxis = GetSafeDirection(strand.visualTipAxis, Vector2.down);
        float directionAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float visualAxisAngle = Mathf.Atan2(visualAxis.y, visualAxis.x) * Mathf.Rad2Deg;
        float targetAngle = directionAngle - visualAxisAngle + strand.rotationOffsetDegrees;

        if (rotationStepDegrees > 0f)
        {
            targetAngle = Mathf.Round(targetAngle / rotationStepDegrees) * rotationStepDegrees;
        }

        visual.rotation = Quaternion.Euler(0f, 0f, targetAngle);
    }

    Transform GetRoot(Strand strand)
    {
        if (strand == null)
        {
            return null;
        }

        return strand.root != null ? strand.root : transform;
    }

    Transform GetVisual(Strand strand, Transform root)
    {
        if (strand == null)
        {
            return null;
        }

        if (strand.visual != null)
        {
            return strand.visual;
        }

        return root;
    }

    Vector2 GetWorldRestDirection(Strand strand, Transform root)
    {
        Vector2 localDirection = GetSafeDirection(strand.restDirection, Vector2.down);
        Transform directionSpace = restDirectionSpace != null ? restDirectionSpace : transform;
        Vector3 worldDirection = directionSpace.TransformDirection(localDirection);
        return GetSafeDirection(new Vector2(worldDirection.x, worldDirection.y), Vector2.down);
    }

    Vector2 ClampSwing(Vector2 direction, Vector2 restDirection, float maxSwingAngle)
    {
        if (maxSwingAngle >= 180f)
        {
            return direction;
        }

        float signedAngle = Vector2.SignedAngle(restDirection, direction);
        float clampedAngle = Mathf.Clamp(signedAngle, -maxSwingAngle, maxSwingAngle);
        return Quaternion.Euler(0f, 0f, clampedAngle) * restDirection;
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

    void OnDrawGizmosSelected()
    {
        if (strands == null)
        {
            return;
        }

        Gizmos.color = Color.magenta;

        for (int i = 0; i < strands.Length; i++)
        {
            Strand strand = strands[i];
            Transform root = GetRoot(strand);
            if (strand == null || root == null)
            {
                continue;
            }

            Vector2 rootPosition = root.position;
            Vector2 tipPosition = strand.initialized
                ? strand.tipPosition
                : rootPosition + GetWorldRestDirection(strand, root) * Mathf.Max(0.01f, strand.length);

            Gizmos.DrawLine(rootPosition, tipPosition);
            Gizmos.DrawWireSphere(tipPosition, 0.03f);
        }
    }
}
