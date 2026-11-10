using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public sealed class AchievementManager : MonoBehaviour
{
    private static AchievementManager instance;
    private static bool isShuttingDown;

    public static bool IsShuttingDown => isShuttingDown;

    /// <summary>Read-only lookup for queries and teardown callbacks. Never creates a GameObject.</summary>
    public static bool TryGetExisting(out AchievementManager manager)
    {
        manager = instance;
        return manager != null;
    }

    private readonly Dictionary<string, AchievementProgress> progressById = new(StringComparer.Ordinal);
    private IAchievementSaveStore saveStore;

    public static AchievementManager Instance
    {
        get
        {
            EnsureInstance();
            return instance;
        }
    }

    public event Action<AchievementProgress> ProgressChanged;

    public IEnumerable<AchievementProgress> AllAchievements => progressById.Values;
    public int CompletedCount
    {
        get
        {
            int count = 0;
            foreach (AchievementProgress progress in progressById.Values)
                if (progress.State == AchievementState.Completed)
                    count++;
            return count;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        isShuttingDown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (isShuttingDown)
            return;
        if (instance != null)
        {
            return;
        }

        instance = FindFirstObjectByType<AchievementManager>();
        if (instance != null)
        {
            return;
        }

        GameObject managerObject = new GameObject("Achievement Manager");
        instance = managerObject.AddComponent<AchievementManager>();
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
        saveStore = new JsonAchievementSaveStore();
        InitializeProgress(Resources.LoadAll<AchievementDefinition>("Achievements"), saveStore.Load());
        ResumePendingCompletionActions();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Save();
        }
    }

    private void OnApplicationQuit()
    {
        if (instance != this)
            return;
        isShuttingDown = true;
        Save();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public bool TryGetProgress(string achievementId, out AchievementProgress progress)
    {
        return progressById.TryGetValue(achievementId, out progress);
    }

    public bool Activate(string achievementId)
    {
        if (!TryGetProgress(achievementId, out AchievementProgress progress)
            || progress.State != AchievementState.Inactive)
        {
            return false;
        }

        progress.State = AchievementState.Active;
        NotifyAndSave(progress);
        return true;
    }

    public void ReportEvent(AchievementEventType eventType, int amount = 1, string customEventId = null)
    {
        if (amount <= 0)
        {
            return;
        }

        List<AchievementProgress> changedAchievements = null;
        foreach (AchievementProgress progress in progressById.Values)
        {
            if (progress.State != AchievementState.Active
                || !progress.Definition.Matches(eventType, customEventId))
            {
                continue;
            }

            progress.CurrentProgress = Mathf.Min(
                progress.CurrentProgress + amount,
                progress.Definition.TargetProgress
            );

            if (progress.CurrentProgress >= progress.Definition.TargetProgress)
            {
                progress.State = AchievementState.ConditionCleared;
            }

            changedAchievements ??= new List<AchievementProgress>();
            changedAchievements.Add(progress);
        }

        if (changedAchievements == null)
        {
            return;
        }

        Save();
        for (int i = 0; i < changedAchievements.Count; i++)
        {
            ProgressChanged?.Invoke(changedAchievements[i]);
        }
    }

    public bool TryComplete(string achievementId)
    {
        if (!TryGetProgress(achievementId, out AchievementProgress progress)
            || progress.State != AchievementState.ConditionCleared)
        {
            return false;
        }

        progress.State = AchievementState.Completed;
        progress.CompletionActionsApplied = false;
        Save();
        ProgressChanged?.Invoke(progress);
        ExecuteCompletionActions(progress);
        return true;
    }

    public void Save()
    {
        saveStore?.Save(CreateSaveData());
    }

    internal void InitializeProgress(
        IReadOnlyList<AchievementDefinition> definitions,
        AchievementSaveData saveData
    )
    {
        progressById.Clear();
        Dictionary<string, AchievementProgressSaveData> savedById = BuildSavedLookup(saveData);

        for (int i = 0; i < definitions.Count; i++)
        {
            AchievementDefinition definition = definitions[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                Debug.LogWarning("An achievement definition has no stable ID and was ignored.", definition);
                continue;
            }

            if (progressById.ContainsKey(definition.Id))
            {
                Debug.LogError($"Duplicate achievement ID '{definition.Id}' was ignored.", definition);
                continue;
            }

            AchievementState state = definition.InitiallyActive
                ? AchievementState.Active
                : AchievementState.Inactive;
            int currentProgress = 0;
            bool completionActionsApplied = false;

            if (savedById.TryGetValue(definition.Id, out AchievementProgressSaveData savedProgress))
            {
                state = ParseState(savedProgress.state, state);
                currentProgress = savedProgress.currentProgress;
                completionActionsApplied = savedProgress.completionActionsApplied;
            }

            if (state == AchievementState.ConditionCleared || state == AchievementState.Completed)
            {
                currentProgress = definition.TargetProgress;
            }

            progressById.Add(
                definition.Id,
                new AchievementProgress(
                    definition,
                    state,
                    currentProgress,
                    completionActionsApplied
                )
            );
        }
    }

    internal AchievementSaveData CreateSaveData()
    {
        AchievementSaveData saveData = new AchievementSaveData();
        foreach (AchievementProgress progress in progressById.Values)
        {
            saveData.achievements.Add(new AchievementProgressSaveData
            {
                id = progress.Definition.Id,
                state = (int)progress.State,
                currentProgress = progress.CurrentProgress,
                completionActionsApplied = progress.CompletionActionsApplied
            });
        }

        return saveData;
    }

    private void NotifyAndSave(AchievementProgress progress)
    {
        Save();
        ProgressChanged?.Invoke(progress);
    }

    private void ResumePendingCompletionActions()
    {
        foreach (AchievementProgress progress in progressById.Values)
        {
            if (progress.State == AchievementState.Completed && !progress.CompletionActionsApplied)
            {
                ExecuteCompletionActions(progress);
            }
        }
    }

    private void ExecuteCompletionActions(AchievementProgress progress)
    {
        if (progress.CompletionActionsApplied)
        {
            return;
        }

        AchievementCompletionAction[] actions = progress.Definition.CompletionActions;
        try
        {
            if (actions != null)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    actions[i]?.Execute(this, progress);
                }
            }

            progress.CompletionActionsApplied = true;
            Save();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Completion actions for achievement '{progress.Definition.Id}' failed and will be retried: {exception}",
                progress.Definition
            );
        }
    }

    private static Dictionary<string, AchievementProgressSaveData> BuildSavedLookup(
        AchievementSaveData saveData
    )
    {
        Dictionary<string, AchievementProgressSaveData> result = new(StringComparer.Ordinal);
        if (saveData?.achievements == null)
        {
            return result;
        }

        for (int i = 0; i < saveData.achievements.Count; i++)
        {
            AchievementProgressSaveData savedProgress = saveData.achievements[i];
            if (savedProgress != null && !string.IsNullOrWhiteSpace(savedProgress.id))
            {
                result[savedProgress.id] = savedProgress;
            }
        }

        return result;
    }

    private static AchievementState ParseState(int value, AchievementState fallback)
    {
        return Enum.IsDefined(typeof(AchievementState), value)
            ? (AchievementState)value
            : fallback;
    }
}
