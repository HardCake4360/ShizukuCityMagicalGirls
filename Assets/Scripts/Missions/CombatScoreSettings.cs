using UnityEngine;

// Preserve D..S+ numeric values and their ten sprite/threshold indices.
public enum CombatStyleRank { None = -1, D = 0, DPlus, C, CPlus, B, BPlus, A, APlus, S, SPlus }

[CreateAssetMenu(fileName = "CombatScoreSettings", menuName = "Shizuku City/Missions/Combat Score Settings")]
public sealed class CombatScoreSettings : ScriptableObject
{
    public const int RankCount = 10;
    [Header("Defeat points")]
    [Min(0)] public int regularPoints = 100;
    [Min(0)] public int speedPoints = 150;
    [Min(0)] public int defensivePoints = 200;
    [Min(0)] public int bossPoints = 1000;

    [Header("Common bonuses only (no character-specific techniques)")]
    [Min(0f)] public float chainWindow = 3f;
    [Min(0)] public int chainStepPoints = 20;
    [Min(0)] public int maxChainBonus = 60;
    [Min(0)] public int multiKillPoints = 50;
    [Min(0f)] public float multiKillWindow = 0.3f;
    [Min(0)] public int flankPoints = 50;
    public bool useBonusRepetition = true;
    [Min(0f)] public float repetitionRecoverySeconds = 5f;
    [Range(0f, 1f)] public float repetitionStep = 0.2f;
    [Range(0f, 1f)] public float minimumBonusMultiplier = 0.6f;

    [Header("Movement (speed / character's normal maximum speed)")]
    [Range(0f, 1f)] public float movingThreshold = 0.35f;
    [Range(0f, 1f)] public float fastThreshold = 0.8f;
    [Min(1f)] public float movingMultiplier = 1.25f;
    [Min(1f)] public float fastMultiplier = 1.5f;
    [Min(0f)] public float movementGraceSeconds = 1.5f;

    [Header("Style gauge (independent of cumulative score)")]
    [Min(1f)] public float maxGauge = 1300f;
    [Tooltip("D, D+, C, C+, B, B+, A, A+, S, S+ (in this order).")]
    public float[] rankThresholds = { 0f, 100f, 220f, 350f, 500f, 650f, 800f, 950f, 1100f, 1250f };
    [Min(0f)] public float bossGaugeGainLimit = 200f;
    [Min(0f)] public float inactivityDelay = 3f;
    [Min(0f)] public float decayPerSecond = 40f;
    [Min(0f)] public float chargeGraceSeconds = 2f;
    [Range(0f, 1f)] public float hitGaugeLossRatio = 0.25f;

    public static string RankName(CombatStyleRank rank) => rank switch
    {
        CombatStyleRank.None => "—",
        CombatStyleRank.DPlus => "D+", CombatStyleRank.CPlus => "C+",
        CombatStyleRank.BPlus => "B+", CombatStyleRank.APlus => "A+",
        CombatStyleRank.SPlus => "S+",
        _ => rank.ToString()
    };

    public CombatStyleRank RankFor(float gauge)
    {
        if (gauge <= 0f || rankThresholds == null || rankThresholds.Length == 0 || gauge < rankThresholds[0])
            return CombatStyleRank.None;
        for (int i = Mathf.Min(rankThresholds.Length, RankCount) - 1; i >= 0; i--)
            if (gauge >= rankThresholds[i])
                return (CombatStyleRank)i;
        return CombatStyleRank.None;
    }

    public float MovementMultiplier(float ratio) => ratio >= fastThreshold ? fastMultiplier
        : ratio >= movingThreshold ? movingMultiplier : 1f;

    private void OnEnable() => EnsureRankThresholds();

    private void EnsureRankThresholds()
    {
        if (rankThresholds != null && rankThresholds.Length == 8)
        {
            // Preserve the eight existing rank thresholds when upgrading older tuning assets.
            float step = Mathf.Max(1f, rankThresholds[7] - rankThresholds[6]);
            System.Array.Resize(ref rankThresholds, RankCount);
            rankThresholds[8] = rankThresholds[7] + step;
            rankThresholds[9] = rankThresholds[8] + step;
            maxGauge = Mathf.Max(maxGauge, rankThresholds[9] + step / 3f);
        }
        else if (rankThresholds == null || rankThresholds.Length != RankCount)
            rankThresholds = new[] { 0f, 100f, 220f, 350f, 500f, 650f, 800f, 950f, 1100f, 1250f };
    }

    private void OnValidate()
    {
        regularPoints = Mathf.Max(0, regularPoints);
        speedPoints = Mathf.Max(0, speedPoints);
        defensivePoints = Mathf.Max(0, defensivePoints);
        bossPoints = Mathf.Max(0, bossPoints);
        chainWindow = Mathf.Max(0f, chainWindow);
        chainStepPoints = Mathf.Max(0, chainStepPoints);
        maxChainBonus = Mathf.Max(0, maxChainBonus);
        multiKillPoints = Mathf.Max(0, multiKillPoints);
        multiKillWindow = Mathf.Max(0f, multiKillWindow);
        flankPoints = Mathf.Max(0, flankPoints);
        repetitionRecoverySeconds = Mathf.Max(0f, repetitionRecoverySeconds);
        repetitionStep = Mathf.Clamp01(repetitionStep);
        minimumBonusMultiplier = Mathf.Clamp01(minimumBonusMultiplier);
        movingThreshold = Mathf.Clamp01(movingThreshold);
        fastThreshold = Mathf.Clamp(fastThreshold, movingThreshold, 1f);
        movingMultiplier = Mathf.Max(1f, movingMultiplier);
        fastMultiplier = Mathf.Max(movingMultiplier, fastMultiplier);
        movementGraceSeconds = Mathf.Max(0f, movementGraceSeconds);
        EnsureRankThresholds();
        maxGauge = Mathf.Max(RankCount - 1f, maxGauge);
        rankThresholds[0] = Mathf.Clamp(rankThresholds[0], 0f, maxGauge - (RankCount - 1));
        for (int i = 1; i < RankCount; i++)
            rankThresholds[i] = Mathf.Clamp(rankThresholds[i], rankThresholds[i - 1] + 1f, maxGauge - (RankCount - 1 - i));
        bossGaugeGainLimit = Mathf.Max(0f, bossGaugeGainLimit);
        inactivityDelay = Mathf.Max(0f, inactivityDelay);
        decayPerSecond = Mathf.Max(0f, decayPerSecond);
        chargeGraceSeconds = Mathf.Max(0f, chargeGraceSeconds);
        hitGaugeLossRatio = Mathf.Clamp01(hitGaugeLossRatio);
    }
}
