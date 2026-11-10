using UnityEngine;

[CreateAssetMenu(fileName = "Chapter", menuName = "Shizuku City/Missions/Chapter Definition")]
public sealed class ChapterDefinition : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] private int chapterNumber = 1;
    [SerializeField] private string displayName;
    [SerializeField] private bool initialChapter;
    [SerializeField] private MissionDefinition[] missions;

    [Header("Achievement Gate")]
    [SerializeField] private AchievementDefinition[] prerequisiteAchievements;
    [SerializeField, Min(1)] private int requiredAchievementCount = 1;
    [SerializeField] private AchievementDefinition bossAchievement;
    [SerializeField] private string bossDefeatedEventId;

    [Header("Next Chapter")]
    [SerializeField] private ChapterDefinition nextChapter;
    [SerializeField] private AchievementDefinition[] nextChapterInitialAchievements;

    public string Id => id;
    public int ChapterNumber => Mathf.Max(1, chapterNumber);
    public string DisplayName => displayName;
    public bool IsInitialChapter => initialChapter;
    public MissionDefinition[] Missions => missions;
    public AchievementDefinition[] PrerequisiteAchievements => prerequisiteAchievements;
    public int RequiredAchievementCount => Mathf.Max(1, requiredAchievementCount);
    public AchievementDefinition BossAchievement => bossAchievement;
    public string BossDefeatedEventId => bossDefeatedEventId;
    public ChapterDefinition NextChapter => nextChapter;
    public AchievementDefinition[] NextChapterInitialAchievements => nextChapterInitialAchievements;

    private void OnValidate()
    {
        id = id?.Trim();
        bossDefeatedEventId = bossDefeatedEventId?.Trim();
        chapterNumber = Mathf.Max(1, chapterNumber);
        requiredAchievementCount = Mathf.Max(1, requiredAchievementCount);
    }
}
