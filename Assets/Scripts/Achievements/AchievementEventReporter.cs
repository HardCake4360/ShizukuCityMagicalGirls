using UnityEngine;

public sealed class AchievementEventReporter : MonoBehaviour
{
    [SerializeField] private AchievementEventType eventType;
    [SerializeField] private string customEventId;
    [SerializeField, Min(1)] private int amount = 1;
    [SerializeField] private bool reportOnlyOnce;

    private bool hasReported;

    public void Report()
    {
        if (reportOnlyOnce && hasReported)
        {
            return;
        }

        AchievementManager.Instance.ReportEvent(eventType, Mathf.Max(1, amount), customEventId);
        hasReported = true;
    }

    public void ReportEnemyDefeated()
    {
        AchievementManager.Instance.ReportEvent(AchievementEventType.EnemyDefeated);
    }

    public void ReportStageCleared()
    {
        AchievementManager.Instance.ReportEvent(AchievementEventType.StageCleared);
    }
}
