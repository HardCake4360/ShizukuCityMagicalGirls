using System;
using System.IO;
using UnityEngine;

public enum BossDefeatProgressionResult
{
    GateInactive = 0,
    AchievementCompleted = 1,
    ChapterAdvanced = 2,
    AlreadyCompleted = 3,
    InvalidChapter = 4
}

[Serializable]
internal sealed class ChapterProgressionSaveData
{
    public int version = 1;
    public string currentChapterId;
}

[DefaultExecutionOrder(-925)]
public sealed class ChapterProgressionManager : MonoBehaviour
{
    private const string ResourcePath = "Missions/Chapters";
    private const string SaveFileName = "chapter_progress.json";

    private static ChapterProgressionManager instance;
    private ChapterDefinition[] chapters = Array.Empty<ChapterDefinition>();

    public static ChapterProgressionManager Instance
    {
        get
        {
            EnsureInstance();
            return instance;
        }
    }

    public ChapterDefinition CurrentChapter { get; private set; }
    public event Action<ChapterDefinition> CurrentChapterChanged;

    private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (instance != null)
        {
            return;
        }

        instance = FindFirstObjectByType<ChapterProgressionManager>();
        if (instance == null)
        {
            instance = new GameObject("Chapter Progression Manager")
                .AddComponent<ChapterProgressionManager>();
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        chapters = Resources.LoadAll<ChapterDefinition>(ResourcePath);
        CurrentChapter = ResolveSavedChapter() ?? FindInitialChapter();
        AchievementManager.Instance.ProgressChanged += HandleAchievementProgressChanged;
        EvaluateBossAchievementGate();
    }

    private void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        AchievementManager manager = FindFirstObjectByType<AchievementManager>();
        if (manager != null)
        {
            manager.ProgressChanged -= HandleAchievementProgressChanged;
        }
    }

    public bool IsCurrentChapter(ChapterDefinition chapter)
    {
        return chapter != null && CurrentChapter == chapter;
    }

    public BossDefeatProgressionResult HandleBossDefeated(ChapterDefinition chapter)
    {
        if (chapter == null || chapter != CurrentChapter || chapter.BossAchievement == null)
        {
            return BossDefeatProgressionResult.InvalidChapter;
        }

        AchievementManager manager = AchievementManager.Instance;
        string achievementId = chapter.BossAchievement.Id;
        if (!manager.TryGetProgress(achievementId, out AchievementProgress progress))
        {
            return BossDefeatProgressionResult.InvalidChapter;
        }

        if (progress.State == AchievementState.Inactive)
        {
            return BossDefeatProgressionResult.GateInactive;
        }

        if (progress.State == AchievementState.Completed)
        {
            return BossDefeatProgressionResult.AlreadyCompleted;
        }

        if (progress.State == AchievementState.Active)
        {
            AchievementDefinition definition = progress.Definition;
            string customId = definition.EventType == AchievementEventType.Custom
                ? chapter.BossDefeatedEventId
                : null;
            manager.ReportEvent(definition.EventType, definition.TargetProgress, customId);
        }

        if (!manager.TryComplete(achievementId))
        {
            return BossDefeatProgressionResult.GateInactive;
        }

        if (chapter.NextChapter == null)
        {
            return BossDefeatProgressionResult.AchievementCompleted;
        }

        AdvanceTo(chapter.NextChapter, chapter.NextChapterInitialAchievements);
        return BossDefeatProgressionResult.ChapterAdvanced;
    }

    public void EvaluateBossAchievementGate()
    {
        if (CurrentChapter == null || CurrentChapter.BossAchievement == null)
        {
            return;
        }

        AchievementDefinition[] prerequisites = CurrentChapter.PrerequisiteAchievements;
        int completedCount = 0;
        if (prerequisites != null)
        {
            for (int i = 0; i < prerequisites.Length; i++)
            {
                AchievementDefinition definition = prerequisites[i];
                if (definition != null
                    && AchievementManager.Instance.TryGetProgress(definition.Id, out AchievementProgress progress)
                    && progress.State == AchievementState.Completed)
                {
                    completedCount++;
                }
            }
        }

        if (completedCount >= CurrentChapter.RequiredAchievementCount)
        {
            AchievementManager.Instance.Activate(CurrentChapter.BossAchievement.Id);
        }
    }

    private void HandleAchievementProgressChanged(AchievementProgress progress)
    {
        if (progress != null && progress.State == AchievementState.Completed)
        {
            EvaluateBossAchievementGate();
        }
    }

    private void AdvanceTo(
        ChapterDefinition nextChapter,
        AchievementDefinition[] initialAchievements
    )
    {
        if (nextChapter == null || CurrentChapter == nextChapter)
        {
            return;
        }

        CurrentChapter = nextChapter;
        if (initialAchievements != null)
        {
            for (int i = 0; i < initialAchievements.Length; i++)
            {
                AchievementDefinition achievement = initialAchievements[i];
                if (achievement != null)
                {
                    AchievementManager.Instance.Activate(achievement.Id);
                }
            }
        }

        Save();
        EvaluateBossAchievementGate();
        CurrentChapterChanged?.Invoke(CurrentChapter);
    }

    private ChapterDefinition ResolveSavedChapter()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                return null;
            }

            ChapterProgressionSaveData data = JsonUtility.FromJson<ChapterProgressionSaveData>(
                File.ReadAllText(SavePath)
            );
            if (data == null || string.IsNullOrWhiteSpace(data.currentChapterId))
            {
                return null;
            }

            for (int i = 0; i < chapters.Length; i++)
            {
                if (chapters[i] != null && chapters[i].Id == data.currentChapterId)
                {
                    return chapters[i];
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to load chapter progression: {exception.Message}");
        }

        return null;
    }

    private ChapterDefinition FindInitialChapter()
    {
        ChapterDefinition lowest = null;
        for (int i = 0; i < chapters.Length; i++)
        {
            ChapterDefinition chapter = chapters[i];
            if (chapter == null)
            {
                continue;
            }

            if (chapter.IsInitialChapter)
            {
                return chapter;
            }

            if (lowest == null || chapter.ChapterNumber < lowest.ChapterNumber)
            {
                lowest = chapter;
            }
        }

        return lowest;
    }

    private void Save()
    {
        try
        {
            ChapterProgressionSaveData data = new ChapterProgressionSaveData
            {
                currentChapterId = CurrentChapter != null ? CurrentChapter.Id : string.Empty
            };
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to save chapter progression: {exception}");
        }
    }
}
