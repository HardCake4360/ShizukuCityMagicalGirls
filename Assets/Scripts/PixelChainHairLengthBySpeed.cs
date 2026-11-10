using UnityEngine;

[DisallowMultipleComponent]
public class PixelChainHairLengthBySpeed : MonoBehaviour
{
    [Header("Motion Source")]
    [Tooltip("Optional. When empty, this component measures this transform velocity.")]
    [SerializeField] Transform motionSourceOverride;
    [SerializeField, Min(0.001f)] float maxMotionSpeed = 10f;

    [Header("Hair Targets")]
    [SerializeField] bool autoFindChildHairRigs = true;
    [SerializeField] bool includeInactiveChildren;
    [SerializeField] PixelChainHairRig[] hairRigs = new PixelChainHairRig[0];

    [Header("Length")]
    [SerializeField, Min(1)] int minPieceCount = 3;
    [SerializeField, Min(1)] int maxPieceCount = 8;
    [SerializeField] bool randomizeSpritesOnLengthChange;

    Vector3 previousMotionPosition;
    bool hasPreviousMotionPosition;

    public float CurrentSpeed { get; private set; }
    public int CurrentPieceCount { get; private set; }

    void Reset()
    {
        RefreshHairRigs();
    }

    void OnEnable()
    {
        if (autoFindChildHairRigs)
        {
            RefreshHairRigs();
        }

        previousMotionPosition = GetMotionSource().position;
        hasPreviousMotionPosition = true;
        ApplyLengthFromSpeed(0f);
    }

    void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        Transform motionSource = GetMotionSource();
        CurrentSpeed = hasPreviousMotionPosition
            ? (motionSource.position - previousMotionPosition).magnitude / deltaTime
            : 0f;

        ApplyLengthFromSpeed(CurrentSpeed);

        previousMotionPosition = motionSource.position;
        hasPreviousMotionPosition = true;
    }

    [ContextMenu("Refresh Hair Rigs")]
    public void RefreshHairRigs()
    {
        hairRigs = GetComponentsInChildren<PixelChainHairRig>(includeInactiveChildren);
    }

    public void SetPieceCountRange(int newMinPieceCount, int newMaxPieceCount)
    {
        minPieceCount = Mathf.Max(1, newMinPieceCount);
        maxPieceCount = Mathf.Max(minPieceCount, newMaxPieceCount);
        ApplyLengthFromSpeed(CurrentSpeed);
    }

    void ApplyLengthFromSpeed(float speed)
    {
        int targetPieceCount = GetPieceCountForSpeed(speed);
        CurrentPieceCount = targetPieceCount;

        if (hairRigs == null)
        {
            return;
        }

        for (int i = 0; i < hairRigs.Length; i++)
        {
            PixelChainHairRig hairRig = hairRigs[i];
            if (hairRig == null || hairRig.PieceCount == targetPieceCount)
            {
                continue;
            }

            hairRig.SetPieceCount(targetPieceCount, randomizeSpritesOnLengthChange);
        }
    }

    int GetPieceCountForSpeed(float speed)
    {
        int safeMinPieceCount = Mathf.Max(1, minPieceCount);
        int safeMaxPieceCount = Mathf.Max(safeMinPieceCount, maxPieceCount);
        float speedRatio = Mathf.Clamp01(speed / maxMotionSpeed);
        return Mathf.RoundToInt(Mathf.Lerp(safeMinPieceCount, safeMaxPieceCount, speedRatio));
    }

    Transform GetMotionSource()
    {
        return motionSourceOverride != null ? motionSourceOverride : transform;
    }

    void OnValidate()
    {
        maxMotionSpeed = Mathf.Max(0.001f, maxMotionSpeed);
        minPieceCount = Mathf.Max(1, minPieceCount);
        maxPieceCount = Mathf.Max(minPieceCount, maxPieceCount);
    }
}
